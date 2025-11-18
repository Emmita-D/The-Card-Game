using Game.Match.Units;

namespace Game.Match.Status
{
    // Base class for all future statuses/buffs/debuffs.
    // Supports:
    // - Stat modifiers
    // - Optional lifetime via time, attacks, or turns using hooks.
    public abstract class StatusEffect
    {
        // Short identifier (can be used for debug/logs/UI).
        public virtual string Name
        {
            get { return "StatusEffect"; }
        }

        // Has this status finished and should be removed?
        public bool IsExpired { get; protected set; }

        // Override this to provide stat changes.
        public virtual StatModifier GetStatModifier()
        {
            return StatModifier.None;
        }

        // Called every frame on the owning UnitRuntime (for time-based effects).
        public virtual void OnUpdate(UnitRuntime owner, float deltaTime)
        {
            // Default: do nothing.
        }

        // Called whenever the owning unit performs an attack
        // (for "lasts N attacks" type buffs).
        public virtual void OnOwnerAttack(UnitRuntime owner)
        {
            // Default: do nothing.
        }

        // Called when a "turn" advances for the owning unit.
        // You decide WHAT counts as a "turn" by where you call it.
        public virtual void OnTurnAdvanced(UnitRuntime owner)
        {
            // Default: do nothing.
        }
    }
}
