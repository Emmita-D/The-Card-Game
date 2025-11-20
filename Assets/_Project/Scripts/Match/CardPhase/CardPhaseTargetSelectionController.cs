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

        private bool isSelecting;
        private OnCallTargetingKind currentKind;
        private CardInstance sourceCard;
        private int sourceOwnerId;
        private int pendingSavageStacks;   // specific for our first use-case

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
        public void BeginOnCallSelection(CardInstance source, OnCallTargetingKind kind, int savageStacksToGive, Action<CardInstance, object> callback)
        {
            if (source == null || source.data == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] BeginOnCallSelection called with null source.");
                return;
            }

            if (kind == OnCallTargetingKind.None)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] BeginOnCallSelection called with kind=None; nothing to do.");
                return;
            }

            if (isSelecting)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Already selecting; ignoring new request.");
                return;
            }

            isSelecting = true;
            currentKind = kind;
            sourceCard = source;
            sourceOwnerId = source.ownerId;
            pendingSavageStacks = savageStacksToGive;
            onTargetChosen = callback;

            Debug.Log($"[CardPhaseTargetSelection] Enter selection mode. kind={kind}, stacks={savageStacksToGive}, source={source.data.cardName}");

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
            currentKind = OnCallTargetingKind.None;
            sourceCard = null;
            sourceOwnerId = -1;
            pendingSavageStacks = 0;
            onTargetChosen = null;
        }

        /// <summary>
        /// Called from CardPhaseSelectableUnit when the player clicks a unit in CardPhase.
        /// </summary>
        public void TrySelectTarget(object target)
        {
            if (!isSelecting)
                return;

            Debug.Log("[CardPhaseTargetSelection] TrySelectTarget called.");

            if (sourceCard == null || sourceCard.data == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] No valid sourceCard while selecting; aborting.");
                ClearState();
                return;
            }

            var selectable = target as CardPhaseSelectableUnit;
            if (selectable == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] TrySelectTarget called with non-CardPhaseSelectableUnit target.");
                return;
            }

            switch (currentKind)
            {
                case OnCallTargetingKind.ChosenFriendlySavageVorgco:
                    HandleChosenFriendlySavageVorgco(selectable);
                    break;

                default:
                    Debug.LogWarning($"[CardPhaseTargetSelection] Unsupported targeting kind {currentKind} in v1.");
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

        private bool IsValidChosenFriendlySavageVorgco(CardPhaseSelectableUnit selectable)
        {
            if (selectable == null)
                return false;

            // Must be same owner.
            if (selectable.OwnerId != sourceOwnerId)
            {
                Debug.Log("[CardPhaseTargetSelection] Clicked unit is not friendly; ignoring.");
                return false;
            }

            var targetCard = selectable.Card;
            if (targetCard == null)
            {
                Debug.LogWarning("[CardPhaseTargetSelection] Target has no CardSO; ignoring.");
                return false;
            }

            // Must be a Vorg'co.
            if (targetCard.race != Race.Vorgco)
            {
                Debug.Log("[CardPhaseTargetSelection] Target is not a Vorg'co; ignoring.");
                return false;
            }

            // Must be marked as part of the Savage archetype.
            if (!targetCard.isSavageArchetype)
            {
                Debug.Log("[CardPhaseTargetSelection] Target is not marked as Savage archetype; ignoring.");
                return false;
            }

            // Keep within same realm for containment / flavor.
            Realm sourceRealm = sourceCard.data.realm;
            if (targetCard.realm != sourceRealm)
            {
                Debug.Log("[CardPhaseTargetSelection] Target is not in the same realm as the source; ignoring.");
                return false;
            }

            return true;
        }

        // --- Highlight helpers ---

        private void HighlightValidTargetsForCurrentSelection()
        {
            ClearAllHighlights();

            if (!isSelecting)
                return;

            var all = FindObjectsOfType<CardPhaseSelectableUnit>();

            foreach (var selectable in all)
            {
                if (selectable == null)
                    continue;

                bool valid = false;

                switch (currentKind)
                {
                    case OnCallTargetingKind.ChosenFriendlySavageVorgco:
                        valid = IsValidChosenFriendlySavageVorgco(selectable);
                        break;
                }

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
