namespace Game.Match.Status
{
    [System.Serializable]
    public struct StatModifier
    {
        public int attackBonus;
        public int healthBonus;

        // New: multiplicative modifiers (1f = no change)
        public float moveSpeedMultiplier;
        public float damageDealtMultiplier;
        public float damageTakenMultiplier;

        public static readonly StatModifier None = new StatModifier(0, 0);

        public StatModifier(int attackBonus, int healthBonus)
        {
            this.attackBonus = attackBonus;
            this.healthBonus = healthBonus;

            // Neutral values for multipliers
            this.moveSpeedMultiplier = 1f;
            this.damageDealtMultiplier = 1f;
            this.damageTakenMultiplier = 1f;
        }

        public static StatModifier operator +(StatModifier a, StatModifier b)
        {
            // Start with summed flat bonuses
            StatModifier result = new StatModifier(
                a.attackBonus + b.attackBonus,
                a.healthBonus + b.healthBonus
            );

            // Combine multiplicative effects by multiplying them
            result.moveSpeedMultiplier = a.moveSpeedMultiplier * b.moveSpeedMultiplier;
            result.damageDealtMultiplier = a.damageDealtMultiplier * b.damageDealtMultiplier;
            result.damageTakenMultiplier = a.damageTakenMultiplier * b.damageTakenMultiplier;

            return result;
        }
    }
}
