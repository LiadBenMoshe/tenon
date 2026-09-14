# How Tenon works

```
tenon.json ──► DefinitionLoader ──► macros ──► GlobHarvester
                                                   │
                                                   ▼
                       Planner (InstallPlan: directories, components, features,
                       shortcuts, registry, services, extensions, custom actions)
                                                   │ lint (TNxxxx)
                                                   ▼
                       TableEmitter (MSI tables) + SequenceBuilder + CabPlanner
                                                   │
                                                   ▼
                       MsiWriter (DTF over msi.dll) ──► product.msi
                                                   │
                                                   ▼
                       PayloadWriter: stub + manifest + msi + prereqs ──► Setup.exe
```

## The MSI

Tenon writes the Windows Installer database directly: every table is defined once in `MsiSchema` (column types, keys, validation), which produces both the `CREATE TABLE` statements and the `_Validation` rows. Design rules that keep packages correct:

- One file per component; component GUIDs are UUID v5 hashes of the target path and architecture, so they are stable across builds without any bookkeeping.
- Shortcuts in user-profile folders live in their own components with an HKCU key path and RemoveFolder rows (the Windows Installer per-user rules).
- Standard actions are emitted only when their tables have rows; Tenon's actions sit in reserved sequence slots.
- Major upgrades are configured for every package: `RemoveExistingProducts` after `InstallInitialize`, plus a downgrade guard.
- `either` scope uses `ALLUSERS=2` with `MSIINSTALLPERUSER=1`, so Windows Installer itself redirects Program Files and the Start Menu for per-user installs.
- The Windows build is read from the registry (`AppSearch`), because `msiexec` reports Windows 8.1 to unmanifested packages.

## Setup.exe

The stub is a self-contained WPF application published once and shipped in the tool package. `tenon build` copies it and appends a payload container: entries (MSI, license, logo, prerequisite installers), a JSON index and a footer. Signing happens after appending, and the reader finds the footer through the PE security directory when a certificate follows it.

At run time the stub:

1. Reads the manifest, detects installed versions through `MsiEnumRelatedProducts`.
2. Shows the wizard (or runs headless for `/quiet` and `/passive`).
3. Installs missing prerequisites, then runs the MSI with `MsiSetInternalUI(NONE)` and an external UI record handler that translates Windows Installer progress messages into the progress bar.
4. When administrator rights are needed it launches a second copy of itself elevated (`--elevated --pipe --plan`), which executes the plan and streams progress over a named pipe. One prompt, no matter how many steps.
5. Registers its own Add/Remove Programs entry pointing at a cached copy of Setup.exe (the MSI is installed with `ARPSYSTEMCOMPONENT=1`), so uninstall, repair and modify from Settings open the same wizard.

## Custom actions

`TenonCA.dll` is written in C# and compiled with NativeAOT into a plain native DLL with exported functions, which is what Windows Installer expects. It runs hooks through `tenon-hookhost.exe`, checks for the .NET runtime, adds and removes firewall rules through `netsh`, deletes user data on uninstall and launches the application after a standalone MSI install.

## Updates

`Tenon.Update` reads the feed URL from the registry key written at install time, compares versions, downloads and verifies the new Setup.exe and starts it with `/update /passive --wait-pid <app> --restart-app <exe>`. Setup waits for the application to exit, performs the upgrade and restarts it.
