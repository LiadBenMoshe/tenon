namespace Tenon.Setup.Engine;

public enum SetupMode { Install, Uninstall, Repair, Modify, Update, Layout }

/// <summary>Command line of Setup.exe: Burn-compatible switches plus MSI-style NAME=value properties.</summary>
public sealed class SetupArgs
{
    public SetupMode Mode { get; private set; } = SetupMode.Install;
    public bool Quiet { get; private set; }
    public bool Passive { get; private set; }
    public bool NoRestart { get; private set; }
    public bool ForceRestart { get; private set; }
    public string? LogPath { get; private set; }
    public string? Language { get; private set; }
    public string? LayoutDir { get; private set; }
    public string? ExtractMsiPath { get; private set; }
    public bool Elevated { get; private set; }
    public string? PipeName { get; private set; }
    public string? PlanFile { get; private set; }
    public string? RestartApp { get; private set; }
    public string? RestartArgs { get; private set; }
    public int? WaitPid { get; private set; }
    public bool ShowHelp { get; private set; }
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Unknown { get; } = new();

    public bool ShowUi => !Quiet;

    public static SetupArgs Parse(string[] args)
    {
        var a = new SetupArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("/") || arg.StartsWith("-"))
            {
                var name = arg.TrimStart('/', '-').ToLowerInvariant();
                string? Next() => i + 1 < args.Length ? args[++i] : null;
                switch (name)
                {
                    case "install": case "i": a.Mode = SetupMode.Install; break;
                    case "uninstall": case "x": case "remove": a.Mode = SetupMode.Uninstall; break;
                    case "repair": case "f": a.Mode = SetupMode.Repair; break;
                    case "modify": a.Mode = SetupMode.Modify; break;
                    case "update": a.Mode = SetupMode.Update; break;
                    case "layout": a.Mode = SetupMode.Layout; a.LayoutDir = Next(); break;
                    case "quiet": case "q": case "qn": case "s": case "silent": a.Quiet = true; break;
                    case "passive": case "qb": a.Passive = true; break;
                    case "norestart": a.NoRestart = true; break;
                    case "forcerestart": a.ForceRestart = true; break;
                    case "log": case "l": a.LogPath = Next(); break;
                    case "lang": case "language": a.Language = Next(); break;
                    case "extract-msi": case "extractmsi": a.ExtractMsiPath = Next(); break;
                    case "elevated": a.Elevated = true; break;
                    case "pipe": a.PipeName = Next(); break;
                    case "plan": a.PlanFile = Next(); break;
                    case "restart-app": a.RestartApp = Next(); break;
                    case "restart-args": a.RestartArgs = Next(); break;
                    case "wait-pid": a.WaitPid = int.TryParse(Next(), out var pid) ? pid : null; break;
                    case "help": case "h": case "?": a.ShowHelp = true; break;
                    default: a.Unknown.Add(arg); break;
                }
            }
            else
            {
                var eq = arg.IndexOf('=');
                if (eq > 0) a.Properties[arg.Substring(0, eq)] = arg.Substring(eq + 1).Trim('"');
                else a.Unknown.Add(arg);
            }
        }
        return a;
    }

    public const string HelpText = @"Setup options:
  /install (default)   /uninstall   /repair   /modify   /update
  /quiet               no user interface, no prompts
  /passive             progress only, no questions
  /norestart           never restart the computer
  /log <file>          write the setup log to this file
  /lang <xx-XX>        user interface language
  /layout <folder>     extract the installer files to a folder
  /extract-msi <file>  extract the MSI package to a file
  INSTALLDIR=<folder>  installation folder
  ALLUSERS=1|0         install for all users (needs administrator) or for the current user";
}
