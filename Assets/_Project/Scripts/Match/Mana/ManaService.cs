using UnityEngine;
using Game.Core.Config;


namespace Game.Match.Mana
{
    public class ManaService
    {
        readonly BalanceConfigSO cfg; public int MaxMana { get; private set; }
        public int CurrentMana { get; private set; }
        public ManaService(BalanceConfigSO cfg, int start = 1) { this.cfg = cfg; MaxMana = Mathf.Clamp(start, 1, cfg.manaCap); CurrentMana = MaxMana; }
        public void Refill() => CurrentMana = MaxMana;
        public void IncreaseCap(int delta = 1) { MaxMana = Mathf.Clamp(MaxMana + delta, 1, cfg.manaCap); }
        public bool CanPay(int cost) => cost <= CurrentMana; public void Pay(int cost) { CurrentMana = Mathf.Max(0, CurrentMana - cost); }
    }
}