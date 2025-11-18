using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.Battle;
using Game.Match.Units;
using Game.Match.Graveyard;
using Game.Match.Log;
using Game.Core;
using Game.Match.Status;   // GroundedStatus

namespace Game.Match.Traps
{
    /// <summary>
    /// Tracks armed traps across CardPhase and BattleStage and resolves them
    /// when their trigger conditions are met.
    ///
    /// Place this on a GameObject that persists across phases (e.g. a Match root / Boot scene)
    /// so that traps set in CardPhase still exist when you enter BattleStage.
    /// </summary>
    public class TrapService : MonoBehaviour
    {
        public static TrapService Instance { get; private set; }

        class ArmedTrap
        {
            public CardSO card;
            public int ownerId;
            public bool consumed;
        }

        readonly List<ArmedTrap> _armedTraps = new List<ArmedTrap>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Called when a trap card is successfully set in CardPhase.
        /// </summary>
        public void RegisterTrap(CardSO card, int ownerId)
        {
            if (card == null)
                return;

            if (card.type != CardType.Trap)
            {
                Debug.LogWarning($"[TrapService] Tried to register non-trap card {card.cardName} as a trap.");
                return;
            }

            _armedTraps.Add(new ArmedTrap
            {
                card = card,
                ownerId = ownerId,
                consumed = false
            });

            // Logging of "Set X as a trap" is handled by DraggableCard, not here.
        }

        /// <summary>
        /// Called from the tower damage code whenever a tower's HP changes.
        /// Only the "TowerBelowHalf_DamageRandomEnemyUnit" trap uses tower HP.
        /// GroundEnemyFlierForTime NO LONGER uses tower HP at all.
        /// </summary>
        public void NotifyTowerHpChanged(BattleTower tower, int oldHp, int newHp, int maxHp)
        {
            if (tower == null)
                return;
            if (maxHp <= 0)
                return;
            if (oldHp <= 0) // already dead, ignore
                return;
            if (newHp >= oldHp) // healed or unchanged, not damage
                return;

            float oldFrac = (float)oldHp / maxHp;
            float newFrac = (float)Mathf.Max(newHp, 0) / maxHp;

            var toTrigger = new List<ArmedTrap>();

            foreach (var trap in _armedTraps)
            {
                if (trap.consumed || trap.card == null)
                    continue;

                // Trap belongs to this tower's owner only.
                if (trap.ownerId != tower.ownerId)
                    continue;

                // Only the "tower below half" trap uses this path.
                if (trap.card.trapEffect != TrapEffectKind.TowerBelowHalf_DamageRandomEnemyUnit)
                    continue;

                float threshold = Mathf.Clamp01(trap.card.trapHpThresholdFraction);
                // Trigger when we cross from above threshold to below/equal.
                if (oldFrac > threshold && newFrac <= threshold)
                {
                    toTrigger.Add(trap);
                }
            }

            foreach (var trap in toTrigger)
            {
                ResolveTowerBelowHalfTrap(trap, tower, maxHp);
            }

            // Clean up consumed traps.
            _armedTraps.RemoveAll(t => t.consumed || t.card == null);
        }

        private void ResolveTowerBelowHalfTrap(ArmedTrap trap, BattleTower tower, int maxHp)
        {
            if (trap == null || trap.card == null)
                return;

            trap.consumed = true;

            var resolver = CombatResolver.Instance;
            if (resolver == null)
            {
                Debug.LogWarning("[TrapService] No CombatResolver found when resolving trap.");
                return;
            }

            // Choose enemy list based on trap owner.
            var enemyUnits = (trap.ownerId == 0)
                ? resolver.RemoteUnits
                : resolver.LocalUnits;

            var candidates = new List<UnitAgent>();
            if (enemyUnits != null)
            {
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var unit = enemyUnits[i];
                    if (unit == null)
                        continue;

                    var rt = unit.GetComponent<UnitRuntime>();
                    if (rt == null || rt.health <= 0)
                        continue;

                    candidates.Add(unit);
                }
            }

            var logger = ActionLogService.Instance;
            string trapName = string.IsNullOrEmpty(trap.card.cardName)
                ? trap.card.name
                : trap.card.cardName;

            if (candidates.Count == 0)
            {
                if (logger != null)
                {
                    logger.SystemCard(
                        $"Trap {trapName} triggered when the tower dropped below 50% HP, " +
                        $"but there were no enemy units to hit.",
                        trap.card.artSprite,
                        trap.card
                    );
                }

                // Still send the trap to graveyard – it fired, even if it fizzled.
                var gyNone = GraveyardService.Instance;
                if (gyNone != null)
                    gyNone.Add(trap.ownerId, trap.card);

                return;
            }

            int idx = Random.Range(0, candidates.Count);
            var target = candidates[idx];

            int dmg = Mathf.Max(0, trap.card.trapDamageAmount);

            if (dmg > 0)
            {
                // Use CombatResolver's kill helper so UnitDied event + list cleanup happen.
                resolver.KillUnit(target, dmg);
            }

            if (logger != null)
            {
                string targetName;
                var targetRuntime = target != null ? target.GetComponent<UnitRuntime>() : null;
                if (targetRuntime != null && !string.IsNullOrEmpty(targetRuntime.displayName))
                    targetName = targetRuntime.displayName;
                else
                    targetName = target != null ? target.gameObject.name : "unknown target";

                logger.SystemCard(
                    $"Trap {trapName} triggered: dealt {dmg} damage to {targetName} " +
                    $"when the tower dropped below 50% HP.",
                    trap.card.artSprite,
                    trap.card
                );
            }

            // Finally, send the trap card to the owner’s graveyard.
            var gy = GraveyardService.Instance;
            if (gy != null)
                gy.Add(trap.ownerId, trap.card);
        }

        /// <summary>
        /// NEW PUBLIC API:
        /// "If the opponent controls any flying unit, then the trap triggers."
        ///
        /// Call this from your Battle start logic (and/or other events)
        /// once per owner. Example:
        ///
        ///     TrapService.Instance?.TryTriggerGroundEnemyFlierTrapsForOwner(0);
        ///     TrapService.Instance?.TryTriggerGroundEnemyFlierTrapsForOwner(1);
        ///
        /// This evaluates:
        ///  - all armed GroundEnemyFlierForTime traps for that owner
        ///  - current enemy flying units
        /// If there is at least one flying enemy unit, one is grounded
        /// for trap.card.trapGroundDurationSeconds and the trap is consumed.
        /// If there are no flying enemies, the trap STAYS armed (not consumed).
        /// </summary>
        public void TryTriggerGroundEnemyFlierTrapsForOwner(int ownerId)
        {
            var resolver = CombatResolver.Instance;
            if (resolver == null)
            {
                Debug.LogWarning("[TrapService] No CombatResolver found when trying to trigger GroundEnemyFlierForTime traps.");
                return;
            }

            // Enemy units relative to the trap owner (likely IReadOnlyList<UnitAgent>).
            var enemyUnitsReadOnly = (ownerId == 0)
                ? resolver.RemoteUnits
                : resolver.LocalUnits;

            // Convert to a mutable List<UnitAgent> so we can pass it to TryResolveOneGroundEnemyFlierTrap
            List<UnitAgent> enemyUnits = null;
            if (enemyUnitsReadOnly != null)
            {
                enemyUnits = new List<UnitAgent>(enemyUnitsReadOnly);
            }

            if (enemyUnits == null || enemyUnits.Count == 0)
            {
                // No enemies → no triggers, keep traps armed for later.
                return;
            }

            // Collect all relevant traps for this owner.
            var groundTraps = new List<ArmedTrap>();
            foreach (var trap in _armedTraps)
            {
                if (trap.consumed || trap.card == null)
                    continue;

                if (trap.ownerId != ownerId)
                    continue;

                if (trap.card.trapEffect != TrapEffectKind.GroundEnemyFlierForTime)
                    continue;

                groundTraps.Add(trap);
            }

            if (groundTraps.Count == 0)
                return;

            foreach (var trap in groundTraps)
            {
                TryResolveOneGroundEnemyFlierTrap(trap, enemyUnits);
            }

            // Remove only traps that were actually consumed.
            _armedTraps.RemoveAll(t => t.consumed || t.card == null);
        }

        /// <summary>
        /// Internal helper for the GroundEnemyFlierForTime trap.
        /// If the opponent controls any flying unit, ground one at random
        /// for trap.card.trapGroundDurationSeconds and consume the trap.
        /// If not, do nothing and leave the trap armed.
        /// </summary>
        private void TryResolveOneGroundEnemyFlierTrap(ArmedTrap trap, List<UnitAgent> enemyUnits)
        {
            if (trap == null || trap.card == null)
                return;

            var logger = ActionLogService.Instance;
            string trapName = string.IsNullOrEmpty(trap.card.cardName)
                ? trap.card.name
                : trap.card.cardName;

            var candidates = new List<UnitAgent>();

            if (enemyUnits != null)
            {
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var unit = enemyUnits[i];
                    if (unit == null)
                        continue;

                    var rt = unit.GetComponent<UnitRuntime>();
                    if (rt == null || rt.health <= 0)
                        continue;

                    // Only consider units that are currently flying (status-aware).
                    if (rt.IsFlying)
                        candidates.Add(unit);
                }
            }

            if (candidates.Count == 0)
            {
                // Condition not met: opponent controls no flying units.
                // Do NOT consume the trap; it can still trigger later
                // if you call TryTriggerGroundEnemyFlierTrapsForOwner again.
                return;
            }

            // At least one flying enemy unit exists → trap condition is met → trigger.
            int idx = Random.Range(0, candidates.Count);
            var target = candidates[idx];
            var targetRuntime = target.GetComponent<UnitRuntime>();

            // Use duration from CardSO, with a small minimum safeguard.
            float duration = Mathf.Max(0.01f, trap.card.trapGroundDurationSeconds);
            if (targetRuntime != null && targetRuntime.StatusController != null)
            {
                targetRuntime.StatusController.AddStatus(new GroundedStatus(duration));
            }

            if (logger != null)
            {
                string targetName;
                if (targetRuntime != null && !string.IsNullOrEmpty(targetRuntime.displayName))
                    targetName = targetRuntime.displayName;
                else
                    targetName = target != null ? target.gameObject.name : "unknown target";

                logger.SystemCard(
                    $"Trap {trapName} triggered: grounded {targetName} for {duration:0.#} seconds " +
                    $"because your opponent controls a flying unit.",
                    trap.card.artSprite,
                    trap.card
                );
            }

            // Mark trap consumed and send it to graveyard.
            trap.consumed = true;

            var gy = GraveyardService.Instance;
            if (gy != null)
                gy.Add(trap.ownerId, trap.card);
        }
    }
}
