using System.Diagnostics;
using Tenon.Core.Logging;
using Tenon.Core.Model;

namespace Tenon.Cli.Signing;

/// <summary>Signs MSI, cabinet and executable files according to the signing section of tenon.json.</summary>
public sealed class Signer
{
    private readonly SigningSection _cfg;
    private readonly ITenonLogger _log;

    public Signer(SigningSection cfg, ITenonLogger log)
    {
        _cfg = cfg;
        _log = log;
    }

    public bool Enabled => _cfg.Mode != SigningMode.None;

    public void Sign(string file)
    {
        if (!Enabled) return;
        _log.Info($"Signing {Path.GetFileName(file)} ...");
        switch (_cfg.Mode)
        {
            case SigningMode.Signtool:
                RunSignTool(file);
                break;
            case SigningMode.Command:
                RunCommand(file);
                break;
        }
    }

    private void RunSignTool(string file)
    {
        var signtool = FindSignTool() ?? throw new InvalidOperationException(
            "signtool.exe was not found. Install the Windows SDK, or set signing.mode to 'command' with your own signing command.");

        var args = new List<string> { "sign", "/fd", "SHA256", "/td", "SHA256", "/tr", _cfg.TimestampUrl };
        var cert = _cfg.Certificate;
        if (cert?.Thumbprint != null)
        {
            args.Add("/sha1"); args.Add(cert.Thumbprint);
        }
        else if (cert?.File != null)
        {
            args.Add("/f"); args.Add(cert.File);
            if (cert.PasswordEnv != null)
            {
                var pwd = Environment.GetEnvironmentVariable(cert.PasswordEnv);
                if (string.IsNullOrEmpty(pwd)) throw new InvalidOperationException($"Environment variable {cert.PasswordEnv} (PFX password) is not set.");
                args.Add("/p"); args.Add(pwd);
            }
        }
        else
        {
            args.Add("/a"); // pick the best certificate automatically
        }
        args.Add(file);

        var psi = new ProcessStartInfo(signtool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Run(psi, "signtool");
    }

    private void RunCommand(string file)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Command))
            throw new InvalidOperationException("signing.mode is 'command' but signing.command is empty. Use {file} and {ts} placeholders.");
        var cmd = _cfg.Command!.Replace("{file}", "\"" + file + "\"").Replace("{ts}", _cfg.TimestampUrl);
        var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        Run(psi, "signing command");
    }

    private void Run(ProcessStartInfo psi, string what)
    {
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _log.Debug(line.TrimEnd());
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{what} failed with exit code {p.ExitCode}:\n{output.Trim()}");
    }

    public static string? FindSignTool()
    {
        var env = Environment.GetEnvironmentVariable("TENON_SIGNTOOL");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var candidate = Path.Combine(dir.Trim(), "signtool.exe");
            if (dir.Length > 0 && File.Exists(candidate)) return candidate;
        }

        var kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (!Directory.Exists(kits)) return null;
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };
        return Directory.GetDirectories(kits, "10.*")
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => Path.Combine(d, arch, "signtool.exe"))
            .FirstOrDefault(File.Exists);
    }
}
