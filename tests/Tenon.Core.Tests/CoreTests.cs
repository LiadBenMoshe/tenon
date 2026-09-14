using Tenon.Core.Files;
using Tenon.Core.Loading;
using Tenon.Core.Model;
using Tenon.Core.Paths;
using Tenon.Core.Validation;
using Tenon.Core.Versioning;
using Xunit;

namespace Tenon.Core.Tests;

public class PathParserTests
{
    [Theory]
    [InlineData("{app}", "app", 0)]
    [InlineData("{app}\\bin\\x.dll", "app", 2)]
    [InlineData("{autopf}/Company/Product", "autopf", 2)]
    [InlineData("plugins\\a.dll", "app", 2)]
    [InlineData("{GROUP}", "group", 0)]
    public void Parses_constants_and_segments(string input, string root, int segments)
    {
        var p = PathParser.Parse(input);
        Assert.Equal(root, p.Root);
        Assert.Equal(segments, p.Segments.Count);
    }

    [Fact]
    public void Parent_and_filename()
    {
        var p = PathParser.Parse("{app}\\bin\\x.dll");
        Assert.Equal("x.dll", p.FileName);
        Assert.Equal("{app}\\bin", p.Parent.ToString());
        Assert.Equal("{app}", p.Parent.Parent.ToString());
    }

    [Fact]
    public void Resolves_build_time_constants()
    {
        var c = new Dictionary<string, string> { ["publish"] = @"C:\out" };
        Assert.Equal(@"C:\out\**", PathParser.ResolveBuildTime("{publish}\\**", c));
        Assert.Equal(@"C:\out", PathParser.ResolveBuildTime("{publish}", c));
        Assert.Equal("{app}\\x", PathParser.ResolveBuildTime("{app}\\x", c));
    }
}

public class MacroExpanderTests
{
    [Fact]
    public void Expands_nested_object_graph()
    {
        var def = new InstallerDefinition();
        def.Product.Name = "$(Name)";
        def.Product.Version = "1.0";
        def.Install.Dir = "{autopf}\\X";
        def.Output.Msi = "x.msi";
        def.Output.Setup = "x.exe";
        def.Files.Add(new FileRule { From = "$(PublishDir)\\**", Exclude = { "$(Ext)" } });
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "App", ["PublishDir"] = "C:\\p", ["Ext"] = "*.pdb" };

        var unknown = MacroExpander.ExpandInPlace(def, props);

        Assert.Empty(unknown);
        Assert.Equal("App", def.Product.Name);
        Assert.Equal("C:\\p\\**", def.Files[0].From);
        Assert.Equal("*.pdb", def.Files[0].Exclude[0]);
    }

    [Fact]
    public void Reports_unknown_macros()
    {
        var def = new InstallerDefinition();
        def.Product.Publisher = "$(Nope)";
        var unknown = MacroExpander.ExpandInPlace(def, new Dictionary<string, string>());
        Assert.Contains("Nope", unknown);
        Assert.Equal("$(Nope)", def.Product.Publisher);
    }
}

public class VersionMapperTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("v2.0.1", "2.0.1", false)]
    [InlineData("1.2.3.4", "1.2.3", true)]
    [InlineData("1.2.3-beta.1", "1.2.3", false)]
    [InlineData("1.2.3.0", "1.2.3", false)]
    public void Maps_to_three_parts(string input, string expected, bool warns)
    {
        var m = VersionMapper.Map(input);
        Assert.Equal(expected, m.MsiVersion);
        Assert.Equal(warns, m.Warning != null);
        Assert.Equal(input, m.DisplayVersion);
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.Throws<FormatException>(() => VersionMapper.Map("latest"));
    }
}

public class DefinitionLoaderTests
{
    [Fact]
    public void Loads_json_with_comments_and_enums()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var file = Path.Combine(tmp, "tenon.json");
        File.WriteAllText(file, """
            {
              // comment
              "product": { "name": "X", "publisher": "Y", "arch": "arm64" },
              "install": { "scope": "perMachine", "dir": "{autopf}\\X", },
              "registry": [ { "root": "HKLM", "key": "Software\\X", "type": "dword", "value": "5" } ]
            }
            """);

        var doc = DefinitionLoader.Load(file);

        Assert.Equal(Architecture.Arm64, doc.Definition.Product.Arch);
        Assert.Equal(InstallScope.PerMachine, doc.Definition.Install.Scope);
        Assert.Equal(RegistryValueType.Dword, doc.Definition.Registry[0].Type);
        Assert.Equal(RegistryRoot.HKLM, doc.Definition.Registry[0].Root);
        Assert.Equal(tmp, doc.Directory);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Round_trips_through_serialize()
    {
        var def = new InstallerDefinition { Product = { Name = "A", Publisher = "B" } };
        def.Tasks.Add(new TaskDef { Id = "t", Title = "T", Default = false });
        var json = DefinitionLoader.Serialize(def);
        Assert.Contains("\"tasks\"", json);
        Assert.Contains("\"either\"", json);
    }
}

public class GlobHarvesterTests
{
    [Fact]
    public void Harvests_recursively_with_excludes_and_preserves_subfolders()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "pub", "sub"));
        File.WriteAllText(Path.Combine(tmp, "pub", "a.exe"), "a");
        File.WriteAllText(Path.Combine(tmp, "pub", "a.pdb"), "p");
        File.WriteAllText(Path.Combine(tmp, "pub", "sub", "b.dll"), "b");
        File.WriteAllText(Path.Combine(tmp, "readme.txt"), "r");

        var lint = new LintCollector();
        var h = new GlobHarvester(tmp, new Dictionary<string, string> { ["publish"] = Path.Combine(tmp, "pub") }, lint);
        var files = h.Harvest(new[]
        {
            new FileRule { From = "{publish}\\**", Exclude = { "*.pdb" }, To = "{app}" },
            new FileRule { From = "readme.txt", To = "{app}\\docs" },
        });

        Assert.Empty(lint.Messages);
        var targets = files.Select(f => f.TargetPath.ToString()).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "{app}\\a.exe", "{app}\\docs\\readme.txt", "{app}\\sub\\b.dll" }, targets);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Missing_source_is_an_error()
    {
        var lint = new LintCollector();
        var h = new GlobHarvester(Path.GetTempPath(), new Dictionary<string, string>(), lint);
        h.Harvest(new[] { new FileRule { From = "does-not-exist-" + Guid.NewGuid().ToString("N") } });
        Assert.True(lint.HasErrors);
        Assert.Equal(LintIds.SourceFileMissing, lint.Messages[0].Id);
    }
}
