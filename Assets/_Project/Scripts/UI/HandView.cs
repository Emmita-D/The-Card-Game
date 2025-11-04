using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;   // CardSO, CardInstance

public class HandView : MonoBehaviour
{
    [Header("Prefabs & Parents")]
    [SerializeField] private Transform slotParent;      // container with FannedHandLayout (no HLG/CSF)
    [SerializeField] private CardView cardViewPrefab;   // your CardView_PF

    // NEW: reference to the fan layout on slotParent
    [SerializeField] private FannedHandLayout fanLayout;

    public void SetHand(List<CardInstance> hand)
    {
        // (re)cache fanLayout if not wired in Inspector
        if (!fanLayout && slotParent) fanLayout = slotParent.GetComponent<FannedHandLayout>();

        // clear old
        for (int i = slotParent.childCount - 1; i >= 0; i--)
            Destroy(slotParent.GetChild(i).gameObject);

        // spawn one CardView per card
        foreach (var ci in hand)
        {
            var view = Instantiate(cardViewPrefab, slotParent);
            view.name = $"Card_{ci.data.GetType().Name}";
            view.Bind(ci.data);

            // wire draggable
            var drag = view.GetComponent<DraggableCard>();
            if (drag != null)
            {
                drag.instance = ci; // runtime link; grid & layer mask are configured on the prefab
                drag.handContainer = (RectTransform)slotParent;
            }
        }

        // PING: force recompute the fan this frame
        if (fanLayout) fanLayout.RebuildImmediate();
    }

    // convenience for testing from CardSOs
    public void SetHandFromSOs(List<CardSO> cards, int ownerId = 0)
    {
        var list = new List<CardInstance>(cards.Count);
        foreach (var so in cards) list.Add(new CardInstance(so, ownerId));
        SetHand(list);
    }
}
