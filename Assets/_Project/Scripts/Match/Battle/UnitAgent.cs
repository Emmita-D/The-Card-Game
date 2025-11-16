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
    /// - Moves along its lane (unless movement is locked)
    /// - Applies simple aggro logic (chase closest enemy unit/tower in front within aggro range).
    /// </summary>
    [RequireComponent(typeof(UnitRuntime))]
    public class UnitAgent : MonoBehaviour
    {
        [Header("Ownership")]
        [Tooltip("0 = local player, 1 = remote player")]
        public int ownerId;

        [Header("Movement")]
        [Tooltip("Current world-space movement direction this frame.")]
        [SerializeField] private Vector3 moveDirection = Vector3.zero;

        [Tooltip("Base lane direction this unit follows when it has no target.")]
        [SerializeField] private Vector3 _laneDirection = Vector3.zero;

        [Tooltip("Units per second along movement direction.")]
        public float moveSpeed = 3f;

        [Tooltip("If true, this unit will not move (used when engaged in combat).")]
        public bool movementLocked = false;

        [Header("Source data")]
        public CardSO sourceCard;

        [Header("Combat timing")]
        [Tooltip("Next time this unit is allowed to attack (set by CombatResolver).")]
        public float nextAttackTime = 0f;

        private UnitRuntime _runtime;

        // Aggro settings copied from CardSO so behaviour is configured per card, not per prefab.
        private float _chaseRangeMultiplier = 3f;
        private float _frontArcDotThreshold = 0.2f;

        // Static cache so we don't keep searching for towers every frame.
        private static BattleTower[] _cachedTowers;

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

            _laneDirection = direction.normalized;
            moveDirection = _laneDirection;
            nextAttackTime = 0f;
            movementLocked = false;

            if (_runtime == null)
                _runtime = GetComponent<UnitRuntime>();

            // Copy stats from CardSO into runtime
            if (_runtime != null && card != null)
            {
                _runtime.InitFrom(card);
            }

            // Copy aggro settings from the CardSO (with safe clamping).
            if (card != null)
            {
                _chaseRangeMultiplier = Mathf.Max(0f, card.chaseRangeMultiplier);
                _frontArcDotThreshold = Mathf.Clamp(card.frontArcDotThreshold, -1f, 1f);
            }

            // Wire graveyard behaviour
            var gy = GetComponent<GraveyardOnDestroy>();
            if (gy != null)
            {
                gy.source = card;
                gy.ownerId = owner;
            }

            // Cache towers once (they live in the battle scene and are reused across rounds)
            if (_cachedTowers == null || _cachedTowers.Length == 0)
            {
                _cachedTowers = FindObjectsOfType<BattleTower>();
            }
        }

        /// <summary>
        /// Returns the closest enemy (unit or tower) in front of us within our aggro range,
        /// or null if there is none.
        /// </summary>
        private Transform FindBestTargetInAggroRange()
        {
            var resolver = CombatResolver.Instance;
            if (resolver == null || _runtime == null)
                return null;

            // Decide which units we consider "enemies".
            var enemyUnits = (ownerId == 0)
                ? resolver.RemoteUnits   // local → look at remote list
                : resolver.LocalUnits;   // remote → look at local list

            Vector3 myPos = transform.position;

            // "Forward" is the lane direction; fall back to transform.forward if needed.
            Vector3 baseForward = _laneDirection.sqrMagnitude > 0.0001f
                ? _laneDirection.normalized
                : transform.forward;

            float attackRange = Mathf.Max(0.1f, _runtime.rangeMeters);
            float chaseRange = attackRange * Mathf.Max(0f, _chaseRangeMultiplier);
            if (chaseRange <= 0f)
                return null;

            float chaseRangeSqr = chaseRange * chaseRange;

            Transform best = null;
            float bestDistSqr = float.MaxValue;

            // ---- 1) Consider enemy units ----
            if (enemyUnits != null)
            {
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var other = enemyUnits[i];
                    if (other == null || other == this)
                        continue;

                    var otherRuntime = other.GetComponent<UnitRuntime>();
                    if (otherRuntime != null && otherRuntime.health <= 0)
                        continue;

                    Vector3 to = other.transform.position - myPos;
                    to.y = 0f;
                    float distSqr = to.sqrMagnitude;
                    if (distSqr < 0.0001f || distSqr > chaseRangeSqr)
                        continue;

                    Vector3 dirTo = to.normalized;
                    float dot = Vector3.Dot(baseForward, dirTo);
                    if (dot < _frontArcDotThreshold)
                        continue;

                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        best = other.transform;
                    }
                }
            }

            // ---- 2) Consider enemy towers ----
            if (_cachedTowers != null && _cachedTowers.Length > 0)
            {
                for (int i = 0; i < _cachedTowers.Length; i++)
                {
                    var tower = _cachedTowers[i];
                    if (tower == null)
                        continue;

                    // Only towers belonging to the enemy side.
                    if (tower.ownerId == ownerId)
                        continue;

                    if (tower.currentHp <= 0)
                        continue;

                    Vector3 to = tower.transform.position - myPos;
                    to.y = 0f;
                    float distSqr = to.sqrMagnitude;
                    if (distSqr < 0.0001f || distSqr > chaseRangeSqr)
                        continue;

                    Vector3 dirTo = to.normalized;
                    float dot = Vector3.Dot(baseForward, dirTo);
                    if (dot < _frontArcDotThreshold)
                        continue;

                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        best = tower.transform;
                    }
                }
            }

            return best;
        }

        private void Update()
        {
            // If we're engaged in combat, we stay put.
            if (movementLocked) return;

            if (_laneDirection == Vector3.zero) return;

            // Default: move along the lane.
            Vector3 desiredDir = _laneDirection;

            // Try to acquire a target (unit or tower) in front of us within aggro range.
            var target = FindBestTargetInAggroRange();
            if (target != null && _runtime != null)
            {
                Vector3 toTarget = target.position - transform.position;
                toTarget.y = 0f;
                float distSqr = toTarget.sqrMagnitude;

                float attackRange = Mathf.Max(0.1f, _runtime.rangeMeters);
                float attackRangeSqr = attackRange * attackRange;

                // If we're outside attack range but inside aggro range, steer towards the target.
                if (distSqr > attackRangeSqr)
                {
                    Vector3 chaseDir = toTarget.normalized;
                    if (chaseDir != Vector3.zero)
                        desiredDir = chaseDir;
                }
                // If already within attack range, CombatResolver will handle locking movement & attacks.
            }

            transform.position += desiredDir * (moveSpeed * Time.deltaTime);

            // Keep moveDirection in sync for any systems that read it, but laneDirection stays fixed.
            moveDirection = desiredDir;
        }
    }
}

