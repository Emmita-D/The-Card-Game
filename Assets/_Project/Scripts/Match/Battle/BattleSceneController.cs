using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Match.State;   // MatchRuntimeService, BattleDescriptor, BattleUnitSeed, BattleResult, UnitSnapshot, TowerSnapshot
using Game.Match.Units;

namespace Game.Match.Battle
{
    /// <summary>
    /// Orchestrates the BattleStage scene:
    /// - Holds references to spawn points and towers
    /// - Reads MatchRuntimeService.pendingBattle
    /// - Spawns units for both sides and registers them with CombatResolver
    /// - Listens for CombatResolver.OnSideDefeated to end the battle and store a BattleResult
    /// </summary>
    public class BattleSceneController : MonoBehaviour
    {
        [Header("Scene References")]
        [Tooltip("Where local units will spawn from.")]
        public Transform localSpawn;

        [Tooltip("Where remote units will spawn from.")]
        public Transform remoteSpawn;

        [Tooltip("Local towers, usually [0] = front, [1] = back.")]
        public BattleTower[] localTowers;

        [Tooltip("Remote towers, usually [0] = front, [1] = back.")]
        public BattleTower[] remoteTowers;

        [Header("Unit spawning")]
        [Tooltip("Prefab used for units in the battle stage (e.g. Unit_Dummy).")]
        public GameObject unitPrefab;

        [Header("Services")]
        public CombatResolver combatResolver;

        private bool _battleFinished = false;

        private void Awake()
        {
            if (combatResolver == null)
            {
                combatResolver = FindObjectOfType<CombatResolver>();
            }
        }

        private void OnEnable()
        {
            if (combatResolver != null)
            {
                combatResolver.OnSideDefeated += HandleSideDefeated;
            }
        }

        private void OnDisable()
        {
            if (combatResolver != null)
            {
                combatResolver.OnSideDefeated -= HandleSideDefeated;
            }
        }

        private void Start()
        {
            var match = MatchRuntimeService.Instance;
            if (match == null)
            {
                Debug.LogWarning("[BattleSceneController] MatchRuntimeService not found. " +
                                 "When running the full game, make sure Boot scene is loaded first.");
                return;
            }

            var desc = match.pendingBattle;
            if (desc == null)
            {
                Debug.LogWarning("[BattleSceneController] No pendingBattle descriptor found. " +
                                 "Did you call StartBattle from CardPhase?");
                return;
            }

            Debug.Log($"[BattleSceneController] pendingBattle: " +
                      $"local={desc.localUnits.Count}, remote={desc.remoteUnits.Count}");

            if (unitPrefab == null)
            {
                Debug.LogError("[BattleSceneController] unitPrefab is not assigned.");
                return;
            }

            if (localSpawn == null || remoteSpawn == null)
            {
                Debug.LogError("[BattleSceneController] Spawn points not assigned.");
                return;
            }

            if (combatResolver != null)
            {
                combatResolver.RegisterTowers(localTowers, remoteTowers);
            }

            SpawnUnits(desc);
        }

        private void SpawnUnits(BattleDescriptor desc)
        {
            // Direction along the lane: from localSpawn to remoteSpawn
            Vector3 laneDir = (remoteSpawn.position - localSpawn.position).normalized;
            Vector3 localDir = laneDir;
            Vector3 remoteDir = -laneDir;

            // Local side
            foreach (var seed in desc.localUnits)
            {
                if (seed.card == null)
                {
                    Debug.LogWarning("[BattleSceneController] Skipping local seed with null card.");
                    continue;
                }

                Vector3 pos = seed.useExactPosition
                    ? seed.exactPosition
                    : localSpawn.position + laneDir * seed.spawnOffset;

                Debug.Log($"[BattleSceneController] Spawning LOCAL unit card={seed.card.name}, " +
                          $"owner={seed.ownerId}, useExact={seed.useExactPosition}, pos={pos}");

                SpawnSingleUnit(seed, pos, localDir);
            }

            // Remote side
            foreach (var seed in desc.remoteUnits)
            {
                if (seed.card == null)
                {
                    Debug.LogWarning("[BattleSceneController] Skipping remote seed with null card.");
                    continue;
                }

                Vector3 pos = seed.useExactPosition
                    ? seed.exactPosition
                    : remoteSpawn.position + (-laneDir) * seed.spawnOffset;

                Debug.Log($"[BattleSceneController] Spawning REMOTE unit card={seed.card.name}, " +
                          $"owner={seed.ownerId}, useExact={seed.useExactPosition}, pos={pos}");

                SpawnSingleUnit(seed, pos, remoteDir);
            }
        }

        private void SpawnSingleUnit(BattleUnitSeed seed, Vector3 position, Vector3 direction)
        {
            // Parent under this controller so the units belong to the BattleStage scene.
            var go = Instantiate(unitPrefab, position, Quaternion.identity, transform);

            var agent = go.GetComponent<UnitAgent>();
            if (agent == null)
            {
                agent = go.AddComponent<UnitAgent>();
            }

            agent.Initialize(seed.card, seed.ownerId, direction);

            if (combatResolver != null)
            {
                combatResolver.RegisterUnit(agent);
            }

            Debug.Log($"[BattleSceneController] Instantiated unit prefab for card={seed.card.name}, " +
                      $"owner={seed.ownerId}, worldPos={go.transform.position}");
        }

        private void HandleSideDefeated(int loserOwnerId)
        {
            if (_battleFinished) return;
            _battleFinished = true;

            var match = MatchRuntimeService.Instance;
            if (match == null)
            {
                Debug.LogWarning("[BattleSceneController] MatchRuntimeService not found when trying to finish battle.");
                return;
            }

            var result = new BattleResult
            {
                winnerId = loserOwnerId == 0 ? 1 : 0
            };

            // Snapshot surviving units
            if (combatResolver != null)
            {
                // Local survivors
                foreach (var agent in combatResolver.LocalUnits)
                {
                    if (agent == null) continue;

                    var rt = agent.GetComponent<UnitRuntime>();
                    if (rt == null || rt.health <= 0) continue;

                    result.survivorsLocal.Add(new UnitSnapshot
                    {
                        card = agent.sourceCard,
                        ownerId = agent.ownerId,
                        remainingHp = rt.health
                    });
                }

                // Remote survivors
                foreach (var agent in combatResolver.RemoteUnits)
                {
                    if (agent == null) continue;

                    var rt = agent.GetComponent<UnitRuntime>();
                    if (rt == null || rt.health <= 0) continue;

                    result.survivorsRemote.Add(new UnitSnapshot
                    {
                        card = agent.sourceCard,
                        ownerId = agent.ownerId,
                        remainingHp = rt.health
                    });
                }
            }

            // Snapshot towers (we treat destroyed towers as HP = 0)
            for (int i = 0; i < localTowers.Length; i++)
            {
                var t = localTowers[i];
                int hp = (t != null) ? t.currentHp : 0;

                result.towers.Add(new TowerSnapshot
                {
                    ownerId = 0,
                    index = i,
                    remainingHp = hp
                });
            }

            for (int i = 0; i < remoteTowers.Length; i++)
            {
                var t = remoteTowers[i];
                int hp = (t != null) ? t.currentHp : 0;

                result.towers.Add(new TowerSnapshot
                {
                    ownerId = 1,
                    index = i,
                    remainingHp = hp
                });
            }

            match.lastBattleResult = result;
            match.pendingBattle = null;

            Debug.Log($"[BattleSceneController] Battle finished. Winner = {result.winnerId}. " +
                      $"Survivors: local={result.survivorsLocal.Count}, remote={result.survivorsRemote.Count}");

            // Unload this BattleStage scene and return to card-phase context.
            var scene = gameObject.scene;
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
