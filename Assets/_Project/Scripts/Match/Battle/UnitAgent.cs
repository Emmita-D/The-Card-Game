using UnityEngine;
using Game.Match.Cards;
using Game.Match.Units;
using Game.Match.Graveyard;
using Game.Core;

namespace Game.Match.Battle
{
    /// <summary>
    /// Handles per-unit runtime data for the battle stage:
    /// - Stores owner + source card
    /// - Initializes UnitRuntime from CardSO
    /// - Moves along its lane and steers toward aggro targets
    /// - Exposes attack timing fields used by CombatResolver
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
        [SerializeField] private Vector3 laneDirection = Vector3.zero;

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

        // Aggro settings copied from CardSO so behaviour is configured per card.
        private float _chaseRangeMultiplier = 3f;
        private float _frontArcDotThreshold = 0.2f;

        // Static cache so we don't keep searching for towers every frame.
        private static BattleTower[] _cachedTowers;

        // Remember who we're currently focused on (unit or tower).
        private Transform _currentTarget;

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

            laneDirection = direction.normalized;
            moveDirection = laneDirection;
            nextAttackTime = 0f;
            movementLocked = false;
            _currentTarget = null;

            if (_runtime == null)
                _runtime = GetComponent<UnitRuntime>();

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
        /// Checks if the current remembered target is still a valid aggro target
        /// (alive, in chase range, allowed by CombatRules).
        /// If valid, returns it; otherwise clears it and returns null.
        /// </summary>
        private Transform ValidateCurrentTarget(float chaseRangeSqr, Vector3 myPos)
        {
            if (_currentTarget == null)
                return null;

            // Is it a tower?
            var tower = _currentTarget.GetComponent<BattleTower>();
            if (tower != null)
            {
                if (tower.ownerId == ownerId || tower.currentHp <= 0)
                {
                    _currentTarget = null;
                    return null;
                }

                if (!CombatRules.CanUnitAttackTower(this, tower))
                {
                    _currentTarget = null;
                    return null;
                }

                Vector3 to = tower.transform.position - myPos;
                to.y = 0f;
                if (to.sqrMagnitude > chaseRangeSqr)
                {
                    _currentTarget = null;
                    return null;
                }

                return _currentTarget;
            }

            // Otherwise, treat it as a unit
            var unit = _currentTarget.GetComponent<UnitAgent>();
            if (unit == null)
            {
                _currentTarget = null;
                return null;
            }

            var enemyRuntime = unit.GetComponent<UnitRuntime>();
            if (enemyRuntime == null || enemyRuntime.health <= 0)
            {
                _currentTarget = null;
                return null;
            }

            if (!CombatRules.CanUnitAttackUnit(this, unit))
            {
                _currentTarget = null;
                return null;
            }

            Vector3 toUnit = unit.transform.position - myPos;
            toUnit.y = 0f;
            if (toUnit.sqrMagnitude > chaseRangeSqr)
            {
                _currentTarget = null;
                return null;
            }

            return _currentTarget;
        }

        /// <summary>
        /// Returns the closest enemy (unit or tower) in front of us within our aggro range,
        /// that we are actually allowed to attack according to CombatRules.
        /// Uses currentTarget if still valid, so we don't drop targets that pass behind us.
        /// </summary>
        private Transform FindBestTargetInAggroRange()
        {
            var resolver = CombatResolver.Instance;
            if (resolver == null || _runtime == null)
                return null;

            var enemyUnits = (ownerId == 0)
                ? resolver.RemoteUnits
                : resolver.LocalUnits;

            Vector3 myPos = transform.position;

            Vector3 baseForward = laneDirection.sqrMagnitude > 0.0001f
                ? laneDirection.normalized
                : transform.forward;

            float attackRange = Mathf.Max(0.1f, _runtime.rangeMeters);
            float chaseRange = attackRange * Mathf.Max(0f, _chaseRangeMultiplier);
            if (chaseRange <= 0f)
                return null;

            float chaseRangeSqr = chaseRange * chaseRange;

            // 0) First, see if our current target is still valid (no front-arc restriction for retention).
            var validated = ValidateCurrentTarget(chaseRangeSqr, myPos);
            if (validated != null)
                return validated;

            // If we got here, we have no valid current target → search for a new one.
            Transform best = null;
            float bestDistSqr = float.MaxValue;

            // ---- 1) Consider enemy units ----
            if (enemyUnits != null)
            {
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var enemy = enemyUnits[i];
                    if (enemy == null || enemy == this)
                        continue;

                    var enemyRuntime = enemy.GetComponent<UnitRuntime>();
                    if (enemyRuntime == null || enemyRuntime.health <= 0)
                        continue;

                    // Respect combat rules (ground melee can't hit air, per-card nerfs, etc.)
                    if (!CombatRules.CanUnitAttackUnit(this, enemy))
                        continue;

                    Vector3 to = enemy.transform.position - myPos;
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
                        best = enemy.transform;
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

                    if (tower.ownerId == ownerId)
                        continue;

                    if (tower.currentHp <= 0)
                        continue;

                    if (!CombatRules.CanUnitAttackTower(this, tower))
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

            _currentTarget = best;
            return best;
        }

        private void Update()
        {
            // If we're engaged in combat, we stay put.
            if (movementLocked) return;

            if (laneDirection == Vector3.zero) return;

            if (_runtime == null)
                _runtime = GetComponent<UnitRuntime>();

            // Default: move along the lane.
            Vector3 desiredDir = laneDirection;

            bool isDiveFlier =
                _runtime != null &&
                _runtime.isDiveFlier &&
                _runtime.movementType == MovementType.Flying &&
                _runtime.attackMode == AttackMode.Melee;

            var target = FindBestTargetInAggroRange();
            if (target != null && _runtime != null)
            {
                Vector3 toTarget = target.position - transform.position;
                toTarget.y = 0f;
                float distSqr = toTarget.sqrMagnitude;

                float attackRange = Mathf.Max(0.1f, _runtime.rangeMeters);
                float attackRangeSqr = attackRange * attackRange;

                // Handle dive-flier height layer based on what we're attacking.
                if (isDiveFlier)
                {
                    var targetTower = target.GetComponent<BattleTower>();
                    var targetUnitRt = target.GetComponent<UnitRuntime>();

                    bool targetIsGround = false;
                    bool targetIsFlying = false;

                    if (targetTower != null)
                    {
                        targetIsGround = true;
                    }
                    else if (targetUnitRt != null)
                    {
                        if (targetUnitRt.heightLayer == HeightLayer.Ground)
                            targetIsGround = true;
                        else
                            targetIsFlying = true;
                    }

                    if (targetIsGround)
                        _runtime.heightLayer = HeightLayer.Ground;
                    else if (targetIsFlying)
                        _runtime.heightLayer = HeightLayer.Air;
                }

                // If we're outside attack range but inside aggro, steer toward the target.
                if (distSqr > attackRangeSqr)
                {
                    Vector3 chaseDir = toTarget.normalized;
                    if (chaseDir != Vector3.zero)
                        desiredDir = chaseDir;
                }
                // If already within attack range, CombatResolver will lock movement via movementLocked.
            }
            else
            {
                // No target -> dive fliers float back up to Air.
                if (isDiveFlier &&
                    _runtime.heightLayer == HeightLayer.Ground &&
                    _runtime.movementType == MovementType.Flying)
                {
                    _runtime.heightLayer = HeightLayer.Air;
                }

                _currentTarget = null;
            }

            // ---- NEW: use status-aware final move speed ----
            float speedThisFrame = moveSpeed;
            if (_runtime != null)
            {
                speedThisFrame = _runtime.GetFinalMoveSpeed(moveSpeed);
            }

            transform.position += desiredDir * (speedThisFrame * Time.deltaTime);

            // Keep moveDirection in sync for any systems that read it.
            moveDirection = desiredDir;
        }
    }
}
