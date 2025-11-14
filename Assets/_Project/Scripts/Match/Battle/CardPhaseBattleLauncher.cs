using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Match.State;      // MatchRuntimeService, BattleDescriptor, BattleUnitSeed
using Game.Match.Cards;      // CardSO
using Game.Match.CardPhase;  // BattlePlacementRegistry

namespace Game.Match.Battle
{
    /// <summary>
    /// Called from the CardPhase scene (e.g. a UI button) to:
    /// - Build a BattleDescriptor from real placements (local),
    /// - Add debug units for the remote side (for now),
    /// - Store it in MatchRuntimeService.pendingBattle,
    /// - Either load the BattleStage scene once (first time) or reuse it on later rounds.
    /// </summary>
    public class CardPhaseBattleLauncher : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] private string battleSceneName = "BattleStage";

        [Header("Debug units (temporary until we hook real remote data)")]
        [Tooltip("Cards that will spawn as REMOTE units when the battle starts.")]
        [SerializeField] private CardSO[] debugRemoteUnits;

        public void StartBattle()
        {
            var match = MatchRuntimeService.Instance;
            if (match == null)
            {
                Debug.LogError("[CardPhaseBattleLauncher] MatchRuntimeService not found. " +
                               "Start from Boot so the MatchRuntime exists.");
                return;
            }

            if (string.IsNullOrWhiteSpace(battleSceneName))
            {
                Debug.LogError("[CardPhaseBattleLauncher] Battle scene name is empty.");
                return;
            }

            var desc = new BattleDescriptor();

            // REAL local units from card phase placements
            var reg = BattlePlacementRegistry.Instance;
            if (reg != null)
            {
                var localSeeds = reg.BuildSeedsForOwner(0);
                Debug.Log($"[CardPhaseBattleLauncher] Registry returned {localSeeds.Count} seeds for owner 0.");

                foreach (var s in localSeeds)
                    desc.localUnits.Add(s);
            }
            else
            {
                Debug.LogWarning("[CardPhaseBattleLauncher] No BattlePlacementRegistry in scene; local side will be empty.");
            }

            // REMOTE side – keep debug list for now
            if (debugRemoteUnits != null)
            {
                for (int i = 0; i < debugRemoteUnits.Length; i++)
                {
                    var so = debugRemoteUnits[i];
                    if (so == null) continue;

                    desc.remoteUnits.Add(new BattleUnitSeed
                    {
                        card = so,
                        ownerId = 1,
                        laneIndex = 0,
                        spawnOffset = i * 1.0f,
                        useExactPosition = false,
                        exactPosition = Vector3.zero
                    });
                }
            }

            match.pendingBattle = desc;
            match.lastBattleResult = null;

            Debug.Log($"[CardPhaseBattleLauncher] Starting battle: " +
                      $"{desc.localUnits.Count} local (real) vs {desc.remoteUnits.Count} remote (debug).");

            // --- NEW: If the BattleStage scene is already loaded, reuse it instead of loading again.
            if (BattleSceneController.Instance != null &&
                BattleSceneController.Instance.gameObject.scene.isLoaded)
            {
                Debug.Log("[CardPhaseBattleLauncher] Reusing existing BattleStage scene for new battle round.");

                BattleSceneController.Instance.BeginBattleRound(desc);

                // Safe to clear registry now; BattleSceneController works from the descriptor we passed.
                reg?.Clear();
                return;
            }

            // --- FIRST TIME: hand the descriptor to the Battle scene and load it additively.
            BattleSceneController.SetPendingDescriptor(desc);

            var op = SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Additive);
            if (op != null)
            {
                op.completed += _ =>
                {
                    // Safe to clear now; BattleSceneController.Start() has consumed the descriptor.
                    reg?.Clear();
                    Debug.Log("[CardPhaseBattleLauncher] Battle scene loaded → registry cleared.");
                };
            }
            else
            {
                // Fallback (shouldn’t happen, but preserve behavior)
                reg?.Clear();
            }
        }
    }
}
