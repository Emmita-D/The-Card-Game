using Game.Match.Units;
using UnityEngine;

namespace Game.Match.Status
{
    /// <summary>
    /// Simple time-limited stun:
    /// - While active, the unit is considered "stunned"
    ///   (movement/attacks will be blocked by higher-level logic).
    /// - Does NOT directly change stats; we just check IsStunned()
    ///   on UnitStatusController to know if the unit is stunned.
    /// </summary>
    public class StunStatus : StatusEffect
    {
        private float remainingDuration;

        public override string Name => "Stun";

        /// <summary>
        /// Remaining stun time in seconds.
        /// </summary>
        public float RemainingDuration => remainingDuration;

        public StunStatus(float durationSeconds)
        {
            // Clamp to non-negative and mark expired if duration is 0.
            remainingDuration = Mathf.Max(0f, durationSeconds);
            if (remainingDuration <= 0f)
            {
                IsExpired = true;
            }
        }

        /// <summary>
        /// Called every frame by UnitStatusController.Update.
        /// Counts down the timer and expires the stun when it reaches 0.
        /// </summary>
        public override void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            if (IsExpired)
                return;

            remainingDuration -= deltaTime;
            if (remainingDuration <= 0f)
            {
                remainingDuration = 0f;
                IsExpired = true;
            }
        }

        /// <summary>
        /// Stun does not directly modify stats in v1.
        /// Higher-level logic (UnitAgent / CombatResolver) will check IsStunned()
        /// and block movement/attacks while a StunStatus is active.
        /// </summary>
        public override StatModifier GetStatModifier()
        {
            return StatModifier.None;
        }
    }
}
