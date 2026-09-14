using System.Security.Cryptography;
using Tenon.Core.Logging;
using Tenon.Core.Model;
using Tenon.Payload;

namespace Tenon.Cli.Prereqs;

/// <summary>A prerequisite package resolved at build time: where it is on disk and how to describe it in the manifest.</summary>
public sealed record ResolvedPrereq(ManifestPrerequisite Manifest, string? LocalPath);

/// <summary>Resolves built-in prerequisite ids to downloadable packages and caches them locally.</summary>
public sealed class PrereqResolver
{
    private const string DotnetFeed = "https://builds.dotnet.microsoft.com/dotnet";
    private readonly ITenonLogger _log;
    private readonly string _cacheDir;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public PrereqResolver(ITenonLogger log)
    {
        _log = log;
        _cacheDir = Environment.GetEnvironmentVariable("TENON_CACHE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tenon", "downloads");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<ResolvedPrereq> ResolveAsync(PrerequisiteDef def, string arch, Func<string, string> resolvePath)
    {
        var id = def.Id.ToLowerInvariant();
        string url;
        string displayName;
        string? localPath = null;

        switch (id)
        {
            case "dotnet-desktop":
            case "dotnet-runtime":
            {
                var desktop = id == "dotnet-desktop";
                var channel = string.IsNullOrEmpty(def.Version) ? "8.0" : def.Version!;
                var version = await ResolveDotnetVersionAsync(desktop ? "WindowsDesktop" : "Runtime", channel);
                var file = desktop ? $"windowsdesktop-runtime-{version}-win-{arch}.exe" : $"dotnet-runtime-{version}-win-{arch}.exe";
                url = $"{DotnetFeed}/{(desktop ? "WindowsDesktop" : "Runtime")}/{version}/{file}";
                displayName = $".NET {(desktop ? "Desktop " : "")}Runtime {version}";
                break;
            }
            case "vcredist-x64":
            case "vcredist-x86":
            case "vcredist-arm64":
            {
                var a = id.Substring("vcredist-".Length);
                url = $"https://aka.ms/vs/17/release/vc_redist.{a}.exe";
                displayName = $"Microsoft Visual C++ Redistributable ({a})";
                break;
            }
            default:
            {
                if (string.IsNullOrEmpty(def.File))
                    throw new InvalidOperationException($"Prerequisite '{def.Id}' is not a built-in id; set 'file' to the package path. Built-in ids: dotnet-desktop, dotnet-runtime, vcredist-x64, vcredist-x86, vcredist-arm64.");
                localPath = resolvePath(def.File!);
                if (!File.Exists(localPath)) throw new FileNotFoundException($"Prerequisite package '{localPath}' does not exist.");
                if (def.Detect == null)
                    throw new InvalidOperationException($"Prerequisite '{def.Id}' needs a 'detect' rule (productCode, upgradeCode, registryValue or file) so Setup knows when it is already installed.");
                url = "";
                displayName = def.Id;
                break;
            }
        }

        if (localPath == null)
        {
            localPath = await DownloadToCacheAsync(url, displayName);
        }

        string sha;
        await using (var s = File.OpenRead(localPath))
            sha = Convert.ToHexString(await SHA256.HashDataAsync(s)).ToLowerInvariant();

        var manifest = new ManifestPrerequisite
        {
            Id = def.Id,
            Version = def.Version,
            RollForward = def.RollForward,
            Args = def.Args,
            DisplayName = displayName,
            Sha256 = sha,
            Requires = def.Requires switch { ScopeRequirement.PerMachine => "perMachine", ScopeRequirement.PerUser => "perUser", _ => "any" },
            Url = url.Length > 0 ? url : null,
            Entry = def.Download && url.Length > 0 ? null : "prereq-" + SafeName(def.Id) + Path.GetExtension(localPath).ToLowerInvariant(),
            Detect = def.Detect == null ? null : new ManifestPrerequisiteDetect
            {
                ProductCode = def.Detect.ProductCode, UpgradeCode = def.Detect.UpgradeCode, RegistryValue = def.Detect.RegistryValue,
                MinVersion = def.Detect.MinVersion, File = def.Detect.File,
            },
        };
        return new ResolvedPrereq(manifest, manifest.Entry != null ? localPath : null);
    }

    private static string SafeName(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

    private async Task<string> ResolveDotnetVersionAsync(string product, string channel)
    {
        // A full version such as 8.0.31 is used as-is; a channel such as 8.0 resolves to its latest patch.
        if (channel.Count(c => c == '.') >= 2) return channel;
        var cacheFile = Path.Combine(_cacheDir, $"{product}-{channel}-latest.version");
        try
        {
            var text = await _http.GetStringAsync($"{DotnetFeed}/{product}/{channel}/latest.version");
            var version = text.Trim().Split('\n').Last().Trim();
            File.WriteAllText(cacheFile, version);
            return version;
        }
        catch (Exception ex) when (File.Exists(cacheFile))
        {
            _log.Warn($"Could not check the latest .NET {channel} version ({ex.Message}); using cached {File.ReadAllText(cacheFile).Trim()}.");
            return File.ReadAllText(cacheFile).Trim();
        }
    }

    private async Task<string> DownloadToCacheAsync(string url, string displayName)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var target = Path.Combine(_cacheDir, fileName);
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            _log.Debug($"Using cached {fileName}");
            return target;
        }

        _log.Info($"Downloading {displayName} ...");
        var tmp = target + ".part";
        using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(tmp);
            await stream.CopyToAsync(file);
        }
        File.Move(tmp, target, overwrite: true);
        _log.Info($"  saved {fileName} ({new FileInfo(target).Length / 1024 / 1024} MB) to {_cacheDir}");
        return target;
    }
}
