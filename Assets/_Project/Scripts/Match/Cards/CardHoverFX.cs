using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class CardHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale & Lift")]
    [SerializeField] float baseScale = 0.5f;
    [SerializeField] float hoverMultiplier = 1.45f;
    [SerializeField] float liftY = 72f;
    [SerializeField] float animTime = 0.08f;

    [Header("Optional visuals")]
    [SerializeField] Image hoverGlow; // drag your glow here (Raycast Target OFF)

    [Header("Layers")]
    [SerializeField] RectTransform hoverLayer; // assign "HoverLayer" here (or auto-found)

    RectTransform rt;
    Vector2 basePos;
    int baseSibling;
    RectTransform originalParent;
    bool hovering;
    DraggableCard drag;

    public bool IsHovering => hovering;

    void Awake()
    {
        rt = (RectTransform)transform;
        drag = GetComponent<DraggableCard>();
        if (hoverLayer == null)
        {
            var canv = GetComponentInParent<Canvas>();
            if (canv != null)
            {
                var t = canv.transform.Find("HoverLayer");
                if (t) hoverLayer = (RectTransform)t;
            }
        }
        if (!hoverLayer && UIHoverLayer.Instance)
            hoverLayer = UIHoverLayer.Instance;

    }

    void OnEnable()
    {
        rt.localScale = Vector3.one * baseScale;
        if (hoverGlow) hoverGlow.enabled = false;
    }

    public void SetBaseScale(float s)
    {
        baseScale = s;
        if (!hovering) rt.localScale = Vector3.one * baseScale;
    }

    // exposed so DraggableCard can use the same behavior while dragging
    public void AttachToHoverLayer()
    {
        if (!hoverLayer) return;
        if (!originalParent) originalParent = (RectTransform)transform.parent;
        baseSibling = transform.GetSiblingIndex();
        transform.SetParent(hoverLayer, worldPositionStays: true);
        transform.SetAsLastSibling(); // render above hand
    }

    public void ReturnToOriginalParent()
    {
        if (!originalParent) return;
        transform.SetParent(originalParent, worldPositionStays: true);
        transform.SetSiblingIndex(baseSibling);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (drag != null && drag.IsDragging) return;
        hovering = true;
        basePos = rt.anchoredPosition;

        AttachToHoverLayer();

        if (hoverGlow) hoverGlow.enabled = true;
        StopAllCoroutines();
        StartCoroutine(Animate(basePos + Vector2.up * liftY, baseScale * hoverMultiplier));
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (drag != null && drag.IsDragging) return;
        hovering = false;

        if (hoverGlow) hoverGlow.enabled = false;
        StopAllCoroutines();
        StartCoroutine(Animate(basePos, baseScale));

        ReturnToOriginalParent();
    }

    IEnumerator Animate(Vector2 targetPos, float targetScale)
    {
        float t = 0f;
        Vector2 startPos = rt.anchoredPosition;
        float startScale = rt.localScale.x;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, k);
            float s = Mathf.Lerp(startScale, targetScale, k);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.anchoredPosition = targetPos;
        rt.localScale = new Vector3(targetScale, targetScale, 1f);
    }

    // kept for API compatibility
    public void SetBaseRenderOrder(int order) { /* not needed with sibling layering */ }
}
