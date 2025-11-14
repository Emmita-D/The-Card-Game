using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Match.Log
{
    /// <summary>
    /// Put this on the full-screen dark background of the preview.
    /// Any click on that background will call ActionLogCardPreview.Hide().
    /// </summary>
    public class ClickOutsideToHide : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private ActionLogCardPreview preview;

        private void Reset()
        {
            // Auto-find preview in parent if not set
            if (preview == null)
                preview = GetComponentInParent<ActionLogCardPreview>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (preview != null)
            {
                preview.Hide();
            }
        }
    }
}
