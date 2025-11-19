using Game.Core;
using Game.Match.Cards;
using Game.Match.Status;   // StatusEffect, StatModifier, GroundedStatus
using UnityEngine;

namespace Game.Match.Units
{
    public enum HeightLayer
    {
        Ground = 0,
        Air = 1
    }

    public class UnitRuntime : MonoBehaviour
    {
        [Header("Stats (debug)")]
        public string displayName;
        public int attack;
        public int health;
        public float rangeMeters;
        public Realm realm;

        [Header("Classification (runtime)")]
        public MovementType movementType;
        public AttackMode attackMode;

        [Header("Height / special flags")]
        [Tooltip("Current vertical layer for combat rules (Ground / Air).")]
        public HeightLayer heightLayer = HeightLayer.Ground;

        [Tooltip("True if this unit behaves as a diving flier (set from CardSO).")]
        public bool isDiveFlier;

        [Header("Status / Buffs")]
        public UnitStatusController StatusController { get; private set; }

        private void Awake()
        {
            // Initialize per-unit status container
            StatusController = new UnitStatusController();
        }

        // Simple visual tint so you can tell Empyrean/Infernum at a glance
        public void InitFrom(CardSO so)
        {
            if (so == null) return;
            displayName = so.cardName;
            attack = so.attack;
            health = so.health;
            rangeMeters = so.rangeMeters;
            realm = so.realm;

            movementType = so.movement;
            attackMode = so.attackMode;

            // Dive flier flag & height initialization.
            isDiveFlier = so.isDiveFlier &&
                          movementType == MovementType.Flying &&
                          attackMode == AttackMode.Melee;

            heightLayer = movementType == MovementType.Flying
                ? HeightLayer.Air
                : HeightLayer.Ground;

            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // Use a material instance so you don't edit the shared asset
                var mats = rend.materials;
                if (mats.Length > 0)
                {
                    var c = (so.realm == Realm.Infernum)
                        ? new Color(0.9f, 0.25f, 0.2f)   // reddish
                        : new Color(0.3f, 0.55f, 1f);     // bluish
                    mats[0].color = c;
                    rend.materials = mats;
                }
            }

            gameObject.name = $"{so.cardName}_Unit";
        }

        private void Update()
        {
            if (StatusController != null)
            {
                StatusController.Update(this, Time.deltaTime);
            }

            // Recompute height layer each frame based on movement type + statuses
            UpdateHeightLayerFromStatus();
        }

        /// <summary>
        /// Returns true if this unit should be treated as flying RIGHT NOW,
        /// taking into account any active GroundedStatus.
        /// </summary>
        public bool IsFlying
        {
            get
            {
                // If any active GroundedStatus is present, treat as not flying.
                if (StatusController != null)
                {
                    foreach (var s in StatusController.GetAll())
                    {
                        var grounded = s as GroundedStatus;
                        if (grounded != null && grounded.IsActive)
                        {
                            return false;
                        }
                    }
                }

                return movementType == MovementType.Flying;
            }
        }

        /// <summary>
        /// Compute the effective height layer from movement type + statuses
        /// and write it into heightLayer for any code that reads it directly.
        /// </summary>
        private void UpdateHeightLayerFromStatus()
        {
            // Base layer from movement type
            var baseLayer = (movementType == MovementType.Flying)
                ? HeightLayer.Air
                : HeightLayer.Ground;

            bool groundedByStatus = false;

            if (StatusController != null)
            {
                foreach (var s in StatusController.GetAll())
                {
                    var grounded = s as GroundedStatus;
                    if (grounded != null && grounded.IsActive)
                    {
                        groundedByStatus = true;
                        break;
                    }
                }
            }

            heightLayer = groundedByStatus ? HeightLayer.Ground : baseLayer;
        }

        // ----- Final stat getters (base runtime stats + status modifiers) -----

        public int GetFinalAttack()
        {
            if (StatusController == null)
            {
                return attack;
            }

            StatModifier total = StatusController.GetTotalModifiers();
            return attack + total.attackBonus;
        }

        public int GetFinalHealth()
        {
            if (StatusController == null)
            {
                return health;
            }

            StatModifier total = StatusController.GetTotalModifiers();
            return health + total.healthBonus;
        }

        /// <summary>
        /// Returns the final movement speed for this unit given a base value,
        /// taking into account any active moveSpeedMultiplier from statuses.
        /// </summary>
        public float GetFinalMoveSpeed(float baseMoveSpeed)
        {
            if (StatusController == null)
            {
                return baseMoveSpeed;
            }

            StatModifier total = StatusController.GetTotalModifiers();

            // Default to 1 if no status touched speed or something gave 0/negative.
            float multiplier = total.moveSpeedMultiplier;
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            return baseMoveSpeed * multiplier;
        }

        /// <summary>
        /// Returns the combined damage-dealt multiplier from all active statuses.
        /// (Used by CombatResolver when computing outgoing damage.)
        /// </summary>
        public float GetDamageDealtMultiplier()
        {
            if (StatusController == null)
            {
                return 1f;
            }

            StatModifier total = StatusController.GetTotalModifiers();
            float multiplier = total.damageDealtMultiplier;
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            return multiplier;
        }

        /// <summary>
        /// Returns the combined damage-taken multiplier from all active statuses.
        /// (Used by CombatResolver when computing incoming damage.)
        /// </summary>
        public float GetDamageTakenMultiplier()
        {
            if (StatusController == null)
            {
                return 1f;
            }

            StatModifier total = StatusController.GetTotalModifiers();
            float multiplier = total.damageTakenMultiplier;
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            return multiplier;
        }
    }
}
