# Changelog

All notable changes to Tenon are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

## [0.1.1] - 2026-09-14

### Added
- MSI engine: files (globs), shortcuts, tasks (features), registry, environment variables, services, file associations, launch conditions, major upgrades, per-user/per-machine/either scope.
- Setup.exe: WPF wizard (welcome, license, folder and scope, options, progress, finish, error, maintenance), light/dark theme, single elevation prompt, Add/Remove Programs registration with cached uninstaller, `/quiet` `/passive` `/uninstall` `/repair` `/log` switches.
- Prerequisites: .NET Desktop Runtime, .NET Runtime, VC++ Redistributable, custom MSI/EXE packages; embedded or downloaded.
- C# setup hooks (`Tenon.Hooks`) running on modern .NET through a NativeAOT custom action DLL.
- Built-in actions: .NET runtime check for standalone MSI, firewall rules, user-data cleanup on uninstall, launch after install.
- Auto-update runtime (`Tenon.Update`) and `tenon release` for folder and GitHub Releases feeds.
- CLI: `init`, `build`, `validate`, `inspect`, `run`, `release`, `sign`, `schema`, `doctor`.
- MSBuild integration (`Tenon.MSBuild`): `dotnet publish -p:BuildInstaller=true`.
- JSON schema for `tenon.json`, documentation, samples, unit and end-to-end tests.
- `product.icon` replaces the Tenon icon on the Setup.exe file and in the wizard; `ui.theme.logo` overrides the wizard logo.
