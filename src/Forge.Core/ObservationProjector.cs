using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Forge.Core;

public static class ObservationProjector
{
    private static JsonObject Pick(JsonObject value, params string[] keys)
    {
        var result = new JsonObject();
        foreach (string key in keys) if (value[key] is { } item) result[key] = item.DeepClone();
        return result;
    }
    private static string Clean(string text)
    {
        text = Regex.Replace(text, @"\[img\][^\[]*star_icon[^\[]*\[/img\]", "★");
        text = Regex.Replace(text, @"\[img\][^\[]*energy_icon[^\[]*\[/img\]", "⚡");
        return Regex.Replace(Regex.Replace(text, @"\[img\].*?\[/img\]", ""), @"\[/?[a-zA-Z_]+\]", "").Replace("\\n", "\n");
    }
    private static JsonObject Card(JsonObject card)
    {
        var result = Pick(card, "title", "type", "cost", "upgraded", "enchantment", "affliction");
        if (card["star_cost"] is JsonValue stars && stars.TryGetValue<int>(out int number) && number >= 0) result["star_cost"] = number;
        if (card["x_cost"]?.ToString() == "true") result["x_cost"] = true;
        if (card["star_x_cost"]?.ToString() == "true") result["star_x_cost"] = true;
        if (card["description"] is { } description) result["text"] = Clean(description.ToString());
        else if (card["generated_definition"] is { } definition)
        {
            var parsed = Wire.Decode<CardDefinition>(definition.ToJsonString());
            result["text"] = Clean(CardText.Render(parsed, true, upgraded: card["upgraded"]?.ToString() == "true"));
        }
        if (!result.ContainsKey("title") && card["id"] is { } id) result["title"] = id.DeepClone();
        return result;
    }
    private static JsonObject Creature(JsonObject value)
    {
        var result = Pick(value, "id", "hp", "max_hp", "block");
        if (value["powers"] is JsonArray powers && powers.Count > 0)
            result["powers"] = new JsonArray(powers.OfType<JsonObject>().Select(p => (JsonNode)Pick(p, "id", "amount")).ToArray());
        return result;
    }
    // A design view, not a combat/session serialization. Unknown fields are omitted, never replaced with zero.
    public static JsonElement Project(GenerationContext context, int historyLimit = 12)
    {
        var run = JsonNode.Parse(context.Run.GetRawText()) as JsonObject ?? new();
        var state = JsonNode.Parse(context.State.GetRawText()) as JsonObject ?? new();
        var result = Pick(run, "character", "ascension", "act", "floor");
        if (run["deck"] is JsonArray deck)
            result["deck"] = new JsonArray(deck.Select(node => node is JsonObject card ? Card(card) : new JsonObject { ["title"] = node?.DeepClone() })
                .Select((card, i) => (card, count: deck[i] is JsonObject original ? original["count"]?.GetValue<int>() ?? 1 : 1))
                .GroupBy(item => item.card.ToJsonString()).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => { var card = group.First().card; card["count"] = group.Sum(item => item.count); return (JsonNode)card; }).ToArray());
        if (run["relics"] is JsonArray relics && relics.Count > 0)
            result["relics"] = new JsonArray(relics.OfType<JsonObject>().Select(relic =>
            {
                var item = Pick(relic, "title");
                if (relic["description"] is { } description) item["text"] = Clean(description.ToString());
                if (relic["state"] is JsonObject counters)
                {
                    var meaningful = Pick(counters, "Amount", "TimesUsed", "Charges", "Counter", "Stacks");
                    if (meaningful.Count > 0) item["counters"] = meaningful;
                }
                return (JsonNode)item;
            }).ToArray());
        if (historyLimit > 0 && context.GenerationHistory.Length > 0)
            result["history"] = new JsonArray(context.GenerationHistory.TakeLast(historyLimit).Select(entry =>
            {
                var history = JsonNode.Parse(entry.GetRawText()) as JsonObject ?? new();
                var item = Pick(history, "name", "status", "type", "rarity", "cost", "text");
                if (history["card"] is JsonObject definition && definition["forms"] is JsonArray)
                {
                    var card = Wire.Decode<CardDefinition>(definition.ToJsonString());
                    item["name"] = card.Name; item["cost"] = JsonSerializer.SerializeToNode(card.Forms[0].Cost, Wire.Json);
                    item["type"] = JsonSerializer.SerializeToNode(card.Type, Wire.Json);
                    item["rarity"] = JsonSerializer.SerializeToNode(card.Rarity, Wire.Json);
                    item["text"] = Clean(CardText.Render(card, true));
                }
                return (JsonNode)item;
            }).ToArray());
        var combat = Pick(state, "round");
        if (state["player"] is JsonObject player) combat["player"] = Creature(player);
        else foreach (string key in new[] { "hp", "max_hp" }) if (state[key] is { } field) combat[key] = field.DeepClone();
        if (state["player_combat"] is JsonObject pcs)
        {
            foreach (string key in new[] { "energy", "max_energy", "stars", "orb_capacity" }) if (pcs[key] is { } field) combat[key] = field.DeepClone();
            if (pcs["orbs"] is JsonArray orbs && orbs.Count > 0) combat["orbs"] = new JsonArray(orbs.OfType<JsonObject>().Select(o => (JsonNode)Pick(o, "id")).ToArray());
        }
        if (state["allies"] is JsonArray allies && allies.Count > 0) combat["allies"] = new JsonArray(allies.OfType<JsonObject>().Select(c => (JsonNode)Creature(c)).ToArray());
        if (state["enemies"] is JsonArray enemies)
            combat["enemies"] = new JsonArray(enemies.OfType<JsonObject>().Select(e =>
            {
                var creature = Creature(e["creature"] as JsonObject ?? e);
                if (e["intents"] is { } intents) creature["intents"] = intents.DeepClone();
                return (JsonNode)creature;
            }).ToArray());
        if (context.CombatSummary is { ValueKind: JsonValueKind.Object } summary)
        {
            var stats = JsonNode.Parse(summary.GetRawText())!.AsObject();
            combat["summary"] = Pick(stats, "complete", "won", "cards_played", "damage_dealt", "damage_taken", "damage_blocked", "block_gained", "stars_gained", "stars_spent", "energy_spent", "event_counts");
        }
        else if (context.RecentEvents.Length > 0)
        {
            // Offline/older observations without summaries still provide a bounded, typed event tally.
            var counts = context.RecentEvents.Select(e => e.TryGetProperty("detail", out var d) ? d : e)
                .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out _))
                .GroupBy(e => e.GetProperty("type").GetString()!).ToDictionary(g => g.Key, g => g.Count());
            combat["observed_events"] = JsonSerializer.SerializeToNode(counts);
        }
        result["combat"] = combat;
        return JsonSerializer.SerializeToElement(result, Wire.Json);
    }
    public static JsonElement CompactEvent(JsonElement entry)
    {
        JsonNode? Compact(JsonNode? node)
        {
            if (node is JsonArray array) return new JsonArray(array.Select(Compact).ToArray());
            if (node is not JsonObject obj) return node?.DeepClone();
            if (obj.ContainsKey("hp") && obj.ContainsKey("side")) return Pick(obj, "id", "hp", "block", "alive");
            if (obj.ContainsKey("rarity") && obj.ContainsKey("cost")) return Pick(obj, "id", "title", "type");
            var result = new JsonObject();
            foreach (var p in obj) if (p.Key is not ("state" or "powers" or "description" or "generated_definition" or "FollowUpState" or "FollowUpStateId")) result[p.Key] = Compact(p.Value);
            return result;
        }
        return JsonSerializer.SerializeToElement(Compact(JsonNode.Parse(entry.GetRawText())), Wire.Json);
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
