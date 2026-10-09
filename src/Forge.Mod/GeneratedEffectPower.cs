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
        int offset = Form.Immediate.Length;
        var lines = new List<string>();
        for (int i = 0; i < Form.Listeners.Length; i++)
        {
            var rule = Form.Listeners[i];
            int index = offset;
            if (_snapshot.State.Remaining[i] != 0)
                lines.Add(CardText.RenderRule(rule, chinese, effect =>
                {
                    string amount = effect.Amount?.Value is not null ? _snapshot.Amounts[index].ToString(System.Globalization.CultureInfo.InvariantCulture) : CardText.Number(effect.Amount, chinese, resource: (kind, value) => CardText.ResourceText(kind, value, prefix));
                    index++;
                    return CardText.RenderEffect(effect, chinese, amount, (kind, value) => CardText.ResourceText(kind, value, prefix));
                }, combatIsImplicit: true,
                    resource: (kind, value) => CardText.ResourceText(kind, value, prefix)));
            offset += rule.Effects.Length;
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
    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player) => player.Creature == Owner ? Fire(choiceContext, RuleEvent.TurnStart) : Task.CompletedTask;
    public override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants) => side == Owner.Side && participants.Contains(Owner) ? Fire(choiceContext, RuleEvent.TurnEnd) : Task.CompletedTask;
    public override async Task AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (_snapshot is null || side != Owner.Side || !participants.Contains(Owner)) return;
        EffectTriggerRuntime.EndTurn(Form, _snapshot.State); await RemoveIfExpired();
    }
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (ReferenceEquals(_armingPlay, cardPlay)) { _armingPlay = null; return Task.CompletedTask; }
        return cardPlay.Player.Creature == Owner ? Fire(choiceContext, RuleEvent.CardPlayed, cardPlay.Card, cardPlay.Target) : Task.CompletedTask;
    }
    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) => card.Owner.Creature == Owner ? Fire(choiceContext, RuleEvent.CardDrawn, card) : Task.CompletedTask;
    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card) => card.Owner.Creature == Owner ? Fire(choiceContext, RuleEvent.CardDiscarded, card) : Task.CompletedTask;
    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal) => card.Owner.Creature == Owner ? Fire(choiceContext, RuleEvent.CardExhausted, card) : Task.CompletedTask;
    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => creator?.Creature == Owner ? Fire(new BlockingPlayerChoiceContext(), RuleEvent.CardGenerated, card) : Task.CompletedTask;
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource) => target == Owner ? Fire(choiceContext, RuleEvent.DamageReceived, target: dealer, amount: result.UnblockedDamage) : Task.CompletedTask;
    public override Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command) => command.Attacker == Owner || command.Attacker == Owner.Player?.Osty ? Fire(choiceContext, RuleEvent.AttackCompleted, command.CardPlay?.Card, command.Results.SelectMany(r => r).Select(r => r.Receiver).FirstOrDefault(), (int)command.Results.SelectMany(r => r).Sum(r => r.TotalDamage)) : Task.CompletedTask;
    public override Task AfterSummon(PlayerChoiceContext choiceContext, Player summoner, decimal amount) => summoner.Creature == Owner ? Fire(choiceContext, RuleEvent.Summoned, amount: (int)amount) : Task.CompletedTask;
    public override Task AfterOrbChanneled(PlayerChoiceContext choiceContext, Player player, OrbModel orb) => player.Creature == Owner ? Fire(choiceContext, RuleEvent.OrbChanneled) : Task.CompletedTask;
    public override Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets) => orb.Owner.Creature == Owner ? Fire(choiceContext, RuleEvent.OrbEvoked, target: targets.FirstOrDefault()) : Task.CompletedTask;
    private async Task Fire(PlayerChoiceContext choice, RuleEvent fired, CardModel? eventCard = null, Creature? target = null, int amount = 0)
    {
        if (_snapshot is null || _firing || TriggerDepth.Value >= 8 || !Owner.IsAlive || CombatManager.Instance.IsEnding || Owner.Player is null) return;
        var snapshot = _snapshot; var form = Form;
        var source = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        source.DefinitionPayload = Wire.Encode(snapshot.Definition); source.Owner = Owner.Player;
        if (snapshot.SourceUpgraded) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
        var context = new GeneratedExecutionContext(source, choice, amounts: snapshot.Amounts)
        {
            Triggered = true, EventCard = eventCard, EventTarget = target, EventAmount = amount,
            OriginCard = Owner.Player.PlayerCombatState?.AllCards.OfType<NeowGeneratedCard>().FirstOrDefault(card => card.InstanceKey == snapshot.SourceInstanceKey),
            PaidEnergy = snapshot.PaidEnergy, PaidStars = snapshot.PaidStars
        };
        var eligible = form.Listeners.Select(rule => rule.Trigger.Event == fired && (rule.Trigger.Filter is null || eventCard is not null && GeneratedEventFacts.MatchesCurrent(rule.Trigger, eventCard, Owner, CombatState))).ToArray();
        // Read ordinals before any payoff can append nested events.
        var ordinals = form.Listeners.Select((rule, i) => eligible[i] && rule.Trigger.Occurrence is not null ? GeneratedEventFacts.Ordinal(rule.Trigger, Owner, CombatState) : 1).ToArray();
        _firing = true; TriggerDepth.Value++; choice.PushModel(source);
        try
        {
            int offset = form.Immediate.Length;
            for (int i = 0; i < form.Listeners.Length; i++)
            {
                var rule = form.Listeners[i];
                context.Selections.Clear();
                if (eligible[i] && EffectTriggerRuntime.TryConsume(form, snapshot.State, i, fired, ordinals[i], context.Matches(rule.Condition)))
                    await GeneratedEffectExecutor.ExecuteGroup(context, rule.Effects, offset);
                offset += rule.Effects.Length;
            }
        }
        finally { source.InvokeExecutionFinished(); choice.PopModel(source); TriggerDepth.Value--; _firing = false; }
        await RemoveIfExpired();
    }
    private Task RemoveIfExpired() => _snapshot is not null && EffectTriggerRuntime.IsExpired(_snapshot.State) ? PowerCmd.Remove(this) : Task.CompletedTask;
}

internal static class GeneratedEventFacts
{
    private sealed record Facts(ModelId Id, CardType Type, CardRarity Rarity, int Cost, bool Upgraded, CardKeyword[] Keywords);
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
        if (card is not null) Saved.GetValue(entry, _ => new(card.Id, card.Type, card.Rarity, card.EnergyCost.CostsX ? -1 : card.EnergyCost.GetWithModifiers(CostModifiers.All), card.IsUpgraded, card.Keywords.ToArray()));
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
