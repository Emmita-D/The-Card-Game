using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;  // New Input System
#endif

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Hover-only zoom for graveyard cards.
    /// - Uses viewport-clipped hit test: only the visible portion of the card can trigger hover.
    /// - Reparents hovered card to a top overlay (no mask clipping) with a spacer left behind.
    /// - Guarantees only one card zoomed at a time.
    /// </summary>
    public class GraveyardCardHoverZoom : MonoBehaviour
    {
        [Tooltip("The RectTransform of the card to scale.")]
        public RectTransform target;

        [Tooltip("Scale when hovering (1 = original).")]
        public float scale = 1.25f;

        [Tooltip("How fast to lerp towards target scale.")]
        public float lerp = 20f;

        [Tooltip("Optional overlay root. If null, created under the root canvas as 'HoverOverlay'.")]
        public RectTransform overlayRoot;

        [Tooltip("Viewport of the ScrollView that contains the cards. If null, auto-detected.")]
        public RectTransform viewport;

        // --- runtime
        static GraveyardCardHoverZoom Active; // enforce single hovered card
        RectTransform baseParent;
        RectTransform spacer;
        int baseSibling = -1;
        bool isHover, isOverlay;
        Coroutine anim;
        Canvas canvas;

        bool _enabledByGraveyardHUD = false;
        // Keep a single dedicated overlay per graveyard panel and clean it up reliably
        static RectTransform s_overlay; // dedicated overlay for the graveyard panel
        const string OverlayName = "GraveyardHoverOverlay";

        void Awake()
        {
            if (target == null) target = transform as RectTransform;
            canvas = GetComponentInParent<Canvas>();
            EnsureOverlayExists();
            EnsureViewport();
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            if (isOverlay) ForceRestoreNow();
            CleanupOverlay();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (isOverlay) ForceRestoreNow();
            CleanupOverlay();
        }

        void Update()
        {
            if (!_enabledByGraveyardHUD) return;

            if (target == null) return;

            // If another card is active, don't even try to hover this one.
            if (Active != null && Active != this)
            {
                if (isHover) { isHover = false; StartAnim(1f); }
                return;
            }

            Vector2 screenPos = GetPointerPosition();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                         ? canvas.worldCamera : null;

            bool over = RectTransformUtility.RectangleContainsScreenPoint(target, screenPos, cam);

            // NEW: require the pointer to also be inside the Viewport AND within the
            // visible (unmasked) intersection between target and viewport.
            if (over && viewport != null)
                over = PointInsideIntersection(target, viewport, screenPos, cam);

            if (over && !isHover)
            {
                if (Active != this) Active = this;
                isHover = true;
                MoveToOverlayWithSpacer();
                StartAnim(scale);
            }
            else if (!over && isHover)
            {
                isHover = false;
                StartAnim(1f);
            }
        }

        // ---------- core ops ----------

        void MoveToOverlayWithSpacer()
        {
            if (overlayRoot == null || isOverlay) return;

            // paranoia: ensure overlay has no other zoomed cards
            for (int i = overlayRoot.childCount - 1; i >= 0; --i)
            {
                var c = overlayRoot.GetChild(i);
                if (c == target) continue;
                var z = c.GetComponent<GraveyardCardHoverZoom>();
                if (z != null && z != this && z.isOverlay) z.ForceRestoreNow();
            }

            baseParent = target.parent as RectTransform;
            baseSibling = target.GetSiblingIndex();

            // Spacer at same slot to keep layout width
            spacer = new GameObject("HoverSpacer", typeof(RectTransform), typeof(LayoutElement))
                        .GetComponent<RectTransform>();
            spacer.SetParent(baseParent, false);
            spacer.SetSiblingIndex(baseSibling);

            CloneLayoutToSpacer(target, spacer);

            // Reparent to overlay, preserving world position (no jump)
            target.SetParent(overlayRoot, true);
            target.SetAsLastSibling();

            isOverlay = true;
        }

        void RestoreFromOverlay()
        {
            // drop any temp state first
            if (!isOverlay || baseParent == null) { DestroySpacer(); return; }

            target.SetParent(baseParent, true);
            if (baseSibling >= 0) target.SetSiblingIndex(baseSibling);
            isOverlay = false;

            DestroySpacer();
        }

        void ForceRestoreNow()
        {
            isHover = false;
            if (anim != null) { StopCoroutine(anim); anim = null; }
            if (target != null) target.localScale = Vector3.one;
            RestoreFromOverlay();
            if (Active == this) Active = null;
        }

        void DestroySpacer()
        {
            if (spacer != null) { Destroy(spacer.gameObject); spacer = null; }
        }

        void StartAnim(float toScale)
        {
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(AnimTo(toScale));
        }

        IEnumerator AnimTo(float to)
        {
            while (true)
            {
                float s = target.localScale.x;
                float ns = Mathf.Lerp(s, to, Time.unscaledDeltaTime * lerp);
                target.localScale = new Vector3(ns, ns, ns);

                if (Mathf.Abs(ns - to) < 0.001f)
                {
                    target.localScale = new Vector3(to, to, to);
                    if (!isHover)
                    {
                        RestoreFromOverlay();
                        if (Active == this) Active = null;
                    }
                    yield break;
                }
                yield return null;
            }
        }

        // ---------- helpers ----------

        void EnsureOverlayExists()
        {
            if (overlayRoot != null) return;

            // Prefer parenting under the Graveyard HUD so it dies with the panel
            RectTransform hudRoot = null;
            var hud = GetComponentInParent<GraveyardHUD>();
            if (hud != null)
            {
                hudRoot = (hud.transform as RectTransform)
                          ?? hud.GetComponentInParent<Canvas>()?.transform as RectTransform;
            }

            var parentForOverlay = hudRoot;
            if (parentForOverlay == null)
            {
                var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
                parentForOverlay = rootCanvas ? rootCanvas.transform as RectTransform : null;
            }
            if (parentForOverlay == null) return;

            const string OverlayName = "GraveyardHoverOverlay";
            var existing = parentForOverlay.Find(OverlayName);
            if (existing != null)
            {
                overlayRoot = (RectTransform)existing;
                return;
            }

            // Create dedicated overlay
            var go = new GameObject(OverlayName, typeof(RectTransform), typeof(Canvas));
            overlayRoot = go.GetComponent<RectTransform>();
            overlayRoot.SetParent(parentForOverlay, false);
            overlayRoot.anchorMin = Vector2.zero;
            overlayRoot.anchorMax = Vector2.one;
            overlayRoot.offsetMin = Vector2.zero;
            overlayRoot.offsetMax = Vector2.zero;
            overlayRoot.SetAsLastSibling();

            // Match root canvas mode/camera, but FORCE a very high sorting order
            var parentCanvas = parentForOverlay.GetComponentInParent<Canvas>();
            var cv = go.GetComponent<Canvas>();

            if (parentCanvas != null)
            {
                cv.renderMode = parentCanvas.renderMode;
                if (cv.renderMode == RenderMode.ScreenSpaceCamera)
                    cv.worldCamera = parentCanvas.worldCamera;
                cv.sortingLayerID = parentCanvas.sortingLayerID;
            }
            else
            {
                cv.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            cv.overrideSorting = true;
            // Make sure we’re above CardView HUD and any other popups.
            // (32760 is safely below Unity’s 32767 cap but above typical UI.)
            cv.sortingOrder = 32760;

            // done
        }
        void EnsureViewport()
        {
            if (viewport != null) return;
            var sr = GetComponentInParent<ScrollRect>();
            if (sr != null) viewport = sr.viewport;
        }

        Vector2 GetPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
            return Input.mousePosition;
#endif
        }

        static void CloneLayoutToSpacer(RectTransform src, RectTransform dst)
        {
            // Mirror measured size so the row doesn't shift
            float prefW = LayoutUtility.GetPreferredWidth(src);
            float prefH = LayoutUtility.GetPreferredHeight(src);
            float minW = LayoutUtility.GetMinWidth(src);
            float minH = LayoutUtility.GetMinHeight(src);

            var srcLE = src.GetComponent<LayoutElement>();
            var dstLE = dst.GetComponent<LayoutElement>();
            dstLE.ignoreLayout = false;

            dstLE.preferredWidth = (srcLE && srcLE.preferredWidth > 0f) ? srcLE.preferredWidth : prefW;
            dstLE.preferredHeight = (srcLE && srcLE.preferredHeight > 0f) ? srcLE.preferredHeight : prefH;
            dstLE.minWidth = (srcLE && srcLE.minWidth > 0f) ? srcLE.minWidth : Mathf.Min(minW, dstLE.preferredWidth);
            dstLE.minHeight = (srcLE && srcLE.minHeight > 0f) ? srcLE.minHeight : Mathf.Min(minH, dstLE.preferredHeight);
            dstLE.flexibleWidth = (srcLE) ? srcLE.flexibleWidth : 0f;
            dstLE.flexibleHeight = (srcLE) ? srcLE.flexibleHeight : 0f;

            dst.anchorMin = src.anchorMin;
            dst.anchorMax = src.anchorMax;
            dst.pivot = src.pivot;
        }

        /// <summary>
        /// True only if the pointer is inside BOTH the card rect AND the visible part of that card
        /// after ScrollRect masking (i.e., inside the viewport & the card's intersection with it).
        /// </summary>
        static bool PointInsideIntersection(RectTransform card, RectTransform viewport, Vector2 screenPos, Camera cam)
        {
            // Quick reject: pointer must be inside viewport at all
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPos, cam))
                return false;

            // Compute the intersection rect in screen space between card and viewport.
            Vector3[] cW = new Vector3[4];
            Vector3[] vW = new Vector3[4];
            card.GetWorldCorners(cW);
            viewport.GetWorldCorners(vW);

            // Convert to screen space
            Vector2 cMin = RectTransformUtility.WorldToScreenPoint(cam, cW[0]);
            Vector2 cMax = RectTransformUtility.WorldToScreenPoint(cam, cW[2]);
            Vector2 vMin = RectTransformUtility.WorldToScreenPoint(cam, vW[0]);
            Vector2 vMax = RectTransformUtility.WorldToScreenPoint(cam, vW[2]);

            float left = Mathf.Max(cMin.x, vMin.x);
            float right = Mathf.Min(cMax.x, vMax.x);
            float bottom = Mathf.Max(cMin.y, vMin.y);
            float top = Mathf.Min(cMax.y, vMax.y);

            if (right <= left || top <= bottom) return false; // no visible overlap

            return (screenPos.x >= left && screenPos.x <= right &&
                    screenPos.y >= bottom && screenPos.y <= top);
        }
        /// <summary>
        /// Add (or reuse) a zoom component on a card and configure it.
        /// Keeps the API that GraveyardHUD calls.
        /// </summary>
        public static GraveyardCardHoverZoom AddTo(
            GameObject cardGO,
            float hoverScale = 1.25f,
            RectTransform optionalOverlay = null,
            RectTransform optionalViewport = null)
        {
            if (cardGO == null) return null;

            var zoom = cardGO.GetComponent<GraveyardCardHoverZoom>();
            if (zoom == null) zoom = cardGO.AddComponent<GraveyardCardHoverZoom>();

            zoom.target = cardGO.transform as RectTransform;
            zoom.scale = hoverScale;

            if (optionalOverlay != null) zoom.overlayRoot = optionalOverlay;
            if (optionalViewport != null) zoom.viewport = optionalViewport;

            zoom._enabledByGraveyardHUD = true;   // <<< important
            zoom.Rebind(optionalOverlay, optionalViewport);
            return zoom;
        }
        public static void CleanupOverlay()
        {
            if (s_overlay == null) return;

            // Restore any children before destroying overlay (safety)
            for (int i = s_overlay.childCount - 1; i >= 0; --i)
            {
                var child = s_overlay.GetChild(i) as RectTransform;
                if (child == null) continue;
                var z = child.GetComponent<GraveyardCardHoverZoom>();
                if (z != null && z.isOverlay)
                {
                    z.ForceRestoreNow();
                }
            }

            if (Application.isPlaying) Object.Destroy(s_overlay.gameObject);
            else Object.DestroyImmediate(s_overlay.gameObject);

            s_overlay = null;
            Active = null;
        }
        /// <summary>
        /// Re-evaluate canvas/overlay/viewport bindings immediately.
        /// </summary>
        public void Rebind(RectTransform overlay = null, RectTransform vp = null)
        {
            if (overlay != null) overlayRoot = overlay;
            if (vp != null) viewport = vp;

            _enabledByGraveyardHUD = true;        // <<< important
            canvas = GetComponentInParent<Canvas>();
            EnsureOverlayExists();
            EnsureViewport();
        }
    }
}
