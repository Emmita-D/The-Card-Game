using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;   // CardSO, CardInstance

public class HandView : MonoBehaviour
{
    [Header("Prefabs & Parents")]
    [SerializeField] private RectTransform slotParent;     // CardSlotContainer
    [SerializeField] private CardView cardViewPrefab;      // CardView_PF

    [Header("Layers")]
    [SerializeField] private RectTransform hoverLayer;     // <— drag Canvas/HandViewport/HoverLayer here in Inspector

    public void SetHand(List<CardInstance> hand)
    {
        // clear
        for (int i = slotParent.childCount - 1; i >= 0; i--)
            Destroy(slotParent.GetChild(i).gameObject);

        foreach (var ci in hand)
        {
            var view = Instantiate(cardViewPrefab, slotParent);
            view.name = $"Card_{ci.data.cardName}";
            view.Bind(ci.data);

            var drag = view.GetComponent<DraggableCard>();
            if (drag)
            {
                drag.instance = ci;
                drag.handContainer = slotParent;           // for SnapBack
            }

            var fx = view.GetComponent<CardHoverFX>();
            if (fx && hoverLayer) fx.InjectHoverLayer(hoverLayer); // <-- key line: ensures no fallback canvas
        }

        // optional: force a fresh fan layout if you're using it
        var fan = slotParent.GetComponentInParent<FannedHandLayout>();
        if (fan) fan.RebuildImmediate();
    }

    public void SetHandFromSOs(List<CardSO> cards, int ownerId = 0)
    {
        var list = new List<CardInstance>(cards.Count);
        foreach (var so in cards) list.Add(new CardInstance(so, ownerId));
        SetHand(list);
    }
}
