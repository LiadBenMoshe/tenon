# Auto-update

Tenon updates work by installing the new Setup.exe as an in-place upgrade. There is one install
mechanism, so hooks, services, shortcuts and Add/Remove Programs are always consistent. Per-user
installs update without an administrator prompt; per-machine installs prompt once.

## 1. Point the installer at a feed

```jsonc
"update": { "feed": "https://downloads.contoso.com/notes/{channel}/releases.json", "channel": "stable" }
```

The feed URL is written to the registry at install time (`HKCU|HKLM\Software\<Publisher>\<Product>\Tenon`), so the application does not need to know it. `file://` URLs and UNC paths work for intranet deployments.

## 2. Check from the application

```
dotnet add package Tenon.Update
```

```csharp
using var updates = UpdateManager.FromInstalledApp();   // uses the Company/Product assembly attributes
if (updates.IsInstalled)                                // false when running from bin/Debug
{
    var update = await updates.CheckForUpdatesAsync();
    if (update != null)
    {
        var package = await updates.DownloadUpdatesAsync(update, progress);
        updates.ApplyUpdatesAndRestart(package);        // Setup waits for the app to exit, upgrades, restarts it
        // or: updates.ApplyUpdatesOnExit(package);     // upgrade silently after the app closes
    }
}
```

`UpdateInfo` carries the version, release notes, size and whether the update is mandatory. `UpdateOptions` lets you pass an explicit feed URL, channel, current version, prerelease policy and `HttpClient`.

## 3. Publish releases

```
tenon build
tenon release --to \\fileserver\downloads\notes            # folder or share
tenon release --to github:contoso/notes --notes notes.md   # GitHub Releases (GITHUB_TOKEN)
```

`tenon release` copies Setup.exe next to `releases.<channel>.json` and adds the entry (version, date, size, SHA-256, notes, prerelease flag, minimum Windows build). For GitHub the feed is attached to every release, so the stable URL is
`https://github.com/<owner>/<repo>/releases/latest/download/releases.{channel}.json`.

Feed format:

```json
{
  "schema": 1, "product": "Contoso Notes", "upgradeCode": "{...}", "channel": "stable",
  "releases": [
    { "version": "1.4.2", "published": "2026-09-14T10:00:00Z", "notes": "...", "mandatory": false,
      "arch": "x64", "full": { "url": "Contoso_Notes-Setup-1.4.2.exe", "size": 65712345, "sha256": "..." } }
  ]
}
```

Relative URLs resolve against the feed location. Downloads are verified against the SHA-256 before Setup.exe runs.

## Channels

Use `{channel}` in the feed URL and pass `--channel beta` to `tenon release`. Applications can switch channels at runtime by setting `UpdateManager.Channel`.
