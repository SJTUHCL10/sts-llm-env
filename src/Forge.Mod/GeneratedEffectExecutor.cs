using Forge.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Forge.Mod;

internal static class GeneratedEffectExecutor
{
    internal static async Task Execute(NeowGeneratedCard source, CardEffect effect, decimal baseAmount,
        ICombatState combat, PlayerChoiceContext choice, CardPlay? play = null, CardModel? excludedCard = null)
    {
        var owner = source.Owner;
        for (int repetition = 0; repetition < effect.Repeat; repetition++)
        {
            if (!owner.Creature.IsAlive || CombatManager.Instance.IsEnding) return;
            var enemies = combat.HittableEnemies.ToArray();
            Creature[] targets = effect.Target switch
            {
                EffectTarget.Self => [owner.Creature],
                EffectTarget.Enemy => play?.Target is { IsAlive: true, IsHittable: true } enemy ? [enemy] : [],
                EffectTarget.AllEnemies => enemies,
                EffectTarget.RandomEnemy => enemies.Length > 0 ? [owner.RunState.Rng.CombatTargets.NextItem(enemies)!] : [],
                _ => throw new InvalidOperationException()
            };
            // Preserve one native AoE attack transaction when all targets share the same amount/predicate.
            if (effect.Kind == EffectKind.Damage && effect.Target == EffectTarget.AllEnemies
                && effect.Condition is not (EffectCondition.TargetWeak or EffectCondition.TargetVulnerable)
                && effect.Scaling != EffectScaling.TargetPoison)
            {
                if (targets.Length > 0 && MeetsCondition(effect.Condition, owner.Creature, targets[0]))
                {
                    decimal amount = ResolveAmount(effect, baseAmount, source, targets[0]);
                    if (EffectRules.IsAttackDamage(effect))
                        await DamageCmd.Attack(amount).FromCard(source, play).TargetingAllOpponents(combat)
                            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choice);
                    else
                        await CreatureCmd.Damage(choice, targets, amount, ValueProp.Unpowered, owner.Creature);
                }
                continue;
            }
            foreach (var target in targets)
            {
                if (!target.IsAlive || !MeetsCondition(effect.Condition, owner.Creature, target)) continue;
                decimal amount = ResolveAmount(effect, baseAmount, source, target);
                switch (effect.Kind)
                {
                    case EffectKind.Damage:
                        if (EffectRules.IsAttackDamage(effect))
                            await DamageCmd.Attack(amount).FromCard(source, play).Targeting(target)
                                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choice);
                        else
                            await CreatureCmd.Damage(choice, target, amount, ValueProp.Unpowered, owner.Creature);
                        break;
                    case EffectKind.Block:
                        await CreatureCmd.GainBlock(owner.Creature,
                            effect.Trigger == EffectTrigger.OnPlay ? GeneratedEffectPreview.EnchantBlock(source, amount) : amount, ValueProp.Move, play);
                        break;
                    case EffectKind.Draw: await CardPileCmd.Draw(choice, amount, owner); break;
                    case EffectKind.Energy: await PlayerCmd.GainEnergy(amount, owner); break;
                    case EffectKind.Stars: await PlayerCmd.GainStars(amount, owner); break;
                    case EffectKind.DiscardRandomHand:
                    case EffectKind.ExhaustRandomHand:
                    case EffectKind.ReturnRandomDiscard:
                        for (int n = 0; n < amount; n++)
                        {
                            if (CombatManager.Instance.IsEnding || !owner.Creature.IsAlive) return;
                            var pile = effect.Kind == EffectKind.ReturnRandomDiscard ? PileType.Discard : PileType.Hand;
                            var candidates = pile.GetPile(owner).Cards.Where(c => pile == PileType.Discard
                                || c != source && c != play?.Card && c != excludedCard).ToArray();
                            if (candidates.Length == 0 || effect.Kind == EffectKind.ReturnRandomDiscard
                                && PileType.Hand.GetPile(owner).Cards.Count >= CardPile.MaxCardsInHand) break;
                            var selected = owner.RunState.Rng.CombatCardSelection.NextItem(candidates);
                            if (selected is null) break;
                            if (effect.Kind == EffectKind.DiscardRandomHand) await CardCmd.Discard(choice, selected);
                            else if (effect.Kind == EffectKind.ExhaustRandomHand) await CardCmd.Exhaust(choice, selected);
                            else await CardPileCmd.Add(selected, PileType.Hand);
                        }
                        break;
                    default: await ApplyPower(source, choice, effect.Kind, target, amount); break;
                }
            }
        }
    }

    internal static bool MeetsCondition(EffectCondition condition, Creature self, Creature target) => condition switch
    {
        EffectCondition.None => true,
        EffectCondition.SelfHasBlock => self.Block > 0,
        EffectCondition.SelfHpBelowHalf => (long)self.CurrentHp * 2 < self.MaxHp,
        EffectCondition.TargetWeak => target.GetPowerAmount<WeakPower>() > 0,
        EffectCondition.TargetVulnerable => target.GetPowerAmount<VulnerablePower>() > 0,
        _ => throw new InvalidOperationException()
    };

    private static decimal ResolveAmount(CardEffect effect, decimal baseAmount, NeowGeneratedCard source, Creature target)
    {
        int units = effect.Scaling switch
        {
            EffectScaling.None => 0,
            EffectScaling.SelfBlock => source.Owner.Creature.Block,
            EffectScaling.HandSize => PileType.Hand.GetPile(source.Owner).Cards.Count,
            EffectScaling.DiscardSize => PileType.Discard.GetPile(source.Owner).Cards.Count,
            EffectScaling.ExhaustSize => PileType.Exhaust.GetPile(source.Owner).Cards.Count,
            EffectScaling.TargetPoison => target.GetPowerAmount<PoisonPower>(),
            EffectScaling.SelfStars => source.Owner.PlayerCombatState?.Stars ?? 0,
            _ => throw new InvalidOperationException()
        };
        return EffectRules.ResolveAmount(effect, baseAmount, units);
    }

    private static Task ApplyPower(NeowGeneratedCard source, PlayerChoiceContext choice, EffectKind kind, Creature target, decimal amount) => kind switch
    {
        EffectKind.Strength => PowerCmd.Apply<StrengthPower>(choice, target, amount, source.Owner.Creature, source),
        EffectKind.Dexterity => PowerCmd.Apply<DexterityPower>(choice, target, amount, source.Owner.Creature, source),
        EffectKind.Weak => PowerCmd.Apply<WeakPower>(choice, target, amount, source.Owner.Creature, source),
        EffectKind.Vulnerable => PowerCmd.Apply<VulnerablePower>(choice, target, amount, source.Owner.Creature, source),
        EffectKind.Poison => PowerCmd.Apply<PoisonPower>(choice, target, amount, source.Owner.Creature, source),
        _ => throw new InvalidOperationException()
    };
}
