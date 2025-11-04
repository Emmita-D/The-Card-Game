using UnityEngine;


namespace Game.Core.Config
{
    [CreateAssetMenu(menuName = "Game/Rules/Deck Rules")]
    public class DeckRulesSO : ScriptableObject
    {
        [Header("Copies & Sizes")] public int maxCopiesNormal = 3;
        public int maxCopiesLegend = 1;
        public int deckSize = 30; // TBD – keep flexible
        public int openingHand = 5; public int maxHandSize = 10; public int mulligans = 1;
    }
}