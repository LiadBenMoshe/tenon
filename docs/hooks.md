# Setup hooks in C#

Hooks let you run your own code during installation, upgrade and uninstall. They run on the .NET
runtime your application already needs, not on .NET Framework, and they are packaged automatically.

## 1. Create a hooks project

```
dotnet new classlib -n MyApp.Hooks
dotnet add MyApp.Hooks package Tenon.Hooks
```

```csharp
using Tenon.Hooks;

public sealed class MyHooks : ISetupHooks
{
    [SetupHook(HookStage.Prepare)]
    public void Prepare(SetupContext ctx)
    {
        // Runs first, as the user. Properties set here are visible to the rest of the installation.
        ctx.SetProperty("PORT", FindFreePort().ToString());
    }

    [SetupHook(HookStage.AfterInstall)]
    public void WriteConfig(SetupContext ctx)
    {
        ctx.Progress("Writing configuration");
        File.WriteAllText(Path.Combine(ctx.InstallDir, "app.json"),
            $$"""{ "installDir": "{{ctx.InstallDir}}", "port": {{ctx.Properties["PORT"]}} }""");
    }

    [SetupHook(HookStage.BeforeUninstall)]
    public void Cleanup(SetupContext ctx) => File.Delete(Path.Combine(ctx.InstallDir, "app.json"));
}
```

## 2. Reference it from tenon.json

```jsonc
"hooks": { "project": "MyApp.Hooks\\MyApp.Hooks.csproj" }
```

`tenon build` builds the project, packs its output into the MSI and schedules a custom action for every stage it finds. Nothing runs at build time; the assembly is only inspected.

## Stages

| Stage | When | Runs as |
|---|---|---|
| `Prepare` | Before anything changes (after costing). May call `SetProperty`. | The user |
| `BeforeInstall` | Before files are copied (after the previous version was removed on upgrade) | Elevated when per-machine |
| `AfterInstall` | After files, shortcuts, registry and services are in place | Elevated when per-machine |
| `BeforeUninstall` | Before files are removed, only on a real uninstall (not on upgrade) | Elevated when per-machine |
| `AfterUninstall` | After the product was removed | Elevated when per-machine |
| `Rollback` | When an installation whose BeforeInstall/AfterInstall hooks ran is rolled back | Elevated when per-machine |

Attribute options: `Elevated = false` runs a stage as the user even for per-machine installs, `ContinueOnError = true` logs failures instead of failing the installation, `Order` sorts hooks within a stage, `Condition` adds a Windows Installer condition.

## SetupContext

`ProductName`, `Version`, `InstallDir`, `IsPerMachine`, `IsUpgrade`, `IsUninstall`, `IsRepair`, `IsElevated`, `UiLevel`, `Properties`, `Task(id)` (was a checkbox selected), `Log`, `LogWarning`, `Progress(text)`, `RequireReboot()`, `Cancellation`, `DataDirectory`.

Throw `SetupHookException("message")` to fail the installation with a message shown to the user. Everything a hook logs ends up in the MSI log (`/log`) prefixed with `Tenon:`.

## Runtime

The hook host is a small framework-dependent .NET executable, so a .NET runtime must exist on the machine when hooks run. Declare the `dotnet-desktop` (or `dotnet-runtime`) prerequisite and Setup.exe installs it first; the standalone MSI checks for it in a launch condition. For self-contained applications set `"hooks": { "runtime": "app" }` and hooks after `BeforeInstall` use the runtime shipped inside your application folder.

## How it works

`TenonCA.dll` (a NativeAOT library, so no dependency on .NET Framework) is stored in the MSI's Binary table. The immediate `TenonPrepare` action unpacks the hook host and your assembly to a temp folder and runs the `Prepare` stage. Deferred actions receive their context through `CustomActionData`, start the hook host for their stage and relay its log, progress, property and reboot events back into the Windows Installer session. For an `either` scope package two variants of each deferred action exist so hooks run as SYSTEM for per-machine installs and as the user for per-user installs.
