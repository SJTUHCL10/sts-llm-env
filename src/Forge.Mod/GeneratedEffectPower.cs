using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Forge.Mod;

public sealed record GeneratedPowerSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public required CardDefinition Definition { get; init; }
    public required decimal[] Amounts { get; init; }
    public required EffectTriggerState State { get; init; }
    public bool SourceUpgraded { get; init; }
    public bool IgnoreArmingPlay { get; init; }
}

// Each play creates its own instance: different generated definitions never stack into a shared model state.
public sealed class GeneratedEffectPower : PowerModel
{
    private static readonly AsyncLocal<int> TriggerDepth = new();
    private GeneratedPowerSnapshot? _snapshot;
    private bool _firing;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => _snapshot is null ? []
        : NeowGeneratedCard.EffectHoverTips(_snapshot.Definition);

    [SavedProperty]
    public string RuntimePayload
    {
        get => _snapshot is null ? "" : Wire.Encode(_snapshot);
        set
        {
            AssertMutable();
            if (string.IsNullOrEmpty(value)) { _snapshot = null; return; }
            var snapshot = Wire.Decode<GeneratedPowerSnapshot>(value);
            if (snapshot.SchemaVersion != 1) throw new FormatException("Unsupported power state schema.");
            CardValidator.Validate(snapshot.Definition);
            if (!snapshot.Definition.Effects.Any(e => e.Trigger != EffectTrigger.OnPlay)
                || snapshot.Amounts is null || snapshot.Amounts.Length != snapshot.Definition.Effects.Length
                || snapshot.Amounts.Any(n => n < 0 || n > int.MaxValue) || snapshot.State is null)
                throw new FormatException("Invalid captured effect state.");
            EffectTriggerRuntime.Validate(snapshot.Definition, snapshot.State);
            _snapshot = snapshot;
        }
    }

    internal void Configure(NeowGeneratedCard card)
    {
        AssertMutable();
        // Detach arrays from the card and other powers, including before native cloning/saving.
        RuntimePayload = Wire.Encode(new GeneratedPowerSnapshot
        {
            Definition = card.Definition,
            Amounts = card.Definition.Effects.Select((_, i) => card.DynamicVars[$"E{i}"].BaseValue).ToArray(),
            State = EffectTriggerRuntime.Create(card.Definition), SourceUpgraded = card.IsUpgraded, IgnoreArmingPlay = true
        });
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        if (_snapshot is not null) _snapshot = Wire.Decode<GeneratedPowerSnapshot>(Wire.Encode(_snapshot));
        _firing = false;
    }

    public override LocString Title => Localize("title", _snapshot?.Definition.Name ?? "涅奥的造物");
    public override LocString Description
    {
        get
        {
            bool chinese = LocManager.Instance.Language is "zhs" or "zht";
            if (_snapshot is null) return Localize("description", "");
            var snapshot = _snapshot;
            string text = string.Join("\n", snapshot.Definition.Effects.Select((effect, index) => (effect, index))
                .Where(item => item.effect.Trigger != EffectTrigger.OnPlay && snapshot.State.Remaining[item.index] != 0)
                .Select(item => CardText.RenderEffect(item.effect with
                {
                    Duration = snapshot.State.Remaining[item.index] < 0 ? 0 : snapshot.State.Remaining[item.index]
                }, chinese, snapshot.Amounts[item.index].ToString(System.Globalization.CultureInfo.InvariantCulture))
                    + (EffectRules.IsEvent(item.effect.Trigger)
                        ? chinese ? $" 本回合已触发 {snapshot.State.Activations[item.index]} 次。"
                            : $" Triggered {snapshot.State.Activations[item.index]} times this turn." : "")));
            return Localize("description", text);
        }
    }

    private static LocString Localize(string suffix, string text)
    {
        string key = "NEOWS_COMPANY." + AtomicStore.Key(text) + "." + suffix;
        LocManager.Instance.GetTable("powers").MergeWith(new Dictionary<string, string> { [key] = text });
        return new LocString("powers", key);
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (_snapshot is not null && Owner.Side == side && participants.Contains(Owner))
            EffectTriggerRuntime.BeginTurn(_snapshot.State);
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature != Owner) return;
        await Fire(choiceContext, [EffectTrigger.NextTurnStart, EffectTrigger.TurnStart]);
        await RemoveIfExpired();
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (Owner.Side == side && participants.Contains(Owner)) await Fire(choiceContext, [EffectTrigger.TurnEnd]);
    }

    public override async Task AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (_snapshot is null || Owner.Side != side || !participants.Contains(Owner)) return;
        EffectTriggerRuntime.EndTurn(_snapshot.Definition, _snapshot.State);
        await RemoveIfExpired();
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (_snapshot is null || cardPlay.Card.Owner != Owner.Player) return;
        if (_snapshot.IgnoreArmingPlay) { _snapshot = _snapshot with { IgnoreArmingPlay = false }; return; }
        EffectTrigger[] triggers = cardPlay.Card.Type switch
        {
            CardType.Attack => [EffectTrigger.CardPlayed, EffectTrigger.AttackPlayed],
            CardType.Skill => [EffectTrigger.CardPlayed, EffectTrigger.SkillPlayed],
            _ => [EffectTrigger.CardPlayed]
        };
        await Fire(choiceContext, triggers, cardPlay.Card);
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) =>
        card.Owner == Owner.Player ? Fire(choiceContext, [EffectTrigger.CardDrawn]) : Task.CompletedTask;

    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal) =>
        card.Owner == Owner.Player ? Fire(choiceContext, [EffectTrigger.CardExhausted]) : Task.CompletedTask;

    private async Task Fire(PlayerChoiceContext choice, EffectTrigger[] triggers, CardModel? eventCard = null)
    {
        if (_snapshot is null || _firing || TriggerDepth.Value >= 8 || !Owner.IsAlive || CombatManager.Instance.IsEnding) return;
        var snapshot = _snapshot;
        var owner = Owner.Player;
        if (owner is null) return;
        var source = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        source.DefinitionPayload = Wire.Encode(snapshot.Definition);
        source.Owner = owner;
        if (snapshot.SourceUpgraded) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
        for (int i = 0; i < snapshot.Amounts.Length; i++) source.DynamicVars[$"E{i}"].BaseValue = snapshot.Amounts[i];
        _firing = true;
        TriggerDepth.Value++;
        try
        {
            for (int i = 0; i < snapshot.Definition.Effects.Length; i++)
            {
                var effect = snapshot.Definition.Effects[i];
                if (triggers.Contains(effect.Trigger) && EffectTriggerRuntime.TryConsume(snapshot.Definition, snapshot.State, i, effect.Trigger))
                    await GeneratedEffectExecutor.Execute(source, effect, snapshot.Amounts[i], CombatState, choice,
                        excludedCard: eventCard);
            }
        }
        finally { TriggerDepth.Value--; _firing = false; }
    }

    private Task RemoveIfExpired() => _snapshot is not null && EffectTriggerRuntime.IsExpired(_snapshot.State)
        ? PowerCmd.Remove(this) : Task.CompletedTask;
}

// Use installed native placeholders so a custom power requires no texture bundle.
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
internal static class GeneratedPowerIconPatch
{
    private static bool Prefix(PowerModel __instance, ref string __result)
    {
        if (__instance is not GeneratedEffectPower) return true;
        __result = ModelDb.Power<DexterityPower>().PackedIconPath;
        return false;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath), MethodType.Getter)]
internal static class GeneratedPowerBigIconPatch
{
    private static bool Prefix(PowerModel __instance, ref string __result)
    {
        if (__instance is not GeneratedEffectPower) return true;
        __result = ModelDb.Power<DexterityPower>().ResolvedBigIconPath;
        return false;
    }
}
