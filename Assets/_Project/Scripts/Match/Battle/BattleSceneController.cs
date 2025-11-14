using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Match.State;   // BattleDescriptor, BattleUnitSeed, BattleResult, UnitSnapshot, TowerSnapshot
using Game.Match.Units;
using Game.Match.CardPhase; // to read board center from BattlePlacementRegistry
using Game.Match.Battle;    // BattleRecallController, UnitOriginStamp

namespace Game.Match.Battle
{
    /// <summary>
    /// Battle orchestrator:
    /// - Hides CardPhase scene roots while the battle runs
    /// - Spawns units from seeds
    /// - Maps CardPhase positions to the lane without scaling:
    ///     CardPhase X -> laneRight (relative to CardPhase board CENTER X)
    ///     CardPhase Z -> laneFwd   (front row anchored a small offset from localSpawn)
    /// - Grounds units and keeps a stable visual yaw
    /// - Resolves battle and unloads or hides this scene on return
    /// </summary>
    public class BattleSceneController : MonoBehaviour
    {
        public static BattleSceneController Instance { get; private set; }

        // First-time handoff from CardPhase -> Battle scene load
        private static BattleDescriptor pendingDescriptor;

        // Descriptor for the currently running battle round (reused scene)
        private BattleDescriptor currentDescriptor;

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

        [SerializeField] private GameObject battleCanvasRoot;    // parent of UnitBarController, recall button, etc.
        [SerializeField] private GameObject unitBarControllerGO; // the GameObject that has BattleUnitBarUI

        [Header("Return / Recall")]
        [SerializeField] private BattleRecallController recallController;

        private bool _battleFinished;
        private bool _laneInitialized;
        private Vector3 _laneFwd;     // local -> remote
        private Vector3 _laneRight;   // right relative to lane forward
        private float _lanePlaneY;    // fallback Y if raycast misses

        public static void SetPendingDescriptor(BattleDescriptor descriptor)
        {
            pendingDescriptor = descriptor;
        }

        private void Awake()
        {
            // Singleton for the Battle scene controller
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (combatResolver == null) combatResolver = FindObjectOfType<CombatResolver>();
        }

        private void OnEnable()
        {
            if (combatResolver != null) combatResolver.OnSideDefeated += HandleSideDefeated;
            if (recallController != null) recallController.OnAllSidesEmpty += ReturnToCardPhase;
        }

        private void OnDisable()
        {
            if (combatResolver != null) combatResolver.OnSideDefeated -= HandleSideDefeated;
            if (recallController != null) recallController.OnAllSidesEmpty -= ReturnToCardPhase;
        }

        private void Start()
        {
            // First time this scene is loaded:
            // - Either we were given a descriptor via SetPendingDescriptor(...)
            // - Or we fall back to MatchRuntimeService.pendingBattle (old path).
            BattleDescriptor desc = pendingDescriptor;

            if (desc == null)
            {
                var match = MatchRuntimeService.Instance;
                if (match != null)
                {
                    desc = match.pendingBattle;
                }
            }

            if (desc == null)
            {
                Debug.LogWarning("[BattleSceneController] Start() → no BattleDescriptor (pendingDescriptor and MatchRuntimeService.pendingBattle are null).");
                return;
            }

            BeginBattleRound(desc);

            // Clear the one-shot static descriptor; future rounds will be triggered directly
            pendingDescriptor = null;
        }

        /// <summary>
        /// Called for:
        /// - The very first battle when the scene loads (from Start()).
        /// - All subsequent battles in the same match (from CardPhaseBattleLauncher via reuse).
        /// Reuses the same towers and CombatResolver so tower HP naturally persists between rounds.
        /// </summary>
        public void BeginBattleRound(BattleDescriptor desc)
        {
            if (desc == null)
            {
                Debug.LogWarning("[BattleSceneController] BeginBattleRound called with null descriptor.");
                return;
            }

            currentDescriptor = desc;
            _battleFinished = false;

            if (unitPrefab == null)
            {
                Debug.LogError("[BattleSceneController] unitPrefab not assigned.");
                return;
            }

            SetupLaneFrame();

            // Hide CardPhase roots so the board/UI don't bleed into battle
            HideSceneRoots(cardPhaseSceneName);

            // Make sure this scene is fully active for this battle
            ShowThisBattleScene();

            // Fresh combat state for the new round but KEEP tower HP as-is
            ReinitCombatResolver();

            // Spawn units for this particular round
            SpawnUnits(desc);

            Debug.Log($"[BattleSceneController] BeginBattleRound → local={desc.localUnits.Count}, remote={desc.remoteUnits.Count}");
        }

        private void SetupLaneFrame()
        {
            if (_laneInitialized) return;

            if (localSpawn == null || remoteSpawn == null)
            {
                Debug.LogError("[BattleSceneController] spawn points not assigned.");
                return;
            }

            // Lane frame from spawn points
            _laneFwd = remoteSpawn.position - localSpawn.position; _laneFwd.y = 0f;
            _laneFwd = _laneFwd.sqrMagnitude < 1e-6f ? Vector3.right : _laneFwd.normalized;
            _laneRight = Vector3.Cross(Vector3.up, _laneFwd).normalized;
            _lanePlaneY = 0.5f * (localSpawn.position.y + remoteSpawn.position.y);

            _laneInitialized = true;
        }

        private void SpawnUnits(BattleDescriptor desc)
        {
            // Ensure battle scene objects are active
            ShowThisBattleScene();

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

                // For remote AI/test units we may not have a true CardPhase origin; pass Vector3.zero
                SpawnOne(seed, desired, remoteDir, "REMOTE", Vector3.zero);
            }

            // Unit bar bootstrap
            var bar = FindObjectOfType<Game.Match.UI.BattleUnitBarUI>(true);
            if (bar != null)
            {
                bar.Initialize(combatResolver, 0);
                bar.BootstrapFrom(combatResolver.LocalUnits);
            }
        }

        private void ReinitCombatResolver()
        {
            // Safety: make sure the GO and script are enabled
            if (combatResolver != null)
            {
                if (!combatResolver.gameObject.activeSelf) combatResolver.gameObject.SetActive(true);
                if (!combatResolver.enabled) combatResolver.enabled = true;

                // Fresh state for a new battle (units, internals, etc.) but towers themselves
                // are reused objects, so their currentHp persists between rounds.
                combatResolver.ResetForNewBattle();
                combatResolver.RegisterTowers(localTowers, remoteTowers);
            }

            Debug.Log($"[Combat] Reinit: towers A={localTowers?.Length ?? 0}, B={remoteTowers?.Length ?? 0}");
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
                Debug.LogWarning("[BattleSceneController] Local board center X not set; using 0. Call BattlePlacementRegistry.SetLocalBoardCenterX from CardPhase.");
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

                // Original CardPhase world position (when exact)
                Vector3 p = seed.useExactPosition ? seed.exactPosition : localSpawn.position + _laneFwd * seed.spawnOffset;

                float dx = p.x - boardCenterX; // preserve side bias relative to the BOARD center
                float dz = p.z - minZ;         // preserve depth ordering relative to the front row

                Vector3 desired = anchor + _laneRight * dx + _laneFwd * dz;

                // Stamp CardPhase origin so survivors can rehydrate onto their original tiles later.
                Vector3 originWorld = p;
                SpawnOne(seed, desired, moveForward, "LOCAL*mapped", originWorld);
            }
        }

        private void SpawnOne(BattleUnitSeed seed, Vector3 desiredWorldPos, Vector3 moveForward, string label, Vector3 originCardPhaseWorld)
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

            var stamp = go.AddComponent<UnitOriginStamp>();
            stamp.ownerId = seed.ownerId;
            stamp.sourceCard = seed.card;
            stamp.cardPhaseWorld = originCardPhaseWorld;

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

        private void ShowSceneRoots(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var s = SceneManager.GetSceneByName(sceneName);
            if (!s.IsValid()) return;
            foreach (var go in s.GetRootGameObjects())
                go.SetActive(true);
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

            // Match is over → unload the whole BattleStage scene.
            SceneManager.UnloadSceneAsync(gameObject.scene);
        }

        // Called by recall controller when both sides have no units on field.
        private void ReturnToCardPhase()
        {
            // Unhide CardPhase
            ShowSceneRoots(cardPhaseSceneName);

            // Apply survivors back into CardPhase (HP restored on next spawn)
            var match = MatchRuntimeService.Instance;
            var survivors = match?.survivors;
            int a = 0, b = 0;
            if (survivors != null)
            {
                (a, b) = survivors.ConsumeAndApplyToCardPhase();
            }

            // Read current tower HP (persisting between phases)
            int aTowers = 0, bTowers = 0;
            for (int i = 0; i < localTowers.Length; i++) if (localTowers[i] != null) aTowers += Mathf.Max(0, localTowers[i].currentHp);
            for (int i = 0; i < remoteTowers.Length; i++) if (remoteTowers[i] != null) bTowers += Mathf.Max(0, remoteTowers[i].currentHp);

            Debug.Log($"[Return] Battle ended → CardPhase (survivors A={a}, B={b}; towers HP sum: A={aTowers}, B={bTowers})");

            // Hide this battle scene (keep loaded) so we can reuse it next time.
            HideThisBattleScene();

            // Pause battle systems
            if (combatResolver) combatResolver.enabled = false;

            var turn = FindObjectOfType<Game.Match.State.TurnController>();
            if (turn != null)
            {
                turn.OnReturnFromBattle();
            }
            else
            {
                Debug.LogWarning("[BattleSceneController] Returned to CardPhase but no TurnController was found.");
            }
        }

        // Hide all roots in THIS (BattleStage) scene
        private void HideThisBattleScene()
        {
            var s = gameObject.scene;
            if (!s.IsValid()) return;

            // Disable cameras & listeners in this scene first (avoids double renders / audio)
            foreach (var root in s.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
                foreach (var al in root.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
            }

            foreach (var root in s.GetRootGameObjects())
                root.SetActive(false);
        }

        // Re-enable everything in this scene (roots, cameras, audio, etc.)
        private void ShowThisBattleScene()
        {
            // First, re-activate all root objects in THIS (BattleStage) scene
            var s = gameObject.scene;
            if (s.IsValid())
            {
                foreach (var root in s.GetRootGameObjects())
                {
                    // Reactivate root
                    root.SetActive(true);

                    // Re-enable any cameras and listeners we turned off in HideThisBattleScene
                    foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                        cam.enabled = true;

                    foreach (var al in root.GetComponentsInChildren<AudioListener>(true))
                        al.enabled = true;
                }
            }

            // Make sure the battle UI is visible again
            if (battleCanvasRoot != null)
                battleCanvasRoot.SetActive(true);

            if (unitBarControllerGO != null)
                unitBarControllerGO.SetActive(true);

            // Finally, ensure the combat logic actually runs
            if (combatResolver != null)
                combatResolver.enabled = true;
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
