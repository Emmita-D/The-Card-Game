using UnityEngine;
using Game.Match.Cards;   // CardSO
using Game.Core;          // CardType
using Game.Match.Mana;    // ManaPool
using System.Reflection;  // reflection for flexible Spend/Notify

[DisallowMultipleComponent]
public class CardAffordability : MonoBehaviour
{
    [Header("Wiring (set by HandView at runtime)")]
    [SerializeField] ManaPool pool;

    CardView view;
    CanvasGroup cg;
    bool lastCanPlay = true;
    bool lastIsUnit = false;

    public void SetPool(ManaPool p) => pool = p;

    void Awake()
    {
        view = GetComponent<CardView>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
    }

    void OnEnable() { ApplyVisual(RecalcCanPlay(out _, out _)); }
    void Update() { ApplyVisual(RecalcCanPlay(out _, out _)); }

    public bool ComputeAffordableNow()
    {
        return RecalcCanPlay(out _, out bool canPlay) ? canPlay : true;
    }

    public void SpendCostNow()
    {
        if (!RecalcCanPlay(out CardSO so, out _)) return;
        if (so.type != CardType.Unit) return;                 // spells/traps never spend
        int cost = Mathf.Max(0, so.manaStars);
        if (pool == null || cost <= 0) return;

        // Prefer TrySpend(int) / Spend(int); otherwise no-op (no direct write to Current).
        var trySpend = pool.GetType().GetMethod("TrySpend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (trySpend != null)
        {
            bool ok = (bool)trySpend.Invoke(pool, new object[] { cost });
            if (ok) NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }
        var spend = pool.GetType().GetMethod("Spend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (spend != null)
        {
            spend.Invoke(pool, new object[] { cost });
            NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        // If your ManaPool later exposes a public setter, we’ll pick it up automatically.
        ApplyVisual(RecalcCanPlay(out _, out _));
    }

    // ---- internals ----
    bool RecalcCanPlay(out CardSO so, out bool canPlay)
    {
        so = (view != null) ? view.BoundSO : null;
        if (so == null) { canPlay = true; return false; }

        if (so.type != CardType.Unit) { canPlay = true; return true; } // spells/traps are always playable

        int cost = Mathf.Max(0, so.manaStars);
        if (pool == null) { canPlay = true; return true; }

        var canSpend = pool.GetType().GetMethod("CanSpend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (canSpend != null)
        {
            canPlay = (bool)canSpend.Invoke(pool, new object[] { cost });
            return true;
        }

        var pCurrent = pool.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        int cur = (pCurrent != null) ? (int)pCurrent.GetValue(pool) : 0;
        canPlay = cur >= cost;
        return true;
    }

    void ApplyVisual(bool _ok)
    {
        if (!RecalcCanPlay(out CardSO so, out bool canPlay)) return;

        bool isUnit = (so.type == CardType.Unit);

        // 🔒 HARD GUARD: spells/traps must always look fully affordable (no dim, no alpha).
        if (!isUnit)
        {
            if (view != null) view.SetAffordableVisual(true);
            else cg.alpha = 1f;
            lastIsUnit = false; lastCanPlay = true;
            return;
        }

        if (isUnit == lastIsUnit && canPlay == lastCanPlay) return;
        lastIsUnit = true;
        lastCanPlay = canPlay;

        if (view != null) view.SetAffordableVisual(canPlay);
        else cg.alpha = canPlay ? 1f : 0.5f;
    }

    void NotifyPoolChanged()
    {
        var notify = pool?.GetType().GetMethod("NotifyChanged", BindingFlags.Public | BindingFlags.Instance);
        if (notify != null) notify.Invoke(pool, null);
    }
}
