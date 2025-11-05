using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class CardHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale & Lift")]
    [SerializeField] float baseScale = 0.6f;       // set by layout
    [SerializeField] float hoverMultiplier = 1.45f;
    [SerializeField] float liftY = 160f;
    [SerializeField] float animTime = 0.12f;

    [Header("Optional")]
    [SerializeField] Image hoverGlow;

    RectTransform rt;
    HandCardAnchor anchor;
    FannedHandLayout fan; // << NEW: we’ll ask layout to restore global order

    bool hovering;
    public bool IsHovering => hovering;

    int baseSibling;
    bool dragLocked;

    void Awake()
    {
        rt = (RectTransform)transform;
        anchor = GetComponent<HandCardAnchor>() ?? gameObject.AddComponent<HandCardAnchor>();
        fan = GetComponentInParent<FannedHandLayout>(); // << NEW
    }

    void OnEnable()
    {
        if (anchor) anchor.ApplyTo(rt);
        if (hoverGlow) hoverGlow.enabled = false;
        hovering = false;
        dragLocked = false;
    }

    public void InjectHoverLayer(RectTransform _ignored) { /* baseline: no overlay layer */ }

    public void ForceToBasePose()
    {
        if (anchor) anchor.ApplyTo(rt);
        if (hoverGlow) hoverGlow.enabled = false;
        hovering = false;
        dragLocked = false;

        // We used to restore to a cached sibling; instead, normalize whole hand:
        fan?.RestoreStableSiblingOrder();
    }

    public void SetBaseScale(float s)
    {
        baseScale = s;
        if (!hovering && !dragLocked) rt.localScale = Vector3.one * baseScale;
    }
    public void SetBaseRenderOrder(int order) { /* no-op */ }

    public void OnPointerEnter(PointerEventData e)
    {
        if (dragLocked) return;
        hovering = true;

        baseSibling = transform.GetSiblingIndex();

        // draw above neighbors but don’t let layout respond mid-hover
        fan?.BeginSuppressLayout();
        transform.SetAsLastSibling();
        fan?.EndSuppressLayout();

        if (hoverGlow) hoverGlow.enabled = true;

        StopAllCoroutines();
        StartCoroutine(AnimTo(anchor.basePos + Vector2.up * liftY,
                              baseScale * hoverMultiplier,
                              Quaternion.identity));
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (dragLocked) return;
        hovering = false;
        if (hoverGlow) hoverGlow.enabled = false;

        StopAllCoroutines();
        StartCoroutine(ReturnToBase());
    }

    public void BeginDragLock()
    {
        dragLocked = true;
        hovering = false;
        if (hoverGlow) hoverGlow.enabled = false;

        fan?.BeginSuppressLayout();
        transform.SetAsLastSibling();
        fan?.EndSuppressLayout();
    }

    public void EndDragUnlock(float delay = 0.10f)
    {
        StartCoroutine(_EndDragUnlock(delay));
    }

    IEnumerator _EndDragUnlock(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        dragLocked = false;

        if (anchor) anchor.ApplyTo(rt);
        // Normalize the entire hand to a stable stair order
        fan?.RestoreStableSiblingOrder();
    }

    IEnumerator ReturnToBase()
    {
        Vector2 p0 = rt.anchoredPosition, p1 = anchor.basePos;
        float s0 = rt.localScale.x, s1 = anchor.baseScale;
        Quaternion r0 = rt.localRotation, r1 = anchor.baseRot;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(p0, p1, k);
            float s = Mathf.Lerp(s0, s1, k);
            rt.localScale = new Vector3(s, s, 1f);
            rt.localRotation = Quaternion.Slerp(r0, r1, k);
            yield return null;
        }

        anchor.ApplyTo(rt);
        // Normalize the entire hand to a stable stair order
        fan?.RestoreStableSiblingOrder();
    }

    IEnumerator AnimTo(Vector2 targetPos, float targetScale, Quaternion targetRot)
    {
        Vector2 p0 = rt.anchoredPosition;
        float s0 = rt.localScale.x;
        Quaternion r0 = rt.localRotation;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / animTime;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(p0, targetPos, k);
            float s = Mathf.Lerp(s0, targetScale, k);
            rt.localScale = new Vector3(s, s, 1f);
            rt.localRotation = Quaternion.Slerp(r0, targetRot, k);
            yield return null;
        }
        rt.anchoredPosition = targetPos;
        rt.localScale = new Vector3(targetScale, targetScale, 1f);
        rt.localRotation = targetRot;
    }
}
