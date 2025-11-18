using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.State; // BattleUnitSeed

namespace Game.Match.CardPhase
{
    /// <summary>
    /// Card-phase registry of units that have been placed on the grid.
    /// Stores card + world position + ownerId, and can build BattleUnitSeed lists.
    /// De-duplicates entries to safely mirror survivors across turns.
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

            // NEW: per-instance stat bonuses relative to CardSO base stats
            public int bonusAttack;
            public int bonusHealth;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        // Optional board-center hint used elsewhere (kept from your original file).
        private float? _localBoardCenterX;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void SetLocalBoardCenterX(float centerX)
        {
            _localBoardCenterX = centerX;
        }

        public bool TryGetLocalBoardCenterX(out float centerX)
        {
            if (_localBoardCenterX.HasValue)
            {
                centerX = _localBoardCenterX.Value;
                return true;
            }

            centerX = 0f;
            return false;
        }

        // -----------------------------
        // Registration + de-dup support
        // -----------------------------

        /// <summary>
        /// True if an entry with the same (card ref, ownerId) is already present
        /// at approximately the same world position (<= posEps).
        /// </summary>
        public bool Contains(CardSO card, Vector3 worldPos, int ownerId, float posEps = 0.01f)
        {
            if (card == null) return false;
            float eps2 = posEps * posEps;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!ReferenceEquals(e.card, card)) continue;
                if (e.ownerId != ownerId) continue;
                if ((e.worldPos - worldPos).sqrMagnitude > eps2) continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Register a placed/survived unit. De-duplicates by (card ref, ownerId, ~worldPos).
        /// This overload assumes NO extra buffs (CardSO stats are the baseline).
        /// </summary>
        public void Register(CardSO card, Vector3 worldPos, int ownerId)
        {
            if (card == null) return;

            // De-dup to avoid double entries when survivors are mirrored back
            if (Contains(card, worldPos, ownerId))
                return;

            _entries.Add(new Entry
            {
                card = card,
                ownerId = ownerId,
                worldPos = worldPos,
                bonusAttack = 0,
                bonusHealth = 0
            });

            Debug.Log($"[BattlePlacementRegistry] Registered card={card.name}, owner={ownerId}, pos={worldPos}");
        }

        /// <summary>
        /// Register a placed unit *from hand*, preserving its per-instance buffs.
        /// Uses CardInstance to compute bonuses relative to CardSO.
        /// </summary>
        public void Register(CardInstance cardInstance, Vector3 worldPos, int ownerId)
        {
            if (cardInstance == null || cardInstance.data == null)
                return;

            var so = cardInstance.data;

            // De-dup based on the underlying CardSO + position + owner
            if (Contains(so, worldPos, ownerId))
                return;

            int baseAtk = so.attack;
            int baseHp = so.health;

            int finalAtk = cardInstance.GetFinalAttack();
            int finalHp = cardInstance.GetFinalHealth();

            int atkBonus = finalAtk - baseAtk;
            int hpBonus = finalHp - baseHp;

            _entries.Add(new Entry
            {
                card = so,
                ownerId = ownerId,
                worldPos = worldPos,
                bonusAttack = atkBonus,
                bonusHealth = hpBonus
            });

            Debug.Log(
                $"[BattlePlacementRegistry] Registered buffed card={so.name}, owner={ownerId}, pos={worldPos}, " +
                $"bonus={atkBonus}/{hpBonus}"
            );
        }

        /// <summary>
        /// Build unit seeds for the given owner (0/1), using exact world positions.
        /// </summary>
        public List<BattleUnitSeed> BuildSeedsForOwner(int ownerId)
        {
            var list = new List<BattleUnitSeed>(_entries.Count);

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
                    exactPosition = e.worldPos,
                    bonusAttack = e.bonusAttack,
                    bonusHealth = e.bonusHealth
                });
            }

            Debug.Log($"[BattlePlacementRegistry] BuildSeedsForOwner({ownerId}) -> {list.Count} seeds");
            return list;
        }

        /// <summary>
        /// Clear all entries (call after starting a battle if desired).
        /// Survivors will be mirrored back in on return to CardPhase.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
