using UnityEngine;
using UnityEngine.UI;
using System;
using System.Reflection;
using Game.Match.Battle; // UnitAgent
using UnityEngine.EventSystems;

// Optional interface you can implement on your card prefab binder.
// Put this in a separate file if you prefer.
public interface ICardView
{
    void Bind(object cardSO); // kept as object so we don't fight namespaces
}

namespace Game.Match.UI
{
    public class CardPreviewPanel : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("This Image is used when showing just the art. It will not stretch; aspect is preserved.")]
        [SerializeField] private Image previewImage;     // assign the Image on your CardPreviewPanel GO

        [Tooltip("Where the card prefab (if used) will be instantiated. Usually this same RectTransform.")]
        [SerializeField] private RectTransform previewRoot; // assign the RectTransform of CardPreviewPanel

        [Header("Modes")]
        [Tooltip("If assigned and enabled, clicking a thumbnail will show this full card prefab instead of only art.")]
        [SerializeField] private bool useCardPrefab = true;

        [SerializeField] private GameObject cardPrefab;  // drag your full Card UI prefab here

        [Header("Fitting")]
        [Tooltip("Padding applied inside the preview rect (pixels).")]
        [SerializeField] private Vector2 padding = new Vector2(8f, 8f);

        [Tooltip("If your card prefab needs a fixed aspect (W/H). 0 means don't enforce.")]
        [SerializeField] private float cardAspect = 0f; // e.g. 0.7f for 5:7

        [SerializeField] bool disableInteractionsInPreview = true;

        public UnitAgent CurrentAgent { get; private set; }

        GameObject cardInstance;
        Component cachedBinder;   // component that can Bind(cardSO)
        MethodInfo cachedBindMI;   // reflection fallback

        void Reset()
        {
            if (!previewRoot) previewRoot = GetComponent<RectTransform>();
            if (!previewImage) previewImage = GetComponent<Image>();
        }

        // Called from the thumbnail:
        // bar.OnClick -> previewPanel.Show(agent, fullSprite);
        public void Show(UnitAgent agent, Sprite fullSprite)
        {
            CurrentAgent = agent;

            // prefer full card if available
            if (useCardPrefab && cardPrefab && agent && agent.sourceCard != null)
            {
                ShowCard(agent.sourceCard);
                return;
            }

            // otherwise, just show art without stretching
            ShowArt(fullSprite ?? (agent && agent.sourceCard ? agent.sourceCard.artSprite : null));
        }

        public void Clear()
        {
            previewImage.enabled = false;
            if (cardInstance) Destroy(cardInstance);
            cardInstance = null;
            cachedBinder = null;
            cachedBindMI = null;
            CurrentAgent = null;
        }

        // ---------- ART (no stretch) ----------
        void ShowArt(Sprite s)
        {
            if (!previewImage || !previewRoot)
                return;

            if (cardInstance) { Destroy(cardInstance); cardInstance = null; }

            previewImage.enabled = (s != null);
            previewImage.color = Color.white;
            previewImage.sprite = s;
            previewImage.preserveAspect = true;
            previewImage.type = Image.Type.Simple;
            previewImage.raycastTarget = false;
            FitImageContain(previewImage, previewRoot, padding);
        }

        // Fits an Image to CONTAIN inside a rect (letter/pillar if needed), no stretch.
        static void FitImageContain(Image img, RectTransform container, Vector2 pad)
        {
            if (!img || !img.sprite || !container) return;

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var cRect = container.rect;
            float cw = Mathf.Max(1f, cRect.width - (pad.x * 2f));
            float ch = Mathf.Max(1f, cRect.height - (pad.y * 2f));

            var sRect = img.sprite.rect;
            float sw = Mathf.Max(1f, sRect.width);
            float sh = Mathf.Max(1f, sRect.height);

            float scale = Mathf.Min(cw / sw, ch / sh);
            float w = sw * scale;
            float h = sh * scale;

            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // ---------- FULL CARD ----------
        void ShowCard(object cardSO)
        {
            if (!previewRoot) return;

            previewImage.enabled = false;

            // (Re)spawn
            if (!cardInstance)
            {
                cardInstance = Instantiate(cardPrefab, previewRoot);
                var rt = cardInstance.transform as RectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.anchoredPosition = Vector2.zero;

                // Optional: enforce fixed aspect for the card prefab
                if (cardAspect > 0f)
                {
                    var arf = cardInstance.GetComponent<AspectRatioFitter>() ?? cardInstance.AddComponent<AspectRatioFitter>();
                    arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    arf.aspectRatio = cardAspect;
                    if (disableInteractionsInPreview)
                        DisableInteractions(cardInstance);
                }

                // Cache a binder
                cachedBinder = FindBinder(cardInstance);
                cachedBindMI = GetBindMethod(cachedBinder);
            }

            // Bind data (try interface, then reflection, then best-effort set art)
            if (cachedBinder is ICardView icv) icv.Bind(cardSO);
            else if (cachedBindMI != null) cachedBindMI.Invoke(cachedBinder, new object[] { cardSO });
            else BestEffortAssignArt(cardInstance, cardSO);

            // Fit the instance inside the preview rect with padding, preserving aspect.
            FitChildToContain((RectTransform)cardInstance.transform, previewRoot, padding, cardAspect);
        }

        void DisableInteractions(GameObject root)
        {
            if (!root) return;

            // Stop pointer events for the whole tree
            var cg = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Belt-and-suspenders: no child graphics receive raycasts
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            // If the prefab has its own GraphicRaycaster(s), disable them
            foreach (var gr in root.GetComponentsInChildren<GraphicRaycaster>(true))
                gr.enabled = false;

            // Optional: disable typical hover/drag handlers if present
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IPointerEnterHandler || mb is IPointerExitHandler ||
                    mb is IBeginDragHandler || mb is IDragHandler ||
                    mb is IEndDragHandler || mb is IPointerClickHandler ||
                    mb is IScrollHandler)
                    mb.enabled = false;
        }

        // Try to find a component on the card prefab that can bind the SO.
        static Component FindBinder(GameObject go)
        {
            if (!go) return null;

            // Prefer an explicit ICardView
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c is ICardView) return c;

            // Otherwise anything that has a Bind(object) or Bind(CardSO) method
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (GetBindMethod(c) != null) return c;
            }
            return go.transform;
        }

        // Return a Bind method that accepts one parameter (we'll pass the cardSO).
        static MethodInfo GetBindMethod(Component c)
        {
            if (!c) return null;
            var t = c.GetType();
            foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mi.Name != "Bind") continue;
                var p = mi.GetParameters();
                if (p.Length == 1) return mi;
            }
            return null;
        }

        // If no binder, at least try to set an Image named "Art" with card.artSprite
        static void BestEffortAssignArt(GameObject cardGO, object cardSO)
        {
            if (!cardGO || cardSO == null) return;

            // Try to read a field or prop called "artSprite"
            Sprite art = null;
            var soType = cardSO.GetType();
            var f = soType.GetField("artSprite", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) art = f.GetValue(cardSO) as Sprite;
            if (!art)
            {
                var p = soType.GetProperty("artSprite", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) art = p.GetValue(cardSO) as Sprite;
            }

            if (!art) return;

            var artTr = cardGO.transform.Find("Art");
            if (!artTr) artTr = cardGO.transform; // last resort
            var img = artTr.GetComponentInChildren<Image>(true);
            if (img)
            {
                img.sprite = art;
                img.preserveAspect = true;
            }
        }

        // Fit any child rect inside parent while preserving aspect (optionally enforced)
        static void FitChildToContain(RectTransform child, RectTransform parent, Vector2 pad, float forceAspect = 0f)
        {
            if (!child || !parent) return;

            var pr = parent.rect;
            float cw = Mathf.Max(1f, pr.width - pad.x * 2f);
            float ch = Mathf.Max(1f, pr.height - pad.y * 2f);

            float targetAspect = forceAspect > 0f ? forceAspect : (child.rect.width > 0f ? child.rect.width / child.rect.height : 1f);

            // Compute size that CONTAINS inside parent
            float w = cw;
            float h = w / targetAspect;
            if (h > ch)
            {
                h = ch;
                w = h * targetAspect;
            }

            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.sizeDelta = new Vector2(w, h);
            child.anchoredPosition = Vector2.zero;
            child.localScale = Vector3.one;
        }
    }
}
