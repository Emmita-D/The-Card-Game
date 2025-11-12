using System;
using UnityEngine;
using UnityEngine.UI;
using Game.Match.Battle;

namespace Game.Match.UI
{
    public class BattleUnitThumbnail : MonoBehaviour
    {
        [Header("UI (assign on prefab)")]
        [SerializeField] private Button button;          // root Button on UnitThumb
        [SerializeField] private Image artImage;        // CHILD Image named "Art"
        [SerializeField] private GameObject selectedFx;  // optional overlay

        [Header("Slice / Crop")]
        [Tooltip("Shift the art up (+) or down (-) inside the clipped tile (pixels).")]
        [SerializeField] private float sliceOffsetY = 36f; // try 36–48 for a 'top slice'

        public UnitAgent BoundAgent { get; private set; }
        public Sprite FullSprite { get; private set; }

        private Action<BattleUnitThumbnail> onClick;

        private RectTransform ArtRT => artImage ? artImage.rectTransform : null;
        private RectTransform TileRT => (RectTransform)transform;

        private void Reset()
        {
            if (!button) button = GetComponent<Button>();
            if (!artImage) artImage = transform.Find("Art")?.GetComponent<Image>();
        }

        public void Bind(UnitAgent agent, Sprite thumb, Sprite full, Action<BattleUnitThumbnail> onClicked)
        {
            BoundAgent = agent;
            FullSprite = full;
            onClick = onClicked;

            if (!button) button = GetComponent<Button>();
            if (!artImage) artImage = transform.Find("Art")?.GetComponent<Image>();

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke(this));

            artImage.enabled = true;
            artImage.material = null;
            artImage.color = Color.white;
            artImage.type = Image.Type.Simple;
            artImage.preserveAspect = true;   // keep aspect
            artImage.maskable = true;   // clipped by UnitThumb's RectMask2D
            artImage.raycastTarget = false;
            artImage.sprite = thumb;

            FitArtToCoverAndSlice();
            SetSelected(false);
        }

        public void Unbind()
        {
            if (button) button.onClick.RemoveAllListeners();
            BoundAgent = null;
            FullSprite = null;
            SetSelected(false);
        }

        public void SetSelected(bool v)
        {
            if (!selectedFx) return;
            var cg = selectedFx.GetComponent<CanvasGroup>();
            if (!cg) cg = selectedFx.AddComponent<CanvasGroup>();
            selectedFx.SetActive(true);
            cg.alpha = v ? 1f : 0f;   // or tween if you like
            selectedFx.SetActive(v);
        }

        // If the tile resizes (e.g., you change Grid cell size), refit the art
        private void OnRectTransformDimensionsChange()
        {
            if (artImage && artImage.sprite) FitArtToCoverAndSlice();
        }

        private void FitArtToCoverAndSlice()
        {
            if (!artImage || artImage.sprite == null || ArtRT == null || TileRT == null) return;

            // Tile size
            var tileRect = TileRT.rect;
            float tileW = Mathf.Max(1f, tileRect.width);
            float tileH = Mathf.Max(1f, tileRect.height);

            // Sprite pixel size
            var sRect = artImage.sprite.rect;
            float sprW = Mathf.Max(1f, sRect.width);
            float sprH = Mathf.Max(1f, sRect.height);

            // Scale to COVER (no letterbox, preserves aspect)
            float scale = Mathf.Max(tileW / sprW, tileH / sprH);
            float drawW = sprW * scale;
            float drawH = sprH * scale;

            // Center anchors so we can offset cleanly
            ArtRT.anchorMin = ArtRT.anchorMax = new Vector2(0.5f, 0.5f);
            ArtRT.pivot = new Vector2(0.5f, 0.5f);
            ArtRT.sizeDelta = new Vector2(drawW, drawH);

            // Slice: push image up (+) or down (-) inside the masked tile
            ArtRT.anchoredPosition = new Vector2(0f, sliceOffsetY);
            ArtRT.localScale = Vector3.one;
        }
    }
}
