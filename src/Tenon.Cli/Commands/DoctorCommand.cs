using Tenon.Cli.Pipeline;
using Tenon.Cli.Signing;
using Tenon.Core.Logging;

namespace Tenon.Cli.Commands;

/// <summary>tenon doctor: checks that everything a build needs is available on this machine.</summary>
public sealed class DoctorCommand
{
    private readonly ITenonLogger _log;

    public DoctorCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var problems = 0;
        void Check(string what, string? path, string hint)
        {
            // Optional tools never count as problems; they only limit what a build can do.
            Console.WriteLine(path != null ? $"  [ok]      {what,-34} {path}" : $"  [missing] {what,-34} {hint}");
        }

        Console.WriteLine($"Tenon {typeof(Program).Assembly.GetName().Version?.ToString(3)} on {Environment.OSVersion.VersionString} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        Console.WriteLine($"  dotnet host   {BuildContext.DotnetPath}");
        Console.WriteLine();
        Console.WriteLine("Build assets:");
        foreach (var arch in new[] { "x64", "x86", "arm64" })
        {
            var stub = ToolAssets.Find(arch, "tenon-setup.exe");
            var ca = ToolAssets.Find(arch, "TenonCA.dll");
            var host = ToolAssets.Find(arch, "tenon-hookhost.exe");
            var optional = arch != "x64";
            Console.WriteLine($"  {arch}:");
            Console.WriteLine($"    setup stub      {(stub != null ? "ok  " + stub : optional ? "not available (Setup.exe cannot be built for this architecture)" : "MISSING")}");
            Console.WriteLine($"    custom actions  {(ca != null ? "ok  " + ca : optional ? "not available (hooks, firewall, app-data cleanup unavailable)" : "MISSING")}");
            Console.WriteLine($"    hook host       {(host != null ? "ok  " + host : optional ? "not available" : "MISSING")}");
            if (!optional && (stub == null || ca == null || host == null)) problems++;
        }
        Console.WriteLine();
        Console.WriteLine("Optional tools:");
        Check("signtool.exe", Signer.FindSignTool(), "install the Windows SDK or set TENON_SIGNTOOL (needed only for signing)");
        var cub = Directory.Exists(@"C:\Program Files (x86)\Windows Kits\10\bin")
            ? Directory.GetFiles(@"C:\Program Files (x86)\Windows Kits\10\bin", "darice.cub", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        Check("ICE validation (darice.cub)", cub, "install the Windows SDK MSI tools (optional)");
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tenon", "downloads");
        Console.WriteLine($"  download cache                       {cache}{(Directory.Exists(cache) ? $" ({Directory.GetFiles(cache).Length} files)" : " (empty)")}");
        Console.WriteLine();
        Console.WriteLine(problems == 0 ? "Everything needed for 'tenon build' is available." : $"{problems} problem(s) found.");
        return problems == 0 ? 0 : 1;
    }
}
