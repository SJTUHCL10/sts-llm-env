using System.Net;
using System.Text.Json;
using Forge.Core;

int passed = 0;
var valid = new CardDefinition
{
    Name = "涅奥的低语", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Common, Cost = 1,
    Effects = [new() { Kind = EffectKind.Damage, Target = EffectTarget.Enemy, Amount = 8, UpgradeAmount = 3 }]
};
var context = new GenerationContext
{
    CombatKey = "combat-a", Run = JsonSerializer.SerializeToElement(new { deck = new[] { "Strike" } }),
    State = JsonSerializer.SerializeToElement(new { hp = 50 }), RecentEvents = [], TotalEvents = 100, OmittedEvents = 100
};
var config = new ForgeConfig { PrefetchMinimumIntervalSeconds = 1, MaxRequestsPerCombat = 2 };

await Test("valid definition and round trip", () =>
{
    var card = CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(valid)));
    Check(card.Effects[0].Amount == 8 && card.Name == valid.Name);
    return Task.CompletedTask;
});
foreach (var (name, invalid) in new (string, CardDefinition)[]
{
    ("markup", valid with { Name = "[img]bad[/img]" }),
    ("negative cost", valid with { Cost = -1 }),
    ("future schema", valid with { SchemaVersion = 2 }),
    ("attack without damage", valid with { Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 3 }] }),
    ("skill with damage", valid with { Type = ForgeCardType.Skill }),
    ("self damage", valid with { Effects = [valid.Effects[0] with { Target = EffectTarget.Self }] }),
    ("excessive upgrade", valid with { Effects = [valid.Effects[0] with { UpgradeAmount = 7 }] }),
    ("overpowered", valid with { Cost = 0, Effects = [valid.Effects[0] with { Amount = 30, UpgradeAmount = 0 }] }),
    ("duplicate keywords", valid with { Keywords = [ForgeKeyword.Exhaust, ForgeKeyword.Exhaust] }),
    ("zero cost draw loop", valid with { Type = ForgeCardType.Skill, Cost = 0,
        Effects = [new() { Kind = EffectKind.Draw, Target = EffectTarget.Self, Amount = 1 }] })
}) await Test("reject " + name, () => Reject(() => CardValidator.Validate(invalid)));

await Test("reject unknown JSON fields / numeric enums / missing required fields", async () =>
{
    await Reject(() => Wire.Decode<CardDefinition>(Wire.Encode(valid).Replace("\"cost\":1", "\"script\":\"bad\",\"cost\":1")));
    await Reject(() => Wire.Decode<CardDefinition>(Wire.Encode(valid).Replace("\"attack\"", "42")));
    await Reject(() => Wire.Decode<CardDefinition>("{}"));
});
await Test("configuration bounds", async () =>
{
    config.Validate();
    await Reject(() => (config with { GeneratedCardsPerReward = 4 }).Validate());
    await Reject(() => (config with { Provider = new() { BaseUrl = "file:///C:/x" } }).Validate());
    await Reject(() => (config with { ActiveStyle = "missing" }).Validate());
    await Reject(() => (config with { Provider = new() { Temperature = double.NaN } }).Validate());
});
await Test("prompt has count, explicit contract, event truncation metadata", () =>
{
    var prompt = PromptBuilder.Build(config, context);
    Check(prompt.User.Contains("REQUESTED_COUNT=1") && prompt.User.Contains("\"omitted_events\":100")
        && prompt.System.Contains("No code") && prompt.System.Contains("JSON"));
    return Task.CompletedTask;
});
await Test("prompt size is bounded", () => Reject(() => PromptBuilder.Build(config with { MaxPromptCharacters = 1 }, context)));
await Test("oversized history trims oldest observations and preserves explicit omissions", () =>
{
    var observations = Enumerable.Range(0, 10).Select(i => JsonSerializer.SerializeToElement(new { index = i, text = new string('a', 500) })).ToArray();
    var prompt = PromptBuilder.Build(config with { MaxPromptCharacters = 4000 }, context with
        { RecentEvents = observations, TotalEvents = 10, OmittedEvents = 0 });
    Check(prompt.System.Length + prompt.User.Length <= 4000 && prompt.User.Contains("\"index\":9")
        && !prompt.User.Contains("\"index\":0") && prompt.User.Contains("omitted_events"));
    return Task.CompletedTask;
});

await Test("OpenAI-compatible route, auth, messages, JSON and token options", async () =>
{
    var handler = new FakeHandler(async (request, token) =>
    {
        Check(request.RequestUri!.ToString() == "http://localhost:1234/v1/chat/completions");
        Check(request.Headers.Authorization!.Parameter == "test-secret");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Check(body.RootElement.GetProperty("messages")[1].GetProperty("role").GetString() == "user");
        Check(body.RootElement.GetProperty("max_completion_tokens").GetInt32() == 1800);
        Check(!body.RootElement.TryGetProperty("temperature", out _));
        Check(body.RootElement.GetProperty("response_format").GetProperty("type").GetString() == "json_object");
        return Response(Wire.Encode(new CardBatch { Cards = [valid] }));
    });
    using var client = new HttpClient(handler);
    var provider = new OpenAiCardGenerator(client, new()
    { BaseUrl = "http://localhost:1234/v1/", ApiKey = "test-secret", ApiKeyEnvironmentVariable = "",
        JsonMode = true, TokenLimitParameter = "max_completion_tokens", IncludeTemperature = false });
    var result = await provider.GenerateAsync(new("s", "JSON u"), default);
    Check(result.Cards[0].Name == valid.Name);
});
await Test("environment key overrides configured key", () =>
{
    string envName = "NEOW_TEST_KEY_" + Guid.NewGuid().ToString("N");
    try
    {
        Environment.SetEnvironmentVariable(envName, "env-secret");
        Check(new ProviderConfig { ApiKeyEnvironmentVariable = envName, ApiKey = "file-secret" }.ResolveKey() == "env-secret");
    }
    finally { Environment.SetEnvironmentVariable(envName, null); }
    return Task.CompletedTask;
});
await Test("fenced JSON accepted", async () =>
{
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("```json\n" + Wire.Encode(new CardBatch { Cards = [valid] }) + "\n```"))));
    Check((await new OpenAiCardGenerator(client, new()).GenerateAsync(new("s", "u"), default)).Cards.Length == 1);
});
await Test("error body never exposed", async () =>
{
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("DO_NOT_LOG_THIS_KEY") })));
    try { await new OpenAiCardGenerator(client, new()).GenerateAsync(new("s", "u"), default); throw new Exception("Expected HTTP failure"); }
    catch (HttpRequestException ex) { Check(!ex.Message.Contains("DO_NOT_LOG") && ex.Message.Contains("401")); }
});
await Test("response bytes bounded", async () =>
{
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response(new string('x', 3000)))));
    await RejectAsync(() => new OpenAiCardGenerator(client, new() { MaxResponseBytes = 1024 }).GenerateAsync(new("s", "u"), default));
});
await Test("timeout and cancellation", async () =>
{
    using var client = new HttpClient(new FakeHandler(async (_, token) =>
    { await Task.Delay(TimeSpan.FromMinutes(1), token); return Response("{}"); }));
    await RejectAsync(() => new OpenAiCardGenerator(client, new() { TimeoutSeconds = 1 }).GenerateAsync(new("s", "u"), default));
});
await Test("truncated completion rejected", async () =>
{
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("{}", "length"))));
    await RejectAsync(() => new OpenAiCardGenerator(client, new()).GenerateAsync(new("s", "u"), default));
});

await Test("prefetch pending deduplication / no wait freeze / late results ignored", async () =>
{
    var completion = new TaskCompletionSource<CardBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var session = new GenerationSession(context.CombatKey, config, new FakeGenerator((_, _) => completion.Task), (_, _) => { });
    Check(session.TryPrefetch(context, DateTimeOffset.UtcNow));
    Check(!session.TryPrefetch(context, DateTimeOffset.UtcNow.AddSeconds(10)));
    Check(session.Freeze().Length == 0);
    completion.SetResult(new() { Cards = [valid] });
    await session.WaitForPendingAsync();
    Check(session.Freeze().Length == 0 && !session.TryPrefetch(context, DateTimeOffset.UtcNow.AddSeconds(20)));
});
await Test("latest valid result survives failed refresh / request budget / wrong combat", async () =>
{
    int call = 0;
    using var session = new GenerationSession(context.CombatKey, config, new FakeGenerator((_, _) =>
        ++call == 1 ? Task.FromResult(new CardBatch { Cards = [valid] }) : Task.FromException<CardBatch>(new FormatException("secret"))), (_, _) => { });
    var now = DateTimeOffset.UtcNow;
    Check(!session.TryPrefetch(context with { CombatKey = "other" }, now));
    Check(session.TryPrefetch(context, now)); await session.WaitForPendingAsync();
    Check(!session.TryPrefetch(context, now.AddMilliseconds(100)));
    Check(session.TryPrefetch(context, now.AddSeconds(2))); await session.WaitForPendingAsync();
    Check(!session.TryPrefetch(context, now.AddSeconds(4)));
    Check(session.Freeze().Single().Name == valid.Name);
});
await Test("invalid batch never published / audit redaction", async () =>
{
    var audit = new List<string>();
    using var session = new GenerationSession(context.CombatKey, config, new FakeGenerator((_, _) =>
        Task.FromException<CardBatch>(new Exception("SECRET_FROM_PROVIDER"))), (kind, data) => audit.Add(kind + Wire.Encode(data)));
    session.TryPrefetch(context, DateTimeOffset.UtcNow); await session.WaitForPendingAsync();
    Check(session.Freeze().Length == 0 && audit.Any(x => x.StartsWith("generation_failed"))
        && !audit.Any(x => x.Contains("SECRET_FROM_PROVIDER")));
});
await Test("wrong card count rejected", async () =>
{
    using var session = new GenerationSession(context.CombatKey, config, new FakeGenerator((_, _) =>
        Task.FromResult(new CardBatch { Cards = [] })), (_, _) => { });
    session.TryPrefetch(context, DateTimeOffset.UtcNow); await session.WaitForPendingAsync();
    Check(session.Freeze().Length == 0);
});
await Test("atomic cache and concurrent journal retain every event", async () =>
{
    string directory = Path.Combine(args.FirstOrDefault() ?? Path.GetTempPath(), "neow-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string cache = Path.Combine(directory, "reward.json");
    AtomicStore.Write(cache, new[] { valid });
    Check(Wire.Decode<CardDefinition[]>(File.ReadAllText(cache)).Single().Name == valid.Name);
    string journal = Path.Combine(directory, "combat.jsonl");
    await using (var writer = new JsonlJournal(journal))
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => writer.Append("event", new { index = i }))));
    string[] lines = File.ReadAllLines(journal);
    Check(lines.Length == 100 && lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("payload").GetProperty("index").GetInt32()).Distinct().Count() == 100);
    File.Delete(cache); File.Delete(journal); Directory.Delete(directory);
});
Console.WriteLine($"PASS: {passed} tests");

async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static Task Reject(Action action)
{
    try { action(); } catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return Task.CompletedTask; }
    throw new Exception("Expected rejection.");
}
static async Task RejectAsync(Func<Task> action)
{
    try { await action(); } catch (Exception ex) when (ex is FormatException or JsonException or OperationCanceledException) { return; }
    throw new Exception("Expected rejection.");
}
static HttpResponseMessage Response(string content, string finish = "stop") => new(HttpStatusCode.OK)
{ Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content }, finish_reason = finish } } })) };
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
sealed class FakeGenerator(Func<Prompt, CancellationToken, Task<CardBatch>> generate) : IContentGenerator<CardBatch>
{
    public Task<CardBatch> GenerateAsync(Prompt prompt, CancellationToken cancellationToken) => generate(prompt, cancellationToken);
}
