using Forge.Core;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;
namespace Forge.Mod;

internal sealed class GeneratedExecutionContext
{
    internal NeowGeneratedCard Source { get; }
    internal PlayerChoiceContext Choice { get; }
    internal CardPlay? Play { get; }
    internal CardModel? EventCard { get; init; }
    internal CardModel? OriginCard { get; init; }
    internal Creature? EventTarget { get; init; }
    internal int EventAmount { get; init; }
    internal bool Triggered { get; init; }
    internal int DamageDealt { get; set; }
    internal decimal[] Amounts { get; }
    internal int PaidEnergy { get; init; }
    internal int PaidStars { get; init; }
    internal Dictionary<string, CardModel[]> Selections { get; } = new(StringComparer.Ordinal);
    internal GeneratedExecutionContext(NeowGeneratedCard source, PlayerChoiceContext choice, CardPlay? play = null, decimal[]? amounts = null)
    {
        Source = source; Choice = choice; Play = play;
        PaidEnergy = play?.Resources.EnergySpent ?? 0; PaidStars = play?.Resources.StarsSpent ?? 0;
        Amounts = amounts ?? source.ActiveForm.AllEffects.Select((_, i) => source.DynamicVars[$"E{i}"].BaseValue).ToArray();
    }
    internal int Read(NumberExpression n, Creature? target = null)
    {
        var owner = Source.Owner;
        var creature = n.Of switch { "osty" => owner.Osty, "target" => target ?? EventTarget ?? Play?.Target, _ => owner.Creature };
        return n.Stat switch
        {
            "hp" => creature?.CurrentHp ?? 0, "max_hp" => creature?.MaxHp ?? 0, "block" => creature?.Block ?? 0,
            "power" => checked((int)(creature?.Powers.FirstOrDefault(p => p.Id == NativeMechanics.Power(n.Id!).Id)?.Amount ?? 0)),
            "energy" => owner.PlayerCombatState?.Energy ?? 0, "stars" => owner.PlayerCombatState?.Stars ?? 0,
            "hand_size" => PileType.Hand.GetPile(owner).Cards.Count, "draw_size" => PileType.Draw.GetPile(owner).Cards.Count,
            "discard_size" => PileType.Discard.GetPile(owner).Cards.Count, "exhaust_size" => PileType.Exhaust.GetPile(owner).Cards.Count,
            "orb_count" => owner.PlayerCombatState?.OrbQueue.Orbs.Count ?? 0, "orb_capacity" => owner.PlayerCombatState?.OrbQueue.Capacity ?? 0,
            "paid_energy" => PaidEnergy, "paid_stars" => PaidStars, "event_amount" => EventAmount, "damage_dealt" => DamageDealt,
            _ => throw new FormatException("Unknown stat.")
        };
    }
    internal int Number(NumberExpression? value, int fallback = 0, Creature? target = null) => EffectRules.Evaluate(value, n => Read(n, target), fallback);
    internal bool Matches(EffectCondition? condition, Creature? target = null) => EffectRules.Matches(condition, n => Read(n, target));
    internal decimal Amount(CardEffect effect, int index, Creature? target = null) => effect.Amount?.Value is not null ? Amounts[index] : Amounts[index] + Number(effect.Amount, target: target);
}

internal static class GeneratedEffectExecutor
{
    private sealed class Budget { internal int Remaining = 4096; }
    private static readonly AsyncLocal<Budget?> Resolution = new();
    private static void Step()
    {
        if (Resolution.Value is { } budget && --budget.Remaining < 0) throw new InvalidOperationException("Generated effect resolution exceeded its operation limit.");
    }
    internal static async Task ExecuteGroup(GeneratedExecutionContext context, CardEffect[] effects, int offset)
    {
        bool root = Resolution.Value is null;
        if (root) Resolution.Value = new();
        try
        {
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                int repetitions = Math.Max(0, context.Number(effect.Repeat, 1));
                for (int r = 0; r < repetitions; r++)
                {
                    Step();
                    if (CombatManager.Instance.IsEnding || !context.Source.Owner.Creature.IsAlive) return;
                    await Execute(context, effect, offset + i);
                }
            }
        }
        finally { if (root) Resolution.Value = null; }
    }
    private static async Task Execute(GeneratedExecutionContext c, CardEffect e, int index)
    {
        var source = c.Source; var owner = source.Owner; var combat = owner.Creature.CombatState!;
        if (e.Kind is EffectKind.Discard or EffectKind.Exhaust or EffectKind.Move or EffectKind.Select or EffectKind.Upgrade or EffectKind.Copy or EffectKind.Transform or EffectKind.Play or EffectKind.AddKeyword or EffectKind.RemoveKeyword or EffectKind.SetCost)
        {
            if (!c.Matches(e.Condition)) return;
            var cards = await SelectCards(c, e.Target!, e.Kind);
            if (e.Kind == EffectKind.Select) { c.Selections[e.As!] = cards; return; }
            if (e.Kind == EffectKind.Transform)
            {
                var replacements = new List<CardTransformation>();
                foreach (var card in cards.Where(card => card.IsTransformable && card.CombatState == combat))
                {
                    Step();
                    var replacement = await Create(c, e.Card!);
                    if (replacement is not null) replacements.Add(new(card, replacement));
                }
                if (replacements.Count > 0) await CardCmd.Transform(replacements, null, CardPreviewStyle.HorizontalLayout);
                return;
            }
            if (e.Kind == EffectKind.Discard)
            {
                foreach (var card in cards) Step();
                await CardCmd.Discard(c.Choice, cards.Where(card => card.CombatState == combat));
                return;
            }
            foreach (var card in cards)
            {
                Step();
                if (CombatManager.Instance.IsEnding) return;
                // References retain identity; they never reach into the permanent deck.
                if (card.CombatState != combat) continue;
                switch (e.Kind)
                {
                    case EffectKind.Exhaust: await CardCmd.Exhaust(c.Choice, card); break;
                    case EffectKind.Move: await CardPileCmd.Add(card, NativeMechanics.Pile(e.To!.Value), Position(e)); break;
                    case EffectKind.Upgrade: if (card.IsUpgradable) CardCmd.Upgrade(card); break;
                    case EffectKind.Copy:
                        for (int n = 0; n < Math.Max(0, c.Number(e.Count, 1)); n++)
                        {
                            Step();
                            await CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), NativeMechanics.Pile(e.To!.Value), owner, Position(e));
                        }
                        break;
                    case EffectKind.Play: await CardCmd.AutoPlay(c.Choice, card, null); break;
                    case EffectKind.AddKeyword:
                        if (e.Until == LifetimeKind.Turn && e.Keyword == ForgeKeyword.Sly) CardCmd.ApplySingleTurnSly(card);
                        else if (e.Until == LifetimeKind.Turn && e.Keyword == ForgeKeyword.Retain) CardCmd.ApplySingleTurnRetain(card);
                        else CardCmd.ApplyKeyword(card, NeowGeneratedCard.NativeKeyword(e.Keyword!.Value));
                        break;
                    case EffectKind.RemoveKeyword: CardCmd.RemoveKeyword(card, NeowGeneratedCard.NativeKeyword(e.Keyword!.Value)); break;
                    case EffectKind.SetCost:
                        int cost = Math.Max(0, c.Number(e.Amount));
                        if (e.Until == LifetimeKind.Turn) card.EnergyCost.SetThisTurn(cost); else card.EnergyCost.SetThisCombat(cost);
                        break;
                }
            }
            return;
        }
        if (e.Kind is EffectKind.Evoke or EffectKind.OrbPassive)
        {
            if (!c.Matches(e.Condition)) return;
            var orbs = owner.PlayerCombatState?.OrbQueue.Orbs.ToArray() ?? [];
            var selected = e.Target?.Ref == "all_orbs" ? orbs : e.Target?.Ref == "last_orb" ? orbs.TakeLast(1).ToArray() : orbs.Take(1).ToArray();
            foreach (var orb in selected)
            {
                Step();
                if (!owner.PlayerCombatState!.OrbQueue.Orbs.Contains(orb)) continue;
                if (e.Kind == EffectKind.OrbPassive) await OrbCmd.Passive(c.Choice, orb, null);
                else await NativeMechanics.Evoke(c.Choice, owner, orb, e.Remove != false);
            }
            return;
        }
        var enemies = combat.HittableEnemies.ToArray();
        Creature[] targets = e.Target?.Ref switch
        {
            null or "self" => [owner.Creature], "osty" => owner.Osty is { IsAlive: true } osty ? [osty] : [],
            "enemy" => c.Play?.Target is { IsAlive: true, IsHittable: true } enemy ? [enemy] : [],
            "event.target" => c.EventTarget is { IsAlive: true } eventTarget ? [eventTarget] : [],
            "all_enemies" => enemies, "random_enemy" => enemies.Length > 0 ? [owner.RunState.Rng.CombatTargets.NextItem(enemies)!] : [], _ => []
        };
        // A uniform AoE remains a single native attack transaction.
        if (e.Kind == EffectKind.Damage && e.Target?.Ref == "all_enemies" && targets.Length > 0
            && targets.All(t => c.Matches(e.Condition, t)) && targets.Select(t => c.Amount(e, index, t)).Distinct().Count() == 1)
        {
            await Damage(c, e, Math.Max(0, c.Amount(e, index, targets[0])), null, true);
            return;
        }
        foreach (var target in targets)
        {
            if (!target.IsAlive || !c.Matches(e.Condition, target)) continue;
            decimal amount = c.Amount(e, index, target);
            if (e.Kind is not (EffectKind.ApplyPower or EffectKind.OrbSlots)) amount = Math.Max(0, amount);
            switch (e.Kind)
            {
                case EffectKind.Damage: await Damage(c, e, amount, target); break;
                case EffectKind.Block: await CreatureCmd.GainBlock(target, c.Triggered ? amount : GeneratedEffectPreview.EnchantBlock(source, amount), ValueProp.Move, c.Play); break;
                case EffectKind.Draw: await CardPileCmd.Draw(c.Choice, amount, owner); break;
                case EffectKind.GainEnergy: await PlayerCmd.GainEnergy(amount, owner); break;
                case EffectKind.GainStars: await PlayerCmd.GainStars(amount, owner); break;
                case EffectKind.ApplyPower:
                    var power = e.Until == LifetimeKind.Turn ? NativeMechanics.TemporaryPower(e.Power!, amount < 0) : NativeMechanics.Power(e.Power!);
                    await PowerCmd.Apply(c.Choice, power.ToMutable(), target, e.Until == LifetimeKind.Turn ? Math.Abs(amount) : amount, owner.Creature, source);
                    break;
                case EffectKind.Summon: await OstyCmd.Summon(c.Choice, owner, amount, source); break;
                case EffectKind.Forge: await ForgeCmd.Forge((int)amount, owner, source); break;
                case EffectKind.Channel:
                    for (int n = 0; n < amount; n++) { Step(); await NativeMechanics.Channel(c.Choice, owner, e.Orb!); }
                    break;
                case EffectKind.OrbSlots: if (amount >= 0) await OrbCmd.AddSlots(owner, (int)amount); else OrbCmd.RemoveSlots(owner, -(int)amount); break;
                case EffectKind.Heal: await CreatureCmd.Heal(target, amount); break;
                case EffectKind.LoseHp: await CreatureCmd.Damage(c.Choice, target, amount, ValueProp.Unblockable | ValueProp.Unpowered, owner.Creature, source, c.Play); break;
                case EffectKind.CreateCard:
                    for (int n = 0; n < Math.Max(0, c.Number(e.Count, 1)); n++)
                    {
                        Step();
                        var generated = await Create(c, e.Card!);
                        if (generated is not null) await CardPileCmd.AddGeneratedCardToCombat(generated, NativeMechanics.Pile(e.To!.Value), owner, Position(e));
                    }
                    break;
            }
        }
    }
    private static async Task Damage(GeneratedExecutionContext c, CardEffect e, decimal amount, Creature? target, bool all = false)
    {
        var source = c.Source; var combat = source.Owner.Creature.CombatState!;
        var attacker = e.Actor == "osty" ? source.Owner.Osty : source.Owner.Creature;
        if (attacker is not { IsAlive: true }) return;
        if (!c.Triggered && source.Type == CardType.Attack)
        {
            var command = e.Actor == "osty" ? DamageCmd.Attack(amount).FromOsty(attacker, source, c.Play) : DamageCmd.Attack(amount).FromCard(source, c.Play);
            if (all) command.TargetingAllOpponents(combat); else command.Targeting(target!);
            await command.WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(c.Choice);
            c.DamageDealt = checked((int)command.Results.SelectMany(r => r).Sum(r => r.TotalDamage));
        }
        else
        {
            var results = all ? await CreatureCmd.Damage(c.Choice, combat.HittableEnemies.ToArray(), amount, ValueProp.Unpowered, attacker)
                : await CreatureCmd.Damage(c.Choice, target!, amount, ValueProp.Unpowered, attacker);
            c.DamageDealt = checked((int)results.Sum(r => r.TotalDamage));
        }
    }
    private static CardPilePosition Position(CardEffect e) => e.Position switch { "random" => CardPilePosition.Random, "bottom" => CardPilePosition.Bottom, _ => e.To == CardPileName.Draw ? CardPilePosition.Top : CardPilePosition.Bottom };
    private static async Task<CardModel[]> SelectCards(GeneratedExecutionContext c, EffectTarget target, EffectKind kind)
    {
        if (target.Ref == "this_card") return c.Triggered ? c.OriginCard is null ? [] : [c.OriginCard] : [c.Source];
        if (target.Ref == "event.card") return c.EventCard is null ? [] : [c.EventCard];
        if (target.Ref?.StartsWith("selected:", StringComparison.Ordinal) == true) return c.Selections.GetValueOrDefault(target.Ref[9..], []);
        var owner = c.Source.Owner;
        var pile = NativeMechanics.Pile(target.Pile!.Value).GetPile(owner);
        bool Filter(CardModel card) => card != c.Source && NativeMechanics.Matches(card, target.Filter)
            && (kind != EffectKind.Upgrade || card.IsUpgradable) && (kind != EffectKind.Transform || card.IsTransformable);
        var candidates = pile.Cards.Where(Filter).ToArray();
        int count = Math.Min(candidates.Length, Math.Max(0, c.Number(target.Count, 1)));
        if (target.Pick == SelectionMode.All) return candidates;
        if (count == 0) return [];
        if (target.Pick == SelectionMode.First) return candidates.Take(count).ToArray();
        if (target.Pick == SelectionMode.Last) return candidates.TakeLast(count).Reverse().ToArray();
        if (target.Pick == SelectionMode.Random)
        {
            var available = candidates.ToList(); var selected = new List<CardModel>();
            for (int n = 0; n < count; n++) { Step(); var card = owner.RunState.Rng.CombatCardSelection.NextItem(available)!; available.Remove(card); selected.Add(card); }
            return selected.ToArray();
        }
        var prompt = kind switch { EffectKind.Discard => CardSelectorPrefs.DiscardSelectionPrompt, EffectKind.Exhaust => CardSelectorPrefs.ExhaustSelectionPrompt, EffectKind.Upgrade => CardSelectorPrefs.UpgradeSelectionPrompt, EffectKind.Transform => CardSelectorPrefs.TransformSelectionPrompt, _ => new MegaCrit.Sts2.Core.Localization.LocString("card_selection", "HAND_TO_DRAW") };
        var prefs = new CardSelectorPrefs(prompt, target.UpTo == true ? 0 : count, count) { Cancelable = target.UpTo == true };
        var result = pile.Type == PileType.Hand
            ? kind == EffectKind.Discard ? await CardSelectCmd.FromHandForDiscard(c.Choice, owner, prefs, Filter, c.Source)
                : await CardSelectCmd.FromHand(c.Choice, owner, prefs, Filter, c.Source)
            : await CardSelectCmd.FromCombatPile(c.Choice, pile, owner, prefs, Filter);
        return result.Where(card => candidates.Contains(card) && ReferenceEquals(card.Pile, pile)).Distinct().Take(count).ToArray();
    }
    private static async Task<CardModel?> Create(GeneratedExecutionContext c, CardSource specification)
    {
        var owner = c.Source.Owner; var combat = owner.Creature.CombatState!;
        CardModel? card;
        if (specification.Id is { } id) card = combat.CreateCard(NativeMechanics.Card(id), owner);
        else
        {
            var pool = specification.Pool == "colorless" ? ModelDb.CardPool<ColorlessCardPool>() : owner.Character.CardPool;
            var candidates = pool.GetUnlockedCards(owner.UnlockState, owner.RunState.CardMultiplayerConstraint)
                .Where(card => card.CanBeGeneratedInCombat && NativeMechanics.Matches(card, specification.Filter)).ToArray();
            if (candidates.Length == 0) return null;
            if (specification.Pick == SelectionMode.Choose)
            {
                var offered = CardFactory.GetDistinctForCombat(owner, candidates, Math.Min(candidates.Length, specification.Options ?? 3), owner.RunState.Rng.CombatCardGeneration).ToArray();
                if (specification.Upgraded == true) foreach (var option in offered) if (option.IsUpgradable) CardCmd.Upgrade(option);
                card = await CardSelectCmd.FromChooseACardScreen(c.Choice, offered, owner, canSkip: false);
            }
            else card = CardFactory.GetForCombat(owner, candidates, 1, owner.RunState.Rng.CombatCardGeneration).Single();
        }
        if (card is not null && specification.Upgraded == true && card.IsUpgradable) CardCmd.Upgrade(card);
        return card;
    }
}
