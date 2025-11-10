using UnityEngine;

namespace Game.Match.Battle
{
    /// <summary>
    /// Simple tower representation for the battle stage.
    /// Holds ownership and basic combat stats.
    /// </summary>
    public class BattleTower : MonoBehaviour
    {
        [Header("Ownership")]
        [Tooltip("0 = local player, 1 = remote player")]
        public int ownerId = 0;

        [Tooltip("0 = front tower, 1 = back tower (for now)")]
        public int index = 0;

        [Header("Stats")]
        public int maxHp = 100;
        public int currentHp = 100;
        public int attack = 10;
        public float rangeMeters = 8f;

        [Header("Timing (used by CombatResolver)")]
        [Tooltip("Next time this tower is allowed to attack.")]
        public float nextAttackTime = 0f;

        private void OnValidate()
        {
            if (maxHp < 1) maxHp = 1;
            if (currentHp < 0) currentHp = 0;
            if (currentHp > maxHp) currentHp = maxHp;
        }
    }
}
