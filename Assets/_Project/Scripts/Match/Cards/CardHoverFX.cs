using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class CardHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale & Lift")]
    [SerializeField] float baseScale = 0.5f;           // set by fan
    [SerializeField] float hoverMultiplier = 1.45f;
    [SerializeField] float liftY = 72f;
    [SerializeField] float animTime = 0.10f;

    [Header("Optional visuals")]
    [SerializeField] Image hoverGlow;                  // e.g., CardView/HoverGlow

    RectTransform rt;
    HandCardAnchor anchor;
    FannedHandLayout fan;

    // Overlay (no reparenting)
    Canvas overlayCanvas;
    GraphicRaycaster raycaster;

    bool hovering;
    bool dragLock;
    bool canHover = true;
    int baseOrder = 0;

    public bool IsHovering => hovering;

    void Awake()
    {
        rt = (RectTransform)transform;
        anchor = GetComponent<HandCardAnchor>() ?? gameObject.AddComponent<HandCardAnchor>();
        fan = GetComponentInParent<FannedHandLayout>();

        overlayCanvas = GetComponent<Canvas>();
        if (!overlayCanvas) overlayCanvas = gameObject.AddComponent<Canvas>();
        raycaster = GetComponent<GraphicRaycaster>();
        if (!raycaster) raycaster = gameObject.AddComponent<GraphicRaycaster>();

        overlayCanvas.overrideSorting = false;
        overlayCanvas.sortingOrder = 0;
    }

    void OnEnable()
    {
        if (hoverGlow) hoverGlow.enabled = false;
        hovering = false;
        dragLock = false;
        canHover = true;

        overlayCanvas.overrideSorting = false;
        overlayCanvas.sortingOrder = baseOrder;
    }

    // --- Compatibility with older HandView code (no-op now) ---
    public void InjectHoverLayer(RectTransform _ignored) { /* intentionally empty */ }

    // --- API used by the fan ---
    public void SetBaseScale(float s)
    {
        baseScale = s;
        if (!hovering && !dragLock) rt.localScale = Vector3.one * baseScale;
    }

    public void SetBaseRenderOrder(int order)
    {
        baseOrder = order;
        if (!hovering && !overlayCanvas.overrideSorting)
            overlayCanvas.sortingOrder = baseOrder;
    }

    // --- API used by DraggableCard ---
    public void BeginDragLock()
    {
        dragLock = true;
        CancelHoverNow();      // ensure we’re not in hovered pose
        EnableOverlay(true);   // keep dragged card on top
    }

    public void EndDragUnlock(float dropOverlayDelay = 0.08f)
    {
        StartCoroutine(CoEndDrag(dropOverlayDelay));
    }

    IEnumerator CoEndDrag(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        dragLock = false;
        EnableOverlay(false);
        fan?.RebuildImmediate();
    }

    public void ForceToBasePose()
    {
        StopAllCoroutines();
        hovering = false;
        canHover = true;

        anchor.ApplyTo(rt);
        if (hoverGlow) hoverGlow.enabled = false;
        EnableOverlay(false);
    }

    // --- Pointer handlers ---
    public void OnPointerEnter(PointerEventData e)
    {
        if (dragLock || !canHover) return;
        hovering = true;

        EnableOverlay(true);
        if (hoverGlow) hoverGlow.enabled = true;

        StopAllCoroutines();
        StartCoroutine(AnimateTo(anchor.basePos + Vector2.up * liftY,
                                 Quaternion.identity,                 // straighten on hover
                                 baseScale * hoverMultiplier));

        fan?.OnCardHoverEnter(this);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (dragLock) return;
        hovering = false;
        if (hoverGlow) hoverGlow.enabled = false;

        StopAllCoroutines();
        StartCoroutine(ReturnToBase());
    }

    // --- Anim helpers ---
    IEnumerator AnimateTo(Vector2 targetPos, Quaternion targetRot, float targetScale)
    {
        Vector2 startPos = rt.anchoredPosition;
        Quaternion startRot = rt.localRotation;
        float startS = rt.localScale.x;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);

            rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, k);
            rt.localRotation = Quaternion.Slerp(startRot, targetRot, k);
            float s = Mathf.Lerp(startS, targetScale, k);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        rt.anchoredPosition = targetPos;
        rt.localRotation = targetRot;
        rt.localScale = new Vector3(targetScale, targetScale, 1f);
    }

    IEnumerator ReturnToBase()
    {
        canHover = false;

        Vector2 startPos = rt.anchoredPosition;
        Quaternion startRot = rt.localRotation;
        float startS = rt.localScale.x;

        Vector2 targetPos = anchor.basePos;
        Quaternion targetRot = anchor.baseRot;
        float targetS = anchor.baseScale;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);

            rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, k);
            rt.localRotation = Quaternion.Slerp(startRot, targetRot, k);
            float s = Mathf.Lerp(startS, targetS, k);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        anchor.ApplyTo(rt);
        EnableOverlay(false);
        fan?.OnCardHoverExit(this);   // tell the fan hover ended
        fan?.RebuildImmediate();

        canHover = true;
    }

    void CancelHoverNow()
    {
        StopAllCoroutines();
        hovering = false;
        if (hoverGlow) hoverGlow.enabled = false;
        anchor.ApplyTo(rt);
        EnableOverlay(false);
        fan?.RebuildImmediate();
    }

    void EnableOverlay(bool enable)
    {
        overlayCanvas.overrideSorting = enable;
        overlayCanvas.sortingOrder = enable ? (baseOrder + 1000) : baseOrder;
    }
}
