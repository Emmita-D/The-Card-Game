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
    public MonoBehaviour placer;                   // optional legacy
    public object mana;                            // optional legacy

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
        startParent = transform.parent;
        startPos = rt.position;

        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.BeginDragLock();   // turn hover off + move to HoverLayer

        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.9f;

        // preview sizing for units
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

        ToggleDebugPreviews(isUnitBegin);
    }

    public void OnDrag(PointerEventData e)
    {
        if (rt == null) rt = GetComponent<RectTransform>();

        var canvas = GetComponentInParent<Canvas>();
        Camera cam = e.pressEventCamera;
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) cam = null;
        if (cam == null && canvas != null && canvas.worldCamera != null) cam = canvas.worldCamera;
        if (cam == null) cam = Camera.main;

        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e.position, cam, out var wp);
        rt.position = wp;
    }

    public void OnEndDrag(PointerEventData e)
    {
        IsDragging = false;
        if (cg != null) { cg.blocksRaycasts = true; cg.alpha = 1f; }

        var fx = GetComponent<CardHoverFX>();

        if (instance == null || grid == null)
        {
            SnapBack();
            if (fx) fx.EndDragUnlock(0.10f);
            return;
        }

        var so = instance.data;

        // ---------- SPELLS / TRAPS ----------
        if (so.type != Game.Core.CardType.Unit)
        {
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
            if (fx) fx.EndDragUnlock(0.10f);
            return;
        }

        // ---------- UNITS ----------
        var camU = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
        if (camU == null) { SnapBack(); if (fx) fx.EndDragUnlock(0.10f); return; }

        var rayU = camU.ScreenPointToRay(e.position);
        if (!Physics.Raycast(rayU, out var hitU, 1000f, gridMask))
        {
            SnapBack(); if (fx) fx.EndDragUnlock(0.10f); return;
        }
        ToggleDebugPreviews(true);

        GetFootprintInts(so, out int w2, out int h2);
        var origin = CenteredOrigin(grid, hitU.point, w2, h2);
        if (!grid.CanPlaceRect(origin, w2, h2))
        {
            SnapBack(); if (fx) fx.EndDragUnlock(0.10f); return;
        }
        grid.PlaceRect(origin, w2, h2);

        var unitPrefab = GetPrefab(so, "unitPrefab", "prefab", "unit");
        if (unitPrefab != null)
        {
            Vector3 center = grid.TileCenterToWorld(origin, 0f)
                           + new Vector3((w2 - 1) * 0.5f * grid.TileSize, 0f, (h2 - 1) * 0.5f * grid.TileSize);

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

        if (destroyOnPlace)
        {
            Destroy(gameObject);
        }
        else
        {
            SnapBack();
            if (fx) fx.EndDragUnlock(0.10f);
        }
    }

    void SnapBack()
    {
        // 1) Parent: always the hand container (set by HandView)
        if (handContainer)
            transform.SetParent(handContainer, worldPositionStays: false);
        else if (startParent)
            transform.SetParent(startParent, worldPositionStays: false);

        // 2) Sibling order back to where it came from
        int idx = Mathf.Clamp(startSibling, 0, transform.parent.childCount);
        transform.SetSiblingIndex(idx);

        // 3) Exact base pose + cancel any hover visuals
        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.ForceToBasePose();

        // 4) Rebuild the fan immediately
        var fan = GetComponentInParent<FannedHandLayout>();
        if (!fan) fan = FindObjectOfType<FannedHandLayout>();
        if (fan) fan.RebuildImmediate();

        // 5) Restore raycasts
        if (cg != null) { cg.blocksRaycasts = true; cg.alpha = 1f; }
        IsDragging = false;
    }

    void ToggleDebugPreviews(bool enable)
    {
        var behaviours = GameObject.FindObjectsOfType<MonoBehaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var tn = b.GetType().Name;
            if (tn == "FootprintPreviewRect" || tn == "FootprintPreview" || tn == "PlaceCubeTest")
                b.enabled = enable;
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
