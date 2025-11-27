using System.Collections.Generic;
using Game.Match.Cards;
using Game.Match.State;
using UnityEngine;
using UnityEngine.UI;

public class DeckSearchVorgcoPanel : MonoBehaviour
{
    public static DeckSearchVorgcoPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject rootPanel;               // WindowRoot (visible window)
    [SerializeField] private Transform optionsParent;            // ScrollView/Viewport/Content
    [SerializeField] private DeckSearchVorgcoOption optionPrefab;
    [SerializeField] private Button confirmButton;               // Only used for multi-select modes (e.g., Savage)

    private readonly List<DeckSearchVorgcoOption> activeOptions = new List<DeckSearchVorgcoOption>();
    private readonly List<DeckSearchVorgcoOption> selectedOptions = new List<DeckSearchVorgcoOption>();

    // Multi-mode enum: we already have Vorg'co, plus a Savage mode for the new spell.
    public enum DeckSearchMode
    {
        VorgcoUnit,
        VorgcoMagic,
        SavageMagic,
        SavageUnit
    }

    [SerializeField]
    private DeckSearchMode currentMode = DeckSearchMode.VorgcoUnit;

    // Selection configuration
    [SerializeField] private bool allowMultiSelect = false;
    [SerializeField] private int maxSelections = 1;

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

        if (confirmButton != null)
        {
            confirmButton.gameObject.SetActive(false);
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirmPressed);
        }

        Debug.Log("[DeckSearchVorgcoPanel] Awake. Instance set.");
    }

    /// <summary>
    /// Configures selection behaviour (single vs multi, confirm button) based on currentMode.
    /// </summary>
    private void ConfigureForMode()
    {
        switch (currentMode)
        {
            case DeckSearchMode.VorgcoUnit:
            case DeckSearchMode.VorgcoMagic:
                allowMultiSelect = false;
                maxSelections = 1;
                if (confirmButton != null)
                    confirmButton.gameObject.SetActive(false);
                break;

            case DeckSearchMode.SavageMagic:
                allowMultiSelect = true;
                if (maxSelections <= 0)
                    maxSelections = 2; // sensible default

                if (confirmButton != null)
                    confirmButton.gameObject.SetActive(true);
                break;

            case DeckSearchMode.SavageUnit:
                allowMultiSelect = true;
                if (maxSelections <= 0)
                    maxSelections = 1; // default to 1 if not specified

                if (confirmButton != null)
                    confirmButton.gameObject.SetActive(true);
                break;
        }

        selectedOptions.Clear();
    }

    /// <summary>
    /// Opens the panel and shows all Vorg'co units or magic cards currently in this TurnController's deck,
    /// depending on the caller's flags. Behaviour is still single-pick immediate resolve.
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
            currentMode = DeckSearchMode.VorgcoMagic;
            candidates = currentTurn.GetVorgcoMagicCardsInDeck();
            Debug.Log("[DeckSearchVorgcoPanel] Searching deck for Vorg'co MAGIC cards.");
        }
        else
        {
            currentMode = DeckSearchMode.VorgcoUnit;
            candidates = currentTurn.GetVorgcoUnitsInDeck();
            Debug.Log("[DeckSearchVorgcoPanel] Searching deck for Vorg'co UNIT cards.");
        }

        // Configure selection rules based on mode
        ConfigureForMode();

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

    /// <summary>
    /// Opens the panel in SavageMagic mode to show a pre-filtered list of Savage Magic spells
    /// from the deck, allowing the player to pick up to maxPicks cards and then confirm.
    /// </summary>
    public void BeginSavageMagic(CardSO caller, int ownerId, TurnController turn, List<CardSO> candidates, int maxPicks)
    {
        if (caller == null || turn == null)
        {
            Debug.LogWarning("[DeckSearchVorgcoPanel] BeginSavageMagic called with null caller or turn.");
            return;
        }

        currentCaller = caller;
        currentOwnerId = ownerId;
        currentTurn = turn;

        ClearOptions();

        currentMode = DeckSearchMode.SavageMagic;
        maxSelections = maxPicks > 0 ? maxPicks : 2;
        ConfigureForMode();

        if (candidates == null || candidates.Count == 0)
        {
            Debug.Log("[DeckSearchVorgcoPanel] BeginSavageMagic: no Savage Magic candidates to show.");
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

    /// <summary>
    /// Opens the panel in SavageUnit mode to show a pre-filtered list of Savage unit cards
    /// from the deck, allowing the player to pick up to maxPicks cards and then confirm.
    /// </summary>
    public void BeginSavageUnit(CardSO caller, int ownerId, TurnController turn, List<CardSO> candidates, int maxPicks)
    {
        if (caller == null || turn == null)
        {
            Debug.LogWarning("[DeckSearchVorgcoPanel] BeginSavageUnit called with null caller or turn.");
            return;
        }

        currentCaller = caller;
        currentOwnerId = ownerId;
        currentTurn = turn;

        ClearOptions();

        currentMode = DeckSearchMode.SavageUnit;
        maxSelections = maxPicks > 0 ? maxPicks : 1;
        ConfigureForMode();

        if (candidates == null || candidates.Count == 0)
        {
            Debug.Log("[DeckSearchVorgcoPanel] BeginSavageUnit: no Savage unit candidates to show.");
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

    /// <summary>
    /// Called by DeckSearchVorgcoOption when the user clicks an option.
    /// Behaviour depends on allowMultiSelect:
    /// - Single-select (Vorg'co modes) — click = resolve immediately & close.
    /// - Multi-select (SavageMagic mode) — click = toggle selection, confirm button will apply later.
    /// </summary>
    public void OnOptionClicked(DeckSearchVorgcoOption option)
    {
        if (option == null || option.Card == null)
            return;

        var chosen = option.Card;

        // Single-pick behaviour (Vorg'co modes) — EXACTLY what we had before.
        if (!allowMultiSelect)
        {
            if (currentTurn == null || currentCaller == null || chosen == null)
            {
                Close();
                return;
            }

            currentTurn.ResolveOnCallSearchVorgcoUnitPick(currentCaller, currentOwnerId, chosen);
            Close();
            return;
        }

        // Multi-select behaviour (for SavageMagic mode)
        // Toggle selection
        if (selectedOptions.Contains(option))
        {
            selectedOptions.Remove(option);
            option.SetSelected(false);
        }
        else
        {
            // If we're at max, drop the oldest selection
            if (selectedOptions.Count >= maxSelections && selectedOptions.Count > 0)
            {
                var first = selectedOptions[0];
                selectedOptions.RemoveAt(0);
                if (first != null)
                    first.SetSelected(false);
            }

            selectedOptions.Add(option);
            option.SetSelected(true);
        }

        Debug.Log($"[DeckSearchVorgcoPanel] Option clicked in multi-select mode. Selected={selectedOptions.Count}/{maxSelections}");
    }

    /// <summary>
    /// Confirm button pressed (only visible/used in multi-select mode).
    /// Sends the chosen CardSOs back to the TurnController based on the current mode.
    /// </summary>
    private void OnConfirmPressed()
    {
        if (!allowMultiSelect)
        {
            // Shouldn't really be visible in this case, but just in case.
            Close();
            return;
        }

        var pickedList = new List<CardSO>();
        foreach (var opt in selectedOptions)
        {
            if (opt != null && opt.Card != null)
                pickedList.Add(opt.Card);
        }

        Debug.Log(
            $"[DeckSearchVorgcoPanel] Confirm pressed in mode={currentMode}. Selected {pickedList.Count} card(s)."
        );

        if (currentTurn != null && currentCaller != null)
        {
            switch (currentMode)
            {
                case DeckSearchMode.SavageMagic:
                    currentTurn.ResolveSavageMagicSearchPick(currentCaller, currentOwnerId, pickedList);
                    break;

                case DeckSearchMode.SavageUnit:
                    currentTurn.ResolveSavageUnitSearchPick(currentCaller, currentOwnerId, pickedList);
                    break;

                    // Other multi-select modes can be added here in future.
            }
        }

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
        selectedOptions.Clear();

        // Make sure confirm is hidden when we reopen for Vorg'co modes
        if (confirmButton != null)
            confirmButton.gameObject.SetActive(false);
    }

    private void ClearOptions()
    {
        for (int i = 0; i < activeOptions.Count; i++)
        {
            if (activeOptions[i] != null)
                Destroy(activeOptions[i].gameObject);
        }
        activeOptions.Clear();
        selectedOptions.Clear();
    }
}
