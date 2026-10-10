namespace Forge.Core;

public static class MechanicCatalog
{
    public const int ProtocolVersion = 5;
    public static readonly string[] Powers = ["strength", "dexterity", "weak", "vulnerable", "frail", "poison", "doom", "focus", "vigor", "thorns", "plating", "intangible", "artifact", "buffer", "retain_block"];
    public static readonly string[] Cards = ["soul", "shiv", "wound", "dazed", "burn", "void", "slimed", "fuel", "debris", "minion_strike", "minion_sacrifice", "minion_dive", "sovereign_blade"];
    public static readonly string[] Orbs = ["lightning", "frost", "dark", "plasma", "glass", "random"];
    public static readonly string[] Stats = ["hp", "max_hp", "block", "power", "energy", "stars", "hand_size", "draw_size", "discard_size", "exhaust_size", "orb_count", "orb_capacity", "paid_energy", "paid_stars", "event_amount", "damage_dealt"];
}

public static class CardValidator
{
    public static CardDefinition Validate(CardDefinition card)
    {
        Require(card is not null, "Missing card.");
        Require(SafeText(card.Name, 40) && (card.Flavor is null || SafeText(card.Flavor, 160, true)), "Invalid plain-text name/flavor.");
        Require(Enum.IsDefined(card.Type) && Enum.IsDefined(card.Rarity), "Invalid type/rarity.");
        Require(card.Forms is { Length: 2 }, "Exactly two complete forms are required.");
        foreach (var form in card.Forms)
        {
            Require(form is not null && form.Cost is not null, "Missing form/cost.");
            var cost = form.Cost;
            Require(cost.Energy >= 0 && cost.Stars is not < 0, "Negative cost.");
            Require(form.Tags.Length <= 5 && form.Tags.Distinct().Count() == form.Tags.Length && form.Tags.All(Enum.IsDefined), "Invalid keywords.");
            Require(form.Listeners.Length <= 8 && form.Listeners.All(r => r is not null && r.Effects is not null)
                && form.AllEffects.Length is >= 1 and <= 24, "A form needs 1..24 actions, at most 8 rules.");
            ValidateEffects(form.Immediate, false, new());
            foreach (var rule in form.Listeners)
            {
                Require(rule is not null && rule.Trigger is not null && Enum.IsDefined(rule.Lifetime), "Invalid rule.");
                var trigger = rule.Trigger;
                Require(Enum.IsDefined(trigger.Event), "Invalid event.");
                Require(rule.Effects is { Length: >= 1 and <= 8 }, "A rule needs 1..8 actions.");
                Require(rule.Turns is null or > 0, "Invalid turn lifetime.");
                Require(rule.Lifetime != LifetimeKind.Combat || rule.Turns is null, "Combat lifetime has no turns.");
                Require(rule.Lifetime != LifetimeKind.NextTurn || trigger.Event == RuleEvent.TurnStart && rule.Turns is null, "Next-turn rules run at the next turn start once.");
                bool cardEvent = trigger.Event is RuleEvent.CardPlayed or RuleEvent.CardDrawn or RuleEvent.CardDiscarded or RuleEvent.CardExhausted or RuleEvent.CardGenerated;
                Require(trigger.Filter is null || cardEvent, "Card filter requires a card event.");
                ValidateFilter(trigger.Filter);
                if (trigger.Occurrence is { } occurrence)
                {
                    Require(cardEvent, "Occurrences currently require a card event.");
                    Require(Enum.IsDefined(occurrence.Within) && occurrence.First is null or > 0 && occurrence.Nth is null or > 0 && occurrence.Every is null or > 0, "Invalid occurrence.");
                    Require(new[] { occurrence.First, occurrence.Nth, occurrence.Every }.Count(v => v is not null) == 1, "Specify first, nth, or every.");
                }
                if (trigger.Limit is { } limit) Require(limit.Count > 0 && Enum.IsDefined(limit.Within), "Invalid successful-activation quota.");
                ValidateCondition(rule.Condition, true);
                ValidateEffects(rule.Effects, true, new());
            }
            Require(card.Type != ForgeCardType.Power || form.Listeners.Length > 0 || form.Immediate.Any(e => e.Kind == EffectKind.ApplyPower), "Power needs a rule or native power.");
            Require(card.Type != ForgeCardType.Power || !form.Tags.Contains(ForgeKeyword.Exhaust), "Power cannot exhaust.");
            Require(card.Type != ForgeCardType.Attack || form.AllEffects.Any(e => e.Kind == EffectKind.Damage), "Attack needs damage.");
        }
        return card;
    }

    private static void ValidateEffects(CardEffect[] effects, bool triggered, HashSet<string> bindings)
    {
        foreach (var e in effects)
        {
            Require(e is not null && Enum.IsDefined(e.Kind), "Invalid action.");
            ValidateCondition(e.Condition, triggered);
            if (e.Repeat is not null) ValidateNumber(e.Repeat, triggered, false);
            if (e.Amount is not null) ValidateNumber(e.Amount, triggered, e.Kind is EffectKind.ApplyPower or EffectKind.OrbSlots);
            if (e.Count is not null) ValidateNumber(e.Count, triggered, false);
            bool cards = e.Kind is EffectKind.Discard or EffectKind.Exhaust or EffectKind.Move or EffectKind.Select or EffectKind.Upgrade or EffectKind.Copy or EffectKind.Transform or EffectKind.Play or EffectKind.AddKeyword or EffectKind.RemoveKeyword or EffectKind.SetCost;
            bool orbTarget = e.Kind is EffectKind.Evoke or EffectKind.OrbPassive;
            if (!cards && e.Target is { } referenceTarget)
                Require(referenceTarget.Pile is null && referenceTarget.Pick is null && referenceTarget.Count is null && referenceTarget.Filter is null && referenceTarget.UpTo is null, "Creature/orb target cannot also select cards.");
            if (cards) ValidateCardTarget(e.Target, triggered, bindings);
            else if (orbTarget) Require(e.Target?.Ref is null or "first_orb" or "last_orb" or "all_orbs", "Invalid orb target.");
            else Require(e.Target is null || e.Target.Ref is "self" or "enemy" or "all_enemies" or "random_enemy" or "osty" or "event.target", "Invalid creature target.");
            if (e.Kind == EffectKind.Damage) Require(e.Target?.Ref is "enemy" or "all_enemies" or "random_enemy" or "event.target", "Damage needs an enemy/event target.");
            if (e.Kind is EffectKind.Draw or EffectKind.GainEnergy or EffectKind.GainStars or EffectKind.Summon or EffectKind.Forge or EffectKind.Channel or EffectKind.OrbSlots or EffectKind.CreateCard)
                Require(e.Target?.Ref is null or "self" && e.Target?.Pile is null, "Player operation requires self.");
            Require(!triggered || e.Target?.Ref != "enemy", "Rule needs random/all enemies or event.target.");
            Require(triggered || e.Target?.Ref != "event.target", "Event target requires a rule.");
            Require(e.Actor is null || e.Kind == EffectKind.Damage && e.Actor == "osty", "Only damage supports an Osty actor.");
            Require(e.Power is null || e.Kind == EffectKind.ApplyPower, "power is only valid on apply_power.");
            Require(e.Orb is null || e.Kind == EffectKind.Channel, "orb is only valid on channel.");
            Require(e.Card is null || e.Kind is EffectKind.CreateCard or EffectKind.Transform, "card source requires create_card/transform.");
            Require(e.To is null || e.Kind is EffectKind.CreateCard or EffectKind.Copy or EffectKind.Move, "Unexpected destination.");
            Require(e.To is null || Enum.IsDefined(e.To.Value), "Invalid destination.");
            Require(e.Position is null || e.To == CardPileName.Draw && e.Position is "top" or "bottom" or "random", "Position requires draw destination.");
            Require(e.As is null || e.Kind == EffectKind.Select, "Only select creates a binding.");
            Require(e.Keyword is null || e.Kind is EffectKind.AddKeyword or EffectKind.RemoveKeyword, "Unexpected keyword.");
            Require(e.Until is null || e.Kind == EffectKind.SetCost || e.Kind == EffectKind.ApplyPower && e.Until == LifetimeKind.Turn && e.Power is "strength" or "focus"
                || e.Kind == EffectKind.AddKeyword && (e.Until == LifetimeKind.Combat || e.Until == LifetimeKind.Turn && e.Keyword is ForgeKeyword.Sly or ForgeKeyword.Retain), "Invalid modification lifetime.");
            Require(e.Remove is null || e.Kind == EffectKind.Evoke, "remove is only valid on evoke.");
            bool amount = e.Kind is EffectKind.Damage or EffectKind.Block or EffectKind.Draw or EffectKind.GainEnergy or EffectKind.GainStars or EffectKind.ApplyPower or EffectKind.SetCost or EffectKind.Summon or EffectKind.Forge or EffectKind.Channel or EffectKind.OrbSlots or EffectKind.Heal or EffectKind.LoseHp;
            Require(amount == (e.Amount is not null), "Action amount is missing or irrelevant.");
            Require(e.Count is null || e.Kind is EffectKind.CreateCard or EffectKind.Copy, "count is only valid for create_card/copy; use target.count for selection.");
            if (e.Kind == EffectKind.ApplyPower) Require(e.Power is not null && MechanicCatalog.Powers.Contains(e.Power), "Unknown native power.");
            if (e.Kind == EffectKind.Channel) Require(e.Orb is not null && MechanicCatalog.Orbs.Contains(e.Orb), "Unknown orb.");
            if (e.Kind is EffectKind.Move or EffectKind.CreateCard or EffectKind.Copy) Require(e.To is not null, "Destination required.");
            if (e.Kind is EffectKind.CreateCard or EffectKind.Transform) ValidateSource(e.Card);
            if (e.Kind is EffectKind.AddKeyword or EffectKind.RemoveKeyword) Require(e.Keyword is not null && Enum.IsDefined(e.Keyword.Value), "Keyword required.");
            if (e.Kind == EffectKind.SetCost) Require(e.Until is LifetimeKind.Turn or LifetimeKind.Combat, "Cost change requires turn/combat lifetime.");
            if (e.Kind == EffectKind.Select)
            {
                Require(e.Condition is null && e.Repeat is null, "select cannot be conditional/repeated.");
                Require(e.As is not null && e.As.Length <= 24 && e.As.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && bindings.Add(e.As), "Invalid/duplicate selection binding.");
            }
        }
    }
    private static void ValidateCardTarget(EffectTarget? target, bool triggered, HashSet<string> bindings)
    {
        Require(target is not null, "Card target required.");
        if (target.Ref is { } reference)
        {
            Require(target.Pile is null && target.Pick is null && target.Count is null && target.Filter is null && target.UpTo is null, "Reference cannot also select.");
            Require(reference == "this_card" || reference == "event.card" && triggered || reference.StartsWith("selected:", StringComparison.Ordinal) && bindings.Contains(reference[9..]), "Unknown/unbound card reference.");
            return;
        }
        Require(target.Pile is not null && Enum.IsDefined(target.Pile.Value) && target.Pick is not null && Enum.IsDefined(target.Pick.Value), "Pile and selection mode required.");
        Require(target.UpTo is null || target.Pick == SelectionMode.Choose, "up_to requires choose.");
        Require(target.Pick != SelectionMode.All || target.Count is null, "All selection has no count.");
        if (target.Count is not null) ValidateNumber(target.Count, triggered, false);
        ValidateFilter(target.Filter);
    }
    public static void ValidateFilter(CardFilter? filter)
    {
        if (filter is null) return;
        Require(filter.Type is null or "attack" or "skill" or "power" or "status" or "curse", "Unknown filter type.");
        Require(filter.Id is null || MechanicCatalog.Cards.Contains(filter.Id), "Unknown card alias.");
        Require(filter.Cost is null or >= 0 && (filter.Rarity is null || Enum.IsDefined(filter.Rarity.Value)) && (filter.Keyword is null || Enum.IsDefined(filter.Keyword.Value)), "Invalid filter.");
    }
    private static void ValidateSource(CardSource? source)
    {
        Require(source is not null && (source.Id is not null) != (source.Pool is not null), "Card source needs exactly one ID or pool.");
        if (source.Id is not null) Require(MechanicCatalog.Cards.Contains(source.Id) && source.Pick is null && source.Filter is null && source.Options is null, "Invalid fixed card source.");
        else
        {
            Require(source.Pool is "character" or "colorless" && source.Pick is SelectionMode.Random or SelectionMode.Choose, "Invalid source pool/selection.");
            Require(source.Options is null || source.Pick == SelectionMode.Choose && source.Options is >= 1 and <= 10, "Invalid discovery options.");
            ValidateFilter(source.Filter);
        }
    }
    public static void ValidateNumber(NumberExpression expression, bool triggered = false, bool signed = true, int depth = 0)
    {
        Require(expression is not null && depth <= 6, "Expression nesting exceeds six.");
        int choices = (expression.Value is not null ? 1 : 0) + (expression.Stat is not null ? 1 : 0) + (expression.Add is not null ? 1 : 0) + (expression.Mul is not null ? 1 : 0) + (expression.Div is not null ? 1 : 0);
        Require(choices == 1, "Numeric expression needs exactly one operation.");
        if (expression.Value is int value) Require((signed || value >= 0) && expression.Of is null && expression.Id is null, "Invalid literal.");
        else if (expression.Stat is { } stat)
        {
            Require(MechanicCatalog.Stats.Contains(stat) && expression.Of is null or "self" or "target" or "osty", "Invalid stat reference.");
            Require(expression.Of is null || stat is "hp" or "max_hp" or "block" or "power", "Only creature stats have a subject.");
            Require(stat != "event_amount" || triggered, "Event amount requires a rule.");
            Require(stat == "power" ? expression.Id is not null && MechanicCatalog.Powers.Contains(expression.Id) : expression.Id is null, "Invalid power stat ID.");
        }
        else
        {
            Require(expression.Of is null && expression.Id is null, "Arithmetic cannot specify a subject.");
            var children = expression.Add ?? expression.Mul ?? expression.Div!;
            Require(children.Length is >= 2 and <= 4 && (expression.Div is null || children.Length == 2), "Invalid expression arity.");
            foreach (var child in children) ValidateNumber(child, triggered, true, depth + 1);
            Require(expression.Div is null || expression.Div[1].Value != 0, "Division by zero.");
        }
    }
    public static void ValidateCondition(EffectCondition? condition, bool triggered, int depth = 0)
    {
        if (condition is null) return;
        Require(depth <= 6, "Condition nesting exceeds six.");
        Require((condition.Op is not null ? 1 : 0) + (condition.All is not null ? 1 : 0) + (condition.Any is not null ? 1 : 0) + (condition.Not is not null ? 1 : 0) == 1, "Condition needs exactly one operation.");
        if (condition.Op is { } op)
        {
            Require(Enum.IsDefined(op) && condition.Left is not null && condition.Right is not null, "Invalid comparison.");
            ValidateNumber(condition.Left, triggered); ValidateNumber(condition.Right, triggered);
        }
        else
        {
            Require(condition.Left is null && condition.Right is null, "Irrelevant comparison operands.");
            var children = condition.All ?? condition.Any ?? [condition.Not!];
            Require(children.Length is >= 1 and <= 4 && children.All(c => c is not null), "Invalid boolean arity.");
            foreach (var child in children) ValidateCondition(child, triggered, depth + 1);
        }
    }
    private static bool SafeText(string? value, int max, bool empty = false) => value is not null && value.Length <= max && (empty || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) || c is '[' or ']' or '{' or '}' or '<' or '>');
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new FormatException(message); }
}
