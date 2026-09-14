using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tenon.Core.Loading;
using Tenon.Core.Logging;
using Tenon.Update;

namespace Tenon.Cli.Commands;

/// <summary>
/// tenon release --to &lt;folder|github:owner/repo&gt; [--channel stable] [--notes file.md] [--setup file.exe]
/// Publishes the built Setup.exe and updates releases.&lt;channel&gt;.json so Tenon.Update can find it.
/// </summary>
public sealed class ReleaseCommand
{
    private readonly ITenonLogger _log;

    public ReleaseCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var to = cl.Option("to");
        if (string.IsNullOrEmpty(to))
        {
            _log.Error("usage: tenon release --to <folder | github:owner/repo> [--channel stable] [--notes notes.md] [--setup Setup.exe] [--mandatory] [--prerelease]");
            return 1;
        }

        var doc = DefinitionLoader.Load(DefinitionLoader.Locate(cl.Option("definition", "d")));
        var def = doc.Definition;
        var channel = cl.Option("channel") ?? def.Update?.Channel ?? "stable";

        // Find the setup package: explicit, or the newest *.exe in the output folder.
        var setup = cl.Option("setup");
        if (setup == null)
        {
            var outDir = doc.Resolve(def.Output.Dir);
            setup = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "*.exe").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (setup == null) throw new FileNotFoundException($"No Setup.exe found in {outDir}. Run 'tenon build' first or pass --setup.");
        }
        setup = Path.GetFullPath(setup);

        // Read version and product identity from the payload manifest, so the feed matches what was built.
        using var payload = Payload.PayloadReader.Open(setup);
        var manifest = payload.ReadManifest();
        var notes = cl.Option("notes") != null ? File.ReadAllText(cl.Option("notes")!) : null;
        var sha = Sha256(setup);
        var entry = new ReleaseEntry
        {
            Version = manifest.Version,
            Published = DateTimeOffset.UtcNow,
            Notes = notes,
            Mandatory = cl.Flag("mandatory"),
            Prerelease = cl.Flag("prerelease") || manifest.Version.Contains('-'),
            MinOsBuild = manifest.MinWindowsBuild,
            Arch = manifest.Arch,
            Full = new ReleasePackage { Url = Path.GetFileName(setup), Size = new FileInfo(setup).Length, Sha256 = sha },
        };

        if (to.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            return PublishGitHub(to.Substring("github:".Length), manifest, entry, setup, channel, notes).GetAwaiter().GetResult();
        return PublishFolder(to, manifest, entry, setup, channel);
    }

    private int PublishFolder(string folder, Payload.SetupManifest manifest, ReleaseEntry entry, string setup, string channel)
    {
        Directory.CreateDirectory(folder);
        var feedPath = Path.Combine(folder, $"releases.{channel}.json");
        var feed = File.Exists(feedPath) ? ReleaseFeed.FromJson(File.ReadAllText(feedPath)) : new ReleaseFeed();
        feed.Product = manifest.ProductName;
        feed.UpgradeCode = manifest.UpgradeCode;
        feed.Channel = channel;
        feed.Releases.RemoveAll(r => r.Version == entry.Version && r.Arch == entry.Arch);
        feed.Releases.Add(entry);
        feed.Releases = feed.Releases.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0)).ToList();

        var target = Path.Combine(folder, Path.GetFileName(setup));
        if (!string.Equals(target, setup, StringComparison.OrdinalIgnoreCase)) File.Copy(setup, target, overwrite: true);
        File.WriteAllText(feedPath, feed.ToJson() + Environment.NewLine);
        _log.Info($"Published {manifest.ProductName} {entry.Version} to {folder}");
        _log.Info($"  feed     {feedPath}");
        _log.Info($"  package  {target} ({entry.Full.Size / 1024 / 1024} MB)");
        _log.Info($"  releases {feed.Releases.Count}");
        return 0;
    }

    private async Task<int> PublishGitHub(string repo, Payload.SetupManifest manifest, ReleaseEntry entry, string setup, string channel, string? notes)
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Set GITHUB_TOKEN to publish to GitHub Releases.");
        var parts = repo.Split('/');
        if (parts.Length != 2) throw new ArgumentException("Use github:owner/repo.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("tenon");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var api = $"https://api.github.com/repos/{parts[0]}/{parts[1]}";
        var tag = "v" + entry.Version;

        // Feed lives at the "latest" release so its URL stays stable: .../releases/latest/download/releases.<channel>.json
        var feed = new ReleaseFeed { Product = manifest.ProductName, UpgradeCode = manifest.UpgradeCode, Channel = channel };
        try
        {
            var existing = await http.GetStringAsync($"https://github.com/{parts[0]}/{parts[1]}/releases/latest/download/releases.{channel}.json");
            feed = ReleaseFeed.FromJson(existing);
        }
        catch
        {
            _log.Debug("No existing feed on GitHub; starting a new one.");
        }
        entry.Full.Url = $"https://github.com/{parts[0]}/{parts[1]}/releases/download/{tag}/{Path.GetFileName(setup)}";
        feed.Releases.RemoveAll(r => r.Version == entry.Version && r.Arch == entry.Arch);
        feed.Releases.Add(entry);
        feed.Releases = feed.Releases.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0)).ToList();

        // Create (or reuse) the release.
        var releaseJson = JsonSerializer.Serialize(new { tag_name = tag, name = $"{manifest.ProductName} {entry.Version}", body = notes ?? "", prerelease = entry.Prerelease, draft = false });
        var create = await http.PostAsync($"{api}/releases", new StringContent(releaseJson, Encoding.UTF8, "application/json"));
        JsonDocument release;
        if (create.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            release = JsonDocument.Parse(await http.GetStringAsync($"{api}/releases/tags/{tag}"));
        }
        else
        {
            create.EnsureSuccessStatusCode();
            release = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        }
        var uploadUrl = release.RootElement.GetProperty("upload_url").GetString()!.Split('{')[0];
        var releaseId = release.RootElement.GetProperty("id").GetInt64();

        await UploadAsset(http, api, releaseId, uploadUrl, release, Path.GetFileName(setup), File.ReadAllBytes(setup), "application/octet-stream");
        await UploadAsset(http, api, releaseId, uploadUrl, release, $"releases.{channel}.json", Encoding.UTF8.GetBytes(feed.ToJson()), "application/json");
        _log.Info($"Published {manifest.ProductName} {entry.Version} to https://github.com/{repo}/releases/tag/{tag}");
        _log.Info($"  feed URL for tenon.json: https://github.com/{repo}/releases/latest/download/releases.{{channel}}.json");
        return 0;
    }

    private async Task UploadAsset(HttpClient http, string api, long releaseId, string uploadUrl, JsonDocument release, string name, byte[] bytes, string contentType)
    {
        // Replace an asset with the same name.
        if (release.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (a.GetProperty("name").GetString() == name)
                    await http.DeleteAsync($"{api}/releases/assets/{a.GetProperty("id").GetInt64()}");
            }
        }
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var response = await http.PostAsync($"{uploadUrl}?name={Uri.EscapeDataString(name)}", content);
        response.EnsureSuccessStatusCode();
        _log.Info($"  uploaded {name} ({bytes.Length / 1024} KB)");
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
