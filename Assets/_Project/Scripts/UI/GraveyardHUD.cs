using System.Text;
using System.Reflection;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems; // <— added
using Game.Match.Cards;
using Game.Match.Graveyard;   // GraveyardService / GraveyardRealm

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Local player's graveyard viewer (Empyrean / Infernum).
    /// Shows newest-first. If CardViewPrefab is assigned, renders real cards;
    /// otherwise falls back to text list (listText).
    /// </summary>
    public class GraveyardHUD : MonoBehaviour
    {
        [Header("Ownership")]
        [Tooltip("Owner id for THIS client/player UI (e.g., 0).")]
        [SerializeField] private int localOwnerId = 0;

        [Header("Popup UI")]
        [SerializeField] private GameObject panel;   // set inactive by default
        [SerializeField] private TMP_Text title;

        [Header("Text Fallback (optional)")]
        [SerializeField] private TMP_Text listText;  // used only if no prefab assigned

        [Header("Card Grid (preferred)")]
        [Tooltip("Scroll View -> Viewport -> Content transform")]
        [SerializeField] private Transform content;  // where card prefabs will be spawned
        [Tooltip("Card UI prefab (same you use in hand).")]
        [SerializeField] private GameObject cardViewPrefab;
        [Tooltip("Scale for spawned card views in the list (1 = original).")]
        [SerializeField] private float cardScale = 0.9f;
        [Tooltip("Optional: clear content when the panel closes.")]
        [SerializeField] private bool clearOnClose = false;

        [Header("Counters (optional)")]
        [SerializeField] private TMP_Text empyreanCount;  // small label near left portal
        [SerializeField] private TMP_Text infernumCount;  // small label near right portal

        private GraveyardService svc;

        void Awake()
        {
            svc = GraveyardService.Instance;
            if (svc != null) svc.OnChanged += OnServiceChanged;
        }

        void OnEnable()
        {
            if (panel) panel.SetActive(false); // keep closed until clicked
            RefreshCounts();
        }

        void OnDestroy()
        {
            if (svc != null) svc.OnChanged -= OnServiceChanged;
        }

        // --- Public button hooks (wire these to your two images/buttons) ---
        public void ShowEmpyrean() => Show(GraveyardRealm.Empyrean);
        public void ShowInfernum() => Show(GraveyardRealm.Infernum);

        public void ClosePanel()
        {
            if (panel) panel.SetActive(false);
            if (clearOnClose && content != null) ClearContent();
        }

        // --- Internal ---
        void OnServiceChanged(int ownerId)
        {
            // -1 = clear all; otherwise update only when our owner changed
            if (ownerId == -1 || ownerId == localOwnerId) RefreshCounts();
        }

        void RefreshCounts()
        {
            if (svc == null) return;
            if (empyreanCount) empyreanCount.text = svc.Count(localOwnerId, GraveyardRealm.Empyrean).ToString();
            if (infernumCount) infernumCount.text = svc.Count(localOwnerId, GraveyardRealm.Infernum).ToString();
        }

        void Show(GraveyardRealm realm)
        {
            if (svc == null) return;

            if (title)
                title.text = (realm == GraveyardRealm.Empyrean) ? "Empyrean Graveyard" : "Infernum Graveyard";

            var cards = svc.Get(localOwnerId, realm);

            // If we have a card prefab + content, render real cards
            bool useCards = (cardViewPrefab != null && content != null);

            if (useCards)
            {
                PopulateContentWithCardsNewestFirst(cards);
                if (listText) listText.text = ""; // keep text hidden/empty when using cards
            }
            else
            {
                // Text fallback (existing behavior)
                if (listText)
                {
                    if (cards.Count == 0)
                    {
                        listText.text = "(Empty)";
                    }
                    else
                    {
                        var sb = new StringBuilder(cards.Count * 12);
                        for (int i = cards.Count - 1; i >= 0; --i)
                        {
                            var so = cards[i];
                            sb.AppendLine(so != null ? so.cardName : "(null)");
                        }
                        listText.text = sb.ToString();
                    }
                }
            }

            if (panel) panel.SetActive(true);
        }

        void PopulateContentWithCardsNewestFirst(System.Collections.Generic.IReadOnlyList<CardSO> cards)
        {
            ClearContent();

            if (cards == null || cards.Count == 0)
            {
                // Optional: show a tiny "(Empty)" placeholder
                if (listText) listText.text = "(Empty)";
                return;
            }

            for (int i = cards.Count - 1; i >= 0; --i) // newest-first
            {
                var so = cards[i];
                if (so == null) continue;

                var go = Instantiate(cardViewPrefab, content);
                var rt = go.transform as RectTransform;
                if (rt != null)
                {
                    rt.localScale = Vector3.one * Mathf.Max(0.1f, cardScale);
                    rt.anchoredPosition3D = Vector3.zero;
                }

                // Try to bind the card using common CardView APIs.
                if (!TryBindToCardView(go, so, localOwnerId))
                {
                    // Last-resort: add a small TMP label so it's never blank.
                    var label = go.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = so.cardName;
                }

                // Freeze/disable hover/drag so it stays in the panel & scrolls smoothly
                MakeCardNonInteractive(go);

                // Add hover-only zoom (keeps ScrollRect working)
                GraveyardCardHoverZoom.AddTo(go, /*hoverScale:*/ 1.25f);
            }

            // If Content has HorizontalLayoutGroup/ContentSizeFitter, it will lay out automatically.
        }

        void ClearContent()
        {
            for (int i = content.childCount - 1; i >= 0; --i)
                Destroy(content.GetChild(i).gameObject);
        }

        // --- Reflection helpers to support different CardView APIs without refactor ---

        static bool TryBindToCardView(GameObject go, CardSO so, int ownerId)
        {
            if (go == null || so == null) return false;

            // 1) Look for a CardView-ish component
            var comps = go.GetComponents<Component>();
            Component best = null;
            foreach (var c in comps)
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (tn == "CardView" || tn == "UICardView" || tn == "CardWidget" || tn == "CardDisplay")
                {
                    best = c; break;
                }
            }
            if (best == null) return false;

            // 2) Try InitFrom(CardInstance) since your project uses CardInstance in hand
            var ci = new CardInstance(so, ownerId);
            if (InvokeIfExists(best, "InitFrom", new object[] { ci })) return true;

            // 3) Try InitFrom(CardSO) or Bind/Card/Set* with CardSO
            if (InvokeIfExists(best, "InitFrom", new object[] { so })) return true;
            if (InvokeIfExists(best, "Bind", new object[] { so })) return true;
            if (InvokeIfExists(best, "SetData", new object[] { so })) return true;
            if (InvokeIfExists(best, "SetCard", new object[] { so })) return true;
            if (InvokeIfExists(best, "Set", new object[] { so })) return true;

            // 4) Some views expose a public 'instance' or 'data' field/property
            if (TrySetMember(best, "instance", ci)) return true;
            if (TrySetMember(best, "data", so)) return true;

            return false;
        }

        static bool InvokeIfExists(object target, string method, object[] args)
        {
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var t = target.GetType();
            var mi = t.GetMethod(method, BF);
            if (mi == null) return false;
            var prms = mi.GetParameters();
            if (args.Length != prms.Length) return false;
            // quick param type compatibility check
            for (int i = 0; i < prms.Length; i++)
            {
                if (args[i] == null) continue;
                if (!prms[i].ParameterType.IsInstanceOfType(args[i])) return false;
            }
            mi.Invoke(target, args);
            return true;
        }

        static bool TrySetMember(object target, string name, object value)
        {
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var t = target.GetType();
            var f = t.GetField(name, BF);
            if (f != null && f.FieldType.IsInstanceOfType(value))
            {
                f.SetValue(target, value);
                return true;
            }
            var p = t.GetProperty(name, BF);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value))
            {
                p.SetValue(target, value, null);
                return true;
            }
            return false;
        }

        // --- Disable interaction so hover from hand doesn’t trigger in graveyard ---

        void MakeCardNonInteractive(GameObject go)
        {
            if (!go) return;

            // Let the ScrollRect receive drag; card is display-only
            var cg = go.GetComponent<CanvasGroup>();
            if (!cg) cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Disable common interactive behaviours if present
            DisableIfPresent(go, "DraggableCard");
            DisableIfPresent(go, "CardHoverFX");

            // Built-in event trigger
            var ev = go.GetComponentInChildren<EventTrigger>(true);
            if (ev) ev.enabled = false;

            // Catch-all: disable any MonoBehaviour implementing pointer/drag handlers
            var comps = go.GetComponentsInChildren<Component>(true);
            foreach (var c in comps)
            {
                if (c is MonoBehaviour mb)
                {
                    if (c is IPointerEnterHandler ||
                        c is IPointerExitHandler ||
                        c is IPointerDownHandler ||
                        c is IPointerUpHandler ||
                        c is IBeginDragHandler ||
                        c is IDragHandler ||
                        c is IEndDragHandler)
                    {
                        mb.enabled = false;
                    }
                }
            }
        }

        static void DisableIfPresent(GameObject go, string typeName)
        {
            var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                var mb = comps[i];
                if (mb == null) continue;
                if (mb.GetType().Name == typeName) { mb.enabled = false; }
            }
        }
    }
}
