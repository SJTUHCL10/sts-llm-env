using Forge.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Forge.Mod;

// Captured at the native callback, before any rule can change the observed state.
internal sealed record GeneratedRuleEvent(RuleEvent Kind)
{
    internal CardModel? Card { get; init; }
    internal AbstractModel? SourceModel { get; init; }
    internal Creature? Actor { get; init; }
    internal Creature? Source { get; init; }
    internal Creature? Subject { get; init; }
    internal Creature? Target { get; init; }
    internal Creature[] Targets { get; init; } = [];
    internal decimal Amount { get; init; }
    internal decimal TotalDamage { get; init; }
    internal decimal UnblockedDamage { get; init; }
    internal CardPlay? Play { get; init; }
    internal string? Power { get; init; }
    internal string? PowerType { get; init; }
    internal bool? Temporary { get; init; }
    internal string? Orb { get; init; }
    internal bool? Attack { get; init; }
    internal bool? FromHandDraw { get; init; }
    internal bool? CausedByEthereal { get; init; }

    internal RuleEventFacts Facts(Creature owner) => new()
    {
        Actor = Relation(Actor, owner), Source = Relation(Source, owner), Targets = Targets.Select(t => Relation(t, owner)!).Distinct().ToArray(),
        SourceKind = SourceModel switch
        {
            CardModel => "card", PowerModel => "power", RelicModel => "relic", OrbModel => "orb", null => "unknown", _ => "other"
        },
        Power = Power, PowerType = PowerType, Temporary = Temporary, Orb = Orb, Attack = Attack,
        OwnTurn = owner.CombatState is { } combat ? combat.CurrentSide == owner.Side : null,
        FromHandDraw = FromHandDraw, CausedByEthereal = CausedByEthereal,
        AutoPlay = Play?.IsAutoPlay, LastInSeries = Play?.IsLastInSeries
    };
    internal bool Matches(EffectTrigger trigger, Creature owner, RuleEventFacts facts)
    {
        if (trigger.Event != Kind || !RuleEvents.Matches(trigger.Where, facts)) return false;
        // Explicit relation filters may widen the default observation scope.
        return Kind switch
        {
            RuleEvent.DamageReceived => trigger.Where?.Target is not null || Subject == owner,
            RuleEvent.BlockGained => trigger.Where?.Actor is not null || trigger.Where?.Target is not null || Subject == owner,
            RuleEvent.DamageGiven or RuleEvent.AttackCompleted => trigger.Where?.Actor is not null || Actor == owner || Actor is not null && Actor == owner.Player?.Osty,
            RuleEvent.PowerAmountChanged => trigger.Where?.Actor is not null || trigger.Where?.Target is not null || Actor == owner,
            _ => trigger.Where?.Actor is not null || Actor == owner
        };
    }
    internal static string? Relation(Creature? creature, Creature owner) => creature is null ? null
        : creature == owner ? "self" : creature == owner.Player?.Osty ? "osty" : creature.Side != owner.Side ? "enemy" : "other";
    internal static Creature? ModelOwner(AbstractModel? source) => source switch
    {
        CardModel card => card.Owner?.Creature, PowerModel power => power.Owner,
        RelicModel relic => relic.Owner?.Creature, OrbModel orb => orb.Owner?.Creature, _ => null
    };
    internal static string? PowerAlias(PowerModel power) => MechanicCatalog.Powers.FirstOrDefault(id => NativeMechanics.Power(id).Id == power.Id);
    internal static string OrbAlias(OrbModel orb) => orb.GetType().Name.Replace("Orb", "", StringComparison.Ordinal).ToLowerInvariant();
}
