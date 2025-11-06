using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Game.Match.Cards;
using Game.Match.Grid;
using Game.Match.Mana;   // ManaPool
using Game.Core;
using Game.Match.State;

public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Runtime (set by HandView)")]
    public CardInstance instance;

    // Legacy fields to satisfy older code (e.g., Stage1Sandbox). Not used.
    public MonoBehaviour placer = null;
    public object mana = null;

    [Header("Placement")]
    [SerializeField] private GridService grid;
    [SerializeField] private LayerMask gridMask;
    [SerializeField] private Transform unitsParent;
    [SerializeField] private bool destroyOnPlace = true;
    [SerializeField] public RectTransform handContainer;
    [SerializeField] private TurnController turn;

    // Footprint preview
    public static bool PreviewActive;
    public static int PreviewW = 1, PreviewH = 1;
    public static GridService PreviewGrid;

    public bool IsDragging { get; private set; }

    RectTransform rt;
    CanvasGroup cg;
    Transform startParent;
    int startSibling;

    CardAffordability aff;
    ManaPool pool;   // injected by HandView

    public void SetManaPool(ManaPool p)
    {
        pool = p;
        if (aff != null && p != null) aff.SetPool(p);
    }

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();

        if (grid == null) grid = FindObjectOfType<GridService>();
        if (unitsParent == null && grid != null) unitsParent = grid.transform;
        if (gridMask.value == 0) gridMask = ~0;

        aff = GetComponent<CardAffordability>();
        if (aff == null) aff = gameObject.AddComponent<CardAffordability>();

        if (turn == null) turn = FindObjectOfType<TurnController>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        IsDragging = true;
        startParent = transform.parent;
        startSibling = transform.GetSiblingIndex();

        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.9f;

        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.BeginDragLock();

        transform.SetAsLastSibling();

        var so = instance != null ? instance.data : null;
        bool isUnit = (so != null && so.type == CardType.Unit);

        if (isUnit)
        {
            GetFootprintInts(so, out int w, out int h);
            PreviewW = w; PreviewH = h;
            PreviewGrid = grid != null ? grid : FindObjectOfType<GridService>();
            PreviewActive = true;
        }
        else
        {
            PreviewActive = false;
        }
        ToggleDebugPreviews(isUnit);
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

        PreviewActive = false;
        ToggleDebugPreviews(false);

        if (instance == null || grid == null) { SnapBack(); return; }
        var so = instance.data;
        bool isUnit = (so.type == CardType.Unit);

        // ---- Spells/Traps: free, only consume if dropped on the grid ----
        if (!isUnit)
        {
            var cam = CameraFor(e);
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(e.position), out var _, 1000f,
                                               (gridMask.value == 0) ? ~0 : gridMask.value))
            {
                ConsumeAndDestroy();
                return;
            }
            SnapBack();
            return;
        }

        // ---- Units: must be affordable ----
        if (aff != null && !aff.ComputeAffordableNow()) { SnapBack(); return; }

        var camU = CameraFor(e);
        if (camU == null) { SnapBack(); return; }
        if (!Physics.Raycast(camU.ScreenPointToRay(e.position), out var hitU, 1000f, gridMask)) { SnapBack(); return; }

        GetFootprintInts(so, out int w, out int h);
        var origin = CenteredOrigin(grid, hitU.point, w, h);
        if (!grid.CanPlaceRect(origin, w, h)) { SnapBack(); return; }

        // Spend AFTER successful placement
        if (aff != null) aff.SpendCostNow();

        grid.PlaceRect(origin, w, h);

        var unitPrefab = so.unitPrefab;
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

        if (destroyOnPlace) ConsumeAndDestroy();
        else
        {
            // even if you keep the card view for debugging, it should leave the hand model
            if (turn != null && instance != null) turn.RemoveFromHand(instance);
            SnapBack();
        }
        var fx2 = GetComponent<CardHoverFX>();
        if (fx2) fx2.EndDragUnlock(0.10f);
    }

    void SnapBack()
    {
        if (handContainer)
            transform.SetParent(handContainer, false);
        else if (startParent)
            transform.SetParent(startParent, false);

        transform.SetSiblingIndex(Mathf.Clamp(startSibling, 0, transform.parent.childCount));

        var fx = GetComponent<CardHoverFX>();
        if (fx) fx.ForceToBasePose();

        var fan = GetComponentInParent<FannedHandLayout>() ?? FindObjectOfType<FannedHandLayout>();
        if (fan) fan.RebuildImmediate();

        if (cg != null) { cg.blocksRaycasts = true; cg.alpha = 1f; }
        PreviewActive = false;
        ToggleDebugPreviews(false);
        IsDragging = false;
    }

    // ---------- helpers ----------
    Camera CameraFor(PointerEventData e)
    {
        var canvas = GetComponentInParent<Canvas>();
        Camera cam = e != null ? e.pressEventCamera : null;
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) cam = null;
        if (cam == null && canvas != null && canvas.worldCamera != null) cam = canvas.worldCamera;
        if (cam == null) cam = Camera.main;
        return cam;
    }

    static Vector2Int CenteredOrigin(GridService grid, Vector3 world, int w, int h)
    {
        float ts = grid.TileSize;
        int ox = ((w & 1) == 1) ? Mathf.FloorToInt(world.x / ts) - (w - 1) / 2
                                : Mathf.RoundToInt(world.x / ts) - (w / 2);
        int oy = ((h & 1) == 1) ? Mathf.FloorToInt(world.z / ts) - (h - 1) / 2
                                : Mathf.RoundToInt(world.z / ts) - (h / 2);
        return new Vector2Int(ox, oy);
    }

    static void GetFootprintInts(CardSO so, out int w, out int h)
    {
        w = Mathf.Clamp(so.sizeW, 1, 4);
        h = Mathf.Clamp(so.sizeH, 1, 4);
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
    void ConsumeAndDestroy()
    {
        // Remove the runtime card from the controller’s hand, then destroy the view
        if (turn != null && instance != null)
            turn.RemoveFromHand(instance);

        // Destroy the UI card
        Destroy(gameObject);
    }
}
