using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System;
using System.Reflection;
using Game.Match.Cards;      // CardSO, CardInstance
using Game.Match.Grid;       // GridService

public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Runtime (set by HandView)")]
    public CardInstance instance;                  // runtime card
    public MonoBehaviour placer;                   // optional: keep for compatibility (unused)
    public object mana;                            // optional: keep for compatibility (unused)

    public static bool PreviewActive;
    public static int PreviewW = 1, PreviewH = 1;
    public static Game.Match.Grid.GridService PreviewGrid;
    public bool IsDragging { get; private set; }

    [Header("Placement")]
    [SerializeField] private GridService grid;     // auto-filled in Awake if null
    [SerializeField] private LayerMask gridMask;   // set to your Grid layer in Inspector
    [SerializeField] private Transform unitsParent;
    [SerializeField] private bool destroyOnPlace = true;
    [SerializeField] public RectTransform handContainer;   // set by HandView

    RectTransform rt;
    CanvasGroup cg;
    Vector3 startPos;
    Transform startParent;
    int startSibling;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        if (grid == null) grid = FindObjectOfType<GridService>();
        if (unitsParent == null && grid != null) unitsParent = grid.transform;
        if (gridMask.value == 0) gridMask = ~0;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        IsDragging = true;
        startSibling = transform.GetSiblingIndex();

        // remember BEFORE moving to HoverLayer
        startParent = transform.parent;
        startSibling = transform.GetSiblingIndex();
        startPos = rt.position;

        // now move to HoverLayer for unclipped hover/drag
        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.AttachToHoverLayer();

        transform.SetAsLastSibling();

        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.9f;
        var soBegin = instance != null ? instance.data : null;
        bool isUnitBegin = (soBegin != null && soBegin.type == Game.Core.CardType.Unit);

        if (isUnitBegin)
        {
            GetFootprintInts(soBegin, out int w, out int h);
            PreviewW = w;
            PreviewH = h;
            PreviewGrid = grid != null ? grid : FindObjectOfType<Game.Match.Grid.GridService>();
            PreviewActive = true;
        }
        else
        {
            PreviewActive = false;
        }

        // If you kept the toggler, keep this one line:
        ToggleDebugPreviews(isUnitBegin);
    }
    public void OnDrag(PointerEventData e)
    {
        if (rt == null) rt = GetComponent<RectTransform>();

        // Use null camera for Screen Space - Overlay
        var canvas = GetComponentInParent<Canvas>();
        Camera cam = e.pressEventCamera;
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            cam = null;
        if (cam == null && canvas != null && canvas.worldCamera != null)
            cam = canvas.worldCamera;
        if (cam == null) cam = Camera.main;

        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e.position, cam, out var wp);
        rt.position = wp;
    }
    public void OnEndDrag(PointerEventData e)
    {
        IsDragging = false;
        cg.blocksRaycasts = true;
        cg.alpha = 1f;

        if (instance == null || grid == null) { SnapBack(); return; }
        var fx = GetComponent<CardHoverFX>();
        IsDragging = false;

        var so = instance.data;

        // ---------- SPELLS / TRAPS ----------
        if (so.type != Game.Core.CardType.Unit)
        {
            // No footprint while dragging; only consume if dropped ON the grid
            // Choose a camera (same pattern you used in OnDrag)
            var canvas = GetComponentInParent<Canvas>();
            Camera cam = e.pressEventCamera;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) cam = null;
            if (cam == null && canvas != null && canvas.worldCamera != null) cam = canvas.worldCamera;
            if (cam == null) cam = Camera.main;

            if (cam != null)
            {
                var ray = cam.ScreenPointToRay(e.position);
                int mask = (gridMask.value == 0) ? ~0 : gridMask.value;

                if (Physics.Raycast(ray, out var hit, 1000f, mask))
                {
                    // Hit the board → play & consume (no placement)
                    Debug.Log($"[DraggableCard] Played {so.type}: {so.cardName} on grid.");
                    ToggleDebugPreviews(true);
                    Destroy(gameObject);
                    return;
                }
            }

            // Didn’t hit the board → return to hand
            ToggleDebugPreviews(true);
            SnapBack();
            return;
        }

        // ---------- UNITS (unchanged) ----------
        var camU = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
        if (camU == null) { SnapBack(); return; }

        var rayU = camU.ScreenPointToRay(e.position);
        if (!Physics.Raycast(rayU, out var hitU, 1000f, gridMask)) { SnapBack(); return; }
        ToggleDebugPreviews(true);

        GetFootprintInts(so, out int w, out int h);
        var origin = CenteredOrigin(grid, hitU.point, w, h);
        if (!grid.CanPlaceRect(origin, w, h)) { SnapBack(); return; }
        grid.PlaceRect(origin, w, h);

        var unitPrefab = GetPrefab(so, "unitPrefab", "prefab", "unit");
        if (unitPrefab != null)
        {
            Vector3 center = grid.TileCenterToWorld(origin, 0f)
                           + new Vector3((w - 1) * 0.5f * grid.TileSize, 0f, (h - 1) * 0.5f * grid.TileSize);

            var go = Instantiate(unitPrefab, center, Quaternion.identity, unitsParent);

            float groundY = grid.transform.position.y;
            var col = go.GetComponentInChildren<Collider>();
            var rend = (col == null) ? go.GetComponentInChildren<Renderer>() : null;
            float halfH = 0.5f;
            if (col != null) halfH = col.bounds.extents.y;
            else if (rend != null) halfH = rend.bounds.extents.y;

            var p = go.transform.position; p.y = groundY + halfH; go.transform.position = p;

            var ur = go.GetComponent<Game.Match.Units.UnitRuntime>();
            if (ur != null) ur.InitFrom(so);
            go.name = so.cardName + $"_{origin.x}_{origin.y}";
        }

        if (destroyOnPlace) Destroy(gameObject); else SnapBack();
    }

    void SnapBack()
    {
        // 1) Always reparent to the hand container we were born in.
        if (handContainer)
            transform.SetParent(handContainer, worldPositionStays: false);
        else if (startParent) // fallback
            transform.SetParent(startParent, worldPositionStays: false);

        // 2) Restore sibling order in the hand.
        transform.SetSiblingIndex(startSibling);

        // 3) Re-apply the saved pose from the fan.
        var rt = (RectTransform)transform;
        var anchor = GetComponent<HandCardAnchor>();
        if (anchor) anchor.ApplyTo(rt);

        // 4) Ask the fan to recompute (safe if null).
        var fan = GetComponentInParent<FannedHandLayout>();
        if (!fan) fan = FindObjectOfType<FannedHandLayout>();
        if (fan) fan.RebuildImmediate();

        // 5) Restore drag visuals.
        if (cg != null) { cg.blocksRaycasts = true; cg.alpha = 1f; }
        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.ReturnToOriginalParent();   // harmless if already in hand
        IsDragging = false;
    }
    void ToggleDebugPreviews(bool enable)
    {
        // Toggle ONLY the preview/old test scripts by type name
        var behaviours = GameObject.FindObjectsOfType<MonoBehaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var tn = b.GetType().Name;
            if (tn == "FootprintPreviewRect" || tn == "FootprintPreview" || tn == "PlaceCubeTest")
            {
                b.enabled = enable;
            }
        }
    }
    // ---------- Helpers ----------
    static Vector2Int CenteredOrigin(GridService grid, Vector3 world, int w, int h)
    {
        float ts = grid.TileSize;

        int ox = ((w & 1) == 1)
            ? Mathf.FloorToInt(world.x / ts) - (w - 1) / 2
            : Mathf.RoundToInt(world.x / ts) - (w / 2);

        int oy = ((h & 1) == 1)
            ? Mathf.FloorToInt(world.z / ts) - (h - 1) / 2
            : Mathf.RoundToInt(world.z / ts) - (h / 2);

        return new Vector2Int(ox, oy);
    }

    static void GetFootprintInts(CardSO so, out int w, out int h)
    {
        w = GetInt(so, "sizeW", "widthTiles", "footprintW", "w");
        h = GetInt(so, "sizeH", "heightTiles", "footprintH", "h");
        if (w <= 0) w = 1;
        if (h <= 0) h = 1;
        w = Mathf.Clamp(w, 1, 4);
        h = Mathf.Clamp(h, 1, 4);
    }

    static GameObject GetPrefab(object o, params string[] names)
    {
        var f = FindField(o, names); if (f != null) return f.GetValue(o) as GameObject;
        var p = FindProp(o, names); if (p != null) return p.GetValue(o) as GameObject;
        return null;
    }

    static int GetInt(object o, params string[] names)
    {
        var f = FindField(o, names); if (f != null) return Convert.ToInt32(f.GetValue(o) ?? 0);
        var p = FindProp(o, names); if (p != null) return Convert.ToInt32(p.GetValue(o) ?? 0);
        return 0;
    }

    static FieldInfo FindField(object obj, params string[] names)
    {
        var t = obj.GetType();
        foreach (var n in names)
        {
            var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f;
        }
        return null;
    }
    static PropertyInfo FindProp(object obj, params string[] names)
    {
        var t = obj.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p;
        }
        return null;
    }
}
