using Tenon.Cli.Signing;
using Tenon.Core.Loading;
using Tenon.Core.Logging;

namespace Tenon.Cli.Commands;

public sealed class SignCommand
{
    private readonly ITenonLogger _log;

    public SignCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        if (cl.Positional.Count == 0)
        {
            _log.Error("usage: tenon sign <file> [<file>...] [--definition tenon.json]");
            return 1;
        }
        var doc = DefinitionLoader.Load(DefinitionLoader.Locate(cl.Option("definition", "d")));
        var signer = new Signer(doc.Definition.Signing, _log);
        if (!signer.Enabled)
        {
            _log.Error("signing.mode is 'none' in tenon.json. Set it to 'signtool' or 'command'.");
            return 1;
        }
        foreach (var f in cl.Positional)
        {
            if (!File.Exists(f)) throw new FileNotFoundException(f);
            signer.Sign(Path.GetFullPath(f));
        }
        return 0;
    }
}
