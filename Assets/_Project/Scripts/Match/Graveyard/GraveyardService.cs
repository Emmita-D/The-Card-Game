using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Game.Match.Cards;

namespace Game.Match.Graveyard
{
    public enum GraveyardRealm { Empyrean = 0, Infernum = 1 }

    /// <summary>
    /// Per-player, per-realm graveyards. Newest entries are appended (UI can show newest-first).
    /// Clears on new match.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GraveyardService : MonoBehaviour
    {
        // -------- Singleton with teardown guard --------
        private static GraveyardService _instance;
        private static bool _shuttingDown; // do NOT spawn during quit/destroy

        /// <summary>
        /// Non-creating accessor. Returns null during teardown or if none exists.
        /// Use this in OnDisable/OnDestroy paths to avoid waking the singleton.
        /// </summary>

        // Reset static fields when the player/editor loads subsystems (works even with
        // "Enter Play Mode Options" and domain reload disabled).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void __ResetStaticsOnLoad()
        {
            _shuttingDown = false;
            _instance = null;
        }
        public static GraveyardService TryGet()
        {
            if (_shuttingDown) return null;
            if (_instance != null) return _instance;
            return FindObjectOfType<GraveyardService>();
        }

        public static GraveyardService Instance
        {
            get
            {
                // Never create while quitting/destroying
                if (_shuttingDown) return _instance;

                if (_instance != null) return _instance;

                _instance = FindObjectOfType<GraveyardService>();
                if (_instance == null && !_shuttingDown)
                {
                    var go = new GameObject(nameof(GraveyardService));
                    _instance = go.AddComponent<GraveyardService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void Awake()
        {
            _shuttingDown = false;
            if (_instance != null && _instance != this)
            {
                // Duplicate — destroy safely
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnApplicationQuit() { _shuttingDown = true; }
        void OnDestroy()
        {
            _shuttingDown = true;
            if (_instance == this) _instance = null;
        }

        // -------- Store & events --------

        // key: (ownerId, realm)
        private readonly Dictionary<(int ownerId, GraveyardRealm realm), List<CardSO>> _store
            = new Dictionary<(int ownerId, GraveyardRealm realm), List<CardSO>>();

        /// <summary>Raised whenever a player's graveyards change. Passes ownerId.</summary>
        public event Action<int> OnChanged;

        // ---------- Public API ----------

        public void ClearAll()
        {
            _store.Clear();
            OnChanged?.Invoke(-1);
        }

        public void ClearOwner(int ownerId)
        {
            var keys = new List<(int, GraveyardRealm)>();
            foreach (var k in _store.Keys)
                if (k.ownerId == ownerId) keys.Add(k);
            foreach (var k in keys) _store.Remove(k);
            OnChanged?.Invoke(ownerId);
        }

        public void Add(int ownerId, CardSO card)
        {
            if (card == null) return;
            var realm = RealmOf(card);
            Add(ownerId, card, realm);
        }

        public void Add(int ownerId, CardSO card, GraveyardRealm realm)
        {
            if (card == null) return;
            var key = (ownerId, realm);
            if (!_store.TryGetValue(key, out var list))
            {
                list = new List<CardSO>(8);
                _store[key] = list;
            }
            list.Add(card); // newest at end
            OnChanged?.Invoke(ownerId);
        }

        public IReadOnlyList<CardSO> Get(int ownerId, GraveyardRealm realm)
        {
            var key = (ownerId, realm);
            if (_store.TryGetValue(key, out var list)) return list;
            return Array.Empty<CardSO>();
        }

        public int Count(int ownerId, GraveyardRealm realm)
        {
            var key = (ownerId, realm);
            if (_store.TryGetValue(key, out var list)) return list.Count;
            return 0;
        }

        // ---------- Realm mapping ----------
        // Uses your existing CardSO realm field (the one that changes borders).
        public static GraveyardRealm RealmOf(CardSO so)
        {
            if (so == null) return GraveyardRealm.Empyrean;

            // Try to find a 'realm' / 'faction' / 'affinity' field or property and map enum/string
            var t = so.GetType();

            object val = TryMember(t, so, "realm") ??
                         TryMember(t, so, "faction") ??
                         TryMember(t, so, "affinity") ??
                         TryMember(t, so, "side");

            if (val != null)
            {
                var name = val.ToString();
                if (string.Equals(name, "Infernum", StringComparison.OrdinalIgnoreCase))
                    return GraveyardRealm.Infernum;
                if (string.Equals(name, "Empyrean", StringComparison.OrdinalIgnoreCase))
                    return GraveyardRealm.Empyrean;

                if (int.TryParse(name, out int i))
                    return (i == 1) ? GraveyardRealm.Infernum : GraveyardRealm.Empyrean;
            }

            // Default safe side
            return GraveyardRealm.Empyrean;
        }

        private static object TryMember(Type t, object inst, string name)
        {
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var f = t.GetField(name, BF);
            if (f != null) return f.GetValue(inst);
            var p = t.GetProperty(name, BF);
            if (p != null && p.CanRead) return p.GetValue(inst, null);
            return null;
        }
    }
}
