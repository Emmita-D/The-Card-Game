using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Game.Match.Cards;
using Game.Match.Grid;
using Game.Match.Mana;   // ManaPool
using Game.Core;
using Game.Match.State;
using Game.Match.Graveyard;
using Game.Match.CardPhase;   // 👈 needed for BattlePlacementRegistry
using Game.Match.Log;
using Game.Match.Traps;


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

    // NEW: preview metadata for affordability coloring
    public static bool PreviewIsUnit = false;
    public static bool PreviewAffordable = true;

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
        PreviewIsUnit = isUnit;

        if (isUnit)
        {
            GetFootprintInts(so, out int w, out int h);
            PreviewW = w; PreviewH = h;
            PreviewGrid = grid != null ? grid : FindObjectOfType<GridService>();
            PreviewActive = true;

            // Evaluate affordability at drag start
            PreviewAffordable = (aff == null) || aff.ComputeAffordableNow();
        }
        else
        {
            PreviewActive = false;
            PreviewAffordable = true; // spells unaffected
        }

        ToggleDebugPreviews(isUnit);
    }

    public void OnDrag(PointerEventData e)
    {
        // Keep affordability up to date while dragging
        if (PreviewIsUnit && aff != null)
            PreviewAffordable = aff.ComputeAffordableNow();

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

        // Stop previews immediately
        PreviewActive = false;
        ToggleDebugPreviews(false);
        PreviewIsUnit = false;
        PreviewAffordable = true;

        if (instance == null || grid == null) { SnapBack(); return; }
        var so = instance.data;
        bool isUnit = (so.type == CardType.Unit);

        // ---- Spells/Traps: free, only consume if dropped on the grid ----
        if (!isUnit)
        {
            var cam = CameraFor(e);
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(e.position),
                                               out var _, 1000f,
                                               (gridMask.value == 0) ? ~0 : gridMask.value))
            {
                // LOG: spells / traps played on the CardPhase board (local-only)
                if (so != null)
                {
                    var logger = ActionLogService.Instance;
                    if (logger != null)
                    {
                        string cardName = !string.IsNullOrEmpty(so.cardName) ? so.cardName : so.name;

                        string msg;
                        switch (so.type)
                        {
                            case CardType.Spell:
                                msg = $"Cast {cardName}.";
                                break;
                            case CardType.Trap:
                                msg = $"Set {cardName} as a trap.";
                                break;
                            default:
                                msg = $"Played {cardName}.";
                                break;
                        }

                        logger.CardLocal(
                            msg,
                            so.artSprite, // icon in the log
                            so            // CardSO so the row is clickable
                        );
                    }
                }

                // NEW: run simple v1 spell effects (like SearchUnitByRealm, RefillManaToMax),
                // or arm traps via TrapService.
                int ownerId = (instance != null) ? instance.ownerId : 0;

                if (so != null)
                {
                    if (so.type == CardType.Spell)
                    {
                        CardEffectRunner.RunOnSpellResolved(so, ownerId);

                        // Spells are consumed immediately -> graveyard.
                        if (instance != null && instance.data != null)
                            GraveyardService.Instance.Add(ownerId, instance.data);
                    }
                    else if (so.type == CardType.Trap)
                    {
                        // Arm trap, do NOT send to graveyard yet.
                        if (TrapService.Instance != null)
                        {
                            TrapService.Instance.RegisterTrap(so, ownerId);
                        }
                        else
                        {
                            Debug.LogWarning("[DraggableCard] Tried to set a trap, but TrapService.Instance is null.");
                        }
                    }
                    else
                    {
                        // Fallback: non-unit, non-spell, non-trap card -> just send to graveyard.
                        if (instance != null && instance.data != null)
                            GraveyardService.Instance.Add(ownerId, instance.data);
                    }
                }

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

            // NEW: Record the placement for the battle layer
            var reg = BattlePlacementRegistry.Instance;
            int owner = (instance != null) ? instance.ownerId : 0; // local = 0 by your convention
            if (reg != null)
            {
                // Compute TRUE board center X from bounds (renderer > collider > fallback)
                float centerX = grid.transform.position.x;
                var rend = grid.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    centerX = rend.bounds.center.x;
                }
                else
                {
                    var coll = grid.GetComponentInChildren<Collider>();
                    if (coll != null)
                        centerX = coll.bounds.center.x;
                }

                reg.SetLocalBoardCenterX(centerX);        // <- patched line
                reg.Register(so, center, owner);
            }
            else
            {
                Debug.LogWarning("[DraggableCard] BattlePlacementRegistry.Instance is null; placement not recorded.");
            }

            var go = Instantiate(unitPrefab, center, Quaternion.identity, unitsParent);

            // Attach graveyard relay to record unit deaths (per-player / per-realm)
            var gy = go.GetComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
            if (gy == null) gy = go.AddComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
            gy.source = so;                               // CardSO that spawned this unit
            gy.ownerId = (instance != null) ? instance.ownerId : 0;

            float groundY = grid.transform.position.y;
            var col = go.GetComponentInChildren<Collider>();
            var rendUnit = (col == null) ? go.GetComponentInChildren<Renderer>() : null;
            float halfH = 0.5f;
            if (col != null) halfH = col.bounds.extents.y;
            else if (rendUnit != null) halfH = rendUnit.bounds.extents.y;

            var p = go.transform.position; p.y = groundY + halfH; go.transform.position = p;

            var ur = go.GetComponent<Game.Match.Units.UnitRuntime>();
            if (ur != null) ur.InitFrom(so);
            go.name = so.cardName + $"_{origin.x}_{origin.y}";
        }
        // Log: unit placed on the CardPhase board (local-only)
        if (so != null && so.type == CardType.Unit)
        {
            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string cardName = !string.IsNullOrEmpty(so.cardName) ? so.cardName : so.name;
                logger.CardLocal(
                    $"Placed {cardName} on the board.",
                    so.artSprite, // icon in the log
                    so            // CardSO so the row is clickable
                );
            }
        }
        // Reset any lingering preview flags (defensive)
        PreviewActive = false;
        PreviewIsUnit = false;
        PreviewAffordable = true;

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

        // Reset preview flags
        PreviewIsUnit = false;
        PreviewAffordable = true;
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
