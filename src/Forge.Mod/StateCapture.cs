using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Forge.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace Forge.Mod;

internal sealed class StateCapture
{
    private sealed class Identity(int id) { public int Id { get; } = id; }
    private readonly ConditionalWeakTable<CardModel, Identity> _cardIds = new();
    private int _nextId;
    public JsonElement Detach(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);

    public object Run(Player player)
    {
        var run = player.RunState;
        return new
        {
            seed = run.Rng.StringSeed, act = run.CurrentActIndex + 1, floor = run.TotalFloor,
            ascension = run.AscensionLevel, character = player.Character.Id.Entry,
            room_type = run.CurrentRoom?.RoomType.ToString(), room_id = run.CurrentRoom?.Id,
            map_path = run is RunState concrete ? concrete.VisitedMapCoords.Select(c => new { c.row, c.col }).ToArray() : null,
            planning = CapturePlanning(run),
            deck = player.Deck.Cards.Select(Card).ToArray(), gold = player.Gold,
            relics = player.Relics.Select(m => new { id = m.Id.Entry, title = Safe(() => m.Title.GetFormattedText()),
                description = Safe(() => m.DynamicDescription.GetFormattedText()), state = Scalars(m) }).ToArray(),
            potions = player.Potions.Select(m => new { id = m.Id.Entry, title = Safe(() => m.Title.GetFormattedText()),
                description = Safe(() => m.DynamicDescription.GetFormattedText()), state = Scalars(m) }).ToArray()
        };
    }

    internal static object CapturePlanning(IRunState run)
    {
        var current = run.CurrentMapPoint ?? run.Map?.StartingMapPoint;
        int? futureElites = current is null ? null : CountFutureElites(current);
        ActModel? eliteAct = run.Act;
        if (futureElites == 0)
            eliteAct = run.CurrentActIndex < 2 && run.CurrentActIndex + 1 < run.Acts.Count
                ? run.Acts[run.CurrentActIndex + 1] : null;
        IEnumerable<EncounterModel> elites = eliteAct?.AllEliteEncounters ?? [];
        if (eliteAct == run.Act && futureElites is > 0)
            elites = PossibleEliteEncounters(run.Act, futureElites.Value);
        return new
        {
            act_id = run.Act.Id.Entry,
            act_title = Safe(() => run.Act.Title.GetFormattedText()) as string ?? run.Act.Id.Entry,
            // Read assigned encounters only. Never PullNextEncounter/GenerateMonsters here.
            bosses = run.CurrentRoom?.RoomType == RoomType.Boss ? [] : new[] { Safe(() => run.Act.BossEncounter) as EncounterModel,
                    Safe(() => run.Act.SecondBossEncounter) as EncounterModel }
                .OfType<EncounterModel>().DistinctBy(e => e.Id).Select(PlanningEncounter).ToArray(),
            elite_act_id = eliteAct?.Id.Entry,
            elite_act_title = eliteAct is null ? null : Safe(() => eliteAct.Title.GetFormattedText()) as string ?? eliteAct.Id.Entry,
            elite_pool = elites.OrderBy(e => e.Id.Entry, StringComparer.Ordinal).Select(PlanningEncounter).ToArray()
        };
    }

    internal static IEnumerable<EncounterModel> PossibleEliteEncounters(ActModel act, int count)
    {
        var pool = act.AllEliteEncounters.OrderBy(e => e.Id.Entry, StringComparer.Ordinal).ToArray();
        if (count <= 0 || pool.Length == 0) return [];
        var rooms = act.ToSave().SerializableRooms;
        var queue = rooms.EliteEncounterIds;
        int visited = rooms.EliteEncountersVisited;
        // Native queues contain 15 pre-rolled draws. Read only the consumed prefix,
        // including the current map combat (marked visited on room entry).
        int offset = queue.Count == 0 ? 0 : visited % queue.Count;
        int usedInBag = offset % pool.Length;
        var used = queue.Skip(offset - usedInBag).Take(usedInBag).ToHashSet();
        var previous = visited == 0 || queue.Count == 0 ? null
            : pool.FirstOrDefault(e => e.Id == queue[(visited - 1) % queue.Count]);
        var possible = new HashSet<EncounterModel>();
        var explored = new HashSet<(string Remaining, ModelId? Previous, int Depth)>();
        Explore(pool.Where(e => !used.Contains(e.Id)).ToArray(), previous, count);
        return possible;

        void Explore(EncounterModel[] remaining, EncounterModel? last, int depth)
        {
            if (depth == 0) return;
            if (remaining.Length == 0) remaining = pool;
            if (!explored.Add((string.Join(",", remaining.Select(e => e.Id.Entry)), last?.Id, depth))) return;
            // Match ActModel.AddWithoutRepeatingTags, including its fallback when
            // every remaining encounter conflicts. Do not inspect future queue entries.
            var preferred = remaining.Where(e => last is null || (e.Id != last.Id && !e.SharesTagsWith(last))).ToArray();
            foreach (var next in preferred.Length == 0 ? remaining : preferred)
            {
                possible.Add(next);
                Explore(remaining.Where(e => e.Id != next.Id).ToArray(), next, depth - 1);
            }
        }
    }

    private static object PlanningEncounter(EncounterModel encounter) => new
    {
        id = encounter.Id.Entry,
        title = Safe(() => encounter.Title.GetFormattedText()) as string ?? encounter.Id.Entry,
        // AllPossibleMonsters is a static candidate set, not a generated lineup.
        monsters = encounter.AllPossibleMonsters.DistinctBy(m => m.Id).OrderBy(m => m.Id.Entry, StringComparer.Ordinal)
            .Select(m => new { id = m.Id.Entry, title = Safe(() => m.Title.GetFormattedText()) as string ?? m.Id.Entry,
                // Native getters apply the active run's ascension; no HP roll or monster generation.
                initial_hp = Safe(() => new { min = m.MinInitialHp, max = m.MaxInitialHp }) }).ToArray()
    };

    internal static int CountFutureElites(MapPoint current)
    {
        var counts = new Dictionary<MapPoint, int>();
        return current.Children.Select(Count).DefaultIfEmpty(0).Max();

        int Count(MapPoint point)
        {
            if (counts.TryGetValue(point, out int count)) return count;
            count = (point.PointType == MapPointType.Elite ? 1 : 0)
                + point.Children.Select(Count).DefaultIfEmpty(0).Max();
            counts[point] = count;
            return count;
        }
    }

    public object State(ICombatState combat, Player player)
    {
        var pcs = player.PlayerCombatState;
        return new
        {
            encounter = CaptureEncounter(combat, player.RunState),
            round = combat.RoundNumber, side = combat.CurrentSide.ToString(), player = Creature(player.Creature),
            player_combat = pcs is null ? null : new
            {
                turn = pcs.TurnNumber, phase = pcs.Phase.ToString(), energy = pcs.Energy, max_energy = pcs.MaxEnergy,
                stars = pcs.Stars, other_resources = Scalars(pcs),
                orb_capacity = pcs.OrbQueue.Capacity,
                orbs = pcs.OrbQueue.Orbs.Select(o => new { id = o.Id.Entry, state = Scalars(o) }).ToArray(),
                piles = pcs.AllPiles.Select(p => new { pile = p.Type.ToString(), cards = p.Cards.Select(Card).ToArray() }).ToArray()
            },
            allies = combat.Allies.Where(c => c != player.Creature).Select(Creature).ToArray(),
            enemies = combat.Enemies.Select(Enemy).ToArray()
        };
    }

    public object Card(CardModel card) => new
    {
        origin = card is NeowGeneratedCard ? "generated" : "native",
        instance = _cardIds.GetValue(card, _ => new Identity(++_nextId)).Id,
        id = card.Id.Entry, pool = card.Pool.Id.Entry, title = Safe(() => card.Title), type = card.Type.ToString(), rarity = card.Rarity.ToString(),
        cost = card.EnergyCost.Canonical, current_cost = Safe(() => card.EnergyCost.GetWithModifiers(CostModifiers.All)),
        star_cost = card.CanonicalStarCost, current_star_cost = Safe(() => card.GetStarCostWithModifiers()),
        star_x_cost = card.HasStarCostX, x_cost = card.EnergyCost.CostsX, upgraded = card.IsUpgraded, upgrade_level = card.CurrentUpgradeLevel,
        target = card.TargetType.ToString(), keywords = card.Keywords.Select(k => k.ToString()).ToArray(),
        variables = card.DynamicVars.ToDictionary(v => v.Key, v => new { base_value = v.Value.BaseValue, preview = v.Value.PreviewValue }),
        description = Safe(() => card.GetDescriptionForPile(card.Pile?.Type ?? PileType.Deck)),
        enchantment = card.Enchantment is { } en ? new { id = en.Id.Entry, en.Amount } : null,
        affliction = card.Affliction is { } af ? new { id = af.Id.Entry, af.Amount } : null,
        generated_definition = card is NeowGeneratedCard generated ? generated.Definition : null
    };

    internal static object CaptureEncounter(ICombatState combat, IRunState run)
    {
        string? eventId = (run.BaseRoom as EventRoom)?.ModelId.Entry ?? (run.CurrentRoom as CombatRoom)?.ParentEventId?.Entry;
        return new
        {
            type = combat.Encounter?.RoomType.ToString().ToLowerInvariant(),
            id = combat.Encounter?.Id.Entry,
            source = eventId is null ? "map" : "event",
            event_id = eventId
        };
    }

    public object Creature(Creature creature) => new
    {
        instance = creature.CombatId, id = creature.ModelId.Entry, side = creature.Side.ToString(),
        hp = creature.CurrentHp, max_hp = creature.MaxHp, block = creature.Block, alive = creature.IsAlive,
        powers = creature.Powers.Select(p => new { id = p.Id.Entry, p.Amount, state = Scalars(p) }).ToArray()
    };

    private object Enemy(Creature enemy) => new
    {
        creature = Creature(enemy), move = Safe(() => enemy.Monster?.NextMove?.Id),
        intents = Safe(() => enemy.Monster?.NextMove?.Intents.Select(i => new
        {
            type = i.IntentType.ToString(),
            damage = i is AttackIntent attack ? Safe(() => attack.GetSingleDamage(enemy.CombatState!.PlayerCreatures, enemy)) : null,
            hits = i is AttackIntent hit ? hit.Repeats : 0
        }).ToArray())
    };

    public object Event(CombatHistoryEntry entry) => new
    {
        type = entry.GetType().Name,
        // Properties are captured now, before the referenced powers/cards change again.
        fields = entry.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("History" or "Description" or "HumanReadableString"))
            .ToDictionary(p => p.Name, p => Safe(() => Flatten(p.GetValue(entry), 0)))
    };

    private object? Flatten(object? value, int depth)
    {
        if (value is null) return null;
        if (value is string || value.GetType().IsPrimitive || value is decimal) return value;
        if (value is Enum) return value.ToString();
        if (value is CardModel card) return Card(card);
        if (value is Creature creature) return Creature(creature);
        if (value is Player player) return new { character = player.Character.Id.Entry };
        if (value is CardPlay play) return new
        {
            card = Card(play.Card), target = play.Target is null ? null : Creature(play.Target),
            play.IsAutoPlay, play.PlayIndex, play.PlayCount, result_pile = play.ResultPile.ToString(), resources = Scalars(play.Resources)
        };
        if (value is AbstractModel model) return new { id = model.Id.Entry, state = Scalars(model) };
        if (value is IEnumerable sequence) return sequence.Cast<object?>().Take(200).Select(v => Flatten(v, depth + 1)).ToArray();
        if (depth >= 2) return new { type = value.GetType().Name };
        return value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("History" or "CombatState" or "RunState"))
            .ToDictionary(p => p.Name, p => Safe(() => Flatten(p.GetValue(value), depth + 1)));
    }

    private static Dictionary<string, object?> Scalars(object model) => model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum
            || p.PropertyType == typeof(decimal) || p.PropertyType == typeof(string))
            && p.Name is not ("NetId" or "Description" or "HumanReadableString"))
        .ToDictionary(p => p.Name, p => Safe(() => p.PropertyType.IsEnum ? p.GetValue(model)?.ToString() : p.GetValue(model)));
    private static object? Safe(Func<object?> read)
    {
        try { return read(); } catch { return new { unavailable = true }; }
    }
}
