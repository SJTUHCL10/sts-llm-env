using System.Diagnostics;

namespace Forge.Core;

/// <summary>Owns only immutable observations and validated content, never game models or UI objects.</summary>
public sealed class GenerationSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ForgeConfig _config;
    private readonly IContentGenerator<CardBatch> _generator;
    private readonly Action<string, object> _audit;
    private readonly Action<CardDefinition[]>? _publish;
    private Task? _pending;
    private CardDefinition[] _ready = [];
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private int _requests;
    private bool _closed;
    private bool _sealed;
    public string Key { get; }
    public bool HasRequests { get { lock (_gate) return _requests > 0; } }

    public GenerationSession(string key, ForgeConfig config, IContentGenerator<CardBatch> generator, Action<string, object> audit,
        Action<CardDefinition[]>? publish = null)
    {
        Key = key; _config = config; _generator = generator; _audit = audit;
        _publish = publish;
    }

    public bool TryPrefetch(GenerationContext context, DateTimeOffset now, bool firstEnemyTurnEnded = false, bool combatEnded = false)
    {
        lock (_gate)
        {
            if (_closed || _sealed || context.CombatKey != Key || _requests >= _config.MaxRequestsPerCombat
                || _pending is { IsCompleted: false } || now - _lastStart < TimeSpan.FromSeconds(_config.PrefetchMinimumIntervalSeconds))
                return false;
            // Enemy turn completion, not damage received: a fully blocked/non-attacking opening still qualifies.
            if (_requests == 0 && _config.GenerationTiming == GenerationTiming.Prefetch
                && !firstEnemyTurnEnded && !combatEnded) return false;
            _lastStart = now;
            int revision = ++_requests;
            // Capture the observation on the main thread, move serialization/network off it.
            _pending = Task.Run(() => Generate(context, revision));
            return true;
        }
    }

    private async Task Generate(GenerationContext context, int revision)
    {
        var timer = Stopwatch.StartNew();
        string stage = "prompt";
        try
        {
            var prompt = PromptBuilder.Build(_config, context) with { Revision = revision };
            _audit("generation_request", new { revision, context.TotalEvents, model = _config.Provider.Model,
                max_tokens = _config.Provider.MaxTokens, reasoning_effort = _config.Provider.ReasoningEffort,
                prompt_characters = prompt.System.Length + prompt.User.Length,
                prompt = _config.RecordGenerationPrompts ? prompt : null });
            stage = "provider";
            var batch = await _generator.GenerateAsync(prompt, _lifetime.Token).ConfigureAwait(false);
            stage = "validation";
            if (batch.Cards is null || batch.Cards.Length != _config.GeneratedCardsPerReward)
                throw new GenerationFailureException("wrong_card_count");
            CardDefinition[] cards;
            var mechanics = CharacterMechanics.FromRun(context.Run);
            try { cards = batch.Cards.Select(mechanics.Validate).ToArray(); }
            // CardValidator messages are local constants/validated enums, with no provider text.
            catch (FormatException ex) { throw new GenerationFailureException(ex.Message); }
            if (cards.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Length)
                throw new GenerationFailureException("duplicate_card_names");
            lock (_gate)
            {
                if (_closed)
                {
                    _audit("generation_discarded", new { revision, reason = "reward_frozen_or_session_ended", elapsed_ms = timer.ElapsedMilliseconds });
                    return;
                }
                stage = "persistence";
                _publish?.Invoke(cards);
                _ready = _ready.Concat(cards).DistinctBy(CandidatePool.Fingerprint).ToArray();
            }
            _audit("generation_ready", new { revision, context.TotalEvents, elapsed_ms = timer.ElapsedMilliseconds, cards });
        }
        catch (Exception ex)
        {
            // No raw provider body, key, URL, or exception message is persisted.
            string reason = ex is GenerationFailureException failure ? failure.Reason
                : ex is OperationCanceledException ? (_lifetime.IsCancellationRequested ? "reward_frozen_or_session_ended" : "provider_timeout")
                : stage == "prompt" && ex is FormatException ? "prompt_size_limit" : "unspecified_failure";
            _audit("generation_failed", new { revision, category = ex.GetType().Name, stage, reason,
                http_status = ex is HttpRequestException http ? (int?)http.StatusCode : null, elapsed_ms = timer.ElapsedMilliseconds });
        }
    }

    public async Task WaitForPendingAsync()
    {
        Task? task;
        lock (_gate) task = _pending;
        if (task is not null) await task;
    }
    // Stop future refreshes at combat end/reward display; keep the current request alive for the run pool.
    public void Seal() { lock (_gate) _sealed = true; }

    // Offline/legacy session finalization. Live rewards use CandidatePool.FreezeReward plus Seal instead.
    public CardDefinition[] Freeze()
    {
        lock (_gate)
        {
            _closed = true;
            _lifetime.Cancel();
            // Caller owns a deep copy, and a late provider cannot replace an already shown reward.
            return Wire.Decode<CardDefinition[]>(Wire.Encode(_ready));
        }
    }

    public void Dispose()
    {
        lock (_gate) { _closed = true; _lifetime.Cancel(); }
        // Pending tasks may still register with this token; CTS lives until this session is collected.
    }
}
