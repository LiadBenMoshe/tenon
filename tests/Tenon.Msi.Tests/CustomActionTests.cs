using Tenon.Core.Files;
using Tenon.Core.Model;
using Tenon.Core.Validation;
using Tenon.Msi.Hooks;
using Tenon.Msi.Planning;
using Xunit;

namespace Tenon.Msi.Tests;

public class CustomActionPlannerTests
{
    private static (InstallerDefinition def, List<HarvestedFile> files, string dir, CustomActionInputs inputs) Fixture(bool withHooks)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tenon-ca-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "App.exe"), "exe");
        File.WriteAllText(Path.Combine(dir, "TenonCA.dll"), "dll");
        File.WriteAllText(Path.Combine(dir, "tenon-hookhost.exe"), "host");
        File.WriteAllText(Path.Combine(dir, "hooks.zip"), "zip");
        var def = new InstallerDefinition
        {
            Product = { Name = "P", Publisher = "C", Version = "1.0.0", UpgradeCode = "6F1B2C0E-9C53-4D8E-9A1E-3B2F0C1D9A77" },
            Install = { Scope = InstallScope.Either, Dir = "{autopf}\\C\\P", MainExe = "App.exe", DeleteAppDataOnUninstall = { "{localappdata}\\C\\P" } },
        };
        def.Firewall.Add(new FirewallDef { Name = "Sync", Program = "{app}\\App.exe", Protocol = FirewallProtocol.Tcp, Port = "80" });
        def.Prerequisites.Add(new PrerequisiteDef { Id = "dotnet-desktop", Version = "8.0" });
        var files = new List<HarvestedFile> { new(Path.Combine(dir, "App.exe"), Core.Paths.PathParser.Parse("{app}"), "App.exe", new FileRule()) };
        var inputs = new CustomActionInputs
        {
            NativeDllPath = Path.Combine(dir, "TenonCA.dll"),
            HookHostPath = Path.Combine(dir, "tenon-hookhost.exe"),
            HooksZipPath = Path.Combine(dir, "hooks.zip"),
        };
        if (withHooks)
        {
            inputs.Hooks = new HookManifest { AssemblyName = "H.dll" };
            inputs.Hooks.Stages["Prepare"] = new HookStageInfo { Stage = "Prepare" };
            inputs.Hooks.Stages["AfterInstall"] = new HookStageInfo { Stage = "AfterInstall" };
            inputs.Hooks.Stages["BeforeUninstall"] = new HookStageInfo { Stage = "BeforeUninstall", Elevated = false };
        }
        return (def, files, dir, inputs);
    }

    [Fact]
    public void Emits_hook_actions_with_two_variants_for_either_scope()
    {
        var (def, files, dir, inputs) = Fixture(withHooks: true);
        var lint = new LintCollector();
        var plan = new Planner(def, files, lint, new PlannerOptions { CustomActions = inputs }).Plan();
        Assert.False(lint.HasErrors, string.Join("\n", lint.Messages));

        var ids = plan.CustomActions.Select(c => c.Id).ToList();
        Assert.Contains("TenonPrepare", ids);
        Assert.Contains("TenonHook_AfterInstallM", ids);
        Assert.Contains("TenonHook_AfterInstallU", ids);
        Assert.Contains("TenonHook_BeforeUninstall", ids); // not elevated: single impersonated variant
        Assert.DoesNotContain("TenonHook_BeforeInstallM", ids);
        Assert.Contains("TenonCheckDotNet", ids);
        Assert.Contains("TenonFirewallAdd", ids);
        Assert.Contains("TenonDeleteAppData", ids);
        Assert.Contains("TenonLaunch", ids);

        var afterM = plan.CustomActions.Single(c => c.Id == "TenonHook_AfterInstallM");
        Assert.Equal(CaType.Dll | CaType.Deferred | CaType.NoImpersonate, afterM.Type);
        var afterU = plan.CustomActions.Single(c => c.Id == "TenonHook_AfterInstallU");
        Assert.Equal(CaType.Dll | CaType.Deferred, afterU.Type);
        Assert.Equal(afterM.Schedule[0].Sequence - 1, afterU.Schedule[0].Sequence);

        Assert.Equal(3, plan.Binaries.Count);
        Assert.Contains(plan.Properties, p => p.Name == "TenonPrepareHooks");
        Assert.Contains(plan.Properties, p => p.Name == "TenonDotNetRequired" && p.Value == "8.0;desktop;x64");
        Assert.Contains(plan.LaunchConditions, l => l.Condition.Contains("TENON_DOTNET_OK"));

        // Everything must land in tables without sequence collisions.
        var (tables, _) = new MsiBuilder(def, files, lint).Emit(plan);
        var seq = tables.TryGet("InstallExecuteSequence")!.Rows.Select(r => (int)r[2]!).ToList();
        Assert.Equal(seq.Count, seq.Distinct().Count());
        // +1: the built-in TenonSetArpInstallLocation action every package has.
        Assert.Equal(plan.CustomActions.Count + 1, tables.TryGet("CustomAction")!.Rows.Count);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Missing_native_dll_is_reported_for_declared_features()
    {
        var (def, files, dir, inputs) = Fixture(withHooks: false);
        inputs.NativeDllPath = null;
        var lint = new LintCollector();
        new Planner(def, files, lint, new PlannerOptions { CustomActions = inputs }).Plan();
        Assert.Contains(lint.Messages, m => m.Id == LintIds.HookRuntime && m.Severity == LintSeverity.Error);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Without_inputs_no_custom_actions_beyond_arp()
    {
        var (def, files, dir, _) = Fixture(withHooks: false);
        def.Firewall.Clear();
        def.Install.DeleteAppDataOnUninstall.Clear();
        def.Prerequisites.Clear();
        var lint = new LintCollector();
        var plan = new Planner(def, files, lint).Plan();
        Assert.False(lint.HasErrors, string.Join("\n", lint.Messages));
        Assert.Empty(plan.CustomActions);
        Directory.Delete(dir, true);
    }
}
