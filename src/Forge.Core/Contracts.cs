using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace Forge.Core;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new FormatException("Empty JSON document.");
}

public enum ForgeCardType { Attack, Skill, Power }
public enum ForgeRarity { Common, Uncommon, Rare }
public enum ForgeKeyword { Exhaust, Ethereal, Retain, Innate, Sly }
public enum GenerationTiming { Prefetch, WaitOnReward }
public enum EffectKind
{
    Damage, Block, Draw, GainEnergy, GainStars, ApplyPower, Discard, Exhaust, Move, Select,
    Upgrade, Copy, Transform, CreateCard, Play, AddKeyword, RemoveKeyword, SetCost,
    Summon, Forge, Channel, Evoke, OrbPassive, OrbSlots, Heal, LoseHp
}
public enum CardPileName { Hand, Draw, Discard, Exhaust }
public enum SelectionMode { Random, Choose, All, First, Last }
public enum RuleEvent { TurnStart, TurnEnd, CardPlayed, CardDrawn, CardDiscarded, CardExhausted, CardGenerated, DamageReceived, AttackCompleted, Summoned, OrbChanneled, OrbEvoked }
public enum CounterScope { Turn, Combat }
public enum LifetimeKind { Turn, NextTurn, Combat }
public enum Comparison { Eq, Ne, Gt, Ge, Lt, Le }

[JsonConverter(typeof(NumberExpressionConverter))]
public sealed record NumberExpression
{
    [JsonIgnore] public int? Value { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Of { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression[]? Add { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression[]? Mul { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression[]? Div { get; init; }
    public static implicit operator NumberExpression(int value) => new() { Value = value };
}
internal sealed class NumberExpressionConverter : JsonConverter<NumberExpression>
{
    public override NumberExpression Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return new() { Value = reader.GetInt32() };
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected integer or expression.");
        string? stat = null, of = null, id = null;
        NumberExpression[]? add = null, mul = null, div = null;
        var seen = new HashSet<string>();
        foreach (var p in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new JsonException("Duplicate expression field.");
            switch (p.Name)
            {
                case "stat": stat = p.Value.GetString(); break;
                case "of": of = p.Value.GetString(); break;
                case "id": id = p.Value.GetString(); break;
                case "add": add = p.Value.Deserialize<NumberExpression[]>(options); break;
                case "mul": mul = p.Value.Deserialize<NumberExpression[]>(options); break;
                case "div": div = p.Value.Deserialize<NumberExpression[]>(options); break;
                default: throw new JsonException("Unknown expression field.");
            }
        }
        return new() { Stat = stat, Of = of, Id = id, Add = add, Mul = mul, Div = div };
    }
    public override void Write(Utf8JsonWriter writer, NumberExpression value, JsonSerializerOptions options)
    {
        if (value.Value is int n) { writer.WriteNumberValue(n); return; }
        writer.WriteStartObject();
        if (value.Stat is not null) writer.WriteString("stat", value.Stat);
        if (value.Of is not null) writer.WriteString("of", value.Of);
        if (value.Id is not null) writer.WriteString("id", value.Id);
        foreach (var pair in new[] { ("add", value.Add), ("mul", value.Mul), ("div", value.Div) })
            if (pair.Item2 is not null) { writer.WritePropertyName(pair.Item1); JsonSerializer.Serialize(writer, pair.Item2, options); }
        writer.WriteEndObject();
    }
}
public sealed record EffectCondition
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Comparison? Op { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Left { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Right { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectCondition[]? All { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectCondition[]? Any { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectCondition? Not { get; init; }
}
public sealed record CardFilter
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForgeRarity? Rarity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Cost { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Upgraded { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForgeKeyword? Keyword { get; init; }
}
[JsonConverter(typeof(EffectTargetConverter))]
public sealed record EffectTarget
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ref { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardPileName? Pile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectionMode? Pick { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Count { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardFilter? Filter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UpTo { get; init; }
    public static implicit operator EffectTarget(string value) => new() { Ref = value };
}
internal sealed class EffectTargetConverter : JsonConverter<EffectTarget>
{
    public override EffectTarget Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new() { Ref = reader.GetString() };
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected target or selector.");
        CardPileName? pile = null; SelectionMode? pick = null; NumberExpression? count = null;
        CardFilter? filter = null; bool? upTo = null;
        var seen = new HashSet<string>();
        foreach (var p in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new JsonException("Duplicate target field.");
            switch (p.Name)
            {
                case "pile": pile = p.Value.Deserialize<CardPileName>(options); break;
                case "pick": pick = p.Value.Deserialize<SelectionMode>(options); break;
                case "count": count = p.Value.Deserialize<NumberExpression>(options); break;
                case "filter": filter = p.Value.Deserialize<CardFilter>(options); break;
                case "up_to": upTo = p.Value.GetBoolean(); break;
                default: throw new JsonException("Unknown target field.");
            }
        }
        return new() { Pile = pile, Pick = pick, Count = count, Filter = filter, UpTo = upTo };
    }
    public override void Write(Utf8JsonWriter writer, EffectTarget value, JsonSerializerOptions options)
    {
        if (value.Ref is not null) { writer.WriteStringValue(value.Ref); return; }
        writer.WriteStartObject();
        void Slot<T>(string name, T item) { if (item is not null) { writer.WritePropertyName(name); JsonSerializer.Serialize(writer, item, options); } }
        Slot("pile", value.Pile); Slot("pick", value.Pick); Slot("count", value.Count); Slot("filter", value.Filter); Slot("up_to", value.UpTo);
        writer.WriteEndObject();
    }
}
public sealed record CardSource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pool { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectionMode? Pick { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardFilter? Filter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Upgraded { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Options { get; init; }
}
public sealed record CardCost
{
    public int Energy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Stars { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnergyX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StarsX { get; init; }
}
public sealed record CardEffect
{
    public required EffectKind Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectTarget? Target { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Amount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Repeat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectCondition? Condition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Power { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Actor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardPileName? To { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Position { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardSource? Card { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumberExpression? Count { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? As { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForgeKeyword? Keyword { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LifetimeKind? Until { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Orb { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Remove { get; init; }
}
public sealed record EventOccurrence
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? First { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Nth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Every { get; init; }
    public CounterScope Within { get; init; } = CounterScope.Turn;
}
public sealed record TriggerQuota
{
    public required int Count { get; init; }
    public CounterScope Within { get; init; } = CounterScope.Turn;
}
public sealed record EffectTrigger
{
    public required RuleEvent Event { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardFilter? Filter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EventOccurrence? Occurrence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TriggerQuota? Limit { get; init; }
}
public sealed record CardRule
{
    public required EffectTrigger Trigger { get; init; }
    public LifetimeKind Lifetime { get; init; } = LifetimeKind.Combat;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Turns { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectCondition? Condition { get; init; }
    public required CardEffect[] Effects { get; init; }
}
public sealed record CardForm
{
    public required CardCost Cost { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForgeKeyword[]? Keywords { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardEffect[]? Effects { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardRule[]? Rules { get; init; }
    [JsonIgnore] public CardEffect[] Immediate => Effects ?? [];
    [JsonIgnore] public CardRule[] Listeners => Rules ?? [];
    [JsonIgnore] public ForgeKeyword[] Tags => Keywords ?? [];
    [JsonIgnore] public CardEffect[] AllEffects => Immediate.Concat(Listeners.SelectMany(r => r.Effects)).ToArray();
}
public sealed record CardDefinition
{
    public required string Name { get; init; }
    public required ForgeCardType Type { get; init; }
    public required ForgeRarity Rarity { get; init; }
    public required CardForm[] Forms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Flavor { get; init; }
    public CardForm Form(bool upgraded) => Forms[upgraded ? 1 : 0];
}
public sealed record CardBatch { public required CardDefinition[] Cards { get; init; } }
// Internal journal/session metadata is projected before being sent to a provider.
public sealed record GenerationContext
{
    public int SchemaVersion { get; init; } = 1;
    public required string CombatKey { get; init; }
    public required JsonElement Run { get; init; }
    public required JsonElement State { get; init; }
    public required JsonElement[] RecentEvents { get; init; }
    public int TotalEvents { get; init; }
    public int OmittedEvents { get; init; }
    public JsonElement? CombatSummary { get; init; }
    public JsonElement? FirstRoundSummary { get; init; }
    public JsonElement[] GenerationHistory { get; init; } = [];
}
public sealed record Prompt(string System, string User) { [JsonIgnore] public int Revision { get; init; } }
public sealed record ProviderDiagnostics(int Revision, string? ReasoningContent, string? FinishReason,
    int? PromptTokens, int? CompletionTokens, int? TotalTokens,
    int? PromptCacheHitTokens = null, int? PromptCacheMissTokens = null, string? Content = null);
public interface IContentGenerator<T> { Task<T> GenerateAsync(Prompt prompt, CancellationToken cancellationToken); }
