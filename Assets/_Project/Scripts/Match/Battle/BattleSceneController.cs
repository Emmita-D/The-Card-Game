using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Match.State;   // BattleDescriptor, BattleUnitSeed, BattleResult, UnitSnapshot, TowerSnapshot
using Game.Match.Units;
using Game.Match.CardPhase; // <--- to read board center from BattlePlacementRegistry

namespace Game.Match.Battle
{
    /// <summary>
    /// Battle orchestrator:
    /// - Hides CardPhase scene roots while the battle runs
    /// - Spawns units from seeds
    /// - Maps CardPhase positions to the lane without scaling:
    ///     CardPhase X -> laneRight (relative to CardPhase board CENTER X, not mean of placements)
    ///     CardPhase Z -> laneFwd   (front row anchored a small offset from localSpawn)
    /// - Grounds units and keeps a stable visual yaw
    /// - Resolves battle and unloads this scene
    /// </summary>
    public class BattleSceneController : MonoBehaviour
    {
        [Header("Scene References")]
        public Transform localSpawn;     // lane start (local side)
        public Transform remoteSpawn;    // lane end   (remote side)
        public BattleTower[] localTowers;
        public BattleTower[] remoteTowers;

        [Header("Unit spawning")]
        public GameObject unitPrefab;
        public CombatResolver combatResolver;

        [Header("CardPhase interop")]
        [Tooltip("Name of the Card Phase scene to hide while the battle runs.")]
        [SerializeField] private string cardPhaseSceneName = "CardPhase";

        [Header("Mapping (no scaling)")]
        [Tooltip("How far in front of localSpawn we place the frontmost CardPhase row.")]
        [SerializeField] private float startLineForwardOffset = 0.25f;

        [Header("Visual facing")]
        [Tooltip("Extra yaw applied to the VISUAL child (not the root). Use ±90 if your model's nose isn't +Z.")]
        [SerializeField] private float yawOffsetDegrees = 0f;
        [Tooltip("Optional child name to treat as visual root (e.g., 'Model', 'Visual').")]
        [SerializeField] private string visualChildHint = "Model";

        [Header("Grounding")]
        [Tooltip("Physics layers considered ground when snapping. If 0, uses all layers.")]
        [SerializeField] private LayerMask groundMask = 0;
        [Tooltip("Extra clearance above the ground after resting the object's bottom.")]
        [SerializeField] private float groundClearance = 0.02f;
        [Tooltip("Max distance for the downward ground ray.")]
        [SerializeField] private float groundRayMaxDistance = 100f;

        private bool _battleFinished;
        private Vector3 _laneFwd;     // local -> remote
        private Vector3 _laneRight;   // right relative to lane forward
        private float _lanePlaneY;  // fallback Y if raycast misses

        private void Awake()
        {
            if (combatResolver == null) combatResolver = FindObjectOfType<CombatResolver>();
        }
        private void OnEnable()
        {
            if (combatResolver != null) combatResolver.OnSideDefeated += HandleSideDefeated;
        }
        private void OnDisable()
        {
            if (combatResolver != null) combatResolver.OnSideDefeated -= HandleSideDefeated;
        }

        private void Start()
        {
            var match = MatchRuntimeService.Instance;
            if (match == null) { Debug.LogWarning("[BattleSceneController] MatchRuntimeService missing."); return; }
            if (unitPrefab == null) { Debug.LogError("[BattleSceneController] unitPrefab not assigned."); return; }
            if (localSpawn == null || remoteSpawn == null) { Debug.LogError("[BattleSceneController] spawn points not assigned."); return; }

            // Lane frame from spawn points
            _laneFwd = remoteSpawn.position - localSpawn.position; _laneFwd.y = 0f;
            _laneFwd = _laneFwd.sqrMagnitude < 1e-6f ? Vector3.right : _laneFwd.normalized;
            _laneRight = Vector3.Cross(Vector3.up, _laneFwd).normalized;
            _lanePlaneY = 0.5f * (localSpawn.position.y + remoteSpawn.position.y);

            // Hide CardPhase roots so the board/UI don't bleed into battle
            HideSceneRoots(cardPhaseSceneName);

            var desc = match.pendingBattle;
            if (desc == null) { Debug.LogWarning("[BattleSceneController] No pendingBattle descriptor."); return; }

            Debug.Log($"[BattleSceneController] pendingBattle: local={desc.localUnits.Count}, remote={desc.remoteUnits.Count}");
            combatResolver?.RegisterTowers(localTowers, remoteTowers);
            SpawnUnits(desc);
        }

        private void SpawnUnits(BattleDescriptor desc)
        {
            Vector3 localDir = _laneFwd;
            Vector3 remoteDir = -_laneFwd;

            if (desc.localUnits.Count > 0)
                SpawnMappedLocals(desc.localUnits, localDir);

            foreach (var seed in desc.remoteUnits)
            {
                if (seed.card == null) continue;
                Vector3 desired = seed.useExactPosition
                    ? seed.exactPosition
                    : remoteSpawn.position + (-_laneFwd) * seed.spawnOffset;

                SpawnOne(seed, desired, remoteDir, "REMOTE");
            }
            var bar = FindObjectOfType<Game.Match.UI.BattleUnitBarUI>(true);
            if (bar != null)
            {
                bar.Initialize(combatResolver, 0);
                bar.BootstrapFrom(combatResolver.LocalUnits);
            }
        }

        /// <summary>
        /// Map CardPhase positions to the lane frame:
        ///   X -> laneRight, measured from the CardPhase BOARD CENTER X (supplied by registry)
        ///   Z -> laneFwd,   measured from the frontmost placed row (min Z)
        /// This preserves left/right bias exactly as seen in CardPhase (no centering to placements).
        /// </summary>
        private void SpawnMappedLocals(List<BattleUnitSeed> locals, Vector3 moveForward)
        {
            // Get board center X from registry (set by DraggableCard)
            float boardCenterX;
            var reg = BattlePlacementRegistry.Instance;
            if (reg != null && reg.TryGetLocalBoardCenterX(out var cx)) boardCenterX = cx;
            else
            {
                // Fallback: if center wasn't set, use zero so at least we don't re-center to mean
                boardCenterX = 0f;
                Debug.LogWarning("[BattleSceneController] Local board center X not set; using 0. " +
                                 "Call BattlePlacementRegistry.SetLocalBoardCenterX from CardPhase.");
            }

            // Find the frontmost Z among the placed locals (so that front row touches start offset)
            float minZ = float.PositiveInfinity;
            foreach (var s in locals)
            {
                if (s.card == null) continue;
                Vector3 p = s.useExactPosition ? s.exactPosition : localSpawn.position + _laneFwd * s.spawnOffset;
                if (p.z < minZ) minZ = p.z;
            }
            if (minZ == float.PositiveInfinity) return;

            Vector3 anchor = localSpawn.position + _laneFwd * startLineForwardOffset;

            foreach (var seed in locals)
            {
                if (seed.card == null) continue;

                Vector3 p = seed.useExactPosition ? seed.exactPosition : localSpawn.position + _laneFwd * seed.spawnOffset;

                float dx = p.x - boardCenterX; // preserve side bias relative to the BOARD center
                float dz = p.z - minZ;         // preserve depth ordering relative to the front row

                Vector3 desired = anchor + _laneRight * dx + _laneFwd * dz;
                SpawnOne(seed, desired, moveForward, "LOCAL*mapped");
            }
        }

        private void SpawnOne(BattleUnitSeed seed, Vector3 desiredWorldPos, Vector3 moveForward, string label)
        {
            float groundY = GetGroundYAt(desiredWorldPos);
            Vector3 spawnPos = new Vector3(desiredWorldPos.x, groundY, desiredWorldPos.z);

            // Root faces lane for movement; visuals get an extra yaw (child) so they LOOK right
            Quaternion rootRot = Quaternion.LookRotation(moveForward, Vector3.up);

            var go = Instantiate(unitPrefab, spawnPos, rootRot, transform);
            RestObjectOnGround(go, groundY + groundClearance);

            var visual = ResolveVisualChild(go.transform);
            AttachVisualYawApplier(visual, yawOffsetDegrees);

            var agent = go.GetComponent<UnitAgent>();
            if (agent == null) agent = go.AddComponent<UnitAgent>();
            agent.Initialize(seed.card, seed.ownerId, moveForward);
            combatResolver?.RegisterUnit(agent);

            Debug.Log($"[BattleSceneController] {label} card={seed.card.name}, owner={seed.ownerId}, pos={go.transform.position}");
        }

        private void HideSceneRoots(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var s = SceneManager.GetSceneByName(sceneName);
            if (!s.IsValid()) return;
            var roots = s.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) roots[i].SetActive(false);
        }

        private float GetGroundYAt(Vector3 worldXZ)
        {
            int mask = (groundMask.value == 0) ? ~0 : groundMask.value;
            Vector3 origin = worldXZ + Vector3.up * groundRayMaxDistance * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayMaxDistance, mask, QueryTriggerInteraction.Collide))
                return hit.point.y;
            return _lanePlaneY;
        }

        private void RestObjectOnGround(GameObject go, float targetY)
        {
            Bounds? best = null;
            var cols = go.GetComponentsInChildren<Collider>();
            foreach (var c in cols) { best = best.HasValue ? Enc(best.Value, c.bounds) : c.bounds; }
            if (!best.HasValue)
            {
                var rends = go.GetComponentsInChildren<Renderer>();
                foreach (var r in rends) { best = best.HasValue ? Enc(best.Value, r.bounds) : r.bounds; }
            }

            if (best.HasValue)
            {
                float delta = targetY - best.Value.min.y;
                go.transform.position += new Vector3(0f, delta, 0f);
            }
            else
            {
                var p = go.transform.position; p.y = targetY; go.transform.position = p;
            }

            static Bounds Enc(Bounds a, Bounds b) { a.Encapsulate(b); return a; }
        }

        private Transform ResolveVisualChild(Transform root)
        {
            if (!string.IsNullOrEmpty(visualChildHint))
            {
                var hinted = root.Find(visualChildHint);
                if (hinted != null) return hinted;
            }
            string[] names = { "Model", "Visual", "Graphics", "MeshRoot", "Art" };
            foreach (var n in names) { var t = root.Find(n); if (t != null) return t; }
            if (root.childCount == 1) return root.GetChild(0);
            var rends = root.GetComponentsInChildren<Renderer>(); if (rends.Length > 0) return rends[0].transform;
            return root;
        }

        private void AttachVisualYawApplier(Transform visual, float yaw)
        {
            if (visual == null) return;
            var applier = visual.GetComponent<VisualYawApplier>();
            if (applier == null) applier = visual.gameObject.AddComponent<VisualYawApplier>();
            applier.yawOffsetDegrees = yaw;
        }

        private void HandleSideDefeated(int loserOwnerId)
        {
            if (_battleFinished) return;
            _battleFinished = true;

            var match = MatchRuntimeService.Instance;
            if (match == null) return;

            var result = new BattleResult { winnerId = loserOwnerId == 0 ? 1 : 0 };

            if (combatResolver != null)
            {
                foreach (var agent in combatResolver.LocalUnits)
                {
                    if (agent == null) continue;
                    var rt = agent.GetComponent<UnitRuntime>();
                    if (rt != null && rt.health > 0)
                        result.survivorsLocal.Add(new UnitSnapshot { card = agent.sourceCard, ownerId = agent.ownerId, remainingHp = rt.health });
                }
                foreach (var agent in combatResolver.RemoteUnits)
                {
                    if (agent == null) continue;
                    var rt = agent.GetComponent<UnitRuntime>();
                    if (rt != null && rt.health > 0)
                        result.survivorsRemote.Add(new UnitSnapshot { card = agent.sourceCard, ownerId = agent.ownerId, remainingHp = rt.health });
                }
            }

            for (int i = 0; i < localTowers.Length; i++)
            {
                int hp = (localTowers[i] != null) ? localTowers[i].currentHp : 0;
                result.towers.Add(new TowerSnapshot { ownerId = 0, index = i, remainingHp = hp });
            }
            for (int i = 0; i < remoteTowers.Length; i++)
            {
                int hp = (remoteTowers[i] != null) ? remoteTowers[i].currentHp : 0;
                result.towers.Add(new TowerSnapshot { ownerId = 1, index = i, remainingHp = hp });
            }

            match.lastBattleResult = result;
            match.pendingBattle = null;

            SceneManager.UnloadSceneAsync(gameObject.scene);
        }
    }

    /// <summary>
    /// Keeps a consistent local yaw offset on the assigned Transform every frame,
    /// regardless of how the parent/root rotates for movement.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class VisualYawApplier : MonoBehaviour
    {
        public float yawOffsetDegrees;
        private Quaternion _base;
        private void Awake() { _base = transform.localRotation; }
        private void LateUpdate()
        {
            transform.localRotation = Quaternion.Euler(0f, yawOffsetDegrees, 0f) * _base;
        }
    }
}
