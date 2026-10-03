using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Forge.Core;

// Logs retain full snapshots. Only this projection is sent to the provider (also for CLI replays).
public static class ObservationProjector
{
    private static JsonObject Pick(JsonObject value, params string[] keys)
    {
        var result = new JsonObject();
        foreach (string key in keys) if (value.TryGetPropertyValue(key, out var item)) result[key] = item?.DeepClone();
        return result;
    }
    private static JsonObject Card(JsonObject card, bool full)
    {
        var result = Pick(card, "id", "title", "type", "rarity", "cost", "star_cost", "current_cost", "current_star_cost",
            "x_cost", "star_x_cost", "upgraded", "target", "keywords", "enchantment", "affliction", "count");
        result["origin"] = card["origin"]?.ToString() ?? (card["generated_definition"] is null ? "native" : "generated");
        if (full)
        {
            result["description"] = Clean(card["description"]?.ToString() ?? "");
            result["generated_definition"] = card["generated_definition"]?.DeepClone();
        }
        else
        {
            result = Pick(result, "id", "title", "type", "current_cost", "current_star_cost", "upgraded", "origin");
            result["instance"] = card["instance"]?.DeepClone();
        }
        return result;
    }
    private static string Clean(string text)
    {
        text = Regex.Replace(text, @"\[img\][^\[]*star_icon[^\[]*\[/img\]", "★");
        text = Regex.Replace(text, @"\[img\][^\[]*energy_icon[^\[]*\[/img\]", "⚡");
        return Regex.Replace(text, @"\[img\].*?\[/img\]", "").Replace("\\n", "\n");
    }
    private static JsonObject Creature(JsonObject value)
    {
        var result = Pick(value, "instance", "id", "side", "hp", "max_hp", "block", "alive");
        result["powers"] = value["powers"] is JsonArray powers
            ? new JsonArray(powers.OfType<JsonObject>().Select(p => (JsonNode)Pick(p, "id", "amount")).ToArray()) : new JsonArray();
        return result;
    }
    private static JsonNode? EventNode(JsonNode? node)
    {
        if (node is JsonArray array) return new JsonArray(array.Select(EventNode).ToArray());
        if (node is not JsonObject obj) return node?.DeepClone();
        if (obj.ContainsKey("hp") && obj.ContainsKey("side")) return Pick(obj, "instance", "id", "side", "hp", "block", "alive");
        if (obj.ContainsKey("rarity") && obj.ContainsKey("cost")) return Card(obj, false);
        var result = new JsonObject();
        foreach (var pair in obj)
        {
            if (pair.Key is "state" or "powers" or "description" or "generated_definition" or "FollowUpState" or "FollowUpStateId") continue;
            result[pair.Key] = EventNode(pair.Value);
        }
        return result;
    }
    public static JsonElement CompactEvent(JsonElement entry) => JsonSerializer.SerializeToElement(EventNode(JsonNode.Parse(entry.GetRawText())), Wire.Json);
    public static GenerationContext Compact(GenerationContext context)
    {
        var run = JsonNode.Parse(context.Run.GetRawText()) as JsonObject;
        var state = JsonNode.Parse(context.State.GetRawText()) as JsonObject;
        if (run is not null && run["deck"] is JsonArray deck)
        {
            var compact = Pick(run, "act", "floor", "ascension", "character", "room_type", "gold");
            compact["deck"] = new JsonArray(deck.OfType<JsonObject>().Select(c => Card(c, true))
                .Concat(deck.Where(c => c is not JsonObject).Select(c => new JsonObject { ["id"] = c?.DeepClone() }))
                .GroupBy(c => c.ToJsonString()).Select(group =>
                {
                    var card = group.First(); card["count"] = group.Sum(c => c["count"]?.GetValue<int>() ?? 1); return (JsonNode)card;
                }).ToArray());
            foreach (string kind in new[] { "relics", "potions" })
                compact[kind] = new JsonArray((run[kind] as JsonArray ?? new()).OfType<JsonObject>().Select(entity =>
                {
                    var item = Pick(entity, "id", "title");
                    if (entity["description"] is not null) item["description"] = Clean(entity["description"]!.ToString());
                    if (entity["state"] is JsonObject counters)
                        item["counters"] = Pick(counters, "Amount", "TimesUsed", "Charges", "Counter", "Stacks");
                    return (JsonNode)item;
                }).ToArray());
            run = compact;
        }
        if (state is not null && state["player"] is JsonObject player)
        {
            var compact = Pick(state, "round", "side"); compact["player"] = Creature(player);
            if (state["player_combat"] is JsonObject pcs)
            {
                var combat = Pick(pcs, "turn", "phase", "energy", "max_energy", "stars", "orb_capacity");
                combat["orbs"] = new JsonArray((pcs["orbs"] as JsonArray ?? new()).OfType<JsonObject>().Select(o => (JsonNode)Pick(o, "id")).ToArray());
                combat["piles"] = new JsonArray((pcs["piles"] as JsonArray ?? new()).OfType<JsonObject>().Select(pile =>
                    (JsonNode)new JsonObject { ["pile"] = pile["pile"]?.DeepClone(), ["cards"] = new JsonArray(
                        (pile["cards"] as JsonArray ?? new()).OfType<JsonObject>().Select(c => (JsonNode)Card(c, false)).ToArray()) }).ToArray());
                compact["player_combat"] = combat;
            }
            compact["allies"] = new JsonArray((state["allies"] as JsonArray ?? new()).OfType<JsonObject>().Select(c => (JsonNode)Creature(c)).ToArray());
            compact["enemies"] = new JsonArray((state["enemies"] as JsonArray ?? new()).OfType<JsonObject>().Select(enemy =>
            {
                var value = Pick(enemy, "move", "intents");
                value["creature"] = enemy["creature"] is JsonObject creature ? Creature(creature) : null;
                return (JsonNode)value;
            }).ToArray());
            state = compact;
        }
        return context with { SchemaVersion = 2,
            Run = run is null ? context.Run : JsonSerializer.SerializeToElement(run, Wire.Json),
            State = state is null ? context.State : JsonSerializer.SerializeToElement(state, Wire.Json),
            RecentEvents = context.RecentEvents.Select(CompactEvent).ToArray() };
    }
}

public sealed class CombatSummary
{
    private readonly Dictionary<string, int> _events = new();
    private readonly Dictionary<string, (string Title, string Type, string Origin, int Count)> _plays = new();
    private long _damageDealt, _damageTaken, _blocked, _blockGained, _starsGained, _starsSpent, _energySpent;
    public void Add(JsonElement observation, string character)
    {
        var detail = observation.GetProperty("detail");
        string kind = detail.GetProperty("type").GetString()!;
        _events[kind] = _events.GetValueOrDefault(kind) + 1;
        var fields = detail.GetProperty("fields");
        bool owner = fields.TryGetProperty("Actor", out var actor) && actor.TryGetProperty("id", out var id) && id.GetString() == character;
        if (kind == "CardPlayFinishedEntry" && owner && fields.TryGetProperty("CardPlay", out var play))
        {
            var card = play.GetProperty("card"); string cardId = card.GetProperty("id").GetString()!;
            string title = card.GetProperty("title").GetString() ?? cardId;
            string key = cardId + "|" + title;
            var old = _plays.GetValueOrDefault(key);
            _plays[key] = (title, card.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                card.TryGetProperty("origin", out var origin) ? origin.GetString() ?? "native" : "native", old.Count + 1);
            if (play.TryGetProperty("resources", out var resources))
            { _starsSpent += Number(resources, "StarsSpent"); _energySpent += Number(resources, "EnergySpent"); }
        }
        if (kind == "CreatureAttackedEntry" && fields.TryGetProperty("DamageResults", out var damages))
            foreach (var damage in damages.EnumerateArray())
            {
                var receiver = damage.GetProperty("Receiver");
                if (receiver.GetProperty("id").GetString() == character)
                { _damageTaken += Number(damage, "UnblockedDamage"); _blocked += Number(damage, "BlockedDamage"); }
                else if (owner) _damageDealt += Number(damage, "UnblockedDamage");
            }
        if (owner && kind == "BlockGainedEntry") _blockGained += Number(fields, "Amount");
        if (owner && kind == "StarsModifiedEntry") _starsGained += Math.Max(0, Number(fields, "Amount"));
    }
    private static long Number(JsonElement obj, string key) => obj.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) ? (long)number : 0;
    public JsonElement Snapshot(bool complete = false, bool? won = null) => JsonSerializer.SerializeToElement(new
    {
        complete, won, event_counts = _events, cards_played = _plays.Select(p => new
        { id = p.Key.Split('|')[0], title = p.Value.Title, type = p.Value.Type, origin = p.Value.Origin, count = p.Value.Count }).ToArray(),
        damage_dealt = _damageDealt, damage_taken = _damageTaken, damage_blocked = _blocked, block_gained = _blockGained,
        stars_gained = _starsGained, stars_spent = _starsSpent, energy_spent = _energySpent
    }, Wire.Json);
}
