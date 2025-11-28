using UnityEngine;
using UnityEngine.InputSystem;
using Game.Match.Grid;

[ExecuteAlways]
public class FootprintPreviewRect : MonoBehaviour
{
    [Header("Grid / Raycast")]
    public GridService grid;         // optional; will fall back to DraggableCard.PreviewGrid
    public LayerMask gridMask;       // if 0, we’ll raycast everything

    [Header("Debug")]
    [Tooltip("If true, logs a few frames of preview debug info whenever a preview becomes active.")]
    public bool debugLogs = true;

    // simple frame counter so we don’t spam endlessly
    int debugFramesRemaining = 0;

    Vector2 lastMousePos;

    void Update()
    {
        // Track the last mouse position every frame (new Input System)
        if (Mouse.current != null)
            lastMousePos = Mouse.current.position.ReadValue();

        // When preview is off, reset debug counter so next activation logs again
        if (!DraggableCard.PreviewActive)
            debugFramesRemaining = 0;
    }

    void OnDrawGizmos()
    {
        // Only draw while a preview is active (drag OR external summons)
        if (!DraggableCard.PreviewActive)
            return;

        var g = grid != null ? grid : DraggableCard.PreviewGrid;
        if (g == null || Camera.main == null)
            return;

        // Get footprint from DraggableCard static preview fields
        int footW = Mathf.Clamp(DraggableCard.PreviewW, 1, 4);
        int footH = Mathf.Clamp(DraggableCard.PreviewH, 1, 4);

        var cam = Camera.main;
        int mask = (gridMask.value == 0) ? ~0 : gridMask.value;

        var ray = cam.ScreenPointToRay(lastMousePos);
        if (!Physics.Raycast(ray, out var hit, 1000f, mask))
        {
            MaybeLog(
                $"[FootprintPreviewRect] PreviewActive but raycast did not hit any collider. " +
                $"mouse={lastMousePos}, mask={mask}"
            );
            return;
        }

        // Centering that matches DraggableCard placement
        var origin = CenteredOrigin(g, hit.point, footW, footH);

        bool can = g.CanPlaceRect(origin, footW, footH);

        // Green only if placement is valid AND (not a unit OR unit is affordable)
        bool ok = can && (!DraggableCard.PreviewIsUnit || DraggableCard.PreviewAffordable);

        var solid = ok ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
        var wire = ok ? new Color(0f, 0.9f, 0f, 1f) : new Color(0.95f, 0f, 0f, 1f);

        // --- DEBUG LOGGING ---
        MaybeLog(
            $"[FootprintPreviewRect] DRAW " +
            $"origin=({origin.x},{origin.y}), size={footW}x{footH}, can={can}, ok={ok}, " +
            $"hitPos={hit.point}"
        );

        // Draw one gizmo quad per tile in the footprint
        for (int dy = 0; dy < footH; dy++)
        {
            for (int dx = 0; dx < footW; dx++)
            {
                var t = new Vector2Int(origin.x + dx, origin.y + dy);
                var c = g.TileCenterToWorld(t, 0f);
                var pos = c + Vector3.up * 0.01f;
                var sz = new Vector3(g.TileSize, 0.02f, g.TileSize);  // use actual tile size

                Gizmos.color = solid;
                Gizmos.DrawCube(pos, sz);

                Gizmos.color = wire;
                Gizmos.DrawWireCube(pos, sz);
            }
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

    void MaybeLog(string msg)
    {
        if (!debugLogs)
            return;

        // Only log a small number of frames after preview becomes active
        if (debugFramesRemaining <= 0)
        {
            // If this is the first time we log for this activation, seed the counter
            debugFramesRemaining = 15;
        }

        if (debugFramesRemaining > 0)
        {
            Debug.Log(msg);
            debugFramesRemaining--;
        }
    }
}
