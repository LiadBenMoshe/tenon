using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Tenon.Update
{
    public sealed class UpdateOptions
    {
        /// <summary>Feed URL; "{channel}" is replaced by <see cref="Channel"/>. file:// and UNC paths work too.</summary>
        public string FeedUrl { get; set; } = "";
        public string Channel { get; set; } = "stable";
        /// <summary>Defaults to the entry assembly version.</summary>
        public Version CurrentVersion { get; set; }
        public bool AllowPrerelease { get; set; }
        public string Arch { get; set; } = Environment.Is64BitProcess ? (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()) : "x86";
        public HttpClient Http { get; set; }
        /// <summary>Folder where downloaded updates are kept. Defaults to %LocalAppData%\Tenon\updates.</summary>
        public string DownloadDirectory { get; set; }
    }

    public sealed class UpdateInfo
    {
        public Version Version { get; set; }
        public string VersionString { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
        public string ReleaseNotes { get; set; }
        public DateTimeOffset? Published { get; set; }
        public bool Mandatory { get; set; }
    }

    public sealed class DownloadedUpdate
    {
        public UpdateInfo Info { get; set; }
        public string FilePath { get; set; } = "";
    }

    public sealed class UpdateProgress
    {
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public double Percent => TotalBytes > 0 ? BytesReceived * 100.0 / TotalBytes : 0;
    }

    /// <summary>
    /// Checks a Tenon release feed for newer versions, downloads the setup package and applies it by
    /// running Setup.exe as an in-place upgrade. Per-user installs update without administrator rights.
    /// </summary>
    public sealed class UpdateManager : IDisposable
    {
        private readonly UpdateOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        public UpdateManager(UpdateOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.FeedUrl)) throw new ArgumentException("FeedUrl is required.", nameof(options));
            _options.CurrentVersion = _options.CurrentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            _ownsHttp = options.Http == null;
            _http = options.Http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            IsInstalled = true;
        }

        private UpdateManager(bool notInstalled)
        {
            _options = new UpdateOptions();
            _http = new HttpClient();
            _ownsHttp = true;
            IsInstalled = false;
        }

        /// <summary>False when the application is not installed by Tenon (for example when run from bin/Debug); every method is then a no-op.</summary>
        public bool IsInstalled { get; }
        public Version CurrentVersion => _options.CurrentVersion;
        public string Channel { get => _options.Channel; set => _options.Channel = value; }

        /// <summary>
        /// Creates a manager from the registration Tenon wrote at install time
        /// (HKCU or HKLM\Software\&lt;Publisher&gt;\&lt;Product&gt;\Tenon). Publisher and product default to the entry
        /// assembly's Company and Product attributes.
        /// </summary>
        public static UpdateManager FromInstalledApp(string publisher = null, string productName = null)
        {
            var asm = Assembly.GetEntryAssembly();
            publisher = publisher ?? asm?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            productName = productName ?? asm?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
            if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(productName)) return new UpdateManager(notInstalled: true);

            var subKey = $@"Software\{publisher}\{productName}\Tenon";
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using (var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKey))
                    {
                        var feed = key?.GetValue("UpdateFeed") as string;
                        if (string.IsNullOrEmpty(feed)) continue;
                        var installDir = key.GetValue("InstallDir") as string ?? "";
                        var version = key.GetValue("Version") as string;
                        var channel = key.GetValue("Channel") as string ?? "stable";
                        // Only treat this as installed when we actually run from the installed location.
                        if (installDir.Length > 0 && !AppContext.BaseDirectory.StartsWith(installDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            continue;
                        return new UpdateManager(new UpdateOptions
                        {
                            FeedUrl = feed,
                            Channel = channel,
                            CurrentVersion = Version.TryParse(version ?? "", out var v) ? v : null,
                        });
                    }
                }
                catch
                {
                    // registry not accessible
                }
            }
            return new UpdateManager(notInstalled: true);
        }

        public string FeedUrl => _options.FeedUrl.Replace("{channel}", _options.Channel);

        public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            if (!IsInstalled) return null;
            var feed = ReleaseFeed.FromJson(await ReadTextAsync(FeedUrl, ct).ConfigureAwait(false));
            var current = _options.CurrentVersion;
            var best = feed.Releases
                .Where(r => TryParseVersion(r.Version, out _))
                .Where(r => string.IsNullOrEmpty(r.Arch) || string.Equals(r.Arch, _options.Arch, StringComparison.OrdinalIgnoreCase))
                .Where(r => _options.AllowPrerelease || !r.Prerelease)
                .Where(r => r.MinOsBuild == null || Environment.OSVersion.Version.Build >= r.MinOsBuild)
                .Select(r => { TryParseVersion(r.Version, out var parsed); return new { Release = r, Parsed = parsed }; })
                .Where(x => x.Parsed > current)
                .OrderByDescending(x => x.Parsed)
                .FirstOrDefault();
            if (best == null) return null;
            return new UpdateInfo
            {
                Version = best.Parsed,
                VersionString = best.Release.Version,
                DownloadUrl = Resolve(FeedUrl, best.Release.Full.Url),
                Size = best.Release.Full.Size,
                Sha256 = best.Release.Full.Sha256,
                ReleaseNotes = best.Release.Notes,
                Published = best.Release.Published,
                Mandatory = best.Release.Mandatory,
            };
        }

        public async Task<DownloadedUpdate> DownloadUpdatesAsync(UpdateInfo info, IProgress<UpdateProgress> progress = null, CancellationToken ct = default)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            var dir = _options.DownloadDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tenon", "updates");
            Directory.CreateDirectory(dir);
            var fileName = Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
            if (string.IsNullOrEmpty(fileName)) fileName = "Setup-" + info.VersionString + ".exe";
            var target = Path.Combine(dir, fileName);

            if (File.Exists(target) && await HashMatchesAsync(target, info.Sha256).ConfigureAwait(false))
                return new DownloadedUpdate { Info = info, FilePath = target };

            var tmp = target + ".part";
            await CopyToFileAsync(info.DownloadUrl, tmp, info.Size, progress, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(info.Sha256) && !await HashMatchesAsync(tmp, info.Sha256).ConfigureAwait(false))
            {
                File.Delete(tmp);
                throw new InvalidDataException("The downloaded update is corrupt (hash mismatch).");
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
            return new DownloadedUpdate { Info = info, FilePath = target };
        }

        /// <summary>Starts Setup.exe to install the update, restarts the application afterwards and exits the current process.</summary>
        public void ApplyUpdatesAndRestart(DownloadedUpdate update, string[] restartArgs = null)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var restart = exe.Length > 0 ? "--restart-app \"" + exe + "\"" + (restartArgs != null && restartArgs.Length > 0 ? " --restart-args \"" + string.Join(" ", restartArgs).Replace("\"", "\\\"") + "\"" : "") : "";
            StartSetup(update.FilePath, "/update /passive --wait-pid " + Process.GetCurrentProcess().Id + " " + restart);
            Environment.Exit(0);
        }

        /// <summary>Installs the update silently after the application exits (no restart).</summary>
        public void ApplyUpdatesOnExit(DownloadedUpdate update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            var pid = Process.GetCurrentProcess().Id;
            StartSetup(update.FilePath, "/update /quiet --wait-pid " + pid);
        }

        private static void StartSetup(string setupPath, string arguments)
        {
            Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setupPath) });
        }

        /// <summary>Parses "1.2.3", "1.2", "v1.2.3" and SemVer forms such as "1.2.3-beta.1" (the suffix is ignored).</summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim().TrimStart('v', 'V');
            var cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut > 0) s = s.Substring(0, cut);
            if (s.IndexOf('.') < 0) s += ".0";
            return Version.TryParse(s, out version);
        }

        // ------------------------------------------------------------------ transport

        private static bool IsLocal(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("\\\\") || (url.Length > 2 && url[1] == ':');

        private static string LocalPath(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;

        private async Task<string> ReadTextAsync(string url, CancellationToken ct)
        {
            if (IsLocal(url)) return File.ReadAllText(LocalPath(url));
            using (var response = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private async Task CopyToFileAsync(string url, string target, long expectedSize, IProgress<UpdateProgress> progress, CancellationToken ct)
        {
            if (IsLocal(url))
            {
                File.Copy(LocalPath(url), target, true);
                progress?.Report(new UpdateProgress { BytesReceived = new FileInfo(target).Length, TotalBytes = new FileInfo(target).Length });
                return;
            }
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? expectedSize;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1 << 16];
                    long received = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new UpdateProgress { BytesReceived = received, TotalBytes = total });
                    }
                }
            }
        }

        private static string Resolve(string feedUrl, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https" || abs.Scheme == "file")) return url;
            if (IsLocal(feedUrl))
            {
                var dir = Path.GetDirectoryName(LocalPath(feedUrl)) ?? "";
                return Path.Combine(dir, url);
            }
            return new Uri(new Uri(feedUrl), url).ToString();
        }

        private static async Task<bool> HashMatchesAsync(string path, string sha256)
        {
            if (string.IsNullOrEmpty(sha256)) return true;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = await Task.Run(() => sha.ComputeHash(stream)).ConfigureAwait(false);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), sha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
