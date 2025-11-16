using UnityEngine;
using Game.Core;   // <-- keep

namespace Game.Match.Cards
{
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

        [Header("Movement (if Unit)")]
        public MovementType movement = MovementType.Ground;

        [Header("Aggro / Targeting (if Unit)")]
        [Tooltip("How far (in multiples of attack range) this unit will look for targets to chase.")]
        [Min(0f)] public float chaseRangeMultiplier = 3f;

        [Tooltip("Dot threshold for what counts as 'in front'. 1 = dead ahead, 0 = full 180° front arc.")]
        [Range(-1f, 1f)] public float frontArcDotThreshold = 0.2f;

        [Header("Visuals & Prefab")]
        public Sprite artSprite;
        public GameObject unitPrefab;

        // ---------- ADD THIS ----------
        [Header("Legend")]
        public bool isLegend = false;

        // Back-compat for older code that expects an enum SizeClass
        // Maps your sizeW/sizeH to the nearest legacy bucket.
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
        // ---------- END ADD ----------
    }
}
