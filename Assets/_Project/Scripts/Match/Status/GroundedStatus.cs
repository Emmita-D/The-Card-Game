using Game.Match.Units;

namespace Game.Match.Status
{
    /// <summary>
    /// Temporarily forces a flying unit to be treated as ground.
    /// </summary>
    public class GroundedStatus : StatusEffect
    {
        private float duration;
        private bool active;

        public GroundedStatus(float durationSeconds)
        {
            duration = durationSeconds;
            active = true;
        }

        // Name for logs / future UI
        public override string Name => "Grounded";

        /// <summary>
        /// True while this status is active and not expired.
        /// </summary>
        public bool IsActive => active && !IsExpired;

        /// <summary>
        /// Remaining grounded time in seconds.
        /// </summary>
        public float RemainingDuration => duration;

        /// <summary>
        /// Extend this grounded effect by the given amount of seconds.
        /// If it was expired, this "reactivates" it with the new duration.
        /// </summary>
        public void AddDuration(float extraSeconds)
        {
            if (extraSeconds <= 0f)
                return;

            if (IsExpired)
            {
                duration = extraSeconds;
                IsExpired = false;
                active = true;
            }
            else
            {
                duration += extraSeconds;
            }
        }

        public override void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            if (!active || IsExpired)
                return;

            duration -= deltaTime;
            if (duration <= 0f)
            {
                duration = 0f;
                IsExpired = true;
                active = false;
            }
        }

        public override StatModifier GetStatModifier()
        {
            // Grounded doesn't change ATK/HP/speed,
            // it only affects IsFlying / heightLayer logic.
            return StatModifier.None;
        }

        public override void OnOwnerAttack(UnitRuntime owner)
        {
            // No special behavior on attack for grounded.
        }

        public override void OnTurnAdvanced(UnitRuntime owner)
        {
            // No turn-based expiry here; it's purely time-based.
        }
    }
}
