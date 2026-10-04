using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace Forge.Core;

public static class Wire
{
    internal static readonly JsonSerializerOptions LegacyCardJson = CreateOptions();
    public static readonly JsonSerializerOptions Json = CreateOptions();

    static Wire() => Json.Converters.Insert(0, new CardDefinitionJsonConverter());

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Decode<T>(string value)
    {
        return JsonSerializer.Deserialize<T>(value, Json) ?? throw new FormatException("Empty JSON document.");
    }
}

internal sealed class CardDefinitionJsonConverter : JsonConverter<CardDefinition>
{
    public override CardDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var json = document.RootElement;
        var card = json.Deserialize<CardDefinition>(Wire.LegacyCardJson);
        if (card?.SchemaVersion == 4 && json.TryGetProperty("effects", out var effects) && effects.ValueKind == JsonValueKind.Array)
            foreach (var effect in effects.EnumerateArray())
                if (effect.ValueKind == JsonValueKind.Object && effect.TryGetProperty("scaling_cap", out _))
                    throw new JsonException("scaling_cap is not part of schema_version 4.");
        return card;
    }

    public override void Write(Utf8JsonWriter writer, CardDefinition card, JsonSerializerOptions options)
    {
        var json = JsonSerializer.SerializeToElement(card, Wire.LegacyCardJson);
        if (card.SchemaVersion != 4) { json.WriteTo(writer); return; }
        if (card.Effects?.Any(e => e?.ScalingCap != 0) == true) throw new JsonException("Scaling caps require a legacy card schema.");
        writer.WriteStartObject();
        foreach (var property in json.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (property.Name != "effects" || property.Value.ValueKind != JsonValueKind.Array) { property.Value.WriteTo(writer); continue; }
            writer.WriteStartArray();
            foreach (var effect in property.Value.EnumerateArray())
            {
                if (effect.ValueKind != JsonValueKind.Object) { effect.WriteTo(writer); continue; }
                writer.WriteStartObject();
                foreach (var slot in effect.EnumerateObject()) if (slot.Name != "scaling_cap") slot.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}

public enum EffectKind { Damage, Block, Draw, Energy, Strength, Dexterity, Weak, Vulnerable, Poison, DiscardRandomHand, ExhaustRandomHand, ReturnRandomDiscard, Stars }
public enum EffectTarget { Self, Enemy, AllEnemies, RandomEnemy }
public enum ForgeCardType { Attack, Skill, Power }
public enum ForgeRarity { Common, Uncommon, Rare }
public enum ForgeKeyword { Exhaust, Ethereal, Retain, Innate }
public enum GenerationTiming { Prefetch, WaitOnReward }
public enum EffectTrigger { OnPlay, NextTurnStart, TurnStart, TurnEnd, CardPlayed, AttackPlayed, SkillPlayed, CardDrawn, CardExhausted }
public enum EffectCondition { None, SelfHasBlock, SelfHpBelowHalf, TargetWeak, TargetVulnerable }
public enum EffectScaling { None, SelfBlock, HandSize, DiscardSize, ExhaustSize, TargetPoison, SelfStars }

public sealed record CardEffect
{
    public required EffectKind Kind { get; init; }
    public required EffectTarget Target { get; init; }
    public required int Amount { get; init; }
    public int UpgradeAmount { get; init; }
    public EffectTrigger Trigger { get; init; }
    // Duration 0 means combat-long and is legal only on Power cards. Event durations include the arming turn.
    public int Duration { get; init; } = 1;
    public int MaxPerTurn { get; init; } = 1;
    public int Repeat { get; init; } = 1;
    public EffectCondition Condition { get; init; }
    public EffectScaling Scaling { get; init; }
    public int ScalingAmount { get; init; }
    // Read/preserve v1-v3 saved caps. New v4 definitions have no cap field.
    public int ScalingCap { get; init; }
}

public sealed record CardDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Name { get; init; }
    public required ForgeCardType Type { get; init; }
    public required ForgeRarity Rarity { get; init; }
    public required int Cost { get; init; }
    public int StarCost { get; init; } = -1;
    public int UpgradeCost { get; init; }
    public int UpgradeStarCost { get; init; }
    public ForgeKeyword[] Keywords { get; init; } = [];
    public required CardEffect[] Effects { get; init; }
    public string Flavor { get; init; } = "";
}

public sealed record CardBatch
{
    public required CardDefinition[] Cards { get; init; }
}

// Game-independent serialized snapshots keep the provider and prompt pipeline reusable.
public sealed record GenerationContext
{
    [JsonPropertyOrder(-10)]
    public int SchemaVersion { get; init; } = 1;
    [JsonPropertyOrder(1)]
    public required string CombatKey { get; init; }
    [JsonPropertyOrder(-9)]
    public required JsonElement Run { get; init; }
    [JsonPropertyOrder(2)]
    public required JsonElement State { get; init; }
    [JsonPropertyOrder(6)]
    public required JsonElement[] RecentEvents { get; init; }
    [JsonPropertyOrder(4)]
    public int TotalEvents { get; init; }
    [JsonPropertyOrder(5)]
    public int OmittedEvents { get; init; }
    [JsonPropertyOrder(3)]
    public JsonElement? CombatSummary { get; init; }
    [JsonPropertyOrder(0)]
    public JsonElement? FirstRoundSummary { get; init; }
    [JsonPropertyOrder(-8)]
    public JsonElement[] GenerationHistory { get; init; } = [];
}

public sealed record Prompt(string System, string User)
{
    [JsonIgnore] public int Revision { get; init; }
}
public sealed record ProviderDiagnostics(int Revision, string? ReasoningContent, string? FinishReason,
    int? PromptTokens, int? CompletionTokens, int? TotalTokens,
    int? PromptCacheHitTokens = null, int? PromptCacheMissTokens = null);
public interface IContentGenerator<T>
{
    Task<T> GenerateAsync(Prompt prompt, CancellationToken cancellationToken);
}
