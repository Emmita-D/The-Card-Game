using UnityEngine;
using Game.Match.Cards;
using Game.Match.Units;
using Game.Match.Graveyard;

namespace Game.Match.Battle
{
    /// <summary>
    /// Handles per-unit runtime data for the battle stage:
    /// - Stores owner + source card
    /// - Initializes UnitRuntime from CardSO
    /// - Moves forward along the lane (unless movement is locked)
    /// - Exposes attack timing fields used by CombatResolver
    /// </summary>
    [RequireComponent(typeof(UnitRuntime))]
    public class UnitAgent : MonoBehaviour
    {
        [Header("Ownership")]
        [Tooltip("0 = local player, 1 = remote player")]
        public int ownerId;

        [Header("Movement")]
        [Tooltip("World-space direction the unit will move in.")]
        [SerializeField] private Vector3 moveDirection = Vector3.zero;

        [Tooltip("Units per second along moveDirection.")]
        public float moveSpeed = 3f;

        [Tooltip("If true, this unit will not move (used when engaged in combat).")]
        public bool movementLocked = false;

        [Header("Source data")]
        public CardSO sourceCard;

        [Header("Combat timing")]
        [Tooltip("Next time this unit is allowed to attack (set by CombatResolver).")]
        public float nextAttackTime = 0f;

        private UnitRuntime _runtime;

        private void Awake()
        {
            _runtime = GetComponent<UnitRuntime>();
        }

        /// <summary>
        /// Called right after instantiating the unit.
        /// </summary>
        public void Initialize(CardSO card, int owner, Vector3 direction)
        {
            ownerId = owner;
            sourceCard = card;
            moveDirection = direction.normalized;
            nextAttackTime = 0f;
            movementLocked = false;

            if (_runtime == null)
                _runtime = GetComponent<UnitRuntime>();

            // Copy stats / visuals from the CardSO into UnitRuntime (attack, health, realm tint, etc.)
            if (_runtime != null && card != null)
            {
                _runtime.InitFrom(card);
            }

            // Wire graveyard behaviour so that when this GO is destroyed,
            // the card goes to the correct graveyard.
            var gy = GetComponent<GraveyardOnDestroy>();
            if (gy != null)
            {
                gy.source = card;
                gy.ownerId = owner;
            }
        }

        private void Update()
        {
            // If we're engaged in combat, we stay put.
            if (movementLocked) return;
            if (moveDirection == Vector3.zero) return;

            transform.position += moveDirection * (moveSpeed * Time.deltaTime);
        }
    }
}
