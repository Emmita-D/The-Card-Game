using UnityEngine;
using Game.Match.Cards;   // CardSO

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

        public void ShowCard(CardSO card)
        {
            if (panelRoot == null || cardParent == null || cardViewPrefab == null || card == null)
                return;

            // Show the panel (you control its position/size in the Inspector)
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

            // 🔹 DISABLE HOVER / DRAG ON THE PREVIEW INSTANCE ONLY
            // Use string-based GetComponent so we don’t depend on namespaces.
            var hoverBehaviour = _currentView.GetComponent("CardHoverFX") as Behaviour;
            if (hoverBehaviour != null)
            {
                hoverBehaviour.enabled = false;
            }

            var dragBehaviour = _currentView.GetComponent("DraggableCard") as Behaviour;
            if (dragBehaviour != null)
            {
                dragBehaviour.enabled = false;
            }

            // This uses your existing CardView logic (stats, badges, realm frame, etc.).
            _currentView.Bind(card);
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
