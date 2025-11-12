using UnityEngine;
using Game.Match.Cards;

namespace Game.Match.Battle
{
    /// <summary>
    /// Attached to each spawned UnitAgent at battle start to remember
    /// its original CardPhase world position (so we can map back to grid).
    /// </summary>
    public class UnitOriginStamp : MonoBehaviour
    {
        public int ownerId;
        public CardSO sourceCard;
        public Vector3 cardPhaseWorld; // snapped world from CardPhase grid
    }
}
