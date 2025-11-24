using Game.Core;
using Game.Match.Battle;     // CardPhaseBattleLauncher
using Game.Match.Cards;
using Game.Match.Graveyard;
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

                if (ci.data.type == CardType.Unit)
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

                if (so.type == CardType.Spell && so.isSavageMagic)
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
                    $"[Turn] ReturnUnitCardsToDeck: returned {removedCount} unit card(s) to deck bottom. New hand={hand.Count}, deck={deck.Count}"
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
                if (so == null) continue;

                // Magic = Spell type + race Vorg'co
                if (so.type != CardType.Spell) continue;
                if (so.race != Race.Vorgco) continue;

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
                ResolveSavageMagicSearchAfterCost(spell, ownerIdForEffect);
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

        private void PushHandToView()
        {
            if (handView != null)
                handView.SetHand(hand);
        }

        /// <summary>Fisher–Yates shuffle from deckList into the runtime queue.</summary>
        private void BuildDeck()
        {
            deck.Clear();
            if (deckList == null || deckList.Count == 0) return;

            var arr = new List<CardSO>(deckList);
            var rng = new System.Random(shuffleSeed);
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }

            foreach (var so in arr)
                deck.Enqueue(so);
        }
    }
}
