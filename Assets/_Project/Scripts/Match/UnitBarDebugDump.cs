using UnityEngine;
using UnityEngine.UI;

public class UnitBarDebugDump : MonoBehaviour
{
    public RectTransform unitBarPanel;
    public RectTransform viewport;
    public RectTransform content;

    void LateUpdate()
    {
        if (!unitBarPanel || !viewport || !content) return;
        var p = unitBarPanel.rect;
        var v = viewport.rect;
        var c = content.rect;
        Debug.Log($"[UI-Dump] Panel:{p.width}x{p.height}  Viewport:{v.width}x{v.height}  Content:{c.width}x{c.height}");
    }
}
