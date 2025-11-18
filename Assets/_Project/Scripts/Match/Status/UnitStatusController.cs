using System.Collections.Generic;
using Game.Match.Units;

namespace Game.Match.Status
{
    // Aggregates all active StatusEffect instances for a unit
    // and handles updating + auto-removal of expired statuses.
    public class UnitStatusController
    {
        private readonly List<StatusEffect> activeStatuses = new List<StatusEffect>();

        public void AddStatus(StatusEffect status)
        {
            if (status == null)
                return;

            activeStatuses.Add(status);
        }

        public StatModifier GetTotalModifiers()
        {
            StatModifier total = StatModifier.None;

            for (int i = 0; i < activeStatuses.Count; i++)
            {
                StatusEffect status = activeStatuses[i];
                if (status == null)
                    continue;

                total = total + status.GetStatModifier();
            }

            return total;
        }

        public IEnumerable<StatusEffect> GetAll()
        {
            return activeStatuses;
        }

        // Called every frame from UnitRuntime.Update (time-based effects).
        public void Update(UnitRuntime owner, float deltaTime)
        {
            if (activeStatuses.Count == 0)
                return;

            for (int i = 0; i < activeStatuses.Count; i++)
            {
                var s = activeStatuses[i];
                if (s == null)
                    continue;

                s.OnUpdate(owner, deltaTime);
            }

            CleanupExpired();
        }

        // Called when the owning unit performs an attack (for attack-limited buffs).
        public void NotifyOwnerAttack(UnitRuntime owner)
        {
            if (activeStatuses.Count == 0)
                return;

            for (int i = 0; i < activeStatuses.Count; i++)
            {
                var s = activeStatuses[i];
                if (s == null)
                    continue;

                s.OnOwnerAttack(owner);
            }

            CleanupExpired();
        }

        // 🔹 NEW: called when a "turn" advances for this unit (for turn-limited buffs).
        public void NotifyTurnAdvanced(UnitRuntime owner)
        {
            if (activeStatuses.Count == 0)
                return;

            for (int i = 0; i < activeStatuses.Count; i++)
            {
                var s = activeStatuses[i];
                if (s == null)
                    continue;

                s.OnTurnAdvanced(owner);
            }

            CleanupExpired();
        }

        private void CleanupExpired()
        {
            for (int i = activeStatuses.Count - 1; i >= 0; i--)
            {
                var s = activeStatuses[i];
                if (s == null || s.IsExpired)
                {
                    activeStatuses.RemoveAt(i);
                }
            }
        }
    }
}