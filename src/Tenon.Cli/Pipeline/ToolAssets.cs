namespace Tenon.Cli.Pipeline;

/// <summary>
/// Locates the prebuilt binaries Tenon ships per architecture: the setup stub, the custom action DLL
/// and the hook host. Lookup order: TENON_ASSETS, the tool package's "stub" folder, the dev tree.
/// </summary>
public static class ToolAssets
{
    public static string? Find(string arch, string fileName)
    {
        var rid = "win-" + arch;
        var env = Environment.GetEnvironmentVariable("TENON_ASSETS");
        if (!string.IsNullOrEmpty(env))
        {
            var p = Path.Combine(env, rid, fileName);
            if (File.Exists(p)) return p;
        }

        var packaged = Path.Combine(AppContext.BaseDirectory, "stub", rid, fileName);
        if (File.Exists(packaged)) return packaged;

        var repo = FindRepoRoot();
        if (repo == null) return null;
        var candidates = new[]
        {
            Path.Combine(repo, "src", "Tenon.Setup", "bin", "stub", rid, fileName),
            Path.Combine(repo, "src", "Tenon.CustomAction", "bin", "native", rid, fileName),
            Path.Combine(repo, "src", "Tenon.HookHost", "bin", "host", rid, fileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tenon.sln"))) return dir.FullName;
        return null;
    }
}
