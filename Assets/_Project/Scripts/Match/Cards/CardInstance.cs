using System;
using System.Collections.Generic;
using Game.Match.Status;

namespace Game.Match.Cards
{
    [Serializable]
    public class CardInstance
    {
        public CardSO data;
        public int ownerId;
        public string instanceId;

        // Runtime-only statuses/buffs that affect this specific card instance.
        [NonSerialized]
        private List<StatusEffect> statuses;

        public CardInstance(CardSO data, int ownerId = 0)
        {
            this.data = data;
            this.ownerId = ownerId;
            this.instanceId = Guid.NewGuid().ToString("N");

            statuses = new List<StatusEffect>();
        }

        // --- Status / buff API ---------------------------------------------

        public void AddStatus(StatusEffect status)
        {
            if (status == null)
                return;

            if (statuses == null)
                statuses = new List<StatusEffect>();

            statuses.Add(status);
        }

        public IEnumerable<StatusEffect> GetStatuses()
        {
            if (statuses == null)
                statuses = new List<StatusEffect>();

            return statuses;
        }

        public StatModifier GetTotalModifiers()
        {
            if (statuses == null || statuses.Count == 0)
                return StatModifier.None;

            StatModifier total = StatModifier.None;

            for (int i = 0; i < statuses.Count; i++)
            {
                var s = statuses[i];
                if (s == null)
                    continue;

                total = total + s.GetStatModifier();
            }

            return total;
        }

        // Convenience: "final" stats for this card in hand/deck.
        public int GetFinalAttack()
        {
            if (data == null)
                return 0;

            StatModifier total = GetTotalModifiers();
            return data.attack + total.attackBonus;
        }

        public int GetFinalHealth()
        {
            if (data == null)
                return 0;

            StatModifier total = GetTotalModifiers();
            return data.health + total.healthBonus;
        }

        public void AdvanceTurn()
        {
            if (statuses == null || statuses.Count == 0)
                return;

            // Let each status handle turn advance.
            for (int i = 0; i < statuses.Count; i++)
            {
                var s = statuses[i];
                if (s == null)
                    continue;

                // For card-in-hand statuses we don't need a UnitRuntime,
                // so we pass null. Turn-based logic doesn't depend on the owner here.
                s.OnTurnAdvanced(null);
            }

            // Remove any expired statuses.
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                var s = statuses[i];
                if (s == null || s.IsExpired)
                {
                    statuses.RemoveAt(i);
                }
            }
        }

    }
}
