using Tenon.Cli.Commands;
using Tenon.Core.Logging;
using Tenon.Core.Validation;

namespace Tenon.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var log = new ConsoleLogger();
        try
        {
            var parsed = CommandLine.Parse(args);
            log.Verbose = parsed.Flag("verbose", "v");

            if (parsed.Command == null || parsed.Flag("help", "h") && parsed.Command == null)
            {
                PrintUsage();
                return parsed.Command == null && args.Length > 0 && !parsed.Flag("help", "h") ? 1 : 0;
            }

            return parsed.Command switch
            {
                "init" => new InitCommand(log).Run(parsed),
                "build" => new BuildCommand(log).Run(parsed),
                "validate" => new ValidateCommand(log).Run(parsed),
                "inspect" => new InspectCommand(log).Run(parsed),
                "schema" => new SchemaCommand(log).Run(parsed),
                "sign" => new SignCommand(log).Run(parsed),
                "release" => new ReleaseCommand(log).Run(parsed),
                "doctor" => new DoctorCommand(log).Run(parsed),
                "run" => new RunCommand(log).Run(parsed),
                "version" => Version(),
                _ => Unknown(parsed.Command),
            };
        }
        catch (LintException ex)
        {
            foreach (var e in ex.Errors) log.Error(e.ToString());
            return 2;
        }
        catch (Exception ex)
        {
            log.Error(ex.Message);
            if (log.Verbose) log.Error(ex.ToString());
            return 1;
        }
    }

    private static int Version()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3));
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    public static void PrintUsage()
    {
        Console.WriteLine(@"Tenon - installers for .NET desktop apps

usage: tenon <command> [options]

commands:
  init       Create a tenon.json for a project
               --project <csproj>      application project to package
               --name <product name>   product name (default: assembly name)
               --publisher <name>      publisher name
               --force                 overwrite an existing tenon.json
  build      Publish the app and build the MSI
               --definition <path>     tenon.json (default: ./tenon.json)
               --publish-dir <dir>     use an existing publish folder instead of dotnet publish
               --no-publish            skip dotnet publish (requires build.publishDir or --publish-dir)
               --out <dir>             output folder (default: output.dir)
               --msi-only              build only the MSI (no Setup.exe)
               --config <name>         build configuration (default: build.configuration)
  validate   Load the definition, resolve everything and report problems without building
               --definition <path>, --publish-dir <dir>
  inspect    Show the tables of an MSI
               <file.msi> [--table <name>] [--summary]
  sign       Sign files using the signing section of tenon.json
               <file> [<file>...]
  release    Publish Setup.exe and update the releases feed for auto-update
               --to <folder | github:owner/repo> [--channel stable] [--notes notes.md] [--setup file.exe]
               [--mandatory] [--prerelease]
  schema     Print the JSON schema for tenon.json
               [--out <file>]
  run        Launch the last built Setup.exe
               [--quiet] [--passive] [--uninstall] [extra setup arguments]
  doctor     Check that stubs, custom action binaries and optional tools are available
  version    Print the Tenon version

global options: --verbose (-v), --help (-h)");
    }
}
