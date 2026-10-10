namespace Forge.Core;

// Descriptions are derived from the program; generated prose never drives execution.
public static class CardText
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["strength"]="力量", ["dexterity"]="敏捷", ["weak"]="虚弱", ["vulnerable"]="易伤", ["frail"]="脆弱",
        ["poison"]="中毒", ["doom"]="灾厄", ["focus"]="集中", ["vigor"]="活力", ["thorns"]="荆棘",
        ["plating"]="覆甲", ["intangible"]="无实体", ["artifact"]="人工制品", ["buffer"]="缓冲", ["retain_block"]="保留格挡",
        ["soul"]="灵魂", ["shiv"]="小刀", ["wound"]="伤口", ["dazed"]="晕眩", ["burn"]="灼伤", ["void"]="虚空", ["slimed"]="黏液", ["fuel"]="燃料", ["debris"]="碎屑", ["minion_strike"]="仆从打击", ["minion_sacrifice"]="仆从捐躯", ["minion_dive"]="仆从俯冲", ["sovereign_blade"]="君王之剑",
        ["lightning"]="闪电", ["frost"]="冰霜", ["dark"]="黑暗", ["plasma"]="等离子", ["glass"]="玻璃", ["random"]="随机",
        ["hp"]="生命", ["max_hp"]="最大生命", ["block"]="格挡", ["energy"]="能量", ["stars"]="星", ["hand_size"]="手牌数", ["draw_size"]="抽牌堆牌数", ["discard_size"]="弃牌堆牌数", ["exhaust_size"]="消耗堆牌数", ["orb_count"]="充能球数", ["orb_capacity"]="充能球栏位数", ["paid_energy"]="本次消耗能量", ["paid_stars"]="本次消耗星", ["event_amount"]="事件数值", ["damage_dealt"]="上次攻击实际伤害",
        ["attack"]="攻击", ["skill"]="技能", ["power"]="能力", ["status"]="状态", ["curse"]="诅咒",
        ["exhaust"]="消耗", ["ethereal"]="虚无", ["retain"]="保留", ["innate"]="固有", ["sly"]="奇巧"
    };
    public static string Name(string id, bool chinese) => chinese ? Names.GetValueOrDefault(id, id) : id.Replace('_', ' ');
    private static bool UsesX(NumberExpression? number, CardCost? cost) => number is not null
        && (number.Stat == "paid_energy" && cost?.EnergyX == true || number.Stat == "paid_stars" && cost?.StarsX == true
            || (number.Add ?? number.Sub ?? number.Mul ?? number.Div)?.Any(n => UsesX(n, cost)) == true);
    public static string Number(NumberExpression? number, bool chinese, int fallback = 1, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        if (number is null) return fallback.ToString();
        if (number.Value is int value) return value.ToString();
        if (number.Stat is { } stat)
        {
            if (UsesX(number, cost)) return "X";
            if (stat is "paid_energy" or "paid_stars")
                return (chinese ? "本次消耗的" : "the amount of ") + (resource?.Invoke(stat == "paid_energy" ? "energy" : "stars", "1") ?? Name(stat == "paid_energy" ? "energy" : "stars", chinese)) + (chinese ? "数量" : " spent");
            string subject = number.Of switch { "osty" => chinese ? "奥斯提的" : "Osty's ", "target" => chinese ? "目标的" : "target's ", _ => chinese ? "你的" : "your " };
            return subject + (stat is "energy" or "stars" && resource is not null ? resource(stat, "1") + (chinese ? "数量" : " amount")
                : Name(stat == "power" ? number.Id! : stat, chinese) + (stat == "power" && chinese ? PowerUnit(number.Id!) + "数" : ""));
        }
        // Keep legacy programs intact while displaying their subtraction idiom naturally.
        if (number.Add is { Length: 2 } sum && NegativeFactor(sum[1]) is { } subtrahend)
            return Number(new() { Sub = [sum[0], subtrahend] }, chinese, resource: resource, cost: cost);
        string op = number.Add is not null ? " + " : number.Sub is not null ? " - " : number.Mul is not null ? " × " : " ÷ ";
        return "(" + string.Join(op, (number.Add ?? number.Sub ?? number.Mul ?? number.Div!).Select(n => Number(n, chinese, resource: resource, cost: cost))) + ")" + (number.Div is not null ? chinese ? "向下取整" : " rounded down" : "");
    }
    private static NumberExpression? NegativeFactor(NumberExpression number) => number.Mul is { Length: 2 } factors
        ? factors[0].Value == -1 ? factors[1] : factors[1].Value == -1 ? factors[0] : null : null;
    public static string Condition(EffectCondition? condition, bool chinese, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        if (condition is null) return "";
        if (condition.Not is { } not) return (chinese ? "不满足 " : "not ") + Condition(not, chinese, resource, cost);
        if (condition.All is { } all) return string.Join(chinese ? "且" : " and ", all.Select(c => Condition(c, chinese, resource, cost)));
        if (condition.Any is { } any) return string.Join(chinese ? "或" : " or ", any.Select(c => Condition(c, chinese, resource, cost)));
        string op = condition.Op switch { Comparison.Eq => "=", Comparison.Ne => "≠", Comparison.Gt => ">", Comparison.Ge => "≥", Comparison.Lt => "<", _ => "≤" };
        string Operand(NumberExpression? number, NumberExpression? other)
        {
            string text = Number(number, chinese, resource: resource, cost: cost);
            string? kind = other?.Stat switch { "energy" or "paid_energy" => "energy", "stars" or "paid_stars" => "stars", _ => null };
            return number?.Value is not null && kind is not null ? Resource(kind, text, chinese, resource) : text;
        }
        return Operand(condition.Left, condition.Right) + " " + op + " " + Operand(condition.Right, condition.Left);
    }
    public static string Target(EffectTarget? target, bool chinese, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        if (target?.Ref is { } reference) return reference switch
        {
            "self" => chinese ? "你" : "you", "enemy" => chinese ? "目标敌人" : "the enemy", "random_enemy" => chinese ? "随机敌人" : "a random enemy", "all_enemies" => chinese ? "所有敌人" : "ALL enemies", "osty" => chinese ? "奥斯提" : "Osty", "this_card" => chinese ? "本牌" : "this card", "event.card" => chinese ? "该牌" : "that card", "event.target" => chinese ? "事件目标" : "the event target", "first_orb" => chinese ? "最前方充能球" : "the first orb", "last_orb" => chinese ? "最后方充能球" : "the last orb", "all_orbs" => chinese ? "所有充能球" : "all orbs", _ => chinese ? "选中的牌" : "the selected cards"
        };
        if (target?.Pile is not { } pile) return chinese ? "你" : "you";
        string zone = Pile(pile, chinese);
        string selection = target.Pick switch { SelectionMode.All => chinese ? "所有" : "all ", SelectionMode.Choose => chinese ? "" : "chosen ", SelectionMode.Random => chinese ? "随机" : "random ", SelectionMode.First => chinese ? "最前方" : "first ", _ => chinese ? "最后方" : "last " };
        string count = target.Pick == SelectionMode.All ? "" : Number(target.Count, chinese, resource: resource, cost: cost) + (chinese ? "张" : " ");
        return (chinese ? "你的" + zone + "中的" : "from " + zone + ": ") + selection + (target.UpTo == true ? chinese ? "至多" : "up to " : "") + count + Filter(target.Filter, chinese, resource) + (chinese ? target.Filter?.Id is null ? "牌" : "" : "cards");
    }
    private static string Filter(CardFilter? filter, bool chinese, Func<string, string, string>? resource = null, string? pool = null)
    {
        if (filter is null) return pool ?? "";
        return (filter.Cost is int cost ? chinese ? $"耗能为{Resource("energy", cost.ToString(), true, resource)}的" : $"{Resource("energy", cost.ToString(), false, resource)}-cost " : "")
            + (filter.Keyword is { } keyword ? Name(keyword.ToString().ToLowerInvariant(), chinese) + (chinese ? "的" : " ") : "")
            + (filter.Upgraded is bool upgraded ? chinese ? upgraded ? "已升级" : "未升级" : upgraded ? "upgraded " : "unupgraded " : "")
            + (filter.Rarity is { } rarity ? chinese ? rarity switch { ForgeRarity.Common => "普通", ForgeRarity.Uncommon => "罕见", _ => "稀有" } : rarity.ToString().ToLowerInvariant() + " " : "")
            + (pool ?? "")
            + (filter.Type is { } type ? Name(type, chinese) + (chinese ? "" : " ") : "")
            + (filter.Id is { } id ? Name(id, chinese) + (chinese ? "" : " ") : "");
    }
    public static string Pile(CardPileName pile, bool chinese) => chinese ? pile switch { CardPileName.Hand => "手牌", CardPileName.Draw => "抽牌堆", CardPileName.Discard => "弃牌堆", _ => "消耗堆" } : pile.ToString().ToLowerInvariant();
    public static string Render(CardDefinition card, bool chinese, Func<int, string>? amount = null, bool upgraded = false, Func<string, string, string>? resource = null) => Render(card.Form(upgraded), chinese, amount, card.Type, resource);
    public static string Render(CardForm form, bool chinese, Func<int, string>? amount = null, ForgeCardType? cardType = null, Func<string, string, string>? resource = null)
    {
        int index = 0;
        string Action(CardEffect effect) => RenderEffect(effect, chinese, amount?.Invoke(index++), resource, form.Cost);
        var lines = RenderEffects(form.Immediate, chinese, Action, resource, form.Cost).ToList();
        foreach (var rule in form.Listeners) lines.Add(RenderRule(rule, chinese, Action, cardType == ForgeCardType.Power, resource, form.Cost));
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
        Func<string, string, string>? resource, CardCost? cost)
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
                yield return (chinese ? "若" : "If ") + Condition(effect.Condition, chinese, resource, cost) + (chinese ? "，" : ": ")
                    + (chinese ? string.Join("，", texts.Select(t => t.TrimEnd('。'))) + "。" : string.Join(" ", texts));
            }
            i = end;
        }
    }
    public static string RenderRule(CardRule rule, bool chinese, Func<CardEffect, string>? render = null, bool combatIsImplicit = false, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        string when = rule.Trigger.Event switch
        {
            RuleEvent.TurnStart => chinese ? "在你的回合开始时" : "At the start of your turn", RuleEvent.TurnEnd => chinese ? "在你的回合结束时" : "At the end of your turn",
            RuleEvent.CardPlayed => chinese ? "打出" : "play", RuleEvent.CardDrawn => chinese ? "抽到" : "draw", RuleEvent.CardDiscarded => chinese ? "丢弃" : "discard", RuleEvent.CardExhausted => chinese ? "消耗" : "exhaust", RuleEvent.CardGenerated => chinese ? "生成" : "create",
            RuleEvent.DamageReceived => chinese ? "受到伤害后" : "After receiving damage", RuleEvent.AttackCompleted => chinese ? "攻击结算后" : "After an attack", RuleEvent.Summoned => chinese ? "召唤后" : "After Summon", RuleEvent.OrbChanneled => chinese ? "生成充能球后" : "After channeling", _ => chinese ? "激发充能球后" : "After evoking"
        };
        string verb = when;
        string card = Filter(rule.Trigger.Filter, chinese, resource) + (chinese ? rule.Trigger.Filter?.Id is null ? "牌" : "" : "card");
        if (rule.Trigger.Event is >= RuleEvent.CardPlayed and <= RuleEvent.CardGenerated)
            when = (chinese ? "每当你" : "Whenever you ") + when + (chinese ? "一张" : " a ") + card;
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
            when = chinese ? o.Every is int every ? $"{scope}，每{verb}{every}张{card}时"
                : scope + (o.First is int first ? first == 1 ? "首次" : $"前{first}次" : $"第{o.Nth}次") + verb + "一张" + card + "时"
                : scope + (o.First is int firstEnglish ? $"the first {firstEnglish} times you " : o.Nth is int nth ? $"the {nth}th time you " : $"every {o.Every} times you ") + verb + " a " + card;
        }
        string quota = rule.Trigger.Limit is { } limit ? chinese ? $"（{(limit.Within == CounterScope.Turn ? "每回合" : "本场战斗")}最多生效{limit.Count}次）" : $" (at most {limit.Count} activations per {limit.Within.ToString().ToLowerInvariant()})" : "";
        string condition = rule.Condition is null ? "" : (chinese ? "若" : "if ") + Condition(rule.Condition, chinese, resource, cost) + (chinese ? "，" : ": ");
        var effects = RenderEffects(rule.Effects, chinese, render ?? (e => RenderEffect(e, chinese, resource: resource, cost: cost)), resource, cost);
        return life + when + quota + (chinese ? "，" : ", ") + condition
            + (chinese ? string.Join("，", effects.Select(t => t.TrimEnd('。'))) + "。" : string.Join(" ", effects));
    }
    public static string RenderEffect(CardEffect e, bool chinese, string? amount = null, Func<string, string, string>? resource = null, CardCost? cost = null)
    {
        string n = UsesX(e.Amount, cost) ? Number(e.Amount, chinese, resource: resource, cost: cost) : amount ?? Number(e.Amount, chinese, resource: resource, cost: cost);
        string target = Target(e.Target, chinese, resource, cost);
        string source = e.Card?.Id is { } id ? Name(id, chinese) : Filter(e.Card?.Filter, chinese, resource,
            e.Card?.Pool == "colorless" ? chinese ? "无色" : "Colorless " : chinese ? "本角色" : "character ");
        if (e.Card?.Pool is not null) source += chinese ? "牌" : "cards";
        if (e.Card?.Pick == SelectionMode.Random) source = (chinese ? "随机" : "random ") + source;
        if (e.Card?.Upgraded == true) source += "+";
        if (e.Card?.Pick == SelectionMode.Choose) source = chinese ? $"从{e.Card.Options ?? 3}张随机{source}中选择1张" : $"choose one of {e.Card.Options ?? 3} random {source}";
        string destination = e.To is { } to ? (chinese ? "你的" : "your ") + Pile(to, chinese) + (to == CardPileName.Draw ? e.Position switch { "bottom" => chinese ? "底部" : " bottom", "random" => chinese ? "随机位置" : " randomly", _ => chinese ? "顶部" : " top" } : "") : "";
        string text = chinese ? e.Kind switch
        {
            EffectKind.Damage => $"{(e.Actor == "osty" ? "奥斯提" : "")}{(e.Target?.Ref == "enemy" ? "" : "对" + target)}造成{n}点伤害。", EffectKind.Block => $"{(e.Target?.Ref is null or "self" ? "" : target)}获得{n}点格挡。", EffectKind.Draw => $"抽{n}张牌。", EffectKind.GainEnergy => $"获得{Resource("energy", n, true, resource)}。", EffectKind.GainStars => $"获得{Resource("stars", n, true, resource)}。", EffectKind.ApplyPower => e.Target?.Ref is null or "self" ? $"获得{n}{PowerUnit(e.Power!)}{Name(e.Power!, true)}。" : $"对{target}施加{n}{PowerUnit(e.Power!)}{Name(e.Power!, true)}。",
            EffectKind.Discard => $"丢弃{target}。", EffectKind.Exhaust => $"消耗{target}。", EffectKind.Move => $"将{target}移至{destination}。", EffectKind.Select => $"选择{target}。", EffectKind.Upgrade => $"升级{target}。", EffectKind.Copy => $"将{target}的{Number(e.Count, true, resource: resource, cost: cost)}张复制品添加到{destination}。", EffectKind.Transform => e.Card?.Pick == SelectionMode.Choose ? $"{source}，将{target}变化为所选的牌。" : $"将{target}变化为{source}。", EffectKind.CreateCard => e.Card?.Pick == SelectionMode.Choose ? (e.Count is null || e.Count.Value == 1 ? "" : $"重复以下操作{Number(e.Count, true, resource: resource, cost: cost)}次：") + $"{source}，添加到{destination}。" : $"将{Number(e.Count, true, resource: resource, cost: cost)}张{source}添加到{destination}。", EffectKind.Play => $"自动打出{target}。", EffectKind.AddKeyword => $"{target}获得{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.RemoveKeyword => $"移除{target}的{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.SetCost => $"{target}在{(e.Until == LifetimeKind.Turn ? "本回合" : "本场战斗")}的耗能变为{Resource("energy", n, true, resource)}。",
            EffectKind.Summon => $"召唤{n}。", EffectKind.Forge => $"铸造{n}。", EffectKind.Channel => $"生成{n}个{Name(e.Orb!, true)}充能球。", EffectKind.Evoke => $"激发{target}{(e.Remove == false ? "，但不移除它" : "")}。", EffectKind.OrbPassive => $"触发{target}的被动效果。", EffectKind.OrbSlots => OrbSlots(n, true), EffectKind.Heal => $"{(e.Target?.Ref is null or "self" ? "" : target)}回复{n}点生命。", EffectKind.LoseHp => $"{(e.Target?.Ref is null or "self" ? "" : target)}失去{n}点生命。", _ => throw new InvalidOperationException()
        } : e.Kind switch
        {
            EffectKind.Damage => $"{(e.Actor == "osty" ? "Osty: " : "")}Deal {n} damage to {target}.", EffectKind.Block => e.Target?.Ref is null or "self" ? $"Gain {n} Block." : $"{target} gains {n} Block.", EffectKind.Draw => $"Draw {n} cards.", EffectKind.GainEnergy => $"Gain {Resource("energy", n, false, resource)}.", EffectKind.GainStars => $"Gain {Resource("stars", n, false, resource)}.", EffectKind.ApplyPower => $"Apply {n} {Name(e.Power!, false)} to {target}.", EffectKind.Discard => $"Discard {target}.", EffectKind.Exhaust => $"Exhaust {target}.", EffectKind.Move => $"Move {target} to {destination}.", EffectKind.Select => $"Select {target}.", EffectKind.Upgrade => $"Upgrade {target}.", EffectKind.Copy => $"Add {Number(e.Count, false, resource: resource, cost: cost)} copies of {target} to {destination}.", EffectKind.Transform => $"Transform {target} into {source}.", EffectKind.CreateCard => e.Card?.Pick == SelectionMode.Choose ? $"{source}; add it to {destination}. Repeat {Number(e.Count, false, resource: resource, cost: cost)} times." : $"Add {Number(e.Count, false, resource: resource, cost: cost)} {source} to {destination}.", EffectKind.Play => $"Auto-play {target}.", EffectKind.AddKeyword => $"Give {target} {e.Keyword}.", EffectKind.RemoveKeyword => $"Remove {e.Keyword} from {target}.", EffectKind.SetCost => $"Set {target}'s cost to {Resource("energy", n, false, resource)} for this {e.Until.ToString()!.ToLowerInvariant()}.", EffectKind.Summon => $"Summon {n}.", EffectKind.Forge => $"Forge {n}.", EffectKind.Channel => $"Channel {n} {Name(e.Orb!, false)} orbs.", EffectKind.Evoke => $"Evoke {target}{(e.Remove == false ? " without removing it" : "")}.", EffectKind.OrbPassive => $"Trigger {target}'s passive.", EffectKind.OrbSlots => OrbSlots(n, false), EffectKind.Heal => $"Heal {target} for {n} HP.", EffectKind.LoseHp => $"{target} loses {n} HP.", _ => throw new InvalidOperationException()
        };
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
            text += ownTurn ? chinese ? "持续至回合结束。" : " Until the end of your turn."
                : chinese ? "持续至目标的回合结束。" : " Until the end of the target's turn.";
        }
        if (e.Condition is not null) text = (chinese ? "若" : "If ") + Condition(e.Condition, chinese, resource, cost) + (chinese ? "，" : ": ") + text;
        if (e.Repeat is not null) text = chinese ? $"重复以下效果{Number(e.Repeat, true, resource: resource, cost: cost)}次：" + text : $"Repeat the following {Number(e.Repeat, false, resource: resource, cost: cost)} times: " + text;
        return text;
    }
    private static string PowerUnit(string power) => power is "strength" or "dexterity" or "focus" or "vigor" or "thorns" or "plating" ? "点" : "层";
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
