namespace Forge.Core;

/// <summary>Owns only immutable observations and validated content, never game models or UI objects.</summary>
public sealed class GenerationSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ForgeConfig _config;
    private readonly IContentGenerator<CardBatch> _generator;
    private readonly Action<string, object> _audit;
    private Task? _pending;
    private CardDefinition[] _ready = [];
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private int _requests;
    private bool _closed;
    public string Key { get; }

    public GenerationSession(string key, ForgeConfig config, IContentGenerator<CardBatch> generator, Action<string, object> audit)
    {
        Key = key; _config = config; _generator = generator; _audit = audit;
    }

    public bool TryPrefetch(GenerationContext context, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_closed || context.CombatKey != Key || _requests >= _config.MaxRequestsPerCombat
                || _pending is { IsCompleted: false } || now - _lastStart < TimeSpan.FromSeconds(_config.PrefetchMinimumIntervalSeconds))
                return false;
            _lastStart = now;
            int revision = ++_requests;
            // Capture the observation on the main thread, move serialization/network off it.
            _pending = Task.Run(() => Generate(context, revision));
            return true;
        }
    }

    private async Task Generate(GenerationContext context, int revision)
    {
        try
        {
            var prompt = PromptBuilder.Build(_config, context);
            if (_config.RecordGenerationPrompts) _audit("generation_request", new { revision, context.TotalEvents, prompt });
            var batch = await _generator.GenerateAsync(prompt, _lifetime.Token).ConfigureAwait(false);
            if (batch.Cards is null || batch.Cards.Length != _config.GeneratedCardsPerReward)
                throw new FormatException("Wrong card count.");
            var cards = batch.Cards.Select(CardValidator.Validate).ToArray();
            if (cards.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Length)
                throw new FormatException("Duplicate generated names.");
            lock (_gate)
            {
                if (_closed) return;
                _ready = cards;
            }
            _audit("generation_ready", new { revision, context.TotalEvents, cards });
        }
        catch (Exception ex)
        {
            // No raw provider body, key, URL, or exception message is persisted.
            _audit("generation_failed", new { revision, category = ex.GetType().Name });
        }
    }

    public async Task WaitForPendingAsync()
    {
        Task? task;
        lock (_gate) task = _pending;
        if (task is not null) await task;
    }

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
