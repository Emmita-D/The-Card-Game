using Game.Match.Units;

namespace Game.Match.Status
{
    // Temporarily disables flying, forcing the unit to be treated as grounded.
    public class GroundedStatus : StatusEffect
    {
        private float duration;
        private bool active;

        public GroundedStatus(float durationSeconds)
        {
            duration = durationSeconds;
            active = true;
        }

        public override string Name => "Grounded";

        public override void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            if (!active || IsExpired)
                return;

            duration -= deltaTime;
            if (duration <= 0f)
            {
                duration = 0f;
                active = false;
                IsExpired = true;
            }
        }

        // Helper for UnitRuntime to check if this status is active
        public bool IsActive => active && !IsExpired;
    }
}
