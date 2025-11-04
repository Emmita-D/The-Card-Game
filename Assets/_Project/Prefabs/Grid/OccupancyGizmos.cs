// Assets/_Project/Prefabs/Grid/OccupancyGizmos.cs
using UnityEngine;
using Game.Match.Grid;

[ExecuteAlways]
public class OccupancyGizmos : MonoBehaviour
{
    public GridService grid;
    public Color occupiedSolid = new Color(0.2f, 0.6f, 1f, 0.18f);
    public Color occupiedWire = new Color(0.2f, 0.6f, 1f, 0.75f);

    void OnDrawGizmos()
    {
        if (grid == null) grid = GetComponent<GridService>();
        if (grid == null) return;

        foreach (var t in grid.AllOccupiedTiles())
        {
            var c = grid.TileCenterToWorld(t, 0f);
            var pos = c + Vector3.up * 0.005f;
            var sz = new Vector3(1f, 0.01f, 1f);
            Gizmos.color = occupiedSolid; Gizmos.DrawCube(pos, sz);
            Gizmos.color = occupiedWire; Gizmos.DrawWireCube(pos, sz);
        }
    }
}
