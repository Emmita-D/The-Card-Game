using Game.Match.Units;

namespace Game.Match.Status
{
    /// <summary>
    /// Taunt: a tag-style status that marks this unit as a high-priority
    /// aggro target for enemy units and towers.
    ///
    /// - Does not change stats directly.
    /// - Does not expire on its own; it is expected to be removed by
    ///   external systems if needed (or treated as permanent for innate taunt).
    /// </summary>
    public class TauntStatus : StatusEffect
    {
        public override string Name => "Taunt";

        /// <summary>
        /// Taunt does not modify stats directly. It only influences
        /// target selection and movement when consulted via
        /// UnitStatusController.HasTaunt().
        /// </summary>
        public override StatModifier GetStatModifier()
        {
            return StatModifier.None;
        }

        // No time-based expiration logic here.
        // If you later want temporary taunt, you can:
        // - extend this class, or
        // - override OnUpdate / OnTurnAdvanced and flip IsExpired.
    }
}
