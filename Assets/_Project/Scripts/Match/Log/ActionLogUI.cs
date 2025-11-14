using UnityEngine;
using UnityEngine.UI;

namespace Game.Match.Log
{
    /// <summary>
    /// Panel + ScrollView controller for the action log.
    /// Uses a prefab per entry so we can show an icon + text and click to preview.
    /// </summary>
    public class ActionLogUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private ActionLogItemUI itemPrefab;
        [SerializeField] private bool openOnStart = false;
        [Header("Optional")]
        [SerializeField] private ActionLogCardPreview cardPreview; // can be auto-found

        private void Start()
        {
            if (panelRoot != null)
                panelRoot.SetActive(openOnStart);

            if (cardPreview == null)
            {
                cardPreview = FindObjectOfType<ActionLogCardPreview>(true);
            }

            if (openOnStart)
                Refresh();
        }

        /// <summary>Hook this to the main Log button.</summary>
        public void TogglePanel()
        {
            if (panelRoot == null) return;

            bool show = !panelRoot.activeSelf;
            panelRoot.SetActive(show);

            if (show)
                Refresh();
        }

        /// <summary>Hook this to the X/Close button inside the panel.</summary>
        public void ClosePanel()
        {
            if (panelRoot == null) return;
            panelRoot.SetActive(false);
        }

        public void Refresh()
        {
            if (contentRoot == null || itemPrefab == null)
                return;

            var svc = ActionLogService.Instance;
            var entries = svc.All;

            // Clear old children
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }

            if (entries == null || entries.Count == 0)
            {
                var empty = new ActionEvent(0, LogPhase.Card, LogSide.System, "(No actions logged yet)", null, null);
                var item = Instantiate(itemPrefab, contentRoot);
                item.Setup(empty, OnItemClicked);
            }
            else
            {
                foreach (var e in entries)
                {
                    var item = Instantiate(itemPrefab, contentRoot);
                    item.Setup(e, OnItemClicked);
                }
            }

            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f; // jump to bottom
            }
        }

        private void OnItemClicked(Cards.CardSO card)
        {
            if (cardPreview != null && card != null)
            {
                cardPreview.ShowCard(card);
            }
        }
    }
}
