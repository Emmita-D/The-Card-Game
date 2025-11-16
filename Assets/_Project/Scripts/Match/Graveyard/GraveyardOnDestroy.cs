using UnityEngine;
using Game.Match.Cards;
using Game.Match.Units;
using Game.Match.Log;

namespace Game.Match.Graveyard
{
    /// <summary>
    /// Attach to spawned unit GameObjects.
    /// When the unit actually dies and is destroyed, its source CardSO is:
    /// - sent to the correct graveyard
    /// - logged to the BattlePhase action log with icon + CardSO, so the log row is clickable.
    ///
    /// IMPORTANT:
    /// - If the unit is still alive (health > 0), we treat the destroy as a cleanup/recall and skip logging.
    /// </summary>
    public class GraveyardOnDestroy : MonoBehaviour
    {
        [Tooltip("Original CardSO that created this unit.")]
        public CardSO source;

        [Tooltip("Owner/player id of the card that spawned this unit. 0 = local player, 1 = opponent.")]
        public int ownerId = 0;

        private bool _sent;

        /// <summary>
        /// Manually trigger graveyard + log (for custom kill flows).
        /// Will only record if the unit is actually dead or has no UnitRuntime.
        /// </summary>
        public void NotifyNow()
        {
            if (_sent) return;

            // No source card? Nothing to send/log.
            if (source == null)
            {
                _sent = true;
                return;
            }

            // If we have a UnitRuntime and it's still alive, this is a recall/cleanup, not a death.
            var runtime = GetComponent<UnitRuntime>();
            if (runtime != null && runtime.health > 0)
            {
                _sent = true;
                return;
            }

            // Send to graveyard
            var gy = GraveyardService.Instance;
            if (gy == null)
            {
                _sent = true;
                return;
            }

            gy.Add(ownerId, source);

            // Log this death into the BattlePhase log with icon + CardSO
            var log = ActionLogService.Instance;
            if (log != null)
            {
                string cardName = !string.IsNullOrEmpty(source.cardName) ? source.cardName : source.name;
                string ownerLabel = ownerId == 0 ? "Player" : "Opponent";

                if (ownerId == 0)
                {
                    // Local side death
                    log.BattleLocal(
                        $"{ownerLabel}'s {cardName} was destroyed.",
                        source.artSprite,   // icon
                        source              // CardSO → makes the row clickable
                    );
                }
                else
                {
                    // Remote/enemy side death
                    log.BattleRemote(
                        $"{ownerLabel}'s {cardName} was destroyed.",
                        source.artSprite,   // icon
                        source              // CardSO → makes the row clickable
                    );
                }
            }

            _sent = true;
        }

        private void OnDestroy()
        {
            // Runs when the unit GO is destroyed; if it's truly dead, this will handle graveyard + log.
            NotifyNow();
        }
    }
}
