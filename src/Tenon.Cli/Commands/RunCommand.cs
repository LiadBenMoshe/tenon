using System.Diagnostics;
using Tenon.Core.Loading;
using Tenon.Core.Logging;

namespace Tenon.Cli.Commands;

/// <summary>tenon run [--quiet|--passive] [--uninstall] [extra setup args]: launches the last built Setup.exe.</summary>
public sealed class RunCommand
{
    private readonly ITenonLogger _log;

    public RunCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var doc = DefinitionLoader.Load(DefinitionLoader.Locate(cl.Option("definition", "d")));
        var outDir = doc.Resolve(doc.Definition.Output.Dir);
        var setup = Directory.Exists(outDir) ? Directory.GetFiles(outDir, "*.exe").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (setup == null)
        {
            _log.Error($"No Setup.exe in {outDir}. Run 'tenon build' first.");
            return 1;
        }

        var args = new List<string>();
        if (cl.Flag("uninstall")) args.Add("/uninstall");
        if (cl.Flag("quiet")) args.Add("/quiet");
        if (cl.Flag("passive")) args.Add("/passive");
        var log = Path.Combine(outDir, "setup-run.log");
        args.Add("/log"); args.Add("\"" + log + "\"");
        args.AddRange(cl.Positional);

        _log.Info($"Starting {Path.GetFileName(setup)} {string.Join(" ", args)}");
        using var p = Process.Start(new ProcessStartInfo(setup, string.Join(" ", args)) { UseShellExecute = true, WorkingDirectory = outDir })!;
        p.WaitForExit();
        _log.Info($"Setup exited with {p.ExitCode} (log: {log})");
        return p.ExitCode;
    }
}
