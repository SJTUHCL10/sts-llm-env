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
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;

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
            deck = player.Deck.Cards.Select(Card).ToArray(), gold = player.Gold,
            relics = player.Relics.Select(m => new { id = m.Id.Entry, state = Scalars(m) }).ToArray(),
            potions = player.Potions.Select(m => new { id = m.Id.Entry, state = Scalars(m) }).ToArray()
        };
    }

    public object State(ICombatState combat, Player player)
    {
        var pcs = player.PlayerCombatState;
        return new
        {
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
        instance = _cardIds.GetValue(card, _ => new Identity(++_nextId)).Id,
        id = card.Id.Entry, title = Safe(() => card.Title), type = card.Type.ToString(), rarity = card.Rarity.ToString(),
        cost = card.EnergyCost.Canonical, current_cost = Safe(() => card.EnergyCost.GetWithModifiers(CostModifiers.All)),
        x_cost = card.EnergyCost.CostsX, upgraded = card.IsUpgraded, upgrade_level = card.CurrentUpgradeLevel,
        target = card.TargetType.ToString(), keywords = card.Keywords.Select(k => k.ToString()).ToArray(),
        variables = card.DynamicVars.ToDictionary(v => v.Key, v => new { base_value = v.Value.BaseValue, preview = v.Value.PreviewValue }),
        description = Safe(() => card.GetDescriptionForPile(card.Pile?.Type ?? PileType.Deck)),
        enchantment = card.Enchantment is { } en ? new { id = en.Id.Entry, en.Amount } : null,
        affliction = card.Affliction is { } af ? new { id = af.Id.Entry, af.Amount } : null,
        generated_definition = card is NeowGeneratedCard generated ? generated.Definition : null
    };

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
