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
var config = new ForgeConfig { PrefetchMinimumIntervalSeconds = 1, PrefetchInitialCardPlays = 0, MaxRequestsPerCombat = 2, GenerationTiming = GenerationTiming.WaitOnReward };

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
    ("future schema", valid with { SchemaVersion = 4 }),
    ("attack without damage", valid with { Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 3 }] }),
    ("skill with damage", valid with { Type = ForgeCardType.Skill }),
    ("self damage", valid with { Effects = [valid.Effects[0] with { Target = EffectTarget.Self }] }),
    ("excessive upgrade", valid with { Effects = [valid.Effects[0] with { UpgradeAmount = 16 }] }),
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
var complex = new CardDefinition
{
    SchemaVersion = 2, Name = "余烬编织", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Uncommon, Cost = 2,
    Effects =
    [
        new() { Kind = EffectKind.Damage, Target = EffectTarget.Enemy, Amount = 3, UpgradeAmount = 1, Repeat = 2,
            Condition = EffectCondition.TargetVulnerable, Scaling = EffectScaling.ExhaustSize, ScalingAmount = 1, ScalingCap = 3 },
        new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 3, UpgradeAmount = 1,
            Trigger = EffectTrigger.NextTurnStart }
    ]
};
var engine = new CardDefinition
{
    SchemaVersion = 2, Name = "余烬之心", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare, Cost = 2,
    Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 1, UpgradeAmount = 1,
        Trigger = EffectTrigger.CardExhausted, Duration = 0, MaxPerTurn = 3 }]
};
await Test("v1 old JSON keeps immediate semantics and v2 complex programs round trip", () =>
{
    var old = Wire.Decode<CardDefinition>("""
        {"schema_version":1,"name":"Old","type":"skill","rarity":"common","cost":1,
        "effects":[{"kind":"block","target":"self","amount":5,"upgrade_amount":3}]}
        """);
    CardValidator.Validate(old);
    Check(old.Effects[0].Trigger == EffectTrigger.OnPlay && old.Effects[0].Repeat == 1 && old.Effects[0].Scaling == EffectScaling.None);
    foreach (var card in new[] { complex, engine })
        Check(Wire.Encode(CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(card)))) == Wire.Encode(card));
    return Task.CompletedTask;
});
await Test("high-cost cards accept 52 to 63 damage upgrades and five-cost payloads", () =>
{
    foreach (int schema in new[] { 1, 2 })
    {
        var heavy = valid with { SchemaVersion = schema, Cost = 4, Rarity = ForgeRarity.Rare,
            Effects = [valid.Effects[0] with { Amount = 52, UpgradeAmount = 11 }] };
        CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(heavy)));
        CardValidator.Validate(heavy with { Cost = 5, Effects = [heavy.Effects[0] with { Amount = 65, UpgradeAmount = 15 }] });
    }
    Check(PromptBuilder.Contract.Contains("schema_version\":3") && !PromptBuilder.Contract.Contains("Score="));
    return Task.CompletedTask;
});
await Test("relaxed effect bounds still reject excess values, scaled totals and cheap oversized payoffs", async () =>
{
    foreach (var kind in Enum.GetValues<EffectKind>().Where(k => k != EffectKind.Stars))
    {
        var (maximum, upgrade) = CardValidator.Limits(kind);
        var effect = new CardEffect { Kind = kind,
            Target = kind is EffectKind.Damage or EffectKind.Weak or EffectKind.Vulnerable or EffectKind.Poison
                ? EffectTarget.Enemy : EffectTarget.Self, Amount = maximum - upgrade, UpgradeAmount = upgrade };
        var card = valid with { SchemaVersion = 2, Cost = 5, Rarity = ForgeRarity.Rare,
            Type = kind == EffectKind.Damage ? ForgeCardType.Attack : ForgeCardType.Skill, Effects = [effect] };
        CardValidator.Validate(card);
        await Reject(() => CardValidator.Validate(card with { Effects = [effect with { Amount = maximum + 1 }] }));
        await Reject(() => CardValidator.Validate(card with { Effects = [effect with { UpgradeAmount = upgrade + 1 }] }));
        await Reject(() => CardValidator.Validate(card with { Effects = [effect with
            { Scaling = EffectScaling.HandSize, ScalingAmount = 1, ScalingCap = 1 }] }));
    }
    await Reject(() => CardValidator.Validate(valid with { SchemaVersion = 2, Cost = 6 }));
    await Reject(() => CardValidator.Validate(valid with { SchemaVersion = 2, Cost = 1,
        Effects = [valid.Effects[0] with { Amount = 52, UpgradeAmount = 11 }] }));
    await Reject(() => CardValidator.Validate(valid with { SchemaVersion = 2, Cost = 5,
        Effects = [valid.Effects[0] with { Amount = 52, UpgradeAmount = 11, Repeat = 2 }] }));
});
foreach (var (name, invalid) in new (string, CardDefinition)[]
{
    ("v1 complex semantics", complex with { SchemaVersion = 1 }),
    ("unknown trigger", complex with { Effects = [complex.Effects[0] with { Trigger = (EffectTrigger)999 }] }),
    ("too many repeats", complex with { Effects = [complex.Effects[0] with { Repeat = 4 }] }),
    ("negative duration", engine with { Effects = [engine.Effects[0] with { Duration = -1 }] }),
    ("permanent skill", engine with { Type = ForgeCardType.Skill }),
    ("immediate permanent", engine with { Effects = [engine.Effects[0] with { Trigger = EffectTrigger.OnPlay }] }),
    ("power with immediate damage", complex with { Type = ForgeCardType.Power }),
    ("power without persistent effects", valid with { SchemaVersion = 2, Type = ForgeCardType.Power,
        Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 5 }] }),
    ("exhaust power", engine with { Keywords = [ForgeKeyword.Exhaust] }),
    ("retain power", engine with { Keywords = [ForgeKeyword.Retain] }),
    ("delayed selected target", complex with { Effects = [complex.Effects[0] with { Trigger = EffectTrigger.NextTurnStart }] }),
    ("next turn duration", complex with { Effects = [complex.Effects[1] with { Duration = 2 }] }),
    ("turn trigger activation quota", complex with { Effects = [complex.Effects[1] with { Trigger = EffectTrigger.TurnStart, MaxPerTurn = 2 }], Type = ForgeCardType.Skill }),
    ("missing scaling slots", complex with { Effects = [complex.Effects[0] with { ScalingCap = 0 }] }),
    ("unused scaling slots", complex with { Effects = [complex.Effects[0] with { Scaling = EffectScaling.None }] }),
    ("scaled amount bound", complex with { Effects = [complex.Effects[0] with { Amount = 78 }] }),
    ("target condition on self", engine with { Effects = [engine.Effects[0] with { Condition = EffectCondition.TargetWeak }] }),
    ("target scaling on self", engine with { Effects = [engine.Effects[0] with { Scaling = EffectScaling.TargetPoison, ScalingAmount = 1, ScalingCap = 1 }] }),
    ("repeat budget", complex with { Cost = 0, Effects = [valid.Effects[0] with { Amount = 5, UpgradeAmount = 0, Repeat = 3 }] }),
    ("persistent event budget", engine with { Effects = [engine.Effects[0] with { Kind = EffectKind.Strength }] }),
    ("zero cost return loop", engine with { Type = ForgeCardType.Skill, Cost = 0,
        Effects = [new() { Kind = EffectKind.ReturnRandomDiscard, Target = EffectTarget.Self, Amount = 1 }] })
}) await Test("reject complex " + name, () => Reject(() => CardValidator.Validate(invalid)));

await Test("all advertised triggers and card movement routes are valid within budgets", () =>
{
    foreach (var trigger in Enum.GetValues<EffectTrigger>())
        CardValidator.Validate(new CardDefinition
        {
            SchemaVersion = 2, Name = "触发", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Rare, Cost = 3,
            Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 1, Trigger = trigger,
                Duration = trigger == EffectTrigger.OnPlay || trigger == EffectTrigger.NextTurnStart ? 1 : 3,
                MaxPerTurn = EffectRules.IsEvent(trigger) ? 3 : 1 }]
        });
    foreach (var kind in new[] { EffectKind.DiscardRandomHand, EffectKind.ExhaustRandomHand, EffectKind.ReturnRandomDiscard })
        CardValidator.Validate(complex with { Type = ForgeCardType.Skill,
            Effects = [new() { Kind = kind, Target = EffectTarget.Self, Amount = 2, UpgradeAmount = 1 }] });
    CardValidator.Validate(complex with { Type = ForgeCardType.Skill,
        Effects = [new() { Kind = EffectKind.Damage, Target = EffectTarget.RandomEnemy, Amount = 4,
            Trigger = EffectTrigger.TurnEnd, Duration = 3 }] });
    return Task.CompletedTask;
});
await Test("scaling clamps state and budget prices upgrade, repeats, AoE and full lifetime", () =>
{
    var effect = complex.Effects[0];
    Check(EffectRules.ResolveAmount(effect, 4, -3) == 4 && EffectRules.ResolveAmount(effect, 4, 2) == 6
        && EffectRules.ResolveAmount(effect, 4, 1000) == 7 && EffectRules.MaximumAmount(effect) == 7);
    Check(EffectRules.BudgetActivations(engine.Effects[0]) == 18);
    return Task.CompletedTask;
});
await Test("next turn triggers survive arming turn and fire exactly once even after persistence", () =>
{
    var state = EffectTriggerRuntime.Create(complex);
    EffectTriggerRuntime.EndTurn(complex, state);
    Check(state.Remaining[0] == 0 && state.Remaining[1] == 1);
    state = Wire.Decode<EffectTriggerState>(Wire.Encode(state));
    EffectTriggerRuntime.Validate(complex, state);
    EffectTriggerRuntime.BeginTurn(state);
    Check(EffectTriggerRuntime.TryConsume(complex, state, 1, EffectTrigger.NextTurnStart));
    Check(!EffectTriggerRuntime.TryConsume(complex, state, 1, EffectTrigger.NextTurnStart) && EffectTriggerRuntime.IsExpired(state));
    return Task.CompletedTask;
});
await Test("finite turn and event triggers expire independently and combat-long triggers remain", () =>
{
    var definition = engine with { Effects =
    [
        engine.Effects[0] with { Trigger = EffectTrigger.CardPlayed, Duration = 2 },
        engine.Effects[0] with { Trigger = EffectTrigger.TurnStart, Duration = 2, MaxPerTurn = 1 },
        engine.Effects[0] with { Trigger = EffectTrigger.TurnEnd, Duration = 1, MaxPerTurn = 1 },
        engine.Effects[0]
    ] };
    var state = EffectTriggerRuntime.Create(definition);
    Check(EffectTriggerRuntime.TryConsume(definition, state, 2, EffectTrigger.TurnEnd));
    EffectTriggerRuntime.EndTurn(definition, state);
    Check(state.Remaining.SequenceEqual(new[] { 1, 2, 0, -1 }));
    EffectTriggerRuntime.BeginTurn(state);
    Check(EffectTriggerRuntime.TryConsume(definition, state, 1, EffectTrigger.TurnStart));
    Check(!EffectTriggerRuntime.TryConsume(definition, state, 1, EffectTrigger.TurnStart));
    EffectTriggerRuntime.EndTurn(definition, state);
    Check(state.Remaining.SequenceEqual(new[] { 0, 1, 0, -1 }));
    EffectTriggerRuntime.BeginTurn(state);
    Check(EffectTriggerRuntime.TryConsume(definition, state, 1, EffectTrigger.TurnStart));
    Check(!EffectTriggerRuntime.IsExpired(state) && state.Remaining[1] == 0);
    return Task.CompletedTask;
});
await Test("event quota is consumed before nested execution, persists, and resets on owner turn", () =>
{
    var state = EffectTriggerRuntime.Create(engine);
    Check(!EffectTriggerRuntime.TryConsume(engine, state, 0, EffectTrigger.CardDrawn));
    for (int i = 0; i < 3; i++) Check(EffectTriggerRuntime.TryConsume(engine, state, 0, EffectTrigger.CardExhausted));
    state = Wire.Decode<EffectTriggerState>(Wire.Encode(state));
    EffectTriggerRuntime.Validate(engine, state);
    Check(!EffectTriggerRuntime.TryConsume(engine, state, 0, EffectTrigger.CardExhausted));
    EffectTriggerRuntime.EndTurn(engine, state);
    Check(state.Remaining[0] == -1);
    EffectTriggerRuntime.BeginTurn(state);
    Check(EffectTriggerRuntime.TryConsume(engine, state, 0, EffectTrigger.CardExhausted));
    return Task.CompletedTask;
});
await Test("reject corrupted lifecycle arrays and impossible saved quotas", async () =>
{
    var state = EffectTriggerRuntime.Create(complex);
    await Reject(() => EffectTriggerRuntime.Validate(complex, state with { Remaining = [] }));
    await Reject(() => EffectTriggerRuntime.Validate(complex, state with { Remaining = [0, -1] }));
    await Reject(() => EffectTriggerRuntime.Validate(complex, state with { Remaining = [0, 2] }));
    await Reject(() => EffectTriggerRuntime.Validate(complex, state with { Activations = [0, 4] }));
});
await Test("description projects repeat, conditional scaling, triggers and bilingual limits", () =>
{
    var zh = CardText.Render(complex, true, i => $"{{E{i}:diff()}}");
    var en = CardText.Render(engine, false, _ => "2");
    Check(zh.Contains("下回合") && zh.Contains("易伤") && zh.Contains("重复 2 次") && zh.Contains("最多计 3")
        && zh.Contains("{E0:diff()}") && en.Contains("For this combat") && en.Contains("3 times per turn"));
    Check(PromptBuilder.Contract.Contains("schema_version\":3") && PromptBuilder.Contract.Contains("card_exhausted")
        && PromptBuilder.Contract.Contains("scaling_cap"));
    return Task.CompletedTask;
});
await Test("v2 complete batch publication and invalid complex refresh preserve previous definitions", async () =>
{
    int call = 0;
    using var session = new GenerationSession(context.CombatKey, config with { GeneratedCardsPerReward = 2 },
        new FakeGenerator((_, _) => Task.FromResult(new CardBatch
        {
            Cards = ++call == 1 ? [complex, engine] : [complex, engine with
                { Effects = [engine.Effects[0] with { Trigger = (EffectTrigger)999 }] }]
        })), (_, _) => { });
    var now = DateTimeOffset.UtcNow;
    Check(session.TryPrefetch(context, now)); await session.WaitForPendingAsync();
    Check(session.TryPrefetch(context, now.AddSeconds(2))); await session.WaitForPendingAsync();
    var frozen = session.Freeze();
    Check(frozen.Length == 2 && frozen.All(c => c.SchemaVersion == 2) && frozen[1].Effects[0].Trigger == EffectTrigger.CardExhausted);
});
await Test("configuration bounds", async () =>
{
    config.Validate();
    await Reject(() => (config with { GeneratedCardsPerReward = 4 }).Validate());
    await Reject(() => (config with { Provider = new() { BaseUrl = "file:///C:/x" } }).Validate());
    await Reject(() => (config with { ActiveStyle = "missing" }).Validate());
    await Reject(() => (config with { Provider = new() { Temperature = double.NaN } }).Validate());
    await Reject(() => (config with { PrefetchInitialCardPlays = -1 }).Validate());
    await Reject(() => (config with { PrefetchInitialCardPlays = 11 }).Validate());
});
await Test("deprecated opening-play config stays readable but new enemy-turn gating is independent", () =>
{
    Check(Wire.Decode<ForgeConfig>("{}").PrefetchInitialCardPlays == 2);
    foreach (int threshold in new[] { 0, 2, 10 })
    {
        var loaded = Wire.Decode<ForgeConfig>(Wire.Encode(config with { PrefetchInitialCardPlays = threshold }));
        loaded.Validate();
        Check(loaded.PrefetchInitialCardPlays == threshold);
    }
    return Task.CompletedTask;
});
await Test("prompt has count, explicit contract, event truncation metadata", () =>
{
    var prompt = PromptBuilder.Build(config, context);
    Check(prompt.User.Contains("REQUESTED_COUNT=1") && prompt.User.Contains("\"omitted_events\":100")
        && prompt.System.Contains("No code") && prompt.System.Contains("JSON"));
    return Task.CompletedTask;
});
await Test("prompt size is bounded", () => Reject(() => PromptBuilder.Build(config with { MaxPromptCharacters = 1 }, context)));
await Test("v3 contract fits the minimum configured prompt size with a small observation", () =>
{
    var prompt = PromptBuilder.Build(config with { MaxPromptCharacters = 4000 }, context);
    Check(prompt.System.Length + prompt.User.Length <= 4000);
    return Task.CompletedTask;
});
await Test("oversized history trims oldest observations and preserves explicit omissions", () =>
{
    var observations = Enumerable.Range(0, 10).Select(i => JsonSerializer.SerializeToElement(new { index = i, text = new string('a', 500) })).ToArray();
    var prompt = PromptBuilder.Build(config with { MaxPromptCharacters = 6500 }, context with
        { RecentEvents = observations, TotalEvents = 10, OmittedEvents = 0 });
    Check(prompt.System.Length + prompt.User.Length <= 6500 && prompt.User.Contains("\"index\":9")
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
        Check(body.RootElement.GetProperty("max_completion_tokens").GetInt32() == 4096);
        Check(!body.RootElement.TryGetProperty("temperature", out _));
        Check(!body.RootElement.TryGetProperty("reasoning_effort", out _));
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
await Test("reasoning effort config validates and round trips without changing old configs", async () =>
{
    Check(Wire.Decode<ProviderConfig>("{}").ReasoningEffort is null);
    foreach (string? effort in new string?[] { null, "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" })
    {
        var configured = config with { Provider = new() { ReasoningEffort = effort } };
        var loaded = Wire.Decode<ForgeConfig>(Wire.Encode(configured));
        loaded.Validate();
        Check(loaded.Provider.ReasoningEffort == effort);
    }
    foreach (string invalid in new[] { "", "LOW", "automatic", "disabled" })
        await Reject(() => (config with { Provider = new() { ReasoningEffort = invalid } }).Validate());
});
await Test("reasoning effort sends exactly the selected value and ignores reasoning content when decoding cards", async () =>
{
    foreach (string effort in new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" })
    {
        using var client = new HttpClient(new FakeHandler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(body.RootElement.GetProperty("reasoning_effort").GetString() == effort);
            Check(!body.RootElement.TryGetProperty("thinking", out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = Wire.Encode(new CardBatch { Cards = [valid] }),
                        reasoning_content = "This is not card JSON." }, finish_reason = "stop" } }
                }))
            };
        }));
        var batch = await new OpenAiCardGenerator(client, new() { ReasoningEffort = effort }).GenerateAsync(new("s", "u"), default);
        Check(batch.Cards.Single().Name == valid.Name);
    }
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

await Test("initial prefetch waits for the enemy turn, not opening plays, without spending budget", async () =>
{
    var prompts = new List<Prompt>();
    using var session = new GenerationSession(context.CombatKey, config with { GenerationTiming = GenerationTiming.Prefetch },
        new FakeGenerator((prompt, _) => { prompts.Add(prompt); return Task.FromResult(new CardBatch { Cards = [valid] }); }), (_, _) => { });
    var now = DateTimeOffset.UtcNow;
    Check(!session.TryPrefetch(context, now));
    Check(!session.HasRequests);
    var observed = context with { FirstRoundSummary = JsonSerializer.SerializeToElement(new { damage_taken = 0, enemy_acted = true }) };
    Check(session.TryPrefetch(observed, now, firstEnemyTurnEnded: true));
    await session.WaitForPendingAsync();
    Check(prompts.Count == 1 && prompts[0].User.Contains("enemy_acted"));
    Check(!session.TryPrefetch(context, now.AddMilliseconds(500)));
    Check(session.TryPrefetch(context, now.AddSeconds(2)));
    await session.WaitForPendingAsync();
    Check(prompts.Count == 2 && !session.TryPrefetch(context, now.AddSeconds(4)));
});
await Test("short combat final summary can start initial generation and reward mode bypasses gating", async () =>
{
    using var shortCombat = new GenerationSession(context.CombatKey, config with { GenerationTiming = GenerationTiming.Prefetch },
        new FakeGenerator((_, _) => Task.FromResult(new CardBatch { Cards = [valid] })), (_, _) => { });
    Check(shortCombat.TryPrefetch(context, DateTimeOffset.UtcNow, combatEnded: true));
    await shortCombat.WaitForPendingAsync();
    using var reward = new GenerationSession(context.CombatKey, config,
        new FakeGenerator((_, _) => Task.FromResult(new CardBatch { Cards = [valid] })), (_, _) => { });
    Check(reward.TryPrefetch(context, DateTimeOffset.UtcNow)); await reward.WaitForPendingAsync();
});
await Test("sealed combat does not schedule another request", () =>
{
    using var session = new GenerationSession(context.CombatKey, config,
        new FakeGenerator((_, _) => throw new Exception("Should not call provider.")), (_, _) => { });
    session.Seal();
    Check(!session.TryPrefetch(context, DateTimeOffset.UtcNow, firstEnemyTurnEnded: true, combatEnded: true));
    return Task.CompletedTask;
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
await Test("generation diagnostics without prompts distinguish provider, validation and HTTP failures", async () =>
{
    foreach (var (generator, reason, stage, status) in new (IContentGenerator<CardBatch>, string, string, int?)[]
    {
        (new FakeGenerator((_, _) => Task.FromException<CardBatch>(new GenerationFailureException("completion_token_limit"))), "completion_token_limit", "provider", null),
        (new FakeGenerator((_, _) => Task.FromResult(new CardBatch { Cards = [valid with { Cost = -1 }] })), "Invalid card type/rarity/cost.", "validation", null),
        (new FakeGenerator((_, _) => Task.FromException<CardBatch>(new HttpRequestException("SECRET", null, HttpStatusCode.TooManyRequests))), "unspecified_failure", "provider", 429)
    })
    {
        var entries = new List<(string Kind, JsonElement Payload)>();
        using var session = new GenerationSession(context.CombatKey, config with { RecordGenerationPrompts = false,
            Provider = config.Provider with { ReasoningEffort = "low" } }, generator,
            (kind, value) => entries.Add((kind, JsonSerializer.SerializeToElement(value, Wire.Json))));
        Check(session.TryPrefetch(context, DateTimeOffset.UtcNow));
        await session.WaitForPendingAsync();
        var request = entries.Single(e => e.Kind == "generation_request").Payload;
        Check(request.GetProperty("prompt").ValueKind == JsonValueKind.Null && request.GetProperty("prompt_characters").GetInt32() > 0);
        Check(request.GetProperty("reasoning_effort").GetString() == "low");
        var failure = entries.Single(e => e.Kind == "generation_failed").Payload;
        Check(failure.GetProperty("reason").GetString() == reason && failure.GetProperty("stage").GetString() == stage);
        Check(status is null ? failure.GetProperty("http_status").ValueKind == JsonValueKind.Null
            : failure.GetProperty("http_status").GetInt32() == status);
        Check(failure.GetProperty("elapsed_ms").GetInt64() >= 0 && !failure.GetRawText().Contains("SECRET"));
    }
});
await Test("provider format diagnostics do not expose response text", async () =>
{
    foreach (var (response, reason) in new (HttpResponseMessage, string)[]
    {
        (Response("SECRET", "length"), "completion_token_limit"),
        (Response("SECRET", "content_filter"), "content_filtered"),
        (Response("SECRET"), "invalid_response_json_or_schema"),
        (new(HttpStatusCode.OK) { Content = new StringContent("{}") }, "invalid_response_shape")
    })
    {
        using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(response)));
        try
        {
            await new OpenAiCardGenerator(client, new()).GenerateAsync(new("s", "u"), default);
            throw new Exception("Expected provider format rejection.");
        }
        catch (GenerationFailureException ex) { Check(ex.Reason == reason && !ex.ToString().Contains("SECRET")); }
    }
});
await Test("freezing a pending request records cancellation instead of timeout", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    string? reason = null;
    using var session = new GenerationSession(context.CombatKey, config, new FakeGenerator(async (_, token) =>
    {
        started.SetResult();
        await Task.Delay(Timeout.Infinite, token);
        return new CardBatch { Cards = [valid] };
    }), (kind, value) =>
    {
        if (kind == "generation_failed") reason = JsonSerializer.SerializeToElement(value, Wire.Json).GetProperty("reason").GetString();
    });
    Check(session.TryPrefetch(context, DateTimeOffset.UtcNow));
    await started.Task;
    Check(session.Freeze().Length == 0);
    await session.WaitForPendingAsync();
    Check(reason == "reward_frozen_or_session_ended");
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
await Test("v3 removes balance ceilings while legacy cards retain caps and budgets", async () =>
{
    var powerful = valid with { SchemaVersion = 3, Cost = 0, Effects = [valid.Effects[0] with
        { Amount = 100, UpgradeAmount = 50, Repeat = 4, Scaling = EffectScaling.SelfBlock, ScalingAmount = 4, ScalingCap = 0 }] };
    CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(powerful)));
    CardValidator.Validate(powerful with { Cost = 8 });
    CardValidator.Validate(powerful with { Type = ForgeCardType.Skill, Effects = [new() { Kind = EffectKind.Draw, Target = EffectTarget.Self, Amount = 8 }] });
    await Reject(() => CardValidator.Validate(powerful with { SchemaVersion = 2 }));
    Check(EffectRules.ResolveAmount(powerful.Effects[0], 150, 100) == 550);
    Check(EffectRules.ResolveAmount(complex.Effects[0], 4, 100) == 7);
});
await Test("v3 unlimited events persist past three activations and finite durations still expire", () =>
{
    var definition = engine with { SchemaVersion = 3, Cost = 0, Effects = [engine.Effects[0] with { MaxPerTurn = 0, Duration = 5 }] };
    CardValidator.Validate(definition);
    var state = EffectTriggerRuntime.Create(definition);
    for (int i = 0; i < 20; i++) Check(EffectTriggerRuntime.TryConsume(definition, state, 0, EffectTrigger.CardExhausted));
    state = Wire.Decode<EffectTriggerState>(Wire.Encode(state)); EffectTriggerRuntime.Validate(definition, state);
    Check(state.Activations[0] == 20);
    for (int i = 0; i < 5; i++) EffectTriggerRuntime.EndTurn(definition, state);
    Check(EffectTriggerRuntime.IsExpired(state));
    return Task.CompletedTask;
});
await Test("v3 still rejects invalid executable programs and incompatible legacy star mechanics", async () =>
{
    var card = valid with { SchemaVersion = 3, StarCost = 2, UpgradeStarCost = 1 };
    CardValidator.Validate(card);
    foreach (var invalid in new[] { card with { StarCost = -2 }, card with { UpgradeStarCost = 3 },
        card with { UpgradeCost = 2 }, card with { SchemaVersion = 2 },
        card with { Effects = [card.Effects[0] with { Repeat = 0 }] },
        card with { Effects = [card.Effects[0] with { Scaling = EffectScaling.HandSize }] },
        card with { Effects = [card.Effects[0] with { Trigger = EffectTrigger.CardPlayed, MaxPerTurn = 0 }] },
        card with { Effects = [card.Effects[0] with { Amount = int.MaxValue, UpgradeAmount = 1 }] } })
        await Reject(() => CardValidator.Validate(invalid));
    CardValidator.Validate(card with { Type = ForgeCardType.Skill, Effects = [new()
        { Kind = EffectKind.Stars, Target = EffectTarget.Self, Amount = 3 }] });
});
await Test("duration one text says this turn and unlimited scaling/events omit quota clauses", () =>
{
    var effect = engine.Effects[0] with { Duration = 1, MaxPerTurn = 0, Scaling = EffectScaling.SelfStars, ScalingAmount = 2, ScalingCap = 0 };
    string zh = CardText.RenderEffect(effect, true, "3"), en = CardText.RenderEffect(effect, false, "3");
    Check(zh.Contains("本回合，") && !zh.Contains("0 个回合") && !zh.Contains("最多") && zh.Contains("星数"));
    Check(en.Contains("This turn, ") && !en.Contains("next 0") && !en.Contains("at most"));
    Check(CardText.RenderEffect(effect with { MaxPerTurn = 2, ScalingCap = 3 }, true, "3").Contains("最多 2 次"));
    return Task.CompletedTask;
});
await Test("provider logs reasoning, usage and revision before malformed final content and redacts credentials", async () =>
{
    ProviderDiagnostics? response = null;
    using var client = new HttpClient(new FakeHandler(async (request, token) =>
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Check(!body.RootElement.TryGetProperty("reasoning_effort", out _));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "invalid", reasoning_content = "idea TEST_SECRET" }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 12, completion_tokens = 34, total_tokens = 46 }
        })) };
    }));
    await RejectAsync(() => new OpenAiCardGenerator(client, new() { ApiKey = "TEST_SECRET", ApiKeyEnvironmentVariable = "", ReasoningEffort = null },
        diagnostics => response = diagnostics).GenerateAsync(new("s", "u") { Revision = 2 }, default));
    Check(response is { Revision: 2, PromptTokens: 12, CompletionTokens: 34, TotalTokens: 46 }
        && response.ReasoningContent == "idea [redacted]");
});
await Test("truncated responses without a message still retain token-limit diagnosis", async () =>
{
    ProviderDiagnostics? diagnostics = null;
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent("{\"choices\":[{\"finish_reason\":\"length\"}]}") })));
    try { await new OpenAiCardGenerator(client, new(), value => diagnostics = value).GenerateAsync(new("s", "u"), default);
        throw new Exception("Expected truncation failure."); }
    catch (GenerationFailureException ex) { Check(ex.Reason == "completion_token_limit" && diagnostics?.FinishReason == "length"); }
});
await Test("missing reasoning is logged as null independently of final card decoding", async () =>
{
    ProviderDiagnostics? response = null;
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response(Wire.Encode(new CardBatch { Cards = [valid] })))));
    var batch = await new OpenAiCardGenerator(client, new(), diagnostics => response = diagnostics).GenerateAsync(new("s", "u"), default);
    Check(batch.Cards.Length == 1 && response is { ReasoningContent: null, FinishReason: "stop" });
});
await Test("compact observations group deck copies and preserve star costs, generated origin and summaries", () =>
{
    var card = new { instance = 1, id = "FALLING_STAR", title = "陨星", type = "Attack", rarity = "Basic", cost = 0,
        star_cost = 2, current_star_cost = 2, current_cost = 0, upgraded = false, description = "damage", generated_definition = (CardDefinition?)null };
    var generated = new { instance = 2, id = "NEOW_GENERATED_CARD", title = "gift", type = "Skill", rarity = "Common", cost = 1,
        star_cost = -1, current_star_cost = -1, current_cost = 1, upgraded = false, description = "block", generated_definition = valid };
    var source = context with
    {
        Run = JsonSerializer.SerializeToElement(new { character = "REGENT", deck = new object[] { card, card, generated }, seed = "omit" }),
        State = JsonSerializer.SerializeToElement(new { player = new { id = "REGENT", hp = 50, side = "Player", powers = new[] { new
            { id = "BUFF", amount = 2, state = new { PackedIconPath = "omit", IsCanonical = false } } } },
            player_combat = new { stars = 5, energy = 3, other_resources = new { PrivateStuff = "omit" },
                piles = new[] { new { pile = "Hand", cards = new object[] { card, generated } } } } }),
        FirstRoundSummary = JsonSerializer.SerializeToElement(new { damage_taken = 4 })
    };
    var compact = ObservationProjector.Compact(source);
    Check(compact.Run.GetProperty("deck").GetArrayLength() == 2 && compact.Run.GetProperty("deck")[0].GetProperty("count").GetInt32() == 2);
    Check(compact.Run.GetProperty("deck")[0].GetProperty("star_cost").GetInt32() == 2);
    Check(compact.State.GetProperty("player_combat").GetProperty("piles")[0].GetProperty("cards")[1].GetProperty("origin").GetString() == "generated");
    Check(!Wire.Encode(compact).Contains("PackedIconPath") && !Wire.Encode(compact).Contains("PrivateStuff") && !Wire.Encode(compact).Contains("\"omit\""));
    Check(compact.FirstRoundSummary!.Value.GetProperty("damage_taken").GetInt32() == 4);
    return Task.CompletedTask;
});
await Test("combat summaries retain opening card sequence counts and enemy damage after event trimming", () =>
{
    var summary = new CombatSummary();
    summary.Add(JsonSerializer.SerializeToElement(new { detail = new { type = "CardPlayFinishedEntry", fields = new
        { Actor = new { id = "REGENT" }, CardPlay = new { card = new { id = "VENERATE", title = "崇拜", type = "Skill", origin = "native" },
            resources = new { EnergySpent = 1, StarsSpent = 0 } } } } }), "REGENT");
    summary.Add(JsonSerializer.SerializeToElement(new { detail = new { type = "CreatureAttackedEntry", fields = new
        { Actor = new { id = "MONSTER" }, DamageResults = new[] { new { Receiver = new { id = "REGENT" }, UnblockedDamage = 3, BlockedDamage = 5 } } } } }), "REGENT");
    var snapshot = summary.Snapshot(true);
    Check(snapshot.GetProperty("damage_taken").GetInt32() == 3 && snapshot.GetProperty("damage_blocked").GetInt32() == 5
        && snapshot.GetProperty("cards_played")[0].GetProperty("title").GetString() == "崇拜"
        && snapshot.GetProperty("energy_spent").GetInt32() == 1);
    return Task.CompletedTask;
});
await Test("candidate pool retains multiple batches, deduplicates mechanics and restores frozen rewards", () =>
{
    string directory = TestDirectory();
    string path = Path.Combine(directory, "pool.json");
    var pool = new CandidatePool(path, "run-a", 3);
    Check(pool.Add("a", [valid, complex]) == 2);
    Check(pool.Add("b", [valid with { Name = "renamed", Flavor = "new" }, engine]) == 1);
    var first = pool.FreezeReward("floor-a", 1);
    Check(first.Length == 1 && pool.HasCandidates);
    Check(pool.Add("c", [valid with { Effects = [valid.Effects[0] with { Amount = 9 }] }]) == 1);
    Check(Wire.Encode(first) == Wire.Encode(pool.FreezeReward("floor-a", 3)));
    pool = new CandidatePool(path, "run-a", 3);
    Check(Wire.Encode(first) == Wire.Encode(pool.FreezeReward("floor-a", 1)));
    Check(pool.FreezeReward("floor-b", 3).Length == 3);
    Check(pool.Add("d", [valid with { Name = "another name" }]) == 0);
    pool.RecordChoice("floor-a", first);
    Check(pool.History().Any(c => c.Status == "selected"));
    Directory.Delete(directory, true);
    return Task.CompletedTask;
});
await Test("reward freeze keeps pending requests alive and late results enter the next reward", async () =>
{
    string directory = TestDirectory();
    var pool = new CandidatePool(Path.Combine(directory, "pool.json"), "run", 3);
    var completion = new TaskCompletionSource<CardBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken providerToken = default;
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var session = new GenerationSession(context.CombatKey, config,
        new FakeGenerator((_, token) => { providerToken = token; started.SetResult(); return completion.Task; }), (_, _) => { }, cards => pool.Add("a", cards));
    Check(session.TryPrefetch(context, DateTimeOffset.UtcNow)); await started.Task;
    Check(pool.FreezeReward("a", 1).Length == 0);
    session.Seal(); Check(!providerToken.IsCancellationRequested);
    completion.SetResult(new() { Cards = [valid] }); await session.WaitForPendingAsync();
    Check(pool.FreezeReward("a", 1).Length == 0 && pool.FreezeReward("b", 1).Single().Name == valid.Name);
    Directory.Delete(directory, true);
});
await Test("pool closure rejects stale results, capacity expires oldest and corrupt or failed writes do not consume cards", async () =>
{
    string directory = TestDirectory();
    string path = Path.Combine(directory, "pool.json");
    var pool = new CandidatePool(path, "run", 3);
    pool.Add("a", [valid, complex, engine]);
    pool.Add("b", [valid with { Name = "other", Effects = [valid.Effects[0] with { Amount = 9 }] }]);
    Check(pool.History().Any(c => c.Status == "expired"));
    await Reject(() => new CandidatePool(path, "different-run", 3));
    // Make the snapshot destination unwritable by replacing the file with a directory.
    File.Delete(path); Directory.CreateDirectory(path);
    try { pool.FreezeReward("reward", 1); throw new Exception("Expected persistence failure."); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    Check(pool.HasCandidates && !pool.HasReward("reward"));
    pool.Close(); Check(pool.Add("late", [valid]) == 0 && pool.FreezeReward("later", 1).Length == 0);
    Directory.Delete(directory, true);
});
Console.WriteLine($"PASS: {passed} tests");

async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
}
string TestDirectory()
{
    string directory = Path.Combine(args.FirstOrDefault() ?? Path.GetTempPath(), "neow-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory); return directory;
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
