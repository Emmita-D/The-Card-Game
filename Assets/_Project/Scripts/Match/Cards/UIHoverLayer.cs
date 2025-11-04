// UIHoverLayer.cs (attach to the HoverLayer GameObject)
using UnityEngine;

public class UIHoverLayer : MonoBehaviour
{
    public static RectTransform Instance;
    void Awake() => Instance = (RectTransform)transform;
}
