using UnityEngine;
using UnityEngine.UI;
using Game.Match.Cards;
using Game.Match.Log;

namespace Game.Match.UI
{
    /// <summary>
    /// Attach this to a unit-bar card button.
    /// When clicked, it asks the shared ActionLogCardPreview to show this card.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UnitBarCardPreviewRelay : MonoBehaviour
    {
        [Tooltip("The CardSO this unit-bar slot represents.")]
        [SerializeField] private CardSO card;

        [Tooltip("Shared preview controller in the scene. If left null, will be auto-found.")]
        [SerializeField] private ActionLogCardPreview preview;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();

            if (preview == null)
            {
                // Auto-find the shared preview (even if panel is inactive)
                preview = FindObjectOfType<ActionLogCardPreview>(true);
            }

            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (preview != null && card != null)
            {
                preview.ShowCard(card);
            }
        }

        /// <summary>
        /// Allow other code to set the card at runtime (if unit bar is dynamic).
        /// </summary>
        public void SetCard(CardSO newCard)
        {
            card = newCard;
        }
    }
}
