using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;    // <-- add this
using Game.Match.Grid;   // to use GridService

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
            if (isSelecting)
            {
                Debug.LogWarning("[TileSelection] Begin called while already selecting; ignoring.");
                return;
            }

            if (count <= 0)
            {
                Debug.LogWarning("[TileSelection] Begin called with non-positive count; ignoring.");
                return;
            }

            if (grid == null)
            {
                Debug.LogError("[TileSelection] Cannot begin, GridService is null.");
                return;
            }

            selectingOwner = ownerId;
            requiredCount = count;
            onComplete = callback;
            selected.Clear();
            isSelecting = true;

            footprintW = Mathf.Max(1, footprintWidth);
            footprintH = Mathf.Max(1, footprintHeight);

            // Reuse DraggableCard's footprint preview (FootprintPreviewRect / FootprintPreview).
            if (grid != null)
            {
                DraggableCard.SetExternalFootprintPreview(grid, footprintW, footprintH, true, true);
            }

            if (logSelection)
                Debug.Log(
                    $"[TileSelection] BEGIN (owner={ownerId}, need={count}, footprint={footprintW}x{footprintH}). " +
                    "Left-click valid tiles to select."
                );
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

            if (logSelection) Debug.Log("[TileSelection] CANCELLED.");
        }

        private void Update()
        {
            if (!isSelecting) return;

            if (cam == null)
                cam = Camera.main;

            if (cam == null)
            {
                Debug.LogError("[TileSelection] Camera.main is null; cannot read mouse ray.");
                return;
            }

            // New Input System: use Mouse.current instead of UnityEngine.Input
            if (Mouse.current == null)
                return; // no mouse device available

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (TryGetMouseTile(out var tile))
                {
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

                    if (logSelection)
                        Debug.Log($"[TileSelection] SELECT {tile} ({selected.Count}/{requiredCount}).");

                    if (selected.Count >= requiredCount)
                    {
                        var result = new List<Vector2Int>(selected);

                        isSelecting = false;
                        selected.Clear();

                        if (grid != null)
                        {
                            DraggableCard.SetExternalFootprintPreview(grid, footprintW, footprintH, false, true);
                        }

                        if (logSelection)
                            Debug.Log("[TileSelection] COMPLETE.");

                        var cb = onComplete;
                        onComplete = null;
                        cb?.Invoke(result);
                    }
                }
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

            if (!grid.WorldToTile(hit.point, out var t))
                return false;

            tile = t;
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

            // Use full footprint instead of just the single tile.
            return grid.CanPlaceRect(t, footprintW, footprintH);
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
