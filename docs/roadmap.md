# Status and roadmap

## Verified end to end (Windows 11, x64)

- Silent and interactive per-user install, in-place upgrade, uninstall with clean-up (files, shortcuts, registry, Add/Remove entry, package cache).
- Setup.exe wizard pages: welcome, license, folder with scope choice, options (tasks), progress with real Windows Installer progress, finish, error, maintenance (repair/uninstall).
- Uninstall from Add/Remove Programs through the cached Setup.exe.
- C# hooks: Prepare (property set), AfterInstall, BeforeUninstall, rollback wiring; .NET runtime check; app-data deletion; file associations; environment variables; registry; tasks.
- Auto-update: feed publishing with `tenon release`, in-app check, download with hash verification, apply with wait-for-exit and restart.
- Standalone MSI with `msiexec /qn`.

## Implemented but not yet verified on a clean machine

- Per-machine installs through the elevated helper (single prompt) and firewall rules (need an administrator prompt to test).
- Prerequisite installation when the .NET Desktop Runtime is missing (use `tests/e2e/sandbox.wsb` in Windows Sandbox).
- Windows services (the table rows are emitted and unit-tested; no sample service yet).
- x86 and arm64 packages (the engine supports them; stubs must be published for those architectures).
- GitHub Releases publishing in `tenon release`.

## Not yet implemented

- Multi-language packages (embedded transforms per language) and localized Setup.exe strings.
- Custom XAML pages in Setup.exe.
- `tenon run --dry-run` plan preview.
- ICE validation runner (the Windows SDK CUB files are optional; Tenon's own lint covers the common rules).
- Delta updates (the feed format reserves the fields).
- Closing a running application through a dedicated page (Windows Installer's Restart Manager integration is used instead).
- Merge modules, patches, MSIX.
