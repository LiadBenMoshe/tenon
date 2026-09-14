using System.Security.Cryptography;
using Tenon.Payload;
using Tenon.Update;
using Xunit;

namespace Tenon.Update.Tests;

public class UpdateManagerTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "tenon-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static ReleaseFeed Feed(params (string version, bool prerelease)[] versions)
    {
        var feed = new ReleaseFeed { Product = "P", UpgradeCode = "{X}", Channel = "stable" };
        foreach (var (v, pre) in versions)
            feed.Releases.Add(new ReleaseEntry { Version = v, Prerelease = pre, Arch = "x64", Full = new ReleasePackage { Url = $"Setup-{v}.exe", Size = 3, Sha256 = "" } });
        return feed;
    }

    [Fact]
    public async Task Picks_highest_stable_version_newer_than_current()
    {
        var dir = TempDir();
        var feedPath = Path.Combine(dir, "releases.stable.json");
        File.WriteAllText(feedPath, Feed(("1.0.0", false), ("1.2.0", false), ("1.3.0-beta", true), ("1.1.0", false)).ToJson());
        using var mgr = new UpdateManager(new UpdateOptions { FeedUrl = feedPath, CurrentVersion = new Version(1, 0, 0), Arch = "x64" });

        var info = await mgr.CheckForUpdatesAsync();

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.VersionString);
        Assert.Equal(Path.Combine(dir, "Setup-1.2.0.exe"), info.DownloadUrl);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Returns_null_when_up_to_date_and_honours_prerelease_flag()
    {
        var dir = TempDir();
        var feedPath = Path.Combine(dir, "releases.stable.json");
        File.WriteAllText(feedPath, Feed(("1.0.0", false), ("1.1.0-rc", true)).ToJson());
        using var stable = new UpdateManager(new UpdateOptions { FeedUrl = feedPath, CurrentVersion = new Version(1, 0, 0), Arch = "x64" });
        using var pre = new UpdateManager(new UpdateOptions { FeedUrl = feedPath, CurrentVersion = new Version(1, 0, 0), Arch = "x64", AllowPrerelease = true });

        Assert.Null(await stable.CheckForUpdatesAsync());
        Assert.Equal("1.1.0-rc", (await pre.CheckForUpdatesAsync())!.VersionString);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Downloads_and_verifies_hash()
    {
        var dir = TempDir();
        var package = Path.Combine(dir, "Setup-2.0.0.exe");
        File.WriteAllBytes(package, new byte[] { 1, 2, 3, 4 });
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
        var feed = Feed(("2.0.0", false));
        feed.Releases[0].Full.Sha256 = sha;
        var feedPath = Path.Combine(dir, "releases.stable.json");
        File.WriteAllText(feedPath, feed.ToJson());
        using var mgr = new UpdateManager(new UpdateOptions { FeedUrl = feedPath, CurrentVersion = new Version(1, 0), Arch = "x64", DownloadDirectory = Path.Combine(dir, "dl") });

        var info = await mgr.CheckForUpdatesAsync();
        var downloaded = await mgr.DownloadUpdatesAsync(info!);

        Assert.True(File.Exists(downloaded.FilePath));
        Assert.Equal(4, new FileInfo(downloaded.FilePath).Length);

        feed.Releases[0].Full.Sha256 = "deadbeef";
        File.WriteAllText(feedPath, feed.ToJson());
        File.Delete(downloaded.FilePath);
        var bad = await mgr.CheckForUpdatesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => mgr.DownloadUpdatesAsync(bad!));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Channel_placeholder_is_replaced()
    {
        using var mgr = new UpdateManager(new UpdateOptions { FeedUrl = "https://x/{channel}/releases.json", Channel = "beta", CurrentVersion = new Version(1, 0) });
        Assert.Equal("https://x/beta/releases.json", mgr.FeedUrl);
    }
}

public class PayloadTests
{
    [Fact]
    public void Round_trips_entries_and_manifest_after_a_stub()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tenon-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stub = Path.Combine(dir, "stub.exe");
        File.WriteAllBytes(stub, Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        var msi = Path.Combine(dir, "x.msi");
        File.WriteAllBytes(msi, Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray());
        var manifest = new SetupManifest { ProductName = "P", Version = "1.2.3", UpgradeCode = "{U}", Scope = "perUser" };

        var output = Path.Combine(dir, "setup.exe");
        new PayloadWriter().AddFile("package.msi", msi).AddText(PayloadFormat.ManifestEntry, manifest.ToJson()).Write(stub, output);

        using var reader = PayloadReader.Open(output);
        Assert.True(reader.Contains("package.msi"));
        var m = reader.ReadManifest();
        Assert.Equal("1.2.3", m.Version);
        var extracted = reader.Extract("package.msi", Path.Combine(dir, "out.msi"));
        Assert.Equal(File.ReadAllBytes(msi), File.ReadAllBytes(extracted));
        Assert.Null(PayloadReader.TryOpen(stub));
        reader.Dispose();
        Directory.Delete(dir, true);
    }
}
