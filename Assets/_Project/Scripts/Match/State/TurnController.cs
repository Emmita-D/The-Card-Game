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

        // Events
        public event Action<CardInstance> OnCardDiscarded;
        public event Action<int> OnTurnStarted;
        public event Action<int> OnTurnEnded;
        /// <summary>Raised when we attempt to draw but the deck is empty. Hook match-end here.</summary>
        public event Action OnDeckDepleted;

        public int TurnIndex => turnIndex;
        public int DeckCount => deck.Count;
        public int HandCount => hand.Count;

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
