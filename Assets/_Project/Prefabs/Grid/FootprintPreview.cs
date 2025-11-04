using UnityEngine;
using UnityEngine.InputSystem;
using Game.Match.Grid;

[ExecuteAlways]
public class FootprintPreviewRect : MonoBehaviour
{
    public GridService grid;
    [Range(1, 4)] public int footW = 2;
    [Range(1, 4)] public int footH = 2;
    public LayerMask gridMask;

    Vector2 lastMousePos;

    void Update()
    {
        if (Mouse.current != null)
            lastMousePos = Mouse.current.position.ReadValue();
    }

    void OnDrawGizmos()
    {
        if (grid == null || Camera.main == null) return;

        var ray = Camera.main.ScreenPointToRay(lastMousePos);
        if (!Physics.Raycast(ray, out var hit, 1000f, gridMask)) return;

        // New: proper center anchoring
        var origin = CenteredOrigin(grid, hit.point, footW, footH);

        bool can = grid.CanPlaceRect(origin, footW, footH);

        var solid = can ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
        var wire = can ? new Color(0f, 0.9f, 0f, 1f) : new Color(0.95f, 0f, 0f, 1f);

        for (int dy = 0; dy < footH; dy++)
            for (int dx = 0; dx < footW; dx++)
            {
                var t = new Vector2Int(origin.x + dx, origin.y + dy);
                var c = grid.TileCenterToWorld(t, 0f);
                var pos = c + Vector3.up * 0.01f;
                var sz = new Vector3(1f, 0.02f, 1f);
                Gizmos.color = solid; Gizmos.DrawCube(pos, sz);
                Gizmos.color = wire; Gizmos.DrawWireCube(pos, sz);
            }
    }

    // Center rule:
    //  - odd sizes: center on the *tile center* under the mouse
    //  - even sizes: center on the *nearest gridline* to the mouse
    static Vector2Int CenteredOrigin(GridService grid, Vector3 world, int w, int h)
    {
        float ts = grid.TileSize;

        // X axis
        int ox;
        if ((w & 1) == 1) // odd width
        {
            int midTile = Mathf.FloorToInt(world.x / ts);       // tile under mouse
            ox = midTile - (w - 1) / 2;
        }
        else // even width
        {
            int gridline = Mathf.RoundToInt(world.x / ts);       // nearest gridline
            ox = gridline - (w / 2);
        }

        // Y/Z axis
        int oy;
        if ((h & 1) == 1) // odd height
        {
            int midTile = Mathf.FloorToInt(world.z / ts);
            oy = midTile - (h - 1) / 2;
        }
        else // even height
        {
            int gridline = Mathf.RoundToInt(world.z / ts);
            oy = gridline - (h / 2);
        }

        return new Vector2Int(ox, oy);
    }
}
