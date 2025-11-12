using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Game.Match.Battle;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // Mouse.current, Touchscreen.current
#endif

namespace Game.Match.UI
{
    /// <summary>
    /// Bottom bar showing local (friendly) units as thumbnails.
    /// GridLayoutGroup + ContentSizeFitter so the Content never collapses.
    /// Supports tall tiles, outside-click deselect, and auto-removal on unit death.
    /// </summary>
    public class BattleUnitBarUI : MonoBehaviour
    {
        [Header("Scene Refs")]
        [SerializeField] private RectTransform content;                 // Viewport/Content
        [SerializeField] private ScrollRect scrollRect;                 // parent ScrollRect
        [SerializeField] private BattleUnitThumbnail thumbnailPrefab;   // prefab
        [SerializeField] private CardPreviewPanel previewPanel;         // left preview
        [SerializeField] private Sprite fallbackSprite;                 // placeholder

        [Header("Behaviour")]
        [SerializeField] private int localOwnerId = 0;
        [SerializeField, Range(0.05f, 1f)] private float pollInterval = 0.25f;

        [Header("Layout")]
        [SerializeField] private float thumbWidth = 96f;
        [SerializeField] private float thumbHeight = 380f;
        [SerializeField] private float spacingX = 8f;

        [Header("Click Deselect")]
        [Tooltip("RectTransform of the whole bar (e.g., UnitBarPanel). Used to avoid clearing when you click inside the bar background.")]
        [SerializeField] private RectTransform unitBarPanel; // drag UnitBarPanel here

        [Tooltip("If true, clicks inside the bar BACKGROUND will NOT clear the selection. Clicks outside the bar will clear.")]
        [SerializeField] private bool keepSelectionWhenClickingBarBackground = true;

        private CombatResolver resolver;
        private readonly Queue<BattleUnitThumbnail> pool = new Queue<BattleUnitThumbnail>(16);
        private readonly Dictionary<UnitAgent, BattleUnitThumbnail> byAgent = new Dictionary<UnitAgent, BattleUnitThumbnail>(32);
        private WaitForSeconds waitPoll;

        // Track current selection
        private BattleUnitThumbnail currentSelected;

        // --- tiny relay attached to unit objects so we know when they're destroyed
        private class UnitLifeRelay : MonoBehaviour
        {
            public UnitAgent agent;
            public BattleUnitBarUI bar;

            public void Init(UnitAgent a, BattleUnitBarUI b) { agent = a; bar = b; }

            private void OnDestroy()
            {
                // Will be called when the unit GameObject is destroyed
                if (bar != null && agent != null) bar.NotifyAgentDestroyed(agent);
            }
        }

        private void Awake()
        {
            EnsureScrollHierarchy();   // make sure Content has Grid+Fitter and proper anchors
            waitPoll = new WaitForSeconds(pollInterval);
        }

        private void Update()
        {
            // Global click: if not on a thumbnail and (optionally) outside the bar, clear selection
            Vector2 screenPos;
            if (GetPointerDown(out screenPos))
                TryDeselectOnOutsideClick(screenPos);
        }

        public void Initialize(CombatResolver combatResolver, int localOwner)
        {
            resolver = combatResolver ? combatResolver : FindObjectOfType<CombatResolver>();
            localOwnerId = localOwner;

            SyncNow(true);
            StartCoroutine(PollLoop());
            Debug.Log("[UnitBar] Initialized.");
        }

        public void BootstrapFrom(IReadOnlyList<UnitAgent> locals)
        {
            int added = 0;
            if (locals != null)
            {
                for (int i = 0; i < locals.Count; i++)
                {
                    var ua = locals[i];
                    if (ua && ua.ownerId == localOwnerId && !byAgent.ContainsKey(ua))
                    { AddUnit(ua); added++; }
                }
            }
            Debug.Log($"[UnitBar] {added} local units found.");
            SnapLeft();
        }

        private IEnumerator PollLoop()
        {
            while (true) { yield return waitPoll; SyncNow(false); }
        }

        private void SyncNow(bool firstPass)
        {
            if (!resolver) return;

            var locals = resolver.LocalUnits;

            // Add any new locals
            for (int i = 0; i < locals.Count; i++)
            {
                var ua = locals[i];
                if (ua && ua.ownerId == localOwnerId && !byAgent.ContainsKey(ua))
                    AddUnit(ua);
            }

            // Remove those that died or disappeared
            var toRemove = new List<UnitAgent>();
            foreach (var kv in byAgent)
            {
                var ua = kv.Key;
                if (ua == null) { toRemove.Add(ua); continue; }

                bool stillListed = false;
                for (int i = 0; i < locals.Count; i++) if (locals[i] == ua) { stillListed = true; break; }

                // Treat as dead if:
                //  - no longer in resolver.LocalUnits
                //  - OR GameObject destroyed/disabled
                //  - OR has an 'IsAlive/Alive/isAlive' flag that's now false (on UnitAgent or its runtime)
                if (!stillListed || IsAgentDead(ua))
                    toRemove.Add(ua);
            }
            for (int i = 0; i < toRemove.Count; i++) RemoveUnit(toRemove[i]);

            // Keep spawn order
            int sib = 0;
            for (int i = 0; i < locals.Count; i++)
            {
                var ua = locals[i];
                if (ua && ua.ownerId == localOwnerId && byAgent.TryGetValue(ua, out var v))
                    v.transform.SetSiblingIndex(sib++);
            }

            SnapLeft();
            if (firstPass) Debug.Log($"[UnitBar] Sync complete. Alive locals tracked: {byAgent.Count}.");
        }

        private void AddUnit(UnitAgent ua)
        {
            Sprite art = (ua && ua.sourceCard) ? ua.sourceCard.artSprite : null;
            if (art == null) art = fallbackSprite;

            var v = pool.Count > 0 ? pool.Dequeue() : Instantiate(thumbnailPrefab, content);
            v.gameObject.SetActive(true);
            v.Bind(ua, art, art, OnClick);
            byAgent[ua] = v;

            // Attach life relay so we remove instantly if the unit is destroyed
            var relay = ua.gameObject.GetComponent<UnitLifeRelay>();
            if (relay == null) relay = ua.gameObject.AddComponent<UnitLifeRelay>();
            relay.Init(ua, this);

            Debug.Log($"[UnitBar] Added {ua?.sourceCard?.cardName ?? "Unit"} sprite={(art ? art.name : "NULL")}");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            SnapLeft();
        }

        private void RemoveUnit(UnitAgent ua)
        {
            if (ua == null) return;
            if (byAgent.TryGetValue(ua, out var v))
            {
                v.Unbind();
                v.gameObject.SetActive(false);
                v.transform.SetParent(content, false);
                pool.Enqueue(v);
                byAgent.Remove(ua);
                if (previewPanel && previewPanel.CurrentAgent == ua) previewPanel.Clear();
                if (currentSelected == v) currentSelected = null;

                Debug.Log($"[UnitBar] Removed {ua.sourceCard?.cardName ?? "Unit"}.");
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                SnapLeft();
            }
        }

        private void OnClick(BattleUnitThumbnail v)
        {
            currentSelected = v;
            foreach (var kv in byAgent) kv.Value.SetSelected(kv.Value == v);
            if (previewPanel) previewPanel.Show(v.BoundAgent, v.FullSprite);
        }

        private void ClearSelection()
        {
            if (currentSelected == null && (previewPanel == null || previewPanel.CurrentAgent == null))
                return;

            currentSelected = null;
            foreach (var kv in byAgent) kv.Value.SetSelected(false);
            if (previewPanel) previewPanel.Clear();
        }

        // Called by the UnitLifeRelay when the unit object gets destroyed
        internal void NotifyAgentDestroyed(UnitAgent ua)
        {
            RemoveUnit(ua);
        }

        // ---- Layout bootstrap (creates a sane Grid+Fitter even if children were nuked) ----
        private void EnsureScrollHierarchy()
        {
            if (!content)
            {
                var vp = transform.GetComponentInChildren<Mask>(true); // find viewport via any Mask/RectMask2D
                if (vp) content = vp.transform.Find("Content") as RectTransform;
            }
            if (!scrollRect) scrollRect = GetComponentInParent<ScrollRect>();

            if (scrollRect)
            {
                scrollRect.horizontal = true;
                scrollRect.vertical = false;
            }

            if (content)
            {
                // Anchor/pivot so width grows rightwards
                content.anchorMin = new Vector2(0f, 0.5f);
                content.anchorMax = new Vector2(0f, 0.5f);
                content.pivot = new Vector2(0f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, thumbHeight);

                var grid = content.GetComponent<GridLayoutGroup>() ?? content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(thumbWidth, thumbHeight);
                grid.spacing = new Vector2(spacingX, 0f);
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                grid.constraintCount = 1;
                grid.childAlignment = TextAnchor.MiddleLeft;

                var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                if (scrollRect) scrollRect.content = content;
            }
        }

        private void SnapLeft()
        {
            if (!content) return;
            content.anchoredPosition = Vector2.zero;
            if (scrollRect) scrollRect.horizontalNormalizedPosition = 0f;
        }

        // ----------------- Global click logic -----------------
        private void TryDeselectOnOutsideClick(Vector2 screenPos)
        {
            // If you click a thumbnail, selection is handled by OnClick; don't clear here.
            if (IsPointerOverThumbnail(screenPos)) return;

            // If you clicked inside the bar background and you want to KEEP selection while scrolling, return.
            if (keepSelectionWhenClickingBarBackground && IsPointerInside(unitBarPanel, screenPos))
                return;

            // Otherwise, clear selection + preview
            ClearSelection();
        }

        private bool IsPointerOverThumbnail(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;

            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject.GetComponentInParent<BattleUnitThumbnail>() != null)
                    return true;
            }
            return false;
        }

        private bool IsPointerInside(RectTransform rt, Vector2 screenPos)
        {
            if (!rt) return false;
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam);
        }

        // Unified pointer-down for new Input System and legacy Input
        private bool GetPointerDown(out Vector2 screenPos)
        {
            screenPos = default;

#if ENABLE_INPUT_SYSTEM
            // Mouse click
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }
            // Touch tap
            if (Touchscreen.current != null)
            {
                var t = Touchscreen.current.primaryTouch;
                if (t.press.wasPressedThisFrame)
                {
                    screenPos = t.position.ReadValue();
                    return true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPos = Input.mousePosition;
                return true;
            }
#endif
            return false;
        }

        // -------------- Death heuristics --------------
        private bool IsAgentDead(UnitAgent ua)
        {
            // destroyed reference?
            if (ua == null) return true;

            var go = ua.gameObject;
            if (go == null) return true;

            // treat disabled object as "dead" for UI purposes
            if (!go.activeInHierarchy) return true;

            // Look for common flags on UnitAgent or its runtime: IsAlive/Alive/isAlive (bool)
            try
            {
                // on the agent
                if (CheckAliveFlag(ua, out bool alive1)) return !alive1;

                // on a 'runtime' field/property
                object runtime = null;
                var t = ua.GetType();
                var pf = t.GetField("runtime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var pp = t.GetProperty("runtime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var pP = t.GetProperty("Runtime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pf != null) runtime = pf.GetValue(ua);
                else if (pp != null) runtime = pp.GetValue(ua);
                else if (pP != null) runtime = pP.GetValue(ua);

                if (runtime != null && CheckAliveFlag(runtime, out bool alive2)) return !alive2;
            }
            catch { /* ignore reflection issues */ }

            return false; // assume alive if nothing obvious says otherwise
        }

        private bool CheckAliveFlag(object obj, out bool alive)
        {
            alive = true;
            if (obj == null) return false;

            var tt = obj.GetType();
            // Properties first
            var prop = tt.GetProperty("IsAlive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetProperty("Alive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetProperty("isAlive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetProperty("IsDead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                var v = (bool)prop.GetValue(obj);
                alive = (prop.Name == "IsDead") ? !v : v;
                return true;
            }

            // Fields
            var field = tt.GetField("IsAlive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetField("Alive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetField("isAlive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? tt.GetField("IsDead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(bool))
            {
                var v = (bool)field.GetValue(obj);
                alive = (field.Name == "IsDead") ? !v : v;
                return true;
            }

            return false;
        }
    }
}
