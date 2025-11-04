using UnityEngine;
using UnityEngine.InputSystem;

public class RaycastProbeInput : MonoBehaviour
{
    public LayerMask mask = ~0;

    void Update()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[Probe] Camera.main is NULL. Tag your camera as MainCamera."); return; }

        Vector2 screen = Mouse.current.position.ReadValue();
        var ray = cam.ScreenPointToRay(screen);
        Debug.DrawRay(ray.origin, ray.direction * 100f, Color.yellow, 1f);

        if (Physics.Raycast(ray, out var hit, 1000f, mask))
            Debug.Log($"[Probe] HIT {hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) @ {hit.point}");
        else
            Debug.Log("[Probe] NO HIT");
    }
}
