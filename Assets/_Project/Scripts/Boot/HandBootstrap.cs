using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards; // CardSO
// using ... (no other deps)

public class HandBootstrap : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public HandView handView;             // drag your HandPanel's HandView here
    public List<CardSO> openingCards;     // drag your CardSO assets here (order = left→right)
    public int ownerId = 0;

    void Start()
    {
        if (handView == null)
        {
            handView = FindObjectOfType<HandView>();
        }
        if (handView != null && openingCards != null && openingCards.Count > 0)
        {
            handView.SetHandFromSOs(openingCards, ownerId);
        }
        else
        {
            Debug.LogWarning("[HandBootstrap] Missing HandView or openingCards are empty.");
        }
    }
}
