namespace Forge.Core;

public sealed record GeneratedCandidate(string Id, string CombatKey, CardDefinition Card, string Status);
public sealed record CandidatePoolSnapshot
{
    public int SchemaVersion { get; init; } = 2;
    public int CardProtocol { get; init; } = MechanicCatalog.ProtocolVersion;
    public required string RunKey { get; init; }
    public GeneratedCandidate[] Candidates { get; init; } = [];
    public GeneratedCandidate[] History { get; init; } = [];
    public string[] Seen { get; init; } = [];
    public Dictionary<string, CardDefinition[]> Rewards { get; init; } = new();
}

// One transaction owns pool consumption and frozen rewards, including empty rewards.
public sealed class CandidatePool
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _capacity;
    private CandidatePoolSnapshot _snapshot;
    private bool _closed;
    public string RunKey => _snapshot.RunKey;

    public CandidatePool(string path, string runKey, int capacity)
    {
        _path = path; _capacity = capacity;
        _snapshot = File.Exists(path) ? Wire.Decode<CandidatePoolSnapshot>(File.ReadAllText(path))
            : new() { RunKey = runKey };
        if (_snapshot.SchemaVersion != 2 || _snapshot.CardProtocol != MechanicCatalog.ProtocolVersion || _snapshot.RunKey != runKey || _snapshot.Candidates is null
            || _snapshot.History is null || _snapshot.Seen is null || _snapshot.Rewards is null)
            throw new FormatException("Invalid candidate pool snapshot.");
        foreach (var candidate in _snapshot.Candidates.Concat(_snapshot.History)) CardValidator.Validate(candidate.Card);
        foreach (var reward in _snapshot.Rewards.Values)
        {
            if (reward is null || reward.Length > 3) throw new FormatException("Invalid pool reward.");
            foreach (var card in reward) CardValidator.Validate(card);
        }
    }

    public static string Fingerprint(CardDefinition card) => AtomicStore.Key(Wire.Encode(card with { Name = "", Flavor = "" }));
    private static string Shape(CardDefinition card) => $"{card.Type}|{card.Forms[0].Cost.Energy}|{card.Forms[0].Cost.Stars}|"
        + string.Join(",", card.Forms.SelectMany(f => f.Listeners).Select(r => $"{r.Trigger.Event}/{Wire.Encode(r.Trigger.Filter)}/{r.Lifetime}/{Wire.Encode(r.Trigger.Occurrence)}")) + "|"
        + string.Join(",", card.Forms.SelectMany(f => f.AllEffects).Select(e => $"{e.Kind}/{e.Power}/{e.Actor}/{e.Target?.Ref}/{e.Target?.Pile}/{e.Card?.Id}/{e.Card?.Pool}/{e.Orb}"));
    private static T Copy<T>(T value) => Wire.Decode<T>(Wire.Encode(value));
    public GeneratedCandidate[] History()
    {
        lock (_gate) return Copy(_snapshot.History.TakeLast(16).Concat(_snapshot.Candidates).DistinctBy(c => c.Id).ToArray());
    }
    public bool HasReward(string key) { lock (_gate) return _snapshot.Rewards.ContainsKey(key); }
    public bool HasCandidates { get { lock (_gate) return _snapshot.Candidates.Length > 0; } }

    public int Add(string combatKey, CardDefinition[] cards)
    {
        var validated = cards.Select(CardValidator.Validate).ToArray();
        lock (_gate)
        {
            if (_closed) return 0;
            var seen = _snapshot.Seen.ToHashSet(StringComparer.Ordinal);
            var candidates = _snapshot.Candidates.ToList();
            var history = _snapshot.History.ToList();
            int added = 0;
            foreach (var card in validated)
            {
                string id = Fingerprint(card);
                if (!seen.Add(id)) continue;
                var candidate = new GeneratedCandidate(id, combatKey, Copy(card), "candidate");
                candidates.Add(candidate); history.Add(candidate); added++;
            }
            if (added == 0) return 0;
            while (candidates.Count > _capacity)
            {
                var expired = candidates[0]; candidates.RemoveAt(0);
                history = history.Select(c => c.Id == expired.Id ? c with { Status = "expired" } : c).ToList();
            }
            Save(_snapshot with { Candidates = candidates.ToArray(), History = history.TakeLast(64).ToArray(), Seen = seen.ToArray() });
            return added;
        }
    }

    public CardDefinition[] FreezeReward(string key, int count, CardDefinition[]? restored = null)
    {
        lock (_gate)
        {
            if (_snapshot.Rewards.TryGetValue(key, out var previous)) return Copy(previous);
            if (_closed) return [];
            var candidates = _snapshot.Candidates.ToList();
            var selected = new List<GeneratedCandidate>();
            var recentShapes = _snapshot.History.Where(c => c.Status is "shown" or "selected" or "skipped")
                .TakeLast(8).Select(c => Shape(c.Card)).ToList();
            for (int i = 0; restored is null && i < count && candidates.Count > 0; i++)
            {
                // Stable selection prefers unseen mechanic shapes; ties keep the oldest candidate.
                var next = candidates.OrderBy(c => recentShapes.Count(s => s == Shape(c.Card))).First();
                candidates.Remove(next); selected.Add(next); recentShapes.Add(Shape(next.Card));
            }
            var reward = restored is null ? selected.Select(c => c.Card).ToArray() : restored.Select(CardValidator.Validate).ToArray();
            var ids = reward.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
            var rewards = new Dictionary<string, CardDefinition[]>(_snapshot.Rewards) { [key] = Copy(reward) };
            Save(_snapshot with { Candidates = candidates.Where(c => !ids.Contains(c.Id)).ToArray(), Rewards = rewards,
                Seen = _snapshot.Seen.Concat(ids).Distinct().ToArray(),
                History = _snapshot.History.Select(c => ids.Contains(c.Id) ? c with { Status = "shown" } : c).ToArray() });
            return Copy(reward);
        }
    }

    public void RecordChoice(string rewardKey, IEnumerable<CardDefinition> deck)
    {
        lock (_gate)
        {
            if (_closed || !_snapshot.Rewards.TryGetValue(rewardKey, out var reward)) return;
            var offered = reward.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
            var owned = deck.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
            Save(_snapshot with { History = _snapshot.History.Select(c => offered.Contains(c.Id)
                ? c with { Status = owned.Contains(c.Id) ? "selected" : "skipped" } : c).ToArray() });
        }
    }
    public void Close() { lock (_gate) _closed = true; }
    private void Save(CandidatePoolSnapshot next)
    {
        AtomicStore.Write(_path, next); // On failure keep the old in-memory transaction; no lost candidates.
        _snapshot = next;
    }
}
