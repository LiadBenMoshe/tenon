# Getting started

## 1. Install the tool

```
dotnet tool install -g tenon
tenon doctor
```

`tenon doctor` tells you whether the setup stub, the custom action DLL and optional tools (signtool) are available.

## 2. Create tenon.json

From your solution folder:

```
tenon init --project src/MyApp/MyApp.csproj --publisher "My Company"
```

This writes `tenon.json` with sensible defaults: a fresh upgrade code, the publish folder as the file set, a Start Menu shortcut, an optional desktop shortcut and the .NET Desktop Runtime prerequisite. The file has a `$schema` reference, so Visual Studio and VS Code give you completion and documentation for every property.

## 3. Build

```
tenon build
```

Tenon publishes your project (`dotnet publish -c Release -r win-x64`), harvests the output, lints the definition, writes the MSI, then wraps it in `Setup.exe`. Output goes to `artifacts/installer` by default:

```
MyApp-1.0.0-x64.msi     for IT: msiexec /i MyApp-1.0.0-x64.msi /qn
MyApp-Setup-1.0.0.exe   for users: double-click
```

Useful switches:

| Switch | Effect |
|---|---|
| `--publish-dir <dir>` | Use an existing publish folder instead of publishing |
| `--msi-only` | Skip Setup.exe |
| `--no-sign` | Skip signing even when configured |
| `--out <dir>` | Output folder |
| `-v` | Verbose output |

## 4. Try it

```
tenon run             # launches Setup.exe
tenon run --quiet     # silent install, exit code tells the result
tenon run --uninstall --quiet
```

Setup.exe understands the usual switches: `/quiet`, `/passive`, `/uninstall`, `/repair`, `/norestart`, `/log file`, `INSTALLDIR=...`, `ALLUSERS=1|0`.

## 5. Ship an update

Change the version in your project, run `tenon build` again and distribute the new Setup.exe. Installing it over an older version performs an in-place major upgrade. If you want applications to update themselves, see [Auto-update](updates.md).

## Per-user or per-machine?

`install.scope` decides:

- `perUser`: installs under `%LocalAppData%\Programs`, never asks for administrator rights.
- `perMachine`: installs under `Program Files`, requires administrator rights.
- `either` (default): the user chooses on the folder page. Per-user is the default and needs no prompt; per-machine asks once through the standard Windows prompt.

Services and firewall rules need a per-machine install; Tenon tells you when a definition mixes them with a per-user scope.

## Building in CI

Use `dotnet publish -p:BuildInstaller=true` with the `Tenon.MSBuild` package, or call the tool. Builds run on Windows only (the MSI is written through `msi.dll`). Signing works with a certificate thumbprint, a PFX file, or any command template (Azure Trusted Signing, for example).
