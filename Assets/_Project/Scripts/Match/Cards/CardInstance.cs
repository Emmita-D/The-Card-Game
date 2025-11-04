using System;

namespace Game.Match.Cards
{
    [Serializable]
    public class CardInstance
    {
        public CardSO data;
        public int ownerId;
        public string instanceId;

        public CardInstance(CardSO data, int ownerId = 0)
        {
            this.data = data;
            this.ownerId = ownerId;
            this.instanceId = Guid.NewGuid().ToString("N");
        }
    }
}
