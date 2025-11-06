using UnityEngine;
using Game.Core.Config;
using Game.Match.Cards;
using Game.Match.State;
using Game.Match.Grid;
using Game.Match.Mana;   // <- make sure your ManaPool is in this namespace

namespace Game.Sandbox
{
    public class Stage1Sandbox : MonoBehaviour
    {
        [Header("Config")]
        public DeckRulesSO deckRules;
        public BalanceConfigSO balance;

        [Header("References")]
        public GridService grid;
        public PlacementController placer;   // kept for other experiments; not used here
        public HandView handView;
        public Canvas gameCanvas;

        [Header("Card Catalog (examples)")]
        public CardSO[] catalog;

        PlayerRuntime player;
        CardPhaseState cardPhase;

        void Start()
        {
            if (balance == null || deckRules == null || handView == null)
            {
                Debug.LogError("[Stage1Sandbox] Missing balance/deckRules/handView references.");
                return;
            }

            player = new PlayerRuntime(balance);

            // Build a quick deck from catalog within copy rules
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Length && player.deck.Count < deckRules.deckSize; i++)
                {
                    var so = catalog[i];
                    if (so == null) continue;

                    int maxCopies = so.isLegend ? deckRules.maxCopiesLegend : deckRules.maxCopiesNormal;
                    int copies = Mathf.Min(maxCopies, Mathf.Max(1, so.isLegend ? 1 : 2));

                    for (int c = 0; c < copies && player.deck.Count < deckRules.deckSize; c++)
                        player.deck.Add(new CardInstance(so, ownerId: 0));   // <-- fixed: use 'so'
                }
            }

            // Draw opening hand
            for (int i = 0; i < deckRules.openingHand; i++)
                player.Draw(1);

            handView.SetHand(player.hand);

            // Enter Card Phase (optional sandbox flow)
            cardPhase = new CardPhaseState(balance, player);
            cardPhase.Enter();

            // Ensure all DraggableCards know the scene ManaPool (HandView also injects on spawn)
            var pool = FindObjectOfType<ManaPool>();
            if (pool != null)
            {
                var cards = FindObjectsOfType<DraggableCard>(true);
                foreach (var dc in cards)
                    dc.SetManaPool(pool);
            }

            // NOTE:
            // The old lines:
            //   dc.placer = placer; dc.mana = player.mana;
            // are intentionally removed—DraggableCard now gates via CardAffordability + ManaPool.
        }
    }
}
