using Game.Match.Cards;   // CardSO, CardInstance, CardType
using Game.Match.Mana;    // ManaPool
using System.Collections.Generic;
using UnityEngine;

public class HandView : MonoBehaviour
{
    [Header("Prefabs & Parents")]
    [SerializeField] private Transform slotParent;
    [SerializeField] private CardView cardViewPrefab;

    [Header("Scene References")]
    [SerializeField] private ManaPool manaPool;

    private readonly List<CardView> activeCards = new List<CardView>();

    public void SetHand(List<CardInstance> hand)
    {
        // Clear old cards in the slot parent
        if (slotParent != null)
        {
            for (int i = slotParent.childCount - 1; i >= 0; i--)
            {
                var child = slotParent.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        activeCards.Clear();

        if (hand == null || hand.Count == 0)
        {
            var emptyFan = GetComponentInChildren<FannedHandLayout>();
            if (emptyFan != null)
                emptyFan.RebuildImmediate();
            return;
        }

        if (manaPool == null)
            manaPool = FindObjectOfType<ManaPool>();

        // Build card views for each card in hand
        for (int i = 0; i < hand.Count; i++)
        {
            CardInstance ci = hand[i];
            if (ci == null || ci.data == null)
                continue;

            // Instantiate card view
            CardView view = Instantiate(cardViewPrefab, slotParent);
            view.name = $"Card_{ci.data.cardName}";

            // 1) Normal SO-based binding (frame, art, text, etc.)
            view.Bind(ci.data);

            // 2) Override stats using per-instance buffed values
            int finalAtk = ci.GetFinalAttack();
            int finalHp = ci.GetFinalHealth();
            view.OverrideStats(finalAtk, finalHp);

            activeCards.Add(view);

            // Draggable behaviour
            var drag = view.GetComponent<DraggableCard>();
            if (drag != null)
            {
                drag.instance = ci;
                if (manaPool != null)
                    drag.SetManaPool(manaPool);
            }

            // Affordability behaviour
            var afford = view.GetComponent<CardAffordability>();
            if (afford != null && manaPool != null)
                afford.SetPool(manaPool);
        }

        // Rebuild fan layout if present
        var fan = GetComponentInChildren<FannedHandLayout>();
        if (fan != null)
            fan.RebuildImmediate();
    }

    // Convenience for testing directly from CardSOs in editor
    public void SetHandFromSOs(List<CardSO> cards, int ownerId = 0)
    {
        var list = new List<CardInstance>(cards.Count);
        foreach (var so in cards)
        {
            if (so == null) continue;
            list.Add(new CardInstance(so, ownerId));
        }
        SetHand(list);
    }
}
