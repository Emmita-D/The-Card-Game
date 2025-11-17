using Game.Core;
using Game.Match.Cards;
using Game.Match.Units;

namespace Game.Match.Battle
{
    /// <summary>
    /// Centralised combat rules:
    /// - Who can hit what (Ground/Flying + Melee/Ranged matrix)
    /// - Per-card attack permission nerfs (canAttackGroundUnits, etc.).
    /// Dive fliers are handled via UnitRuntime.heightLayer + isDiveFlier.
    /// </summary>
    public static class CombatRules
    {
        public static bool CanUnitAttackUnit(UnitAgent attacker, UnitAgent defender)
        {
            if (attacker == null || defender == null) return false;

            var attackerRuntime = attacker.GetComponent<UnitRuntime>();
            var defenderRuntime = defender.GetComponent<UnitRuntime>();
            if (attackerRuntime == null || defenderRuntime == null) return false;
            if (attackerRuntime.health <= 0 || defenderRuntime.health <= 0) return false;

            var card = attacker.sourceCard;

            // Target classification based on defender's current height layer
            bool targetIsFlying = defenderRuntime.heightLayer == HeightLayer.Air;
            bool targetIsGround = !targetIsFlying;

            // Base allowances from movement + attack mode (global rules).
            bool canHitGroundByCategory = true;
            bool canHitFlyingByCategory = true;

            MovementType move = MovementType.Ground;
            AttackMode mode = AttackMode.Melee;

            if (card != null)
            {
                move = card.movement;
                mode = card.attackMode;
            }
            else
            {
                move = attackerRuntime.movementType;
                mode = attackerRuntime.attackMode;
            }

            // Global rule: ground melee units cannot hit flying.
            if (move == MovementType.Ground && mode == AttackMode.Melee)
            {
                canHitFlyingByCategory = false;
            }

            // Dive fliers don't need special-case here:
            // they always *can* hit both categories; their vulnerability
            // is determined by their own heightLayer when OTHERS attack them.

            // Per-card nerfs (CardSO-level)
            bool canAttackGroundUnitsFlag = card == null || card.canAttackGroundUnits;
            bool canAttackFlyingUnitsFlag = card == null || card.canAttackFlyingUnits;

            if (targetIsFlying)
            {
                return canHitFlyingByCategory && canAttackFlyingUnitsFlag;
            }

            // targetIsGround
            return canHitGroundByCategory && canAttackGroundUnitsFlag;
        }

        public static bool CanUnitAttackTower(UnitAgent attacker, BattleTower tower)
        {
            if (attacker == null || tower == null) return false;
            if (tower.currentHp <= 0) return false;

            var attackerRuntime = attacker.GetComponent<UnitRuntime>();
            if (attackerRuntime == null || attackerRuntime.health <= 0) return false;

            var card = attacker.sourceCard;

            MovementType move = MovementType.Ground;
            AttackMode mode = AttackMode.Melee;

            if (card != null)
            {
                move = card.movement;
                mode = card.attackMode;
            }
            else
            {
                move = attackerRuntime.movementType;
                mode = attackerRuntime.attackMode;
            }

            // v1: all unit categories are allowed to hit towers.
            bool canHitTowerByCategory = true;

            // Per-card nerf
            bool canAttackTowersFlag = card == null || card.canAttackTowers;

            return canHitTowerByCategory && canAttackTowersFlag;
        }
    }
}
