using UnityEngine;


namespace Game.Core.Config
{
    [CreateAssetMenu(menuName = "Game/Rules/Balance Config")]
    public class BalanceConfigSO : ScriptableObject
    {
        [Header("Card Phase")] public int cardPhaseSeconds = 30; public int graceSeconds = 5;
        [Header("Mana")] public int manaCap = 10;
        [Header("Retreat")] public float retreatChannelSeconds = 2f; public bool retreatInterruptible = false; // must remain false per rules
    }
}