using System.Text;
using System.Text.Json;
using Forge.Core;

namespace Forge.Tool;

internal static class CardTextAudit
{
    // Only definition objects are exported. Prompts, reasoning and configuration stay private.
    internal static int Write(string input, string output)
    {
        string[] files = Directory.Exists(input) ? Directory.GetFiles(input, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".json" or ".jsonl").Order().ToArray() : [input];
        var cards = new Dictionary<string, CardDefinition>();
        int rejected = 0;
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("forms", out _) && node.TryGetProperty("name", out _) && node.TryGetProperty("type", out _))
                {
                    try
                    {
                        var card = CardValidator.Validate(Wire.Decode<CardDefinition>(node.GetRawText()));
                        cards.TryAdd(Wire.Encode(card), card);
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException) { rejected++; }
                    return;
                }
                foreach (var property in node.EnumerateObject()) Visit(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Visit(item);
        }
        foreach (string file in files)
        {
            if (Path.GetExtension(file) == ".jsonl")
                foreach (string line in File.ReadLines(file).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    // Interrupted writes may leave a partial final line; report it without its contents.
                    try { using var document = JsonDocument.Parse(line); Visit(document.RootElement); }
                    catch (JsonException) { rejected++; }
                }
            else { using var document = JsonDocument.Parse(File.ReadAllText(file)); Visit(document.RootElement); }
        }
        var report = new StringBuilder($"# 卡牌文本检查（协议 {MechanicCatalog.ProtocolVersion}）\n\n");
        report.AppendLine($"共 {cards.Count} 张不同定义，跳过 {rejected} 个不兼容定义／不完整记录。费用图标以 ⚡ 代替，星图标以 ⭐ 代替；游戏内费用图标跟随职业。\n");
        foreach (var card in cards.Values)
        {
            report.AppendLine($"## {card.Name}（{CardText.Name(card.Type.ToString().ToLowerInvariant(), true)}）\n");
            for (int i = 0; i < card.Forms.Length; i++)
            {
                var form = card.Forms[i];
                string cost = form.Cost.EnergyX == true ? "X⚡" : Icons("energy", form.Cost.Energy.ToString());
                if (form.Cost.Stars is int stars) cost += " / " + (form.Cost.StarsX == true ? "X⭐" : Icons("stars", stars.ToString()));
                report.AppendLine($"**{(i == 0 ? "基础" : "升级")}** · 耗能 {cost}\n");
                report.AppendLine(CardText.Render(card, true, upgraded: i == 1, resource: Icons) + "\n");
                if (form.Tags.Length > 0) report.AppendLine(string.Join("。", form.Tags.Select(k => CardText.Name(k.ToString().ToLowerInvariant(), true))) + "。\n");
            }
        }
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Text audit: {cards.Count} definitions; {rejected} incompatible/incomplete records skipped. Report: {path}");
        return cards.Count == 0 || rejected > 0 ? 1 : 0;
    }
    private static string Icons(string kind, string amount) => CardText.ResourceText(kind, amount, "character")
        .Replace("[img]res://images/packed/sprite_fonts/character_energy_icon.png[/img]", "⚡")
        .Replace("[img]res://images/packed/sprite_fonts/star_icon.png[/img]", "⭐");
}
