using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
namespace Forge.Mod;

public sealed record GeneratedPowerSnapshot
{
    public required CardDefinition Definition { get; init; }
    public required decimal[] Amounts { get; init; }
    public required EffectTriggerState State { get; init; }
    public bool SourceUpgraded { get; init; }
    public int PaidEnergy { get; init; }
    public int PaidStars { get; init; }
    public required string SourceInstanceKey { get; init; }
}
public sealed class GeneratedEffectPower : PowerModel
{
    private static readonly AsyncLocal<int> TriggerDepth = new();
    private GeneratedPowerSnapshot? _snapshot;
    private CardPlay? _armingPlay;
    private bool _firing;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    private CardForm Form => _snapshot!.Definition.Form(_snapshot.SourceUpgraded);
    protected override IEnumerable<IHoverTip> ExtraHoverTips => _snapshot is null ? [] : NeowGeneratedCard.EffectHoverTips(Form);
    [SavedProperty]
    public string RuntimePayload
    {
        get => _snapshot is null ? "" : Wire.Encode(_snapshot);
        set
        {
            AssertMutable();
            if (value.Length == 0) { _snapshot = null; return; }
            var snapshot = Wire.Decode<GeneratedPowerSnapshot>(value);
            CardValidator.Validate(snapshot.Definition);
            var form = snapshot.Definition.Form(snapshot.SourceUpgraded);
            if (form.Listeners.Length == 0 || snapshot.Amounts is null || snapshot.Amounts.Length != form.AllEffects.Length
                || snapshot.Amounts.Any(n => n < int.MinValue || n > int.MaxValue) || snapshot.State is null || snapshot.PaidEnergy < 0 || snapshot.PaidStars < 0
                || !Guid.TryParseExact(snapshot.SourceInstanceKey, "N", out _))
                throw new FormatException("Invalid captured rule state.");
            EffectTriggerRuntime.Validate(form, snapshot.State);
            _snapshot = snapshot;
        }
    }
    internal void Configure(NeowGeneratedCard card, CardPlay? play = null)
    {
        RuntimePayload = Wire.Encode(new GeneratedPowerSnapshot
        {
            Definition = card.Definition, Amounts = card.ActiveForm.AllEffects.Select((_, i) => card.DynamicVars[$"E{i}"].BaseValue).ToArray(),
            State = EffectTriggerRuntime.Create(card.ActiveForm), SourceUpgraded = card.IsUpgraded,
            SourceInstanceKey = card.InstanceKey,
            PaidEnergy = play?.Resources.EnergySpent ?? 0, PaidStars = play?.Resources.StarsSpent ?? 0
        });
        _armingPlay = play;
    }
    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        if (_snapshot is not null) _snapshot = Wire.Decode<GeneratedPowerSnapshot>(Wire.Encode(_snapshot));
        _firing = false; _armingPlay = null;
    }
    public override LocString Title => Localize("title", _snapshot?.Definition.Name ?? "涅奥的造物");
    public override LocString Description => Localize("description", RuntimeDescriptionText(LocManager.Instance.Language is "zhs" or "zht"));
    internal string RuntimeDescriptionText(bool chinese)
    {
        if (_snapshot is null) return "";
        string prefix = EnergyIconHelper.GetPrefix(this);
        int offset = Form.ImmediateEffects.Length;
        var lines = new List<string>();
        for (int i = 0; i < Form.Listeners.Length; i++)
        {
            var rule = Form.Listeners[i];
            int index = offset;
            string Action(CardEffect effect)
            {
                string amount = effect.Amount?.Value is not null ? _snapshot.Amounts[index].ToString(System.Globalization.CultureInfo.InvariantCulture) : CardText.Number(effect.Amount, chinese, resource: (kind, value) => CardText.ResourceText(kind, value, prefix));
                index++;
                return CardText.RenderEffect(effect, chinese, amount, (kind, value) => CardText.ResourceText(kind, value, prefix), Form.Cost, nested: Action);
            }
            if (_snapshot.State.Remaining[i] != 0)
                lines.Add(CardText.RenderRule(rule, chinese, Action, combatIsImplicit: true,
                    resource: (kind, value) => CardText.ResourceText(kind, value, prefix), cost: Form.Cost));
            offset += CardEffect.Flatten(rule.Effects).Count();
        }
        return string.Join("\n", lines);
    }
    private static LocString Localize(string suffix, string text)
    {
        string key = "NEOWS_COMPANY." + AtomicStore.Key(text) + "." + suffix;
        LocManager.Instance.GetTable("powers").MergeWith(new Dictionary<string, string> { [key] = text });
        return new LocString("powers", key);
    }
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (_snapshot is not null && Owner.Side == side && participants.Contains(Owner)) EffectTriggerRuntime.BeginTurn(Form, _snapshot.State);
        return Task.CompletedTask;
    }
    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player) => player.Creature == Owner ? FireTurn(choiceContext, RuleEvent.TurnStart) : Task.CompletedTask;
    public override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants) => side == Owner.Side && participants.Contains(Owner) ? FireTurn(choiceContext, RuleEvent.TurnEnd) : Task.CompletedTask;
    public override async Task AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (_snapshot is null || side != Owner.Side || !participants.Contains(Owner)) return;
        EffectTriggerRuntime.EndTurn(Form, _snapshot.State); await RemoveIfExpired();
    }
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (ReferenceEquals(_armingPlay, cardPlay)) { _armingPlay = null; return Task.CompletedTask; }
        return Fire(choiceContext, new(RuleEvent.CardPlayed)
        {
            Actor = cardPlay.Player.Creature, Source = cardPlay.Player.Creature, SourceModel = cardPlay.Card, Card = cardPlay.Card, Play = cardPlay,
            Target = cardPlay.Target, Subject = cardPlay.Target, Targets = cardPlay.Target is { } target ? [target] : []
        });
    }
    private static GeneratedRuleEvent CardEvent(RuleEvent kind, CardModel card) => new(kind) { Card = card, Actor = card.Owner.Creature };
    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) => Fire(choiceContext, CardEvent(RuleEvent.CardDrawn, card) with { FromHandDraw = fromHandDraw });
    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card) => Fire(choiceContext, CardEvent(RuleEvent.CardDiscarded, card));
    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal) => Fire(choiceContext, CardEvent(RuleEvent.CardExhausted, card) with { CausedByEthereal = causedByEthereal });
    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => Fire(new BlockingPlayerChoiceContext(), CardEvent(RuleEvent.CardGenerated, card) with { Actor = creator?.Creature, Source = creator?.Creature });
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource) => Fire(choiceContext, new(RuleEvent.DamageReceived)
    {
        Actor = dealer, Source = dealer, SourceModel = cardSource, Subject = target, Target = dealer, Targets = [target], Card = cardSource,
        Amount = result.UnblockedDamage, TotalDamage = result.TotalDamage, UnblockedDamage = result.UnblockedDamage, Attack = props.IsPoweredAttack()
    });
    public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource) => Fire(choiceContext, new(RuleEvent.DamageGiven)
    {
        Actor = dealer, Source = dealer, SourceModel = cardSource, Subject = target, Target = target, Targets = [target], Card = cardSource,
        Amount = result.TotalDamage, TotalDamage = result.TotalDamage, UnblockedDamage = result.UnblockedDamage, Attack = props.IsPoweredAttack()
    });
    public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource) => amount > 0 ? Fire(new BlockingPlayerChoiceContext(), new(RuleEvent.BlockGained)
    {
        Actor = creature, Source = cardSource?.Owner?.Creature, SourceModel = cardSource, Subject = creature, Target = creature, Targets = [creature], Card = cardSource, Amount = amount
    }) : Task.CompletedTask;
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource) => amount != 0 ? Fire(choiceContext, new(RuleEvent.PowerAmountChanged)
    {
        Actor = applier, Source = applier, SourceModel = cardSource, Subject = power.Owner, Target = power.Owner, Targets = [power.Owner], Card = cardSource,
        Amount = amount, Power = GeneratedRuleEvent.PowerAlias(power), PowerType = power.GetTypeForAmount(amount).ToString().ToLowerInvariant(), Temporary = power is ITemporaryPower
    }) : Task.CompletedTask;
    public override Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
    {
        var results = command.Results.SelectMany(r => r).ToArray();
        var targets = results.Select(r => r.Receiver).Distinct().ToArray();
        return Fire(choiceContext, new(RuleEvent.AttackCompleted)
        {
            Actor = command.Attacker, Source = command.Attacker, SourceModel = command.ModelSource, Card = command.CardPlay?.Card, Play = command.CardPlay,
            Target = targets.FirstOrDefault(), Subject = targets.FirstOrDefault(), Targets = targets,
            Amount = results.Sum(r => r.TotalDamage), TotalDamage = results.Sum(r => r.TotalDamage), UnblockedDamage = results.Sum(r => r.UnblockedDamage), Attack = command.DamageProps.IsPoweredAttack()
        });
    }
    public override Task AfterSummon(PlayerChoiceContext choiceContext, Player summoner, decimal amount) => Fire(choiceContext, new(RuleEvent.Summoned) { Actor = summoner.Creature, Amount = amount });
    public override Task AfterForge(decimal amount, Player forger, AbstractModel? source) => amount > 0 ? Fire(new BlockingPlayerChoiceContext(), new(RuleEvent.Forged)
    {
        Actor = forger.Creature, Source = GeneratedRuleEvent.ModelOwner(source), SourceModel = source, Card = source as CardModel,
        Subject = forger.Creature, Target = forger.Creature, Targets = [forger.Creature], Amount = amount
    }) : Task.CompletedTask;
    public override Task AfterStarsGained(int amount, Player gainer) => ResourceEvent(RuleEvent.StarsGained, amount, gainer);
    public override Task AfterStarsSpent(int amount, Player spender) => ResourceEvent(RuleEvent.StarsSpent, amount, spender);
    private Task ResourceEvent(RuleEvent kind, int amount, Player player) => amount > 0 ? Fire(new BlockingPlayerChoiceContext(), new(kind)
    {
        Actor = player.Creature, Subject = player.Creature, Target = player.Creature, Targets = [player.Creature], Amount = amount
    }) : Task.CompletedTask;
    public override Task AfterOrbChanneled(PlayerChoiceContext choiceContext, Player player, OrbModel orb) => Fire(choiceContext, new(RuleEvent.OrbChanneled)
    {
        Actor = player.Creature, Source = player.Creature, SourceModel = orb, Orb = GeneratedRuleEvent.OrbAlias(orb)
    });
    public override Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets)
    {
        var affected = targets.Distinct().ToArray();
        return Fire(choiceContext, new(RuleEvent.OrbEvoked)
        {
            Actor = orb.Owner.Creature, Source = orb.Owner.Creature, SourceModel = orb, Orb = GeneratedRuleEvent.OrbAlias(orb),
            Target = affected.FirstOrDefault(), Subject = affected.FirstOrDefault(), Targets = affected
        });
    }
    private Task FireTurn(PlayerChoiceContext choice, RuleEvent kind) => Fire(choice, new GeneratedRuleEvent(kind) { Actor = Owner });
    private async Task Fire(PlayerChoiceContext choice, GeneratedRuleEvent occurrence)
    {
        if (_snapshot is null || _firing || TriggerDepth.Value >= 8 || !Owner.IsAlive || CombatManager.Instance.IsEnding || Owner.Player is null) return;
        var snapshot = _snapshot; var form = Form;
        var facts = occurrence.Facts(Owner);
        var eligible = form.Listeners.Select(rule => occurrence.Matches(rule.Trigger, Owner, facts) && (rule.Trigger.Filter is null || occurrence.Card is { } eventCard
            && (RuleEvents.IsCardEvent(occurrence.Kind) ? GeneratedEventFacts.MatchesCurrent(rule.Trigger, eventCard, Owner, CombatState) : NativeMechanics.Matches(eventCard, rule.Trigger.Filter)))).ToArray();
        if (!eligible.Any(match => match)) { await RemoveIfExpired(); return; }
        var source = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        source.DefinitionPayload = Wire.Encode(snapshot.Definition); source.Owner = Owner.Player;
        if (snapshot.SourceUpgraded) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
        var context = new GeneratedExecutionContext(source, choice, amounts: snapshot.Amounts)
        {
            Triggered = true, EffectSource = this, EventCard = occurrence.Card, EventTarget = occurrence.Target, EventAmount = occurrence.Amount,
            EventActor = occurrence.Actor, EventSource = occurrence.Source, EventSubject = occurrence.Subject, EventTargets = occurrence.Targets,
            EventTotalDamage = occurrence.TotalDamage, EventUnblockedDamage = occurrence.UnblockedDamage,
            EventPaidEnergy = occurrence.Play?.Resources.EnergySpent ?? 0, EventPaidStars = occurrence.Play?.Resources.StarsSpent ?? 0,
            OriginCard = Owner.Player.PlayerCombatState?.AllCards.OfType<NeowGeneratedCard>().FirstOrDefault(card => card.InstanceKey == snapshot.SourceInstanceKey),
            PaidEnergy = snapshot.PaidEnergy, PaidStars = snapshot.PaidStars
        };
        // Read ordinals before any payoff can append nested events.
        var ordinals = form.Listeners.Select((rule, i) => eligible[i] && rule.Trigger.Occurrence is not null ? GeneratedEventFacts.Ordinal(rule.Trigger, Owner, CombatState) : 1).ToArray();
        _firing = true; TriggerDepth.Value++; choice.PushModel(source);
        try
        {
            int offset = form.ImmediateEffects.Length;
            for (int i = 0; i < form.Listeners.Length; i++)
            {
                var rule = form.Listeners[i];
                context.Selections.Clear();
                if (eligible[i] && EffectTriggerRuntime.TryConsume(form, snapshot.State, i, occurrence.Kind, ordinals[i], context.Matches(rule.Condition)))
                    await GeneratedEffectExecutor.ExecuteGroup(context, rule.Effects, offset);
                offset += CardEffect.Flatten(rule.Effects).Count();
            }
        }
        finally { source.InvokeExecutionFinished(); choice.PopModel(source); TriggerDepth.Value--; _firing = false; }
        await RemoveIfExpired();
    }
    private Task RemoveIfExpired() => _snapshot is not null && EffectTriggerRuntime.IsExpired(_snapshot.State) ? PowerCmd.Remove(this) : Task.CompletedTask;
}

internal static class GeneratedEventFacts
{
    private sealed record Facts(ModelId Id, CardType Type, CardRarity Rarity, int Cost, bool Upgraded, CardKeyword[] Keywords, string[] Tags);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CombatHistoryEntry, Facts> Saved = new();
    internal static CardModel? EventCard(CombatHistoryEntry e, RuleEvent type) => (type, e) switch
    {
        (RuleEvent.CardPlayed, CardPlayFinishedEntry p) => p.CardPlay.Card, (RuleEvent.CardDrawn, CardDrawnEntry p) => p.Card,
        (RuleEvent.CardDiscarded, CardDiscardedEntry p) => p.Card, (RuleEvent.CardExhausted, CardExhaustedEntry p) => p.Card,
        (RuleEvent.CardGenerated, CardGeneratedEntry p) => p.Card, _ => null
    };
    internal static void Capture(CombatHistoryEntry entry)
    {
        var card = new[] { RuleEvent.CardPlayed, RuleEvent.CardDrawn, RuleEvent.CardDiscarded, RuleEvent.CardExhausted, RuleEvent.CardGenerated }.Select(type => EventCard(entry, type)).FirstOrDefault(c => c is not null);
        if (card is not null) Saved.GetValue(entry, _ => new(card.Id, card.Type, card.Rarity, card.EnergyCost.CostsX ? -1 : card.EnergyCost.GetWithModifiers(CostModifiers.All), card.IsUpgraded, card.Keywords.ToArray(), card.Tags.Select(tag => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(tag.ToString())).ToArray()));
    }
    internal static int Ordinal(EffectTrigger trigger, Creature owner, ICombatState combat)
    {
        return CombatManager.Instance.History.Entries.Count(entry => entry.Actor == owner
            && (trigger.Occurrence!.Within == CounterScope.Combat || entry.HappenedThisTurn(combat))
            && EventCard(entry, trigger.Event) is { } card && Matches(entry, card, trigger.Filter));
    }
    internal static bool MatchesCurrent(EffectTrigger trigger, CardModel card, Creature owner, ICombatState combat)
    {
        var entry = CombatManager.Instance.History.Entries.LastOrDefault(e => e.Actor == owner && e.HappenedThisTurn(combat) && ReferenceEquals(EventCard(e, trigger.Event), card));
        return entry is null ? NativeMechanics.Matches(card, trigger.Filter) : Matches(entry, card, trigger.Filter);
    }
    private static bool Matches(CombatHistoryEntry entry, CardModel card, CardFilter? filter)
    {
        if (filter is null) return true;
        if (!Saved.TryGetValue(entry, out var f)) return NativeMechanics.Matches(card, filter);
        return (filter.Id is null || f.Id == NativeMechanics.Card(filter.Id).Id)
            && (filter.Type is null || f.Type.ToString().Equals(filter.Type, StringComparison.OrdinalIgnoreCase))
            && (filter.Rarity is null || f.Rarity.ToString().Equals(filter.Rarity.ToString(), StringComparison.OrdinalIgnoreCase))
            && (filter.Cost is null || f.Cost == filter.Cost) && (filter.Upgraded is null || f.Upgraded == filter.Upgraded)
            && (filter.MinCost is null || f.Cost >= filter.MinCost) && (filter.MaxCost is null || f.Cost >= 0 && f.Cost <= filter.MaxCost)
            && (filter.Tag is null || f.Tags.Contains(filter.Tag))
            && (filter.Keyword is null || f.Keywords.Contains(NeowGeneratedCard.NativeKeyword(filter.Keyword.Value)));
    }
}
[HarmonyPatch(typeof(CombatHistory), "Add")]
internal static class GeneratedHistoryFactsPatch
{
    private static void Prefix(CombatHistoryEntry entry) => GeneratedEventFacts.Capture(entry);
}
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
internal static class GeneratedPowerIconPatch
{
    private static bool Prefix(PowerModel __instance, ref string __result) { if (__instance is not GeneratedEffectPower) return true; __result = ModelDb.Power<DexterityPower>().PackedIconPath; return false; }
}
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath), MethodType.Getter)]
internal static class GeneratedPowerBigIconPatch
{
    private static bool Prefix(PowerModel __instance, ref string __result) { if (__instance is not GeneratedEffectPower) return true; __result = ModelDb.Power<DexterityPower>().ResolvedBigIconPath; return false; }
}
