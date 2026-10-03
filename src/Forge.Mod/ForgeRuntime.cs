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
    private JsonlJournal? _journal;
    private readonly Queue<JsonElement> _events = new();
    private StateCapture _capture = new();
    private ICombatState? _combat;
    private Player? _player;
    private int _totalEvents;
    private bool _ended;
    private bool _frozen;
    private CardDefinition[] _frozenCards = [];
    private GenerationContext? _lastContext;
    internal ForgeConfig Config => config;
    internal bool IsSingleplayer => RunManager.Instance.NetService?.Type == NetGameType.Singleplayer;
    internal bool Eligible => config.Enabled && config.GeneratedCardsPerReward > 0 && IsSingleplayer;

    public void Subscribe()
    {
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
        if (!IsSingleplayer) return;
        _player = LocalContext.GetMe(combat);
        if (_player is null) return;
        _combat = combat;
        _capture = new StateCapture();
        string key = CombatKey(_player);
        string path = Path.Combine(root, "data", "combats", key + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".jsonl");
        if (config.RecordCombat) _journal = new JsonlJournal(path);
        var combatJournal = _journal;
        _session = new GenerationSession(key, config, new OpenAiCardGenerator(Http, config.Provider),
            (kind, value) => combatJournal?.Append(kind, value));
        _lastContext = Context(combat);
        Audit("combat_started", new { run = _lastContext.Run, state = _lastContext.State, game_version = "0.111.0" });
        // TurnStarted provides the first dealt hand; do not spend the first request before it.
    }

    private void Boundary(ICombatState combat, string kind)
    {
        if (_combat != combat || _ended || _player is null) return;
        _lastContext = Context(combat);
        Audit(kind, new { state = _lastContext.State, run = _lastContext.Run });
        MaybePrefetch(_lastContext);
    }

    internal void OnHistory(ICombatState combat, CombatHistoryEntry entry)
    {
        if (_combat != combat || _player is null || _ended) return;
        var eventData = _capture.Detach(new { sequence = ++_totalEvents, round = combat.RoundNumber,
            side = combat.CurrentSide.ToString(), detail = _capture.Event(entry) });
        _events.Enqueue(eventData);
        while (_events.Count > config.PromptEventLimit) _events.Dequeue();
        // Local journal keeps EVERY event plus its full state; only prompt context is truncated.
        var state = _capture.Detach(_capture.State(combat, _player));
        Audit("combat_event", new { combat_event = eventData, state });
        if (entry is CardPlayFinishedEntry)
        {
            _lastContext = Context(combat, state);
            MaybePrefetch(_lastContext);
        }
    }

    internal void BeforeHistoryClear()
    {
        if (_combat is null || _player is null || _ended || _totalEvents == 0) return;
        _lastContext = Context(_combat);
        Audit("before_history_clear", new { run = _lastContext.Run, state = _lastContext.State });
    }

    private void MaybePrefetch(GenerationContext context)
    {
        if (Eligible && config.GenerationTiming == GenerationTiming.Prefetch)
            _session?.TryPrefetch(context, DateTimeOffset.UtcNow);
    }

    private void End(CombatRoom room, bool won)
    {
        if (_ended || _combat is null || _player is null) return;
        _ended = true;
        Audit("combat_ended", new { won, room_type = room.RoomType.ToString(), total_events = _totalEvents,
            final_run = _capture.Detach(_capture.Run(_player)), last_state = _lastContext?.State });
        if (!won) _session?.Dispose();
    }

    private GenerationContext Context(ICombatState combat, JsonElement? state = null) => new()
    {
        CombatKey = _session!.Key, Run = _capture.Detach(_capture.Run(_player!)),
        State = state ?? _capture.Detach(_capture.State(combat, _player!)), RecentEvents = _events.ToArray(),
        TotalEvents = _totalEvents, OmittedEvents = _totalEvents - _events.Count
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
        string key = CombatKey(player);
        string cache = Path.Combine(root, "data", "rewards", key + ".json");
        if (_session?.Key == key)
        {
            if (!_frozen)
            {
                _frozen = true;
                _frozenCards = _session.Freeze();
                // A frozen empty result is meaningful: late generation cannot change a reopened reward.
                try { AtomicStore.Write(cache, _frozenCards); }
                catch (IOException) { Audit("reward_cache_failed", new { key }); }
                Audit("reward_frozen", new { key, cards = _frozenCards });
            }
            return _frozenCards;
        }
        // Rewards are re-populated from RNG on load. Reattach exactly the previously shown generated definitions.
        if (!File.Exists(cache)) return [];
        try
        {
            var cards = Wire.Decode<CardDefinition[]>(File.ReadAllText(cache));
            if (cards.Length > 3) return [];
            return cards.Select(CardValidator.Validate).ToArray();
        }
        catch { Log.Warn("[NeowsCompany] Invalid reward cache ignored."); return []; }
    }

    internal void Audit(string kind, object value) => _journal?.Append(kind, value);
    internal object[] CaptureCards(IEnumerable<CardModel> cards) => cards.Select(_capture.Card).ToArray();
    internal static string CombatKey(Player player)
    {
        var run = player.RunState;
        return AtomicStore.Key($"v1|{run.Rng.StringSeed}|{run.AscensionLevel}|{player.Character.Id.Entry}|{run.CurrentActIndex}|{run.TotalFloor}|{run.CurrentRoom?.Id}");
    }

    internal void Reset()
    {
        _session?.Dispose(); _session = null;
        if (_journal is not null) _ = _journal.DisposeAsync().AsTask();
        _journal = null; _events.Clear(); _totalEvents = 0; _ended = false; _frozen = false;
        _frozenCards = []; _combat = null; _player = null; _lastContext = null;
    }
}
