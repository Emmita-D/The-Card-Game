using Game.Core;
using Game.Match.Cards;   // CardInstance, CardSO, CardType
using System;
using System.Collections;
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

    // Coroutine that (re)builds candidates on the next frame, after the hand UI is rebuilt
    private Coroutine buildCandidatesCoroutine;

    /// <summary>
    /// Exposed so DraggableCard can block drag/plays while a hand selection is active.
    /// </summary>
    public bool IsSelecting => isSelecting;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[HandSelection] Multiple HandSelectionController instances found. Destroying this one.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Begin selection of exactly <paramref name="count"/> UNIT cards in hand
    /// for the given owner. When done, invokes <paramref name="callback"/> with
    /// the chosen CardInstances (or not at all if cancelled).
    /// </summary>
    public void BeginUnitCostSelection(int ownerIdForEffect, int count, Action<List<CardInstance>> callback)
    {
        // If we were already mid-build, stop that first.
        if (buildCandidatesCoroutine != null)
        {
            StopCoroutine(buildCandidatesCoroutine);
            buildCandidatesCoroutine = null;
        }

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

        // Important: do NOT build candidates immediately.
        // The spell card that triggered this selection is still in hand, and
        // DraggableCard.OnEndDrag will call ConsumeAndDestroy -> PushHandToView,
        // which rebuilds the entire hand UI this frame.
        // We wait one frame so HandView has finished rebuilding, then build
        // candidates against the final set of hand cards.
        buildCandidatesCoroutine = StartCoroutine(CoBuildCandidatesNextFrame());
    }

    /// <summary>
    /// Coroutine that waits one frame so the hand UI can rebuild,
    /// then finds candidates and applies visual highlights.
    /// </summary>
    private IEnumerator CoBuildCandidatesNextFrame()
    {
        // Wait one frame so:
        // - The spell card is removed from hand.
        // - HandView has called SetHand and rebuilt all DraggableCards.
        yield return null;

        buildCandidatesCoroutine = null;

        if (!isSelecting)
        {
            yield break;
        }

        candidateCards.Clear();

        // Find all DraggableCard instances currently in the scene that:
        // - belong to this owner
        // - live under a HandView (i.e., are actually in the hand UI)
        // Candidates are the subset that are Unit cards.
        var allDraggables = FindObjectsOfType<DraggableCard>();
        foreach (var drag in allDraggables)
        {
            if (drag == null || drag.instance == null || drag.instance.data == null)
                continue;

            var ci = drag.instance;
            var so = ci.data;

            if (ci.ownerId != sourceOwnerId)
                continue;

            // Only consider cards that live inside a HandView (i.e., in the hand UI)
            var hv = drag.GetComponentInParent<HandView>();
            if (hv == null)
                continue;

            // Only Unit cards are valid candidates
            if (so.type != CardType.Unit)
                continue;

            candidateCards.Add(drag);

            // Drive highlight exclusively through HandCardHighlight (NOT CardView).
            var fx = drag.GetComponent<HandCardHighlight>();
            if (fx != null)
            {
                fx.Clear();
                fx.ShowCandidate();
            }
            else
            {
                Debug.LogWarning($"[HandSelection] Candidate card {so.cardName} has no HandCardHighlight component.");
            }
        }

        Debug.Log($"[HandSelection] BeginUnitCostSelection (candidates built) owner={sourceOwnerId}, required={requiredCount}, candidates={candidateCards.Count}");

        // If no candidates, immediately cancel (and do NOT call the callback).
        if (candidateCards.Count == 0)
        {
            Debug.LogWarning("[HandSelection] No candidate Unit cards found for hand selection; cancelling.");
            CancelSelection();
        }
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

        var ci = clicked.instance;
        var so = ci.data;

        // Must belong to the same owner as the effect
        if (ci.ownerId != sourceOwnerId)
        {
            Debug.LogWarning(
                $"[HandSelection] TrySelectCard: owner mismatch. clicked.owner={ci.ownerId}, sourceOwner={sourceOwnerId}");
            return;
        }

        // Only unit cards may be used to pay this cost.
        if (so.type != CardType.Unit)
        {
            Debug.Log($"[HandSelection] TrySelectCard: clicked card {so.cardName} is not a Unit.");
            return;
        }

        var fx = clicked.GetComponent<HandCardHighlight>();

        // 🔁 TOGGLE BEHAVIOUR:
        // If this card is already selected, unselect it instead of ignoring.
        if (pickedCards.Contains(ci))
        {
            pickedCards.Remove(ci);

            if (fx != null)
            {
                // Go back to "candidate" visuals (yellow border, etc.)
                fx.ShowCandidate();
            }
            else
            {
                Debug.LogWarning("[HandSelection] TrySelectCard: DraggableCard has no HandCardHighlight for deselect.");
            }

            Debug.Log($"[HandSelection] Deselected card {so.cardName}. Count={pickedCards.Count}/{requiredCount}");
            return;
        }

        // Prefer cards we previously detected as candidates,
        // but if for some reason this DraggableCard was not in that list,
        // still allow it (owner + type already validated).
        if (candidateCards.Count > 0 && !candidateCards.Contains(clicked))
        {
            Debug.Log($"[HandSelection] TrySelectCard: clicked card {so.cardName} was not in candidate list; accepting anyway (fallback).");
        }

        // ✅ SELECT PATH
        pickedCards.Add(ci);

        if (fx != null)
        {
            fx.ShowSelected();
        }
        else
        {
            Debug.LogWarning("[HandSelection] TrySelectCard: DraggableCard has no HandCardHighlight for highlight.");
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
    /// Does NOT invoke the completion callback.
    /// </summary>
    public void CancelSelection()
    {
        if (buildCandidatesCoroutine != null)
        {
            StopCoroutine(buildCandidatesCoroutine);
            buildCandidatesCoroutine = null;
        }

        if (!isSelecting && candidateCards.Count == 0 && pickedCards.Count == 0)
            return;

        Debug.Log("[HandSelection] CancelSelection called.");

        ClearHighlights();

        isSelecting = false;
        requiredCount = 0;
        sourceOwnerId = -1;

        candidateCards.Clear();
        pickedCards.Clear();

        // Do not call the completion callback on cancel.
        onSelectionComplete = null;
    }

    /// <summary>
    /// Internal: completes selection, clears state, and invokes callback with chosen cards.
    /// </summary>
    private void CompleteSelection()
    {
        if (buildCandidatesCoroutine != null)
        {
            StopCoroutine(buildCandidatesCoroutine);
            buildCandidatesCoroutine = null;
        }

        if (!isSelecting)
        {
            Debug.LogWarning("[HandSelection] CompleteSelection called while not selecting.");
            return;
        }

        isSelecting = false;

        // Build chosen list (copy, in order of selection)
        var chosen = new List<CardInstance>(pickedCards);

        ClearHighlights();

        requiredCount = 0;
        sourceOwnerId = -1;
        candidateCards.Clear();
        pickedCards.Clear();

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

    /// <summary>
    /// Clear all visual highlights on candidate cards.
    /// </summary>
    private void ClearHighlights()
    {
        foreach (var drag in candidateCards)
        {
            if (drag == null)
                continue;

            var fx = drag.GetComponent<HandCardHighlight>();
            if (fx != null)
            {
                fx.Clear();
            }
        }
    }
}
