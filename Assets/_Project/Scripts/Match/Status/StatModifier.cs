namespace Game.Match.Status
{
    [System.Serializable]
    public struct StatModifier
    {
        public int attackBonus;
        public int healthBonus;

        public static readonly StatModifier None = new StatModifier(0, 0);

        public StatModifier(int attackBonus, int healthBonus)
        {
            this.attackBonus = attackBonus;
            this.healthBonus = healthBonus;
        }

        public static StatModifier operator +(StatModifier a, StatModifier b)
        {
            return new StatModifier(
                a.attackBonus + b.attackBonus,
                a.healthBonus + b.healthBonus
            );
        }
    }
}
