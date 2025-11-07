using System.Collections.Generic;
using UnityEngine;
using Game.Match.Grid;

[ExecuteAlways]
public class RuntimeOccupancyOverlay : MonoBehaviour
{
    public GridService grid;

    [Header("Visuals")]
    public Color color = new Color(0.2f, 0.6f, 1f, 0.28f);
    [Tooltip("Vertical separation from the grid plane, in world units.")]
    public float yOffset = 0.02f;

    Mesh quad;
    Material mat;
    readonly List<Matrix4x4> batch = new List<Matrix4x4>(1023);

    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Color = Shader.PropertyToID("_Color");

    void OnEnable()
    {
        if (grid == null) grid = GetComponent<GridService>();
        if (quad == null) quad = BuildQuad();

        if (mat == null)
        {
            // Prefer URP Unlit; fallback to legacy Unlit/Color.
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            mat = new Material(sh);

            // ✅ Draw AFTER the grid plane but BEFORE other transparents
            // (We'll set the plane to ~2950; keep this overlay at 2990.)
            mat.renderQueue = 2990;

            // Transparent, depth-tested (so units occlude the overlay)
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            TrySetMatColor(color);
            mat.enableInstancing = true;
        }

        // Small lift above the plane to avoid z-fighting with the field
        if (yOffset < 0.01f) yOffset = 0.01f;
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            if (mat) Destroy(mat);
            if (quad) Destroy(quad);
        }
        else
        {
            if (mat) DestroyImmediate(mat);
            if (quad) DestroyImmediate(quad);
        }
        mat = null; quad = null;
    }

    void LateUpdate()
    {
        if (grid == null || quad == null || mat == null) return;

        batch.Clear();
        float s = grid != null ? grid.TileSize : 1f;

        foreach (var t in grid.AllOccupiedTiles())
        {
            Vector3 c = grid.TileCenterToWorld(t, 0f) + new Vector3(0f, yOffset, 0f);
            var M = Matrix4x4.TRS(c, Quaternion.identity, new Vector3(s, 1f, s));
            batch.Add(M);

            if (batch.Count == 1023)
            {
                Graphics.DrawMeshInstanced(quad, 0, mat, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) Graphics.DrawMeshInstanced(quad, 0, mat, batch);
    }

    Mesh BuildQuad()
    {
        var m = new Mesh { name = "RuntimeOverlayQuad" };
        m.SetVertices(new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f),
        });
        m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        m.RecalculateBounds();
        return m;
    }

    void TrySetMatColor(Color c)
    {
        if (!mat) return;
        if (mat.HasProperty(ID_BaseColor)) mat.SetColor(ID_BaseColor, c); // URP Unlit
        if (mat.HasProperty(ID_Color)) mat.SetColor(ID_Color, c); // Legacy Unlit/Color
    }
}
