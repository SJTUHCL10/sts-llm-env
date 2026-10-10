using System.Text.Json;
using Forge.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Forge.Mod;

internal sealed class ForgeRuntime(string root, ForgeConfig config)
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private GenerationSession? _session;
    private JsonlJournal? _generationJournal;
    private readonly Queue<JsonElement> _events = new();
    private StateCapture _capture = new();
    private ICombatState? _combat;
    private Player? _player;
    private int _totalEvents;
    private CandidatePool? _pool;
    private readonly List<GenerationSession> _retired = new();
    private CombatSummary _summary = new();
    private JsonElement? _firstRoundSummary;
    private bool _won;
    private CombatSide? _activeTurnSide;
    private bool _firstEnemyTurnEnded;
    private bool _ended;
    private bool _frozen;
    private CardDefinition[] _frozenCards = [];
    private GenerationContext? _lastContext;
    internal ForgeConfig Config => config;
    internal bool IsSingleplayer => RunManager.Instance.NetService?.Type == NetGameType.Singleplayer;
    internal bool Eligible => config.Enabled && config.GeneratedCardsPerReward > 0 && IsSingleplayer;

    // Match native RewardsSet.WithRewardsFromRoom: the final act's bosses have no card rewards.
    internal static bool HasCombatCardReward(ICombatState combat) => combat.Encounter?.RoomType != RoomType.Boss
        || combat.RunState.CurrentActIndex < combat.RunState.Acts.Count - 1;

    public void Subscribe()
    {
        RunManager.Instance.RunStarted += _ => Bootstrap.Safe(StopRun);
        var manager = CombatManager.Instance;
        manager.CombatBegan += combat => Bootstrap.Safe(() => Begin(combat));
        manager.TurnStarted += combat => Bootstrap.Safe(() => Boundary(combat, "turn_started"));
        manager.TurnEnded += combat => Bootstrap.Safe(() => Boundary(combat, "turn_ended"));
        manager.CombatWon += room => Bootstrap.Safe(() => End(room, true));
        manager.CombatEnded += room => Bootstrap.Safe(() => End(room, false));
    }

    private void Begin(ICombatState combat)
    {
        Reset();
        if (!IsSingleplayer || !HasCombatCardReward(combat)) return;
        _player = LocalContext.GetMe(combat);
        if (_player is null) return;
        EnsurePool(_player);
        _combat = combat;
        _capture = new StateCapture();
        string key = CombatKey(_player);
        string filename = key + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".jsonl";
        if (config.RecordGeneration) _generationJournal = new JsonlJournal(Path.Combine(root, "data", "generation", filename));
        var generationJournal = _generationJournal;
        var pool = _pool!;
        _session = new GenerationSession(key, config, new OpenAiCardGenerator(Http, config.Provider,
            response => generationJournal?.Append("generation_response", response with
            { ReasoningContent = config.RecordGenerationReasoning ? response.ReasoningContent : null })),
            (kind, value) => generationJournal?.Append(kind, value), cards =>
            {
                int added = pool.Add(key, cards);
                generationJournal?.Append("generation_pool_added", new { key, added, received = cards.Length });
            });
        if (pool.HasReward(key)) _session.Seal(); // Reloading a shown reward must not farm fresh pool candidates.
        _lastContext = Context(combat);
        Audit("generation_session", new { key, floor = _player.RunState.TotalFloor, timing = config.GenerationTiming,
            initial_trigger = "first_enemy_turn_ended", run_key = pool.RunKey });
        // Initial request waits for the complete enemy opening, even when all damage is blocked.
    }

    private void Boundary(ICombatState combat, string kind)
    {
        if (_combat != combat || _ended || _player is null) return;
        if (kind == "turn_started") _activeTurnSide = combat.CurrentSide;
        // v111 switches CurrentSide before emitting TurnEnded; remember which side actually started this turn.
        if (kind == "turn_ended" && _activeTurnSide == CombatSide.Enemy && !_firstEnemyTurnEnded)
        {
            _firstEnemyTurnEnded = true;
            _firstRoundSummary = _summary.Snapshot(complete: true);
        }
        _lastContext = Context(combat);
        MaybePrefetch(_lastContext);
    }

    internal void OnHistory(ICombatState combat, CombatHistoryEntry entry)
    {
        if (_combat != combat || _player is null || _ended) return;
        var eventData = _capture.Detach(new { sequence = ++_totalEvents, round = combat.RoundNumber,
            side = combat.CurrentSide.ToString(), detail = _capture.Event(entry) });
        var promptEvent = ObservationProjector.CompactEvent(eventData);
        _summary.Add(promptEvent, _player.Character.Id.Entry);
        _events.Enqueue(promptEvent);
        while (_events.Count > config.PromptEventLimit) _events.Dequeue();
        if (entry is CardPlayFinishedEntry)
        {
            _lastContext = Context(combat);
            MaybePrefetch(_lastContext);
        }
    }

    internal void BeforeHistoryClear()
    {
        if (_combat is null || _player is null || _ended || _totalEvents == 0) return;
        _lastContext = Context(_combat);
    }

    private void MaybePrefetch(GenerationContext context)
    {
        if (Eligible && config.GenerationTiming == GenerationTiming.Prefetch)
            _session?.TryPrefetch(context, DateTimeOffset.UtcNow, firstEnemyTurnEnded: _firstEnemyTurnEnded);
    }

    private void End(CombatRoom room, bool won)
    {
        if (_ended || _combat is null || _player is null) return;
        _ended = true; _won = won;
        var finalSummary = _summary.Snapshot(complete: true, won: won);
        if (!_firstEnemyTurnEnded) _firstRoundSummary = finalSummary;
        // Cleanup may already have emptied combat piles; retain the last detached combat state.
        if (_lastContext is not null) _lastContext = _lastContext with
        { Run = _capture.Detach(_capture.Run(_player)), CombatSummary = finalSummary, FirstRoundSummary = _firstRoundSummary };
        if (won && Eligible && config.GenerationTiming == GenerationTiming.Prefetch && _session is { HasRequests: false } && _lastContext is not null)
            _session.TryPrefetch(_lastContext, DateTimeOffset.UtcNow, combatEnded: true);
        if (config.GenerationTiming == GenerationTiming.Prefetch) _session?.Seal();
        if (!won) StopRun();
    }

    private GenerationContext Context(ICombatState combat) => new()
    {
        CombatKey = _session!.Key, Run = _capture.Detach(_capture.Run(_player!)),
        State = _capture.Detach(_capture.State(combat, _player!)), RecentEvents = _events.ToArray(),
        TotalEvents = _totalEvents, OmittedEvents = _totalEvents - _events.Count,
        CombatSummary = _summary.Snapshot(), FirstRoundSummary = _firstRoundSummary,
        GenerationHistory = _pool?.History().Select(item => _capture.Detach(new
        { item.CombatKey, item.Status, card = item.Card })).ToArray() ?? []
    };

    internal async Task PrepareReward(Player player)
    {
        if (!Eligible) return;
        string key = CombatKey(player);
        if (_session?.Key != key) return;
        if (config.GenerationTiming == GenerationTiming.WaitOnReward && !_frozen && _lastContext is not null)
        {
            // Update deck/HP after combat cleanup, retaining the final combat state and sequence.
            var context = _lastContext with { Run = _capture.Detach(_capture.Run(player)) };
            _session.TryPrefetch(context, DateTimeOffset.UtcNow);
            await _session.WaitForPendingAsync(); // Return to Godot's synchronization context before creating models.
        }
    }

    internal CardDefinition[] FreezeReward(Player player)
    {
        if (!Eligible) return [];
        EnsurePool(player);
        string key = CombatKey(player);
        if (_session?.Key == key && _frozen) return _frozenCards;
        CardDefinition[]? restored = null;
        // Import pre-pool frozen rewards only if written during this run, never from an older same-seed run.
        string legacy = Path.Combine(root, "data", "rewards", key + ".json");
        if (!_pool!.HasReward(key) && File.Exists(legacy)
            && new DateTimeOffset(File.GetLastWriteTimeUtc(legacy)).ToUnixTimeSeconds() >= RunStartTime())
        {
            try { restored = Wire.Decode<CardDefinition[]>(File.ReadAllText(legacy));
                if (restored.Length > 3) restored = null; }
            catch { Log.Warn("[NeowsCompany] Invalid legacy reward cache ignored."); }
        }
        var cards = _pool.FreezeReward(key, config.GeneratedCardsPerReward, restored);
        if (_session?.Key == key)
        {
            _frozen = true; _frozenCards = cards; _session.Seal();
        }
        Audit("reward_frozen", new { key, run_key = _pool.RunKey, cards });
        return cards;
    }

    internal void RecordRewardChoice(Player player)
    {
        _pool?.RecordChoice(CombatKey(player), player.Deck.Cards.OfType<NeowGeneratedCard>().Select(c => c.Definition));
    }
    private static long RunStartTime() => (long)HarmonyLib.AccessTools.Field(typeof(RunManager), "_startTime").GetValue(RunManager.Instance)!;
    private void EnsurePool(Player player)
    {
        var run = player.RunState;
        string key = AtomicStore.Key($"pool-v1|{RunStartTime()}|{run.Rng.StringSeed}|{run.AscensionLevel}|{player.Character.Id.Entry}");
        if (_pool?.RunKey == key) return;
        _pool?.Close();
        _pool = new CandidatePool(Path.Combine(root, "data", "runs", key + ".json"), key, config.CandidatePoolCapacity);
    }
    internal void StopRun()
    {
        _pool?.Close(); _pool = null;
        _session?.Dispose();
        foreach (var session in _retired) session.Dispose();
        _retired.Clear();
        _won = false;
        Reset();
    }

    internal void Audit(string kind, object value)
    {
        if (kind.StartsWith("generation_", StringComparison.Ordinal) || kind.StartsWith("reward_", StringComparison.Ordinal))
            _generationJournal?.Append(kind, value);
    }
    internal object[] CaptureCards(IEnumerable<CardModel> cards) => cards.Select(_capture.Card).ToArray();
    internal static string CombatKey(Player player)
    {
        var run = player.RunState;
        return AtomicStore.Key($"v1|{run.Rng.StringSeed}|{run.AscensionLevel}|{player.Character.Id.Entry}|{run.CurrentActIndex}|{run.TotalFloor}|{run.CurrentRoom?.Id}");
    }

    internal void Reset()
    {
        var session = _session;
        if (session is not null)
        {
            if (_won) { session.Seal(); _retired.Add(session); }
            else session.Dispose();
        }
        _session = null;
        _ = CloseJournal(session, _generationJournal);
        _generationJournal = null; _events.Clear(); _totalEvents = 0; _ended = false; _frozen = false;
        _activeTurnSide = null; _firstEnemyTurnEnded = false; _won = false;
        _summary = new(); _firstRoundSummary = null;
        _frozenCards = []; _combat = null; _player = null; _lastContext = null;
    }

    private static async Task CloseJournal(GenerationSession? session, JsonlJournal? generation)
    {
        // Let canceled requests record their final outcome before completing the writer queue.
        if (session is not null)
        {
            await session.WaitForPendingAsync().ConfigureAwait(false);
            session.Dispose();
        }
        if (generation is not null) await generation.DisposeAsync().ConfigureAwait(false);
    }
}
