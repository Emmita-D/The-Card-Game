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
    /// - Load the BattleStage scene additively.
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

            // --- NEW: if a persistent CombatResolver exists, reset it so no stale state carries over.
            if (CombatResolver.Instance != null)
            {
                Debug.Log("[CardPhaseBattleLauncher] Found existing CombatResolver → ResetForNewBattle()");
                CombatResolver.Instance.ResetForNewBattle();
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

            // --- CHANGED: clear the registry AFTER the battle scene has finished loading.
            var op = SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Additive);
            if (op != null)
            {
                op.completed += _ =>
                {
                    // Safe to clear now; battle scene has read MatchRuntimeService.pendingBattle.
                    reg?.Clear();
                    Debug.Log("[CardPhaseBattleLauncher] Battle scene loaded → registry cleared.");
                };
            }
            else
            {
                // Fallback (shouldn’t happen, but keep old behavior if the async op is null)
                reg?.Clear();
            }
        }
    }
}
