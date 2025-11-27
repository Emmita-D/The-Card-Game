using System;
using UnityEngine;
using Game.Match.Cards;
using Game.Core;
using Game.Match.Units;

namespace Game.Match.CardPhase
{
    /// <summary>
    /// v1 CardPhase target selection controller.
    /// Responsibilities:
    /// - Knows when we are in "choose a unit" mode.
    /// - Knows which card/effect is requesting a target.
    /// - Validates whether a clicked unit is a legal target.
    /// - Applies simple effects like: "When called, give X Savage to a chosen friendly Savage Vorg'co".
    /// - Highlights valid targets while selection is active.
    ///
    /// This is CardPhase-only and does NOT know about BattleStage.
    /// </summary>
    public class CardPhaseTargetSelectionController : MonoBehaviour
    {
        public static CardPhaseTargetSelectionController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Multiple instances detected; destroying the new one.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // --- Selection state ---

        private enum SelectionMode
        {
            None = 0,
            OnCall = 1,
            TokenCost = 2,
        }

        private SelectionMode selectionMode = SelectionMode.None;

        private bool isSelecting;
        private OnCallTargetingKind currentKind;
        private CardInstance sourceCard;
        private int sourceOwnerId;
        private int pendingSavageStacks;   // specific for our first OnCall use-case

        // v1 token-cost selection state (spend stacks from a chosen unit).
        private FieldTokenCostKind tokenCostKind = FieldTokenCostKind.None;
        private int tokenCostRequiredStacks;

        /// <summary>
        /// Optional callback invoked when a valid target is chosen.
        /// For v1 Savage-on-call we don't need it, but it's here for future flexibility.
        /// </summary>
        private Action<CardInstance, object> onTargetChosen;

        /// <summary>
        /// True if we're currently waiting for the player to click a target on the CardPhase board.
        /// </summary>
        public bool IsSelecting => isSelecting;

        /// <summary>
        /// Begin a selection for a specific OnCall targeting mode.
        /// This does NOT yet apply any effect; it just enters selection mode.
        ///
        /// v1: used for "When called, give X Savage tokens to a chosen friendly Savage Vorg'co unit".
        /// </summary>
        public void BeginOnCallSelection(
            CardInstance source,
            OnCallTargetingKind kind,
            int savageStacksToGive,
            Action<CardInstance, object> callback)
        {
            if (source == null || source.data == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] BeginOnCallSelection called with null source.");
                return;
            }

            if (isSelecting)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Already selecting; ignoring BeginOnCallSelection.");
                return;
            }

            isSelecting = true;
            selectionMode = SelectionMode.OnCall;

            currentKind = kind;
            sourceCard = source;
            sourceOwnerId = source.ownerId;
            pendingSavageStacks = savageStacksToGive;
            onTargetChosen = callback;

            Debug.Log(
                $"[CardPhaseTargetSelection] Enter selection mode (OnCall): kind={kind}, stacks={savageStacksToGive}, source={source.data.cardName}, owner={sourceOwnerId}"
            );

            HighlightValidTargetsForCurrentSelection();
        }

        /// <summary>
        /// Begin a selection to pay a token cost from a chosen friendly unit on the CardPhase board.
        /// This does not pay any cost by itself; it just enters selection mode and will invoke the
        /// callback with the chosen target when the player clicks a valid unit.
        ///
        /// v1: we only support FieldTokenCostKind.SavageStacks and a single chosen unit.
        /// </summary>
        public void BeginTokenCostSelection(
            CardInstance source,
            FieldTokenCostKind costKind,
            int requiredStacks,
            Action<CardInstance, object> callback)
        {
            if (source == null || source.data == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] BeginTokenCostSelection called with null source.");
                return;
            }

            if (costKind == FieldTokenCostKind.None || requiredStacks <= 0)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] BeginTokenCostSelection called with invalid costKind/requiredStacks.");
                return;
            }

            if (isSelecting)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Already selecting; ignoring new token-cost request.");
                return;
            }

            isSelecting = true;
            selectionMode = SelectionMode.TokenCost;

            currentKind = OnCallTargetingKind.None;
            sourceCard = source;
            sourceOwnerId = source.ownerId;
            pendingSavageStacks = 0; // not used for token-cost selection

            tokenCostKind = costKind;
            tokenCostRequiredStacks = requiredStacks;
            onTargetChosen = callback;

            Debug.Log($"[CardPhaseTargetSelection] Enter token-cost selection mode: costKind={costKind}, requiredStacks={requiredStacks}, source={source.data.cardName}");

            HighlightValidTargetsForCurrentSelection();
        }

        /// <summary>
        /// Cancels the current selection (e.g., player pressed ESC / right-click).
        /// The original Call still happened; this just means no target-based bonus is applied.
        /// </summary>
        public void CancelSelection()
        {
            if (!isSelecting)
                return;

            Debug.Log("[CardPhaseTargetSelection] Selection cancelled.");
            ClearState();
        }

        private void ClearState()
        {
            ClearAllHighlights();

            isSelecting = false;
            selectionMode = SelectionMode.None;

            currentKind = OnCallTargetingKind.None;
            sourceCard = null;
            sourceOwnerId = -1;
            pendingSavageStacks = 0;

            tokenCostKind = FieldTokenCostKind.None;
            tokenCostRequiredStacks = 0;

            onTargetChosen = null;
        }

        /// <summary>
        /// Called from CardPhaseSelectableUnit when the player clicks a unit in CardPhase.
        /// </summary>
        public void TrySelectTarget(object target)
        {
            if (!isSelecting)
                return;

            var selectable = target as CardPhaseSelectableUnit;
            if (selectable == null)
                return;

            switch (selectionMode)
            {
                case SelectionMode.OnCall:
                    switch (currentKind)
                    {
                        case OnCallTargetingKind.ChosenFriendlySavageVorgco:
                            HandleChosenFriendlySavageVorgco(selectable);
                            break;

                        default:
                            Debug.LogWarning($"[CardPhaseTargetSelection] Unsupported OnCall targeting kind {currentKind}.");
                            break;
                    }
                    break;

                case SelectionMode.TokenCost:
                    HandleTokenCostSelection(selectable);
                    break;

                default:
                    Debug.LogWarning("[CardPhaseTargetSelection] TrySelectTarget called while selectionMode=None; clearing state.");
                    ClearState();
                    break;
            }
        }

        /// <summary>
        /// v1 implementation: "When called, give X Savage tokens to a chosen friendly Savage Vorg'co unit."
        /// Rules:
        /// - Same owner as the source card.
        /// - Race = Vorgco.
        /// - isSavageArchetype = true.
        /// - Same realm as the source card (keeps effects contained).
        /// </summary>
        private void HandleChosenFriendlySavageVorgco(CardPhaseSelectableUnit selectable)
        {
            if (!IsValidChosenFriendlySavageVorgco(selectable))
            {
                // Invalid click; keep selection active, do not clear highlights.
                return;
            }

            var runtime = selectable.Runtime;
            if (runtime == null || runtime.StatusController == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Target unit has no UnitRuntime or StatusController; cannot apply Savage.");
                ClearState();
                return;
            }

            if (pendingSavageStacks <= 0)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] pendingSavageStacks <= 0; nothing to apply.");
                ClearState();
                return;
            }

            runtime.StatusController.AddSavageStacks(pendingSavageStacks);
            int totalStacks = runtime.StatusController.GetSavageStacks();
            float dmgMult = runtime.GetDamageDealtMultiplier();

            Debug.Log(
                $"[Savage] {runtime.displayName} received {pendingSavageStacks} Savage from on-call targeting " +
                $"(now {totalStacks} stacks, dmgMult={dmgMult:F2})."
            );

            // If a callback was provided, invoke it too.
            if (onTargetChosen != null)
            {
                onTargetChosen.Invoke(sourceCard, selectable);
            }

            ClearState();
        }

        /// <summary>
        /// v1 token-cost selection handler.
        /// It only validates that the clicked unit is a valid token-cost candidate and then
        /// notifies the caller via onTargetChosen. Actual token spending is handled by the effect code.
        /// </summary>
        private void HandleTokenCostSelection(CardPhaseSelectableUnit selectable)
        {
            if (!IsValidTokenCostCandidate(selectable))
            {
                // Invalid click; keep selection active and highlights as-is.
                return;
            }

            Debug.Log(
                $"[CardPhaseTargetSelection] Token-cost selection accepted: target={selectable.name}, costKind={tokenCostKind}, requiredStacks={tokenCostRequiredStacks}."
            );

            onTargetChosen?.Invoke(sourceCard, selectable);

            ClearState();
        }

        /// <summary>
        /// v1: treat ANY selectable unit as a valid target for the OnCall effect
        /// "chosen friendly Savage Vorg'co".
        ///
        /// This restores the ability to target units that may have a weird OwnerId,
        /// including ones summoned on previous turns. We can later tighten this again
        /// (e.g. require same owner, isSavageArchetype, race=Vorgco) once owner
        /// assignment is fully audited.
        /// </summary>
        /// <summary>
        /// Valid target for "ChosenFriendlySavageVorgco":
        /// - Same owner as the source card.
        /// - CardSO is marked as Savage archetype.
        /// - CardSO race is Vorgco.
        /// </summary>
        private bool IsValidChosenFriendlySavageVorgco(CardPhaseSelectableUnit selectable)
        {
            if (selectable == null)
                return false;

            // 1) Same owner
            if (selectable.OwnerId != sourceOwnerId)
            {
                // Uncomment if you need to debug owner issues:
                // Debug.Log($"[CardPhaseTargetSelection] Reject {selectable.name}: owner={selectable.OwnerId}, sourceOwner={sourceOwnerId}");
                return false;
            }

            // 2) Need access to the card data (CardSO) behind this unit.
            var card = selectable.Card; // relies on CardPhaseSelectableUnit exposing CardSO
            if (card == null)
            {
                // Debug.Log($"[CardPhaseTargetSelection] Reject {selectable.name}: no CardSO assigned.");
                return false;
            }

            // 3) Must be Savage archetype
            if (!card.isSavageArchetype)
            {
                // Debug.Log($"[CardPhaseTargetSelection] Reject {selectable.name}: not Savage archetype.");
                return false;
            }

            // 4) Must be race Vorgco
            if (card.race != Race.Vorgco)
            {
                // Debug.Log($"[CardPhaseTargetSelection] Reject {selectable.name}: race={card.race}, expected=Vorgco.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// v1 token-cost validator: for now we only support FieldTokenCostKind.SavageStacks
        /// and require:
        /// - same owner as the sourceCard,
        /// - unit has UnitRuntime + StatusController,
        /// - StatusController.HasAtLeastSavageStacks(tokenCostRequiredStacks).
        /// </summary>
        private bool IsValidTokenCostCandidate(CardPhaseSelectableUnit selectable)
        {
            if (selectable == null)
                return false;

            if (selectionMode != SelectionMode.TokenCost)
                return false;

            if (tokenCostKind == FieldTokenCostKind.None)
                return false;

            if (tokenCostRequiredStacks <= 0)
                return false;

            // For now, we only allow spending tokens from friendly units.
            if (selectable.OwnerId != sourceOwnerId)
            {
                return false;
            }

            var runtime = selectable.Runtime;
            if (runtime == null || runtime.StatusController == null)
            {
                return false;
            }

            switch (tokenCostKind)
            {
                case FieldTokenCostKind.SavageStacks:
                    return runtime.StatusController.HasAtLeastSavageStacks(tokenCostRequiredStacks);

                default:
                    // Unknown token-cost kind; treat as invalid.
                    return false;
            }
        }

        // --- Highlight helpers ---

        private void HighlightValidTargetsForCurrentSelection()
        {
            ClearAllHighlights();

            if (!isSelecting)
                return;

            var all = FindObjectsOfType<CardPhaseSelectableUnit>();

            Debug.Log(
                $"[CardPhaseTargetSelection] HighlightValidTargets: mode={selectionMode}, totalSelectableUnits={all.Length}, sourceOwner={sourceOwnerId}"
            );

            foreach (var selectable in all)
            {
                if (selectable == null)
                    continue;

                bool valid = false;

                switch (selectionMode)
                {
                    case SelectionMode.OnCall:
                        // For OnCall we currently use the generic "any unit" filter.
                        valid = IsValidChosenFriendlySavageVorgco(selectable);
                        break;

                    case SelectionMode.TokenCost:
                        valid = IsValidTokenCostCandidate(selectable);
                        break;

                    default:
                        valid = false;
                        break;
                }

                Debug.Log(
                    $"[CardPhaseTargetSelection]   candidate={selectable.name}, ownerId={selectable.OwnerId}, valid={valid}, mode={selectionMode}"
                );

                selectable.SetHighlight(valid);
            }
        }

        private void ClearAllHighlights()
        {
            var all = FindObjectsOfType<CardPhaseSelectableUnit>();
            foreach (var selectable in all)
            {
                if (selectable == null)
                    continue;

                selectable.SetHighlight(false);
            }
        }

        // Helper for future: expose the pending Savage info for effect code.
        public int GetPendingSavageStacks() => pendingSavageStacks;
        public OnCallTargetingKind GetCurrentKind() => currentKind;
    }
}
