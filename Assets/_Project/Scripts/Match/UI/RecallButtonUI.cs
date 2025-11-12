using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Game.Match.Battle;

namespace Game.Match.UI
{
    /// <summary>
    /// Bottom-right button + keybind (R) to recall local player units.
    /// Disables when the local player has no units; shows a brief "recalling..." state.
    /// </summary>
    public class RecallButtonUI : MonoBehaviour
    {
        [Header("Refs")]
        public BattleRecallController recall;
        public Button button;
        public GameObject recallingSpinnerOrDim; // optional

        [Header("Owner")]
        public int localOwnerId = 0;

        private InputAction _recallAction;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(OnPressed);
            // Create a simple Input System action for R (keeps it self-contained)
            _recallAction = new InputAction("Recall", binding: "<Keyboard>/r");
        }

        void OnEnable()
        {
            _recallAction.Enable();
            _recallAction.performed += OnKey;

            if (recall != null)
            {
                recall.OnRecallStarted += HandleRecallStarted;
                recall.OnRecallCompleted += HandleRecallCompleted;
            }
        }

        void OnDisable()
        {
            _recallAction.performed -= OnKey;
            _recallAction.Disable();

            if (recall != null)
            {
                recall.OnRecallStarted -= HandleRecallStarted;
                recall.OnRecallCompleted -= HandleRecallCompleted;
            }
        }

        void Update()
        {
            // Live-enable/disable based on whether local has any alive units
            bool hasUnits = (recall != null) && recall.HasAliveUnits(localOwnerId);
            if (button != null) button.interactable = hasUnits;
        }

        void OnKey(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;
            if (button != null && button.interactable) OnPressed();
        }

        void OnPressed()
        {
            if (recall == null) return;
            recall.RequestRecall(localOwnerId);
        }

        void HandleRecallStarted(int ownerId)
        {
            if (ownerId != localOwnerId) return;
            if (recallingSpinnerOrDim != null) recallingSpinnerOrDim.SetActive(true);
            if (button != null) button.interactable = false;
        }

        void HandleRecallCompleted(int ownerId)
        {
            if (ownerId != localOwnerId) return;
            if (recallingSpinnerOrDim != null) recallingSpinnerOrDim.SetActive(false);
            // Re-enabled next Update() if there are still local units
        }
    }
}
