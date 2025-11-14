// Assets/_Project/Scripts/Match/Grid/GridService.cs
using UnityEngine;
using Game.Core;
using System.Collections.Generic;

namespace Game.Match.Grid
{
    public class GridService : MonoBehaviour
    {
        [SerializeField] int width = 8;
        [SerializeField] int height = 6;
        [SerializeField] float tileSize = 1f;

        public int Width => width;
        public int Height => height;
        public float TileSize => tileSize;

        bool[,] occ;

        void Init()
        {
            if (occ == null || occ.GetLength(0) != width || occ.GetLength(1) != height)
                occ = new bool[width, height];
        }

        void Awake() => Init();
        void OnEnable() => Init();

        public bool InBounds(Vector2Int t) =>
            t.x >= 0 && t.y >= 0 && t.x < width && t.y < height;

        public Vector3 TileToWorld(Vector2Int t, float y = 0f) =>
            new Vector3(t.x * tileSize, y, t.y * tileSize);

        public Vector3 TileCenterToWorld(Vector2Int t, float y = 0f) =>
            TileToWorld(t, y) + new Vector3(tileSize * 0.5f, 0f, tileSize * 0.5f);

        public bool WorldToTile(Vector3 w, out Vector2Int t)
        {
            t = new Vector2Int(Mathf.FloorToInt(w.x / tileSize),
                               Mathf.FloorToInt(w.z / tileSize));
            return InBounds(t);
        }

        // ---- Rectangle footprints (fixed orientation), supports up to 4x4 ----
        IEnumerable<Vector2Int> RectTiles(Vector2Int origin, int w, int h)
        {
            // Clamp to 1..4 just as a safety net (you can raise this later)
            w = Mathf.Clamp(w, 1, 4);
            h = Mathf.Clamp(h, 1, 4);
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    yield return new Vector2Int(origin.x + dx, origin.y + dy);
        }

        public bool CanPlaceRect(Vector2Int origin, int w, int h)
        {
            Init();
            foreach (var t in RectTiles(origin, w, h))
                if (!InBounds(t) || occ[t.x, t.y]) return false;
            return true;
        }

        public bool PlaceRect(Vector2Int origin, int w, int h)
        {
            Init();
            if (!CanPlaceRect(origin, w, h)) return false;
            foreach (var t in RectTiles(origin, w, h)) occ[t.x, t.y] = true;
            return true;
        }

        public void RemoveRect(Vector2Int origin, int w, int h)
        {
            Init();
            foreach (var t in RectTiles(origin, w, h))
                if (InBounds(t)) occ[t.x, t.y] = false;
        }

        public bool IsOccupied(Vector2Int t)
        {
            Init();
            return InBounds(t) && occ[t.x, t.y];
        }

        public IEnumerable<Vector2Int> AllOccupiedTiles()
        {
            Init();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (occ[x, y]) yield return new Vector2Int(x, y);
        }

        public void ClearAll()
        {
            Init();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    occ[x, y] = false;
        }


        // ---- Back-compat wrappers for your existing SizeClass calls ----
        // Fixed orientation: I=1x1, II=1x2 (vertical), III=2x2
        IEnumerable<Vector2Int> Footprint(SizeClass s, Vector2Int origin) =>
            RectTiles(origin, s == SizeClass.I_1x1 ? 1 : (s == SizeClass.II_1x2 ? 1 : 2),
                               s == SizeClass.I_1x1 ? 1 : (s == SizeClass.II_1x2 ? 2 : 2));

        public bool CanPlace(SizeClass s, Vector2Int origin)
        {
            if (s == SizeClass.I_1x1) return CanPlaceRect(origin, 1, 1);
            if (s == SizeClass.II_1x2) return CanPlaceRect(origin, 1, 2); // fixed vertical
            /* III_2x2 */
            return CanPlaceRect(origin, 2, 2);
        }

        public bool Place(SizeClass s, Vector2Int origin)
        {
            if (s == SizeClass.I_1x1) return PlaceRect(origin, 1, 1);
            if (s == SizeClass.II_1x2) return PlaceRect(origin, 1, 2);
            /* III_2x2 */
            return PlaceRect(origin, 2, 2);
        }

        public void Remove(SizeClass s, Vector2Int origin)
        {
            if (s == SizeClass.I_1x1) { RemoveRect(origin, 1, 1); return; }
            if (s == SizeClass.II_1x2) { RemoveRect(origin, 1, 2); return; }
            /* III_2x2 */
            RemoveRect(origin, 2, 2);
        }
    }
}
