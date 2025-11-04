// If you already defined any of these, keep yours and delete duplicates.
namespace Game.Core
{
    public enum CardType { Unit, Spell, Trap }
    public enum Realm { Empyrean, Infernum }
    public enum Race { Human, Vorgco, Elf, Dragon, Alien, Divine, Shatani, Machine, Colossal, Primal, Undead }
    public enum AttackMode { Melee, Ranged }
    public enum MovementType { Ground, Flying }
    public enum SizeClass
    {
        I_1x1 = 0,
        II_1x2 = 1,  // fixed vertical
        III_2x2 = 2
    }

}