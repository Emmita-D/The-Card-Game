using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.State; // BattleUnitSeed

namespace Game.Match.CardPhase
{
    /// <summary>
    /// Card-phase registry of units that have been placed on the grid.
    /// Stores card + world position + ownerId, and can build BattleUnitSeed lists.
    /// </summary>
    public class BattlePlacementRegistry : MonoBehaviour
    {
        public static BattlePlacementRegistry Instance { get; private set; }

        [System.Serializable]
        public struct Entry
        {
            public CardSO card;
            public int ownerId;        // 0 = local, 1 = remote (future)
            public Vector3 worldPos;   // exact position on the board
        }

        private readonly List<Entry> _entries = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Register a placed unit at the given world position.
        /// </summary>
        public void Register(CardSO card, Vector3 worldPos, int ownerId)
        {
            if (card == null) return;

            _entries.Add(new Entry
            {
                card = card,
                ownerId = ownerId,
                worldPos = worldPos
            });

            Debug.Log($"[BattlePlacementRegistry] Registered card={card.name}, owner={ownerId}, pos={worldPos}");
        }

        /// <summary>
        /// Build unit seeds for the given owner (0/1), using exact positions.
        /// </summary>
        public List<BattleUnitSeed> BuildSeedsForOwner(int ownerId)
        {
            var list = new List<BattleUnitSeed>();

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.card == null) continue;
                if (e.ownerId != ownerId) continue;

                list.Add(new BattleUnitSeed
                {
                    card = e.card,
                    ownerId = e.ownerId,
                    laneIndex = 0,
                    spawnOffset = 0f,
                    useExactPosition = true,
                    exactPosition = e.worldPos
                });
            }

            Debug.Log($"[BattlePlacementRegistry] BuildSeedsForOwner({ownerId}) -> {list.Count} seeds");
            return list;
        }

        /// <summary>
        /// Clear all entries (call after starting a battle if desired).
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
