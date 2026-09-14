# Contributing to Tenon

Thanks for helping. This page tells you how to get a change from idea to merged.

## Before you start

- Look through the [open issues](https://github.com/LiadBenMoshe/tenon/issues). For anything larger than a bug fix, open an issue first so we can agree on the approach.
- Read [docs/architecture.md](docs/architecture.md) for the shape of the code.

## Setting up

Windows 10/11 with:

- .NET 8 SDK
- Visual Studio 2022 with the "Desktop development with C++" workload (only for the NativeAOT custom action DLL)

```
git clone https://github.com/LiadBenMoshe/tenon.git
cd tenon
dotnet build Tenon.sln
dotnet test Tenon.sln
./build/publish-assets.ps1     # builds the setup stub, custom action DLL and hook host for win-x64
```

Then try a sample end to end (per-user, no administrator prompt):

```
cd samples/HelloWpf
dotnet ../../src/Tenon.Cli/bin/Debug/net8.0/tenon.dll build
dotnet ../../src/Tenon.Cli/bin/Debug/net8.0/tenon.dll run --quiet
dotnet ../../src/Tenon.Cli/bin/Debug/net8.0/tenon.dll run --uninstall --quiet
```

## Making a change

1. Create a branch from `main`.
2. Keep the change focused. Refactoring and features go in separate pull requests.
3. Add or update tests:
   - `tests/Tenon.Core.Tests`: loading, macros, paths, globbing, lint
   - `tests/Tenon.Msi.Tests`: planner and tables (the golden file `golden/fixture.idt.txt` shows every table row; regenerate it with `TENON_UPDATE_GOLDEN=1 dotnet test` when a change is intentional)
   - `tests/Tenon.Update.Tests`: feed and payload
   - `tests/e2e/run.ps1`: real install/uninstall of a built Setup.exe
4. Update the docs in `docs/` when behavior or `tenon.json` changes. The JSON schema is generated from the model: run `tenon schema --out schema/tenon.schema.json` after changing `InstallerDefinition`.
5. Run `dotnet test Tenon.sln` and, for installer-affecting changes, the e2e script.
6. Open a pull request. Describe what changed, why, and how you tested it.

## Style

- C# 12, nullable enabled, file-scoped namespaces, 4-space indentation (see `.editorconfig`).
- Windows Installer rules are subtle; when you touch the planner, say which rule (component key paths, per-user folders, sequence numbers) the change respects.
- User-facing messages (lint, Setup.exe pages, logs) are plain English sentences with a suggested fix where possible.

## Reporting bugs

Use the bug report template. Attach the setup log (`Setup.exe /log file.txt` writes it, and the Error page has an "Open the setup log" link) and the MSI log that sits next to it.

## License

By contributing you agree that your contributions are licensed under the MIT License of this repository.
