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

        // NEW: allow card phase to dictate exact world position
        public bool useExactPosition;
        public Vector3 exactPosition;
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
        public BattleResult lastBattleResult; // filled when BattleStage finishes

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
    }
}
