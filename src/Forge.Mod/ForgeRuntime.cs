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
using MegaCrit.Sts2.Core.Rewards;

namespace Forge.Mod;

internal sealed class ForgeRuntime(string root, ForgeConfig config)
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private GenerationSession? _session;
    private int _sessionFloor;
    private JsonlJournal? _generationJournal;
    private readonly Queue<JsonElement> _events = new();
    private StateCapture _capture = new();
    private ICombatState? _combat;
    private Player? _player;
    private int _totalEvents;
    private CandidatePool? _pool;
    private readonly List<(int Floor, GenerationSession Session)> _retired = new();
    private CombatSummary _summary = new();
    private JsonElement? _firstRoundSummary;
    private bool _won;
    private CombatSide? _activeTurnSide;
    private bool _firstEnemyTurnEnded;
    private bool _ended;
    private bool _frozen;
    private readonly Dictionary<CardReward, (string CombatKey, string Key)> _rewardKeys = new();
    private GenerationContext? _lastContext;
    internal ForgeConfig Config => config;
    internal bool IsSingleplayer => RunManager.Instance.NetService?.Type == NetGameType.Singleplayer;
    internal bool Eligible => config.Enabled && config.GeneratedCardsPerReward > 0 && IsSingleplayer;

    // Match native RewardsSet.WithRewardsFromRoom: the final act's bosses have no card rewards.
    internal static bool HasCombatCardReward(ICombatState combat) => combat.Encounter?.RoomType != RoomType.Boss
        || combat.RunState.CurrentActIndex < combat.RunState.Acts.Count - 1;

    // Native history already includes the current room at CombatBegan, including event-spawned combats.
    // It also survives loading a run, unlike a flag tracking combats seen by this runtime.
    internal static bool IsFirstCombat(IRunState run) => run.MapPointHistory.SelectMany(act => act)
        .SelectMany(point => point.Rooms).Count(room => room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss) <= 1;

    public void Subscribe()
    {
        RunManager.Instance.RunStarted += run => Bootstrap.Safe(() =>
        {
            if (IsSingleplayer && LocalContext.GetMe(run) is { } player) EnsurePool(player);
            else StopRun();
        });
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
        var player = LocalContext.GetMe(combat);
        if (player is null) return;
        EnsurePool(player);
        _player = player;
        _combat = combat;
        _sessionFloor = _player.RunState.TotalFloor;
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
        bool firstCombat = IsFirstCombat(combat.RunState);
        Audit("generation_session", new { key, floor = _player.RunState.TotalFloor, timing = config.GenerationTiming,
            initial_trigger = firstCombat ? "combat_began" : "first_enemy_turn_ended", run_key = pool.RunKey });
        if (firstCombat) MaybePrefetch(_lastContext, firstCombatStarted: true);
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

    private void MaybePrefetch(GenerationContext context, bool firstCombatStarted = false)
    {
        if (Eligible && config.GenerationTiming == GenerationTiming.Prefetch)
            _session?.TryPrefetch(context, DateTimeOffset.UtcNow, firstEnemyTurnEnded: _firstEnemyTurnEnded,
                firstCombatStarted: firstCombatStarted);
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

    internal CardDefinition[] FreezeReward(CardReward reward)
    {
        if (!Eligible) return [];
        var player = reward.Player;
        EnsurePool(player);
        string combatKey = CombatKey(player);
        string key = RewardKey(reward);
        CardDefinition[]? restored = null;
        // Import pre-pool frozen rewards only if written during this run, never from an older same-seed run.
        string legacy = Path.Combine(root, "data", "rewards", key + ".json");
        if (key == combatKey && !_pool!.HasReward(key) && File.Exists(legacy)
            && new DateTimeOffset(File.GetLastWriteTimeUtc(legacy)).ToUnixTimeSeconds() >= RunStartTime())
        {
            try { restored = Wire.Decode<CardDefinition[]>(File.ReadAllText(legacy));
                if (restored.Length > 3) restored = null; }
            catch { Log.Warn("[NeowsCompany] Invalid legacy reward cache ignored."); }
        }
        var cards = _pool!.FreezeReward(key, config.GeneratedCardsPerReward, restored, combatKey);
        if (_session?.Key == combatKey)
        {
            _frozen = true; _session.Seal();
        }
        Audit("reward_frozen", new { key, run_key = _pool.RunKey, cards });
        return cards;
    }

    private string RewardKey(CardReward reward)
    {
        if (_rewardKeys.TryGetValue(reward, out var existing)) return existing.Key;
        string combatKey = CombatKey(reward.Player);
        // Native Populate order includes groups appended by relic hooks; retain identity for rerolls.
        int index = _rewardKeys.Values.Count(entry => entry.CombatKey == combatKey);
        // Keep the original key for group zero so existing snapshots and legacy caches still load.
        string key = index == 0 ? combatKey : $"{combatKey}:reward:{index}";
        _rewardKeys.Add(reward, (combatKey, key));
        return key;
    }

    internal void RecordRewardChoice(CardReward reward)
    {
        if (_rewardKeys.TryGetValue(reward, out var entry))
            _pool?.RecordChoice(entry.Key, reward.Player.Deck.Cards.OfType<NeowGeneratedCard>().Select(c => c.Definition));
    }
    private static long RunStartTime() => (long)HarmonyLib.AccessTools.Field(typeof(RunManager), "_startTime").GetValue(RunManager.Instance)!;
    private void EnsurePool(Player player)
    {
        var run = player.RunState;
        string key = AtomicStore.Key($"pool-v1|{RunStartTime()}|{run.Rng.StringSeed}|{run.AscensionLevel}|{player.Character.Id.Entry}");
        ResumeRun(key);
    }

    internal void ResumeRun(string key)
    {
        if (_pool?.RunKey == key) return;
        StopRun();
        _pool = new CandidatePool(Path.Combine(root, "data", "runs", key + ".json"), key, config.CandidatePoolCapacity, config.PromptHistoryLimit);
    }

    internal void UnloadRun(int floor, bool graceful)
    {
        if (!graceful) { StopRun(); return; }
        // CleanUp also runs for save/load. Only this floor's requests belong to the discarded attempt.
        foreach (var item in _retired.Where(item => item.Floor == floor)) item.Session.Dispose();
        _retired.RemoveAll(item => item.Floor == floor);
        if (_sessionFloor == floor) { _session?.Dispose(); _won = false; }
        Reset(preservePending: _sessionFloor < floor);
        // Keep the same pool instance: historical publishers captured it before the reload.
    }

    internal void StopRun()
    {
        _pool?.Close(); _pool = null;
        _session?.Dispose();
        foreach (var item in _retired) item.Session.Dispose();
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

    internal void Reset(bool preservePending = false)
    {
        _retired.RemoveAll(item => item.Session.WaitForPendingAsync().IsCompleted);
        var session = _session;
        if (session is not null)
        {
            if (_won || preservePending) { session.Seal(); _retired.Add((_sessionFloor, session)); }
            else session.Dispose();
        }
        _session = null;
        _sessionFloor = 0;
        _ = CloseJournal(session, _generationJournal);
        _generationJournal = null; _events.Clear(); _totalEvents = 0; _ended = false; _frozen = false;
        _activeTurnSide = null; _firstEnemyTurnEnded = false; _won = false;
        _summary = new(); _firstRoundSummary = null;
        _rewardKeys.Clear(); _combat = null; _player = null; _lastContext = null;
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
