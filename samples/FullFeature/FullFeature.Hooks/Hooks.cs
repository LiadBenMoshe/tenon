using System.Text.Json;
using Tenon.Hooks;

namespace FullFeature.Hooks;

/// <summary>Installer logic for Contoso Notes, written in plain C#.</summary>
public sealed class ContosoHooks : ISetupHooks
{
    [SetupHook(HookStage.Prepare)]
    public void Prepare(SetupContext ctx)
    {
        // Runs before anything changes. Properties set here are available to the rest of the installation.
        ctx.Log($"Preparing {ctx.ProductName} {ctx.Version} (upgrade: {ctx.IsUpgrade}, per-machine: {ctx.IsPerMachine})");
        ctx.SetProperty("CONTOSO_PREPARED_AT", DateTime.UtcNow.ToString("O"));
    }

    [SetupHook(HookStage.AfterInstall)]
    public void WriteSettings(SetupContext ctx)
    {
        ctx.Progress("Writing application settings");
        var settings = new
        {
            installDir = ctx.InstallDir,
            version = ctx.Version.ToString(),
            perMachine = ctx.IsPerMachine,
            desktopIcon = ctx.Task("desktopIcon"),
            installedAt = DateTime.UtcNow,
        };
        File.WriteAllText(Path.Combine(ctx.InstallDir, "app.settings.json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        ctx.Log("Wrote app.settings.json");
    }

    [SetupHook(HookStage.BeforeUninstall)]
    public void CleanUp(SetupContext ctx)
    {
        var settings = Path.Combine(ctx.InstallDir, "app.settings.json");
        if (File.Exists(settings))
        {
            File.Delete(settings);
            ctx.Log("Removed app.settings.json");
        }
    }

    [SetupHook(HookStage.Rollback, ContinueOnError = true)]
    public void Rollback(SetupContext ctx)
    {
        var settings = Path.Combine(ctx.InstallDir, "app.settings.json");
        if (File.Exists(settings)) File.Delete(settings);
        ctx.Log("Rolled back settings file");
    }
}
