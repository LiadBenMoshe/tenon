# CLI reference

```
tenon <command> [options]
```

| Command | Purpose |
|---|---|
| `init [--project x.csproj] [--name N] [--publisher P] [--force]` | Create `tenon.json` for a project |
| `build [--definition f] [--publish-dir d] [--no-publish] [--out d] [--config c] [--msi-only] [--no-sign]` | Publish, lint, build the MSI and Setup.exe, sign |
| `validate [--definition f] [--publish-dir d]` | Resolve and lint without building |
| `inspect <file.msi> [--table T] [--summary]` | Show MSI tables and summary information |
| `run [--quiet] [--passive] [--uninstall] [args]` | Launch the last built Setup.exe |
| `release --to <folder\|github:owner/repo> [--channel c] [--notes f] [--setup f] [--mandatory] [--prerelease]` | Publish Setup.exe and the update feed |
| `sign <file>...` | Sign files with the `signing` section |
| `schema [--out f]` | Print the JSON schema for tenon.json |
| `doctor` | Check that stubs, custom action binaries and optional tools are present |
| `version` | Print the version |

Global options: `--verbose` (`-v`), `--help` (`-h`).

## Exit codes

`0` success, `1` error, `2` lint errors. Setup.exe itself returns Windows Installer codes: `0`, `1602` cancelled, `1603` failed, `1638` newer version installed, `3010` reboot required.

## Setup.exe switches

```
/install (default)  /uninstall  /repair  /modify  /update
/quiet  /passive  /norestart  /forcerestart
/log <file>  /lang <xx-XX>  /layout <folder>  /extract-msi <file>
INSTALLDIR=<folder>  ALLUSERS=1|0  ADDLOCAL=<tasks>  NAME=value
```

## Environment variables

| Variable | Purpose |
|---|---|
| `TENON_ASSETS` | Folder with `win-<arch>\tenon-setup.exe`, `TenonCA.dll`, `tenon-hookhost.exe` (overrides the packaged ones) |
| `TENON_STUB` | Path to a specific setup stub |
| `TENON_CACHE` | Download cache for prerequisites (default `%LocalAppData%\Tenon\downloads`) |
| `TENON_SIGNTOOL` | Path to signtool.exe |
| `DOTNET_HOST_PATH` | dotnet host used for `dotnet publish` |

## MSBuild

With the `Tenon.MSBuild` package referenced by the application project:

```
dotnet publish -c Release -r win-x64 -p:BuildInstaller=true
```

Properties: `TenonDefinition` (default `tenon.json` next to the project), `TenonOutputDir`, `TenonExtraArgs`.
