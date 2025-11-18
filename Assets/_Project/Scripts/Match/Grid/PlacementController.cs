using UnityEngine;
using Game.Match.Cards;
using Game.Core;
using Game.Match.CardPhase;   // <-- NEW

namespace Game.Match.Grid
{
    public class PlacementController : MonoBehaviour
    {
        [SerializeField] GridService grid;
        [SerializeField] LayerMask gridMask = ~0;
        [SerializeField] Transform unitParent;

        /// <summary>
        /// Tries to place a unit card on the grid at the given world position.
        /// If successful:
        /// - Marks tiles as occupied in GridService
        /// - Registers the placement in BattlePlacementRegistry so BattleStage can mirror it
        /// </summary>
        public bool TryPlaceUnit(CardInstance card, Vector3 worldPos, out Vector2Int originTile)
        {
            originTile = default;

            if (card == null || card.data == null)
            {
                Debug.LogWarning("[PlacementController] TryPlaceUnit called with null card or data.");
                return false;
            }

            // Only units can be placed
            if (card.data.type != CardType.Unit)
                return false;

            // Convert world position to grid tile
            if (!grid.WorldToTile(worldPos, out var t))
                return false;

            // Check occupancy / bounds
            if (!grid.CanPlace(card.data.size, t))
                return false;

            // Mark tiles as occupied
            grid.Place(card.data.size, t);
            originTile = t;

            // Compute snapped world position at the tile center
            Vector3 snappedWorld = grid.TileToWorld(t, 0f);

            // Register this placement for the battle stage
            var registry = BattlePlacementRegistry.Instance;
            if (registry != null)
            {
                registry.Register(card, snappedWorld, ownerId: 0); // 0 = local player
                Debug.Log($"[PlacementController] Placed card {card.data.name} at tile {t}, world {snappedWorld}");
            }
            else
            {
                Debug.LogWarning("[PlacementController] BattlePlacementRegistry.Instance is null; placement not recorded.");
            }

            return true;
        }

        public Vector3 SnapToWorld(Vector2Int tile) => grid.TileToWorld(tile, 0f);
    }
}
