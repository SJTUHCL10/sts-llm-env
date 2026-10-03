using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace Forge.Core;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new FormatException("Empty JSON document.");
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

public sealed record Prompt(string System, string User)
{
    [JsonIgnore] public int Revision { get; init; }
}
public sealed record ProviderDiagnostics(int Revision, string? ReasoningContent, string? FinishReason,
    int? PromptTokens, int? CompletionTokens, int? TotalTokens);
public interface IContentGenerator<T>
{
    Task<T> GenerateAsync(Prompt prompt, CancellationToken cancellationToken);
}
