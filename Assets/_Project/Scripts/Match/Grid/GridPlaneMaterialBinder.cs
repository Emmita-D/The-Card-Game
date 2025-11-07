using UnityEngine;
using Game.Match.Grid;

[ExecuteAlways]
public class GridPlaneMaterialBinder : MonoBehaviour
{
    public GridService grid;
    public MeshRenderer targetRenderer;
    [ColorUsage(false, true)] public Color fillColor = new Color(0, 0, 0, 0);
    [ColorUsage(false, true)] public Color lineColor = new Color(0.2f, 0.9f, 1f, 0.6f);
    [Range(0.001f, 0.08f)] public float thickness = 0.02f;

    static readonly int ID_Tiling = Shader.PropertyToID("_Tiling");
    static readonly int ID_Fill = Shader.PropertyToID("_FillColor");
    static readonly int ID_Line = Shader.PropertyToID("_LineColor");
    static readonly int ID_Thickness = Shader.PropertyToID("_Thickness");

    void OnValidate() { Apply(); }
    void Awake() { Apply(); }
    void Update() { Apply(); }

    void Apply()
    {
        if (grid == null) grid = GetComponentInParent<GridService>();
        if (targetRenderer == null) targetRenderer = GetComponent<MeshRenderer>();
        if (targetRenderer == null) return;

        var m = targetRenderer.sharedMaterial;
        if (m == null || m.shader == null || m.shader.name != "Unlit/ProceduralGrid") return;

        // grid size in tiles
        int cols = Mathf.Max(1, grid != null ? grid.Width : 8);
        int rows = Mathf.Max(1, grid != null ? grid.Height : 6);

        m.SetVector(ID_Tiling, new Vector4(cols, rows, 0, 0));
        m.SetColor(ID_Fill, fillColor);
        m.SetColor(ID_Line, lineColor);
        m.SetFloat(ID_Thickness, thickness);

        // ✅ Ensure the grid plane draws BEFORE the occupancy overlay
        // (Overlay is 2990; keep the plane slightly earlier.)
        m.renderQueue = 2950;
    }
}
