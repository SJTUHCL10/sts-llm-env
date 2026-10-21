using System.Text.Json.Serialization;

namespace Forge.Core;

public sealed record EventFilter
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Actor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Target { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Power { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PowerType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Temporary { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Orb { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Attack { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OwnTurn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FromHandDraw { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? CausedByEthereal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AutoPlay { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? LastInSeries { get; init; }
}

// Nullable facts distinguish an unavailable native fact from a known false value.
public sealed record RuleEventFacts
{
    public string? Actor { get; init; }
    public string? Source { get; init; }
    public string[] Targets { get; init; } = [];
    public string SourceKind { get; init; } = "unknown";
    public string? Power { get; init; }
    public string? PowerType { get; init; }
    public bool? Temporary { get; init; }
    public string? Orb { get; init; }
    public bool? Attack { get; init; }
    public bool? OwnTurn { get; init; }
    public bool? FromHandDraw { get; init; }
    public bool? CausedByEthereal { get; init; }
    public bool? AutoPlay { get; init; }
    public bool? LastInSeries { get; init; }
}

public static class RuleEvents
{
    public static bool IsCardEvent(RuleEvent e) => e is >= RuleEvent.CardPlayed and <= RuleEvent.CardGenerated;
    public static bool CarriesCard(RuleEvent e) => IsCardEvent(e) || e is RuleEvent.DamageReceived or RuleEvent.AttackCompleted
        or RuleEvent.BlockGained or RuleEvent.DamageGiven or RuleEvent.PowerAmountChanged or RuleEvent.Forged;
    public static bool CarriesResources(RuleEvent e) => e is RuleEvent.CardPlayed or RuleEvent.AttackCompleted;
    public static bool MatchesRelation(string filter, string? relation) => filter == "any" ? relation is not null
        : filter == "self_or_osty" ? relation is "self" or "osty" : filter == relation;
    public static bool Matches(EventFilter? filter, RuleEventFacts facts) => filter is null
        || (filter.Actor is null || MatchesRelation(filter.Actor, facts.Actor))
        && (filter.Target is null || facts.Targets.Any(t => MatchesRelation(filter.Target, t)))
        && (filter.Source is null || MatchesRelation(filter.Source, facts.Source))
        && (filter.SourceKind is null || filter.SourceKind == facts.SourceKind)
        && (filter.Power is null || filter.Power == facts.Power)
        && (filter.PowerType is null || filter.PowerType == facts.PowerType)
        && (filter.Temporary is null || filter.Temporary == facts.Temporary)
        && (filter.Orb is null || filter.Orb == facts.Orb)
        && (filter.Attack is null || filter.Attack == facts.Attack)
        && (filter.OwnTurn is null || filter.OwnTurn == facts.OwnTurn)
        && (filter.FromHandDraw is null || filter.FromHandDraw == facts.FromHandDraw)
        && (filter.CausedByEthereal is null || filter.CausedByEthereal == facts.CausedByEthereal)
        && (filter.AutoPlay is null || filter.AutoPlay == facts.AutoPlay)
        && (filter.LastInSeries is null || filter.LastInSeries == facts.LastInSeries);

    public static void Validate(EffectTrigger trigger)
    {
        if (trigger.Filter is not null && !CarriesCard(trigger.Event)) throw new FormatException("This event has no source card.");
        if (trigger.Occurrence is not null && (!IsCardEvent(trigger.Event) || trigger.Where is not null))
            throw new FormatException("Historical occurrences require a card event without event-context filters; use limit for activation quotas.");
        if (trigger.Where is not { } w) return;
        Require(trigger.Event is not (RuleEvent.TurnStart or RuleEvent.TurnEnd), "Turn events have fixed owner/phase semantics and do not use event-context filters.");
        Require(w.Actor is null or "self" or "osty" or "self_or_osty" or "enemy" or "any", "Invalid event actor.");
        Require(w.Target is null or "self" or "osty" or "self_or_osty" or "enemy" or "any", "Invalid event target.");
        Require(w.Source is null or "self" or "osty" or "self_or_osty" or "enemy" or "any", "Invalid event source creature.");
        Require(w.SourceKind is null or "card" or "power" or "relic" or "orb" or "other" or "unknown", "Invalid event source kind.");
        Require(w.Power is null || MechanicCatalog.Powers.Contains(w.Power), "Unknown event power.");
        Require(w.PowerType is null or "buff" or "debuff", "Invalid event power type.");
        Require(w.Power is null && w.PowerType is null && w.Temporary is null || trigger.Event == RuleEvent.PowerAmountChanged, "Power filters require power_amount_changed.");
        Require(w.Orb is null || MechanicCatalog.Orbs.Contains(w.Orb) && w.Orb != "random"
            && trigger.Event is RuleEvent.OrbChanneled or RuleEvent.OrbEvoked, "Orb filter requires an orb event and concrete type.");
        Require(w.Attack is null || trigger.Event is RuleEvent.DamageGiven or RuleEvent.DamageReceived or RuleEvent.AttackCompleted, "Attack filter requires a damage/attack event.");
        Require(w.FromHandDraw is null || trigger.Event == RuleEvent.CardDrawn, "Draw origin requires card_drawn.");
        Require(w.CausedByEthereal is null || trigger.Event == RuleEvent.CardExhausted, "Exhaust cause requires card_exhausted.");
        Require(w.AutoPlay is null && w.LastInSeries is null || CarriesResources(trigger.Event), "Play facts require card_played or attack_completed.");
        Require(w.SourceKind is null || CarriesCard(trigger.Event) || trigger.Event is RuleEvent.OrbChanneled or RuleEvent.OrbEvoked, "This native event exposes no source model.");
        Require(w.Source is null || CarriesCard(trigger.Event) || trigger.Event is RuleEvent.OrbChanneled or RuleEvent.OrbEvoked, "This native event exposes no source creature.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
