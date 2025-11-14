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
    /// - During evacuate, recalled units cannot attack (we bump their nextAttackTime) and movement is locked.
    /// - After evacuate, surviving recalled units are despawned and recorded as survivors.
    /// - Watches alive-unit counts and raises OnAllSidesEmpty when both sides are empty.
    /// - Can be safely re-bound every battle to avoid stale subscriptions.
    /// </summary>
    public class BattleRecallController : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("Duration of the evacuate window (seconds).")]
        public float evacuateSeconds = 2f;

        [Header("Runtime-wired refs")]
        [SerializeField] private CombatResolver resolver;
        [SerializeField] private int localOwnerId = 0;

        // --- Events for UI / scene orchestrators ---
        public System.Action<int> OnRecallStarted;   // param: ownerId
        public System.Action<int> OnRecallCompleted; // param: ownerId
        public System.Action OnAllSidesEmpty;

        // Track units being recalled so we only despawn those
        private readonly HashSet<UnitAgent> _recalling = new();

        // Keep who we're currently listening to, so we can unhook on rebind
        private CombatResolver _boundResolver;

        // --------------------------------------------------------------------
        // Lifecycle / binding
        // --------------------------------------------------------------------
        public void Initialize(CombatResolver combatResolver, int localOwner)
        {
            localOwnerId = localOwner;
            Rebind(combatResolver);
        }

        /// <summary>
        /// Re-subscribe to resolver events every time a new battle starts.
        /// Safe to call multiple times. Also clears transient recall state.
        /// </summary>
        public void Rebind(CombatResolver newResolver)
        {
            if (_boundResolver == newResolver && resolver == newResolver)
                return;

            // Unhook old
            if (_boundResolver != null)
            {
                _boundResolver.UnitRegistered -= HandleUnitRegistered;
                _boundResolver.UnitDied -= HandleUnitDied;
            }

            _boundResolver = newResolver;
            resolver = newResolver;

            // Hook new
            if (_boundResolver != null)
            {
                _boundResolver.UnitRegistered += HandleUnitRegistered;
                _boundResolver.UnitDied += HandleUnitDied;
            }

            // Clear any lingering per-battle state
            _recalling.Clear();
        }

        private void OnDestroy()
        {
            if (_boundResolver != null)
            {
                _boundResolver.UnitRegistered -= HandleUnitRegistered;
                _boundResolver.UnitDied -= HandleUnitDied;
            }
        }

        // --------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------
        public bool HasAliveUnits(int ownerId) => CountAlive(ownerId) > 0;

        public void RequestRecall(int ownerId)
        {
            if (resolver == null) return;
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[Return] Recall requested but BattleRecallController object is inactive; ignoring.");
                return;
            }

            var targets = GetAliveUnits(ownerId);
            if (targets.Count == 0) return;

            // Mark + silence attacks during evacuate
            foreach (var u in targets)
            {
                if (u == null) continue;

                _recalling.Add(u);

                // Prevent attacking during evacuate (no heavy combat refactor)
                u.nextAttackTime = Time.time + evacuateSeconds + 0.05f;

                // Keep them in place while evacuating – helps readability
                u.movementLocked = true;
            }

            Debug.Log($"[Return] Player {ownerId} recalled {targets.Count} units (evac={evacuateSeconds:0.0}s)");
            OnRecallStarted?.Invoke(ownerId);

            StartCoroutine(FinishRecallAfter(ownerId, evacuateSeconds));
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------
        private IEnumerator FinishRecallAfter(int ownerId, float delay)
        {
            yield return new WaitForSeconds(delay);

            // Survivors = those still alive AND flagged for recall
            var survivorsThisOwner = new List<UnitAgent>();
            foreach (var u in GetAliveUnits(ownerId))
            {
                if (u != null && _recalling.Contains(u))
                    survivorsThisOwner.Add(u);
            }

            // Record survivors back to card phase (if any)
            if (survivorsThisOwner.Count > 0)
            {
                var reg = MatchRuntimeService.Instance?.survivors;
                reg?.RecordSurvivors(ownerId, survivorsThisOwner);
            }

            // Despawn recalled survivors (not deaths)
            foreach (var u in survivorsThisOwner)
            {
                _recalling.Remove(u);
                if (u != null) Destroy(u.gameObject);
            }

            OnRecallCompleted?.Invoke(ownerId);

            // Let Unity process destructions before we check emptiness
            yield return null;

            TryAnnounceAllSidesEmpty();
        }

        private void HandleUnitRegistered(UnitAgent _)
        {
            // If something spawns mid-battle (unlikely), keep emptiness rules honest
            TryAnnounceAllSidesEmpty();
        }

        private void HandleUnitDied(UnitAgent dead)
        {
            _recalling.Remove(dead);
            TryAnnounceAllSidesEmpty();
        }

        private void TryAnnounceAllSidesEmpty()
        {
            int a = CountAlive(0);
            int b = CountAlive(1);

            Debug.Log($"[Return] Alive counts after recall: A={a}, B={b}");

            if (a == 0 && b == 0)
            {
                Debug.Log("[Return] All sides empty → firing OnAllSidesEmpty");
                OnAllSidesEmpty?.Invoke();
            }
        }

        private int CountAlive(int ownerId)
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

        private List<UnitAgent> GetAliveUnits(int ownerId)
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
