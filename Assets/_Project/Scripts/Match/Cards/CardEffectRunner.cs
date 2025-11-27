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
        /// <summary>
        /// Called when a spell successfully resolves on the CardPhase board.
        /// Overload that receives the CardInstance (needed for token-cost spells).
        /// </summary>
        public static void RunOnSpellResolved(CardInstance instance, int ownerId)
        {
            if (instance == null || instance.data == null)
                return;

            RunOnSpellResolvedInternal(instance, instance.data, ownerId);
        }

        /// <summary>
        /// Back-compat overload: only receives the CardSO.
        /// This still works for older effects that don't need the CardInstance.
        /// </summary>
        public static void RunOnSpellResolved(CardSO spell, int ownerId)
        {
            if (spell == null)
                return;

            RunOnSpellResolvedInternal(null, spell, ownerId);
        }

        /// <summary>
        /// Shared implementation for both overloads.
        /// </summary>
        private static void RunOnSpellResolvedInternal(CardInstance instance, CardSO spell, int ownerId)
        {
            if (spell == null)
                return;

            // Safety: only process spells.
            if (spell.type != Game.Core.CardType.Spell)
                return;

            // 1) Field token-cost spells (e.g. "Use 2 Savage tokens from a chosen unit").
            if (spell.fieldTokenCostKind != FieldTokenCostKind.None &&
                spell.fieldTokenCostAmount > 0)
            {
                RunFieldTokenCostSpell(instance, spell, ownerId);
                return;
            }

            // 2) Custom Savage spell: "Return 3 units from hand → search up to 2 Savage Magic cards (once per turn)".
            // This is controlled by a dedicated flag on CardSO instead of SpellEffectKind.
            if (spell.onCallReturn3UnitsSearch2SavageMagic)
            {
                RunSavageReturnSearch(spell, ownerId);
                return;
            }

            // 3) Normal v1 spell effects via SpellEffectKind.
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
        /// <summary>
        /// Entry point for spells that pay a field token cost (e.g. use Savage stacks from a chosen unit).
        /// </summary>
        private static void RunFieldTokenCostSpell(CardInstance instance, CardSO spell, int ownerId)
        {
            if (instance == null)
            {
                Debug.LogWarning("[CardEffectRunner] RunFieldTokenCostSpell called without CardInstance; cannot start token-cost flow.");
                return;
            }

            var turn = Object.FindObjectOfType<TurnController>();
            if (turn == null)
            {
                Debug.LogWarning("[CardEffectRunner] No TurnController found in scene. Field token-cost spell ignored.");
                return;
            }

            Debug.Log($"[CardEffectRunner] Running field token-cost spell for {spell.cardName} (owner={ownerId}).");

            // Delegate to TurnController, which knows how to start token-cost selection.
            turn.ResolveFieldTokenCostSpell(instance);
        }
        private static void RunSavageReturnSearch(CardSO spell, int ownerId)
        {
            var turn = Object.FindObjectOfType<TurnController>();
            if (turn == null)
            {
                Debug.LogWarning("[CardEffectRunner] No TurnController found in scene. Savage return/search spell ignored.");
                return;
            }

            turn.ResolveSavageReturnSearchSpell(spell, ownerId);
        }
    }
}
