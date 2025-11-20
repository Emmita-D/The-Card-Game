using UnityEngine;
using Game.Core;   // CardType, Realm, Race, AttackMode, MovementType, SizeClass

namespace Game.Match.Cards
{
    // v1 spell effects.
    public enum SpellEffectKind
    {
        None = 0,
        SearchUnitByRealm = 1,
        RefillManaToMax = 2,
        BuffRandomHandUnitSimple = 3
    }
    // v1 trap effects.
    public enum TrapEffectKind
    {
        None = 0,
        TowerBelowHalf_DamageRandomEnemyUnit = 1,
        GroundEnemyFlierForTime = 2,
        Outnumbered_SlowEnemyUnitsForTime = 3,
        VulnerableEnemyUnitsForTime = 4            // NEW: apply vulnerability to all enemy units
    }
    public enum BuffLifetimeKind
    {
        Permanent = 0,
        TimeSeconds = 1,
        AttackCount = 2,
        TurnCount = 3
    }

    /// <summary>
    /// v1 targeting categories for CardPhase "Call" effects.
    /// This is intentionally tiny for now.
    /// </summary>
    public enum OnCallTargetingKind
    {
        None = 0,

        /// <summary>
        /// When called, choose ONE friendly unit that matches:
        /// - same owner
        /// - Race = Vorgco
        /// - isSavageArchetype = true
        /// Then apply some effect (e.g. give Savage stacks).
        /// </summary>
        ChosenFriendlySavageVorgco = 1,
    }

    [CreateAssetMenu(fileName = "NewCard", menuName = "Game/Cards/Card", order = 0)]
    public class CardSO : ScriptableObject
    {
        [Header("Identity")]
        public string cardName = "New Card";
        [TextArea] public string rulesText;

        [Header("Classification")]
        public CardType type = CardType.Unit;
        public Realm realm = Realm.Empyrean;
        public Race race = Race.Human;

        [Header("Spell (if Spell)")]
        [Tooltip("Simple v1 effect used by the CardEffectRunner for spells.")]
        public SpellEffectKind spellEffect = SpellEffectKind.None;

        [Tooltip("Realm filter for SearchUnitByRealm spells. Only unit cards from this realm are eligible.")]
        public Realm spellSearchRealmFilter = Realm.Empyrean;

        [Tooltip("Attack bonus for simple buff spells that target a unit in hand.")]
        public int spellBuffAttackAmount = 2;

        [Tooltip("Health bonus for simple buff spells that target a unit in hand.")]
        public int spellBuffHealthAmount = 2;

        [Header("Spell Buff Lifetime")]
        public BuffLifetimeKind spellBuffLifetimeKind = BuffLifetimeKind.Permanent;
        public float spellBuffDurationSeconds = 0f;
        public int spellBuffAttackCount = 0;
        public int spellBuffTurnCount = 0;

        [Header("Trap (if Trap)")]
        [Tooltip("Simple v1 effect used by TrapService for traps.")]
        public TrapEffectKind trapEffect = TrapEffectKind.None;

        [Tooltip("Damage dealt by this trap when it triggers (for tower-below-half trap).")]
        public int trapDamageAmount = 100;

        [Tooltip("Fraction of tower max HP below which this trap triggers (e.g., 0.5 = 50%).")]
        [Range(0f, 1f)] public float trapHpThresholdFraction = 0.5f;

        [Tooltip("Duration in seconds for GroundEnemyFlierForTime traps (e.g., 10f).")]
        [Min(0f)] public float trapGroundDurationSeconds = 10f;

        [Tooltip("Outnumbered slow trap tuning")]
        [Range(0f, 1f)] 
        public float trapSlowPercent = 0.40f;          // 0.4 = 40% slow
        public float trapSlowDurationSeconds = 6f;     // how long the slow lasts

        [Tooltip("Vulnerability trap tuning (extra damage taken by enemy units).")]
        [Range(1f, 3f)]
        public float trapVulnerabilityDamageTakenMultiplier = 1.25f; // 25% extra damage
        public float trapVulnerabilityDurationSeconds = 6f;          // how long the vulnerability lasts

        [Header("Footprint (tiles)")]
        [Range(1, 4)] public int sizeW = 1;
        [Range(1, 4)] public int sizeH = 1;

        [Header("Cost")]
        [Min(0)] public int manaStars = 0;

        [Header("Unit Stats (if Unit)")]
        public int attack = 0;
        public int health = 1;
        public AttackMode attackMode = AttackMode.Melee;
        public float rangeMeters = 0f;

        [Header("On-Call Targeting (CardPhase, v1)")]
        [Tooltip("If not None, this unit's Call requires choosing a target on the CardPhase board.")]
        public OnCallTargetingKind onCallTargeting = OnCallTargetingKind.None;

        [Tooltip("For effects like: 'When called, give X Savage tokens to a chosen friendly Savage Vorg'co unit'.")]
        [Min(0)]
        public int onCallSavageStacksToChosenTarget = 0;

        [Header("Savage (unit-only, v1)")]
        [Tooltip("Marks this unit as part of the 'Savage' archetype for card effects (e.g., deathrattles that target Savage units only).")]
        public bool isSavageArchetype = false;

        [Tooltip("If > 0, this unit gains this many Savage stacks whenever it delivers a killing blow to an enemy unit.")]
        public int savageStacksOnKill = 0;

        [Tooltip("If > 0, this unit gains this many Savage tokens whenever a Vorg'co unit of the same realm dies on the battlefield.")]
        [Min(0)]
        public int savageStacksOnVorgcoDeath = 0;

        [Tooltip("If > 0, this unit gains this many Savage tokens when its health first drops below 25% of its base HP (per damage event).")]
        [Min(0)]
        public int savageStacksOnLowHealth = 0;

        [Tooltip("If > 0, when THIS unit dies, it gives this many Savage tokens to a random friendly unit that is both race = Vorg'co and marked as a Savage archetype.")]
        [Min(0)]
        public int savageStacksOnDeathToSavage = 0;

        [Header("Movement (if Unit)")]
        public MovementType movement = MovementType.Ground;

        [Header("Aggro / Targeting (if Unit)")]
        [Tooltip("How far (in multiples of attack range) this unit will look for targets to chase.")]
        [Min(0f)] public float chaseRangeMultiplier = 3f;

        [Tooltip("Dot threshold for what counts as 'in front'. 1 = dead ahead, 0 = full 180° front arc.")]
        [Range(-1f, 1f)] public float frontArcDotThreshold = 0.2f;

        [Header("Attack permissions (optional nerfs)")]
        [Tooltip("If false, this unit can never attack ground units, even if its type would normally allow it.")]
        public bool canAttackGroundUnits = true;

        [Tooltip("If false, this unit can never attack flying units, even if its type would normally allow it.")]
        public bool canAttackFlyingUnits = true;

        [Tooltip("If false, this unit can never damage towers, even if its type would normally allow it.")]
        public bool canAttackTowers = true;

        [Header("Special movement / behaviour")]
        [Tooltip("If true and this is a Flying Melee unit, it will dive to the ground when attacking ground targets, making it hittable by ground melee while diving.")]
        public bool isDiveFlier = false;

        [Header("Visuals & Prefab")]
        public Sprite artSprite;
        public GameObject unitPrefab;

        [Header("Legend")]
        public bool isLegend = false;

        [Header("Realm matchup (unit effect)")]
        [Tooltip("If > 1, this unit deals extra damage when attacking units from the opposing realm (Empyrean vs Infernum). 1 = no bonus.")]
        public float realmBonusVsOpposingMultiplier = 1f;



        // Back-compat for older code that expects an enum SizeClass
        public SizeClass size
        {
            get
            {
                if (sizeW == 1 && sizeH == 1) return SizeClass.I_1x1;
                if (sizeW == 1 && sizeH == 2) return SizeClass.II_1x2;
                if (sizeW == 2 && sizeH == 2) return SizeClass.III_2x2;
                // Fallback: treat larger shapes as 2x2 for legacy callers.
                return SizeClass.III_2x2;
            }
        }
    }
}
