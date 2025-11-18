using Game.Match.Units;

namespace Game.Match.Status
{
    // Simple "+ATK / +HP" buff status.
    // Supports:
    // - permanent buffs  (existing behavior),
    // - time-limited buffs (seconds),
    // - attack-count-limited buffs (N attacks),
    // - turn-limited buffs (N turns, when you call OnTurnAdvanced).
    public class StatBuffStatus : StatusEffect
    {
        private StatModifier modifier;

        // Time-based lifetime
        private float durationSeconds;
        private readonly bool useDuration;

        // Attack-count-based lifetime
        private int remainingAttacks;
        private readonly bool useAttackCount;

        // Turn-based lifetime
        private int remainingTurns;
        private readonly bool useTurnCount;

        // ---------- Constructors ----------

        // Permanent buff (existing behavior)
        public StatBuffStatus(int attackBonus, int healthBonus)
        {
            modifier = new StatModifier(attackBonus, healthBonus);

            durationSeconds = 0f;
            remainingAttacks = 0;
            remainingTurns = 0;

            useDuration = false;
            useAttackCount = false;
            useTurnCount = false;
        }

        // Time-limited buff: lasts for durationSeconds of real time
        public StatBuffStatus(int attackBonus, int healthBonus, float durationSeconds)
        {
            modifier = new StatModifier(attackBonus, healthBonus);

            this.durationSeconds = durationSeconds;
            remainingAttacks = 0;
            remainingTurns = 0;

            useDuration = durationSeconds > 0f;
            useAttackCount = false;
            useTurnCount = false;
        }

        // Attack-limited buff: lasts for a number of attacks performed by the owner
        public StatBuffStatus(int attackBonus, int healthBonus, int attackCount)
        {
            modifier = new StatModifier(attackBonus, healthBonus);

            durationSeconds = 0f;
            remainingAttacks = attackCount;
            remainingTurns = 0;

            useDuration = false;
            useAttackCount = attackCount > 0;
            useTurnCount = false;
        }

        // Turn-limited buff: lasts for a number of "turn advances" for this unit
        // (you decide when a turn advances by calling NotifyTurnAdvanced on the controller).
        public StatBuffStatus(int attackBonus, int healthBonus, int turnCount, bool isTurnBased)
        {
            modifier = new StatModifier(attackBonus, healthBonus);

            durationSeconds = 0f;
            remainingAttacks = 0;
            remainingTurns = turnCount;

            useDuration = false;
            useAttackCount = false;
            useTurnCount = isTurnBased && turnCount > 0;
        }

        public override string Name
        {
            get { return "Stat Buff"; }
        }

        public override StatModifier GetStatModifier()
        {
            return modifier;
        }

        // ---------- Lifetime hooks ----------

        public override void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            if (!useDuration || IsExpired)
                return;

            durationSeconds -= deltaTime;
            if (durationSeconds <= 0f)
            {
                durationSeconds = 0f;
                IsExpired = true;
            }
        }

        public override void OnOwnerAttack(UnitRuntime owner)
        {
            if (!useAttackCount || IsExpired)
                return;

            remainingAttacks--;
            if (remainingAttacks <= 0)
            {
                remainingAttacks = 0;
                IsExpired = true;
            }
        }

        public override void OnTurnAdvanced(UnitRuntime owner)
        {
            if (!useTurnCount || IsExpired)
                return;

            remainingTurns--;
            if (remainingTurns <= 0)
            {
                remainingTurns = 0;
                IsExpired = true;
            }
        }
    }
}
