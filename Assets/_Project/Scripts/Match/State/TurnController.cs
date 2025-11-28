using Game.Core;
using Game.Match.Battle;     // CardPhaseBattleLauncher
using Game.Match.CardPhase;
using Game.Match.Cards;
using Game.Match.Graveyard;
using Game.Match.Grid;
using Game.Match.Log;
using Game.Match.Mana;        // ManaPool (Slots/Current/SetSlots/SetCurrent)
using Game.Match.Status;
using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Game.Match.State
{
    public class TurnController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private ManaPool mana;             // drag ManaRoot -> ManaPool
        [SerializeField] private HandView handView;         // drag HandPanel -> HandView
        [SerializeField] private TurnTimerHUD timer;        // drag TurnTimerHUD here
        [SerializeField] private CardPhaseBattleLauncher battleLauncher; // drag CardPhaseBattleLauncher here
        [SerializeField] private bool autoStart = true;
        [SerializeField] private Game.Match.Mana.ManaPool[] manaPools;
        [SerializeField] private CardPhaseTargetSelectionController cardPhaseTargetSelectionController; // drag CardPhaseTargetSelectionController here

        [Header("Deck & Hand")]
        [SerializeField] private List<CardSO> deckList = new();
        [SerializeField] private int startingHand = 5;
        [SerializeField] private int handMax = 10;
        [SerializeField] private int ownerId = 0;
        [SerializeField] private int shuffleSeed = 12345;

        [Header("Turn Behavior")]
        [SerializeField] private bool bumpMaxOnStartTurn = true;   // ON
        [SerializeField] private bool refillOnStartTurn = true;    // ON
        [Tooltip("Cards drawn at the START of each CardPhase turn (Hearthstone-style = 1).")]
        [SerializeField] private int drawOnStartTurn = 1;          // 🔁 now main draw point
        [Tooltip("Cards drawn at END of turn (set to 0 for HS-style turns).")]
        [SerializeField] private int drawOnEndTurn = 0;            // 🔁 default 0 now
        [Tooltip("If true, mana bump/refill also happens on Turn 1.")]
        [SerializeField] private bool bumpOnFirstTurn = true;

        [Header("Caps")]
        [SerializeField] private int slotsCap = 10; // mana cap (max crystals)

        // runtime
        private readonly Queue<CardSO> deck = new();
        private readonly List<CardInstance> hand = new();
        private int turnIndex = 0;

        // Once-per-turn flags (per controller/owner)
        private bool hasUsedSavageReturnSearchThisTurn = false;

        /// <summary>
        /// True if this controller's owner has called at least one Savage unit
        /// (isSavageArchetype = true) on the CardPhase grid during the current turn.
        /// </summary>
        private bool hasCalledSavageUnitThisTurn = false;

        // Events
        public event Action<CardInstance> OnCardDiscarded;
        public event Action<int> OnTurnStarted;
        public event Action<int> OnTurnEnded;
        /// <summary>Raised when we attempt to draw but the deck is empty. Hook match-end here.</summary>
        public event Action OnDeckDepleted;

        public int TurnIndex => turnIndex;
        public int DeckCount => deck.Count;
        public int HandCount => hand.Count;

        /// <summary>
        /// Returns true if this controller's owner can use the
        /// "Return 3 units → search up to 2 Savage Magic" spell this turn.
        /// </summary>
        public bool CanUseSavageReturnSearch(int ownerIdForEffect)
        {
            // This TurnController instance manages a single owner (ownerId).
            if (ownerIdForEffect != ownerId)
                return false;

            return !hasUsedSavageReturnSearchThisTurn;
        }

        /// <summary>
        /// Marks the special Savage return/search spell as used for this turn.
        /// </summary>
        public void MarkSavageReturnSearchUsed(int ownerIdForEffect)
        {
            if (ownerIdForEffect != ownerId)
                return;

            hasUsedSavageReturnSearchThisTurn = true;
        }

        /// <summary>
        /// Returns true if this controller's owner has called at least one
        /// Savage unit (isSavageArchetype = true) this turn.
        /// </summary>
        public bool HasCalledSavageUnitThisTurn(int ownerIdForEffect)
        {
            // This TurnController instance manages a single owner (ownerId).
            if (ownerIdForEffect != ownerId)
                return false;

            return hasCalledSavageUnitThisTurn;
        }

        /// <summary>
        /// Convenience wrapper: returns the flag for THIS controller's owner,
        /// without needing to pass an ownerId.
        /// </summary>
        public bool HasCalledSavageUnitThisTurn()
        {
            return hasCalledSavageUnitThisTurn;
        }

        // --- Token-cost v1 state (spend stacks from a chosen friendly unit) ---

        /// <summary>
        /// True while we are waiting for the player to choose a unit to pay a token cost.
        /// Prevents overlapping requests.
        /// </summary>
        private bool isResolvingTokenCost;

        /// <summary>
        /// How many stacks this pending token-cost request is trying to spend from the chosen unit.
        /// For our first use-case this will typically be 2 (use 2 Savage tokens).
        /// </summary>
        private int pendingTokenCostStacks;
        private FieldTokenCostKind pendingTokenCostKind = FieldTokenCostKind.None;

        // --- On-call: Extra Summons (unit-only, v1) ---

        /// <summary>
        /// True while we are processing an extra-summon request triggered by an On-call effect.
        /// Prevents overlapping requests while we later plug in tile selection callbacks.
        /// </summary>
        private bool isResolvingOnCallExtraSummons;

        /// <summary>
        /// The unit card instance whose On-call effect requested extra summons.
        /// </summary>
        private CardInstance pendingOnCallSummonSource;

        /// <summary>
        /// How many extra units we intend to summon for the current pending request.
        /// </summary>
        private int pendingOnCallExtraSummonCount;

        /// <summary>
        /// The CardSO used for the extra units to summon (typically a token-like unit).
        /// </summary>
        private CardSO pendingOnCallExtraSummonUnit;

        /// <summary>
        /// Notifies this TurnController that a unit has been Called on the
        /// CardPhase grid. Used to track once-per-turn Savage-unit conditions.
        /// </summary>
        public void OnUnitCalledFromCardPhase(CardSO unitData, int ownerIdForEffect)
        {
            if (unitData == null)
                return;

            if (ownerIdForEffect != ownerId)
                return;

            bool isSavage = unitData.isSavageArchetype;

            string unitName = string.IsNullOrEmpty(unitData.cardName)
                ? unitData.name
                : unitData.cardName;

            if (isSavage)
            {
                hasCalledSavageUnitThisTurn = true;
                Debug.Log(
                    $"[Turn] OnUnitCalledFromCardPhase: owner={ownerId} summoned Savage unit {unitName}; " +
                    "hasCalledSavageUnitThisTurn set to TRUE."
                );
            }
            else
            {
                Debug.Log(
                    $"[Turn] OnUnitCalledFromCardPhase: owner={ownerId} summoned non-Savage unit {unitName}; " +
                    "flag unchanged."
                );
            }
        }

        /// <summary>
        /// Entry point for On-call extra summons.
        /// Called by the UI (DraggableCard) after a unit has been successfully placed
        /// on the CardPhase board, when its CardSO has onCallSummonExtraUnits enabled.
        ///
        /// Step 2: this only validates config and logs. Step 3 will plug in tile selection
        /// and actual extra unit spawning.
        /// </summary>
        public void RequestOnCallExtraSummons(CardInstance caller)
        {
            if (caller == null || caller.data == null)
            {
                Debug.LogWarning("[OnCallSummon] RequestOnCallExtraSummons called with null caller or data.");
                return;
            }

            var so = caller.data;

            // Quick config validation: feature disabled or incomplete?
            if (!so.onCallSummonExtraUnits
                || so.onCallExtraSummonCount <= 0
                || so.onCallExtraSummonUnit == null)
            {
                Debug.Log(
                    $"[OnCallSummon] RequestOnCallExtraSummons for {so.cardName} ignored – config disabled or incomplete."
                );
                return;
            }

            if (isResolvingOnCallExtraSummons)
            {
                Debug.LogWarning(
                    $"[OnCallSummon] Already resolving an extra-summon request; " +
                    $"ignoring new one for {so.cardName}."
                );
                return;
            }

            isResolvingOnCallExtraSummons = true;

            pendingOnCallSummonSource = caller;
            pendingOnCallExtraSummonCount = so.onCallExtraSummonCount;
            pendingOnCallExtraSummonUnit = so.onCallExtraSummonUnit;

            string callerName = string.IsNullOrEmpty(so.cardName) ? so.name : so.cardName;
            string extraName = (pendingOnCallExtraSummonUnit != null)
                ? (string.IsNullOrEmpty(pendingOnCallExtraSummonUnit.cardName)
                    ? pendingOnCallExtraSummonUnit.name
                    : pendingOnCallExtraSummonUnit.cardName)
                : "<null>";

            Debug.Log(
                $"[OnCallSummon] Extra-summon request from {callerName} (owner={caller.ownerId}) → " +
                $"count={pendingOnCallExtraSummonCount}, unit={extraName}. " +
                "Starting tile selection..."
            );

            var tiles = Game.Match.CardPhase.CardPhaseTileSelectionController.Instance;
            if (tiles == null)
            {
                Debug.LogError("[OnCallSummon] CardPhaseTileSelectionController.Instance is null. Cannot select tiles.");
                isResolvingOnCallExtraSummons = false;
                return;
            }

            // Determine footprint from the extra unit CardSO.
            int w = Mathf.Clamp(pendingOnCallExtraSummonUnit.sizeW, 1, 4);
            int h = Mathf.Clamp(pendingOnCallExtraSummonUnit.sizeH, 1, 4);

            // Begin selection: pick exactly N tiles where a w×h rect can fit.
            tiles.Begin(
                caller.ownerId,
                pendingOnCallExtraSummonCount,
                OnOnCallTilesChosen,
                w,
                h
            );
        }
        private void OnOnCallTilesChosen(List<Vector2Int> tiles)
        {
            if (tiles == null || tiles.Count == 0)
            {
                Debug.LogWarning("[OnCallSummon] Tile selection returned no tiles; cancelling extra summons.");
                isResolvingOnCallExtraSummons = false;
                pendingOnCallSummonSource = null;
                pendingOnCallExtraSummonCount = 0;
                pendingOnCallExtraSummonUnit = null;
                return;
            }

            if (pendingOnCallSummonSource == null || pendingOnCallExtraSummonUnit == null)
            {
                Debug.LogWarning("[OnCallSummon] Tiles chosen but pending On-call summon data is missing; aborting.");
                isResolvingOnCallExtraSummons = false;
                pendingOnCallSummonSource = null;
                pendingOnCallExtraSummonCount = 0;
                pendingOnCallExtraSummonUnit = null;
                return;
            }

            var callerSo = pendingOnCallSummonSource.data;
            string callerName = callerSo != null && !string.IsNullOrEmpty(callerSo.cardName)
                ? callerSo.cardName
                : (callerSo != null ? callerSo.name : "<null>");

            string extraName = !string.IsNullOrEmpty(pendingOnCallExtraSummonUnit.cardName)
                ? pendingOnCallExtraSummonUnit.cardName
                : pendingOnCallExtraSummonUnit.name;

            Debug.Log(
                $"[OnCallSummon] Tile selection complete for caller {callerName} (owner={pendingOnCallSummonSource.ownerId}) " +
                $"→ {tiles.Count} tile(s): {string.Join(", ", tiles)}. Spawning extra units..."
            );

            int maxToSpawn = Mathf.Min(pendingOnCallExtraSummonCount, tiles.Count);

            for (int i = 0; i < maxToSpawn; i++)
            {
                var tile = tiles[i];

                // New CardInstance for each extra unit.
                int extraOwnerId = pendingOnCallSummonSource.ownerId;

                // Use your existing CardInstance constructor (CardSO, ownerId)
                var extraInstance = new CardInstance(pendingOnCallExtraSummonUnit, extraOwnerId)
                {
                    instanceId = System.Guid.NewGuid().ToString(),
                    isGeneratedToken = pendingOnCallSummonSource.data != null &&
                                       pendingOnCallSummonSource.data.onCallExtraSummonsAreTokens
                };

                Debug.Log(
                    $"[OnCallSummon] -> Extra unit {extraName} instance={extraInstance.instanceId} " +
                    $"at tile {tile} (owner={extraInstance.ownerId}, token={extraInstance.isGeneratedToken})."
                );

                SpawnExtraUnitInstanceAtTile(extraInstance, tile);
            }

            isResolvingOnCallExtraSummons = false;
            pendingOnCallSummonSource = null;
            pendingOnCallExtraSummonCount = 0;
            pendingOnCallExtraSummonUnit = null;
        }

        /// <summary>
        /// Spawns an extra unit instance at the given CardPhase tile, using the same
        /// 3D spawn + registration pipeline as a normal unit played from hand.
        /// </summary>
        private void SpawnExtraUnitInstanceAtTile(CardInstance instance, Vector2Int tile)
        {
            if (instance == null || instance.data == null)
            {
                Debug.LogWarning("[OnCallSummon] SpawnExtraUnitInstanceAtTile called with null instance or data.");
                return;
            }

            var so = instance.data;

            var grid = GameObject.FindObjectOfType<GridService>();
            if (grid == null)
            {
                Debug.LogError("[OnCallSummon] No GridService found in scene; cannot spawn extra unit.");
                return;
            }

            // Use the same footprint rules as DraggableCard.GetFootprintInts
            int w = Mathf.Clamp(so.sizeW, 1, 4);
            int h = Mathf.Clamp(so.sizeH, 1, 4);
            if (!grid.CanPlaceRect(tile, w, h))
            {
                Debug.LogWarning(
                    $"[OnCallSummon] Cannot place extra unit {so.cardName} at {tile} (w={w}, h={h}); " +
                    "rect occupied or out of bounds."
                );
                return;
            }

            grid.PlaceRect(tile, w, h);

            var unitPrefab = so.unitPrefab;
            if (unitPrefab == null)
            {
                Debug.LogError($"[OnCallSummon] CardSO {so.cardName} has no unitPrefab; cannot spawn extra unit.");
                return;
            }

            Vector3 center = grid.TileCenterToWorld(tile, 0f)
                           + new Vector3((w - 1) * 0.5f * grid.TileSize, 0f, (h - 1) * 0.5f * grid.TileSize);

            // Record the placement for the battle layer
            var reg = BattlePlacementRegistry.Instance;
            int owner = instance.ownerId;
            if (reg != null)
            {
                // Compute TRUE board center X from bounds (renderer > collider > fallback)
                float centerX = grid.transform.position.x;
                var rendGrid = grid.GetComponentInChildren<Renderer>();
                if (rendGrid != null)
                {
                    centerX = rendGrid.bounds.center.x;
                }
                else
                {
                    var collGrid = grid.GetComponentInChildren<Collider>();
                    if (collGrid != null)
                        centerX = collGrid.bounds.center.x;
                }

                reg.SetLocalBoardCenterX(centerX);
                reg.Register(instance, center, owner);
            }
            else
            {
                Debug.LogWarning("[OnCallSummon] BattlePlacementRegistry.Instance is null; placement not recorded for extra unit.");
            }

            // Parent: use the grid's transform just like DraggableCard does when unitsParent is null.
            Transform parent = grid.transform;
            var go = GameObject.Instantiate(unitPrefab, center, Quaternion.identity, parent);

            // Attach graveyard relay to record unit deaths (per-player / per-realm)
            var gy = go.GetComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
            if (gy == null) gy = go.AddComponent<Game.Match.Graveyard.GraveyardOnDestroy>();
            gy.source = so;
            gy.ownerId = owner;

            // Adjust Y so the unit sits on the board surface
            float groundY = grid.transform.position.y;
            var col = go.GetComponentInChildren<Collider>();
            var rendUnit = (col == null) ? go.GetComponentInChildren<Renderer>() : null;
            float halfH = 0.5f;
            if (col != null) halfH = col.bounds.extents.y;
            else if (rendUnit != null) halfH = rendUnit.bounds.extents.y;

            var p = go.transform.position;
            p.y = groundY + halfH;
            go.transform.position = p;

            var ur = go.GetComponent<Game.Match.Units.UnitRuntime>();
            if (ur != null) ur.InitFrom(so);

            // Attach CardPhaseSelectableUnit so this unit can be chosen as a target in CardPhase.
            CardPhaseSelectableUnit selectable = null;
            if (col != null)
            {
                selectable = col.GetComponent<CardPhaseSelectableUnit>();
                if (selectable == null)
                    selectable = col.gameObject.AddComponent<CardPhaseSelectableUnit>();
            }
            else
            {
                selectable = go.GetComponent<CardPhaseSelectableUnit>();
                if (selectable == null)
                    selectable = go.AddComponent<CardPhaseSelectableUnit>();
            }

            if (selectable != null)
            {
                int ownerId = instance.ownerId;
                selectable.InitializeForCardPhase(ownerId, so, ur);

                // This extra unit counts as "called" for Savage and similar conditions.
                OnUnitCalledFromCardPhase(so, ownerId);

                Debug.Log(
                    $"[OnCallSummon] Extra unit {so.cardName} spawned and marked as Called on CardPhase grid for owner={ownerId}."
                );
            }

            go.name = $"{so.cardName}_extra_{tile.x}_{tile.y}";
        }

        /// <summary>
        /// Returns a snapshot list of all unit cards currently in this player's hand.
        /// </summary>
        public List<CardInstance> GetUnitCardsInHand(int ownerIdForEffect)
        {
            var result = new List<CardInstance>();

            if (ownerIdForEffect != ownerId)
                return result;

            foreach (var ci in hand)
            {
                if (ci == null || ci.data == null)
                    continue;

                // Only true unit cards
                if (ci.data.type != CardType.Unit)
                    continue;

                // Rule: token-only cards should not be treated as normal hand units.
                if (ci.data.isTokenOnly)
                    continue;

                // Rule: generated tokens in hand should be ignored by "unit card" costs.
                if (ci.isGeneratedToken)
                    continue;

                result.Add(ci);
            }

            return result;
        }

        void Awake()
        {
            BuildDeck();
        }

        void Start()
        {
            if (autoStart)
                BeginMatch();
            // NOTE: TurnTimerHUD should NOT auto-start. StartTurn() is the single source of truth.
        }

        /// <summary>Called once at match start from sandbox/boot.</summary>
        public void BeginMatch()
        {
            GraveyardService.Instance.ClearAll();

            hand.Clear();
            BuildDeck();

            // opening hand
            Draw(startingHand);

            turnIndex = 0;
            StartTurn(); // starts timer, bumps mana, draws 1, notifies UI
        }

        /// <summary>
        /// Called by:
        /// - TurnTimerHUD when the countdown hits 0
        /// - End Turn button (via UnityEvent / script)
        ///
        /// Ends the CardPhase portion of the turn and launches the BattlePhase.
        /// Next CardPhase turn will be started automatically when BattleScene calls back.
        /// </summary>
        public void EndTurn()
        {
            // Stop current countdown immediately
            timer?.StopTimer();

            // Optional: extra draw on end-turn (default 0 for HS-style)
            if (drawOnEndTurn > 0)
                Draw(drawOnEndTurn);

            Debug.Log($"[Turn] End → launching battle (turn={turnIndex})");

            OnTurnEnded?.Invoke(turnIndex);

            // Launch BattlePhase from current CardPhase state
            if (battleLauncher != null)
            {
                battleLauncher.StartBattle();
            }
            else
            {
                Debug.LogWarning("[TurnController] EndTurn() called but no CardPhaseBattleLauncher assigned. Staying in CardPhase and starting next turn.");
                // Fallback: no battle scene wired yet, just continue the loop.
                StartTurn();
            }
        }

        /// <summary>
        /// Called by BattleSceneController.ReturnToCardPhase()
        /// when the battle is done and CardPhase scene is visible again.
        /// </summary>
        public void OnReturnFromBattle()
        {
            Debug.Log("[Turn] ReturnFromBattle → starting next CardPhase turn.");
            StartTurn();
        }

        /// <summary>Internal per-turn entry point: bumps + refills mana, draws 1, restarts timer.</summary>
        private void StartTurn()
        {
            // (Re)start a fresh FULL countdown first, so UI snaps immediately
            timer?.StartTurnTimer();

            turnIndex++;

            // Reset once-per-turn spell usage flags.
            hasUsedSavageReturnSearchThisTurn = false;
            hasCalledSavageUnitThisTurn = false;

            // Bump/refill mana every turn (including turn 1 if bumpOnFirstTurn)
            bool doOps = (turnIndex > 1) || bumpOnFirstTurn;
            if (doOps && mana != null)
            {
                if (bumpMaxOnStartTurn)
                {
                    int newSlots = Mathf.Min(slotsCap, mana.Slots + 1);
                    mana.SetSlots(newSlots);
                }

                if (refillOnStartTurn)
                {
                    mana.SetCurrent(mana.Slots);
                }
            }

            int beforeHand = hand.Count;
            int beforeDeck = deck.Count;

            // Start-of-turn draw (hand cap enforced in Draw)
            if (drawOnStartTurn > 0)
                Draw(drawOnStartTurn);

            int drew = hand.Count - beforeHand;

            // 🔹 Turn-based buffs on cards in hand:
            // For now we assume local player is ownerId = 0.
            AdvanceTurnOnHandForOwner(0);

            Debug.Log(
                $"[Turn] Start → index={turnIndex}, mana={mana?.Current}/{mana?.Slots}, " +
                $"drew={drew}, hand={hand.Count}, deck={deck.Count}"
            );

            PushHandToView();
            OnTurnStarted?.Invoke(turnIndex);
        }
        /// <summary>Remove a card from the hand without sending it to graveyard.</summary>
        public void RemoveFromHand(CardInstance ci)
        {
            if (ci == null) return;
            if (hand.Remove(ci)) PushHandToView();
        }

        /// <summary>
        /// Discard a specific card from this controller's hand. Returns true if removed.
        /// Sends the card to this player's realm graveyard and refreshes the hand UI.
        /// </summary>
        public bool Discard(CardInstance ci)
        {
            if (ci == null) return false;

            bool removed = hand.Remove(ci);
            if (!removed) return false;

            // Send to per-player, per-realm graveyard
            if (ci.data != null)
                GraveyardService.Instance.Add(ownerId, ci.data);

            PushHandToView();
            OnCardDiscarded?.Invoke(ci);
            return true;
        }

        /// <summary>Discard by hand index (0-based). Returns true on success.</summary>
        public bool DiscardByIndex(int index)
        {
            if (index < 0 || index >= hand.Count) return false;
            return Discard(hand[index]);
        }

        /// <summary>Discard a random card from hand. Returns true on success.</summary>
        public bool DiscardRandom()
        {
            if (hand.Count == 0) return false;
            int idx = UnityEngine.Random.Range(0, hand.Count);
            return Discard(hand[idx]);
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG: Discard First In Hand")]
        void Debug_DiscardFirst()
        {
            if (hand.Count > 0) Discard(hand[0]);
        }
#endif

        /// <summary>
        /// Draw up to <paramref name="count"/> cards, respecting handMax.
        /// If the deck is empty when we need to draw, signals deck depletion
        /// (player loses by decking out) via OnDeckDepleted.
        /// </summary>
        void Draw(int count)
        {
            int originalCount = count;
            int beforeHand = hand.Count;
            int beforeDeck = deck.Count;

            while (count-- > 0 && hand.Count < handMax)
            {
                if (deck.Count == 0)
                {
                    Debug.LogWarning("[Turn] Deck empty while trying to draw → local player loses. (Hook OnDeckDepleted.)");
                    OnDeckDepleted?.Invoke();
                    break;
                }

                var so = deck.Dequeue();
                if (so == null) continue;

                var ci = new CardInstance(so, ownerId);
                hand.Add(ci);

                // 🔹 Log this draw with sprite + CardSO
                var logger = ActionLogService.Instance;
                if (logger != null)
                {
                    string name = !string.IsNullOrEmpty(so.cardName) ? so.cardName : so.name;
                    logger.CardLocal($"Drew {name}.", so.artSprite, so);
                }
            }

            int drawn = hand.Count - beforeHand;
            Debug.Log(
                $"[Turn] Draw({originalCount}) → drew={drawn}, hand={beforeHand}->{hand.Count}, deck={beforeDeck}->{deck.Count}"
            );
        }

        /// <summary>
        /// Resolve a simple "search unit in deck and add to hand" spell.
        /// Uses the spell's spellSearchRealmFilter to pick a unit CardSO from the deck.
        /// </summary>
        public void ResolveSearchSpell(CardSO spell, int ownerIdForEffect)
        {
            if (spell == null)
                return;

            // Only makes sense for spells with SearchUnitByRealm
            if (spell.spellEffect != SpellEffectKind.SearchUnitByRealm)
                return;

            var realmFilter = spell.spellSearchRealmFilter;

            if (deck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSearchSpell: deck empty, nothing to search.");
                var loggerEmpty = ActionLogService.Instance;
                if (loggerEmpty != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerEmpty.SystemCard($"Cast {spellName}, but the deck was empty.");
                }
                return;
            }

            // Copy queue → list so we can remove exactly one matching card and rebuild in the same order.
            var tmp = new List<CardSO>(deck);
            deck.Clear();

            CardSO found = null;

            foreach (var card in tmp)
            {
                if (found == null &&
                    card != null &&
                    card.type == CardType.Unit &&
                    card.realm == realmFilter)
                {
                    // First matching unit in the chosen realm -> we "tutor" this.
                    found = card;
                    continue; // don't re-enqueue this one
                }

                deck.Enqueue(card);
            }

            if (found == null)
            {
                Debug.Log($"[Turn] ResolveSearchSpell: no unit in realm {realmFilter} found in deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerNone.SystemCard($"Cast {spellName}, but found no valid unit in the deck.");
                }
                return;
            }

            // If hand is full, we burn the searched card to graveyard (v1 behaviour).
            if (hand.Count >= handMax)
            {
                Debug.Log(
                    $"[Turn] ResolveSearchSpell: hand full ({handMax}), cannot add searched card {found.cardName}. Sending to graveyard."
                );

                var loggerFull = ActionLogService.Instance;
                if (loggerFull != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    string foundName = string.IsNullOrEmpty(found.cardName) ? found.name : found.cardName;
                    loggerFull.SystemCard(
                        $"Cast {spellName}, found {foundName} but your hand is full. The card was sent to the graveyard."
                    );
                }

                var gy = GraveyardService.Instance;
                if (gy != null)
                    gy.Add(ownerIdForEffect, found);

                return;
            }

            // Normal case: add to hand as a CardInstance
            var ci = new CardInstance(found, ownerIdForEffect);
            hand.Add(ci);

            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string foundName = string.IsNullOrEmpty(found.cardName) ? found.name : found.cardName;
                logger.CardLocal(
                    $"Searched the deck and added {foundName} to your hand.",
                    found.artSprite,
                    found
                );
            }

            // Update hand UI
            PushHandToView();
        }

        /// <summary>
        /// Returns a snapshot list of all Savage Magic cards currently in this player's deck.
        /// The deck queue is not modified.
        /// </summary>
        public List<CardSO> GetSavageMagicCardsInDeck(int ownerIdForEffect)
        {
            var result = new List<CardSO>();

            if (ownerIdForEffect != ownerId)
                return result;

            foreach (var so in deck)
            {
                if (so == null)
                    continue;

                // Skip token-only entries just in case.
                if (so.isTokenOnly)
                    continue;

                if (so.type == CardType.Spell && so.isSavageMagic)
                    result.Add(so);
            }

            return result;
        }

        /// <summary>
        /// Returns all unit cards in this controller's deck that belong to the Savage archetype.
        /// Used by token-cost spells that search for Savage units.
        /// </summary>
        public List<CardSO> GetSavageUnitCardsInDeck(int ownerIdForEffect)
        {
            var result = new List<CardSO>();

            if (ownerIdForEffect != ownerId)
                return result;

            foreach (var so in deck)
            {
                if (so == null)
                    continue;

                // Skip token-only entries just in case.
                if (so.isTokenOnly)
                    continue;

                if (so.type == CardType.Unit && so.isSavageArchetype)
                    result.Add(so);
            }

            return result;
        }

        /// <summary>
        /// Returns the given unit card instances from hand back into this player's deck.
        /// Cards are placed on the bottom of the deck (they will be drawn later).
        /// Returns the number of cards successfully returned.
        /// </summary>
        public int ReturnUnitCardsToDeck(List<CardInstance> cardsToReturn, int ownerIdForEffect)
        {
            if (cardsToReturn == null || cardsToReturn.Count == 0)
                return 0;

            if (ownerIdForEffect != ownerId)
            {
                Debug.LogWarning("[Turn] ReturnUnitCardsToDeck called with mismatched ownerId.");
                return 0;
            }

            int removedCount = 0;

            foreach (var ci in cardsToReturn)
            {
                if (ci == null || ci.data == null)
                    continue;

                if (ci.data.type != CardType.Unit)
                {
                    Debug.LogWarning("[Turn] ReturnUnitCardsToDeck: tried to return a non-unit card as a unit cost.");
                    continue;
                }

                // Hard rule: never put token-only / generated tokens back into the deck.
                if (ci.data.isTokenOnly || ci.isGeneratedToken)
                {
                    Debug.LogWarning(
                        $"[Turn] ReturnUnitCardsToDeck: refusing to return token-only / generated token card {ci.data.cardName} to deck."
                    );
                    continue;
                }

                bool removed = hand.Remove(ci);
                if (!removed)
                {
                    Debug.LogWarning("[Turn] ReturnUnitCardsToDeck: card was not found in hand.");
                    continue;
                }

                // Place the underlying CardSO on the bottom of the deck.
                deck.Enqueue(ci.data);
                removedCount++;
            }

            if (removedCount > 0)
            {
                PushHandToView();

                var logger = ActionLogService.Instance;
                if (logger != null)
                {
                    logger.SystemCard(
                        $"Returned {removedCount} unit card(s) from your hand to the bottom of your deck as a cost."
                    );
                }

                Debug.Log(
                    $"[Turn] ReturnUnitCardsToDeck: returned {removedCount} unit card(s) to deck bottom. " +
                    $"New hand={hand.Count}, deck={deck.Count}"
                );
            }

            return removedCount;
        }

        /// <summary>
        /// Called when a unit with an on-call Vorg'co search effect is successfully Called
        /// on the CardPhase board. Searches this player's deck for the first unit card
        /// with Race.Vorgco, removes it from the deck, and adds it to hand (or graveyard
        /// if the hand is full).
        /// </summary>
        public void ResolveOnCallSearchVorgcoUnit(CardSO caller, int ownerIdForEffect)
        {
            if (caller == null)
                return;

            var candidates = GetVorgcoUnitsInDeck();
            if (candidates == null || candidates.Count == 0)
            {
                Debug.Log("[Turn] ResolveOnCallSearchVorgcoUnit: no Vorg'co unit found in deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string callerName = string.IsNullOrEmpty(caller.cardName) ? caller.name : caller.cardName;
                    loggerNone.SystemCard($"Called {callerName}, but found no Vorg'co unit in the deck.");
                }
                return;
            }

            // Fallback: auto-pick the first candidate.
            var picked = candidates[0];
            ResolveOnCallSearchVorgcoUnitPick(caller, ownerIdForEffect, picked);
        }

        // Returns all Vorg'co SPELL cards currently in this player's deck.
        public List<CardSO> GetVorgcoMagicCardsInDeck()
        {
            var result = new List<CardSO>();

            foreach (var so in deck)
            {
                if (so == null)
                    continue;

                // Skip token-only entries just in case.
                if (so.isTokenOnly)
                    continue;

                if (so.type == CardType.Spell && so.race == Race.Vorgco)
                    result.Add(so);
            }

            return result;
        }

        /// <summary>
        /// Returns a snapshot list of all Vorg'co unit CardSOs currently in this player's deck,
        /// in deck order (top-first).
        /// </summary>
        public List<CardSO> GetVorgcoUnitsInDeck()
        {
            var result = new List<CardSO>();

            foreach (var so in deck)
            {
                if (so == null)
                    continue;

                // Skip token-only entries just in case.
                if (so.isTokenOnly)
                    continue;

                if (so.type == CardType.Unit && so.race == Race.Vorgco)
                    result.Add(so);
            }

            return result;
        }
        /// <summary>
        /// Resolve the Vorg'co on-call search when the caller has already chosen a specific
        /// CardSO from the deck. Removes the first instance of that CardSO from the deck and
        /// adds it to hand (or graveyard if hand is full).
        /// </summary>

        public void ResolveOnCallSearchVorgcoUnitPick(CardSO caller, int ownerIdForEffect, CardSO picked)
        {
            if (caller == null || picked == null)
                return;

            if (deck.Count == 0)
            {
                Debug.Log("[Turn] ResolveOnCallSearchVorgcoUnitPick: deck empty, nothing to search.");
                var loggerEmpty = ActionLogService.Instance;
                if (loggerEmpty != null)
                {
                    string callerName = string.IsNullOrEmpty(caller.cardName) ? caller.name : caller.cardName;
                    loggerEmpty.SystemCard($"Called {callerName}, but the deck was empty.");
                }
                return;
            }

            // Rebuild the deck, removing only the first instance of 'picked'.
            var tmp = new List<CardSO>(deck);
            deck.Clear();

            bool removed = false;
            foreach (var card in tmp)
            {
                if (!removed && card == picked)
                {
                    removed = true;
                    continue; // skip this one; it's the chosen card
                }

                deck.Enqueue(card);
            }

            if (!removed)
            {
                Debug.LogWarning("[Turn] ResolveOnCallSearchVorgcoUnitPick: picked card was not found in deck.");
                return;
            }

            // Hand full? send to graveyard instead.
            if (hand.Count >= handMax)
            {
                Debug.Log(
                    $"[Turn] ResolveOnCallSearchVorgcoUnitPick: hand full ({hand.Count}/{handMax}), " +
                    $"cannot add searched card {picked.cardName}. Sending to graveyard."
                );

                var loggerFull = ActionLogService.Instance;
                if (loggerFull != null)
                {
                    string callerName = string.IsNullOrEmpty(caller.cardName) ? caller.name : caller.cardName;
                    string foundName = string.IsNullOrEmpty(picked.cardName) ? picked.name : picked.cardName;
                    loggerFull.SystemCard(
                        $"Called {callerName}, found {foundName} but your hand is full. The card was sent to the graveyard."
                    );
                }

                var gy = GraveyardService.Instance;
                if (gy != null)
                    gy.Add(ownerIdForEffect, picked);

                return;
            }

            // Normal case: add to hand as a CardInstance
            var ci = new CardInstance(picked, ownerIdForEffect);
            hand.Add(ci);

            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string callerName = string.IsNullOrEmpty(caller.cardName) ? caller.name : caller.cardName;
                string foundName = string.IsNullOrEmpty(picked.cardName) ? picked.name : picked.cardName;
                logger.CardLocal(
                    $"When {callerName} was Called, you searched the deck and added {foundName} to your hand.",
                    picked.artSprite,
                    picked
                );
            }

            // Update hand UI
            PushHandToView();
        }

        /// <summary>
        /// Starts a token-cost selection using the CardPhaseTargetSelectionController.
        /// v1: only supports FieldTokenCostKind.SavageStacks and a single chosen friendly unit.
        ///
        /// This method does NOT handle any follow-up effect yet. It only:
        /// - lets the player choose a unit that can pay the cost,
        /// - and then attempts to spend the required stacks from that unit.
        /// </summary>
        private void RequestTokenCostFromFriendlyUnit(
            CardInstance sourceSpell,
            FieldTokenCostKind costKind,
            int requiredStacks)
        {
            if (sourceSpell == null || sourceSpell.data == null)
            {
                UnityEngine.Debug.LogWarning("[TokenCost] RequestTokenCostFromFriendlyUnit called with null sourceSpell.");
                return;
            }

            if (costKind == FieldTokenCostKind.None || requiredStacks <= 0)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] RequestTokenCostFromFriendlyUnit invalid costKind={costKind}, requiredStacks={requiredStacks} for spell={sourceSpell.data.cardName}.");
                return;
            }

            if (isResolvingTokenCost)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] Already resolving a token-cost request; ignoring new request for spell={sourceSpell.data.cardName}.");
                return;
            }

            // The selection controller is the same controller used for the OnCall target-selection
            // (e.g. ChosenFriendlySavageVorgco).
            if (cardPhaseTargetSelectionController == null)
            {
                UnityEngine.Debug.LogWarning("[TokenCost] No CardPhaseTargetSelectionController assigned to TurnController; cannot start token-cost selection.");
                return;
            }

            isResolvingTokenCost = true;
            pendingTokenCostStacks = requiredStacks;
            pendingTokenCostKind = costKind;

            UnityEngine.Debug.Log($"[TokenCost] Starting token-cost selection: spell={sourceSpell.data.cardName}, costKind={costKind}, stacks={requiredStacks}.");

            cardPhaseTargetSelectionController.BeginTokenCostSelection(
                sourceSpell,
                costKind,
                requiredStacks,
                OnTokenCostUnitChosen);
        }

        /// <summary>
        /// Callback invoked by CardPhaseTargetSelectionController when the player chooses
        /// a unit to pay the token cost from.
        ///
        /// v1: We assume the cost is SavageStacks, and we simply attempt to spend
        /// 'pendingTokenCostStacks' from the chosen unit. No follow-up effect yet.
        /// </summary>
        private void OnTokenCostUnitChosen(CardInstance sourceSpell, object rawTarget)
        {
            // We are done with the modal interaction regardless of success/failure.
            isResolvingTokenCost = false;
            var costKind = pendingTokenCostKind;
            var requiredStacks = pendingTokenCostStacks;

            // Reset pending cost state now that the selection has concluded.
            pendingTokenCostKind = FieldTokenCostKind.None;
            pendingTokenCostStacks = 0;

            if (sourceSpell == null || sourceSpell.data == null)
            {
                UnityEngine.Debug.LogWarning("[TokenCost] OnTokenCostUnitChosen received null sourceSpell.");
                return;
            }

            if (rawTarget == null)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] OnTokenCostUnitChosen received null target for spell={sourceSpell.data.cardName}.");
                return;
            }

            var selectable = rawTarget as CardPhaseSelectableUnit;
            if (selectable == null)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] OnTokenCostUnitChosen target is not a CardPhaseSelectableUnit (type={rawTarget.GetType().Name}) for spell={sourceSpell.data.cardName}.");
                return;
            }

            var runtime = selectable.Runtime;
            if (runtime == null || runtime.StatusController == null)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] OnTokenCostUnitChosen target={selectable.name} has no Runtime/StatusController; cannot pay token cost for spell={sourceSpell.data.cardName}.");
                return;
            }

            if (requiredStacks <= 0 || costKind == FieldTokenCostKind.None)
            {
                UnityEngine.Debug.LogWarning($"[TokenCost] OnTokenCostUnitChosen got invalid cost config: kind={costKind}, stacks={requiredStacks} for spell={sourceSpell.data.cardName}.");
                return;
            }

            bool paid = false;
            string debugContext = $"Spell={sourceSpell.data.cardName}, owner={sourceSpell.ownerId}, target={selectable.name}";

            switch (costKind)
            {
                case FieldTokenCostKind.SavageStacks:
                    paid = runtime.StatusController.TrySpendSavageStacks(requiredStacks, debugContext);
                    break;

                default:
                    UnityEngine.Debug.LogWarning($"[TokenCost] Unsupported FieldTokenCostKind={costKind} in OnTokenCostUnitChosen for spell={sourceSpell.data.cardName}.");
                    break;
            }

            if (!paid)
            {
                UnityEngine.Debug.Log($"[TokenCost] Failed to pay token cost {requiredStacks} ({costKind}) from unit={selectable.name} for spell={sourceSpell.data.cardName}.");
                return;
            }

            UnityEngine.Debug.Log($"[TokenCost] Successfully paid token cost {requiredStacks} ({costKind}) from unit={selectable.name} for spell={sourceSpell.data.cardName}.");

            // Follow-up: if this spell is configured to search a Savage unit after paying the cost,
            // trigger that flow now.
            var data = sourceSpell.data;
            if (data != null && data.fieldTokenCostSearchSavageUnit)
            {
                ResolveSavageUnitSearchFromTokenCost(data, sourceSpell.ownerId);
            }
        }        

        /// <summary>
        /// Convenience entrypoint for a spell that says:
        /// "Use 2 Savage tokens from a chosen unit you control".
        /// This just kicks off the token-cost selection and attempts to spend 2 stacks
        /// from the chosen unit. No follow-up effect attached yet.
        /// </summary>
        private void ResolveUseTwoSavageTokensFromChosenUnit(CardInstance spellInstance)
        {
            if (spellInstance == null || spellInstance.data == null)
            {
                UnityEngine.Debug.LogWarning("[TokenCost] ResolveUseTwoSavageTokensFromChosenUnit called with null spellInstance.");
                return;
            }

            // For now, we hardcode the cost-kind + amount.
            // Later we can drive this from spellInstance.data.fieldTokenCostKind / fieldTokenCostAmount.
            RequestTokenCostFromFriendlyUnit(
                spellInstance,
                FieldTokenCostKind.SavageStacks,
                2);
        }

        /// <summary>
        /// After successfully paying a token cost on a spell that is configured to
        /// search for Savage units, open the deck search panel in SavageUnit mode.
        /// </summary>
        private void ResolveSavageUnitSearchFromTokenCost(CardSO spell, int ownerIdForEffect)
        {
            if (spell == null)
                return;

            if (ownerIdForEffect != ownerId)
                return;

            // Build candidate list: Savage units in this deck.
            var candidates = GetSavageUnitCardsInDeck(ownerIdForEffect);
            if (candidates == null || candidates.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageUnitSearchFromTokenCost: paid cost, but found no Savage units in deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerNone.SystemCard($"Paid the cost for {spellName}, but found no Savage units in your deck.");
                }
                return;
            }

            var searchPanel = DeckSearchVorgcoPanel.Instance;
            if (searchPanel == null)
            {
                Debug.LogWarning("[Turn] ResolveSavageUnitSearchFromTokenCost: DeckSearchVorgcoPanel not found; effect ends with no search.");
                // Optional: you could later add a non-UI fallback here.
                return;
            }

            int maxPicks = spell.fieldTokenCostSearchSavageUnitMaxSelections;
            if (maxPicks <= 0)
                maxPicks = 1;

            Debug.Log($"[Turn] ResolveSavageUnitSearchFromTokenCost: opening deck search for up to {maxPicks} Savage unit(s).");

            searchPanel.BeginSavageUnit(spell, ownerIdForEffect, this, candidates, maxPicks);
        }

        /// <summary>
        /// Generic entry point for spells that pay a field token cost,
        /// e.g. "Use 2 Savage tokens from a chosen unit you control".
        /// Reads the cost kind/amount from the spell's CardSO and starts
        /// the token-cost selection flow.
        /// </summary>
        public void ResolveFieldTokenCostSpell(CardInstance spellInstance)
        {
            if (spellInstance == null || spellInstance.data == null)
            {
                UnityEngine.Debug.LogWarning("[TokenCost] ResolveFieldTokenCostSpell called with null spellInstance.");
                return;
            }

            var data = spellInstance.data;

            if (data.fieldTokenCostKind == FieldTokenCostKind.None || data.fieldTokenCostAmount <= 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[TokenCost] ResolveFieldTokenCostSpell: spell={data.cardName} has no valid field token cost configured."
                );
                return;
            }

            // Kick off the standard token-cost selection flow.
            RequestTokenCostFromFriendlyUnit(
                spellInstance,
                data.fieldTokenCostKind,
                data.fieldTokenCostAmount);
        }

        /// <summary>
        /// Resolves the custom Savage spell:
        /// "Return 3 unit cards from your hand to your deck to search up to 2 Savage Magic spells (once per turn)."
        /// Cost payment is done via HandSelectionController selecting exactly 3 unit cards from hand.
        /// </summary>
        public void ResolveSavageReturnSearchSpell(CardSO spell, int ownerIdForEffect)
        {
            if (spell == null)
                return;

            if (ownerIdForEffect != ownerId)
            {
                Debug.LogWarning("[Turn] ResolveSavageReturnSearchSpell called for non-owner TurnController.");
                return;
            }

            // Once-per-turn gate.
            if (!CanUseSavageReturnSearch(ownerIdForEffect))
            {
                Debug.Log("[Turn] ResolveSavageReturnSearchSpell: already used this effect this turn. Ignoring.");
                var loggerUsed = ActionLogService.Instance;
                if (loggerUsed != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerUsed.SystemCard($"You have already activated {spellName} this turn. The effect does nothing.");
                }
                return;
            }

            // Check we have at least 3 unit cards in hand.
            var unitCards = GetUnitCardsInHand(ownerIdForEffect);
            if (unitCards.Count < 3)
            {
                Debug.Log("[Turn] ResolveSavageReturnSearchSpell: not enough unit cards in hand to pay cost.");
                var loggerFew = ActionLogService.Instance;
                if (loggerFew != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerFew.SystemCard($"Tried to cast {spellName}, but you don't have at least 3 unit cards in hand to pay the cost.");
                }
                return;
            }

            var handSel = HandSelectionController.Instance;
            if (handSel == null)
            {
                Debug.LogWarning("[Turn] ResolveSavageReturnSearchSpell: no HandSelectionController in scene. Cannot start cost selection.");
                return;
            }

            // Log that we're entering cost selection mode.
            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                logger.SystemCard($"Select 3 unit cards in your hand to return to your deck as a cost for {spellName}.");
            }

            Debug.Log("[Turn] ResolveSavageReturnSearchSpell: starting hand selection for 3 unit cards.");

            // Begin selection of exactly 3 unit cards as the cost.
            handSel.BeginUnitCostSelection(ownerIdForEffect, 3, chosenCards =>
            {
                if (chosenCards == null || chosenCards.Count < 3)
                {
                    Debug.LogWarning("[Turn] SavageReturnSearch: selection callback with < 3 cards. Cost not paid.");
                    return;
                }

                int removed = ReturnUnitCardsToDeck(chosenCards, ownerIdForEffect);
                if (removed < 3)
                {
                    Debug.LogWarning($"[Turn] SavageReturnSearch: only {removed}/3 selected unit cards were actually returned to deck. Aborting effect.");
                    return;
                }

                // Cost successfully paid → mark once-per-turn usage.
                MarkSavageReturnSearchUsed(ownerIdForEffect);

                // Now resolve the actual deck search for up to 2 Savage Magic spells.
                // Preferred path: open the deck search UI in SavageMagic mode so the player
                // can choose up to 2 Savage Magic spells manually.
                var savageCandidates = GetSavageMagicCardsInDeck(ownerIdForEffect);
                if (savageCandidates == null || savageCandidates.Count == 0)
                {
                    Debug.Log("[Turn] ResolveSavageReturnSearchSpell: paid cost, but found no Savage Magic spells in deck.");
                    var loggerNone = ActionLogService.Instance;
                    if (loggerNone != null)
                    {
                        string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                        loggerNone.SystemCard($"Paid the cost for {spellName}, but found no Savage Magic spells in your deck.");
                    }
                    return;
                }

                var searchPanel = DeckSearchVorgcoPanel.Instance;
                if (searchPanel == null)
                {
                    Debug.LogWarning("[Turn] ResolveSavageReturnSearchSpell: DeckSearchVorgcoPanel not found; falling back to random search.");
                    ResolveSavageMagicSearchAfterCost(spell, ownerIdForEffect);
                    return;
                }

                // Open the deck search panel in SavageMagic mode, allowing the player to pick up to 2 spells.
                searchPanel.BeginSavageMagic(spell, ownerIdForEffect, this, savageCandidates, 2);
            });
        }
        /// <summary>
        /// After the 3-unit cost has been paid, search the deck for up to 2 "Savage Magic" spells:
        /// Spell cards with isSavageMagic == true.
        /// Adds them to hand if there is space; otherwise sends them to graveyard.
        /// </summary>
        private void ResolveSavageMagicSearchAfterCost(CardSO spell, int ownerIdForEffect)
        {
            if (ownerIdForEffect != ownerId)
                return;

            if (deck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageMagicSearchAfterCost: deck empty, nothing to search.");
                var loggerEmpty = ActionLogService.Instance;
                if (loggerEmpty != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerEmpty.SystemCard($"Paid the cost for {spellName}, but your deck is empty.");
                }
                return;
            }

            // Copy queue -> list so we can remove exactly 2 matching cards and rebuild in the same order.
            var tmp = new List<CardSO>(deck);
            deck.Clear();

            var picked = new List<CardSO>();

            foreach (var card in tmp)
            {
                if (picked.Count < 2 &&
                    card != null &&
                    card.type == CardType.Spell &&
                    card.isSavageMagic)
                {
                    picked.Add(card);
                    continue; // don't re-enqueue this one
                }

                deck.Enqueue(card);
            }

            if (picked.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageMagicSearchAfterCost: no Savage Magic spells found in deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerNone.SystemCard($"Paid the cost for {spellName}, but found no Savage Magic spells in your deck.");
                }
                return;
            }

            var gy = GraveyardService.Instance;
            var logger = ActionLogService.Instance;

            foreach (var card in picked)
            {
                if (card == null)
                    continue;

                // If hand is full, we burn the searched card to graveyard (same behaviour as ResolveSearchSpell).
                if (hand.Count >= handMax)
                {
                    Debug.Log(
                        $"[Turn] ResolveSavageMagicSearchAfterCost: hand full ({hand.Count}/{handMax}), cannot add searched card {card.cardName}. Sending to graveyard."
                    );

                    if (logger != null)
                    {
                        string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                        string foundName = string.IsNullOrEmpty(card.cardName) ? card.name : card.cardName;
                        logger.SystemCard(
                            $"Paid the cost for {spellName}, found {foundName} but your hand is full. The card was sent to the graveyard."
                        );
                    }

                    if (gy != null)
                        gy.Add(ownerIdForEffect, card);

                    continue;
                }

                // Normal case: add to hand as a CardInstance
                var ci = new CardInstance(card, ownerIdForEffect);
                hand.Add(ci);

                Debug.Log($"[Turn] ResolveSavageMagicSearchAfterCost: added Savage Magic {card.cardName} to hand. hand={hand.Count}/{handMax}");
            }

            // Finally, push hand changes to the UI.
            PushHandToView();

            if (logger != null)
            {
                string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;

                if (picked.Count == 1)
                {
                    string foundName = string.IsNullOrEmpty(picked[0].cardName) ? picked[0].name : picked[0].cardName;
                    logger.SystemCard(
                        $"Paid the cost for {spellName} and added {foundName} (Savage Magic) to your hand."
                    );
                }
                else
                {
                    string n1 = string.IsNullOrEmpty(picked[0].cardName) ? picked[0].name : picked[0].cardName;
                    string n2 = string.IsNullOrEmpty(picked[1].cardName) ? picked[1].name : picked[1].cardName;
                    logger.SystemCard(
                        $"Paid the cost for {spellName} and added {n1} and {n2} (Savage Magic) to your hand."
                    );
                }
            }
        }
        public void ResolveRefillManaSpell(CardSO spell, int ownerIdForEffect)
        {
            if (spell == null)
                return;

            if (spell.spellEffect != SpellEffectKind.RefillManaToMax)
                return;

            bool manaActuallyRefilled = false;

            // Use the ManaPool for this owner (0 = local, 1 = remote)
            if (manaPools != null &&
                ownerIdForEffect >= 0 &&
                ownerIdForEffect < manaPools.Length &&
                manaPools[ownerIdForEffect] != null)
            {
                var pool = manaPools[ownerIdForEffect];

                // Refill: set Current to max (Slots)
                pool.SetCurrent(pool.Slots);
                manaActuallyRefilled = true;
            }
            else
            {
                Debug.LogWarning(
                    $"[Turn] ResolveRefillManaSpell: no ManaPool hooked up for owner {ownerIdForEffect}."
                );
            }

            var logger = ActionLogService.Instance;
            if (logger != null)
            {
                string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;

                if (manaActuallyRefilled)
                {
                    logger.SystemCard($"Cast {spellName} and refilled your mana to max.");
                }
                else
                {
                    logger.SystemCard(
                        $"Cast {spellName}, but no ManaPool was assigned for this player."
                    );
                }
            }
        }

        private void AdvanceTurnOnHandForOwner(int ownerId)
        {
            if (hand == null || hand.Count == 0)
                return;

            for (int i = 0; i < hand.Count; i++)
            {
                var ci = hand[i];
                if (ci == null)
                    continue;

                if (ci.ownerId != ownerId)
                    continue;

                ci.AdvanceTurn();
            }

            // Hand UI will also get refreshed by the PushHandToView() call in StartTurn,
            // but this ensures any immediate changes are reflected if you ever call this elsewhere.
            PushHandToView();
        }
        public void ResolveBuffHandSpell(CardSO spell, int ownerIdForEffect)
        {
            if (spell == null)
                return;

            if (spell.spellEffect != SpellEffectKind.BuffRandomHandUnitSimple)
                return;

            // Collect candidate unit cards in this player's hand.
            var candidates = new List<CardInstance>();

            for (int i = 0; i < hand.Count; i++)
            {
                var ci = hand[i];
                if (ci == null || ci.data == null)
                    continue;

                // Only buff units, and only for the correct owner.
                if (ci.data.type != CardType.Unit)
                    continue;

                if (ci.ownerId != ownerIdForEffect)
                    continue;

                candidates.Add(ci);
            }

            var logger = ActionLogService.Instance;

            if (candidates.Count == 0)
            {
                if (logger != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    logger.SystemCard(
                        $"Cast {spellName}, but you had no unit cards in hand to buff."
                    );
                }
                return;
            }

            // Pick a random candidate.
            int idx = UnityEngine.Random.Range(0, candidates.Count);
            var chosen = candidates[idx];
            var chosenData = chosen.data;

            int atkBonus = spell.spellBuffAttackAmount;
            int hpBonus = spell.spellBuffHealthAmount;

            // 🔹 NEW: build the buff according to lifetime config on the CardSO
            StatBuffStatus buffStatus;

            switch (spell.spellBuffLifetimeKind)
            {
                case BuffLifetimeKind.TimeSeconds:
                    {
                        float seconds = Mathf.Max(0.01f, spell.spellBuffDurationSeconds);
                        buffStatus = new StatBuffStatus(atkBonus, hpBonus, seconds);
                        break;
                    }

                case BuffLifetimeKind.AttackCount:
                    {
                        int attackCount = Mathf.Max(1, spell.spellBuffAttackCount);
                        buffStatus = new StatBuffStatus(atkBonus, hpBonus, attackCount);
                        break;
                    }

                case BuffLifetimeKind.TurnCount:
                    {
                        int turnCount = Mathf.Max(1, spell.spellBuffTurnCount);
                        buffStatus = new StatBuffStatus(atkBonus, hpBonus, turnCount, true);
                        break;
                    }

                case BuffLifetimeKind.Permanent:
                default:
                    {
                        buffStatus = new StatBuffStatus(atkBonus, hpBonus);
                        break;
                    }
            }

            // Apply per-instance status instead of mutating CardSO
            chosen.AddStatus(buffStatus);

            // Log a detailed entry mentioning which card was buffed
            if (logger != null && chosenData != null)
            {
                string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                string targetName = string.IsNullOrEmpty(chosenData.cardName)
                    ? chosenData.name
                    : chosenData.cardName;

                int finalAtk = chosen.GetFinalAttack();
                int finalHp = chosen.GetFinalHealth();

                string msg =
                    $"Cast {spellName}, buffing {targetName} in your hand " +
                    $"+{atkBonus} ATK / +{hpBonus} HP → now {finalAtk}/{finalHp}.";

                logger.CardLocal(
                    msg,
                    chosenData.artSprite,
                    chosenData
                );
            }

            // Refresh hand UI so updated stats show up on the card.
            PushHandToView();
        }
        /// <summary>
        /// Applies the result of a Savage Magic deck search where the player has explicitly
        /// chosen which Savage Magic spells to take from the deck (e.g., via a deck search UI).
        /// Removes the chosen CardSOs from the deck, then adds them to hand if there is space,
        /// otherwise sends them to the graveyard. Deck order for non-chosen cards is preserved.
        /// </summary>
        public void ResolveSavageMagicSearchPick(CardSO spell, int ownerIdForEffect, List<CardSO> chosenCards)
        {
            if (spell == null)
                return;

            if (ownerIdForEffect != ownerId)
                return;

            if (chosenCards == null || chosenCards.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageMagicSearchPick: no cards were chosen; effect ends with no additional cards.");
                return;
            }

            if (deck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageMagicSearchPick: deck empty when applying chosen cards.");
                var loggerEmpty = ActionLogService.Instance;
                if (loggerEmpty != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerEmpty.SystemCard($"Paid the cost for {spellName}, but your deck is empty when resolving the search.");
                }
                return;
            }

            // We want to remove the chosen CardSOs from the deck while preserving the relative
            // order of all other cards.
            var tmp = new List<CardSO>(deck);
            deck.Clear();

            var remainingToMatch = new List<CardSO>(chosenCards);
            var pickedFromDeck = new List<CardSO>();

            foreach (var card in tmp)
            {
                if (card != null && remainingToMatch.Contains(card))
                {
                    pickedFromDeck.Add(card);
                    remainingToMatch.Remove(card);
                    // Do not re-enqueue this one; it is being taken by the effect.
                    continue;
                }

                deck.Enqueue(card);
            }

            if (pickedFromDeck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageMagicSearchPick: none of the chosen cards were found in the deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerNone.SystemCard($"You resolved the effect of {spellName}, but none of the chosen Savage Magic spells were found in your deck.");
                }
                return;
            }

            var gy = GraveyardService.Instance;
            var logger = ActionLogService.Instance;

            foreach (var card in pickedFromDeck)
            {
                if (card == null)
                    continue;

                // If hand is full, we burn the searched card to graveyard (same behaviour as other search effects).
                if (hand.Count >= handMax)
                {
                    Debug.Log(
                        $"[Turn] ResolveSavageMagicSearchPick: cannot add searched card {card.cardName}. Sending to graveyard."
                    );

                    if (logger != null)
                    {
                        string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                        string foundName = string.IsNullOrEmpty(card.cardName) ? card.name : card.cardName;
                        logger.SystemCard(
                            $"Paid the cost for {spellName}, found {foundName} but your hand is full. The card was sent to the graveyard."
                        );
                    }

                    if (gy != null)
                        gy.Add(ownerIdForEffect, card);

                    continue;
                }

                // Normal case: add to hand as a CardInstance
                var ci = new CardInstance(card, ownerIdForEffect);
                hand.Add(ci);

                Debug.Log($"[Turn] ResolveSavageMagicSearchPick: added Savage Magic {card.cardName} to hand. hand={hand.Count}/{handMax}");
            }

            // Finally, push hand changes to the UI.
            PushHandToView();
        }

        /// <summary>
        /// Applies the result of a SavageUnit deck search.
        /// Removes the chosen Savage unit CardSOs from the deck, and for each one:
        /// - If hand is full, sends the card to graveyard.
        /// - Otherwise, adds it to hand as a CardInstance.
        /// </summary>
        public void ResolveSavageUnitSearchPick(CardSO spell, int ownerIdForEffect, List<CardSO> chosenCards)
        {
            if (spell == null)
                return;

            if (ownerIdForEffect != ownerId)
                return;

            if (chosenCards == null || chosenCards.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageUnitSearchPick: no cards were chosen; effect ends with no additional cards.");
                return;
            }

            if (deck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageUnitSearchPick: deck empty when applying chosen cards.");
                var loggerEmpty = ActionLogService.Instance;
                if (loggerEmpty != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerEmpty.SystemCard($"Paid the cost for {spellName}, but your deck is empty when resolving the search.");
                }
                return;
            }

            // Remove the chosen CardSOs from the deck while preserving the order of all others.
            var tmp = new List<CardSO>(deck);
            deck.Clear();

            var remainingToMatch = new List<CardSO>(chosenCards);
            var pickedFromDeck = new List<CardSO>();

            foreach (var card in tmp)
            {
                if (card == null)
                {
                    deck.Enqueue(card);
                    continue;
                }

                // Only match Savage unit cards; anything else stays in the deck.
                if (remainingToMatch.Count > 0 &&
                    card.type == CardType.Unit &&
                    card.isSavageArchetype &&
                    remainingToMatch.Contains(card))
                {
                    pickedFromDeck.Add(card);
                    remainingToMatch.Remove(card);
                    // Do not re-enqueue this one; it is being taken by the effect.
                    continue;
                }

                deck.Enqueue(card);
            }

            if (pickedFromDeck.Count == 0)
            {
                Debug.Log("[Turn] ResolveSavageUnitSearchPick: none of the chosen cards were found in the deck.");
                var loggerNone = ActionLogService.Instance;
                if (loggerNone != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    loggerNone.SystemCard($"You resolved the effect of {spellName}, but none of the chosen Savage units were found in your deck.");
                }
                return;
            }

            var gy = GraveyardService.Instance;
            var logger = ActionLogService.Instance;

            foreach (var card in pickedFromDeck)
            {
                if (card == null)
                    continue;

                if (hand.Count >= handMax)
                {
                    Debug.Log($"[Turn] ResolveSavageUnitSearchPick: cannot add searched card {card.cardName}. Hand is full ({hand.Count}/{handMax}); sending to graveyard.");
                    if (logger != null)
                    {
                        string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                        logger.SystemCard(
                            $"Paid the cost for {spellName}, found {card.cardName} but your hand is full. The card was sent to the graveyard."
                        );
                    }

                    if (gy != null)
                        gy.Add(ownerIdForEffect, card);

                    continue;
                }

                // Normal case: add to hand as a CardInstance
                var ci = new CardInstance(card, ownerIdForEffect);
                hand.Add(ci);

                Debug.Log($"[Turn] ResolveSavageUnitSearchPick: added Savage unit {card.cardName} to hand. hand={hand.Count}/{handMax}");

                if (logger != null)
                {
                    string spellName = string.IsNullOrEmpty(spell.cardName) ? spell.name : spell.cardName;
                    logger.SystemCard($"You added Savage unit {card.cardName} to your hand with the effect of {spellName}.");
                }
            }

            // Finally, push hand changes to the UI.
            PushHandToView();
        }

        private void PushHandToView()
        {
            if (handView != null)
                handView.SetHand(hand);
        }

        /// <summary>
        /// Build the runtime deck queue from the configured deckList,
        /// shuffling using shuffleSeed. Token-only cards are never added.
        /// </summary>
        private void BuildDeck()
        {
            deck.Clear();

            if (deckList == null || deckList.Count == 0)
                return;

            var arr = new List<CardSO>(deckList);
            var rng = new System.Random(shuffleSeed);

            // Fisher–Yates shuffle
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }

            foreach (var so in arr)
            {
                if (so == null)
                    continue;

                // Hard rule: token-only CardSOs must never live in the deck.
                if (so.isTokenOnly)
                {
                    Debug.Log($"[Turn] BuildDeck: skipping token-only card {so.cardName}.");
                    continue;
                }

                deck.Enqueue(so);
            }
        }
    }
}
