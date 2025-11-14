using UnityEngine;
using Game.Match.Cards;
using Game.Match.Units;
using Game.Match.Log;

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Attach to spawned unit GOs. When the unit GameObject is destroyed
    /// *because it actually died*, the source CardSO is added to the correct
    /// graveyard and logged to the BattlePhase action log.
    /// If your kill flow disables first (no Destroy), call NotifyNow() manually.
    /// </summary>
    public class GraveyardOnDestroy : MonoBehaviour
    {
        [Tooltip("Original CardSO that created this unit.")]
        public CardSO source;

        [Tooltip("Owner/player id of the card that spawned this unit.")]
        public int ownerId = 0;

        private bool _sent;

        public void NotifyNow()
        {
            if (_sent) return;

            if (source == null)
            {
                _sent = true;
                return;
            }

            var runtime = GetComponent<UnitRuntime>();
            if (runtime != null && runtime.health > 0)
            {
                _sent = true;
                return;
            }

            var gy = GraveyardService.TryGet();
            if (gy == null)
            {
                _sent = true;
                return;
            }

            gy.Add(ownerId, source);

            // Log this death into the BattlePhase log (for both sides)
            var log = ActionLogService.Instance;
            if (log != null)
            {
                string cardName = !string.IsNullOrEmpty(source.cardName) ? source.cardName : source.name;
                string ownerLabel = ownerId == 0 ? "Player" : "Opponent";

                if (ownerId == 0)
                    log.BattleLocal($"{ownerLabel}'s {cardName} was destroyed.");
                else
                    log.BattleRemote($"{ownerLabel}'s {cardName} was destroyed.");
            }

            _sent = true;
        }

        private void OnDestroy()
        {
            NotifyNow();
        }
    }
}
