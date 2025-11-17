using UnityEngine;
using Game.Match.State;   // TurnController

namespace Game.Match.Cards
{
    /// <summary>
    /// v1 effect runner for spell cards.
    /// Currently supports:
    /// - SearchUnitByRealm
    /// - RefillManaToMax
    /// - BuffRandomHandUnitSimple
    /// </summary>
    public static class CardEffectRunner
    {
        /// <summary>
        /// Called when a spell successfully resolves on the CardPhase board.
        /// </summary>
        public static void RunOnSpellResolved(CardSO spell, int ownerId)
        {
            if (spell == null)
                return;

            // Safety: only process spells.
            if (spell.type != Game.Core.CardType.Spell)
                return;

            switch (spell.spellEffect)
            {
                case SpellEffectKind.None:
                    // Spell has no v1 effect, just do nothing.
                    return;

                case SpellEffectKind.SearchUnitByRealm:
                    RunSearchUnitByRealm(spell, ownerId);
                    break;

                case SpellEffectKind.RefillManaToMax:
                    RunRefillManaToMax(spell, ownerId);
                    break;

                case SpellEffectKind.BuffRandomHandUnitSimple:
                    RunBuffRandomHandUnitSimple(spell, ownerId);
                    break;

                default:
                    Debug.LogWarning(
                        $"[CardEffectRunner] Unhandled spellEffect {spell.spellEffect} on {spell.cardName}."
                    );
                    break;
            }
        }

        private static void RunSearchUnitByRealm(CardSO spell, int ownerId)
        {
            var turn = Object.FindObjectOfType<TurnController>();
            if (turn == null)
            {
                Debug.LogWarning("[CardEffectRunner] No TurnController found in scene. Search spell ignored.");
                return;
            }

            turn.ResolveSearchSpell(spell, ownerId);
        }

        private static void RunRefillManaToMax(CardSO spell, int ownerId)
        {
            var turn = Object.FindObjectOfType<TurnController>();
            if (turn == null)
            {
                Debug.LogWarning("[CardEffectRunner] No TurnController found in scene. Mana spell ignored.");
                return;
            }

            turn.ResolveRefillManaSpell(spell, ownerId);
        }

        private static void RunBuffRandomHandUnitSimple(CardSO spell, int ownerId)
        {
            var turn = Object.FindObjectOfType<TurnController>();
            if (turn == null)
            {
                Debug.LogWarning("[CardEffectRunner] No TurnController found in scene. Buff spell ignored.");
                return;
            }

            turn.ResolveBuffHandSpell(spell, ownerId);
        }
    }
}
