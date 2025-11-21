using System.Collections.Generic;
using Game.Match.Cards;
using Game.Match.State;
using UnityEngine;

public class DeckSearchVorgcoPanel : MonoBehaviour
{
    public static DeckSearchVorgcoPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject rootPanel;               // WindowRoot (visible window)
    [SerializeField] private Transform optionsParent;            // ScrollView/Viewport/Content
    [SerializeField] private DeckSearchVorgcoOption optionPrefab;

    private readonly List<DeckSearchVorgcoOption> activeOptions = new List<DeckSearchVorgcoOption>();

    private TurnController currentTurn;
    private CardSO currentCaller;
    private int currentOwnerId;

    public bool IsActive => rootPanel != null && rootPanel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (rootPanel != null)
            rootPanel.SetActive(false); // panel hidden by default

        Debug.Log("[DeckSearchVorgcoPanel] Awake. Instance set.");
    }

    /// <summary>
    /// Opens the panel and shows all Vorg'co units currently in this TurnController's deck.
    /// </summary>
    public void Begin(CardSO caller, int ownerId, TurnController turn)
    {
        if (caller == null || turn == null)
        {
            Debug.LogWarning("[DeckSearchVorgcoPanel] Begin called with null caller or turn.");
            return;
        }

        currentCaller = caller;
        currentOwnerId = ownerId;
        currentTurn = turn;

        ClearOptions();

        List<CardSO> candidates;

        // Decide what to search for based on the caller's flags
        if (currentCaller != null && currentCaller.onCallSearchVorgcoMagic)
        {
            candidates = currentTurn.GetVorgcoMagicCardsInDeck();
            Debug.Log("[DeckSearchVorgcoPanel] Searching deck for Vorg'co MAGIC cards.");
        }
        else
        {
            candidates = currentTurn.GetVorgcoUnitsInDeck();
            Debug.Log("[DeckSearchVorgcoPanel] Searching deck for Vorg'co UNIT cards.");
        }

        if (candidates == null || candidates.Count == 0)
        {
            Debug.Log("[DeckSearchVorgcoPanel] No matching Vorg'co cards in deck to show.");
            Close();
            return;
        }
        if (rootPanel != null)
            rootPanel.SetActive(true);

        foreach (var so in candidates)
        {
            if (so == null) continue;

            var opt = Instantiate(optionPrefab, optionsParent);
            opt.Initialize(so, this);
            activeOptions.Add(opt);
        }
    }

    public void OnOptionChosen(CardSO chosen)
    {
        if (currentTurn == null || currentCaller == null || chosen == null)
        {
            Close();
            return;
        }

        currentTurn.ResolveOnCallSearchVorgcoUnitPick(currentCaller, currentOwnerId, chosen);
        Close();
    }

    public void Close()
    {
        ClearOptions();

        if (rootPanel != null)
            rootPanel.SetActive(false);

        currentCaller = null;
        currentTurn = null;
        currentOwnerId = 0;
    }

    private void ClearOptions()
    {
        for (int i = 0; i < activeOptions.Count; i++)
        {
            if (activeOptions[i] != null)
                Destroy(activeOptions[i].gameObject);
        }
        activeOptions.Clear();
    }
}
