using UnityEngine;
using Game.Match.Cards;    // CardSO
using Game.Match.Battle;   // UnitAgent
using Game.Match.Units;    // UnitRuntime

namespace Game.Match.Log
{
    public class ActionLogCardPreview : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("Where the CardView prefab will be instantiated.")]
        [SerializeField] private Transform cardParent;

        [Tooltip("The same CardView prefab you use in HandView.")]
        [SerializeField] private CardView cardViewPrefab;

        private CardView _currentView;

        /// <summary>
        /// Show a plain CardSO (used by the action log).
        /// This shows base stats from the asset.
        /// </summary>
        public void ShowCard(CardSO card)
        {
            if (panelRoot == null || cardParent == null || cardViewPrefab == null || card == null)
                return;

            panelRoot.SetActive(true);

            // Destroy previous preview, if any
            if (_currentView != null)
            {
                Destroy(_currentView.gameObject);
                _currentView = null;
            }

            // Instantiate full card prefab as child of the configured parent
            _currentView = Instantiate(cardViewPrefab, cardParent);
            _currentView.transform.localScale = Vector3.one;
            _currentView.transform.localPosition = Vector3.zero;

            // Disable hover / drag on the preview instance only (by name to avoid extra deps)
            var hoverBehaviour = _currentView.GetComponent("CardHoverFX") as Behaviour;
            if (hoverBehaviour != null)
                hoverBehaviour.enabled = false;

            var dragBehaviour = _currentView.GetComponent("DraggableCard") as Behaviour;
            if (dragBehaviour != null)
                dragBehaviour.enabled = false;

            // Use your existing CardView logic (stats, badges, realm frame, etc.)
            _currentView.Bind(card);
        }

        /// <summary>
        /// Shows a preview for a live unit from the battlefield:
        /// - Uses sourceCard for art/frame/text
        /// - Uses UnitRuntime for buffed stats
        /// - Attack is shown as (FinalAttack * DamageDealtMultiplier)
        /// </summary>
        public void ShowUnit(UnitAgent agent)
        {
            if (agent == null || agent.sourceCard == null)
            {
                Hide();
                return;
            }

            // First build the normal preview from the CardSO
            ShowCard(agent.sourceCard);

            // Then override ATK/HP using the runtime (buffed) values
            if (_currentView != null)
            {
                var runtime = agent.GetComponent<UnitRuntime>();
                if (runtime != null)
                {
                    int baseAtk = runtime.GetFinalAttack();
                    int finalHp = runtime.GetFinalHealth();

                    float dmgMult = runtime.GetDamageDealtMultiplier();
                    if (dmgMult <= 0f)
                        dmgMult = 1f;

                    int shownAtk = Mathf.Max(1, Mathf.RoundToInt(baseAtk * dmgMult));

                    // DEBUG: log what we’re actually showing
                    UnityEngine.Debug.Log(
                        $"[Preview] {runtime.displayName} baseAtk={baseAtk}, dmgMult={dmgMult:F2}, shownAtk={shownAtk}, savage={runtime.StatusController?.GetSavageStacks()}"
                    );

                    _currentView.OverrideStats(shownAtk, finalHp);
                }
            }
        }

        public void Hide()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);

            if (_currentView != null)
            {
                Destroy(_currentView.gameObject);
                _currentView = null;
            }
        }
    }
}
