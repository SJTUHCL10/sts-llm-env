using System.Text;
using System.Text.Json;

namespace Forge.NativeAudit;

internal static class PckLocalization
{
    internal static Dictionary<string, Dictionary<string, string>> Read(string path, string language)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x43504447) throw new FormatException("Expected Godot PCK header.");
        uint version = reader.ReadUInt32();
        if (version is not (2 or 3)) throw new FormatException("Unsupported PCK version.");
        _ = reader.ReadUInt32(); _ = reader.ReadUInt32(); _ = reader.ReadUInt32();
        uint flags = reader.ReadUInt32();
        if ((flags & 1) != 0) throw new FormatException("Encrypted PCK directories are unsupported.");
        ulong dataBase = reader.ReadUInt64();
        ulong directory = version == 3 ? reader.ReadUInt64() : 0;
        file.Seek(64, SeekOrigin.Current);
        if (directory > 0) file.Position = checked((long)directory);
        uint count = reader.ReadUInt32();
        if (count > 1_000_000) throw new FormatException("Invalid PCK file count.");
        var entries = new List<(string Name, ulong Offset, ulong Size)>();
        for (uint i = 0; i < count; i++)
        {
            uint length = reader.ReadUInt32();
            if (length > 16_384) throw new FormatException("Invalid PCK path length.");
            string name = Encoding.UTF8.GetString(reader.ReadBytes(checked((int)length))).TrimEnd('\0');
            ulong offset = reader.ReadUInt64(), size = reader.ReadUInt64();
            _ = reader.ReadBytes(16);
            uint entryFlags = reader.ReadUInt32();
            if (name.StartsWith($"localization/{language}/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            {
                if ((entryFlags & 1) != 0) throw new FormatException("Encrypted localization is unsupported.");
                entries.Add((name, checked(dataBase + offset), size));
            }
        }
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Size > 32 * 1024 * 1024 || entry.Offset > (ulong)file.Length || entry.Size > (ulong)file.Length - entry.Offset)
                throw new FormatException("Localization entry outside PCK bounds.");
            file.Position = checked((long)entry.Offset);
            byte[] bytes = reader.ReadBytes(checked((int)entry.Size));
            tables.Add(Path.GetFileNameWithoutExtension(entry.Name), JsonSerializer.Deserialize<Dictionary<string, string>>(bytes)
                ?? throw new FormatException("Empty localization table."));
        }
        if (!tables.ContainsKey("cards")) throw new FormatException("Card localization table missing from PCK.");
        return tables;
    }
}
