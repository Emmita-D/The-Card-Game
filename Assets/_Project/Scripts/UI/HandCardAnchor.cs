using UnityEngine;

[DisallowMultipleComponent]
public class HandCardAnchor : MonoBehaviour
{
    public Vector2 basePos;
    public Quaternion baseRot;
    public float baseScale = 0.5f;
    public int baseOrder = 0;

    public void ApplyTo(RectTransform rt)
    {
        rt.anchoredPosition = basePos;
        rt.localRotation = baseRot;
        rt.localScale = Vector3.one * baseScale;
    }
}
