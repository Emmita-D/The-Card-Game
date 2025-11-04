using System.Collections.Generic;
using Game.Core;
using Game.Match.Cards;


namespace Game.Match.Graveyard
{
    public class GraveyardService
    {
        readonly List<CardInstance> empyrean = new(); readonly List<CardInstance> infernum = new();
        public void SendToGraveyard(CardInstance card)
        {
            if (card.data.realm == Realm.Empyrean) empyrean.Add(card); else infernum.Add(card);
        }
        public IReadOnlyList<CardInstance> Empyrean => empyrean; public IReadOnlyList<CardInstance> Infernum => infernum;
    }
}