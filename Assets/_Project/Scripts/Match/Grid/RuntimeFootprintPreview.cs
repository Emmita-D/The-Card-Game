using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Match.Grid;

[ExecuteAlways]
public class RuntimeFootprintPreview : MonoBehaviour
{
    [Header("Refs")]
    public GridService grid;           // optional; falls back to DraggableCard.PreviewGrid
    public LayerMask gridMask;         // if 0, raycasts everything

    [Header("Visuals")]
    public Color validColor = new(0f, 1f, 0f, 0.35f);
    public Color invalidColor = new(1f, 0f, 0f, 0.35f);

    [Tooltip("Vertical lift above the field, in world units.")]
    public float yOffset = 0.035f;     // slightly above occupancy (~0.02)

    // runtime
    Mesh quad;
    Material mat;
    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Color = Shader.PropertyToID("_Color");

    readonly List<Matrix4x4> batch = new(1023);
    Vector2 lastMousePos;

    void OnEnable()
    {
        if (quad == null) quad = BuildQuad();
        if (mat == null)
        {
            // Prefer URP Unlit; fallback to legacy Unlit/Color
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            mat = new Material(sh);

            // Draw AFTER occupancy (2990) but BEFORE other transparent effects
            mat.renderQueue = 2995;

            // Transparent, depth-tested (units occlude it; grid plane does not)
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            mat.enableInstancing = true;
        }
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

    void Update()
    {
        if (Mouse.current != null)
            lastMousePos = Mouse.current.position.ReadValue();
    }

    void LateUpdate()
    {
        // Only draw while a Unit card is actively being dragged
        if (!DraggableCard.PreviewActive) return;

        var g = grid != null ? grid : DraggableCard.PreviewGrid;
        if (g == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        int mask = (gridMask.value == 0) ? ~0 : gridMask.value;
        var ray = cam.ScreenPointToRay(lastMousePos);
        if (!Physics.Raycast(ray, out var hit, 1000f, mask)) return;

        int w = Mathf.Clamp(DraggableCard.PreviewW, 1, 4);
        int h = Mathf.Clamp(DraggableCard.PreviewH, 1, 4);

        // Same centering rule as your gizmo script (and placement) :contentReference[oaicite:1]{index=1}
        var origin = CenteredOrigin(g, hit.point, w, h);

        bool can = g.CanPlaceRect(origin, w, h);
        bool ok = can && (!DraggableCard.PreviewIsUnit || DraggableCard.PreviewAffordable);

        // Pick color for this frame
        SetMatColor(ok ? validColor : invalidColor);

        // Build transforms for each tile in the footprint and draw
        batch.Clear();
        float s = g.TileSize;

        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                var t = new Vector2Int(origin.x + dx, origin.y + dy);
                Vector3 c2w = g.TileCenterToWorld(t, 0f) + new Vector3(0f, yOffset, 0f);
                var M = Matrix4x4.TRS(c2w, Quaternion.identity, new Vector3(s, 1f, s));
                batch.Add(M);

                if (batch.Count == 1023)
                {
                    Graphics.DrawMeshInstanced(quad, 0, mat, batch);
                    batch.Clear();
                }
            }

        if (batch.Count > 0)
            Graphics.DrawMeshInstanced(quad, 0, mat, batch);
    }

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

    Mesh BuildQuad()
    {
        var m = new Mesh { name = "RuntimeFootprintQuad" };
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

    void SetMatColor(Color c)
    {
        if (!mat) return;
        if (mat.HasProperty(ID_BaseColor)) mat.SetColor(ID_BaseColor, c); // URP Unlit
        if (mat.HasProperty(ID_Color)) mat.SetColor(ID_Color, c);     // Legacy Unlit/Color
    }
}
