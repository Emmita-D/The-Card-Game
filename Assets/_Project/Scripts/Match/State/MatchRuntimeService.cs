using System.Collections.Generic;
using UnityEngine;
using Game.Core.Config;
using Game.Match.Cards;

namespace Game.Match.State
{
    // ------------- Battle input -------------

    /// <summary>
    /// A single unit that should be spawned in the battle scene.
    /// </summary>
    [System.Serializable]
    public struct BattleUnitSeed
    {
        public CardSO card;        // which card this unit comes from
        public int ownerId;        // 0 = local, 1 = remote

        public int laneIndex;      // for future multi-lane support
        public float spawnOffset;  // used when not using exactPosition

        // allow card phase to dictate exact world position
        public bool useExactPosition;
        public Vector3 exactPosition;

        // NEW: per-instance buffs coming from CardInstance at placement time
        public int bonusAttack;
        public int bonusHealth;
    }
    /// <summary>
    /// Full description of what each side sends into the battle stage.
    /// </summary>
    [System.Serializable]
    public class BattleDescriptor
    {
        public List<BattleUnitSeed> localUnits = new List<BattleUnitSeed>();
        public List<BattleUnitSeed> remoteUnits = new List<BattleUnitSeed>();
    }

    // ------------- Battle output -------------

    [System.Serializable]
    public struct UnitSnapshot
    {
        public CardSO card;
        public int ownerId;       // 0 / 1
        public int remainingHp;   // HP at the end of the battle
    }

    [System.Serializable]
    public struct TowerSnapshot
    {
        public int ownerId;       // 0 / 1
        public int index;         // e.g. 0 = front, 1 = back
        public int remainingHp;
    }

    /// <summary>
    /// Result of a finished battle stage: who won and who survived.
    /// </summary>
    [System.Serializable]
    public class BattleResult
    {
        /// <summary>
        /// 0 = local wins, 1 = remote wins, -1 = draw / retreat.
        /// </summary>
        public int winnerId = -1;

        public List<UnitSnapshot> survivorsLocal = new List<UnitSnapshot>();
        public List<UnitSnapshot> survivorsRemote = new List<UnitSnapshot>();
        public List<TowerSnapshot> towers = new List<TowerSnapshot>();
    }

    // ------------- Match runtime service -------------

    /// <summary>
    /// Lives across scenes and holds per-match state that both CardPhase and BattleStage can see.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class MatchRuntimeService : MonoBehaviour
    {
        private static MatchRuntimeService _instance;

        /// <summary>
        /// Quick singleton-style access. We keep it simple for this project.
        /// </summary>
        public static MatchRuntimeService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MatchRuntimeService>();
                }
                return _instance;
            }
        }

        [Header("Config (assign in Boot scene)")]
        [SerializeField] private BalanceConfigSO balanceConfig;

        [Header("Players (runtime)")]
        public PlayerRuntime local;   // our player
        public PlayerRuntime remote;  // opponent / AI

        [Header("Battle I/O (runtime)")]
        public BattleDescriptor pendingBattle;   // filled before loading BattleStage
        public BattleResult lastBattleResult;    // filled when BattleStage finishes

        private void Awake()
        {
            // Enforce a single instance that survives scene loads
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Initialize player runtimes if we have a balance config
            if (balanceConfig != null)
            {
                local = new PlayerRuntime(balanceConfig);
                remote = new PlayerRuntime(balanceConfig);
            }
            else
            {
                Debug.LogWarning("[MatchRuntimeService] BalanceConfigSO is not assigned. " +
                                 "PlayerRuntime will not be initialized automatically.");
            }

            // Start with clean battle I/O
            pendingBattle = null;
            lastBattleResult = null;
        }

        /// <summary>
        /// Clears per-match data. Call this when starting a fresh match.
        /// </summary>
        public void ResetForNewMatch()
        {
            pendingBattle = null;
            lastBattleResult = null;

            // Note: we are not wiping decks/hands here yet.
            // We'll decide that behaviour when we hook this into the full match flow.
        }

        // ----- Survivor Registry -----
        [System.Serializable]
        public class SurvivorRegistry
        {
            [System.Serializable]
            public struct Survivor
            {
                public int ownerId;
                public Cards.CardSO card;
                public Vector3 originWorld; // CardPhase center-of-footprint position
            }

            private readonly List<Survivor> _pending = new();

            /// <summary>
            /// Record all surviving units from a battle.
            /// </summary>
            public void RecordSurvivors(
                int ownerId,
                System.Collections.Generic.IEnumerable<Game.Match.Battle.UnitAgent> agents)
            {
                foreach (var a in agents)
                {
                    if (a == null) continue;

                    var rt = a.GetComponent<Game.Match.Units.UnitRuntime>();
                    if (rt == null || rt.health <= 0) continue; // dead don't return

                    var stamp = a.GetComponent<Game.Match.Battle.UnitOriginStamp>();
                    if (stamp == null || stamp.sourceCard == null) continue;

                    _pending.Add(new Survivor
                    {
                        ownerId = ownerId,
                        card = stamp.sourceCard,
                        originWorld = stamp.cardPhaseWorld   // center-of-footprint from CardPhase
                    });
                }
            }

            /// <summary>
            /// Applies survivors to CardPhase: marks grid tiles (when free) and ALWAYS re-registers placements
            /// in the BattlePlacementRegistry, so next battle includes every survivor.
            /// Returns survivor counts for both sides.
            /// </summary>
            public (int a, int b) ConsumeAndApplyToCardPhase()
            {
                // Find the CardPhase grid + placement registry
                var grid = Object.FindObjectOfType<Game.Match.Grid.GridService>(includeInactive: true);
                var reg = Game.Match.CardPhase.BattlePlacementRegistry.Instance;

                int a = 0, b = 0;

                if (grid == null || reg == null)
                {
                    Debug.LogWarning("[SurvivorRegistry] Missing GridService or BattlePlacementRegistry in CardPhase scene.");
                    _pending.Clear();
                    return (0, 0);
                }

                // 1) Remove ALL existing CardPhase board units under the grid
                //    (this wipes both survivors and dead ghosts; we will rebuild survivors only).
                var existingUnits = grid.GetComponentsInChildren<Game.Match.Units.UnitRuntime>(true);
                for (int i = 0; i < existingUnits.Length; i++)
                {
                    var u = existingUnits[i];
                    if (u != null)
                        Object.Destroy(u.gameObject);
                }

                // 2) Clear grid occupancy so dead units stop blocking tiles
                grid.ClearAll();

                // 3) Recreate grid + board units for survivors only
                foreach (var s in _pending)
                {
                    if (s.card == null) continue;

                    // Footprint size from the card data (same logic used by DraggableCard.GetFootprintInts)
                    int w = Mathf.Clamp(s.card.sizeW, 1, 4);
                    int h = Mathf.Clamp(s.card.sizeH, 1, 4);

                    // Derive the origin tile from the stored CardPhase world center.
                    // DraggableCard recorded originWorld as the CENTER of the whole footprint, so for tileSize ts:
                    //   s.originWorld.x / ts = origin.x + w/2
                    //   s.originWorld.z / ts = origin.y + h/2
                    // => origin = (center/ts - (w,h)/2)
                    float ts = grid.TileSize;
                    float fx = s.originWorld.x / ts;
                    float fz = s.originWorld.z / ts;
                    int ox = Mathf.RoundToInt(fx - w * 0.5f);
                    int oy = Mathf.RoundToInt(fz - h * 0.5f);
                    var tile = new Vector2Int(ox, oy);

                    // Reserve tiles if possible
                    if (grid.CanPlaceRect(tile, w, h))
                    {
                        grid.PlaceRect(tile, w, h);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[SurvivorRegistry] Tile {tile} can't place survivor {s.card.name} footprint {w}x{h} " +
                            "(occupied or out of bounds). Registering in placement registry anyway.");
                    }

                    // Same centering as DraggableCard: center of the whole footprint
                    Vector3 center =
                        grid.TileCenterToWorld(tile, 0f) +
                        new Vector3((w - 1) * 0.5f * ts, 0f,
                                    (h - 1) * 0.5f * ts);

                    // Register in placement registry so next BattlePhase includes this survivor
                    reg.Register(s.card, center, s.ownerId);

                    // Rebuild the CardPhase board unit visual, like DraggableCard does
                    var prefab = s.card.unitPrefab;
                    if (prefab != null)
                    {
                        var parent = grid.transform;
                        var go = Object.Instantiate(prefab, center, Quaternion.identity, parent);

                        // Snap vertically onto grid plane
                        float groundY = grid.transform.position.y;
                        var col = go.GetComponentInChildren<Collider>();
                        var rendUnit = (col == null) ? go.GetComponentInChildren<Renderer>() : null;
                        float halfH = 0.5f;
                        if (col != null) halfH = col.bounds.extents.y;
                        else if (rendUnit != null) halfH = rendUnit.bounds.extents.y;

                        var p = go.transform.position;
                        p.y = groundY + halfH;
                        go.transform.position = p;

                        // Initialize unit runtime from the card
                        var ur = go.GetComponent<Game.Match.Units.UnitRuntime>();
                        if (ur != null) ur.InitFrom(s.card);

                        // Optional but harmless: attach graveyard relay for future kills in CardPhase
                        var gy = go.GetComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
                        if (gy == null) gy = go.AddComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
                        gy.source = s.card;
                        gy.ownerId = s.ownerId;
                    }

                    if (s.ownerId == 0) a++;
                    else b++;
                }

                _pending.Clear();
                return (a, b);
            }
        }
        // In MatchRuntimeService : MonoBehaviour (field + init)
        public SurvivorRegistry survivors = new SurvivorRegistry();
    }
}
