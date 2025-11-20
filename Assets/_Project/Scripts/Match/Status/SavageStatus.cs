using Game.Match.Units;

namespace Game.Match.Status
{
    /// <summary>
    /// Savage tokens: a permanent, stackable buff that increases
    /// the unit's outgoing damage. Also acts as a "currency" that
    /// other systems can add/remove via UnitStatusController.
    ///
    /// - Each stack: +3% damage (configurable via DamagePerStack).
    /// - Clamped by MaxStacks so damage buff can't grow unbounded.
    ///
    /// NOTE: No stun rider here yet – this is Savage v1 (damage-only).
    /// </summary>
    public class SavageStatus : StatusEffect
    {
        // How much damage each stack adds (e.g. 0.03 = +3% per stack).
        private const float DamagePerStack = 0.03f;

        // Hard cap on stacks that contribute to the damage buff.
        private const int DefaultMaxStacks = 15;

        private int stacks;
        private int maxStacks;

        public override string Name => "Savage";

        /// <summary>
        /// Current Savage stack count on this unit.
        /// </summary>
        public int Stacks => stacks;

        public SavageStatus(int initialStacks = 0, int maxStacks = DefaultMaxStacks)
        {
            this.maxStacks = maxStacks > 0 ? maxStacks : DefaultMaxStacks;
            stacks = 0;

            if (initialStacks > 0)
            {
                AddStacks(initialStacks);
            }
        }

        /// <summary>
        /// Increase Savage stacks by a positive amount.
        /// Values are clamped between 0 and maxStacks.
        /// </summary>
        public void AddStacks(int amount)
        {
            if (amount <= 0)
                return;

            stacks += amount;
            if (stacks > maxStacks)
                stacks = maxStacks;
        }

        /// <summary>
        /// Decrease Savage stacks by a positive amount.
        /// Values are clamped and never go below 0.
        /// </summary>
        public void RemoveStacks(int amount)
        {
            if (amount <= 0)
                return;

            stacks -= amount;
            if (stacks < 0)
                stacks = 0;
        }

        /// <summary>
        /// Contributes the outgoing damage buff to StatModifier.
        /// No speed changes here; only damage.
        /// </summary>
        public override StatModifier GetStatModifier()
        {
            if (IsExpired || stacks <= 0)
                return StatModifier.None;

            int effectiveStacks = stacks;
            if (effectiveStacks > maxStacks)
                effectiveStacks = maxStacks;

            float multiplier = 1f + DamagePerStack * effectiveStacks;

            // Start from neutral (multipliers = 1) and only touch damageDealtMultiplier.
            StatModifier modifier = StatModifier.None;
            modifier.damageDealtMultiplier = multiplier;

            return modifier;
        }

        // No time-based expiry for Savage v1, so OnUpdate/OnOwnerAttack/OnTurnAdvanced
        // just inherit the defaults from StatusEffect.
    }
}
