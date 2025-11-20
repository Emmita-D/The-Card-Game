using System;
using UnityEngine;
using UnityEngine.EventSystems;
using Game.Core;
using Game.Match.Cards;
using Game.Match.Units;

namespace Game.Match.CardPhase
{
    /// <summary>
    /// Small helper attached to CardPhase unit representations.
    /// It knows:
    /// - who owns this unit,
    /// - which CardSO it comes from,
    /// - which UnitRuntime drives its stats.
    ///
    /// It forwards clicks to CardPhaseTargetSelectionController when a selection is in progress,
    /// and can visually highlight itself when it is a valid target.
    /// </summary>
    public class CardPhaseSelectableUnit : MonoBehaviour, IPointerClickHandler
    {
        [Header("Identity")]
        [SerializeField] private int ownerId;
        [SerializeField] private CardSO card;
        [SerializeField] private Realm realm;
        [SerializeField] private UnitRuntime runtime;

        [Header("Highlight (v1)")]
        [SerializeField] private bool useHighlightTint = true;
        [SerializeField] private Color highlightColor = Color.yellow;

        private Renderer[] _renderers;
        private Color[] _baseColors;
        private bool _highlighted;

        public int OwnerId => ownerId;
        public CardSO Card => card;
        public Realm Realm => realm;
        public UnitRuntime Runtime => runtime;

        /// <summary>
        /// Called from DraggableCard when the unit is spawned on the CardPhase grid.
        /// </summary>
        public void InitializeForCardPhase(int owner, CardSO so, UnitRuntime rt)
        {
            ownerId = owner;
            card = so;
            realm = (so != null) ? so.realm : Realm.Empyrean;
            runtime = rt;

            CacheRenderersIfNeeded();
        }

        private void CacheRenderersIfNeeded()
        {
            if (_renderers != null && _renderers.Length > 0)
                return;

            _renderers = GetComponentsInChildren<Renderer>();
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = Array.Empty<Renderer>();
                _baseColors = Array.Empty<Color>();
                return;
            }

            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                // This uses renderer.material, which instantiates a copy per renderer, but that's fine for v1.
                _baseColors[i] = _renderers[i].material.color;
            }
        }

        /// <summary>
        /// Simple v1 highlight: tint all renderer materials to a highlight color, then restore.
        /// </summary>
        public void SetHighlight(bool value)
        {
            if (!useHighlightTint)
                return;

            CacheRenderersIfNeeded();

            if (_renderers == null || _renderers.Length == 0)
                return;

            if (_highlighted == value)
                return;

            _highlighted = value;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null)
                    continue;

                var mat = r.material;
                if (mat == null)
                    continue;

                if (value)
                {
                    mat.color = highlightColor;
                }
                else
                {
                    if (_baseColors != null && i < _baseColors.Length)
                        mat.color = _baseColors[i];
                }
            }
        }

        /// <summary>
        /// Called by the EventSystem when this 3D object is clicked.
        /// Requires:
        /// - An EventSystem in the scene,
        /// - A PhysicsRaycaster on the active camera,
        /// - A Collider on this object (or its children).
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            var sel = CardPhaseTargetSelectionController.Instance;
            if (sel == null)
                return;

            if (!sel.IsSelecting)
                return;

            Debug.Log($"[CardPhaseSelectableUnit] Click on {name} while selecting; forwarding to selection controller.");
            sel.TrySelectTarget(this);
        }
    }
}
