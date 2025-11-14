using UnityEngine;
using Game.Match.Cards;
using Game.Match.Units;

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Attach to spawned unit GOs. When the unit GameObject is destroyed
    /// *because it actually died*, the source CardSO is added to the correct
    /// per-player / per-realm graveyard.
    /// If your kill flow disables first (no Destroy), call NotifyNow() in that path.
    /// </summary>
    public class GraveyardOnDestroy : MonoBehaviour
    {
        [Tooltip("Original CardSO that created this unit.")]
        public CardSO source;

        [Tooltip("Owner/player id of the card that spawned this unit.")]
        public int ownerId = 0;

        bool _sent;

        /// <summary>
        /// Manually trigger graveyard recording (for custom kill flows).
        /// Will only record if this object represents a unit that is actually dead
        /// (UnitRuntime.health <= 0) or if there is no UnitRuntime at all
        /// (e.g. spells, traps, or other non-unit things using this component).
        /// </summary>
        public void NotifyNow()
        {
            if (_sent) return;

            // No source card? Nothing to record.
            if (source == null)
            {
                _sent = true;
                return;
            }

            // If this object has a UnitRuntime and it is still alive,
            // this is a cleanup/despawn (e.g. recall), NOT a death → skip.
            var runtime = GetComponent<UnitRuntime>();
            if (runtime != null && runtime.health > 0)
            {
                _sent = true;
                return;
            }

            // During normal gameplay this will return the singleton, and if needed create it.
            // During shutdown it can return null; in that case we simply skip.
            var gy = GraveyardService.TryGet();
            if (gy == null)
            {
                _sent = true;
                return;
            }

            gy.Add(ownerId, source);
            _sent = true;
        }

        void OnDestroy()
        {
            // When the unit actually dies and its GO is destroyed, send it once.
            NotifyNow();
        }
    }
}
