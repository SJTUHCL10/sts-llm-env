using Forge.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Forge.Mod;

internal static class GeneratedEffectPreview
{
    internal static ValueProp DamageProps(CardEffect effect) => EffectRules.IsAttackDamage(effect) ? ValueProp.Move : ValueProp.Unpowered;

    internal static decimal Modify(NeowGeneratedCard card, CardEffect effect, decimal amount, Creature? target,
        CardPreviewMode mode, bool runGlobalHooks)
    {
        if (effect.Kind == EffectKind.Damage)
        {
            if (!runGlobalHooks && EffectRules.IsAttackDamage(effect) && card.Enchantment is { } enchantment)
            {
                amount += enchantment.EnchantDamageAdditive(amount, DamageProps(effect));
                amount *= enchantment.EnchantDamageMultiplicative(amount, DamageProps(effect));
            }
            if (runGlobalHooks)
                amount = Hook.ModifyDamage(card.Owner.RunState, card.CombatState, target, card.Owner.Creature, amount,
                    DamageProps(effect), EffectRules.IsAttackDamage(effect) ? card : null, null, ModifyDamageHookType.All, mode, out _);
        }
        else if (effect.Kind == EffectKind.Block)
        {
            if (effect.Trigger == EffectTrigger.OnPlay) amount = EnchantBlock(card, amount);
            if (runGlobalHooks)
                amount = Hook.ModifyBlock(card.CombatState!, card.Owner.Creature, amount, ValueProp.Move,
                    effect.Trigger == EffectTrigger.OnPlay ? card : null, null, out _);
        }
        return Math.Max(0, amount);
    }

    internal static decimal EnchantBlock(NeowGeneratedCard card, decimal amount)
    {
        if (card.Enchantment is { } enchantment)
        {
            amount += enchantment.EnchantBlockAdditive(amount);
            amount *= enchantment.EnchantBlockMultiplicative(amount);
        }
        return Math.Max(0, amount);
    }

    internal static void Update(NeowGeneratedCard card, int index, DynamicVar variable,
        CardPreviewMode mode, Creature? target, bool runGlobalHooks)
    {
        var effect = card.Definition.Effects[index];
        variable.EnchantedValue = Modify(card, effect, variable.BaseValue, target, mode, false);
        decimal amount = variable.BaseValue;
        if (runGlobalHooks) amount = ResolveBeforePlay(card, index, target, mode);
        variable.PreviewValue = Modify(card, effect, amount, target, mode, runGlobalHooks);
    }

    // Forecast deterministic resource/pile changes only. Never mutate game models or consume RNG for a preview.
    private static decimal ResolveBeforePlay(NeowGeneratedCard card, int index, Creature? target, CardPreviewMode mode)
    {
        var owner = card.Owner;
        var combat = owner.PlayerCombatState;
        if (combat is null) return card.DynamicVars[$"E{index}"].BaseValue;
        int stars = combat.Stars;
        int hand = PileType.Hand.GetPile(owner).Cards.Count;
        int discard = PileType.Discard.GetPile(owner).Cards.Count;
        int exhaust = PileType.Exhaust.GetPile(owner).Cards.Count;
        int draw = PileType.Draw.GetPile(owner).Cards.Count;
        decimal block = owner.Creature.Block;
        bool inHand = card.Pile?.Type == PileType.Hand;
        if (inHand)
        {
            hand = Math.Max(0, hand - 1);
            stars = Math.Max(0, stars - Math.Max(0, card.GetStarCostWithModifiers()));
        }
        int Units(CardEffect effect) => effect.Scaling switch
        {
            EffectScaling.None => 0, EffectScaling.SelfStars => stars,
            EffectScaling.SelfBlock => (int)block, EffectScaling.HandSize => hand,
            EffectScaling.DiscardSize => discard, EffectScaling.ExhaustSize => exhaust,
            EffectScaling.TargetPoison => target?.GetPowerAmount<PoisonPower>() ?? 0,
            _ => throw new InvalidOperationException()
        };
        if (inHand)
        {
            int stop = card.Definition.Effects[index].Trigger == EffectTrigger.OnPlay ? index : card.Definition.Effects.Length;
            for (int i = 0; i < stop; i++)
            {
                var previous = card.Definition.Effects[i];
                if (previous.Kind is not (EffectKind.Stars or EffectKind.Block or EffectKind.Draw or EffectKind.DiscardRandomHand
                    or EffectKind.ExhaustRandomHand or EffectKind.ReturnRandomDiscard)) continue;
                if (previous.Trigger != EffectTrigger.OnPlay || previous.Condition == EffectCondition.SelfHasBlock && block <= 0
                    || previous.Condition is EffectCondition.TargetWeak or EffectCondition.TargetVulnerable && target is null
                    || previous.Condition != EffectCondition.SelfHasBlock
                        && !GeneratedEffectExecutor.MeetsCondition(previous.Condition, owner.Creature, target ?? owner.Creature)) continue;
                decimal initialAmount = EffectRules.ResolveAmount(previous, card.DynamicVars[$"E{i}"].BaseValue, Units(previous));
                if (previous.Kind == EffectKind.Stars && previous.Scaling != EffectScaling.SelfStars)
                {
                    stars = (int)Math.Min(int.MaxValue, stars + initialAmount * previous.Repeat);
                    continue;
                }
                if (previous.Kind == EffectKind.Block && previous.Scaling != EffectScaling.SelfBlock)
                {
                    // Creature.GainBlockInternal clamps to this native v0.111.0 ceiling after every gain.
                    block = Math.Min(999999999m, block + decimal.Floor(Modify(card, previous, initialAmount, target, mode, true)) * previous.Repeat);
                    continue;
                }
                for (int repetition = 0; repetition < previous.Repeat; repetition++)
                {
                    var before = (stars, block, hand, draw, discard, exhaust);
                    decimal amount = EffectRules.ResolveAmount(previous, card.DynamicVars[$"E{i}"].BaseValue, Units(previous));
                    int moved;
                    switch (previous.Kind)
                    {
                        case EffectKind.Stars: stars = (int)Math.Min(int.MaxValue, stars + amount); break;
                        case EffectKind.Block: block = Math.Min(999999999m, block + decimal.Floor(Modify(card, previous, amount, target, mode, true))); break;
                        case EffectKind.Draw:
                            moved = (int)Math.Min(amount, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), draw + discard));
                            if (moved > draw) { draw += discard; discard = 0; }
                            hand += moved; draw -= moved;
                            break;
                        case EffectKind.DiscardRandomHand:
                            moved = (int)Math.Min(amount, hand); hand -= moved; discard += moved; break;
                        case EffectKind.ExhaustRandomHand:
                            moved = (int)Math.Min(amount, hand); hand -= moved; exhaust += moved; break;
                        case EffectKind.ReturnRandomDiscard:
                            moved = (int)Math.Min(amount, Math.Min(discard, Math.Max(0, CardPile.MaxCardsInHand - hand)));
                            hand += moved; discard -= moved; break;
                    }
                    if (before == (stars, block, hand, draw, discard, exhaust)) break;
                }
            }
        }
        var current = card.Definition.Effects[index];
        return EffectRules.ResolveAmount(current, card.DynamicVars[$"E{index}"].BaseValue, Units(current));
    }
}

internal interface IGeneratedPreviewVar
{
    void SetUpgradeHighlight(bool value);
}

internal sealed class GeneratedDamageVar(string name, decimal amount, CardEffect effect, int index)
    : DamageVar(name, amount, GeneratedEffectPreview.DamageProps(effect)), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) =>
        GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}

internal sealed class GeneratedBlockVar(string name, decimal amount, int index) : BlockVar(name, amount, ValueProp.Move), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) =>
        GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}

internal sealed class GeneratedAmountVar(string name, decimal amount, int index) : DynamicVar(name, amount), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) =>
        GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}
