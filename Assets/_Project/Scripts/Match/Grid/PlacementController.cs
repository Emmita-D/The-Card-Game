using UnityEngine;
using Game.Match.Cards;
using Game.Core;


namespace Game.Match.Grid
{
    public class PlacementController : MonoBehaviour
    {
        [SerializeField] GridService grid; [SerializeField] LayerMask gridMask = ~0; [SerializeField] Transform unitParent;
        public bool TryPlaceUnit(CardInstance card, Vector3 worldPos, out Vector2Int originTile)
        {
            originTile = default; if (card.data.type != CardType.Unit) return false;
            if (!grid.WorldToTile(worldPos, out var t)) return false;
            if (!grid.CanPlace(card.data.size, t)) return false;
            grid.Place(card.data.size, t); originTile = t; return true;
        }


        public Vector3 SnapToWorld(Vector2Int tile) => grid.TileToWorld(tile, 0f);
    }
}