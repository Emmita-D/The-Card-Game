using System.Collections.Generic;
using UnityEngine;
using Game.Match.Cards;   // <-- NEW: so we can store CardSO

namespace Game.Match.Log
{
    public enum LogPhase
    {
        Card,
        Battle
    }

    public enum LogSide
    {
        Local,
        Remote,
        System
    }

    public struct ActionEvent
    {
        public double t;        // Time.timeAsDouble when the event happened
        public LogPhase phase;  // Card or Battle
        public LogSide side;    // Local / Remote / System
        public string text;     // Human-readable description
        public Sprite icon;     // Optional card icon
        public CardSO card;     // Optional card data reference

        public ActionEvent(double t, LogPhase phase, LogSide side, string text, Sprite icon, CardSO card)
        {
            this.t = t;
            this.phase = phase;
            this.side = side;
            this.text = text;
            this.icon = icon;
            this.card = card;
        }
    }

    /// <summary>
    /// Central in-memory log for match actions.
    /// Pure C# singleton (not a MonoBehaviour).
    /// </summary>
    public class ActionLogService
    {
        private static ActionLogService _instance;
        public static ActionLogService Instance => _instance ??= new ActionLogService();

        private readonly List<ActionEvent> events = new();
        public IReadOnlyList<ActionEvent> All => events;

        /// <summary>
        /// Backwards-compatible overload: Add(LogPhase, LogSide)
        /// </summary>
        public void Add(LogPhase phase, LogSide side)
        {
            Add(phase, side, $"{phase} {side}", null, null);
        }

        /// <summary>
        /// Backwards-compatible overload: Add(LogPhase, string)
        /// (defaults to Local side).
        /// </summary>
        public void Add(LogPhase phase, string text)
        {
            Add(phase, LogSide.Local, text, null, null);
        }

        /// <summary>
        /// Backwards-compatible overload: Add(LogPhase, LogSide, string)
        /// </summary>
        public void Add(LogPhase phase, LogSide side, string text)
        {
            Add(phase, side, text, null, null);
        }

        /// <summary>
        /// Main add method with optional icon + card.
        /// </summary>
        public void Add(LogPhase phase, LogSide side, string text, Sprite icon, CardSO card)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            events.Add(new ActionEvent(Time.timeAsDouble, phase, side, text, icon, card));
        }

        // ---- Convenience helpers ----

        // CardPhase (local-only by design)
        public void CardLocal(string text, Sprite icon = null, CardSO card = null)
        {
            Add(LogPhase.Card, LogSide.Local, text, icon, card);
        }

        // BattlePhase – local side
        public void BattleLocal(string text, Sprite icon = null, CardSO card = null)
        {
            Add(LogPhase.Battle, LogSide.Local, text, icon, card);
        }

        // BattlePhase – remote/enemy side
        public void BattleRemote(string text, Sprite icon = null, CardSO card = null)
        {
            Add(LogPhase.Battle, LogSide.Remote, text, icon, card);
        }

        // System / neutral events (start battle, end battle, etc.)
        public void SystemCard(string text, Sprite icon = null, CardSO card = null)
        {
            Add(LogPhase.Card, LogSide.System, text, icon, card);
        }

        public void SystemBattle(string text, Sprite icon = null, CardSO card = null)
        {
            Add(LogPhase.Battle, LogSide.System, text, icon, card);
        }
    }
}
