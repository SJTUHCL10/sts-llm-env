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

public enum EffectKind { Damage, Block, Draw, Energy, Strength, Dexterity, Weak, Vulnerable, Poison }
public enum EffectTarget { Self, Enemy, AllEnemies }
public enum ForgeCardType { Attack, Skill }
public enum ForgeRarity { Common, Uncommon, Rare }
public enum ForgeKeyword { Exhaust, Ethereal, Retain, Innate }
public enum GenerationTiming { Prefetch, WaitOnReward }

public sealed record CardEffect
{
    public required EffectKind Kind { get; init; }
    public required EffectTarget Target { get; init; }
    public required int Amount { get; init; }
    public int UpgradeAmount { get; init; }
}

public sealed record CardDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Name { get; init; }
    public required ForgeCardType Type { get; init; }
    public required ForgeRarity Rarity { get; init; }
    public required int Cost { get; init; }
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
}

public sealed record Prompt(string System, string User);
public interface IContentGenerator<T>
{
    Task<T> GenerateAsync(Prompt prompt, CancellationToken cancellationToken);
}
