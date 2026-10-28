using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace Forge.Mod;

internal static class GeneratedPowerIcons
{
    private static Texture2D? _small;
    private static Texture2D? _large;

    // Accessed by the game's UI on the Godot thread, never by provider work.
    internal static Texture2D Small => _small ??= Load(false);
    internal static Texture2D Large => _large ??= Load(true);

    internal static byte[] ReadPng(bool large)
    {
        string name = $"NeowsCompany.Assets.Powers.neow_ciallo_{(large ? 256 : 64)}.png";
        using var stream = typeof(GeneratedPowerIcons).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded power icon: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Texture2D Load(bool large)
    {
        using var image = new Image();
        var error = image.LoadPngFromBuffer(ReadPng(large));
        if (error != Error.Ok) throw new InvalidOperationException("Cannot decode generated power icon: " + error);
        return ImageTexture.CreateFromImage(image);
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Icon), MethodType.Getter)]
internal static class GeneratedPowerIconTexturePatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        if (__instance is not GeneratedEffectPower) return true;
        __result = GeneratedPowerIcons.Small;
        return false;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.BigIcon), MethodType.Getter)]
internal static class GeneratedPowerBigIconTexturePatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        if (__instance is not GeneratedEffectPower) return true;
        __result = GeneratedPowerIcons.Large;
        return false;
    }
}
