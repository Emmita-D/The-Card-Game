using System.Collections.Generic;
using UnityEngine;
using Game.Match.Units;

namespace Game.Match.Battle
{
    /// <summary>
    /// Central brain for combat: tracks units and towers, picks targets, and applies damage.
    /// Step 5: simple nearest-target logic and shared attack cadence.
    /// Step 6: detects when a side has no towers left and raises OnSideDefeated.
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

        /// <summary>
        /// Fired when one side has no towers left.
        /// Parameter = loser ownerId (0 or 1).
        /// </summary>
        public System.Action<int> OnSideDefeated;

        private readonly List<UnitAgent> _localUnits = new();
        private readonly List<UnitAgent> _remoteUnits = new();
        private readonly List<BattleTower> _localTowers = new();
        private readonly List<BattleTower> _remoteTowers = new();

        // Expose read-only views so BattleSceneController can snapshot survivors.
        public IReadOnlyList<UnitAgent> LocalUnits => _localUnits;
        public IReadOnlyList<UnitAgent> RemoteUnits => _remoteUnits;

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            // Local units vs remote side, then remote units vs local side
            TickUnits(_localUnits, _remoteUnits, _remoteTowers);
            TickUnits(_remoteUnits, _localUnits, _localTowers);

            // Towers shoot only when their own lane is empty of friendly units
            TickTowers(_localTowers, _remoteUnits, _remoteTowers);
            TickTowers(_remoteTowers, _localUnits, _localTowers);
        }

        private void TickUnits(List<UnitAgent> attackers, List<UnitAgent> enemyUnits, List<BattleTower> enemyTowers)
        {
            for (int i = 0; i < attackers.Count; i++)
            {
                var unit = attackers[i];
                if (unit == null) continue;

                var runtime = unit.GetComponent<UnitRuntime>();
                if (runtime == null) continue;
                if (runtime.health <= 0) continue;

                // Default: unit can move unless we find a target in range.
                unit.movementLocked = false;

                float attackRange = runtime.rangeMeters > 0 ? runtime.rangeMeters : defaultMeleeRangeMeters;
                float rangeSqr = attackRange * attackRange;

                Vector3 pos = unit.transform.position;
                float bestDistSqr = float.MaxValue;
                UnitAgent bestEnemyUnit = null;
                BattleTower bestEnemyTower = null;

                // Enemy units first
                for (int j = 0; j < enemyUnits.Count; j++)
                {
                    var enemy = enemyUnits[j];
                    if (enemy == null) continue;

                    var enemyRuntime = enemy.GetComponent<UnitRuntime>();
                    if (enemyRuntime == null || enemyRuntime.health <= 0) continue;

                    float distSqr = (enemy.transform.position - pos).sqrMagnitude;
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestEnemyUnit = enemy;
                        bestEnemyTower = null;
                    }
                }

                // If no enemy units, look at towers
                if (bestEnemyUnit == null)
                {
                    for (int j = 0; j < enemyTowers.Count; j++)
                    {
                        var tower = enemyTowers[j];
                        if (tower == null) continue;
                        if (tower.currentHp <= 0) continue;

                        float distSqr = (tower.transform.position - pos).sqrMagnitude;
                        if (distSqr < bestDistSqr)
                        {
                            bestDistSqr = distSqr;
                            bestEnemyUnit = null;
                            bestEnemyTower = tower;
                        }
                    }
                }

                if (bestEnemyUnit == null && bestEnemyTower == null)
                    continue; // nothing to attack

                if (bestDistSqr > rangeSqr)
                    continue; // not in range yet

                // We are in range: lock movement and attack on cooldown.
                unit.movementLocked = true;

                if (Time.time < unit.nextAttackTime)
                    continue;

                int damage = Mathf.Max(1, runtime.attack);

                if (bestEnemyUnit != null)
                {
                    var enemyRuntime = bestEnemyUnit.GetComponent<UnitRuntime>();
                    if (enemyRuntime != null && enemyRuntime.health > 0)
                    {
                        enemyRuntime.health -= damage;
                        if (enemyRuntime.health <= 0)
                        {
                            Object.Destroy(bestEnemyUnit.gameObject);
                        }
                    }
                }
                else if (bestEnemyTower != null)
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
        }

        private void TickTowers(List<BattleTower> towers, List<UnitAgent> enemyUnits, List<BattleTower> enemyTowers)
        {
            bool haveFriendlyUnits = towers == _localTowers
                ? HasAliveUnits(_localUnits)
                : HasAliveUnits(_remoteUnits);

            // Towers only fire when their side has no units left
            if (haveFriendlyUnits)
                return;

            for (int i = 0; i < towers.Count; i++)
            {
                var tower = towers[i];
                if (tower == null) continue;
                if (tower.currentHp <= 0) continue;

                if (Time.time < tower.nextAttackTime)
                    continue;

                float range = tower.rangeMeters;
                float rangeSqr = range * range;
                Vector3 pos = tower.transform.position;

                float bestDistSqr = float.MaxValue;
                UnitAgent bestEnemyUnit = null;
                BattleTower bestEnemyTower = null;

                // Prefer enemy units if any exist
                for (int j = 0; j < enemyUnits.Count; j++)
                {
                    var enemy = enemyUnits[j];
                    if (enemy == null) continue;

                    var enemyRuntime = enemy.GetComponent<UnitRuntime>();
                    if (enemyRuntime == null || enemyRuntime.health <= 0) continue;

                    float distSqr = (enemy.transform.position - pos).sqrMagnitude;
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestEnemyUnit = enemy;
                        bestEnemyTower = null;
                    }
                }

                // If no enemy units, target enemy towers
                if (bestEnemyUnit == null)
                {
                    for (int j = 0; j < enemyTowers.Count; j++)
                    {
                        var enemyTower = enemyTowers[j];
                        if (enemyTower == null) continue;
                        if (enemyTower.currentHp <= 0) continue;

                        float distSqr = (enemyTower.transform.position - pos).sqrMagnitude;
                        if (distSqr < bestDistSqr)
                        {
                            bestDistSqr = distSqr;
                            bestEnemyUnit = null;
                            bestEnemyTower = enemyTower;
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
                    var enemyRuntime = bestEnemyUnit.GetComponent<UnitRuntime>();
                    if (enemyRuntime != null && enemyRuntime.health > 0)
                    {
                        enemyRuntime.health -= damage;
                        if (enemyRuntime.health <= 0)
                        {
                            Object.Destroy(bestEnemyUnit.gameObject);
                        }
                    }
                }
                else if (bestEnemyTower != null)
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

        private bool HasAliveUnits(List<UnitAgent> list)
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

        private bool HasAliveTowers(List<BattleTower> list)
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

            // Destroy the GameObject; its HP is already 0.
            Object.Destroy(tower.gameObject);

            // Check if this side has any tower HP left
            var towerList = owner == 0 ? _localTowers : _remoteTowers;
            bool anyAlive = HasAliveTowers(towerList);

            if (!anyAlive)
            {
                // This side has no towers left -> they lose.
                OnSideDefeated?.Invoke(owner);
            }
        }
    }
}
