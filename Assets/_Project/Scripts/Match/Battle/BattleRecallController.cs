using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Match.State;
using Game.Match.Units;

namespace Game.Match.Battle
{
    /// <summary>
    /// Isolated controller that:
    /// - Starts a 2s "evacuate" recall window for a given ownerId.
    /// - During evacuate, recalled units cannot attack (we bump their nextAttackTime).
    /// - After evacuate, surviving recalled units are despawned and recorded as survivors.
    /// - Watches alive-unit counts and raises ReturnToCardPhase when both sides are empty.
    /// </summary>
    public class BattleRecallController : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("Duration of the evacuate window (seconds).")]
        public float evacuateSeconds = 2f;

        [Header("Refs (wired at runtime)")]
        [SerializeField] private CombatResolver resolver;
        [SerializeField] private int localOwnerId = 0;

        // --- Events for UI / scene orchestrators ---
        public System.Action<int> OnRecallStarted;
        public System.Action<int> OnRecallCompleted;
        public System.Action OnAllSidesEmpty;

        // Internal: track a set of units being recalled for each owner
        private readonly HashSet<UnitAgent> _recalling = new();

        public void Initialize(CombatResolver combatResolver, int localOwner)
        {
            resolver = combatResolver;
            localOwnerId = localOwner;

            if (resolver != null)
            {
                resolver.UnitRegistered += HandleUnitRegistered;
                resolver.UnitDied += HandleUnitDied;
            }
        }

        private void OnDestroy()
        {
            if (resolver != null)
            {
                resolver.UnitRegistered -= HandleUnitRegistered;
                resolver.UnitDied -= HandleUnitDied;
            }
        }

        // --- Public API ---
        public void RequestRecall(int ownerId)
        {
            if (resolver == null) return;

            var targets = GetAliveUnits(ownerId);
            if (targets.Count == 0) return;

            // Mark + silence attacks during evacuate
            foreach (var u in targets)
            {
                if (u == null) continue;
                _recalling.Add(u);

                // Prevent attacking during evacuate (no heavy combat refactor)
                u.nextAttackTime = Time.time + evacuateSeconds + 0.05f;

                // Optionally stop movement while recalling (keeps things readable)
                u.movementLocked = true;
            }

            Debug.Log($"[Return] Player {ownerId} recalled {targets.Count} units (evac={evacuateSeconds:0.0}s)");
            OnRecallStarted?.Invoke(ownerId);
            StartCoroutine(FinishRecallAfter(ownerId, evacuateSeconds));
        }

        public bool HasAliveUnits(int ownerId) => CountAlive(ownerId) > 0;

        // --- Internals ---
        IEnumerator FinishRecallAfter(int ownerId, float delay)
        {
            yield return new WaitForSeconds(delay);

            var survivorsThisOwner = new List<UnitAgent>();
            foreach (var u in GetAliveUnits(ownerId))
            {
                if (u != null && _recalling.Contains(u))
                    survivorsThisOwner.Add(u);
            }

            // Snapshot survivors
            if (survivorsThisOwner.Count > 0)
            {
                var reg = MatchRuntimeService.Instance?.survivors;
                reg?.RecordSurvivors(ownerId, survivorsThisOwner);
            }

            // Despawn (not deaths)
            foreach (var u in survivorsThisOwner)
            {
                _recalling.Remove(u);
                if (u != null) Destroy(u.gameObject);
            }

            OnRecallCompleted?.Invoke(ownerId);

            // --- NEW: let Unity finalize destruction this frame ---
            yield return null;

            // Re-check after objects are actually gone
            TryAnnounceAllSidesEmpty();
        }
        void HandleUnitRegistered(UnitAgent _)
        {
            // If anything spawns (unlikely during battle), just check emptiness rules.
            TryAnnounceAllSidesEmpty();
        }

        void HandleUnitDied(UnitAgent dead)
        {
            _recalling.Remove(dead);
            TryAnnounceAllSidesEmpty();
        }

        void TryAnnounceAllSidesEmpty()
        {
            int a = CountAlive(0);
            int b = CountAlive(1);

            // Debug aid
            Debug.Log($"[Return] Alive counts after recall: A={a}, B={b}");

            if (a == 0 && b == 0)
            {
                Debug.Log("[Return] All sides empty → firing OnAllSidesEmpty");
                OnAllSidesEmpty?.Invoke();
            }
        }
        int CountAlive(int ownerId)
        {
            if (resolver == null) return 0;
            var list = (ownerId == 0) ? resolver.LocalUnits : resolver.RemoteUnits;
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null) continue;
                var rt = u.GetComponent<UnitRuntime>();
                if (rt != null && rt.health > 0) count++;
            }
            return count;
        }

        List<UnitAgent> GetAliveUnits(int ownerId)
        {
            var res = new List<UnitAgent>();
            if (resolver == null) return res;
            var list = (ownerId == 0) ? resolver.LocalUnits : resolver.RemoteUnits;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null) continue;
                var rt = u.GetComponent<UnitRuntime>();
                if (rt != null && rt.health > 0) res.Add(u);
            }
            return res;
        }
    }
}
