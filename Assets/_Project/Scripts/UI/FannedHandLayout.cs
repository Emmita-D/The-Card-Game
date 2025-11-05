using UnityEngine;

[DisallowMultipleComponent]
public class FannedHandLayout : MonoBehaviour
{
    [Header("Shape")]
    [Range(0.3f, 1.0f)] public float baseScale = 0.5f;
    public float baseSpread = 210f;
    public float spreadPerCard = 10f;
    public float baseArc = 18f;
    public float arcPerCard = 5f;
    public float minAngle = 2f;
    public float maxAngle = 12f;
    public int angleAtCards = 8;

    public enum SortMode { LeftToRight, CenterOnTop }
    [Header("Sorting")]
    public SortMode sortMode = SortMode.LeftToRight;   // rightmost on top

    [Header("Hover give-room")]
    public float hoverGap = 140f;                      // horizontal space created at hovered index
    [Range(0.05f, 0.95f)] public float hoverFalloff = 0.6f; // each step away gets this fraction
    public float closeAnimTime = 0.18f;                // smoothing for neighbors

    RectTransform rt;
    int hoverIndex = -1;                               // -1 => no hover

    void Awake() { rt = (RectTransform)transform; }
    void OnEnable() { RebuildImmediate(); }
    void OnTransformChildrenChanged() => RebuildImmediate();

    void LateUpdate()
    {
        // Smooth neighbor motion each frame
        if (rt == null) return;
        int n = rt.childCount;
        if (n == 0) return;

        float alpha = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, closeAnimTime));

        for (int i = 0; i < n; i++)
        {
            var c = rt.GetChild(i) as RectTransform; if (!c) continue;
            var anchor = c.GetComponent<HandCardAnchor>(); if (!anchor) continue;

            // Skip hovered/dragged cards — they animate themselves
            var fx = c.GetComponent<CardHoverFX>();
            var drag = c.GetComponent<DraggableCard>();
            bool busy = (fx && fx.IsHovering) || (drag && drag.IsDragging);
            if (busy) continue;

            // Target pose = base pose +/- horizontal offset if a card is hovered
            Vector2 targetPos = anchor.basePos;
            if (hoverIndex >= 0 && hoverIndex < n)
            {
                int d = i - hoverIndex;
                if (d != 0)
                {
                    float sign = Mathf.Sign(d);
                    float steps = Mathf.Abs(d) - 1f;           // neighbors closer move more
                    float push = hoverGap * Mathf.Pow(hoverFalloff, Mathf.Max(0f, steps));
                    targetPos.x += push * sign;
                }
            }

            // Smoothly move non-busy cards toward their target (scale & rot stay base)
            c.anchoredPosition = Vector2.Lerp(c.anchoredPosition, targetPos, alpha);
            c.localRotation = Quaternion.Slerp(c.localRotation, anchor.baseRot, alpha);
            float s = Mathf.Lerp(c.localScale.x, anchor.baseScale, alpha);
            c.localScale = new Vector3(s, s, 1f);
        }
    }

    public void RebuildImmediate()
    {
        if (rt == null) rt = (RectTransform)transform;
        int n = rt.childCount;
        if (n == 0) return;

        float spread = baseSpread + spreadPerCard * Mathf.Max(0, n - 1);
        float arc = baseArc + arcPerCard * Mathf.Max(0, n - 1);
        float kAng = Mathf.InverseLerp(1, Mathf.Max(2, angleAtCards), n);
        float angAmp = Mathf.Lerp(minAngle, maxAngle, kAng);
        float mid = (n - 1) * 0.5f;

        for (int i = 0; i < n; i++)
        {
            var c = rt.GetChild(i) as RectTransform; if (!c) continue;

            float t = (n == 1) ? 0f : (i - mid) / mid; // -1..+1
            float x = t * spread;
            float y = -(t * t) * arc;
            float zRot = -t * angAmp;                     // outward tilt

            var anchor = c.GetComponent<HandCardAnchor>() ?? c.gameObject.AddComponent<HandCardAnchor>();
            anchor.basePos = new Vector2(x, y);
            anchor.baseRot = Quaternion.Euler(0, 0, zRot);
            anchor.baseScale = baseScale;

            // Z-order: rightmost on top when LeftToRight
            int order;
            if (sortMode == SortMode.LeftToRight) order = 100 + i;
            else /* CenterOnTop */                 order = 10000 - Mathf.RoundToInt(Mathf.Abs(i - mid) * 100f) * 10 + i;
            anchor.baseOrder = order;

            var fx = c.GetComponent<CardHoverFX>();
            if (fx) { fx.SetBaseScale(baseScale); fx.SetBaseRenderOrder(anchor.baseOrder); }

            var drag = c.GetComponent<DraggableCard>();
            bool busy = (fx && fx.IsHovering) || (drag && drag.IsDragging);
            if (!busy) anchor.ApplyTo(c);
        }
    }

    // Called by CardHoverFX
    public void OnCardHoverEnter(CardHoverFX who)
    {
        hoverIndex = IndexOf((RectTransform)who.transform);
        // neighbors will slide in LateUpdate
    }
    public void OnCardHoverExit(CardHoverFX who)
    {
        if (IndexOf((RectTransform)who.transform) == hoverIndex)
            hoverIndex = -1; // neighbors slide back in LateUpdate
    }

    int IndexOf(RectTransform child)
    {
        if (!child || child.parent != rt) return -1;
        for (int i = 0; i < rt.childCount; i++)
            if (rt.GetChild(i) == child) return i;
        return -1;
    }
}
