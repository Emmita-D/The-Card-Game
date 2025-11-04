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

    [Header("Placement")]
    [SerializeField] private GridService grid;     // auto-filled in Awake if null
    [SerializeField] private LayerMask gridMask;   // set to your Grid layer in Inspector
    [SerializeField] private Transform unitsParent;
    [SerializeField] private bool destroyOnPlace = true;

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
        startPos = rt.position;
        startParent = transform.parent;
        startSibling = transform.GetSiblingIndex();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.9f;
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
        cg.blocksRaycasts = true;
        cg.alpha = 1f;

        var cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
        if (cam == null || grid == null || instance == null) { SnapBack(); return; }

        var ray = cam.ScreenPointToRay(e.position);
        if (!Physics.Raycast(ray, out var hit, 1000f, gridMask)) { SnapBack(); return; }

        // Footprint + prefab from SO (no SizeClass dependency)
        var so = instance.data;
        GetFootprintInts(so, out int w, out int h);               // 1..4
        var unitPrefab = GetPrefab(so, "unitPrefab", "prefab", "unit");

        var origin = CenteredOrigin(grid, hit.point, w, h);

        if (!grid.CanPlaceRect(origin, w, h)) { SnapBack(); return; }
        grid.PlaceRect(origin, w, h);

        if (unitPrefab != null)
        {
            // Start from the origin tile's CENTER, then offset by half the footprint
            Vector3 center = grid.TileCenterToWorld(origin, 0f);
            center += new Vector3((w - 1) * 0.5f * grid.TileSize, 0f, (h - 1) * 0.5f * grid.TileSize);

            // Instantiate first, then lift to sit on the ground
            var go = Instantiate(unitPrefab, center, Quaternion.identity, unitsParent);

            // Ground Y is the grid root's Y (adjust if your board lives elsewhere)
            float groundY = grid.transform.position.y;

            // Use collider or renderer to compute half-height
            var col = go.GetComponentInChildren<Collider>();
            var rend = (col == null) ? go.GetComponentInChildren<Renderer>() : null;
            float halfH = 0.5f;
            if (col != null) halfH = col.bounds.extents.y;
            else if (rend != null) halfH = rend.bounds.extents.y;

            // Apply final Y so the feet sit on the board
            var p = go.transform.position;
            p.y = groundY + halfH;
            go.transform.position = p;

            // Optional: init your debug stats/tint
            var ur = go.GetComponent<Game.Match.Units.UnitRuntime>();
            if (ur != null) ur.InitFrom(so);

            go.name = so.cardName + $"_{origin.x}_{origin.y}";
        }

        if (destroyOnPlace) Destroy(gameObject); else SnapBack();
    }

    void SnapBack()
    {
        rt.position = startPos;
        transform.SetParent(startParent, worldPositionStays: true);
        transform.SetSiblingIndex(startSibling);
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
