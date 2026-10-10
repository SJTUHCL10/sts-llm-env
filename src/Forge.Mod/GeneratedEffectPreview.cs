using Forge.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
namespace Forge.Mod;

internal static class GeneratedEffectPreview
{
    internal static bool CanResolve(NeowGeneratedCard card, int index) => index < card.ActiveForm.Immediate.Length
        && card.Owner?.PlayerCombatState is not null && card.Pile?.Type is PileType.Hand or PileType.Play;
    // Listener rewards come from a power, even when a Skill installed the listener.
    internal static ValueProp BlockProps(bool triggered) => triggered ? ValueProp.Unpowered : ValueProp.Move;
    internal static ValueProp DamageProps(CardEffect effect) => ValueProp.Move;
    internal static decimal EnchantBlock(NeowGeneratedCard card, decimal amount)
    {
        if (card.Enchantment is { } enchantment) { amount += enchantment.EnchantBlockAdditive(amount); amount *= enchantment.EnchantBlockMultiplicative(amount); }
        return Math.Max(0, amount);
    }
    internal static decimal Modify(NeowGeneratedCard card, CardEffect effect, decimal amount, Creature? target, CardPreviewMode mode, bool hooks, bool triggered)
    {
        if (effect.Kind == EffectKind.Damage)
        {
            var props = !triggered && card.Type == CardType.Attack ? ValueProp.Move : ValueProp.Unpowered;
            var dealer = effect.Actor == "osty" ? card.Owner?.Osty : card.Owner?.Creature;
            if (!hooks && !triggered && card.Enchantment is { } enchantment)
            { amount += enchantment.EnchantDamageAdditive(amount, props); amount *= enchantment.EnchantDamageMultiplicative(amount, props); }
            if (hooks) amount = Hook.ModifyDamage(card.Owner!.RunState, card.CombatState, target, dealer, amount, props, triggered ? null : card, null, ModifyDamageHookType.All, mode, out _);
        }
        else if (effect.Kind == EffectKind.Block)
        {
            if (!triggered) amount = EnchantBlock(card, amount);
            if (hooks) amount = Hook.ModifyBlock(card.CombatState!, card.Owner.Creature, amount, BlockProps(triggered), triggered ? null : card, null, out _);
        }
        return effect.Kind is EffectKind.ApplyPower or EffectKind.OrbSlots ? amount : Math.Max(0, amount);
    }
    internal static void Update(NeowGeneratedCard card, int index, DynamicVar variable, CardPreviewMode mode, Creature? target, bool runGlobalHooks)
    {
        var effect = card.ActiveForm.AllEffects[index];
        bool triggered = index >= card.ActiveForm.Immediate.Length;
        variable.EnchantedValue = Modify(card, effect, variable.BaseValue, target, mode, false, triggered);
        decimal amount = variable.BaseValue;
        if (runGlobalHooks && card.Owner?.PlayerCombatState is not null) amount = Resolve(card, index, target, mode);
        variable.PreviewValue = Modify(card, effect, amount, target, mode, runGlobalHooks, triggered);
    }
    // Deterministic preceding resource effects only. Do not consume RNG, choose cards, channel or apply powers in previews.
    private static decimal Resolve(NeowGeneratedCard card, int index, Creature? target, CardPreviewMode mode)
    {
        var owner = card.Owner;
        var state = owner.PlayerCombatState!;
        int hand = PileType.Hand.GetPile(owner).Cards.Count, stars = state.Stars, energy = state.Energy;
        decimal block = owner.Creature.Block;
        bool inHand = card.Pile?.Type == PileType.Hand;
        int paidEnergy = card.EnergyCost.CostsX ? energy : Math.Min(energy, card.EnergyCost.GetWithModifiers(CostModifiers.All));
        int paidStars = card.HasStarCostX ? stars : Math.Min(stars, Math.Max(0, card.GetStarCostWithModifiers()));
        if (inHand) { hand--; stars -= paidStars; energy -= paidEnergy; }
        var context = new GeneratedExecutionContext(card, new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext());
        int Read(NumberExpression n) => n.Stat switch
        {
            "hand_size" => hand, "stars" => stars, "energy" => energy,
            "paid_energy" => paidEnergy, "paid_stars" => paidStars,
            "block" when n.Of is null or "self" => (int)block,
            _ => context.Read(n, target)
        };
        decimal Amount(CardEffect effect, int slot) => effect.Amount?.Value is not null ? card.DynamicVars[$"E{slot}"].BaseValue
            : card.DynamicVars[$"E{slot}"].BaseValue + EffectRules.Evaluate(effect.Amount, Read);
        if (inHand && index < card.ActiveForm.Immediate.Length)
            for (int i = 0; i < index; i++)
            {
                var preceding = card.ActiveForm.Immediate[i];
                if (!EffectRules.Matches(preceding.Condition, Read)) continue;
                int repetitions = Math.Max(0, EffectRules.Evaluate(preceding.Repeat, Read, 1));
                if (repetitions > 4096) break;
                for (int r = 0; r < repetitions; r++)
                {
                    decimal amount = Math.Max(0, Amount(preceding, i));
                    switch (preceding.Kind)
                    {
                        case EffectKind.Block when preceding.Target?.Ref is null or "self": block = Math.Min(999999999m, block + decimal.Floor(Modify(card, preceding, amount, target, mode, true, false))); break;
                        case EffectKind.GainStars: stars = (int)Math.Min(int.MaxValue, stars + amount); break;
                        case EffectKind.GainEnergy: energy = (int)Math.Min(int.MaxValue, energy + amount); break;
                    }
                }
            }
        return Amount(card.ActiveForm.AllEffects[index], index);
    }
}
internal interface IGeneratedPreviewVar { void SetUpgradeHighlight(bool value); }
internal sealed class GeneratedDamageVar(string name, decimal amount, CardEffect effect, int index) : DamageVar(name, amount, GeneratedEffectPreview.DamageProps(effect)), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) => GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}
internal sealed class GeneratedBlockVar(string name, decimal amount, int index, bool triggered) : BlockVar(name, amount, GeneratedEffectPreview.BlockProps(triggered)), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) => GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}
internal sealed class GeneratedAmountVar(string name, decimal amount, int index) : DynamicVar(name, amount), IGeneratedPreviewVar
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) => GeneratedEffectPreview.Update((NeowGeneratedCard)card, index, this, previewMode, target, runGlobalHooks);
    public void SetUpgradeHighlight(bool value) => WasJustUpgraded = value;
}
