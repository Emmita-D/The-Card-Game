// Assets/_Project/Scripts/Match/UI/HandCardHighlight.cs
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pure visual FX component for hand-selection highlighting.
/// CardView does NOT know about this; HandSelectionController will drive it.
/// </summary>
public class HandCardHighlight : MonoBehaviour
{
    [Header("Highlight visuals")]
    [SerializeField] private Image highlightOverlay;   // e.g. HandSelectionHighlight
    [SerializeField] private Image frame;              // optional border/frame image

    private Vector3 originalScale;
    private bool hasOriginalFrameColor;
    private Color originalFrameColor;

    private void Awake()
    {
        originalScale = transform.localScale;

        if (frame != null)
        {
            originalFrameColor = frame.color;
            hasOriginalFrameColor = true;
        }

        // Start disabled
        if (highlightOverlay != null)
        {
            highlightOverlay.enabled = false;
            var c = highlightOverlay.color;
            c.a = 0f;
            highlightOverlay.color = c;
            highlightOverlay.raycastTarget = false;
        }
    }

    /// <summary>
    /// Remove any highlight (candidate or selected).
    /// </summary>
    public void Clear()
    {
        if (highlightOverlay != null)
        {
            highlightOverlay.enabled = false;
            var c = highlightOverlay.color;
            c.a = 0f;
            highlightOverlay.color = c;
        }

        if (hasOriginalFrameColor && frame != null)
        {
            frame.color = originalFrameColor;
        }

        transform.localScale = originalScale;
    }

    /// <summary>
    /// Mark this card as a valid candidate for selection.
    /// </summary>
    public void ShowCandidate()
    {
        EnsureOriginalFrameColor();

        // Frame tint as a fallback (in case overlay is hidden or masked)
        if (frame != null)
        {
            // MUCH more obvious: strong yellow frame
            frame.color = new Color(1f, 0.9f, 0.4f, 1f);
        }

        if (highlightOverlay != null)
        {
            highlightOverlay.enabled = true;
            highlightOverlay.raycastTarget = false;

            // SUPER bright, almost opaque yellow overlay.
            // If you don't see *this*, something is hiding the Image entirely.
            highlightOverlay.color = new Color(1f, 0.95f, 0.5f, 0.35f);
        }
        else
        {
            Debug.LogWarning($"[HandCardHighlight] No highlightOverlay assigned on {name}; only frame tint will be used.");
        }

        // Bump scale aggressively so FannedHandLayout difference is noticeable
        transform.localScale = originalScale * 1.2f;

        Debug.Log($"[HandCardHighlight] ShowCandidate on {name} (strong style)");
    }

    /// <summary>
    /// Mark this card as actively selected.
    /// </summary>
    public void ShowSelected()
    {
        EnsureOriginalFrameColor();

        if (frame != null)
        {
            // Green tint on the frame as a fallback
            frame.color = new Color(0.6f, 1f, 0.6f, 1f);
        }

        if (highlightOverlay != null)
        {
            highlightOverlay.enabled = true;
            highlightOverlay.raycastTarget = false;
            // Green-ish, a bit more opaque
            highlightOverlay.color = new Color(0.6f, 1f, 0.6f, 0.45f);
        }
        else
        {
            Debug.LogWarning($"[HandCardHighlight] No highlightOverlay assigned on {name}; only frame tint will be used.");
        }

        transform.localScale = originalScale * 1.10f;

        Debug.Log($"[HandCardHighlight] ShowSelected on {name}");
    }

    private void EnsureOriginalFrameColor()
    {
        if (!hasOriginalFrameColor && frame != null)
        {
            originalFrameColor = frame.color;
            hasOriginalFrameColor = true;
        }
    }
}
