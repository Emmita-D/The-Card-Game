using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;   // CardSO, CardInstance
using Game.Match.Mana;    // ManaPool

public class HandView : MonoBehaviour
{
    [Header("Prefabs & Parents")]
    [SerializeField] private Transform slotParent;
    [SerializeField] private CardView cardViewPrefab;

    [Header("Scene References")]
    [SerializeField] private ManaPool manaPool;    // <-- drag your ManaRoot’s ManaPool here

    public void SetHand(List<CardInstance> hand)
    {
        // clear old
        for (int i = slotParent.childCount - 1; i >= 0; i--)
            Destroy(slotParent.GetChild(i).gameObject);

        // spawn one CardView per card
        foreach (var ci in hand)
        {
            var view = Instantiate(cardViewPrefab, slotParent);
            view.name = $"Card_{ci.data.cardName}";
            view.Bind(ci.data);

            // wire components
            var drag = view.GetComponent<DraggableCard>();
            if (drag != null)
            {
                drag.instance = ci;
                if (manaPool == null) manaPool = FindObjectOfType<ManaPool>();
                if (manaPool != null) drag.SetManaPool(manaPool);
            }

            var afford = view.GetComponent<CardAffordability>();
            if (afford != null && manaPool != null)
                afford.SetPool(manaPool);
        }

        // optional: rebuild fan immediately if present
        var fan = GetComponentInChildren<FannedHandLayout>();
        if (fan) fan.RebuildImmediate();
    }

    // convenience for testing from CardSOs
    public void SetHandFromSOs(List<CardSO> cards, int ownerId = 0)
    {
        var list = new List<CardInstance>(cards.Count);
        foreach (var so in cards) list.Add(new CardInstance(so, ownerId));
        SetHand(list);
    }
}
