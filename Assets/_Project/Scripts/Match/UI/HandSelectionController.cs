using Game.Core;
using Game.Match.Cards;   // CardInstance, CardSO, CardType
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles "pick N cards from hand as a cost" selection.
/// Works similarly to CardPhaseTargetSelectionController but for hand cards (DraggableCard).
/// </summary>
public class HandSelectionController : MonoBehaviour
{
    public static HandSelectionController Instance { get; private set; }

    // Selection state
    private bool isSelecting;
    private int requiredCount;
    private int sourceOwnerId;
    private Action<List<CardInstance>> onSelectionComplete;

    // Runtime lists (for highlight + cleanup only)
    private readonly List<DraggableCard> candidateCards = new List<DraggableCard>();
    private readonly List<CardInstance> pickedCards = new List<CardInstance>();

    /// <summary>
    /// True if we are currently in a "select cards from hand" mode.
    /// </summary>
    public bool IsSelecting => isSelecting;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[HandSelection] Multiple HandSelectionController instances found; keeping the first one.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Begin selecting UNIT cards from this player's hand to pay a cost.
    /// This does NOT apply the cost; it returns the chosen CardInstances via callback.
    /// </summary>
    public void BeginUnitCostSelection(int ownerIdForEffect, int count, Action<List<CardInstance>> callback)
    {
        if (isSelecting)
        {
            Debug.LogWarning("[HandSelection] BeginUnitCostSelection called while already selecting; cancelling previous selection.");
            CancelSelection();
        }

        if (count <= 0)
            count = 1;

        isSelecting = true;
        requiredCount = count;
        sourceOwnerId = ownerIdForEffect;
        onSelectionComplete = callback;

        candidateCards.Clear();
        pickedCards.Clear();

        // Find all DraggableCard components belonging to this owner,
        // that are Unit cards AND are actually part of a HandView (the player's hand).
        var allDraggables = FindObjectsOfType<DraggableCard>();
        foreach (var drag in allDraggables)
        {
            if (drag == null || drag.instance == null || drag.instance.data == null)
                continue;

            if (drag.instance.ownerId != ownerIdForEffect)
                continue;

            if (drag.instance.data.type != CardType.Unit)
                continue;

            // Only cards that live inside a HandView (i.e., in the hand UI)
            var hv = drag.GetComponentInParent<HandView>();
            if (hv == null)
                continue;

            candidateCards.Add(drag);

            var view = drag.GetComponent<CardView>();
            if (view != null)
            {
                // Clear any previous state just in case, then mark as candidate.
                view.ResetHandSelectionHighlight();
                view.SetHandSelectionHighlight(true);
            }
            else
            {
                Debug.LogWarning($"[HandSelection] Candidate card {drag.instance.data.cardName} has no CardView component.");
            }
        }

        Debug.Log($"[HandSelection] BeginUnitCostSelection owner={ownerIdForEffect}, required={requiredCount}, candidates={candidateCards.Count}");
    }

    /// <summary>
    /// Called by DraggableCard when the player clicks on a card while selection is active.
    /// </summary>
    public void TrySelectCard(DraggableCard clicked)
    {
        if (!isSelecting)
        {
            Debug.Log("[HandSelection] TrySelectCard called but selection is not active.");
            return;
        }

        if (clicked == null)
        {
            Debug.LogWarning("[HandSelection] TrySelectCard: clicked is null.");
            return;
        }

        if (clicked.instance == null || clicked.instance.data == null)
        {
            Debug.LogWarning("[HandSelection] TrySelectCard: clicked card has no instance/data.");
            return;
        }

        if (clicked.instance.ownerId != sourceOwnerId)
        {
            Debug.Log(
                $"[HandSelection] TrySelectCard: owner mismatch. clicked.owner={clicked.instance.ownerId}, sourceOwner={sourceOwnerId}"
            );
            return;
        }

        if (clicked.instance.data.type != CardType.Unit)
        {
            Debug.Log($"[HandSelection] TrySelectCard: clicked card {clicked.instance.data.cardName} is not a Unit.");
            return;
        }

        // Ensure the clicked card is actually in a hand (HandView),
        // to avoid selecting random Unit cards that happen to be on the board, etc.
        var hv = clicked.GetComponentInParent<HandView>();
        if (hv == null)
        {
            Debug.Log($"[HandSelection] TrySelectCard: clicked card {clicked.instance.data.cardName} is not under a HandView.");
            return;
        }

        var ci = clicked.instance;
        if (pickedCards.Contains(ci))
        {
            Debug.Log("[HandSelection] Card already selected; ignoring second click.");
            return;
        }

        pickedCards.Add(ci);
        var view = clicked.GetComponent<CardView>();
        if (view != null)
        {
            view.SetHandSelectionSelected(true);
        }
        else
        {
            Debug.LogWarning("[HandSelection] TrySelectCard: DraggableCard has no CardView for highlight.");
        }

        Debug.Log($"[HandSelection] Selected card {ci.data.cardName}. Count={pickedCards.Count}/{requiredCount}");

        if (pickedCards.Count >= requiredCount)
        {
            Debug.Log("[HandSelection] Required number of cards selected. Completing selection.");
            CompleteSelection();
        }
    }

    /// <summary>
    /// Cancels the current selection (if any) and clears all highlights.
    /// </summary>
    public void CancelSelection()
    {
        if (!isSelecting && candidateCards.Count == 0 && pickedCards.Count == 0)
            return;

        ClearHighlights();

        isSelecting = false;
        requiredCount = 0;
        sourceOwnerId = -1;
        pickedCards.Clear();
        candidateCards.Clear();
        onSelectionComplete = null;

        Debug.Log("[HandSelection] Selection cancelled.");
    }

    private void CompleteSelection()
    {
        var chosen = new List<CardInstance>(pickedCards);

        ClearHighlights();

        isSelecting = false;
        requiredCount = 0;
        sourceOwnerId = -1;
        pickedCards.Clear();
        candidateCards.Clear();

        var callback = onSelectionComplete;
        onSelectionComplete = null;

        if (callback != null)
        {
            try
            {
                callback(chosen);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HandSelection] Exception while invoking completion callback: {ex}");
            }
        }
    }

    private void ClearHighlights()
    {
        foreach (var drag in candidateCards)
        {
            if (drag == null)
                continue;

            var view = drag.GetComponent<CardView>();
            if (view != null)
            {
                view.ResetHandSelectionHighlight();
            }
        }
    }
}
