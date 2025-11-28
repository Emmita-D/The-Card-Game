using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;    // <-- add this
using Game.Match.Grid;   // to use GridService
using Game.Match.Cards;  // 👈 for DraggableCard (static preview helpers)

namespace Game.Match.CardPhase
{
    /// <summary>
    /// Lets the player pick N tiles on the CardPhase board:
    /// - Only tiles on a given owner's side are valid.
    /// - Tiles must be empty according to GridService.IsOccupied.
    /// - Returns chosen tile positions (Vector2Int grid coords) via callback.
    ///
    /// Uses GridService for all tile/world math so it's consistent with placement.
    /// </summary>
    public class CardPhaseTileSelectionController : MonoBehaviour
    {
        public static CardPhaseTileSelectionController Instance { get; private set; }

        [Header("Grid / Raycast")]
        [SerializeField] private GridService grid;
        [SerializeField] private LayerMask gridMask = ~0;
        [SerializeField] private float maxRayDistance = 100f;

        [Header("Debug")]
        public bool logSelection = true;
        public bool drawDebugGizmos = true;

        [Header("On-Call ghost preview")]
        [SerializeField] private OnCallGhostSummonOverlay ghostOverlay;

        // Footprint (width/height in tiles) used when validating a candidate tile.
        // The clicked tile is treated as the bottom-left of this rectangle.
        private int footprintW = 1;
        private int footprintH = 1;

        // Selection state
        private bool isSelecting;
        private int selectingOwner;
        private int requiredCount;
        private Action<List<Vector2Int>> onComplete;
        private readonly HashSet<Vector2Int> selected = new HashSet<Vector2Int>();

        private Camera cam;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            cam = Camera.main;

            if (grid == null)
                grid = FindObjectOfType<GridService>();

            if (grid == null)
                Debug.LogError("[TileSelection] GridService reference is null. Assign it in the inspector.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Begin a tile pick flow. Player must pick exactly 'count' tiles.
        /// </summary>
        public void Begin(int ownerId, int count, Action<List<Vector2Int>> callback, int footprintWidth, int footprintHeight)
        {
            selectingOwner = ownerId;
            requiredCount = count;
            footprintW = footprintWidth;
            footprintH = footprintHeight;

            selected.Clear();
            onComplete = callback;

            // Clear any leftover ghosts from a previous selection.
            if (ghostOverlay != null)
            {
                ghostOverlay.ClearAllGhosts();
            }

            isSelecting = true;

            if (logSelection)
                Debug.Log($"[TileSelection] BEGIN (owner={ownerId}, need={count}, footprint={footprintWidth}x{footprintHeight}). Left-click valid tiles to select.");

            // Turn on footprint preview for this selection.
            if (grid != null)
            {
                DraggableCard.SetExternalFootprintPreview(grid, footprintW, footprintH, true, true);
            }
        }

        /// <summary>
        /// Cancel any ongoing tile selection (no callback).
        /// </summary>
        public void Cancel()
        {
            if (!isSelecting) return;

            isSelecting = false;
            selected.Clear();
            onComplete = null;

            if (grid != null)
            {
                DraggableCard.SetExternalFootprintPreview(grid, footprintW, footprintH, false, true);
            }
            DraggableCard.ClearExternalFootprintTileOverride();

            if (ghostOverlay != null)
            {
                ghostOverlay.ClearAllGhosts();
            }

            if (logSelection) Debug.Log("[TileSelection] CANCELLED.");
        }

        private void Update()
        {
            if (!isSelecting)
                return;

            if (cam == null)
                cam = Camera.main;

            if (cam == null)
            {
                Debug.LogError("[TileSelection] Camera.main is null; cannot read mouse ray.");
                return;
            }

            if (Mouse.current == null)
                return;

            // ------------------------------------------------------------
            // HOVER: drive the footprint preview from the tile under mouse
            // ------------------------------------------------------------
            bool hasTile = TryGetMouseTile(out var tile);

            if (hasTile)
            {
                bool isValidForPreview =
                    IsTileOnOwnerSide(tile, selectingOwner) &&
                    IsTileEmpty(tile);

                DraggableCard.UpdateExternalFootprintTile(tile, isValidForPreview);
            }
            else
            {
                DraggableCard.ClearExternalFootprintTileOverride();
            }

            // ------------------------------------------------------------
            // LEFT CLICK: confirm selection
            // ------------------------------------------------------------
            if (!Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (!hasTile)
                return;

            // Re-validate for actual selection.
            if (!IsTileOnOwnerSide(tile, selectingOwner))
            {
                if (logSelection)
                    Debug.Log($"[TileSelection] Tile {tile} rejected: not on owner {selectingOwner}'s side.");
                return;
            }

            if (!IsTileEmpty(tile))
            {
                if (logSelection)
                    Debug.Log($"[TileSelection] Tile {tile} rejected: not empty.");
                return;
            }

            if (selected.Contains(tile))
            {
                if (logSelection)
                    Debug.Log($"[TileSelection] Tile {tile} already selected.");
                return;
            }

            selected.Add(tile);

            // Spawn a ghost for this footprint (visual-only preview).
            if (ghostOverlay != null)
            {
                ghostOverlay.ShowGhostAt(tile, footprintW, footprintH);
            }

            if (logSelection)
                Debug.Log($"[TileSelection] SELECT {tile} ({selected.Count}/{requiredCount}).");

            if (selected.Count >= requiredCount)
            {
                var result = new List<Vector2Int>(selected);

                isSelecting = false;
                selected.Clear();

                // Turn off footprint preview and clear override.
                if (grid != null)
                {
                    DraggableCard.SetExternalFootprintPreview(grid, footprintW, footprintH, false, true);
                }
                DraggableCard.ClearExternalFootprintTileOverride();

                // Clear all ghosts now that we’re going to spawn real units.
                if (ghostOverlay != null)
                {
                    ghostOverlay.ClearAllGhosts();
                }

                if (logSelection)
                    Debug.Log("[TileSelection] COMPLETE.");

                var cb = onComplete;
                onComplete = null;
                cb?.Invoke(result);
            }
        }

        private bool TryGetMouseTile(out Vector2Int tile)
        {
            tile = default;

            if (grid == null)
                return false;

            if (Mouse.current == null)
                return false;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(screenPos);

            if (!Physics.Raycast(ray, out var hit, maxRayDistance, gridMask))
                return false;

            // Use same centering rule as the drag footprint preview.
            var origin = CenteredOriginForFootprint(hit.point);

            tile = origin;
            return true;
        }

        private bool IsTileOnOwnerSide(Vector2Int t, int ownerId)
        {
            if (grid == null) return false;

            // CURRENT GAME: single local board, fully controlled by the current player.
            // Any in-bounds tile is considered on the owner's side.
            return t.x >= 0 && t.x < grid.Width
                && t.y >= 0 && t.y < grid.Height;
        }

        private bool IsTileEmpty(Vector2Int t)
        {
            if (grid == null) return true;

            // First ask the grid if the rectangle is free (no real units, in bounds, etc.)
            if (!grid.CanPlaceRect(t, footprintW, footprintH))
                return false;

            // Then make sure it doesn't overlap any already-selected footprints
            foreach (var taken in selected)
            {
                if (FootprintsOverlap(t, taken))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Computes a bottom-left origin tile so that a footprintW x footprintH
        /// rectangle is visually centered around the given world point.
        /// Mirrors the centering used by RuntimeFootprintPreview.
        /// </summary>
        private Vector2Int CenteredOriginForFootprint(Vector3 world)
        {
            if (grid == null)
                return default;

            float ts = grid.TileSize;

            int w = Mathf.Max(1, footprintW);
            int h = Mathf.Max(1, footprintH);

            int ox = ((w & 1) == 1)
                ? Mathf.FloorToInt(world.x / ts) - (w - 1) / 2
                : Mathf.RoundToInt(world.x / ts) - (w / 2);

            int oy = ((h & 1) == 1)
                ? Mathf.FloorToInt(world.z / ts) - (h - 1) / 2
                : Mathf.RoundToInt(world.z / ts) - (h / 2);

            return new Vector2Int(ox, oy);
        }

        /// <summary>
        /// Checks whether a candidate placement at 'candidateOrigin' would overlap
        /// with an already-selected placement at 'existingOrigin', assuming both
        /// use the same footprintW/footprintH.
        /// </summary>
        private bool FootprintsOverlap(Vector2Int candidateOrigin, Vector2Int existingOrigin)
        {
            int axMin = candidateOrigin.x;
            int axMax = candidateOrigin.x + footprintW - 1;
            int azMin = candidateOrigin.y;
            int azMax = candidateOrigin.y + footprintH - 1;

            int bxMin = existingOrigin.x;
            int bxMax = existingOrigin.x + footprintW - 1;
            int bzMin = existingOrigin.y;
            int bzMax = existingOrigin.y + footprintH - 1;

            // Separating axis test in grid space
            bool separated =
                axMax < bxMin || bxMax < axMin ||
                azMax < bzMin || bzMax < azMin;

            return !separated;
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos) return;
            if (grid == null) return;

            float tileSize = grid.TileSize;

            Gizmos.color = new Color(1f, 1f, 1f, 0.06f);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var t = new Vector2Int(x, y);
                    var center = grid.TileCenterToWorld(t, 0f);
                    var size = new Vector3(tileSize, 0.01f, tileSize);

                    Gizmos.DrawWireCube(center, size);
                }
            }
        }
    }
}
