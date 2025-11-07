using UnityEngine;
using Game.Match.Cards;

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Attach to spawned unit GOs. When the unit GameObject is destroyed,
    /// the source CardSO is added to the correct per-player/per-realm graveyard.
    /// If your kill flow disables first (no Destroy), call NotifyNow() in that path.
    /// </summary>
    public class GraveyardOnDestroy : MonoBehaviour
    {
        [Tooltip("Original CardSO that created this unit.")]
        public CardSO source;

        [Tooltip("Owner/player id of the card that spawned this unit.")]
        public int ownerId = 0;

        bool _sent;

        public void NotifyNow()
        {
            if (_sent) return;
            if (source != null)
                GraveyardService.Instance.Add(ownerId, source);
            _sent = true;
        }

        void OnDestroy()
        {
            // When the unit actually dies and its GO is destroyed, send it once.
            NotifyNow();
        }
    }
}
