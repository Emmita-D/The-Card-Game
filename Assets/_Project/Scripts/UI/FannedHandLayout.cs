using UnityEngine;
using System.Collections.Generic; // << NEW

[DisallowMultipleComponent]
public class FannedHandLayout : MonoBehaviour
{
    [Header("Shape")]
    [Range(0.3f, 1.2f)] public float baseScale = 0.6f;
    public float baseSpread = 210f;
    public float spreadPerCard = 10f;
    public float baseArc = 18f;
    public float arcPerCard = 5f;
    public float minAngle = 2f;
    public float maxAngle = 12f;
    public int angleAtCards = 8;

    public enum SortMode { LeftToRight, CenterOnTop }
    [Header("Sorting")]
    public SortMode sortMode = SortMode.CenterOnTop;

    RectTransform rt;
    bool suppress; // << keep

    void Awake() { rt = (RectTransform)transform; }
    void OnEnable() { RebuildImmediate(); }

    void OnTransformChildrenChanged()
    {
        if (suppress) return; // ignore sibling swaps during hover/drag
        RebuildImmediate();
    }

    public void BeginSuppressLayout() => suppress = true;
    public void EndSuppressLayout() => suppress = false;

    public void RebuildImmediate()
    {
        if (!rt) rt = (RectTransform)transform;
        int n = rt.childCount;
        if (n == 0) return;

        float spread = baseSpread + spreadPerCard * Mathf.Max(0, n - 1);
        float arc = baseArc + arcPerCard * Mathf.Max(0, n - 1);
        float kAng = Mathf.InverseLerp(1, Mathf.Max(2, angleAtCards), n);
        float angAmp = Mathf.Lerp(minAngle, maxAngle, kAng);
        float mid = (n - 1) * 0.5f;

        for (int i = 0; i < n; i++)
        {
            var c = rt.GetChild(i) as RectTransform;
            if (!c) continue;

            float t = (n == 1) ? 0f : (i - mid) / mid; // -1..+1
            float x = t * spread;
            float y = -(t * t) * arc;
            float zRot = -t * angAmp;

            var anchor = c.GetComponent<HandCardAnchor>() ?? c.gameObject.AddComponent<HandCardAnchor>();
            anchor.basePos = new Vector2(x, y);
            anchor.baseRot = Quaternion.Euler(0, 0, zRot);
            anchor.baseScale = baseScale;

            int order;
            if (sortMode == SortMode.LeftToRight) order = 100 + i;
            else
            {
                float distFromCenter = Mathf.Abs(i - mid);
                order = 10000 - Mathf.RoundToInt(distFromCenter * 100f) * 10 + i;
            }
            anchor.baseOrder = order;

            var fx = c.GetComponent<CardHoverFX>();
            var drag = c.GetComponent<DraggableCard>();
            bool busy = (fx && fx.IsHovering) || (drag && drag.IsDragging);
            if (!busy) anchor.ApplyTo(c);
        }
    }

    // << NEW: restore a stable “stair” sibling order for all cards
    public void RestoreStableSiblingOrder()
    {
        if (!rt) rt = (RectTransform)transform;
        var list = new List<RectTransform>(rt.childCount);
        for (int i = 0; i < rt.childCount; i++)
        {
            var c = rt.GetChild(i) as RectTransform;
            if (c) list.Add(c);
        }

        list.Sort((a, b) =>
        {
            var aa = a.GetComponent<HandCardAnchor>();
            var bb = b.GetComponent<HandCardAnchor>();
            int ao = aa ? aa.baseOrder : 0;
            int bo = bb ? bb.baseOrder : 0;
            return ao.CompareTo(bo);
        });

        BeginSuppressLayout();
        for (int i = 0; i < list.Count; i++)
            list[i].SetSiblingIndex(i);
        EndSuppressLayout();
    }
}
