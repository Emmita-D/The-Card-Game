using Game.Match.Status;
using Game.Match.Units;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Game.Core;
using Game.Match.Cards;


namespace Game.Match.Battle
{
    /// <summary>
    /// Central brain for combat: tracks units and towers, picks targets, and applies damage.
    /// Uses collider-based range checks so units stop at towers instead of walking through them.
    /// </summary>
    public class CombatResolver : MonoBehaviour
    {
        public static CombatResolver Instance { get; private set; }

        [Header("Tuning")]
        [Tooltip("Global attack interval for units (seconds).")]
        public float unitAttackIntervalSeconds = 1.0f;

        [Tooltip("Global attack interval for towers (seconds).")]
        public float towerAttackIntervalSeconds = 1.5f;

        [Tooltip("Default melee range if UnitRuntime.rangeMeters is 0.")]
        public float defaultMeleeRangeMeters = 1.5f;

        public event System.Action<UnitAgent> UnitRegistered;
        public event System.Action<UnitAgent> UnitDied;

        /// <summary>
        /// Fired when one side has no towers left.
        /// Parameter = loser ownerId (0 or 1).
        /// </summary>
        public System.Action<int> OnSideDefeated;

        private readonly List<UnitAgent> _localUnits = new();
        private readonly List<UnitAgent> _remoteUnits = new();
        private readonly List<BattleTower> _localTowers = new();
        private readonly List<BattleTower> _remoteTowers = new();

        // Timing accumulators (you might extend this later if you do fixed-step combat).
        private float _lastTickTime;
        private float _accumulatedUnitTime;
        private float _accumulatedTowerTime;

        // Expose read-only views so BattleSceneController can snapshot survivors.
        public IReadOnlyList<UnitAgent> LocalUnits => _localUnits;
        public IReadOnlyList<UnitAgent> RemoteUnits => _remoteUnits;

        private void Awake()
        {
            // If there is already a CombatResolver (typically from a previous,
            // now-hidden Battle scene), destroy THAT one and let this one be
            // the new global instance.
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
        }

        public void RegisterUnit(UnitAgent unit)
        {
            if (unit == null) return;

            if (unit.ownerId == 0)
            {
                if (!_localUnits.Contains(unit))
                    _localUnits.Add(unit);
            }
            else
            {
                if (!_remoteUnits.Contains(unit))
                    _remoteUnits.Add(unit);
            }

            UnitRegistered?.Invoke(unit);
        }

        public void RegisterTowers(BattleTower[] local, BattleTower[] remote)
        {
            _localTowers.Clear();
            _remoteTowers.Clear();

            if (local != null)
            {
                foreach (var t in local)
                {
                    if (t != null && !_localTowers.Contains(t))
                        _localTowers.Add(t);
                }
            }

            if (remote != null)
            {
                foreach (var t in remote)
                {
                    if (t != null && !_remoteTowers.Contains(t))
                        _remoteTowers.Add(t);
                }
            }
        }

        private void Update()
        {
            // Units try to attack nearest valid enemy (unit first, then tower).
            TickUnits(_localUnits, _remoteUnits, _remoteTowers);
            TickUnits(_remoteUnits, _localUnits, _localTowers);

            // Towers fire only when their side has no friendly units alive (your current rule).
            TickTowers(_localTowers, _remoteUnits, _remoteTowers, _localUnits);
            TickTowers(_remoteTowers, _localUnits, _localTowers, _remoteUnits);
        }

        private void TickUnits(List<UnitAgent> attackers, List<UnitAgent> enemyUnits, List<BattleTower> enemyTowers)
        {
            for (int i = 0; i < attackers.Count; i++)
            {
                var unit = attackers[i];
                if (unit == null) continue;

                var runtime = unit.GetComponent<UnitRuntime>();
                if (runtime == null || runtime.GetFinalHealth() <= 0) continue;

                // If stunned, this unit cannot move or attack this frame.
                if (runtime.IsStunned)
                {
                    unit.movementLocked = true;
                    continue;
                }

                float attackRange = runtime.rangeMeters > 0 ? runtime.rangeMeters : defaultMeleeRangeMeters;
                float rangeSqr = attackRange * attackRange;

                // Assume the unit can move unless we find a target in range this frame.
                bool lockMovement = false;

                Vector3 pos = unit.transform.position;
                float bestDistSqr = float.MaxValue;
                UnitAgent bestEnemyUnit = null;
                BattleTower bestEnemyTower = null;

                // 1) Prefer enemy units
                for (int j = 0; j < enemyUnits.Count; j++)
                {
                    var enemy = enemyUnits[j];
                    if (enemy == null) continue;

                    var enemyRt = enemy.GetComponent<UnitRuntime>();
                    if (enemyRt == null || enemyRt.GetFinalHealth() <= 0) continue;

                    // Respect combat rules (movement type, attack mode, per-card nerfs)
                    if (!CombatRules.CanUnitAttackUnit(unit, enemy))
                        continue;

                    float distSqr = SqrDistanceToAgent(enemy, pos);
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestEnemyUnit = enemy;
                        bestEnemyTower = null;
                    }
                }
                // 2) If no enemy units, consider towers
                if (bestEnemyUnit == null)
                {
                    for (int j = 0; j < enemyTowers.Count; j++)
                    {
                        var tower = enemyTowers[j];
                        if (tower == null || tower.currentHp <= 0) continue;

                        // Respect combat rules (per-card tower permissions)
                        if (!CombatRules.CanUnitAttackTower(unit, tower))
                            continue;

                        float distSqr = SqrDistanceToTower(tower, pos);
                        if (distSqr < bestDistSqr)
                        {
                            bestDistSqr = distSqr;
                            bestEnemyUnit = null;
                            bestEnemyTower = tower;
                        }
                    }
                }

                // Nothing to attack
                if (bestEnemyUnit == null && bestEnemyTower == null)
                {
                    unit.movementLocked = false;
                    continue;
                }

                // Not in range yet -> keep moving
                if (bestDistSqr > rangeSqr)
                {
                    unit.movementLocked = false;
                    continue;
                }

                // In range -> stop and swing on cooldown
                lockMovement = true;

                if (Time.time >= unit.nextAttackTime)
                {
                    // Base damage from attack stat (after ATK buffs).
                    float baseDamage = Mathf.Max(1, runtime.GetFinalAttack());

                    // Start with the attacker's outgoing damage multiplier (Savage, buffs, etc.).
                    float totalMultiplier = runtime.GetDamageDealtMultiplier();

                    // If we're hitting an enemy unit, also factor in:
                    //  - realm-based permanent bonus
                    //  - defender's damage-taken multiplier (Vulnerability, Fear, etc.)
                    UnitRuntime enemyRuntime = null;
                    if (bestEnemyUnit != null)
                    {
                        enemyRuntime = bestEnemyUnit.GetComponent<UnitRuntime>();
                        if (enemyRuntime != null)
                        {
                            // Realm-based permanent bonus: Empyrean vs Infernum
                            if (runtime.realm != enemyRuntime.realm &&
                                runtime.realmBonusVsOpposingMultiplier > 0f)
                            {
                                totalMultiplier *= runtime.realmBonusVsOpposingMultiplier;
                            }

                            // Status-based vulnerability / other effects on the defender
                            totalMultiplier *= enemyRuntime.GetDamageTakenMultiplier();
                        }
                    }

                    // Safety: keep multiplier sane.
                    if (totalMultiplier <= 0f)
                        totalMultiplier = 1f;

                    int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * totalMultiplier));

                    if (bestEnemyUnit != null)
                    {
                        bool willKill = false;

                        if (enemyRuntime != null)
                        {
                            int defenderFinalHp = enemyRuntime.GetFinalHealth();
                            if (damage >= defenderFinalHp)
                            {
                                willKill = true;
                            }
                        }

                        // --- On-kill: award Savage stacks to the attacker, if configured on the card ---
                        if (willKill && runtime.StatusController != null)
                        {
                            var attackerCard = unit.sourceCard;
                            if (attackerCard != null && attackerCard.savageStacksOnKill > 0)
                            {
                                runtime.StatusController.AddSavageStacks(attackerCard.savageStacksOnKill);

                                int stacks = runtime.StatusController.GetSavageStacks();
                                float dmgMultAfter = runtime.GetDamageDealtMultiplier();
                                UnityEngine.Debug.Log(
                                    $"[Savage] {runtime.displayName} gained {attackerCard.savageStacksOnKill} Savage on kill " +
                                    $"(total={stacks}, dmgMult={dmgMultAfter:F2})"
                                );
                            }
                            else
                            {
                                UnityEngine.Debug.Log(
                                    $"[Savage] {runtime.displayName} landed a lethal hit but attackerCard is null " +
                                    $"or savageStacksOnKill == 0."
                                );
                            }
                        }
                        // --- END on-kill logic ---

                        // --- Savage 5+ stun on NON-lethal hits (unit vs unit only) ---
                        if (!willKill &&
                            runtime.StatusController != null &&
                            enemyRuntime != null &&
                            enemyRuntime.StatusController != null)
                        {
                            if (runtime.StatusController.TryProcSavageStun())
                            {
                                // Apply a 2-second stun to the defender.
                                enemyRuntime.StatusController.AddStatus(new StunStatus(2f));

                                UnityEngine.Debug.Log(
                                    $"[Savage] {runtime.displayName} triggered Savage 5+ stun on {enemyRuntime.displayName}."
                                );
                            }
                        }
                        // --- END Savage 5+ stun logic ---

                        // Use shared kill helper so UnitDied + list cleanup are consistent.
                        KillUnit(bestEnemyUnit, damage);
                    }
                    else // tower
                    {
                        bestEnemyTower.ApplyDamage(damage);
                        if (bestEnemyTower.currentHp <= 0)
                        {
                            bestEnemyTower.currentHp = 0;
                            OnTowerDestroyed(bestEnemyTower);
                        }
                    }

                    // Statuses that care about number of attacks get notified here.
                    if (runtime.StatusController != null)
                    {
                        runtime.StatusController.NotifyOwnerAttack(runtime);
                    }

                    unit.nextAttackTime = Time.time + unitAttackIntervalSeconds;
                }

                unit.movementLocked = lockMovement;
            }
        }

        private void TickTowers(
            List<BattleTower> towers,
            List<UnitAgent> enemyUnits,
            List<BattleTower> enemyTowers,
            List<UnitAgent> friendlyUnitsForGate)
        {
            // Current rule: towers only shoot if their side has no units alive
            if (HasAliveUnits(friendlyUnitsForGate))
                return;

            for (int i = 0; i < towers.Count; i++)
            {
                var tower = towers[i];
                if (tower == null || tower.currentHp <= 0) continue;

                if (Time.time < tower.nextAttackTime)
                    continue;

                float range = Mathf.Max(0.1f, tower.rangeMeters);
                float rangeSqr = range * range;
                Vector3 pos = tower.transform.position;

                float bestDistSqr = float.MaxValue;
                UnitAgent bestEnemyUnit = null;
                BattleTower bestEnemyTower = null;

                // Prefer enemy units
                for (int j = 0; j < enemyUnits.Count; j++)
                {
                    var enemy = enemyUnits[j];
                    if (enemy == null) continue;

                    var rt = enemy.GetComponent<UnitRuntime>();
                    if (rt == null || rt.GetFinalHealth() <= 0) continue;

                    float distSqr = SqrDistanceToAgent(enemy, pos);
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestEnemyUnit = enemy;
                        bestEnemyTower = null;
                    }
                }

                // If no enemy units, target an enemy tower
                if (bestEnemyUnit == null)
                {
                    for (int j = 0; j < enemyTowers.Count; j++)
                    {
                        var et = enemyTowers[j];
                        if (et == null || et.currentHp <= 0) continue;

                        float distSqr = SqrDistanceToTower(et, pos);
                        if (distSqr < bestDistSqr)
                        {
                            bestDistSqr = distSqr;
                            bestEnemyUnit = null;
                            bestEnemyTower = et;
                        }
                    }
                }

                if (bestEnemyUnit == null && bestEnemyTower == null)
                    continue;

                if (bestDistSqr > rangeSqr)
                    continue;

                int damage = Mathf.Max(1, tower.attack);

                if (bestEnemyUnit != null)
                {
                    // If the defender has a status that modifies damage taken, apply it.
                    var enemyRuntime = bestEnemyUnit.GetComponent<UnitRuntime>();
                    if (enemyRuntime != null)
                    {
                        float takenMultiplier = enemyRuntime.GetDamageTakenMultiplier();
                        if (takenMultiplier > 0f)
                        {
                            damage = Mathf.Max(1, Mathf.RoundToInt(damage * takenMultiplier));
                        }
                    }

                    // Use shared kill helper so UnitDied + list cleanup are consistent.
                    KillUnit(bestEnemyUnit, damage);
                }
                else
                {
                    bestEnemyTower.ApplyDamage(damage);
                    if (bestEnemyTower.currentHp <= 0)
                    {
                        bestEnemyTower.currentHp = 0;
                        OnTowerDestroyed(bestEnemyTower);
                    }
                }

                tower.nextAttackTime = Time.time + towerAttackIntervalSeconds;
            }
        }

        /// <summary>
        /// Apply damage to a unit, remove it from the correct list, fire UnitDied,
        /// and destroy its GameObject if it dies.
        /// Use this for ALL lethal damage (including traps) so the battle flow stays in sync.
        ///summary>
        public void KillUnit(UnitAgent unit, int damage)
        {
            if (unit == null)
                return;

            var rt = unit.GetComponent<UnitRuntime>();
            if (rt == null || rt.GetFinalHealth() <= 0)
                return;

            int finalDamage = Mathf.Max(0, damage);
            if (finalDamage <= 0)
                return;

            rt.health -= finalDamage;

            // Low-HP Savage trigger:
            // If this unit's card is configured and it just dropped below 25% of its base HP
            // (but survived the hit), grant itself Savage stacks.
            var selfCard = unit.sourceCard;
            if (selfCard != null && selfCard.savageStacksOnLowHealth > 0 && rt.StatusController != null)
            {
                float baseMaxHp = selfCard.health;
                if (baseMaxHp > 0f)
                {
                    float threshold = baseMaxHp * 0.25f;

                    // Health AFTER this damage:
                    float newHealth = rt.GetFinalHealth();
                    // Health BEFORE this damage (approximate: add the damage back).
                    float previousHealth = newHealth + finalDamage;

                    // Trigger only if:
                    // - Unit survived (newHealth > 0),
                    // - It crossed from above the threshold to at/below it.
                    if (newHealth > 0f && previousHealth > threshold && newHealth <= threshold)
                    {
                        rt.StatusController.AddSavageStacks(selfCard.savageStacksOnLowHealth);

                        int totalStacks = rt.StatusController.GetSavageStacks();
                        float dmgMult = rt.GetDamageDealtMultiplier();

                        Debug.Log(
                            $"[Savage] {rt.displayName} dropped below 25% HP and gained {selfCard.savageStacksOnLowHealth} Savage " +
                            $"(now {totalStacks} stacks, dmgMult={dmgMult:F2})."
                        );
                    }
                }
            }


            // Use final health (base + buffs) to decide death
            if (rt.GetFinalHealth() <= 0)
            {
                // Cache info about the dying unit before we remove / destroy it.
                var dyingCard = unit.sourceCard;
                Realm dyingRealm = dyingCard != null ? dyingCard.realm : rt.realm;
                bool isVorgcoDeath = dyingCard != null && dyingCard.race == Race.Vorgco;

                // Remove from the correct list.
                if (unit.ownerId == 0)
                    _localUnits.Remove(unit);
                else
                    _remoteUnits.Remove(unit);

                // Notify listeners (BattleSceneController, etc.).
                UnitDied?.Invoke(unit);

                // Savage trigger: when a Vorg'co unit dies, others may respond.
                if (isVorgcoDeath)
                {
                    ApplySavageOnVorgcoDeath(dyingRealm, unit);
                }

                // Savage deathrattle: when THIS card dies, give Savage to a random
                // friendly Savage Vorg'co unit (if configured).
                if (dyingCard != null && dyingCard.savageStacksOnDeathToSavage > 0)
                {
                    ApplySavageOnDeathToSavage(
                        ownerId: unit.ownerId,
                        realm: dyingRealm,
                        stacks: dyingCard.savageStacksOnDeathToSavage
                    );
                }

                // Destroy the GameObject so GraveyardOnDestroy & visuals run.
                Destroy(unit.gameObject);
            }
        }

        // --- Utilities --------------------------------------------------------

        private static float SqrDistanceToAgent(UnitAgent agent, Vector3 from)
        {
            if (agent == null) return float.MaxValue;

            var col = agent.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 p = col.ClosestPoint(from);
                return (p - from).sqrMagnitude;
            }
            return (agent.transform.position - from).sqrMagnitude;
        }

        private static float SqrDistanceToTower(BattleTower tower, Vector3 from)
        {
            if (tower == null) return float.MaxValue;

            var col = tower.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 p = col.ClosestPoint(from);
                return (p - from).sqrMagnitude;
            }
            return (tower.transform.position - from).sqrMagnitude;
        }

        private static bool HasAliveUnits(List<UnitAgent> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null) continue;

                var rt = u.GetComponent<UnitRuntime>();
                if (rt != null && rt.GetFinalHealth() > 0)
                    return true;
            }
            return false;
        }

        private static bool HasAliveTowers(List<BattleTower> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null) continue;
                if (t.currentHp > 0)
                    return true;
            }
            return false;
        }

        private void OnTowerDestroyed(BattleTower tower)
        {
            if (tower == null) return;

            int owner = tower.ownerId;
            Debug.Log($"[CombatResolver] Tower destroyed: owner={owner}, index={tower.index}");

            Destroy(tower.gameObject);

            var towerList = owner == 0 ? _localTowers : _remoteTowers;
            bool anyAlive = HasAliveTowers(towerList);

            if (!anyAlive)
            {
                // This side has no towers left -> they lose.
                OnSideDefeated?.Invoke(owner);
            }
        }

        /// <summary>
        /// Call this when a new battle is about to start (from CardPhase).
        /// IMPORTANT: We intentionally DO NOT clear tower lists here.
        /// Towers are registered per-battle via RegisterTowers, and clearing
        /// them here after registration would break targeting in later battles.
        /// </summary>
        public void ResetForNewBattle()
        {
            // Clear unit lists so we don't keep stale UnitAgents across battles.
            _localUnits.Clear();
            _remoteUnits.Clear();

            // Do NOT clear _localTowers / _remoteTowers here.
            // They are maintained by RegisterTowers per battle.

            // Reset timing so the Update-driven tick logic starts fresh.
            _lastTickTime = Time.time;
            _accumulatedUnitTime = 0f;
            _accumulatedTowerTime = 0f;

            // Ensure the resolver is active for the new battle.
            if (!enabled)
                enabled = true;
        }

        /// <summary>
        /// When a Vorg'co unit dies, grant Savage stacks to any surviving units whose CardSO
        /// has savageStacksOnVorgcoDeath > 0 and whose realm matches the dying unit's realm.
        /// </summary>
        private void ApplySavageOnVorgcoDeath(Realm dyingRealm, UnitAgent dyingUnit)
        {
            ApplySavageOnVorgcoDeathToList(_localUnits, dyingRealm, dyingUnit);
            ApplySavageOnVorgcoDeathToList(_remoteUnits, dyingRealm, dyingUnit);
        }

        private void ApplySavageOnVorgcoDeathToList(List<UnitAgent> list, Realm dyingRealm, UnitAgent dyingUnit)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || u == dyingUnit)
                    continue;

                var rt = u.GetComponent<UnitRuntime>();
                if (rt == null || rt.GetFinalHealth() <= 0 || rt.StatusController == null)
                    continue;

                var card = u.sourceCard;
                if (card == null)
                    continue;

                // Only respond if this card is configured to care about Vorg'co deaths.
                if (card.savageStacksOnVorgcoDeath <= 0)
                    continue;

                // Optional design rule: only units from the same realm as the dying Vorg'co respond.
                if (card.realm != dyingRealm)
                    continue;

                rt.StatusController.AddSavageStacks(card.savageStacksOnVorgcoDeath);

                int totalStacks = rt.StatusController.GetSavageStacks();
                float dmgMult = rt.GetDamageDealtMultiplier();

                Debug.Log(
                    $"[Savage] {rt.displayName} gained {card.savageStacksOnVorgcoDeath} Savage because a Vorg'co died " +
                    $"(now {totalStacks} stacks, dmgMult={dmgMult:F2})."
                );
            }
        }

        /// <summary>
        /// When a unit with a "Savage deathrattle" dies, grant its configured number
        /// of Savage stacks to ONE random friendly unit that:
        /// - Is alive,
        /// - Has race = Vorg'co,
        /// - Is marked as a Savage archetype.
        /// Optionally constrained by realm (we require same realm as the dying unit).
        /// </summary>
        private void ApplySavageOnDeathToSavage(int ownerId, Realm realm, int stacks)
        {
            if (stacks <= 0)
                return;

            // Choose the correct unit list for this owner.
            List<UnitAgent> list = ownerId == 0 ? _localUnits : _remoteUnits;

            // Collect all eligible candidates.
            List<UnitAgent> candidates = new List<UnitAgent>();

            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null)
                    continue;

                var rt = u.GetComponent<UnitRuntime>();
                if (rt == null || rt.GetFinalHealth() <= 0 || rt.StatusController == null)
                    continue;

                var card = u.sourceCard;
                if (card == null)
                    continue;

                // Must be a Vorg'co and marked as Savage archetype.
                if (card.race != Race.Vorgco)
                    continue;

                if (!card.isSavageArchetype)
                    continue;

                // Keep it within the same realm for flavor / containment.
                if (card.realm != realm)
                    continue;

                candidates.Add(u);
            }

            if (candidates.Count == 0)
            {
                // No eligible Savage Vorg'co unit to receive the deathrattle.
                return;
            }

            // Pick one random candidate.
            int index = Random.Range(0, candidates.Count);
            UnitAgent chosen = candidates[index];

            var chosenRuntime = chosen.GetComponent<UnitRuntime>();
            if (chosenRuntime == null || chosenRuntime.StatusController == null)
                return;

            chosenRuntime.StatusController.AddSavageStacks(stacks);

            int totalStacks = chosenRuntime.StatusController.GetSavageStacks();
            float dmgMult = chosenRuntime.GetDamageDealtMultiplier();

            Debug.Log(
                $"[Savage] {chosenRuntime.displayName} received {stacks} Savage from a deathrattle " +
                $"(now {totalStacks} stacks, dmgMult={dmgMult:F2})."
            );
        }
    }
}
