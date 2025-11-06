using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;
using Game.Match.Mana;   // ManaPool (Slots/Current/SetSlots/SetCurrent)

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
        [SerializeField] private bool refillOnStartTurn = true;   // ON
        [SerializeField] private int drawOnStartTurn = 0;      // 0
        [SerializeField] private int drawOnEndTurn = 1;      // 1
        [Tooltip("If true, also bump/refill on Turn 1. Keep OFF for your flow.")]
        [SerializeField] private bool bumpOnFirstTurn = false;

        [Header("Caps")]
        [SerializeField] private int slotsCap = 10; // mana cap

        // runtime
        private readonly Queue<CardSO> deck = new();
        private readonly List<CardInstance> hand = new();
        private int turnIndex = 0;

        public event Action<int> OnTurnStarted;
        public event Action<int> OnTurnEnded;
        public event Action<CardInstance> OnCardDrawn;

        void Awake() => BuildDeck();
        void Start() { if (autoStart) BeginMatch(); }

        public void BeginMatch()
        {
            // Respect the inspector: e.g., ManaPool startCurrent=1, startSlots=1
            Draw(startingHand);
            turnIndex = 0;
            StartTurn();
        }

        public void EndTurn()
        {
            if (drawOnEndTurn > 0) Draw(drawOnEndTurn);  // draw exactly 1
            OnTurnEnded?.Invoke(turnIndex);
            StartTurn();
        }

        public void RemoveFromHand(CardInstance ci)
        {
            if (ci == null) return;
            if (hand.Remove(ci)) PushHandToView();
        }

        private void StartTurn()
        {
            turnIndex++;

            bool doOps = (turnIndex > 1) || bumpOnFirstTurn; // skip Turn 1 bump/refill unless opted in
            if (doOps && mana != null)
            {
                if (bumpMaxOnStartTurn)
                {
                    int newSlots = Mathf.Min(slotsCap, mana.Slots + 1);
                    mana.SetSlots(newSlots);            // bump Slots by +1 (to cap)
                }
                if (refillOnStartTurn)
                {
                    mana.SetCurrent(mana.Slots);        // refill Current to Slots
                }
            }

            if (drawOnStartTurn > 0) Draw(drawOnStartTurn);
            OnTurnStarted?.Invoke(turnIndex);
        }

        private void Draw(int count)
        {
            while (count-- > 0 && hand.Count < handMax && deck.Count > 0)
            {
                var so = deck.Dequeue();
                var ci = new CardInstance(so, ownerId);
                hand.Add(ci);
                OnCardDrawn?.Invoke(ci);
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
