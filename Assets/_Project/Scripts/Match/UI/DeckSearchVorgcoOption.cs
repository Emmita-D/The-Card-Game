using Game.Match.Cards;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DeckSearchVorgcoOption : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private CardView cardView;          // CardView under this option

    [Header("Selection Visuals")]
    [SerializeField] private Image selectionOutline;     // optional; purely visual

    private CardSO card;
    private DeckSearchVorgcoPanel panel;

    private bool isSelected;
    private Vector3 originalScale;

    // Invisible overlay that actually receives the click
    private Image clickArea;

    public CardSO Card => card;

    private void Awake()
    {
        originalScale = transform.localScale;

        if (cardView == null)
            cardView = GetComponentInChildren<CardView>(true);

        if (cardView == null)
        {
            Debug.LogError($"[DeckSearchVorgcoOption] No CardView found under {name}. Clicks will not work.");
            return;
        }

        EnsureClickAreaOverlay();
        DisableOtherRaycastTargets();

        ApplySelectedVisual(false);

        Debug.Log($"[DeckSearchVorgcoOption] Awake on {name}. cardView={(cardView != null)} clickArea={(clickArea != null)}");
    }

    /// <summary>
    /// Creates or reuses a child 'ClickArea' object that exactly covers the CardView rect
    /// and is the ONLY raycast target for this option.
    /// </summary>
    private void EnsureClickAreaOverlay()
    {
        RectTransform cardRT = cardView.GetComponent<RectTransform>();

        // Try to find an existing child named "ClickArea"
        Transform existing = transform.Find("ClickArea");
        RectTransform areaRT;

        if (existing != null)
        {
            areaRT = existing as RectTransform;
            clickArea = existing.GetComponent<Image>();
            if (clickArea == null)
                clickArea = existing.gameObject.AddComponent<Image>();
        }
        else
        {
            // Create a new child overlay
            var go = new GameObject("ClickArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling(); // ensure overlay is on top for raycasts

            areaRT = go.GetComponent<RectTransform>();
            clickArea = go.GetComponent<Image>();
        }

        // Make the overlay match the CardView rect
        areaRT.anchorMin = cardRT.anchorMin;
        areaRT.anchorMax = cardRT.anchorMax;
        areaRT.pivot = cardRT.pivot;
        areaRT.anchoredPosition = cardRT.anchoredPosition;
        areaRT.sizeDelta = cardRT.sizeDelta;

        // Invisible but raycastable
        clickArea.color = new Color(1f, 1f, 1f, 0f);
        clickArea.raycastTarget = true;
    }

    /// <summary>
    /// Turn OFF raycasts on every other Graphic under this option so only ClickArea catches clicks.
    /// </summary>
    private void DisableOtherRaycastTargets()
    {
        var graphics = GetComponentsInChildren<Graphic>(true);
        foreach (var g in graphics)
        {
            if (g == null)
                continue;

            if (clickArea != null && g.gameObject == clickArea.gameObject)
                continue; // keep overlay active

            g.raycastTarget = false;
        }
    }

    public void Initialize(CardSO so, DeckSearchVorgcoPanel owner)
    {
        card = so;
        panel = owner;

        if (cardView != null && so != null)
        {
            // For deck search we bind the SO directly (no CardInstance)
            cardView.Bind(so);
        }

        // Reset selection state whenever we (re)initialize
        isSelected = false;
        ApplySelectedVisual(false);
    }

    /// <summary>
    /// Called when the user clicks anywhere on the ClickArea (i.e., anywhere on the card).
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (card != null)
        {
            Debug.Log($"[DeckSearchVorgcoOption] Click on {card.cardName}");
        }

        if (panel != null)
        {
            panel.OnOptionClicked(this);
        }
    }

    /// <summary>
    /// Called by the panel to update selection state in multi-select modes.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (isSelected == selected)
            return;

        isSelected = selected;
        ApplySelectedVisual(selected);
    }

    private void ApplySelectedVisual(bool selected)
    {
        // Simple default: slight scale + optional outline toggle.
        transform.localScale = selected ? originalScale * 1.05f : originalScale;

        if (selectionOutline != null)
        {
            selectionOutline.enabled = selected;
        }
    }
}
