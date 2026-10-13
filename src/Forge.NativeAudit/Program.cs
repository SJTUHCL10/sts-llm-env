using System.Runtime.Loader;
using System.Security.Cryptography;
using Forge.Core;
using Forge.NativeAudit;

try
{
    if (args.Length is < 2 or > 4 || args.Length >= 3 && args[2] is not ("zhs" or "eng"))
    {
        Console.WriteLine("Native audit: <game-directory> <output-directory> [zhs|eng] [card-ID-or-class]");
        return 2;
    }
    string game = Path.GetFullPath(args[0]);
    string lib = Path.Combine(game, "data_sts2_windows_x86_64");
    using (var assembly = File.OpenRead(Path.Combine(lib, "sts2.dll")))
        if (Convert.ToHexString(SHA256.HashData(assembly)) != NativeCardConversion.AssemblySha256)
            throw new InvalidOperationException("Game assembly differs from the reviewed v0.111.0 build. Review mappings before changing the pinned hash.");
    AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(lib, name.Name + ".dll"))
        ? context.LoadFromAssemblyPath(Path.Combine(lib, name.Name + ".dll")) : null;
    return NativeCatalog.Write(game, args[1], args.Length >= 3 ? args[2] : "zhs", args.Length == 4 ? args[3] : null);
}
catch (Exception ex)
{
    Console.Error.WriteLine("Native audit failed: " + ex.GetBaseException().Message);
    return 1;
}

