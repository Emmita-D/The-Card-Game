using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class HandCardAnchor : MonoBehaviour
{
    [Header("Computed by layout")]
    public Vector2 basePos;
    public Quaternion baseRot = Quaternion.identity;
    public float baseScale = 1f;
    public int baseOrder = 0;

    Coroutine tween;

    public void ApplyTo(RectTransform rt)
    {
        if (tween != null) { StopCoroutine(tween); tween = null; }
        rt.anchoredPosition = basePos;
        rt.localRotation = baseRot;
        rt.localScale = new Vector3(baseScale, baseScale, 1f);
    }

    // Optional smooth apply if you want it later
    public void SmoothApply(RectTransform rt, float duration)
    {
        if (!gameObject.activeInHierarchy || duration <= 0f) { ApplyTo(rt); return; }
        if (tween != null) StopCoroutine(tween);
        tween = StartCoroutine(Lerp(rt, duration));
    }

    IEnumerator Lerp(RectTransform rt, float duration)
    {
        Vector2 p0 = rt.anchoredPosition, p1 = basePos;
        Quaternion r0 = rt.localRotation, r1 = baseRot;
        float s0 = rt.localScale.x, s1 = baseScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            float k = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(p0, p1, k);
            rt.localRotation = Quaternion.Slerp(r0, r1, k);
            float s = Mathf.Lerp(s0, s1, k);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        ApplyTo(rt);
        tween = null;
    }
}
