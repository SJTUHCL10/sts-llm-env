using System.Net;
using System.Text.Json;
using Forge.Core;

int passed = 0;
var valid = new CardDefinition
{
    Name = "涅奥的低语", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Common,
    Forms = [new() { Cost = new() { Energy = 1 }, Keywords = [ForgeKeyword.Exhaust], Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = 8 }] },
             new() { Cost = new() { Energy = 0 }, Effects = [new() { Kind = EffectKind.Damage, Target = "all_enemies", Amount = 11 }, new() { Kind = EffectKind.Block, Amount = 3 }] }]
};
var complex = valid with { Name = "余烬之歌", Forms = valid.Forms.Select(f => f with { Rules = [new() { Trigger = new() { Event = RuleEvent.CardDiscarded }, Lifetime = LifetimeKind.Turn, Effects = [new() { Kind = EffectKind.Block, Amount = 2 }] }] }).ToArray() };
var engine = new CardDefinition
{
    Name = "余烬之心", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare,
    Forms = new[] { 1, 2 }.Select(n => new CardForm { Cost = new() { Energy = 2 }, Rules = [new() { Trigger = new() { Event = RuleEvent.CardExhausted }, Effects = [new() { Kind = EffectKind.Block, Amount = n }] }] }).ToArray()
};
var context = new GenerationContext
{
    CombatKey = "combat-a", Run = JsonSerializer.SerializeToElement(new { character = "IRONCLAD", deck = new[] { "Strike" } }),
    State = JsonSerializer.SerializeToElement(new { hp = 50 }), RecentEvents = [], TotalEvents = 100, OmittedEvents = 100
};
var config = new ForgeConfig { PrefetchMinimumIntervalSeconds = 1, PrefetchInitialCardPlays = 0, MaxRequestsPerCombat = 2, GenerationTiming = GenerationTiming.WaitOnReward };

await Test("two complete forms round trip and upgrade changes mechanics", () =>
{
    var loaded = CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(valid)));
    Check(loaded.Form(false).Tags.Contains(ForgeKeyword.Exhaust) && loaded.Form(true).Tags.Length == 0);
    Check(loaded.Form(true).Cost.Energy == 0 && loaded.Form(true).Immediate.Length == 2 && loaded.Form(true).Immediate[0].Target!.Ref == "all_enemies");
    string json = Wire.Encode(loaded);
    Check(!json.Contains("upgrade_") && !json.Contains("schema_version") && !json.Contains("null") && !json.Contains("repeat"));
    return Task.CompletedTask;
});
await Test("reject missing forms, old fields, mixed targets, irrelevant slots and invalid expressions", async () =>
{
    await Reject(() => Wire.Decode<CardDefinition>("{}"));
    await Reject(() => Wire.Decode<CardDefinition>(Wire.Encode(valid).Replace("\"name\":", "\"schema_version\":4,\"name\":")));
    await Reject(() => CardValidator.Validate(valid with { Forms = [valid.Forms[0]] }));
    await Reject(() => CardValidator.Validate(valid with { Name = "[bad]" }));
    foreach (var bad in new CardEffect[]
    {
        new() { Kind = EffectKind.Damage, Amount = 8 },
        new() { Kind = EffectKind.Draw, Target = "enemy", Amount = 1 },
        new() { Kind = EffectKind.Block, Amount = 1, Power = "doom" },
        new() { Kind = EffectKind.Discard, Target = new() { Pile = CardPileName.Hand, Pick = SelectionMode.All, Count = 1 } },
        new() { Kind = EffectKind.Move, Target = "selected:missing", To = CardPileName.Hand },
        new() { Kind = EffectKind.CreateCard, Card = new() { Id = "script" }, To = CardPileName.Hand },
        new() { Kind = EffectKind.ApplyPower, Power = "arbitrary_type", Amount = 1 },
        new() { Kind = EffectKind.Block, Amount = new() { Add = [1, 2], Mul = [2, 3] } },
        new() { Kind = EffectKind.Block, Amount = new() { Div = [4, 0] } }
    }) await Reject(() => CardValidator.Validate(valid with { Type = ForgeCardType.Skill, Forms = valid.Forms.Select(f => f with { Effects = [bad] }).ToArray() }));
    await Reject(() => Wire.Decode<EffectTarget>("{\"pile\":\"hand\",\"pick\":\"random\",\"script\":true}"));
    await Reject(() => Wire.Decode<NumberExpression>("{\"stat\":\"hp\",\"stat\":\"block\"}"));
});
await Test("selector bindings, filtering, creation and transformation share reusable structures", () =>
{
    var actions = new CardEffect[]
    {
        new() { Kind = EffectKind.Select, Target = new() { Pile = CardPileName.Discard, Pick = SelectionMode.Choose, Count = 2, Filter = new() { Type = "skill" } }, As = "chosen" },
        new() { Kind = EffectKind.Upgrade, Target = "selected:chosen" },
        new() { Kind = EffectKind.Move, Target = "selected:chosen", To = CardPileName.Draw, Position = "top" },
        new() { Kind = EffectKind.CreateCard, Card = new() { Id = "soul", Upgraded = true }, Count = 2, To = CardPileName.Hand },
        new() { Kind = EffectKind.Transform, Target = new() { Pile = CardPileName.Hand, Pick = SelectionMode.Random }, Card = new() { Pool = "colorless", Pick = SelectionMode.Random, Filter = new() { Type = "attack" } } }
    };
    var card = valid with { Type = ForgeCardType.Skill, Forms = valid.Forms.Select(f => f with { Effects = actions }).ToArray() };
    CardValidator.Validate(Wire.Decode<CardDefinition>(Wire.Encode(card)));
    Check(CardText.Render(card, true).Contains("变化") && CardText.Render(card, false).Contains("Transform"));
    return Task.CompletedTask;
});
await Test("reject null nested nodes and ignored subjects; temporary powers use supported native lifetimes", () =>
{
    CardDefinition With(CardEffect e) => valid with { Forms = valid.Forms.Select(f => f with { Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = 1 }, e] }).ToArray() };
    foreach (var effect in new CardEffect[]
    {
        new() { Kind = EffectKind.Block, Amount = new() { Stat = "energy", Of = "target" } },
        new() { Kind = EffectKind.Block, Amount = 1, Condition = new() { All = [null!] } },
        new() { Kind = EffectKind.Block, Amount = 1, Target = new() { Ref = "self", Pick = SelectionMode.All } },
        new() { Kind = EffectKind.ApplyPower, Power = "poison", Amount = 2, Until = LifetimeKind.Turn }
    }) Throws<FormatException>(() => CardValidator.Validate(With(effect)));
    Throws<FormatException>(() => CardValidator.Validate(valid with { Forms = valid.Forms.Select(f => f with { Rules = [null!] }).ToArray() }));
    CardValidator.Validate(With(new() { Kind = EffectKind.ApplyPower, Power = "focus", Amount = -2, Until = LifetimeKind.Turn }));
    Check(CardText.Render(With(new() { Kind = EffectKind.ApplyPower, Power = "strength", Amount = 3, Until = LifetimeKind.Turn }), false).Contains("Until the end"));
    Throws<InvalidOperationException>(() => EffectRules.Evaluate(new() { Div = [1, new() { Stat = "energy" }] }, _ => 0));
    Throws<OverflowException>(() => EffectRules.Evaluate(new() { Add = [int.MaxValue, 1] }, _ => 0));
    return Task.CompletedTask;
});

await Test("numeric expressions and conditions use current state and floor division", () =>
{
    var number = Wire.Decode<NumberExpression>("{\"add\":[4,{\"mul\":[2,{\"stat\":\"power\",\"of\":\"target\",\"id\":\"poison\"}]}]}");
    CardValidator.ValidateNumber(number);
    Check(EffectRules.Evaluate(number, _ => 3) == 10 && EffectRules.Evaluate(number, _ => 5) == 14);
    Check(EffectRules.Evaluate(new NumberExpression { Div = [-5, 2] }, _ => 0) == -3);
    Check(EffectRules.Matches(new() { Op = Comparison.Gt, Left = number, Right = 9 }, _ => 3));
    var deep = (NumberExpression)1;
    for (int i = 0; i < 8; i++) deep = new() { Add = [deep, 1] };
    return Reject(() => CardValidator.ValidateNumber(deep));
});
await Test("rule group quotas count successful conditions once and reset only their scope", () =>
{
    var rule = new CardRule { Trigger = new() { Event = RuleEvent.CardDiscarded, Limit = new() { Count = 1 } }, Effects = [new() { Kind = EffectKind.Draw, Amount = 1 }, new() { Kind = EffectKind.Block, Amount = 2 }] };
    var form = valid.Forms[0] with { Rules = [rule] };
    var state = EffectTriggerRuntime.Create(form);
    Check(!EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.CardDiscarded, conditionMatches: false));
    Check(state.Activations[0] == 0 && EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.CardDiscarded));
    Check(!EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.CardDiscarded));
    state = Wire.Decode<EffectTriggerState>(Wire.Encode(state)); EffectTriggerRuntime.Validate(form, state);
    EffectTriggerRuntime.BeginTurn(form, state); Check(EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.CardDiscarded));
    form = form with { Rules = [rule with { Trigger = rule.Trigger with { Limit = new() { Count = 1, Within = CounterScope.Combat } } }] };
    EffectTriggerRuntime.BeginTurn(form, state); Check(!EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.CardDiscarded));
    return Task.CompletedTask;
});
await Test("first nth and every use filtered event ordinals independently of quotas", () =>
{
    Check(EffectRules.MatchesOccurrence(new() { First = 2 }, 1) && !EffectRules.MatchesOccurrence(new() { First = 2 }, 3));
    Check(EffectRules.MatchesOccurrence(new() { Nth = 3 }, 3) && !EffectRules.MatchesOccurrence(new() { Nth = 3 }, 4));
    Check(EffectRules.MatchesOccurrence(new() { Every = 3 }, 6) && !EffectRules.MatchesOccurrence(new() { Every = 3 }, 5));
    var form = valid.Forms[0] with { Rules = [new() { Trigger = new() { Event = RuleEvent.CardPlayed, Filter = new() { Type = "attack" }, Occurrence = new() { First = 1 } }, Effects = [new() { Kind = EffectKind.Block, Amount = 1 }] }] };
    Check(!EffectTriggerRuntime.TryConsume(form, EffectTriggerRuntime.Create(form), 0, RuleEvent.CardPlayed, ordinal: 2));
    return Task.CompletedTask;
});
await Test("next-turn and finite lifetimes expire even when conditions fail", () =>
{
    var next = new CardRule { Trigger = new() { Event = RuleEvent.TurnStart }, Lifetime = LifetimeKind.NextTurn, Effects = [new() { Kind = EffectKind.Draw, Amount = 1 }] };
    var form = valid.Forms[0] with { Rules = [next] };
    var state = EffectTriggerRuntime.Create(form);
    EffectTriggerRuntime.EndTurn(form, state); Check(state.Remaining[0] == 1);
    Check(!EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.TurnStart, conditionMatches: false) && EffectTriggerRuntime.IsExpired(state));
    form = form with { Rules = [next with { Lifetime = LifetimeKind.Turn, Turns = 2 }] };
    state = EffectTriggerRuntime.Create(form);
    EffectTriggerRuntime.EndTurn(form, state); Check(state.Remaining[0] == 2);
    EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.TurnStart, conditionMatches: false);
    EffectTriggerRuntime.TryConsume(form, state, 0, RuleEvent.TurnStart, conditionMatches: false);
    Check(EffectTriggerRuntime.IsExpired(state));
    state.Activations[0] = -1; return Reject(() => EffectTriggerRuntime.Validate(form, state));
});
await Test("all four role extensions gate both forms, filters, rules and nested expressions", async () =>
{
    foreach (var (role, action) in new (string, CardEffect)[]
    {
        ("NECROBINDER", new() { Kind = EffectKind.Summon, Amount = 4 }),
        ("DEFECT", new() { Kind = EffectKind.Channel, Orb = "frost", Amount = 1 }),
        ("REGENT", new() { Kind = EffectKind.Forge, Amount = 5 }),
        ("SILENT", new() { Kind = EffectKind.CreateCard, Card = new() { Id = "shiv" }, To = CardPileName.Hand })
    })
    {
        var card = valid with { Type = ForgeCardType.Skill, Forms = valid.Forms.Select(f => f with { Effects = [action] }).ToArray() };
        await Reject(() => CharacterMechanics.FromRun(context.Run).Validate(card));
        CharacterMechanics.FromRun(JsonSerializer.SerializeToElement(new { character = role })).Validate(card);
        CharacterMechanics.FromRun(JsonSerializer.SerializeToElement(new { character = "IRONCLAD", relics = new[] { new { id = "PRISMATIC_GEM" } } })).Validate(card);
    }
    Check(!CharacterMechanics.FromRun(JsonSerializer.SerializeToElement(new { character = "IRONCLAD", deck = new[] { new { pool = "DEFECT_CARD_POOL", origin = "generated" } } })).Defect);
    Check(CharacterMechanics.FromRun(JsonSerializer.SerializeToElement(new { character = "IRONCLAD", deck = new[] { new { pool = "NECROBINDER_CARD_POOL" } } })).Necrobinder);
});
await Test("design projection groups cards, preserves useful mechanics and omits session/state trees", () =>
{
    var observed = context with
    {
        Run = JsonSerializer.SerializeToElement(new { character = "REGENT", seed = "hidden", deck = new[] { new { id = "VENERATE", title = "崇拜", type = "Skill", cost = 0, star_cost = 2, description = "Gain [gold]Stars[/gold]." }, new { id = "VENERATE", title = "崇拜", type = "Skill", cost = 0, star_cost = 2, description = "Gain [gold]Stars[/gold]." } } }),
        State = JsonSerializer.SerializeToElement(new { player = new { hp = 40, max_hp = 60, instance = 123 }, player_combat = new { stars = 5, piles = new[] { new { pile = "Hand", cards = new[] { "unused" } } } } }),
        GenerationHistory = [JsonSerializer.SerializeToElement(new { card = valid, status = "selected", combat_key = "hidden" }, Wire.Json)]
    };
    var projected = ObservationProjector.Project(observed);
    Check(projected.GetProperty("deck")[0].GetProperty("count").GetInt32() == 2 && projected.GetProperty("deck")[0].GetProperty("star_cost").GetInt32() == 2);
    string text = projected.GetRawText();
    foreach (string removed in new[] { "combat_key", "schema_version", "instance", "piles", "generated_definition", "seed", "[gold]" }) Check(!text.Contains(removed));
    Check(projected.GetProperty("history")[0].GetProperty("text").GetString()!.Contains("伤害"));
    Check(PromptBuilder.Build(config, observed).System.StartsWith(PromptBuilder.Contract));
    return Task.CompletedTask;
});
await Test("prompt trims only optional history and rejects an insufficient budget", async () =>
{
    var history = context with { GenerationHistory = Enumerable.Range(0, 12).Select(_ => JsonSerializer.SerializeToElement(new { card = complex, status = "shown" }, Wire.Json)).ToArray() };
    var basePrompt = PromptBuilder.Build(config, context);
    var bounded = PromptBuilder.Build(config with { MaxPromptCharacters = basePrompt.System.Length + basePrompt.User.Length + 32 }, history);
    Check(bounded.System.Length + bounded.User.Length <= basePrompt.System.Length + basePrompt.User.Length + 32);
    await Reject(() => PromptBuilder.Build(config with { MaxPromptCharacters = 1 }, context));
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
    Check(prompts.Count == 1 && !prompts[0].User.Contains("first_round_summary"));
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
        (new FakeGenerator((_, _) => Task.FromResult(new CardBatch { Cards = [valid with { Forms = [valid.Forms[0] with { Cost = new() { Energy = -1 } }, valid.Forms[1]] }] })), "Negative cost.", "validation", null),
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
await Test("resource icons preserve small upgraded counts and abbreviate large counts", () =>
{
    const string path = "res://images/packed/sprite_fonts/star_icon.png";
    string icon = $"[img]{path}[/img]";
    Check(CardText.ResourceIcons(2, "2", path) == icon + icon);
    Check(CardText.ResourceIcons(2, "[green]2[/green]", path) == "[green]" + icon + icon + "[/green]");
    Check(CardText.ResourceIcons(4, "[green]4[/green]", path) == "[green]4[/green]" + icon);
    Check(CardText.Render(engine, true).Contains("消耗"));
    Check(!CardText.Render(engine, true).Contains("最多"));
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
            usage = new { prompt_tokens = 12, completion_tokens = 34, total_tokens = 46,
                prompt_cache_hit_tokens = 8, prompt_cache_miss_tokens = 4 }
        })) };
    }));
    await RejectAsync(() => new OpenAiCardGenerator(client, new() { ApiKey = "TEST_SECRET", ApiKeyEnvironmentVariable = "", ReasoningEffort = null },
        diagnostics => response = diagnostics).GenerateAsync(new("s", "u") { Revision = 2 }, default));
    Check(response is { Revision: 2, PromptTokens: 12, CompletionTokens: 34, TotalTokens: 46, PromptCacheHitTokens: 8, PromptCacheMissTokens: 4 }
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
    Check(response?.PromptCacheHitTokens is null && response?.PromptCacheMissTokens is null);
});
await Test("standard cached-token diagnostics are read without inventing cache misses", async () =>
{
    ProviderDiagnostics? response = null;
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = Wire.Encode(new CardBatch { Cards = [valid] }) }, finish_reason = "stop" } },
        usage = new { prompt_tokens = 20, prompt_tokens_details = new { cached_tokens = 12 } }
    })) })));
    await new OpenAiCardGenerator(client, new(), value => response = value).GenerateAsync(new("s", "u"), default);
    Check(response is { PromptCacheHitTokens: 12, PromptCacheMissTokens: null });
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
    Check(pool.Add("c", [valid with { Forms = [valid.Forms[0] with { Effects = [valid.Forms[0].Immediate[0] with { Amount = 9 }] }, valid.Forms[1]] }]) == 1);
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
    pool.Add("b", [valid with { Name = "other", Forms = [valid.Forms[0] with { Effects = [valid.Forms[0].Immediate[0] with { Amount = 9 }] }, valid.Forms[1]] }]);
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
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
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
