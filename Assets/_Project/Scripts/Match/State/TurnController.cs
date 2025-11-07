using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.Mana;   // ManaPool (Slots/Current/SetSlots/SetCurrent)
using Game.Match.Graveyard;

namespace Game.Match.State
{
    public class TurnController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private ManaPool mana;     // drag ManaRoot -> ManaPool
        [SerializeField] private HandView handView; // drag HandPanel -> HandView
        [SerializeField] private bool autoStart = true;

        [Header("Deck & Hand")]
        [SerializeField] private List<CardSO> deckList = new();
        [SerializeField] private int startingHand = 5;
        [SerializeField] private int handMax = 10;
        [SerializeField] private int ownerId = 0;
        [SerializeField] private int shuffleSeed = 12345;

        [Header("Turn Behavior")]
        [SerializeField] private bool bumpMaxOnStartTurn = true;   // ON
        [SerializeField] private bool refillOnStartTurn = true;    // ON
        [SerializeField] private int drawOnStartTurn = 0;          // 0
        [SerializeField] private int drawOnEndTurn = 1;            // 1
        [Tooltip("If true, also bump/refill on Turn 1. Keep OFF for your flow.")]
        [SerializeField] private bool bumpOnFirstTurn = false;

        [SerializeField] private TurnTimerHUD timer;

        [Header("Caps")]
        [SerializeField] private int slotsCap = 10; // mana cap

        // runtime
        private readonly Queue<CardSO> deck = new();
        private readonly List<CardInstance> hand = new();
        private int turnIndex = 0;
        public event Action<CardInstance> OnCardDiscarded;

        void Awake() => BuildDeck();

        void Start()
        {
            if (autoStart) BeginMatch();
            // DO NOT start the timer here — StartTurn() is the single source of truth
        }

        public void BeginMatch()
        {
            GraveyardService.Instance.ClearAll();
            Draw(startingHand);
            turnIndex = 0;
            StartTurn(); // will start timer fresh
        }

        public void EndTurn()
        {
            // Stop current countdown immediately
            timer?.StopTimer();

            // Your usual end-turn flow
            if (drawOnEndTurn > 0) Draw(drawOnEndTurn);

            // Advance and start the next turn (this starts a FULL countdown)
            StartTurn();
        }

        private void StartTurn()
        {
            // (Re)start a fresh FULL countdown first, so UI snaps immediately
            timer?.StartTurnTimer();

            turnIndex++;

            // Bump/refill mana except on first turn (unless opted in)
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

            if (drawOnStartTurn > 0) Draw(drawOnStartTurn);
        }

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

        private void Draw(int count)
        {
            while (count-- > 0 && hand.Count < handMax && deck.Count > 0)
            {
                var so = deck.Dequeue();
                var ci = new CardInstance(so, ownerId);
                hand.Add(ci);
            }
            PushHandToView();
        }

        private void PushHandToView()
        {
            if (handView != null) handView.SetHand(hand);
        }

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
            foreach (var so in arr) deck.Enqueue(so);
        }
    }
}

