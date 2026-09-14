using Tenon.Cab;
using Tenon.Core.Files;
using Tenon.Core.Model;
using Tenon.Core.Validation;
using Tenon.Msi;
using Tenon.Msi.Ids;
using Tenon.Msi.Inspect;
using Tenon.Msi.Planning;
using Xunit;

namespace Tenon.Msi.Tests;

public class ShortNameGeneratorTests
{
    [Theory]
    [InlineData("app.exe", true)]
    [InlineData("APPLICAT.DLL", true)]
    [InlineData("Contoso Notes.exe", false)]
    [InlineData("verylongname.dll", false)]
    [InlineData("a.b.c", false)]
    [InlineData("x.json", false)]
    public void Detects_valid_short_names(string name, bool valid) => Assert.Equal(valid, ShortNameGenerator.IsValidShort(name));

    [Fact]
    public void Generates_unique_short_names()
    {
        var g = new ShortNameGenerator();
        var a = g.MsiName("Contoso Notes.exe");
        var b = g.MsiName("Contoso Notes.dll");
        var c = g.MsiName("Contoso Notes Helper.exe");
        Assert.Equal("CONTOS~1.EXE|Contoso Notes.exe", a);
        Assert.Equal("CONTOS~1.DLL|Contoso Notes.dll", b);
        Assert.Equal("CONTOS~2.EXE|Contoso Notes Helper.exe", c);
    }
}

public class ComponentGuidTests
{
    [Fact]
    public void Is_stable_and_case_insensitive()
    {
        var a = ComponentGuidGenerator.FromTargetPath("{app}\\Bin\\App.exe", "x64");
        var b = ComponentGuidGenerator.FromTargetPath("{app}\\bin\\app.exe", "x64");
        var c = ComponentGuidGenerator.FromTargetPath("{app}\\bin\\app.exe", "x86");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(5, (a.ToByteArray()[7] >> 4)); // version nibble
    }

    [Fact]
    public void Matches_rfc4122_example()
    {
        // RFC 4122 appendix: uuid5(NAMESPACE_DNS, "www.example.com")
        var dns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var g = ComponentGuidGenerator.Create(dns, "www.example.com");
        Assert.Equal(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"), g);
    }
}

public class IdGeneratorTests
{
    [Fact]
    public void Same_key_same_id()
    {
        var g = new IdGenerator();
        Assert.Equal(g.Make("fil", "{app}\\a.dll"), g.Make("fil", "{APP}\\A.DLL"));
        Assert.NotEqual(g.Make("fil", "{app}\\a.dll"), g.Make("fil", "{app}\\b.dll"));
        Assert.True(IdGenerator.IsValid(g.Make("cmp", "x")));
    }
}

public class CabPlannerTests
{
    [Fact]
    public void Splits_by_size_and_keeps_sequence()
    {
        var entries = Enumerable.Range(1, 5).Select(i => new CabEntry($"f{i}", $"C:\\f{i}", 40, i));
        var plans = CabPlanner.Plan(entries, maxCabBytes: 100);
        Assert.Equal(3, plans.Count);
        Assert.Equal(2, plans[0].LastSequence);
        Assert.Equal(4, plans[1].LastSequence);
        Assert.Equal(5, plans[2].LastSequence);
        Assert.Equal("cab2.cab", plans[1].Name);
    }
}

public class EndToEndMsiTests
{
    private static (InstallerDefinition def, List<HarvestedFile> files, string dir) MakeFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tenon-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "App.exe"), "not really an exe");
        File.WriteAllText(Path.Combine(dir, "Contoso Notes Helper.dll"), "helper");
        File.WriteAllText(Path.Combine(dir, "sub", "data.json"), "{}");

        var def = new InstallerDefinition
        {
            Product = { Name = "Contoso Notes", Publisher = "Contoso", Version = "1.4.2", UpgradeCode = "6F1B2C0E-9C53-4D8E-9A1E-3B2F0C1D9A77" },
            Install = { Scope = InstallScope.Either, Dir = "{autopf}\\Contoso\\Notes", MainExe = "App.exe" },
        };
        def.Shortcuts.Add(new ShortcutDef { Name = "Contoso Notes", Target = "{app}\\App.exe", In = "{group}" });
        def.Shortcuts.Add(new ShortcutDef { Name = "Contoso Notes", Target = "{app}\\App.exe", In = "{desktop}", Task = "desktopIcon" });
        def.Tasks.Add(new TaskDef { Id = "desktopIcon", Title = "Desktop shortcut" });
        def.Registry.Add(new RegistryDef { Root = RegistryRoot.HKMU, Key = "Software\\Contoso\\Notes", Name = "InstallDir", Value = "{app}" });
        def.Registry.Add(new RegistryDef { Root = RegistryRoot.HKMU, Key = "Software\\Contoso\\Notes", Name = "Flag", Type = RegistryValueType.Dword, Value = "1" });

        var rule = new FileRule { From = "*", To = "{app}" };
        var files = new List<HarvestedFile>
        {
            new(Path.Combine(dir, "App.exe"), Core.Paths.PathParser.Parse("{app}"), "App.exe", rule),
            new(Path.Combine(dir, "Contoso Notes Helper.dll"), Core.Paths.PathParser.Parse("{app}"), "Contoso Notes Helper.dll", rule),
            new(Path.Combine(dir, "sub", "data.json"), Core.Paths.PathParser.Parse("{app}\\sub"), "data.json", rule),
        };
        return (def, files, dir);
    }

    [Fact]
    public void Plan_produces_expected_structure()
    {
        var (def, files, dir) = MakeFixture();
        var lint = new LintCollector();
        var plan = new Planner(def, files, lint).Plan();

        Assert.False(lint.HasErrors, string.Join("\n", lint.Messages));
        Assert.Equal("1.4.2", plan.MsiVersion);
        Assert.Equal(3, plan.Files.Count());
        Assert.Contains(plan.Directories, d => d.Id == "INSTALLDIR");
        Assert.Contains(plan.Directories, d => d.Id == "ProgramFiles64Folder");
        Assert.Contains(plan.Directories, d => d.Id == Planner.MenuDirId);
        Assert.Equal(new[] { "Main", "desktopIcon" }, plan.Features.Select(f => f.Id));
        Assert.NotNull(plan.MainExeFileId);

        var desktop = plan.Components.Single(c => c.Shortcuts.Count == 1 && c.DirectoryId == "DesktopFolder");
        Assert.Equal("desktopIcon", desktop.FeatureId);
        Assert.Equal(1, desktop.Registry[0].Root);

        var reg = plan.Components.Single(c => c.Registry.Count == 2);
        Assert.Equal("[INSTALLDIR]", reg.Registry[0].Value);
        Assert.Equal("#1", reg.Registry[1].Value);
        Assert.Equal(-1, reg.Registry[0].Root);

        Assert.Contains(plan.Properties, p => p.Name == "ALLUSERS" && p.Value == "2");
        Assert.Contains(plan.Properties, p => p.Name == "MSIINSTALLPERUSER" && p.Value == "1");
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var (def, files, dir) = MakeFixture();
        var p1 = new Planner(def, files, new LintCollector()).Plan();
        var p2 = new Planner(def, files, new LintCollector()).Plan();
        Assert.Equal(p1.Components.Select(c => c.Id + c.Guid), p2.Components.Select(c => c.Id + c.Guid));
        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Writes_a_readable_msi_with_embedded_cab()
    {
        var (def, files, dir) = MakeFixture();
        var lint = new LintCollector();
        var builder = new MsiBuilder(def, files, lint, options: new MsiBuildOptions { IntermediateDir = Path.Combine(dir, "cab") });
        var msi = Path.Combine(dir, "out.msi");

        var result = builder.Build(msi);

        Assert.True(File.Exists(msi));
        using (var inspect = new MsiInspector(msi))
        {
            Assert.Equal("Contoso Notes", inspect.GetProperty("ProductName"));
            Assert.Equal("1.4.2", inspect.GetProperty("ProductVersion"));
            Assert.Equal(result.ProductCode, inspect.GetProperty("ProductCode"));
            Assert.Equal(3, inspect.RowCount("File"));
            Assert.Equal(3, inspect.RowCount("MsiFileHash"));
            Assert.Equal(2, inspect.RowCount("Shortcut"));
            Assert.Equal(2, inspect.RowCount("Upgrade"));
            Assert.True(inspect.RowCount("InstallExecuteSequence") > 15);
            Assert.True(inspect.HasTable("_Validation"));
            Assert.Contains("cab1.cab", inspect.StreamNames());
            Assert.Equal("x64;1033", inspect.SummaryInfo.Template);
            Assert.Equal(500, inspect.SummaryInfo.PageCount);
            Assert.Equal(10, inspect.SummaryInfo.WordCount);

            var (cols, rows) = inspect.ReadTable("Media");
            var colList = cols.ToList();
            Assert.Single(rows);
            Assert.Equal("#cab1.cab", rows[0][colList.IndexOf("Cabinet")]);
            Assert.Equal(3, rows[0][colList.IndexOf("LastSequence")]);
        }

        var cabFiles = CabWriter.ListFiles(Path.Combine(dir, "cab", "cab1.cab"));
        Assert.Equal(result.Plan.Files.Select(f => f.Id), cabFiles);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Environment_services_and_associations_are_planned()
    {
        var (def, files, dir) = MakeFixture();
        def.Install.Scope = InstallScope.PerMachine;
        def.Environment.Add(new EnvironmentDef { Name = "PATH", Value = "{app}\\cli", Action = EnvironmentAction.Append });
        def.Environment.Add(new EnvironmentDef { Name = "NOTES_HOME", Value = "{app}", Scope = EnvironmentScope.User });
        def.Services.Add(new ServiceDef { Name = "NotesSync", Exe = "{app}\\App.exe", Account = "LocalService", Start = ServiceStartMode.DelayedAuto, Recovery = new ServiceRecoveryDef() });
        def.FileAssociations.Add(new FileAssociationDef { Extension = ".cnote", ProgId = "Contoso.Note", Description = "Contoso Note", Open = "\"{app}\\App.exe\" \"%1\"", Icon = "{app}\\App.exe,1", Mime = "application/x-cnote" });

        var lint = new LintCollector();
        var plan = new Planner(def, files, lint).Plan();
        Assert.False(lint.HasErrors, string.Join("\n", lint.Messages));

        var envs = plan.Components.SelectMany(c => c.Environment).ToList();
        Assert.Contains(envs, e => e.Name == "=-*PATH" && e.Value == "[~];[INSTALLDIR]cli");
        Assert.Contains(envs, e => e.Name == "=-NOTES_HOME" && e.Value == "[INSTALLDIR]");

        var exeComponent = plan.Components.Single(c => c.Files.Any(f => f.LongName == "App.exe"));
        var svc = Assert.Single(exeComponent.Services);
        Assert.Equal("NT AUTHORITY\\LocalService", svc.StartName);
        Assert.True(svc.DelayedAutoStart);
        Assert.Equal(2, svc.StartType);
        Assert.NotNull(svc.Recovery);

        var ext = Assert.Single(exeComponent.Extensions);
        Assert.Equal("cnote", ext.Extension);
        Assert.Equal("\"%1\"", ext.VerbArgument);
        Assert.Contains(plan.Components, c => c.Registry.Any(r => r.Key == "Software\\Classes\\Contoso.Note\\DefaultIcon" && r.Value == "[INSTALLDIR]App.exe,1"));

        var (tables, _) = new MsiBuilder(def, files, lint).Emit(plan);
        Assert.Equal(1, tables.TryGet("ServiceInstall")!.Rows.Count);
        Assert.Equal(1, tables.TryGet("MsiServiceConfig")!.Rows.Count);
        Assert.Equal(1, tables.TryGet("Extension")!.Rows.Count);
        Assert.Equal(1, tables.TryGet("MIME")!.Rows.Count);
        var seq = tables.TryGet("InstallExecuteSequence")!.Rows.Select(r => (string)r[0]!).ToList();
        Assert.Contains("InstallServices", seq);
        Assert.Contains("MsiConfigureServices", seq);
        Assert.Contains("WriteEnvironmentStrings", seq);
        Assert.Contains("RegisterExtensionInfo", seq);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Services_are_rejected_for_per_user_packages()
    {
        var (def, files, dir) = MakeFixture();
        def.Install.Scope = InstallScope.PerUser;
        def.Services.Add(new ServiceDef { Name = "Svc", Exe = "{app}\\App.exe" });
        var lint = new LintCollector();
        new Planner(def, files, lint).Plan();
        Assert.Contains(lint.Messages, m => m.Id == LintIds.RequiresPerMachine && m.Severity == LintSeverity.Error);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Table_output_matches_golden_idt()
    {
        var (def, files, dir) = MakeFixture();
        def.Environment.Add(new EnvironmentDef { Name = "NOTES_HOME", Value = "{app}" });
        def.FileAssociations.Add(new FileAssociationDef { Extension = ".cnote", ProgId = "Contoso.Note", Open = "\"{app}\\App.exe\" \"%1\"" });
        var options = new PlannerOptions
        {
            ProductCode = new Guid("11111111-1111-1111-1111-111111111111"),
            PackageCode = new Guid("22222222-2222-2222-2222-222222222222"),
        };
        var lint = new LintCollector();
        var plan = new Planner(def, files, lint, options).Plan();
        Assert.False(lint.HasErrors, string.Join("\n", lint.Messages));
        var (tables, _) = new MsiBuilder(def, files, lint).Emit(plan);

        // Remove machine-specific noise before comparing.
        var actual = string.Join("\n", tables.Tables.Where(t => t.Rows.Count > 0).Select(t => t.ToIdt()));
        actual = actual.Replace(dir, "<dir>");
        actual = System.Text.RegularExpressions.Regex.Replace(actual, @"TenonVersion\t[\d.]+", "TenonVersion\t<version>");

        var goldenDir = Path.Combine(FindRepoRoot(), "tests", "Tenon.Msi.Tests", "golden");
        Directory.CreateDirectory(goldenDir);
        var goldenFile = Path.Combine(goldenDir, "fixture.idt.txt");
        if (!File.Exists(goldenFile) || Environment.GetEnvironmentVariable("TENON_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(goldenFile, actual);
        }
        var expected = File.ReadAllText(goldenFile);
        Assert.Equal(expected.Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
        Directory.Delete(dir, true);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Tenon.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Tenon.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Lint_catches_missing_shortcut_target_and_unknown_task()
    {
        var (def, files, dir) = MakeFixture();
        def.Shortcuts.Add(new ShortcutDef { Name = "Bad", Target = "{app}\\nope.exe" });
        def.Files.Add(new FileRule { From = "x", Task = "missing" });
        files.Add(new HarvestedFile(files[0].SourcePath, Core.Paths.PathParser.Parse("{app}\\x"), "y.txt", def.Files[0]));
        var lint = new LintCollector();
        new Planner(def, files, lint).Plan();
        Assert.Contains(lint.Messages, m => m.Id == LintIds.ShortcutTargetMissing);
        Assert.Contains(lint.Messages, m => m.Id == LintIds.UnknownTask);
        Directory.Delete(dir, true);
    }
}
