using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.Battle;
using Game.Match.Units;
using Game.Match.Graveyard;
using Game.Match.Log;
using Game.Core;

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

            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string name = string.IsNullOrEmpty(card.cardName) ? card.name : card.cardName;
                logger.SystemCard($"Set trap {name}. It will trigger when its condition is met.");
            }
        }

        /// <summary>
        /// Called from the tower damage code whenever a tower's HP changes.
        /// maxHp is passed explicitly so TrapService doesn't depend on BattleTower internals.
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

            // Collect which traps to trigger, then resolve them.
            var toTrigger = new List<ArmedTrap>();

            foreach (var trap in _armedTraps)
            {
                if (trap.consumed || trap.card == null)
                    continue;

                if (trap.ownerId != tower.ownerId)
                    continue;

                if (trap.card.trapEffect != TrapEffectKind.TowerBelowHalf_DamageRandomEnemyUnit)
                    continue;

                float threshold = Mathf.Clamp01(trap.card.trapHpThresholdFraction);
                // We only want "crossing the threshold" from above to below.
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
                        $"but there were no enemy units to hit."
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
                // ✅ Use CombatResolver's kill helper so UnitDied event + list cleanup happen.
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
                    $"when the tower dropped below 50% HP."
                );
            }

            // Finally, send the trap card to the owner’s graveyard.
            var gy = GraveyardService.Instance;
            if (gy != null)
                gy.Add(trap.ownerId, trap.card);
        }
    }
}
