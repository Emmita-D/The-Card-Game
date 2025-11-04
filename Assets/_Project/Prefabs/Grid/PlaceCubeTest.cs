using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Game.Match.Grid;

public class PlaceCubeTest : MonoBehaviour
{
    public GridService grid;
    public LayerMask gridMask;
    [Range(1, 4)] public int widthTiles = 2;
    [Range(1, 4)] public int heightTiles = 2;

    struct Placed
    {
        public Vector2Int origin;
        public int w, h;
        public GameObject group;
    }
    readonly List<Placed> placed = new();

    void Update()
    {
        if (grid == null || Camera.main == null || Mouse.current == null) return;

        var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 1000f, gridMask)) return;

        // New: same center anchoring as the preview
        var origin = CenteredOrigin(grid, hit.point, widthTiles, heightTiles);

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (grid.CanPlaceRect(origin, widthTiles, heightTiles))
            {
                grid.PlaceRect(origin, widthTiles, heightTiles);

                var group = new GameObject($"Placed_{origin.x}_{origin.y}_{widthTiles}x{heightTiles}");
                for (int dy = 0; dy < heightTiles; dy++)
                    for (int dx = 0; dx < widthTiles; dx++)
                    {
                        var t = new Vector2Int(origin.x + dx, origin.y + dy);
                        var pos = grid.TileCenterToWorld(t, 0f) + Vector3.up * 0.5f;

                        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.transform.SetPositionAndRotation(pos, Quaternion.identity);
                        cube.transform.localScale = new Vector3(1f, 1f, 1f);
                        cube.transform.SetParent(group.transform, worldPositionStays: true);
                        cube.name = $"Cell_{t.x}_{t.y}";
                    }

                placed.Add(new Placed { origin = origin, w = widthTiles, h = heightTiles, group = group });
            }
            else
            {
                Debug.Log($"[PlaceCubeTest] Blocked at {origin} for {widthTiles}x{heightTiles}");
            }
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            // For removal we can use the tile under the click
            if (!grid.WorldToTile(hit.point, out var tile)) return;

            for (int i = placed.Count - 1; i >= 0; i--)
            {
                if (ContainsTile(placed[i], tile))
                {
                    grid.RemoveRect(placed[i].origin, placed[i].w, placed[i].h);
                    if (placed[i].group != null) Destroy(placed[i].group);
                    placed.RemoveAt(i);
                    break;
                }
            }
        }
    }

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

    static bool ContainsTile(Placed p, Vector2Int t)
    {
        return t.x >= p.origin.x && t.x < p.origin.x + p.w &&
               t.y >= p.origin.y && t.y < p.origin.y + p.h;
    }
}
