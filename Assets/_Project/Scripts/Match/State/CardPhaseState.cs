using System.Collections.Generic;
using Game.Core.Config;
using Game.Match.Cards;
using Game.Match.Mana;
using Game.Match.Graveyard;
using Game.Match.Log;


namespace Game.Match.State
{
    public class PlayerRuntime
    {
        public readonly List<CardInstance> deck = new(); public readonly List<CardInstance> hand = new();
        public readonly ManaService mana; public readonly GraveyardService gy = new(); public readonly ActionLogService log = new();
        public PlayerRuntime(BalanceConfigSO bal) { mana = new ManaService(bal, 1); }
        public void Draw(int n) { for (int i = 0; i < n && deck.Count > 0; i++) { var c = deck[0]; deck.RemoveAt(0); hand.Add(c); log.Add(LogPhase.Card, $"Drew {c.data.cardName}"); } }
    }


    public class CardPhaseState
    {
        readonly BalanceConfigSO bal; public readonly PlayerRuntime local;
        public CardPhaseState(BalanceConfigSO bal, PlayerRuntime local) { this.bal = bal; this.local = local; }
        public void Enter() { local.mana.Refill(); local.Draw(1); local.log.Add(LogPhase.Card, "Card Phase start: mana refilled."); }
        public void Exit() { /* redaction handled on reveal later */ }
    }
}