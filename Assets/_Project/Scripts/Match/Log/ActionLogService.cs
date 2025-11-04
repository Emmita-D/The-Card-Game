using System.Collections.Generic;
using UnityEngine;


namespace Game.Match.Log
{
    public enum LogPhase { Card, Battle }
    public struct ActionEvent { public double t; public LogPhase phase; public string text; public ActionEvent(double t, LogPhase p, string s) { this.t = t; this.phase = p; this.text = s; } }
    public class ActionLogService
    {
        readonly List<ActionEvent> events = new(); public void Add(LogPhase p, string s) { events.Add(new ActionEvent(Time.timeAsDouble, p, s)); }
        public IReadOnlyList<ActionEvent> All => events;
    }
}