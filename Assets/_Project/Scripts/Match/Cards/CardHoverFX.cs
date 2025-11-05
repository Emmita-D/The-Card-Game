using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class CardHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale & Lift")]
    [SerializeField] float baseScale = 0.5f;          // set by FannedHandLayout
    [SerializeField] float hoverMultiplier = 1.45f;
    [SerializeField] float liftY = 72f;
    [SerializeField] float animTime = 0.10f;

    RectTransform rt;
    HandCardAnchor anchor;
    FannedHandLayout fan;

    bool hovering;
    bool dragLock;
    bool canHover = true;
    int baseOrder;                 // kept for compatibility with FannedHandLayout
    int origSibling = -1;          // sibling index to restore on exit/end drag

    public bool IsHovering => hovering;

    void Awake()
    {
        rt = (RectTransform)transform;
        anchor = GetComponent<HandCardAnchor>() ?? gameObject.AddComponent<HandCardAnchor>();
        fan = GetComponentInParent<FannedHandLayout>();
    }

    void OnEnable()
    {
        hovering = false;
        dragLock = false;
        canHover = true;
        origSibling = -1;
    }

    // --- compatibility API used elsewhere ---
    public void InjectHoverLayer(RectTransform _ignored) { }   // no-op
    public void SetMasked(bool _masked) { }                    // no-op

    public void SetBaseScale(float s)
    {
        baseScale = s;
        if (!hovering && !dragLock) rt.localScale = Vector3.one * baseScale;
    }
    public void SetBaseRenderOrder(int order) { baseOrder = order; } // no per-card canvas

    public void BeginDragLock()
    {
        dragLock = true;
        CancelHoverNow();
        // keep dragged card on top within the SAME canvas to avoid fringe
        if (origSibling < 0) origSibling = transform.GetSiblingIndex();
        transform.SetAsLastSibling();
    }

    public void EndDragUnlock(float _delay = 0.08f)
    {
        StartCoroutine(CoEndDrag(_delay));
    }
    IEnumerator CoEndDrag(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        dragLock = false;
        RestoreSibling();
        fan?.RebuildImmediate();
    }

    public void ForceToBasePose()
    {
        StopAllCoroutines();
        hovering = false;
        canHover = true;
        anchor.ApplyTo(rt);
        RestoreSibling();
    }

    // -------- pointer events --------
    public void OnPointerEnter(PointerEventData e)
    {
        if (dragLock || !canHover) return;
        hovering = true;

        // NOTE: no SetAsLastSibling() here — keep logical slot
        if (origSibling < 0) origSibling = transform.GetSiblingIndex();

        StopAllCoroutines();
        StartCoroutine(AnimateTo(
            anchor.basePos + Vector2.up * liftY,
            Quaternion.identity,
            baseScale * hoverMultiplier));

        fan?.OnCardHoverEnter(this);   // neighbors slide aside
    }
    public void OnPointerExit(PointerEventData e)
    {
        if (dragLock) return;
        hovering = false;

        StopAllCoroutines();
        StartCoroutine(ReturnToBase());
    }

    // -------- anim helpers --------
    IEnumerator AnimateTo(Vector2 targetPos, Quaternion targetRot, float targetScale)
    {
        Vector2 sp = rt.anchoredPosition;
        Quaternion sr = rt.localRotation;
        float ss = rt.localScale.x;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(sp, targetPos, k);
            rt.localRotation = Quaternion.Slerp(sr, targetRot, k);
            float s = Mathf.Lerp(ss, targetScale, k);
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

        Vector2 sp = rt.anchoredPosition;
        Quaternion sr = rt.localRotation;
        float ss = rt.localScale.x;

        Vector2 tp = anchor.basePos;
        Quaternion tr = anchor.baseRot;
        float ts = anchor.baseScale;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(sp, tp, k);
            rt.localRotation = Quaternion.Slerp(sr, tr, k);
            float s = Mathf.Lerp(ss, ts, k);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        anchor.ApplyTo(rt);
        RestoreSibling();                   // back under neighbors
        fan?.OnCardHoverExit(this);         // neighbors lerp back
        fan?.RebuildImmediate();
        canHover = true;
    }

    void CancelHoverNow()
    {
        StopAllCoroutines();
        hovering = false;
        anchor.ApplyTo(rt);
        RestoreSibling();
        fan?.RebuildImmediate();
    }

    void RestoreSibling()
    {
        if (origSibling >= 0 && transform.parent != null)
        {
            int max = transform.parent.childCount - 1;
            transform.SetSiblingIndex(Mathf.Clamp(origSibling, 0, max));
        }
        origSibling = -1;
    }
}
