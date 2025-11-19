using Game.Match.Units;

namespace Game.Match.Status
{
    // Time-limited slow: reduces movement speed for a duration,
    // using the moveSpeedMultiplier field on StatModifier.
    public class SlowStatus : StatusEffect
    {
        private float duration;
        private readonly float slowPercent; // 0.3f = 30% slower

        public SlowStatus(float durationSeconds, float slowPercent)
        {
            duration = durationSeconds;
            this.slowPercent = slowPercent;
        }

        // Give this status a proper name for logs/UI.
        public override string Name => "Slow";

        public override void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            if (IsExpired)
                return;

            duration -= deltaTime;
            if (duration <= 0f)
            {
                duration = 0f;
                IsExpired = true;
            }
        }

        public override StatModifier GetStatModifier()
        {
            // Convert slowPercent into a multiplier:
            // 0.0f -> 100% speed (no slow)
            // 0.3f -> 70% speed
            // 1.0f -> 0% speed (frozen)
            float moveMult = 1f - slowPercent;
            if (moveMult < 0f)
                moveMult = 0f;

            // Start from "no change" and only touch moveSpeedMultiplier.
            StatModifier modifier = StatModifier.None;
            modifier.moveSpeedMultiplier = moveMult;

            return modifier;
        }
    }
}
