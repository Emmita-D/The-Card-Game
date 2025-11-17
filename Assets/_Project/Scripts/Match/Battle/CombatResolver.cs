using System.Collections.Generic;
using UnityEngine;
using Game.Match.Units;

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
                if (runtime == null || runtime.health <= 0) continue;

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
                    if (enemyRt == null || enemyRt.health <= 0) continue;

                    // NEW: respect combat rules (movement type, attack mode, per-card nerfs)
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

                        // NEW: respect combat rules (per-card tower permissions)
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
                    int damage = Mathf.Max(1, runtime.attack);

                    if (bestEnemyUnit != null)
                    {
                        var enemyRuntime = bestEnemyUnit.GetComponent<UnitRuntime>();
                        if (enemyRuntime != null && enemyRuntime.health > 0)
                        {
                            enemyRuntime.health -= damage;
                            if (enemyRuntime.health <= 0)
                            {
                                // Remove & destroy
                                if (bestEnemyUnit.ownerId == 0) _localUnits.Remove(bestEnemyUnit);
                                else _remoteUnits.Remove(bestEnemyUnit);
                                UnitDied?.Invoke(bestEnemyUnit);
                                Destroy(bestEnemyUnit.gameObject);
                            }
                        }
                    }
                    else // tower
                    {
                        bestEnemyTower.currentHp -= damage;
                        if (bestEnemyTower.currentHp <= 0)
                        {
                            bestEnemyTower.currentHp = 0;
                            OnTowerDestroyed(bestEnemyTower);
                        }
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
                    if (rt == null || rt.health <= 0) continue;

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
                    var rt = bestEnemyUnit.GetComponent<UnitRuntime>();
                    if (rt != null && rt.health > 0)
                    {
                        rt.health -= damage;
                        if (rt.health <= 0)
                        {
                            if (bestEnemyUnit.ownerId == 0) _localUnits.Remove(bestEnemyUnit);
                            else _remoteUnits.Remove(bestEnemyUnit);
                            UnitDied?.Invoke(bestEnemyUnit);
                            Destroy(bestEnemyUnit.gameObject);
                        }
                    }
                }
                else
                {
                    bestEnemyTower.currentHp -= damage;
                    if (bestEnemyTower.currentHp <= 0)
                    {
                        bestEnemyTower.currentHp = 0;
                        OnTowerDestroyed(bestEnemyTower);
                    }
                }

                tower.nextAttackTime = Time.time + towerAttackIntervalSeconds;
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
                if (rt != null && rt.health > 0)
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
    }
}
