using UnityEngine;
using Game.Core.Config;
using Game.Match.Cards;
using Game.Match.State;
using Game.Match.Grid;


namespace Game.Sandbox
{
    public class Stage1Sandbox : MonoBehaviour
    {
        [Header("Config")] public DeckRulesSO deckRules; public BalanceConfigSO balance;
        [Header("References")] public GridService grid; public PlacementController placer; public HandView handView; public Canvas gameCanvas;
        [Header("Card Catalog (examples)")] public CardSO[] catalog;


        PlayerRuntime player; CardPhaseState cardPhase;
        private CardSO cardSo;

        void Start()
        {
            player = new PlayerRuntime(balance);
            // Quick starter deck: take first 10 entries and duplicate within copy rules
            for (int i = 0; i < catalog.Length && player.deck.Count < deckRules.deckSize; i++)
            {
                var so = catalog[i]; int maxCopies = so.isLegend ? deckRules.maxCopiesLegend : deckRules.maxCopiesNormal;
                int copies = Mathf.Min(maxCopies, Mathf.Max(1, so.isLegend ? 1 : 2));
                for (int c = 0; c < copies; c++) player.deck.Add(new CardInstance(cardSo, ownerId: 0));
            }
            // draw opening hand
            for (int i = 0; i < deckRules.openingHand; i++) player.Draw(1);
            handView.SetHand(player.hand);


            // Enter Card Phase
            cardPhase = new CardPhaseState(balance, player); cardPhase.Enter();


            // Hook placer to draggable cards at runtime if needed
            foreach (var dc in FindObjectsOfType<DraggableCard>(true)) { dc.placer = placer; dc.mana = player.mana; }
        }
    }
}