using UnityEngine;

[DisallowMultipleComponent]
public class FannedHandLayout : MonoBehaviour
{
    [Header("Shape")]
    [Range(0.3f, 1.0f)] public float baseScale = 0.5f; // in-hand size
    public float baseSpread = 210f;     // horizontal half-spread at ~5 cards
    public float spreadPerCard = 10f;   // more cards -> wider spread
    public float baseArc = 18f;         // vertical “lift” in px (center lowest)
    public float arcPerCard = 5f;       // more cards -> more arc
    public float minAngle = 2f;         // degrees at small hands
    public float maxAngle = 12f;        // degrees at large hands
    public int angleAtCards = 8;        // reach maxAngle around this many cards
    public enum SortMode { LeftToRight, CenterOnTop }
    [Header("Sorting")]
    public SortMode sortMode = SortMode.CenterOnTop;   // default to center-on-top

    RectTransform rt;
    void Awake() { rt = (RectTransform)transform; }
    void OnEnable() { RebuildImmediate(); }

    public void RebuildImmediate()
    {
        if (rt == null) rt = (RectTransform)transform;
        int n = rt.childCount;
        if (n == 0) return;

        float spread = baseSpread + spreadPerCard * Mathf.Max(0, n - 1);
        float arc = baseArc + arcPerCard * Mathf.Max(0, n - 1);
        float kAng = Mathf.InverseLerp(1, Mathf.Max(2, angleAtCards), n);
        float angAmp = Mathf.Lerp(minAngle, maxAngle, kAng);

        float mid = (n - 1) * 0.5f; // center index

        for (int i = 0; i < n; i++)
        {
            var c = rt.GetChild(i) as RectTransform;
            if (!c) continue;

            float t = (n == 1) ? 0f : (i - mid) / mid; // -1..+1
            float x = t * spread;
            float y = -(t * t) * arc;             // parabolic arc
            float zRot = -t * angAmp;             // outward tilt (note the minus)

            // Cache base pose in an anchor
            var anchor = c.GetComponent<HandCardAnchor>() ?? c.gameObject.AddComponent<HandCardAnchor>();
            anchor.basePos = new Vector2(x, y);
            anchor.baseRot = Quaternion.Euler(0, 0, zRot);
            anchor.baseScale = baseScale;
            int order;
            if (sortMode == SortMode.LeftToRight)
            {
                order = 100 + i;
            }
            else // CenterOnTop
            {
                // Center draws last; farther from center draws earlier
                float dist = Mathf.Abs(i - mid);         // 0 at center, up to ~mid at edges
                                                         // Make a big descending scale so differences are clear and stable
                order = 10000 - Mathf.RoundToInt(dist * 100f) * 10 + i;
            }
            anchor.baseOrder = order;
            // Inform hover FX about scale & base order
            var fx = c.GetComponent<CardHoverFX>();
            if (fx) { fx.SetBaseScale(baseScale); fx.SetBaseRenderOrder(anchor.baseOrder); }

            // Don’t fight interactions: only apply when not hovering/dragging
            var drag = c.GetComponent<DraggableCard>();
            bool busy = (fx && fx.IsHovering) || (drag && drag.IsDragging);
            if (!busy) anchor.ApplyTo(c);
        }
    }
}
