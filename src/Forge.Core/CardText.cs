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
        ["hp"]="生命", ["max_hp"]="最大生命", ["block"]="格挡", ["energy"]="能量", ["stars"]="星", ["hand_size"]="手牌数", ["draw_size"]="抽牌堆牌数", ["discard_size"]="弃牌堆牌数", ["exhaust_size"]="消耗堆牌数", ["orb_count"]="充能球数", ["orb_capacity"]="球槽数", ["paid_energy"]="本次消耗能量", ["paid_stars"]="本次消耗星", ["event_amount"]="事件数值", ["damage_dealt"]="上次攻击实际伤害",
        ["attack"]="攻击", ["skill"]="技能", ["power"]="能力", ["status"]="状态", ["curse"]="诅咒",
        ["exhaust"]="消耗", ["ethereal"]="虚无", ["retain"]="保留", ["innate"]="固有", ["sly"]="奇巧"
    };
    public static string Name(string id, bool chinese) => chinese ? Names.GetValueOrDefault(id, id) : id.Replace('_', ' ');
    public static string Number(NumberExpression? number, bool chinese, int fallback = 1)
    {
        if (number is null) return fallback.ToString();
        if (number.Value is int value) return value.ToString();
        if (number.Stat is { } stat)
        {
            string subject = number.Of switch { "osty" => chinese ? "奥斯提的" : "Osty's ", "target" => chinese ? "目标的" : "target's ", _ => chinese ? "你的" : "your " };
            return subject + Name(stat == "power" ? number.Id! : stat, chinese);
        }
        string op = number.Add is not null ? " + " : number.Mul is not null ? " × " : " ÷ ";
        return "(" + string.Join(op, (number.Add ?? number.Mul ?? number.Div!).Select(n => Number(n, chinese))) + ")" + (number.Div is not null ? chinese ? "向下取整" : " rounded down" : "");
    }
    public static string Condition(EffectCondition? condition, bool chinese)
    {
        if (condition is null) return "";
        if (condition.Not is { } not) return (chinese ? "不满足 " : "not ") + Condition(not, chinese);
        if (condition.All is { } all) return string.Join(chinese ? "且" : " and ", all.Select(c => Condition(c, chinese)));
        if (condition.Any is { } any) return string.Join(chinese ? "或" : " or ", any.Select(c => Condition(c, chinese)));
        string op = condition.Op switch { Comparison.Eq => "=", Comparison.Ne => "≠", Comparison.Gt => ">", Comparison.Ge => "≥", Comparison.Lt => "<", _ => "≤" };
        return Number(condition.Left, chinese) + " " + op + " " + Number(condition.Right, chinese);
    }
    public static string Target(EffectTarget? target, bool chinese)
    {
        if (target?.Ref is { } reference) return reference switch
        {
            "self" => chinese ? "你" : "you", "enemy" => chinese ? "目标敌人" : "the enemy", "random_enemy" => chinese ? "随机敌人" : "a random enemy", "all_enemies" => chinese ? "所有敌人" : "ALL enemies", "osty" => chinese ? "奥斯提" : "Osty", "this_card" => chinese ? "本牌" : "this card", "event.card" => chinese ? "触发事件的牌" : "the event card", "event.target" => chinese ? "事件目标" : "the event target", "first_orb" => chinese ? "最前方充能球" : "the first orb", "last_orb" => chinese ? "最后方充能球" : "the last orb", "all_orbs" => chinese ? "所有充能球" : "all orbs", _ => chinese ? "选中的牌" : "the selected cards"
        };
        if (target?.Pile is not { } pile) return chinese ? "你" : "you";
        string zone = Pile(pile, chinese);
        string selection = target.Pick switch { SelectionMode.All => chinese ? "所有" : "all ", SelectionMode.Choose => chinese ? "选择" : "choose ", SelectionMode.Random => chinese ? "随机" : "random ", SelectionMode.First => chinese ? "最前方" : "first ", _ => chinese ? "最后方" : "last " };
        string count = target.Pick == SelectionMode.All ? "" : Number(target.Count, chinese) + (chinese ? "张" : " ");
        return (chinese ? zone + "中的" : "from " + zone + ": ") + selection + (target.UpTo == true ? chinese ? "至多" : "up to " : "") + count + Filter(target.Filter, chinese) + (chinese ? "牌" : "cards");
    }
    private static string Filter(CardFilter? filter, bool chinese)
    {
        if (filter is null) return "";
        return (filter.Id is { } id ? Name(id, chinese) + " " : "") + (filter.Type is { } type ? Name(type, chinese) + " " : "")
            + (filter.Cost is int cost ? chinese ? $"{cost}费" : $"{cost}-cost " : "")
            + (filter.Keyword is { } keyword ? Name(keyword.ToString().ToLowerInvariant(), chinese) + " " : "")
            + (filter.Upgraded is bool upgraded ? chinese ? upgraded ? "已升级" : "未升级" : upgraded ? "upgraded " : "unupgraded " : "")
            + (filter.Rarity is { } rarity ? chinese ? rarity switch { ForgeRarity.Common => "普通", ForgeRarity.Uncommon => "罕见", _ => "稀有" } : rarity.ToString().ToLowerInvariant() + " " : "");
    }
    public static string Pile(CardPileName pile, bool chinese) => chinese ? pile switch { CardPileName.Hand => "手牌", CardPileName.Draw => "抽牌堆", CardPileName.Discard => "弃牌堆", _ => "消耗堆" } : pile.ToString().ToLowerInvariant();
    public static string Render(CardDefinition card, bool chinese, Func<int, string>? amount = null, bool upgraded = false) => Render(card.Form(upgraded), chinese, amount);
    public static string Render(CardForm form, bool chinese, Func<int, string>? amount = null)
    {
        int index = 0;
        string Action(CardEffect effect) => RenderEffect(effect, chinese, amount?.Invoke(index++));
        var lines = form.Immediate.Select(Action).ToList();
        foreach (var rule in form.Listeners) lines.Add(RenderRule(rule, chinese, Action));
        return string.Join("\n", lines);
    }
    public static string RenderRule(CardRule rule, bool chinese, Func<CardEffect, string>? render = null)
    {
        string when = rule.Trigger.Event switch
        {
            RuleEvent.TurnStart => chinese ? "回合开始时" : "At turn start", RuleEvent.TurnEnd => chinese ? "回合结束时" : "At turn end",
            RuleEvent.CardPlayed => chinese ? "打出" : "After playing ", RuleEvent.CardDrawn => chinese ? "抽到" : "After drawing ", RuleEvent.CardDiscarded => chinese ? "丢弃" : "After discarding ", RuleEvent.CardExhausted => chinese ? "消耗" : "After exhausting ", RuleEvent.CardGenerated => chinese ? "生成" : "After creating ",
            RuleEvent.DamageReceived => chinese ? "受到伤害后" : "After receiving damage", RuleEvent.AttackCompleted => chinese ? "攻击结算后" : "After an attack", RuleEvent.Summoned => chinese ? "召唤后" : "After Summon", RuleEvent.OrbChanneled => chinese ? "生成充能球后" : "After channeling", _ => chinese ? "激发充能球后" : "After evoking"
        };
        if (rule.Trigger.Event is >= RuleEvent.CardPlayed and <= RuleEvent.CardGenerated) when += Filter(rule.Trigger.Filter, chinese) + (chinese ? "牌后" : "a card");
        string life = rule.Lifetime switch { LifetimeKind.Combat => chinese ? "本场战斗，" : "For this combat, ", LifetimeKind.NextTurn => chinese ? "下个回合，" : "Next turn, ", _ => rule.Trigger.Event == RuleEvent.TurnStart ? chinese ? $"接下来 {rule.Turns ?? 1} 个回合开始时，" : $"For the next {rule.Turns ?? 1} turn starts, " : chinese ? $"本回合起 {rule.Turns ?? 1} 回合，" : $"For {rule.Turns ?? 1} turns including this one, " };
        string occurrence = "";
        if (rule.Trigger.Occurrence is { } o)
        {
            string scope = o.Within == CounterScope.Turn ? chinese ? "每回合" : "each turn: " : chinese ? "本场战斗" : "this combat: ";
            occurrence = scope + (o.First is int first ? chinese ? $"前 {first} 次" : $"first {first} times " : o.Nth is int nth ? chinese ? $"第 {nth} 次" : $"the {nth}th time " : chinese ? $"每 {o.Every} 次" : $"every {o.Every} times ");
        }
        string quota = rule.Trigger.Limit is { } limit ? chinese ? $"（{(limit.Within == CounterScope.Turn ? "每回合" : "本场战斗")}最多生效 {limit.Count} 次）" : $" (at most {limit.Count} activations per {limit.Within.ToString().ToLowerInvariant()})" : "";
        string condition = rule.Condition is null ? "" : (chinese ? "若 " : "if ") + Condition(rule.Condition, chinese) + (chinese ? "：" : ": ");
        return life + occurrence + when + quota + (chinese ? "：" : ": ") + condition + string.Join(" ", rule.Effects.Select(render ?? (e => RenderEffect(e, chinese))));
    }
    public static string RenderEffect(CardEffect e, bool chinese, string? amount = null)
    {
        string n = amount ?? Number(e.Amount, chinese), target = Target(e.Target, chinese);
        string source = e.Card?.Id is { } id ? Name(id, chinese) : e.Card?.Pool == "colorless" ? chinese ? "无色" : "Colorless" : chinese ? "本角色" : "character";
        source += Filter(e.Card?.Filter, chinese);
        if (e.Card?.Pool is not null) source += chinese ? "牌" : " cards";
        if (e.Card?.Pick == SelectionMode.Random) source = (chinese ? "随机" : "random ") + source;
        if (e.Card?.Upgraded == true) source += "+";
        if (e.Card?.Pick == SelectionMode.Choose) source = chinese ? $"从 {e.Card.Options ?? 3} 张{source}中选择一张" : $"choose one of {e.Card.Options ?? 3} {source}";
        string destination = e.To is { } to ? Pile(to, chinese) + (to == CardPileName.Draw ? e.Position switch { "bottom" => chinese ? "底部" : " bottom", "random" => chinese ? "随机位置" : " randomly", _ => chinese ? "顶部" : " top" } : "") : "";
        string text = chinese ? e.Kind switch
        {
            EffectKind.Damage => $"{(e.Actor == "osty" ? "奥斯提" : "")}对{target}造成 {n} 点伤害。", EffectKind.Block => $"{target}获得 {n} 点格挡。", EffectKind.Draw => $"抽 {n} 张牌。", EffectKind.GainEnergy => $"获得 {n} 点能量。", EffectKind.GainStars => $"获得 {n} 点星。", EffectKind.ApplyPower => $"给予{target} {n} 层{Name(e.Power!, true)}。",
            EffectKind.Discard => $"丢弃{target}。", EffectKind.Exhaust => $"消耗{target}。", EffectKind.Move => $"将{target}移至{destination}。", EffectKind.Select => $"{target}。", EffectKind.Upgrade => $"升级{target}。", EffectKind.Copy => $"复制{target} {Number(e.Count, true)} 次，加入{destination}。", EffectKind.Transform => $"将{target}变化为{source}。", EffectKind.CreateCard => $"生成 {Number(e.Count, true)} 张{source}，加入{destination}。", EffectKind.Play => $"自动打出{target}。", EffectKind.AddKeyword => $"{target}获得{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.RemoveKeyword => $"移除{target}的{Name(e.Keyword!.Value.ToString().ToLowerInvariant(), true)}。", EffectKind.SetCost => $"{target}耗能变为 {n}（{(e.Until == LifetimeKind.Turn ? "本回合" : "本场战斗")}）。",
            EffectKind.Summon => $"召唤 {n}。", EffectKind.Forge => $"铸造 {n}。", EffectKind.Channel => $"生成 {n} 个{Name(e.Orb!, true)}充能球。", EffectKind.Evoke => $"激发{target}，{(e.Remove == false ? "保留球" : "移除球")}。", EffectKind.OrbPassive => $"触发{target}的被动。", EffectKind.OrbSlots => $"改变 {n} 个充能球槽位。", EffectKind.Heal => $"{target}回复 {n} 点生命。", EffectKind.LoseHp => $"{target}失去 {n} 点生命。", _ => throw new InvalidOperationException()
        } : e.Kind switch
        {
            EffectKind.Damage => $"{(e.Actor == "osty" ? "Osty: " : "")}Deal {n} damage to {target}.", EffectKind.Block => $"{target} gains {n} Block.", EffectKind.Draw => $"Draw {n} cards.", EffectKind.GainEnergy => $"Gain {n} Energy.", EffectKind.GainStars => $"Gain {n} Stars.", EffectKind.ApplyPower => $"Apply {n} {Name(e.Power!, false)} to {target}.", EffectKind.Discard => $"Discard {target}.", EffectKind.Exhaust => $"Exhaust {target}.", EffectKind.Move => $"Move {target} to {destination}.", EffectKind.Select => $"Select {target}.", EffectKind.Upgrade => $"Upgrade {target}.", EffectKind.Copy => $"Copy {target} {Number(e.Count, false)} times into {destination}.", EffectKind.Transform => $"Transform {target} into {source}.", EffectKind.CreateCard => $"Create {Number(e.Count, false)} {source} in {destination}.", EffectKind.Play => $"Auto-play {target}.", EffectKind.AddKeyword => $"Give {target} {e.Keyword}.", EffectKind.RemoveKeyword => $"Remove {e.Keyword} from {target}.", EffectKind.SetCost => $"Set {target}'s cost to {n} for this {e.Until.ToString()!.ToLowerInvariant()}.", EffectKind.Summon => $"Summon {n}.", EffectKind.Forge => $"Forge {n}.", EffectKind.Channel => $"Channel {n} {Name(e.Orb!, false)} orbs.", EffectKind.Evoke => $"Evoke {target}{(e.Remove == false ? " without removing it" : "")}.", EffectKind.OrbPassive => $"Trigger {target}'s passive.", EffectKind.OrbSlots => $"Change orb slots by {n}.", EffectKind.Heal => $"Heal {target} for {n} HP.", EffectKind.LoseHp => $"{target} loses {n} HP.", _ => throw new InvalidOperationException()
        };
        if (e.Until == LifetimeKind.Turn && e.Kind is EffectKind.ApplyPower or EffectKind.AddKeyword) text += chinese ? " 持续至目标回合结束。" : " Until the end of the target's turn.";
        if (e.Condition is not null) text = (chinese ? "若 " : "If ") + Condition(e.Condition, chinese) + ": " + text;
        if (e.Repeat is not null) text += chinese ? $" 重复 {Number(e.Repeat, true)} 次。" : $" Repeat {Number(e.Repeat, false)} times.";
        return text;
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
