namespace Forge.Core;

// Descriptions are derived from the program; generated prose never drives execution.
public static class CardText
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["strength"]="力量", ["dexterity"]="敏捷", ["weak"]="虚弱", ["vulnerable"]="易伤", ["frail"]="脆弱",
        ["poison"]="中毒", ["doom"]="灾厄", ["focus"]="集中", ["vigor"]="活力", ["thorns"]="荆棘",
        ["plating"]="覆甲", ["intangible"]="无实体", ["artifact"]="人工制品", ["buffer"]="缓冲",
        ["soul"]="灵魂", ["shiv"]="小刀", ["wound"]="伤口", ["dazed"]="晕眩", ["burn"]="灼伤", ["void"]="虚空", ["slimed"]="黏液", ["fuel"]="燃料", ["debris"]="碎屑", ["minion_strike"]="仆从打击", ["minion_sacrifice"]="仆从捐躯", ["minion_dive"]="仆从俯冲", ["sovereign_blade"]="君王之剑",
        ["lightning"]="闪电", ["frost"]="冰霜", ["dark"]="黑暗", ["plasma"]="等离子", ["glass"]="玻璃", ["random"]="随机",
        ["hp"]="当前生命值", ["max_hp"]="最大生命值", ["gold"]="金币", ["block"]="格挡", ["energy"]="能量", ["stars"]="星", ["hand_size"]="手牌数", ["draw_size"]="抽牌堆牌数", ["discard_size"]="弃牌堆牌数", ["exhaust_size"]="消耗堆牌数", ["orb_count"]="充能球数", ["orb_capacity"]="充能球栏位数", ["paid_energy"]="本次消耗能量", ["paid_stars"]="本次消耗星", ["event_amount"]="本次事件数值", ["damage_dealt"]="攻击造成的伤害",
        ["attack"]="攻击", ["skill"]="技能", ["power"]="能力", ["status"]="状态", ["curse"]="诅咒",
        ["exhaust"]="消耗", ["ethereal"]="虚无", ["retain"]="保留", ["innate"]="固有", ["sly"]="奇巧",
        ["event_total_damage"]="本次伤害（含被格挡部分）", ["event_unblocked_damage"]="本次未被格挡的伤害", ["event_paid_energy"]="打出该牌实际花费的能量", ["event_paid_stars"]="打出该牌实际花费的星", ["event_target_count"]="本次影响的生物数量"
    };
    public static string Name(string id, bool chinese) => chinese ? Names.GetValueOrDefault(id, id) : id.Replace('_', ' ');
    private static bool UsesX(NumberExpression? number, CardCost? cost) => number is not null
        && (number.Stat == "paid_energy" && cost?.EnergyX == true || number.Stat == "paid_stars" && cost?.StarsX == true
            || (number.Add ?? number.Sub ?? number.Mul ?? number.Div)?.Any(n => UsesX(n, cost)) == true);
    public static string Number(NumberExpression? number, bool chinese, int fallback = 1, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        if (number is null) return fallback.ToString();
        if (number.Value is decimal value) return value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
        if (number.Stat is { } stat)
        {
            if (UsesX(number, cost)) return "X";
            if (stat is "event_amount" or "damage_dealt") return chinese ? Name(stat, true) : stat == "event_amount" ? "the event amount" : "the damage dealt by the last attack";
            if (stat.StartsWith("event_", StringComparison.Ordinal)) return chinese ? Name(stat, true) : "the " + Name(stat, false);
            if (stat is "paid_energy" or "paid_stars")
                return (chinese ? "本次消耗的" : "the amount of ") + (resource?.Invoke(stat == "paid_energy" ? "energy" : "stars", "1") ?? Name(stat == "paid_energy" ? "energy" : "stars", chinese)) + (chinese ? "数量" : " spent");
            string subject = number.Of switch
            {
                "osty" => chinese ? "奥斯提的" : "Osty's ", "target" => chinese ? "目标的" : "target's ",
                "event.actor" => chinese ? "事件执行者的" : "the event actor's ", "event.source" => chinese ? "事件来源生物的" : "the event source creature's ",
                "event.subject" => chinese ? "事件受影响生物的" : "the affected creature's ", "event.target" => chinese ? "事件目标的" : "the event target's ",
                _ => chinese ? "你的" : "your "
            };
            return subject + (stat is "energy" or "stars" && resource is not null ? resource(stat, "1") + (chinese ? "数量" : " amount")
                : Name(stat == "power" ? number.Id! : stat, chinese) + (stat == "power" && chinese ? PowerUnit(number.Id!) + "数" : ""));
        }
        // Keep legacy programs intact while displaying their subtraction idiom naturally.
        if (number.Add is { Length: 2 } sum && NegativeFactor(sum[1]) is { } subtrahend)
            return Number(new() { Sub = [sum[0], subtrahend] }, chinese, resource: resource, cost: cost);
        string op = number.Add is not null ? " + " : number.Sub is not null ? " - " : number.Mul is not null ? " × " : " ÷ ";
        return "(" + string.Join(op, (number.Add ?? number.Sub ?? number.Mul ?? number.Div!).Select(n => Number(n, chinese, resource: resource, cost: cost))) + ")";
    }
    private static NumberExpression? NegativeFactor(NumberExpression number) => number.Mul is { Length: 2 } factors
        ? factors[0].Value == -1 ? factors[1] : factors[1].Value == -1 ? factors[0] : null : null;
    // Compare corresponding expression nodes; keep unchanged terms plain in upgrade previews.
    public static string NumberUpgrade(NumberExpression? number, NumberExpression? baseline, bool chinese, int fallback = 1,
        Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        NumberExpression? Normalize(NumberExpression? expression) => expression?.Add is { Length: 2 } sum && NegativeFactor(sum[1]) is { } subtrahend
            ? new() { Sub = [sum[0], subtrahend] } : expression;
        number = Normalize(number); baseline = Normalize(baseline);
        if (Wire.Encode(number) == Wire.Encode(baseline)) return Number(number, chinese, fallback, resource, cost);
        string? Operator(NumberExpression? expression) => expression?.Add is not null ? " + " : expression?.Sub is not null ? " - "
            : expression?.Mul is not null ? " × " : expression?.Div is not null ? " ÷ " : null;
        var current = number?.Add ?? number?.Sub ?? number?.Mul ?? number?.Div;
        var previous = baseline?.Add ?? baseline?.Sub ?? baseline?.Mul ?? baseline?.Div;
        if (current is not null && previous is not null && current.Length == previous.Length && Operator(number) == Operator(baseline))
            return "(" + string.Join(Operator(number), current.Select((term, i) => NumberUpgrade(term, previous[i], chinese, fallback, resource, cost))) + ")";
        // An added wrapper can retain the complete old expression as one of its terms.
        if (current is not null && current.Any(term => Wire.Encode(Normalize(term)) == Wire.Encode(baseline)))
            return "(" + string.Join(Operator(number), current.Select(term => NumberUpgrade(term, baseline, chinese, fallback, resource, cost))) + ")";
        // A changed operator/reference or a new subtree has no reliable leaf correspondence.
        return "[green]" + Number(number, chinese, fallback, resource, cost) + "[/green]";
    }
    private static string CountNumber(NumberExpression? number, bool chinese, int fallback = 1, Func<string, string, string>? resource = null, CardCost? cost = null)
        => number?.Value is decimal value ? EffectRules.Count(value).ToString(System.Globalization.CultureInfo.InvariantCulture) : Number(number, chinese, fallback, resource, cost);
    public static string Condition(EffectCondition? condition, bool chinese, Func<string, string, string>? resource = null, CardCost? cost = null,
        EffectCondition? upgradeBaseline = null, bool highlightUpgrade = false)
    {
        if (condition is null) return "";
        if (condition.Not is { } not) return (chinese ? "不满足 " : "not ") + Condition(not, chinese, resource, cost, upgradeBaseline?.Not, highlightUpgrade);
        if (condition.All is { } all) return string.Join(chinese ? "且" : " and ", all.Select((c, i) => Condition(c, chinese, resource, cost, upgradeBaseline?.All?.ElementAtOrDefault(i), highlightUpgrade)));
        if (condition.Any is { } any) return string.Join(chinese ? "或" : " or ", any.Select((c, i) => Condition(c, chinese, resource, cost, upgradeBaseline?.Any?.ElementAtOrDefault(i), highlightUpgrade)));
        string op = condition.Op switch { Comparison.Eq => "=", Comparison.Ne => "≠", Comparison.Gt => ">", Comparison.Ge => "≥", Comparison.Lt => "<", _ => "≤" };
        string Operand(NumberExpression? number, NumberExpression? other, NumberExpression? baseline)
        {
            string text = highlightUpgrade ? NumberUpgrade(number, baseline, chinese, resource: resource, cost: cost)
                : Number(number, chinese, resource: resource, cost: cost);
            string? kind = other?.Stat switch { "energy" or "paid_energy" => "energy", "stars" or "paid_stars" => "stars", _ => null };
            return number?.Value is not null && kind is not null ? Resource(kind, text, chinese, resource) : text;
        }
        return Operand(condition.Left, condition.Right, upgradeBaseline?.Left) + " " + op + " " + Operand(condition.Right, condition.Left, upgradeBaseline?.Right);
    }
    public static string Target(EffectTarget? target, bool chinese, Func<string, string, string>? resource = null, CardCost? cost = null, Func<NumberExpression?, int, string>? countText = null)
    {
        if (target?.Ref is { } reference) return reference switch
        {
            "self" => chinese ? "你" : "you", "enemy" => chinese ? "目标敌人" : "the enemy", "random_enemy" => chinese ? "随机敌人" : "a random enemy", "all_enemies" => chinese ? "所有敌人" : "ALL enemies", "osty" => chinese ? "奥斯提" : "Osty", "this_card" => chinese ? "本牌" : "this card", "event.card" => chinese ? "该牌" : "that card", "event.target" => chinese ? "事件目标" : "the event target", "event.targets" => chinese ? "该事件的所有目标" : "all event targets", "event.actor" => chinese ? "事件执行者" : "the event actor", "event.source" => chinese ? "事件来源生物" : "the event source creature", "event.subject" => chinese ? "事件受影响生物" : "the affected creature", "first_orb" => chinese ? "最前方充能球" : "the first orb", "last_orb" => chinese ? "最后方充能球" : "the last orb", "all_orbs" => chinese ? "所有充能球" : "all orbs", _ => chinese ? "选中的牌" : "the selected cards"
        };
        if (target?.Pile is not { } pile) return chinese ? "你" : "you";
        string zone = Pile(pile, chinese);
        string selection = target.Pick switch { SelectionMode.All => chinese ? "所有" : "all ", SelectionMode.Any => chinese ? "任意张" : "any number of ", SelectionMode.Choose => chinese ? "" : "chosen ", SelectionMode.Random => chinese ? "随机" : "random ", SelectionMode.First => chinese ? "最前方" : "first ", _ => chinese ? "最后方" : "last " };
        string count = target.Pick is SelectionMode.All or SelectionMode.Any ? "" : (countText?.Invoke(target.Count, 1) ?? CountNumber(target.Count, chinese, resource: resource, cost: cost)) + (chinese ? "张" : " ");
        return (chinese ? "你的" + zone + "中的" : "from " + zone + ": ") + selection + (target.UpTo == true ? chinese ? "至多" : "up to " : "") + count + Filter(target.Filter, chinese, resource) + (chinese ? target.Filter?.Id is null ? "牌" : "" : "cards");
    }
    private static string Filter(CardFilter? filter, bool chinese, Func<string, string, string>? resource = null, string? pool = null)
    {
        if (filter is null) return pool ?? "";
        return (filter.Cost is int cost ? chinese ? $"耗能为{Resource("energy", cost.ToString(), true, resource)}的" : $"{Resource("energy", cost.ToString(), false, resource)}-cost " : "")
            + (filter.MinCost is int min ? chinese ? $"耗能至少为{Resource("energy", min.ToString(), true, resource)}的" : $"costing at least {min} energy " : "")
            + (filter.MaxCost is int max ? chinese ? $"耗能至多为{Resource("energy", max.ToString(), true, resource)}的" : $"costing at most {max} energy " : "")
            + (filter.Tag is { } tag ? chinese ? $"带有{(tag switch { "strike" => "打击", "defend" => "防御", "shiv" => "小刀", "minion" => "仆从", _ => "奥斯提攻击" })}标签的" : $"tagged {tag.Replace('_', ' ')} " : "")
            + (filter.Keyword is { } keyword ? Name(keyword.ToString().ToLowerInvariant(), chinese) + (chinese ? "的" : " ") : "")
            + (filter.Upgraded is bool upgraded ? chinese ? upgraded ? "已升级" : "未升级" : upgraded ? "upgraded " : "unupgraded " : "")
            + (filter.Rarity is { } rarity ? chinese ? rarity switch { ForgeRarity.Common => "普通", ForgeRarity.Uncommon => "罕见", _ => "稀有" } : rarity.ToString().ToLowerInvariant() + " " : "")
            + (pool ?? "")
            + (filter.Type is { } type ? Name(type, chinese) + (chinese ? "" : " ") : "")
            + (filter.Id is { } id ? Name(id, chinese) + (chinese ? "" : " ") : "");
    }
    public static string Pile(CardPileName pile, bool chinese) => chinese ? pile switch { CardPileName.Hand => "手牌", CardPileName.Draw => "抽牌堆", CardPileName.Discard => "弃牌堆", _ => "消耗堆" } : pile.ToString().ToLowerInvariant();
    public static string Render(CardDefinition card, bool chinese, Func<int, string>? amount = null, bool upgraded = false, Func<string, string, string>? resource = null, Func<int, string, NumberExpression?, int, string>? quantity = null, Func<int, int, string>? limit = null) => Render(card.Form(upgraded), chinese, amount, card.Type, resource, quantity, limit);
    public static string Render(CardForm form, bool chinese, Func<int, string>? amount = null, ForgeCardType? cardType = null, Func<string, string, string>? resource = null, Func<int, string, NumberExpression?, int, string>? quantity = null, Func<int, int, string>? limit = null,
        CardForm? upgradeBaseline = null)
    {
        var conditions = new Dictionary<EffectCondition, EffectCondition?>(ReferenceEqualityComparer.Instance);
        if (upgradeBaseline is not null)
        {
            var before = upgradeBaseline.AllEffects;
            foreach (var (effect, i) in form.AllEffects.Select((effect, i) => (effect, i)))
                if (effect.Condition is { } condition)
                    conditions[condition] = i < before.Length && effect.Kind == before[i].Kind ? before[i].Condition : null;
            for (int i = 0; i < form.Listeners.Length; i++)
                if (form.Listeners[i].Condition is { } condition)
                    conditions[condition] = upgradeBaseline.Listeners.ElementAtOrDefault(i)?.Condition;
        }
        string ConditionText(EffectCondition condition) => Condition(condition, chinese, resource, form.Cost,
            conditions.GetValueOrDefault(condition), upgradeBaseline is not null);
        int index = 0;
        string Action(CardEffect effect)
        {
            int slot = index++;
            var before = upgradeBaseline?.AllEffects;
            var baseline = before is not null && slot < before.Length && before[slot].Kind == effect.Kind ? before[slot] : null;
            return RenderEffect(effect, chinese, amount?.Invoke(slot), resource, form.Cost,
                quantity is null ? null : (name, value, fallback) => quantity(slot, name, value, fallback), Action, ConditionText,
                baseline, upgradeBaseline is not null);
        }
        var lines = RenderEffects(form.Immediate, chinese, Action, resource, form.Cost, ConditionText).ToList();
        for (int i = 0; i < form.Listeners.Length; i++)
        {
            int ruleIndex = i;
            lines.Add(RenderRule(form.Listeners[i], chinese, Action, cardType == ForgeCardType.Power, resource, form.Cost,
                limit is null ? null : count => limit(ruleIndex, count), ConditionText));
        }
        return string.Join("\n", lines);
    }
    private static bool ReadsStat(NumberExpression? number, string stat) => number is not null && (number.Stat == stat
        || (number.Add ?? number.Sub ?? number.Mul ?? number.Div ?? []).Any(n => ReadsStat(n, stat)));
    private static bool ReadsStat(EffectCondition condition, string stat) => ReadsStat(condition.Left, stat) || ReadsStat(condition.Right, stat)
        || (condition.All ?? condition.Any ?? []).Any(c => ReadsStat(c, stat)) || condition.Not is { } not && ReadsStat(not, stat);
    // Only share conditions across resource gains that cannot change their tested stat.
    // Other actions can mutate state or fire listeners, so keep their checks explicit.
    private static bool PreservesCondition(CardEffect effect, EffectCondition condition) => effect.Kind switch
    {
        EffectKind.GainEnergy => !ReadsStat(condition, "energy"),
        EffectKind.GainStars => !ReadsStat(condition, "stars"),
        _ => false
    };
    private static IEnumerable<string> RenderEffects(CardEffect[] effects, bool chinese, Func<CardEffect, string> render,
        Func<string, string, string>? resource, CardCost? cost, Func<EffectCondition, string>? conditionText = null)
    {
        for (int i = 0; i < effects.Length;)
        {
            var effect = effects[i];
            int end = i + 1;
            if (effect.Condition is { } condition && effect.Repeat is null && effect.Target?.Ref is null or "self")
                while (end < effects.Length && effects[end].Repeat is null && effects[end].Target?.Ref is null or "self" && effects[end].Condition is { } next
                    && Wire.Encode(condition) == Wire.Encode(next) && PreservesCondition(effects[end - 1], condition)) end++;
            if (end == i + 1) yield return render(effect);
            else
            {
                var texts = effects[i..end].Select(e => render(e with { Condition = null })).ToArray();
                yield return (chinese ? "若" : "If ") + (conditionText?.Invoke(effect.Condition!) ?? Condition(effect.Condition, chinese, resource, cost)) + (chinese ? "，" : ": ")
                    + (chinese ? string.Join("，", texts.Select(t => t.TrimEnd('。'))) + "。" : string.Join(" ", texts));
            }
            i = end;
        }
    }
    public static string RenderRule(CardRule rule, bool chinese, Func<CardEffect, string>? render = null, bool combatIsImplicit = false, Func<string, string, string>? resource = null, CardCost? cost = null, Func<int, string>? limit = null,
        Func<EffectCondition, string>? conditionText = null)
    {
        string actor = EventRelation(rule.Trigger.Where?.Actor ?? "self", chinese);
        string when = rule.Trigger.Event switch
        {
            RuleEvent.TurnStart => chinese ? "在你的回合开始时" : "At the start of your turn", RuleEvent.TurnEnd => chinese ? "在你的回合结束时" : "At the end of your turn",
            RuleEvent.CardPlayed => chinese ? "打出" : "play", RuleEvent.CardDrawn => chinese ? "抽到" : "draw", RuleEvent.CardDiscarded => chinese ? "丢弃" : "discard", RuleEvent.CardExhausted => chinese ? "消耗" : "exhaust", RuleEvent.CardGenerated => chinese ? "生成" : "create",
            _ => EventWhen(rule.Trigger, chinese, resource)
        };
        string verb = when;
        string card = Filter(rule.Trigger.Filter, chinese, resource) + (chinese ? rule.Trigger.Filter?.Id is null ? "牌" : "" : "card");
        if (rule.Trigger.Event is >= RuleEvent.CardPlayed and <= RuleEvent.CardGenerated)
            when = (chinese ? "每当" + actor : "Whenever " + actor + " ") + (chinese ? when : EventVerb(when, actor))
                + (chinese ? "一张" : " a ") + card;
        var restrictions = EventRestrictions(rule.Trigger, chinese).ToList();
        if (!RuleEvents.IsCardEvent(rule.Trigger.Event) && rule.Trigger.Filter is not null)
            restrictions.Add(chinese ? "由一张" + card + "引发" : "caused by a " + card);
        if (restrictions.Count > 0) when += chinese ? "（" + string.Join("，", restrictions) + "）" : " (" + string.Join(", ", restrictions) + ")";
        string life = rule.Lifetime switch
        {
            LifetimeKind.Combat => combatIsImplicit ? "" : chinese ? "本场战斗，" : "For this combat, ",
            LifetimeKind.NextTurn => chinese ? "下回合，" : "Next turn, ",
            _ when rule.Trigger.Event == RuleEvent.TurnStart => rule.Turns is null or 1 ? chinese ? "下回合，" : "Next turn, " : chinese ? $"接下来的{rule.Turns}个回合，" : $"For the next {rule.Turns} turn starts, ",
            _ => rule.Turns is null or 1 ? chinese ? "本回合，" : "This turn, " : chinese ? $"从本回合起的{rule.Turns}个回合，" : $"For {rule.Turns} turns including this one, "
        };
        if (rule.Trigger.Occurrence is { } o)
        {
            string scope = o.Within == CounterScope.Turn ? chinese ? "每回合" : "each turn: " : chinese ? "本场战斗" : "this combat: ";
            if (combatIsImplicit && rule.Lifetime == LifetimeKind.Combat && o.Within == CounterScope.Turn && o.Every is not null) scope = "";
            when = chinese ? o.Every is int every ? $"{scope}{(scope.Length == 0 ? "" : "，")}每{verb}{every}张{card}时"
                : scope + (o.First is int first ? first == 1 ? "首次" : $"前{first}次" : $"第{o.Nth}次") + verb + "一张" + card + "时"
                : scope + (o.First is int firstEnglish ? $"the first {firstEnglish} times you " : o.Nth is int nth ? $"the {nth}th time you " : $"{(scope.Length == 0 ? "Every" : "every")} {o.Every} times you ") + verb + " a " + card;
        }
        string quota = rule.Trigger.Limit is { } quotaLimit ? chinese ? $"（{(quotaLimit.Within == CounterScope.Turn ? "每回合" : "本场战斗")}最多生效{limit?.Invoke(quotaLimit.Count) ?? quotaLimit.Count.ToString()}次）" : $" (at most {limit?.Invoke(quotaLimit.Count) ?? quotaLimit.Count.ToString()} activations per {quotaLimit.Within.ToString().ToLowerInvariant()})" : "";
        string condition = rule.Condition is null ? "" : (chinese ? "若" : "if ") + (conditionText?.Invoke(rule.Condition) ?? Condition(rule.Condition, chinese, resource, cost)) + (chinese ? "，" : ": ");
        var effects = RenderEffects(rule.Effects, chinese, render ?? (e => RenderEffect(e, chinese, resource: resource, cost: cost, conditionText: conditionText)), resource, cost, conditionText);
        string text = life + when + quota + (chinese ? "，" : ", ") + condition
            + (chinese ? string.Join(rule.Effects.Any(e => e.Repeat is not null) ? "。" : "，", effects.Select(t => t.TrimEnd('。'))) + "。" : string.Join(" ", effects));
        string eventAmount = rule.Trigger.Event switch
        {
            RuleEvent.Summoned => chinese ? "本次召唤量" : "the amount summoned",
            RuleEvent.DamageReceived => chinese ? "本次受到的未格挡伤害" : "the unblocked damage received",
            RuleEvent.DamageGiven => chinese ? "本次伤害（含被格挡部分）" : "the total damage of this hit",
            RuleEvent.BlockGained => chinese ? "本次获得的格挡" : "the Block gained",
            RuleEvent.PowerAmountChanged => chinese ? "本次能力层数变化量" : "the signed power amount change",
            RuleEvent.Forged => chinese ? "本次铸造量" : "the amount forged",
            RuleEvent.StarsGained => resource is null ? chinese ? "本次获得的星数" : "the Stars gained"
                : chinese ? "本次获得的" + resource("stars", "1") + "数量" : "the " + resource("stars", "1") + " gained",
            RuleEvent.StarsSpent => resource is null ? chinese ? "本次花费的星数" : "the Stars spent"
                : chinese ? "本次花费的" + resource("stars", "1") + "数量" : "the " + resource("stars", "1") + " spent",
            _ => chinese ? "本次事件数值" : "the event amount"
        };
        text = text.Replace(chinese ? "本次事件数值" : "the event amount", eventAmount, StringComparison.Ordinal);
        foreach (string role in new[] { "actor", "source", "subject", "target", "targets" })
        {
            string reference = Target("event." + role, chinese), resolved = EventReference(rule.Trigger, role, chinese);
            if (!chinese) text = text.Replace(reference + "'s", Possessive(resolved), StringComparison.Ordinal);
            text = text.Replace(reference, resolved, StringComparison.Ordinal);
        }
        return text;
    }
    private static string EventRelation(string relation, bool chinese) => chinese ? relation switch
    {
        "self" => "你", "osty" => "奥斯提", "self_or_osty" => "你或奥斯提", "enemy" => "敌人", _ => "任意生物"
    } : relation switch { "self" => "you", "osty" => "Osty", "self_or_osty" => "you or Osty", "enemy" => "an enemy", _ => "any creature" };
    private static string EventVerb(string verb, string subject) => subject == "you" ? verb : verb + "s";
    private static string Possessive(string subject) => subject == "you" ? "your" : subject + "'s";
    private static string EventWhen(EffectTrigger trigger, bool chinese, Func<string, string, string>? resource)
    {
        var w = trigger.Where;
        string actor = EventRelation(w?.Actor ?? "self", chinese);
        string recipient = EventRelation(w?.Target ?? "self", chinese);
        string damage = chinese ? w?.Attack switch { true => "攻击伤害", false => "非攻击伤害", _ => "伤害" }
            : w?.Attack switch { true => "attack damage", false => "non-attack damage", _ => "damage" };
        string power = w?.Power is { } id ? Name(id, chinese) : chinese ? "状态" : "power";
        string powerChange = power + (w?.Power is null ? "" : "层数") + "发生" + (w?.PowerType == "buff" ? "正面" : "负面") + "变化";
        string orb = (w?.Orb is { } kind ? Name(kind, chinese) : "") + (chinese ? "充能球" : " Orb");
        string After(string verb, string english) => chinese ? $"每当{actor}{verb}时" : $"After {actor} {english}";
        string Verb(string plain) => EventVerb(plain, actor);
        string stars = resource?.Invoke("stars", "1") ?? (chinese ? "星" : "Stars");
        // These native debuffs classify positive deltas as Debuff and negative deltas as Buff.
        // Match native Vicious wording for applications, while retaining reductions and recipient-only filters.
        string? namedDebuffChange = null;
        if (chinese && w?.Power is "weak" or "vulnerable" or "frail" or "poison" or "doom" && w.PowerType is not null)
        {
            bool applied = w.PowerType == "debuff";
            namedDebuffChange = w is { Target: not null, Actor: null }
                ? applied ? $"每当{recipient}获得{power}时" : $"每当{recipient}的{power}减少时"
                : $"每当{actor}{(applied ? "给予" : "减少")}{(w.Target is null ? "" : applied ? recipient : recipient + "的")}{power}时";
        }
        return trigger.Event switch
        {
            RuleEvent.DamageGiven => After((w?.Target is null ? "" : "对" + recipient) + "造成" + damage,
                Verb("deal") + " " + damage + (w?.Target is null ? "" : " to " + recipient)),
            RuleEvent.DamageReceived => chinese ? $"每当{recipient}受到{(w?.Actor is null ? "" : actor + "造成的")}{damage}时"
                : $"After {recipient} {(recipient == "you" || recipient == "you or Osty" ? "take" : "takes")} {damage}{(w?.Actor is null ? "" : " from " + actor)}",
            RuleEvent.BlockGained => chinese ? $"每当{EventRelation(w?.Actor ?? w?.Target ?? "self", true)}获得格挡时"
                : $"After {EventRelation(w?.Actor ?? w?.Target ?? "self", false)} {EventVerb("gain", EventRelation(w?.Actor ?? w?.Target ?? "self", false))} Block",
            RuleEvent.PowerAmountChanged when namedDebuffChange is not null => namedDebuffChange,
            RuleEvent.PowerAmountChanged => w is { Target: not null, Actor: null }
                ? chinese ? w.PowerType is not null ? $"每当{recipient}的{powerChange}时" : $"每当{recipient}的{power}层数改变时" : $"After {Possessive(recipient)} {power} amount changes"
                : After(w?.PowerType is not null ? "使" + (w.Target is null ? "" : recipient + "的") + powerChange : "改变" + (w?.Target is null ? "" : recipient + "的") + power + "层数",
                    Verb("change") + " " + (w?.Target is null ? "a " : Possessive(recipient) + " ") + power + " amount"),
            RuleEvent.Summoned => After("召唤", Verb("summon")),
            RuleEvent.OrbChanneled => After("生成" + orb, Verb("channel") + " a " + orb.Trim()),
            RuleEvent.OrbEvoked => After("激发" + orb, Verb("evoke") + " a " + orb.Trim()),
            RuleEvent.Forged => After("铸造", Verb("forge")),
            RuleEvent.StarsGained => After("获得" + stars, Verb("gain") + " " + stars),
            RuleEvent.StarsSpent => After("花费" + stars, Verb("spend") + " " + stars),
            _ => throw new FormatException("Unknown rule event.")
        };
    }
    // Resolve rule references by their gameplay role, rather than exposing protocol field names.
    private static string EventReference(EffectTrigger trigger, string role, bool chinese)
    {
        var w = trigger.Where;
        bool received = trigger.Event == RuleEvent.DamageReceived;
        bool damage = received || trigger.Event == RuleEvent.DamageGiven;
        bool block = trigger.Event == RuleEvent.BlockGained;
        // A target filter only promises that some target matches on multi-target orb events.
        string? targetRelation = trigger.Event == RuleEvent.OrbEvoked ? null : w?.Target;
        string? relation = role switch
        {
            "actor" => w?.Actor ?? (damage || trigger.Event == RuleEvent.PowerAmountChanged && w?.Target is not null ? null : block ? w?.Target ?? "self" : "self"),
            "source" => w?.Source,
            "subject" => targetRelation ?? (received ? "self" : block ? w?.Actor ?? "self" : null),
            "target" => received ? w?.Actor : targetRelation ?? (block ? w?.Actor ?? "self" : null),
            _ => null
        };
        if (relation is "self" or "osty") return EventRelation(relation, chinese);
        if (relation == "enemy")
        {
            if (role == "source") return chinese ? "引发此效果的敌人" : "the enemy causing this effect";
            if (damage && w?.Actor == "enemy" && w.Target == "enemy")
                return role == "actor" || role == "target" && received
                    ? chinese ? "造成伤害的敌人" : "the enemy dealing damage"
                    : chinese ? "承受伤害的敌人" : "the enemy taking damage";
            return chinese ? "该敌人" : "that enemy";
        }
        return role switch
        {
            "actor" when damage => chinese ? "造成伤害的生物" : "the damage dealer",
            "actor" when block => chinese ? "获得格挡的生物" : "the creature gaining Block",
            "actor" when trigger.Event == RuleEvent.PowerAmountChanged => chinese ? "改变状态层数的生物" : "the creature changing the power",
            "source" => chinese ? "引发此效果的生物" : "the creature causing this effect",
            "target" when received => chinese ? "伤害来源" : "the damage dealer",
            "target" or "subject" when damage => chinese ? "该目标" : "that target",
            "target" or "subject" when block => chinese ? "获得格挡的生物" : "the creature gaining Block",
            "targets" => chinese ? "本次影响的所有生物" : "all creatures affected this time",
            _ => chinese ? "该生物" : "that creature"
        };
    }
    private static IEnumerable<string> EventRestrictions(EffectTrigger trigger, bool chinese)
    {
        var w = trigger.Where;
        if (w is null) yield break;
        if (w.Target is { } target && (trigger.Event is not (RuleEvent.DamageGiven or RuleEvent.DamageReceived or RuleEvent.PowerAmountChanged or RuleEvent.BlockGained)
            || trigger.Event == RuleEvent.BlockGained && w.Actor is not null && w.Actor != target))
            yield return chinese ? trigger.Event == RuleEvent.BlockGained ? "仅计入" + EventRelation(target, true) + "获得的格挡" : "至少影响" + EventRelation(target, true)
                : "affecting " + EventRelation(target, false);
        if (w.Source is not null || w.SourceKind is not null)
        {
            string owner = w.Source is { } source ? chinese ? EventRelation(source, true) + "的" : Possessive(EventRelation(source, false)) + " " : "";
            string cause = w.SourceKind switch
            {
                "card" => chinese ? "卡牌" : "card", "power" => chinese ? "能力" : "power",
                "relic" => chinese ? "遗物" : "relic", "orb" => chinese ? "充能球" : "Orb",
                "other" => chinese ? "其他效果" : "other effect", _ => chinese ? "效果" : "effect"
            };
            if (w.Source is not null || w.SourceKind != "unknown")
                yield return chinese ? "由" + owner + cause + "引发" : "caused by " + owner + (owner.Length > 0 ? "" : w.SourceKind is "orb" or "other" ? "an " : "a ") + cause;
            if (w.SourceKind == "unknown") yield return chinese ? "具体来源未知" : "specific cause unknown";
        }
        if (!chinese && w.PowerType is { } type) yield return type + " changes only";
        if (w.Temporary is bool temporary) yield return chinese ? temporary ? "仅限临时能力" : "排除临时能力" : temporary ? "temporary powers only" : "excluding temporary powers";
        if (w.OwnTurn is bool own) yield return chinese ? own ? "仅在你的回合" : "仅在敌方回合" : own ? "during your turn" : "outside your turn";
        if (w.FromHandDraw is bool initial) yield return chinese ? initial ? "仅限回合初始抽牌" : "排除回合初始抽牌" : initial ? "initial turn draw only" : "excluding initial turn draw";
        if (w.CausedByEthereal is bool ethereal) yield return chinese ? ethereal ? "仅限虚无消耗" : "排除虚无消耗" : ethereal ? "Ethereal exhaust only" : "excluding Ethereal exhaust";
        if (w.AutoPlay is bool auto) yield return chinese ? auto ? "仅限自动打出" : "仅限手动打出" : auto ? "autoplay only" : "manual play only";
        if (w.LastInSeries is bool last) yield return chinese ? last ? "仅限连打的最后一次" : "排除连打的最后一次" : last ? "last play in series only" : "excluding the last play in series";
    }
    public static string RenderEffect(CardEffect e, bool chinese, string? amount = null, Func<string, string, string>? resource = null, CardCost? cost = null, Func<string, NumberExpression?, int, string>? numeric = null, Func<CardEffect, string>? nested = null,
        Func<EffectCondition, string>? conditionText = null, CardEffect? upgradeBaseline = null, bool highlightUpgrade = false)
    {
        string Quantity(string slot, NumberExpression? value, int fallback = 1) => numeric?.Invoke(slot, value, fallback) ?? CountNumber(value, chinese, fallback, resource, cost);
        string? repeat = e.Repeat is null ? null : Quantity("repeat", e.Repeat);
        bool attackHits = chinese && e.Kind == EffectKind.Damage && e.Condition is null && e.Repeat is not null
            && (e.Repeat.Value is not null || UsesX(e.Repeat, cost));
        string n = UsesX(e.Amount, cost) ? amount is not null && amount.Contains('X') ? amount : Number(e.Amount, chinese, resource: resource, cost: cost)
            : amount ?? Number(e.Amount, chinese, resource: resource, cost: cost);
        if (e.Kind == EffectKind.Draw && decimal.TryParse(n, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal draws))
            n = decimal.Ceiling(Math.Max(0, draws)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string quantity = ChineseQuantity(n);
        string target = Target(e.Target, chinese, resource, cost, (value, fallback) => Quantity("target_count", value, fallback));
        string source = e.Card?.Id is { } id ? Name(id, chinese) : Filter(e.Card?.Filter, chinese, resource,
            e.Card?.Pool == "colorless" ? chinese ? "无色" : "Colorless " : chinese ? "本角色" : "character ");
        if (e.Card?.Pool is not null) source += chinese ? "牌" : "cards";
        if (e.Card?.Pick == SelectionMode.Random) source = (chinese ? "随机" : "random ") + source;
        if (e.Card?.Upgraded == true) source += "+";
        if (highlightUpgrade && e.Card?.Upgraded == true && upgradeBaseline?.Card?.Upgraded != true)
            source = "[green]" + source + "[/green]";
        if (e.Card?.Pick == SelectionMode.Choose) source = chinese ? $"从{e.Card.Options ?? 3}张随机{source}中选择1张" : $"choose one of {e.Card.Options ?? 3} random {source}";
        string destination = e.To is { } to ? (chinese ? "你的" : "your ") + Pile(to, chinese) + (to == CardPileName.Draw ? e.Position switch { "bottom" => chinese ? "底部" : " bottom", "random" => chinese ? "随机位置" : " randomly", _ => chinese ? "顶部" : " top" } : "") : "";
        string text = chinese ? e.Kind switch
        {
            EffectKind.GainMaxHp => $"{(e.Target?.Ref is null or "self" ? "" : target)}增加{quantity}最大生命。",
            EffectKind.LoseMaxHp => $"{(e.Target?.Ref is null or "self" ? "" : target)}减少{quantity}最大生命。",
            EffectKind.GainGold => $"获得{ChineseQuantity(n, "枚")}金币。",
            EffectKind.LoseGold => $"失去{ChineseQuantity(n, "枚")}金币。",
            EffectKind.Damage => $"{(e.Actor == "osty" ? "奥斯提" : "")}{(e.Target?.Ref == "enemy" ? "" : "对" + target)}造成{quantity}伤害。", EffectKind.Block => $"{(e.Target?.Ref is null or "self" ? "" : target)}获得{quantity}格挡。", EffectKind.Draw => $"抽{n}张牌。", EffectKind.GainEnergy => $"获得{Resource("energy", n, true, resource)}。", EffectKind.GainStars => $"获得{Resource("stars", n, true, resource)}。", EffectKind.ApplyPower => e.Target?.Ref is null or "self" ? $"获得{ChineseQuantity(n, PowerUnit(e.Power!))}{Name(e.Power!, true)}。" : $"对{target}给予{ChineseQuantity(n, PowerUnit(e.Power!))}{Name(e.Power!, true)}。",
            EffectKind.Discard => $"丢弃{target}。", EffectKind.Exhaust => $"消耗{target}。", EffectKind.Move => $"将{target}移至{destination}。", EffectKind.Select => $"选择{target}。", EffectKind.Upgrade => $"升级{target}。", EffectKind.Copy => $"将{target}的{Quantity("count", e.Count)}张复制品添加到{destination}。", EffectKind.Transform => e.Card?.Pick == SelectionMode.Choose ? $"{source}，将{target}变化为所选的牌。" : $"将{target}变化为{source}。", EffectKind.CreateCard => e.Card?.Pick == SelectionMode.Choose ? (e.Count is null || e.Count.Value == 1 ? "" : $"重复以下操作{Quantity("count", e.Count)}次：") + $"{source}，添加到{destination}。" : $"将{Quantity("count", e.Count)}张{source}添加到{destination}。", EffectKind.Play => $"自动打出{target}。", EffectKind.AddKeyword => $"{target}获得{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.RemoveKeyword => $"移除{target}的{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.SetCost => $"{target}在{(e.Until == LifetimeKind.Turn ? "本回合" : "本场战斗")}的耗能变为{Resource("energy", n, true, resource)}。",
            EffectKind.Summon => $"召唤{n}。", EffectKind.Forge => $"铸造{n}。", EffectKind.Channel => $"生成{n}个{Name(e.Orb!, true)}充能球。", EffectKind.Evoke => $"激发{target}{(e.Remove == false ? "，但不移除它" : "")}。", EffectKind.OrbPassive => $"触发{target}的被动效果。", EffectKind.OrbSlots => OrbSlots(n, true), EffectKind.Heal => $"{(e.Target?.Ref is null or "self" ? "" : target)}回复{quantity}生命。", EffectKind.LoseHp => $"{(e.Target?.Ref is null or "self" ? "" : target)}失去{quantity}生命。", _ => throw new InvalidOperationException()
        } : e.Kind switch
        {
            EffectKind.GainMaxHp => $"{target} gains {n} Max HP.", EffectKind.LoseMaxHp => $"{target} loses {n} Max HP.",
            EffectKind.GainGold => $"Gain {n} Gold.", EffectKind.LoseGold => $"Lose {n} Gold.",
            EffectKind.Damage => $"{(e.Actor == "osty" ? "Osty: " : "")}Deal {n} damage to {target}.", EffectKind.Block => e.Target?.Ref is null or "self" ? $"Gain {n} Block." : $"{target} gains {n} Block.", EffectKind.Draw => $"Draw {n} cards.", EffectKind.GainEnergy => $"Gain {Resource("energy", n, false, resource)}.", EffectKind.GainStars => $"Gain {Resource("stars", n, false, resource)}.", EffectKind.ApplyPower => $"Apply {n} {Name(e.Power!, false)} to {target}.", EffectKind.Discard => $"Discard {target}.", EffectKind.Exhaust => $"Exhaust {target}.", EffectKind.Move => $"Move {target} to {destination}.", EffectKind.Select => $"Select {target}.", EffectKind.Upgrade => $"Upgrade {target}.", EffectKind.Copy => $"Add {Quantity("count", e.Count)} copies of {target} to {destination}.", EffectKind.Transform => $"Transform {target} into {source}.", EffectKind.CreateCard => e.Card?.Pick == SelectionMode.Choose ? $"{source}; add it to {destination}. Repeat {Quantity("count", e.Count)} times." : $"Add {Quantity("count", e.Count)} {source} to {destination}.", EffectKind.Play => $"Auto-play {target}.", EffectKind.AddKeyword => $"Give {target} {e.Keyword}.", EffectKind.RemoveKeyword => $"Remove {e.Keyword} from {target}.", EffectKind.SetCost => $"Set {target}'s cost to {Resource("energy", n, false, resource)} for this {e.Until.ToString()!.ToLowerInvariant()}.", EffectKind.Summon => $"Summon {n}.", EffectKind.Forge => $"Forge {n}.", EffectKind.Channel => $"Channel {n} {Name(e.Orb!, false)} orbs.", EffectKind.Evoke => $"Evoke {target}{(e.Remove == false ? " without removing it" : "")}.", EffectKind.OrbPassive => $"Trigger {target}'s passive.", EffectKind.OrbSlots => OrbSlots(n, false), EffectKind.Heal => $"Heal {target} for {n} HP.", EffectKind.LoseHp => $"{target} loses {n} HP.", _ => throw new InvalidOperationException()
        };
        if (attackHits)
        {
            // Keep Fatal payoffs attached to each hit, outside the hit-count phrase.
            if (e.Target?.Ref == "random_enemy") text = text.Replace("对随机敌人", "随机对敌人", StringComparison.Ordinal);
            text = text.TrimEnd('。') + repeat + "次。";
        }
        if (e.Kind == EffectKind.ApplyPower && e.Amount?.Mul is { Length: 2 } factors
            && (factors[0].Value == -1 || factors[1].Value == -1))
        {
            var stat = factors[factors[0].Value == -1 ? 1 : 0];
            bool sameTarget = stat.Of == "target" || (stat.Of is null or "self" && e.Target?.Ref is null or "self") || (stat.Of == "osty" && e.Target?.Ref == "osty");
            if (stat.Stat == "power" && stat.Id == e.Power && sameTarget)
                text = chinese ? $"移除{target}的所有{Name(e.Power!, true)}。" : $"Remove all {Name(e.Power!, false)} from {target}.";
        }
        if (e.Until == LifetimeKind.Turn && e.Kind is EffectKind.ApplyPower or EffectKind.AddKeyword)
        {
            bool ownTurn = e.Kind == EffectKind.AddKeyword || e.Target?.Ref is null or "self" or "osty";
            if (chinese) text = text.TrimEnd('。') + "，";
            text += ownTurn ? chinese ? "持续至回合结束。" : " Until the end of your turn."
                : chinese ? "持续至目标的回合结束。" : " Until the end of the target's turn.";
        }
        if (e.OnFatal is { } fatal)
        {
            var payoff = RenderEffects(fatal, chinese, nested ?? (child => RenderEffect(child, chinese, resource: resource, cost: cost, conditionText: conditionText)), resource, cost, conditionText);
            text += chinese ? "\n斩杀时，" + string.Join(fatal.Any(e => e.Repeat is not null) ? "。" : "，", payoff.Select(t => t.TrimEnd('。'))) + "。" : " Fatal: " + string.Join(" ", payoff);
        }
        if (e.Condition is not null) text = (chinese ? "若" : "If ") + (conditionText?.Invoke(e.Condition) ?? Condition(e.Condition, chinese, resource, cost)) + (chinese ? "，" : ": ") + text;
        if (repeat is not null && !attackHits)
        {
            // A resolved preview must retain its count slot and upgrade highlighting.
            string? each = chinese && e.Condition is null && e.OnFatal is null && e.Until is null
                && repeat == Number(e.Repeat, true, resource: resource, cost: cost) ? RepeatEach(e.Repeat!) : null;
            text = each is not null ? each + (text.StartsWith("奥斯提", StringComparison.Ordinal) ? "，奥斯提就" + text[3..] : "，就" + text)
                : chinese ? $"重复{repeat}次：『{text.TrimEnd('。')}』。" : $"Repeat {repeat} times: “{text.TrimEnd('.')}”.";
        }
        return text;
    }
    private static string? RepeatEach(NumberExpression number) => number.Stat switch
    {
        "hand_size" => "手牌中每有一张牌",
        "draw_size" => "抽牌堆中每有一张牌",
        "discard_size" => "弃牌堆中每有一张牌",
        "exhaust_size" => "消耗堆中每有一张牌",
        "orb_count" => "当前每有一个充能球",
        "orb_capacity" => "每有一个充能球栏位",
        "power" when number.Of is null or "self" or "target" or "osty" => (number.Of switch { "target" => "目标", "osty" => "奥斯提", _ => "你" })
            + "每有一" + PowerUnit(number.Id!) + Name(number.Id!, true),
        _ => null
    };
    private static string PowerUnit(string power) => power is "strength" or "dexterity" or "focus" or "vigor" or "thorns" or "plating" ? "点" : "层";
    private static string ChineseQuantity(string amount, string unit = "点")
    {
        // Live previews and upgrade highlighting still render a concrete native number.
        string plain = System.Text.RegularExpressions.Regex.Replace(amount, @"\[[^\]]*\]", "");
        bool numeric = decimal.TryParse(plain, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _) || System.Text.RegularExpressions.Regex.IsMatch(plain, @"^\{[ECRT]\d+:diff\(\)\}$")
            || System.Text.RegularExpressions.Regex.IsMatch(plain, @"^[\dX.\s()+\-×÷]+$");
        return numeric ? amount + unit : "等量于" + amount + "的";
    }
    private static string OrbSlots(string amount, bool chinese)
    {
        string plain = System.Text.RegularExpressions.Regex.Replace(amount, @"\[[^\]]*\]", "");
        if (!int.TryParse(plain, out int value)) return chinese ? $"充能球栏位增加{amount}个（负数表示失去）。" : $"Change orb slots by {amount} (negative means lose).";
        string magnitude = value < 0 ? amount.Remove(amount.IndexOf('-'), 1) : amount;
        return chinese ? $"{(value < 0 ? "失去" : "获得")}{magnitude}个充能球栏位。" : $"{(value < 0 ? "Lose" : "Gain")} {magnitude} orb {(value is -1 or 1 ? "slot" : "slots")}.";
    }
    private static string Resource(string kind, string amount, bool chinese, Func<string, string, string>? resource) => resource?.Invoke(kind, amount)
        ?? (chinese ? amount + "点" + Name(kind, true) : amount + (kind == "energy" ? " Energy" : " Stars"));
    // Shared by card faces, power tooltips and offline audits. Dynamic expressions use one icon.
    public static string ResourceText(string kind, string amount, string energyPrefix)
    {
        int opening = amount.IndexOf(']'), closing = amount.LastIndexOf('[');
        string number = opening >= 0 && closing > opening ? amount[(opening + 1)..closing] : amount;
        _ = int.TryParse(number, out int count);
        string path = $"res://images/packed/sprite_fonts/{(kind == "stars" ? "star" : energyPrefix + "_energy")}_icon.png";
        return ResourceIcons(count, amount, path);
    }
    public static string ResourceIcons(int count, string highlightedAmount, string iconPath)
    {
        string icon = $"[img]{iconPath}[/img]";
        if (count is < 1 or >= 4) return highlightedAmount + icon;
        string icons = string.Concat(Enumerable.Repeat(icon, count));
        int end = highlightedAmount.IndexOf(']');
        return end >= 0 && highlightedAmount.StartsWith('[') ? highlightedAmount[..(end + 1)] + icons + highlightedAmount[highlightedAmount.LastIndexOf('[')..] : icons;
    }
}
