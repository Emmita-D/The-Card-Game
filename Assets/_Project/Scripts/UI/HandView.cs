using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;   // CardSO, CardInstance

public class HandView : MonoBehaviour
{
    [Header("Prefabs & Parents")]
    [SerializeField] private Transform slotParent;      // container with Horizontal Layout
    [SerializeField] private CardView cardViewPrefab;   // your CardView_PF

    public void SetHand(List<CardInstance> hand)
    {
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
            }
        }
    }

    // convenience for testing from CardSOs
    public void SetHandFromSOs(List<CardSO> cards, int ownerId = 0)
    {
        var list = new List<CardInstance>(cards.Count);
        foreach (var so in cards) list.Add(new CardInstance(so, ownerId));
        SetHand(list);
    }
}
