using System.Collections.Generic;
using UnityEngine;
using Game.Match.Grid;

namespace Game.Match.CardPhase
{
    /// <summary>
    /// Manages visual-only "ghost" units for On-Call extra summon selection.
    /// Ghosts:
    /// - Are spawned on the CardPhase grid as a preview.
    /// - Do NOT register in BattlePlacementRegistry or affect statuses.
    /// - Are positioned at the CENTER of the full footprint rectangle.
    /// </summary>
    public class OnCallGhostSummonOverlay : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("GridService used to convert tile coords to world positions.")]
        public GridService grid;

        [Tooltip("Optional parent for all ghost instances. If null, uses this transform.")]
        public Transform ghostRoot;

        [Header("Ghost visuals")]
        [Tooltip("Default prefab to use for ghost tokens if no specific prefab is provided.")]
        public GameObject defaultGhostPrefab;

        [Tooltip("Vertical offset above the grid plane for ghost visuals.")]
        public float yOffset = 0.02f;

        [Tooltip("Layer to assign to ghost instances (should NOT be included in CardPhase grid raycast mask).")]
        public int ghostLayer = 0;

        // Internal mapping: origin tile (bottom-left of footprint) -> ghost instance
        readonly Dictionary<Vector2Int, GameObject> ghostsByOrigin = new Dictionary<Vector2Int, GameObject>();

        void Awake()
        {
            if (!grid)
            {
                grid = GetComponentInParent<GridService>();
                if (!grid)
                {
                    Debug.LogError("[OnCallGhostSummonOverlay] GridService reference is null. Assign it in the inspector.");
                }
            }

            if (!ghostRoot)
            {
                ghostRoot = transform;
            }
        }

        /// <summary>
        /// Show a ghost for a footprint placed at 'origin' (bottom-left tile),
        /// using a footprintW x footprintH rectangle. The ghost itself is
        /// positioned at the CENTER of that rectangle.
        /// </summary>
        public void ShowGhostAt(Vector2Int origin, int footprintW, int footprintH, GameObject prefabOverride = null)
        {
            if (ghostsByOrigin.ContainsKey(origin))
                return;

            if (grid == null)
                return;

            var prefab = prefabOverride != null ? prefabOverride : defaultGhostPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[OnCallGhostSummonOverlay] No ghost prefab assigned; cannot show ghost at origin {origin}.");
                return;
            }

            // --- Compute the WORLD CENTER of the full footprint rectangle ---
            // origin = bottom-left footprint tile
            float ts = grid.TileSize;

            // World position of the bottom-left corner of the footprint
            Vector3 baseWorld = grid.TileToWorld(origin, 0f);

            // Half-size of the footprint in world units
            float halfW = footprintW * ts * 0.5f;
            float halfH = footprintH * ts * 0.5f;

            // Center of the footprint rectangle
            Vector3 worldPos = baseWorld + new Vector3(halfW, 0f, halfH);
            worldPos.y += yOffset;

            var instance = Instantiate(prefab, worldPos, Quaternion.identity, ghostRoot);

            if (ghostLayer != 0)
            {
                SetLayerRecursively(instance, ghostLayer);
            }

            ghostsByOrigin[origin] = instance;
        }

        /// <summary>
        /// Hides and destroys the ghost at the given origin tile, if any.
        /// </summary>
        public void HideGhostAt(Vector2Int origin)
        {
            if (!ghostsByOrigin.TryGetValue(origin, out var instance))
                return;

            if (instance != null)
            {
                Destroy(instance);
            }

            ghostsByOrigin.Remove(origin);
        }

        /// <summary>
        /// Clears all ghost instances from the scene.
        /// </summary>
        public void ClearAllGhosts()
        {
            foreach (var kvp in ghostsByOrigin)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }

            ghostsByOrigin.Clear();
        }

        void OnDestroy()
        {
            ghostsByOrigin.Clear();
        }

        void SetLayerRecursively(GameObject go, int layer)
        {
            if (!go) return;

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                if (child != null)
                    SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
