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

            // GroundedStatus: accumulate duration instead of stacking multiple instances.
            var newGrounded = status as GroundedStatus;
            if (newGrounded != null)
            {
                for (int i = 0; i < activeStatuses.Count; i++)
                {
                    var existingGrounded = activeStatuses[i] as GroundedStatus;
                    if (existingGrounded != null && !existingGrounded.IsExpired)
                    {
                        // 👇 Key line: add the new grounded time to the existing one
                        existingGrounded.AddDuration(newGrounded.RemainingDuration);
                        return; // do not add a new instance
                    }
                }
            }

            // Default behaviour: just add the status.
            activeStatuses.Add(status);
        }

        // Internal counter used for Savage 5+ stun (every 15th attack).
        private int savageAttackCounter = 0;

        // --------- Savage tokens convenience API ---------
        // Savage is implemented as a StatusEffect (SavageStatus), but most systems
        // should not need to know about it. They just call these helpers.

        /// <summary>
        /// Internal helper: find the active SavageStatus, or null if none.
        /// We assume at most one SavageStatus instance per unit.
        /// </summary>
        private SavageStatus GetSavageStatusInternal()
        {
            for (int i = 0; i < activeStatuses.Count; i++)
            {
                var savage = activeStatuses[i] as SavageStatus;
                if (savage != null && !savage.IsExpired)
                {
                    return savage;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the current Savage stack count on this unit.
        /// If no SavageStatus is present, returns 0.
        /// </summary>
        public int GetSavageStacks()
        {
            var savage = GetSavageStatusInternal();
            return savage != null ? savage.Stacks : 0;
        }

        /// <summary>
        /// Adds Savage stacks to this unit. If there is no SavageStatus yet,
        /// one will be created. Negative or zero amounts are ignored.
        /// </summary>
        public void AddSavageStacks(int amount)
        {
            if (amount <= 0)
                return;

            var savage = GetSavageStatusInternal();
            if (savage == null)
            {
                savage = new SavageStatus();
                activeStatuses.Add(savage);
            }

            savage.AddStacks(amount);
        }

        /// <summary>
        /// Tries to consume (subtract) the given amount of Savage stacks.
        /// Returns true if the unit had at least that many stacks and they
        /// were deducted; false otherwise (no change made).
        /// </summary>
        public bool TryConsumeSavageStacks(int amount)
        {
            if (amount <= 0)
                return true; // Paying 0 is always "affordable".

            var savage = GetSavageStatusInternal();
            if (savage == null || savage.Stacks < amount)
                return false;

            savage.RemoveStacks(amount);
            return true;
        }

        /// <summary>
        /// Called when this unit successfully lands a basic attack.
        /// Returns true if this attack should trigger Savage 5+ stun.
        ///
        /// Rules:
        /// - If Savage stacks < 5, never stuns.
        /// - If Savage stacks >= 5, every 15th attack returns true.
        /// </summary>
        public bool TryProcSavageStun()
        {
            int stacks = GetSavageStacks();
            if (stacks < 5)
                return false;

            savageAttackCounter++;

            // Every 15th attack when Savage >= 5.
            return (savageAttackCounter % 15) == 0;
        }

        // --------- Stun convenience API ---------

        /// <summary>
        /// Returns true if this unit is currently stunned.
        /// A unit is stunned if it has at least one non-expired StunStatus.
        /// </summary>
        public bool IsStunned()
        {
            for (int i = 0; i < activeStatuses.Count; i++)
            {
                var stun = activeStatuses[i] as StunStatus;
                if (stun != null && !stun.IsExpired)
                {
                    return true;
                }
            }

            return false;
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

        // Called when a "turn" advances for this unit (for turn-limited buffs).
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
