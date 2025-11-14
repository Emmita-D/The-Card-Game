using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Match.Cards;

namespace Game.Match.Log
{
    [RequireComponent(typeof(Button))]
    public class ActionLogItemUI : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text messageText;

        private Button _button;
        private CardSO _card;
        private System.Action<CardSO> _onClicked;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        public void Setup(ActionEvent e, System.Action<CardSO> onClickHandler)
        {
            _card = e.card;
            _onClicked = onClickHandler;

            // Text
            if (messageText != null)
                messageText.text = e.text;

            // Icon
            if (iconImage != null)
            {
                if (e.icon != null)
                {
                    iconImage.sprite = e.icon;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.enabled = false;
                }
            }

            // Button / click
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();

                if (_card != null && _onClicked != null)
                {
                    _button.interactable = true;
                    _button.onClick.AddListener(() => _onClicked(_card));
                }
                else
                {
                    // No card attached to this log entry → disable click
                    _button.interactable = false;
                }
            }
        }
    }
}
