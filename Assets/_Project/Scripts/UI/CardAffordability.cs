using UnityEngine;
using Game.Match.Cards;   // CardSO
using Game.Core;          // CardType
using Game.Match.Mana;    // ManaPool
using Game.Match.State;   // TurnController
using System.Reflection;  // reflection for flexible Spend/Notify
using System.Collections.Generic; // for cost-change logging cache

[DisallowMultipleComponent]
public class CardAffordability : MonoBehaviour
{
    [Header("Wiring (set by HandView at runtime)")]
    [SerializeField] ManaPool pool;

    [Header("Debug")]
    [SerializeField] private bool logEffectiveCosts = false;

    CardView view;
    CanvasGroup cg;
    bool lastCanPlay = true;
    bool lastIsUnit = false;

    /// <summary>
    /// Cached TurnController for querying once-per-turn Savage flags.
    /// </summary>
    TurnController turn;

    /// <summary>
    /// Cache of last effective cost logged per CardSO to avoid log spam.
    /// </summary>
    readonly Dictionary<CardSO, int> _lastLoggedEffectiveCost =
        new Dictionary<CardSO, int>();

    public void SetPool(ManaPool p) => pool = p;

    void Awake()
    {
        view = GetComponent<CardView>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
    }

    void OnEnable() { ApplyVisual(RecalcCanPlay(out _, out _)); }
    void Update() { ApplyVisual(RecalcCanPlay(out _, out _)); }

    /// <summary>
    /// Computes the effective mana cost for a unit in hand, including special
    /// Savage turn-based discount rules. Non-unit cards simply return 0 here
    /// and are treated as always playable elsewhere.
    /// </summary>
    int ComputeEffectiveUnitCost(CardSO so)
    {
        if (so == null) return 0;

        // Only units use mana stars; spells/traps are handled as always-playable.
        if (so.type != CardType.Unit)
            return 0;

        int baseCost = Mathf.Max(0, so.manaStars);
        int cost = baseCost;

        bool usedSavageRule = false;
        bool hasSavageThisTurn = false;

        // Apply "costs 0 if a Savage unit was called this turn" only when flagged.
        if (so.costsZeroIfSavageUnitCalledThisTurn)
        {
            if (turn == null)
                turn = Object.FindObjectOfType<TurnController>();

            if (turn != null)
            {
                hasSavageThisTurn = turn.HasCalledSavageUnitThisTurn();
            }
            else
            {
                Debug.LogWarning("[CardAffordability] No TurnController found when checking Savage turn-based cost.");
            }

            if (hasSavageThisTurn)
            {
                cost = 0;
            }

            usedSavageRule = true;
        }

        // Optional debug: only when this special rule is in play, and only when
        // the effective cost actually changes (to avoid per-frame spam).
        if (logEffectiveCosts && usedSavageRule)
        {
            int last;
            bool hasLast = _lastLoggedEffectiveCost.TryGetValue(so, out last);

            if (!hasLast || last != cost)
            {
                _lastLoggedEffectiveCost[so] = cost;

                Debug.Log(
                    $"[CardAffordability] Effective cost for {so.cardName} " +
                    $"(SavageCostFlag={so.costsZeroIfSavageUnitCalledThisTurn}, " +
                    $"hasSavageThisTurn={hasSavageThisTurn}) => {cost} (base={baseCost})."
                );
            }
        }

        return cost;
    }

    public bool ComputeAffordableNow()
    {
        return RecalcCanPlay(out _, out bool canPlay) ? canPlay : true;
    }

    public void SpendCostNow()
    {
        if (!RecalcCanPlay(out CardSO so, out _)) return;
        if (so == null) return;

        // Only units spend mana through this component; spells/traps are handled elsewhere.
        if (so.type != CardType.Unit) return;

        int cost = ComputeEffectiveUnitCost(so);

        // If effective cost is 0 or no pool is wired, don't try to spend,
        // but still refresh visuals so other cards update correctly.
        if (pool == null || cost <= 0)
        {
            Debug.Log($"[CardAffordability] SpendCostNow for {so.cardName}: effective cost {cost} (no mana spent).");
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        var poolType = pool.GetType();

        // Prefer TrySpend(int) if available.
        var trySpend = poolType.GetMethod(
            "TrySpend",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(int) },
            null
        );

        if (trySpend != null)
        {
            bool ok = (bool)trySpend.Invoke(pool, new object[] { cost });
            Debug.Log($"[CardAffordability] SpendCostNow for {so.cardName}: TrySpend({cost}) -> {ok}.");
            if (ok) NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        // Fallback: Spend(int) if present.
        var spend = poolType.GetMethod(
            "Spend",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(int) },
            null
        );

        if (spend != null)
        {
            spend.Invoke(pool, new object[] { cost });
            Debug.Log($"[CardAffordability] SpendCostNow for {so.cardName}: Spend({cost}).");
            NotifyPoolChanged();
            ApplyVisual(RecalcCanPlay(out _, out _));
            return;
        }

        // Last fallback: attempt to write a 'Current' property directly.
        var currentProp = poolType.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        if (currentProp != null)
        {
            int current = (int)currentProp.GetValue(pool);
            int newValue = Mathf.Max(0, current - cost);
            currentProp.SetValue(pool, newValue);
            Debug.Log($"[CardAffordability] SpendCostNow for {so.cardName}: Current {current} -> {newValue}.");
            NotifyPoolChanged();
        }

        ApplyVisual(RecalcCanPlay(out _, out _));
    }

    // ---- internals ----
    bool RecalcCanPlay(out CardSO so, out bool canPlay)
    {
        so = (view != null) ? view.BoundSO : null;
        if (so == null)
        {
            canPlay = true;
            return false; // no card bound; nothing to do
        }

        // Spells and traps are always considered playable from the point of view
        // of this component; their special effects are handled elsewhere.
        if (so.type != CardType.Unit)
        {
            canPlay = true;
            return true;
        }

        // Units: compute the effective cost (includes Savage discount if flagged).
        int cost = ComputeEffectiveUnitCost(so);

        if (view != null)
        {
            view.SetDisplayedCost(cost);
        }

        // If we have no pool wired, treat as always playable so UI does not dim.
        if (pool == null)
        {
            canPlay = true;
            return true;
        }

        var poolType = pool.GetType();

        // Prefer a CanSpend(int) method if present.
        var canSpend = poolType.GetMethod(
            "CanSpend",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(int) },
            null
        );

        if (canSpend != null)
        {
            canPlay = (bool)canSpend.Invoke(pool, new object[] { cost });
            return true;
        }

        // Fallback: check a 'Current' property if it exists.
        var currentProp = poolType.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        if (currentProp != null)
        {
            int current = (int)currentProp.GetValue(pool);
            canPlay = current >= cost;
            return true;
        }

        // If we can't inspect the pool, default to playable.
        canPlay = true;
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
