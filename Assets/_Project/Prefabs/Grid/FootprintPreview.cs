using UnityEngine;
using UnityEngine.InputSystem;
using Game.Match.Grid;

[ExecuteAlways]
public class FootprintPreviewRect : MonoBehaviour
{
    public GridService grid;         // optional; will fall back to DraggableCard.PreviewGrid
    public LayerMask gridMask;       // if 0, we’ll raycast everything

    Vector2 lastMousePos;

    void Update()
    {
        if (Mouse.current != null)
            lastMousePos = Mouse.current.position.ReadValue();
    }

    void OnDrawGizmos()
    {
        // Only draw while a Unit card is actively being dragged
        if (!DraggableCard.PreviewActive) return;

        var g = grid != null ? grid : DraggableCard.PreviewGrid;
        if (g == null || Camera.main == null) return;

        // Get footprint from the dragged card
        int footW = Mathf.Clamp(DraggableCard.PreviewW, 1, 4);
        int footH = Mathf.Clamp(DraggableCard.PreviewH, 1, 4);

        var cam = Camera.main;
        int mask = (gridMask.value == 0) ? ~0 : gridMask.value;

        var ray = cam.ScreenPointToRay(lastMousePos);
        if (!Physics.Raycast(ray, out var hit, 1000f, mask)) return;

        // Centering that matches placement
        var origin = CenteredOrigin(g, hit.point, footW, footH);

        bool can = g.CanPlaceRect(origin, footW, footH);

        // Green only if placement is valid AND (not a unit OR unit is affordable)
        bool ok = can && (!DraggableCard.PreviewIsUnit || DraggableCard.PreviewAffordable);

        var solid = ok ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
        var wire = ok ? new Color(0f, 0.9f, 0f, 1f) : new Color(0.95f, 0f, 0f, 1f);

        for (int dy = 0; dy < footH; dy++)
            for (int dx = 0; dx < footW; dx++)
            {
                var t = new Vector2Int(origin.x + dx, origin.y + dy);
                var c = g.TileCenterToWorld(t, 0f);
                var pos = c + Vector3.up * 0.01f;
                var sz = new Vector3(g.TileSize, 0.02f, g.TileSize);  // use actual tile size
                Gizmos.color = solid; Gizmos.DrawCube(pos, sz);
                Gizmos.color = wire; Gizmos.DrawWireCube(pos, sz);
            }
    }

    // Same centering rule used by DraggableCard
    static Vector2Int CenteredOrigin(GridService g, Vector3 world, int w, int h)
    {
        float ts = g.TileSize;

        int ox = ((w & 1) == 1)
            ? Mathf.FloorToInt(world.x / ts) - (w - 1) / 2
            : Mathf.RoundToInt(world.x / ts) - (w / 2);

        int oy = ((h & 1) == 1)
            ? Mathf.FloorToInt(world.z / ts) - (h - 1) / 2
            : Mathf.RoundToInt(world.z / ts) - (h / 2);

        return new Vector2Int(ox, oy);
    }
}
