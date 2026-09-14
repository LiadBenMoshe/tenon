using Tenon.Core.Logging;
using Tenon.Msi.Inspect;

namespace Tenon.Cli.Commands;

public sealed class InspectCommand
{
    private readonly ITenonLogger _log;

    public InspectCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var file = cl.Positional.FirstOrDefault();
        if (file == null || !File.Exists(file))
        {
            _log.Error("usage: tenon inspect <file.msi> [--table <name>] [--summary]");
            return 1;
        }

        using var msi = new MsiInspector(file);
        var table = cl.Option("table", "t");

        if (cl.Flag("summary") || table == null)
        {
            var si = msi.SummaryInfo;
            Console.WriteLine($"Subject      {si.Subject}");
            Console.WriteLine($"Author       {si.Author}");
            Console.WriteLine($"Template     {si.Template}");
            Console.WriteLine($"Package code {si.RevisionNumber}");
            Console.WriteLine($"Page count   {si.PageCount}   Word count {si.WordCount}");
            Console.WriteLine($"Created by   {si.CreatingApp}");
            Console.WriteLine($"ProductCode  {msi.GetProperty("ProductCode")}");
            Console.WriteLine($"Version      {msi.GetProperty("ProductVersion")}");
            Console.WriteLine();
        }

        if (table == null)
        {
            Console.WriteLine("Tables:");
            foreach (var name in msi.TableNames)
                Console.WriteLine($"  {name,-32} {msi.RowCount(name),6} rows");
            return 0;
        }

        if (!msi.HasTable(table))
        {
            _log.Error($"Table '{table}' does not exist.");
            return 1;
        }

        var (columns, rows) = msi.ReadTable(table);
        var widths = columns.Select((c, i) => Math.Min(60, Math.Max(c.Length, rows.Count == 0 ? 0 : rows.Max(r => (r[i]?.ToString() ?? "").Length)))).ToList();
        Console.WriteLine(string.Join("  ", columns.Select((c, i) => c.PadRight(widths[i]))));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows)
            Console.WriteLine(string.Join("  ", row.Select((v, i) => Truncate(v?.ToString() ?? "", widths[i]).PadRight(widths[i]))));
        Console.WriteLine($"{rows.Count} rows");
        return 0;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}
