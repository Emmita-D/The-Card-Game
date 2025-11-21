using Game.Match.Cards;
using Game.Match.Graveyard;     // for GraveyardCardHoverZoom
using UnityEngine;
using UnityEngine.UI;

public class DeckSearchVorgcoOption : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CardView cardView;   // CardView on this option (assigned in inspector)
    [SerializeField] private Button button;       // Button wrapping the option

    private CardSO card;
    private DeckSearchVorgcoPanel panel;

    public void Initialize(CardSO so, DeckSearchVorgcoPanel owner)
    {
        card = so;
        panel = owner;

        if (cardView != null && card != null)
        {
            // Show the card
            cardView.Bind(card);

            // Make sure we can't drag this card from the choose panel
            var drag = cardView.GetComponent<DraggableCard>();
            if (drag != null)
            {
                drag.enabled = false;
            }

            // IMPORTANT: disable hover zoom for this context
            var hoverZoom = cardView.GetComponent<GraveyardCardHoverZoom>();
            if (hoverZoom != null)
            {
                hoverZoom.enabled = false;
            }
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
    }

    private void OnClick()
    {
        if (panel != null && card != null)
        {
            panel.OnOptionChosen(card);
        }
    }
}
