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

    /// True if playable right now (units are gated by mana, spells/traps are always true)
    public bool ComputeAffordableNow()
    {
        return RecalcCanPlay(out _, out bool canPlay) ? canPlay : true;
    }

    /// Spend cost after a successful unit placement.
    public void SpendCostNow()
    {
        if (!RecalcCanPlay(out CardSO so, out bool _)) return;
        if (so.type != CardType.Unit) return;                 // spells/traps don't spend
        int cost = Mathf.Max(0, so.manaStars);
        if (pool == null || cost <= 0) return;

        // 1) Prefer a TrySpend(int) method on ManaPool, if it exists.
        var trySpend = pool.GetType().GetMethod("TrySpend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (trySpend != null)
        {
            bool ok = (bool)trySpend.Invoke(pool, new object[] { cost });
            if (ok) NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        // 2) Or a Spend(int) method.
        var spend = pool.GetType().GetMethod("Spend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (spend != null)
        {
            spend.Invoke(pool, new object[] { cost });
            NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        // 3) Fall back to writable 'Current' property *if* it has a public setter.
        var pCurrent = pool.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        if (pCurrent != null && pCurrent.CanWrite)
        {
            int cur = (int)pCurrent.GetValue(pool);
            pCurrent.SetValue(pool, Mathf.Max(0, cur - cost));
            NotifyPoolChanged();
        }

        ApplyVisual(RecalcCanPlay(out _, out _));
    }

    // ---- internals ----
    bool RecalcCanPlay(out CardSO so, out bool canPlay)
    {
        so = (view != null) ? view.BoundSO : null;
        if (so == null) { canPlay = true; return false; }

        if (so.type != CardType.Unit) { canPlay = true; return true; } // spells/traps free

        int cost = Mathf.Max(0, so.manaStars);
        if (pool == null) { canPlay = true; return true; }

        // If ManaPool exposes CanSpend(int), use it; else compare to Current.
        var canSpend = pool.GetType().GetMethod("CanSpend", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (canSpend != null)
        {
            canPlay = (bool)canSpend.Invoke(pool, new object[] { cost });
            return true;
        }

        // Fallback: read Current (getter must be public, which it already is for you)
        var pCurrent = pool.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        int cur = (pCurrent != null) ? (int)pCurrent.GetValue(pool) : 0;
        canPlay = cur >= cost;
        return true;
    }

    void ApplyVisual(bool _ok)
    {
        RecalcCanPlay(out CardSO so, out bool canPlay);

        bool isUnit = (so != null && so.type == CardType.Unit);
        if (isUnit == lastIsUnit && canPlay == lastCanPlay) return;
        lastIsUnit = isUnit;
        lastCanPlay = canPlay;

        if (view != null) view.SetAffordableVisual(canPlay);
        else cg.alpha = canPlay ? 1f : 0.5f;
    }

    void NotifyPoolChanged()
    {
        // Call ManaPool.NotifyChanged() if you added one; otherwise HUD will refresh on its next update
        var notify = pool.GetType().GetMethod("NotifyChanged", BindingFlags.Public | BindingFlags.Instance);
        if (notify != null) notify.Invoke(pool, null);
    }
}
