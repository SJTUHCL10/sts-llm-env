using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Forge.Core;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Forge.NativeAudit;

internal sealed record NativeAuditEntry(string Id, string ClassName, NativeCardSnapshot? Source, NativeConversion Conversion);

internal static partial class NativeAuditReport
{
    internal static void Write(string directory, string language, NativeAuditEntry[] entries)
    {
        Directory.CreateDirectory(directory);
        var json = new JsonSerializerOptions(Wire.Json) { WriteIndented = true };
        void Save(string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, json), new UTF8Encoding(false));
        Save("audit.json", new { protocol_version = MechanicCatalog.ProtocolVersion, game_version = NativeCardConversion.GameVersion,
            assembly_sha256 = NativeCardConversion.AssemblySha256, language, entries });
        Save("cards.json", new CardBatch { Cards = entries.Where(e => e.Conversion.Status == NativeConversionStatus.EffectsMapped).Select(e => e.Conversion.Definition!).ToArray() });
        Save("partial-cards.json", new CardBatch { Cards = entries.Where(e => e.Conversion.Status == NativeConversionStatus.Partial).Select(e => e.Conversion.Definition!).ToArray() });
        var text = new StringBuilder($"# 原生卡 → 协议 JSON → 文本审计\n\n游戏 {NativeCardConversion.GameVersion}，协议 v{MechanicCatalog.ProtocolVersion}，语言 {language}。\n\n");
        text.AppendLine("原文由安装游戏 PCK 的本地化和原生格式化器生成，基础／升级数值来自实际模型。映射依据效果代码，未从描述猜测。\n");
        text.AppendLine("effects_mapped 表示已核对的效果与协议动作匹配；不是整个游戏中完全等价的证明。生成牌仍有自己的 ID、Token 卡池和视觉，未复制原生身份、专属识别、附魔及运行时改造。partial 明确存在语义／元数据差异；unsupported 有已确认缺口；unreviewed 尚待核对，不能算作协议不支持。\n");
        text.AppendLine("cards.json 只收录 effects_mapped；partial-cards.json 是有差异的候选定义，供文本检查；audit.json 保存 ID、原生快照、定义、分类及原因。文本对照采用牌库状态，包含关键词；仅去除样式和替换资源图标，保留词序及标点差异。文本相同也不证明效果等价。\n");
        text.AppendLine("| 分类 | 模型数 |\n| --- | ---: |");
        foreach (var group in entries.GroupBy(e => e.Conversion.Status)) text.AppendLine($"| {Status(group.Key)} | {group.Count()} |");
        text.AppendLine();
        foreach (var entry in entries.Where(e => e.Conversion.Definition is not null))
        {
            var source = entry.Source!;
            var card = entry.Conversion.Definition!;
            text.AppendLine($"## {Escape(source.Name)} · {entry.Id}\n\n{Status(entry.Conversion.Status)} · {source.Pool} · {source.Type}/{source.Rarity}\n");
            foreach (string reason in entry.Conversion.Reasons) text.AppendLine($"- {Escape(reason)}");
            text.AppendLine("\n| 形态 | 原生费用 | 原生文本 | 协议文本 | 样式归一后相同 |\n| --- | --- | --- | --- | --- |");
            for (int i = 0; i < 2; i++)
            {
                string original = source.Forms[i].Description is string description ? Plain(description) : "[文本不可用：" + source.Forms[i].TextError + "]";
                string rendered = Plain(CardText.Render(card, language == "zhs", upgraded: i == 1, resource: Icons));
                // The mod delegates card-keyword display to the game; mirror its insertion order.
                var keywords = card.Forms[i].Tags.Select(k => Enum.Parse<CardKeyword>(k.ToString())).ToHashSet();
                var lines = CardKeywordOrder.beforeDescription.Reverse().Where(keywords.Contains).Select(KeywordText)
                    .Concat([rendered]).Concat(CardKeywordOrder.afterDescription.Where(keywords.Contains).Select(KeywordText));
                rendered = string.Join("\n", lines);
                bool same = source.Forms[i].TextError is null && Normalize(original) == Normalize(rendered);
                text.AppendLine($"| {(i == 0 ? "基础" : source.Upgradable ? "升级" : "不可升级（重复基础）")} | {Cost(source.Forms[i].Cost)} | {Escape(original)} | {Escape(rendered)} | {(same ? "是" : "否／需人工核对")} |");
            }
            text.AppendLine("\n<details><summary>协议 JSON</summary>\n\n```json");
            text.AppendLine(JsonSerializer.Serialize(card, json));
            text.AppendLine("```\n\n</details>\n");
        }
        text.AppendLine("## 未输出定义的模型\n\n| ID | 类名 | 分类 | 原因 |\n| --- | --- | --- | --- |");
        foreach (var entry in entries.Where(e => e.Conversion.Definition is null))
            text.AppendLine($"| {entry.Id} | {entry.ClassName} | {Status(entry.Conversion.Status)} | {Escape(string.Join("；", entry.Conversion.Reasons))} |");
        File.WriteAllText(Path.Combine(directory, "report.md"), text.ToString(), new UTF8Encoding(false));
    }

    private static string Status(NativeConversionStatus status) => JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());
    private static string KeywordText(CardKeyword keyword) => new MegaCrit.Sts2.Core.Localization.LocString("card_keywords", keyword.ToString().ToUpperInvariant() + ".title").GetFormattedText()
        + new MegaCrit.Sts2.Core.Localization.LocString("card_keywords", "PERIOD").GetRawText();
    private static string Icons(string kind, string amount) => CardText.ResourceText(kind, amount, "character")
        .Replace("[img]res://images/packed/sprite_fonts/character_energy_icon.png[/img]", "⚡")
        .Replace("[img]res://images/packed/sprite_fonts/star_icon.png[/img]", "⭐");
    private static string Cost(CardCost cost) => (cost.EnergyX == true ? "X" : cost.Energy.ToString()) + "⚡"
        + (cost.StarsX == true ? " / X⭐" : cost.Stars is int stars ? $" / {stars}⭐" : "");
    private static string Plain(string value) => Style().Replace(Image().Replace(value, m => m.Value.Contains("star_icon") ? "⭐" : m.Value.Contains("energy_icon") ? "⚡" : m.Value), "");
    private static string Normalize(string value) => Whitespace().Replace(value, "");
    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("|", "&#124;").Replace("\r", "").Replace("\n", "<br>");
    [GeneratedRegex(@"\[img\].*?\[/img\]")] private static partial Regex Image();
    [GeneratedRegex(@"\[/?(?:b|i|u|red|green|blue|purple|gold|gray|color(?:=[^\]]+)?)\]")] private static partial Regex Style();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
