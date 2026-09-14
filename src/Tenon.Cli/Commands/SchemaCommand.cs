using Tenon.Core.Logging;
using Tenon.Core.Schema;

namespace Tenon.Cli.Commands;

public sealed class SchemaCommand
{
    private readonly ITenonLogger _log;

    public SchemaCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var json = JsonSchemaGenerator.Generate();
        var output = cl.Option("out", "o");
        if (output == null)
        {
            Console.WriteLine(json);
            return 0;
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(output, json + Environment.NewLine);
        _log.Info($"Wrote {output}");
        return 0;
    }
}
