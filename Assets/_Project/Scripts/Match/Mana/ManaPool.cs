using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Match.Mana
{
    [DisallowMultipleComponent]
    public class ManaPool : MonoBehaviour
    {
        [Header("Config (optional)")]
        [Tooltip("Optional balance asset. Not required for now.")]
        public ScriptableObject cfg;

        // Migrate any old serialized values automatically:
        [FormerlySerializedAs("capIfNoConfig")]
        [Tooltip("Total crystal capacity at start.")]
        [Min(0)] public int startSlots = 10;

        [FormerlySerializedAs("startMax")]
        [Tooltip("Filled crystals at start.")]
        [Min(0)] public int startCurrent = 0;

        public int Slots { get; private set; }
        public int Current { get; private set; }

        /// <summary>(current, slots)</summary>
        public event Action<int, int> OnChanged;

        void Awake()
        {
            Slots = Mathf.Max(0, startSlots);
            Current = Mathf.Clamp(startCurrent, 0, Slots);
            Raise();
        }

        public bool CanAfford(int cost) => cost <= Current;

        public bool TrySpend(int cost)
        {
            if (cost < 0) cost = 0;
            if (!CanAfford(cost)) return false;
            Current -= cost;
            Raise();
            return true;
        }

        public void Set(int current, int slots)
        {
            Slots = Mathf.Max(0, slots);
            Current = Mathf.Clamp(current, 0, Slots);
            Raise();
        }

        public void SetCurrent(int v)
        {
            Current = Mathf.Clamp(v, 0, Slots);
            Raise();
        }

        public void SetSlots(int v)
        {
            Slots = Mathf.Max(0, v);
            Current = Mathf.Min(Current, Slots);
            Raise();
        }

        void Raise() => OnChanged?.Invoke(Current, Slots);
    }
}
