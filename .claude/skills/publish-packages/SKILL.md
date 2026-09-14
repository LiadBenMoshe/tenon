---
name: publish-packages
description: Release Tenon after a change - bump the version, build, test, publish assets, pack the NuGet packages, push them to nuget.org, commit and tag. Use after any feature or fix is finished.
---

# Publish Tenon packages

Run this after every completed change to the Tenon source (the user wants nuget.org to always carry the latest).

## Steps

1. Make sure the working tree builds and the change is finished (docs and CHANGELOG "Unreleased" section updated).
2. Run from the repository root:

   ```powershell
   ./build/release.ps1 -Bump patch      # or -Bump minor for new features, -Version x.y.z for an explicit number
   ```

   The script sets `VersionPrefix` in `Directory.Build.props`, dates the CHANGELOG section, builds and tests,
   runs `build/publish-assets.ps1` (stub, NativeAOT custom action DLL, hook host), `build/pack.ps1`, pushes the
   four packages (`tenon`, `Tenon.Hooks`, `Tenon.Update`, `Tenon.MSBuild`) with the `NUGET_API_KEY`
   environment variable, then commits "Release x.y.z" and tags `vx.y.z`.
3. Confirm the packages are listed:

   ```powershell
   foreach ($id in "tenon","tenon.hooks","tenon.update","tenon.msbuild") { (Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$id/index.json").versions[-1] }
   ```

   nuget.org can take a few minutes to index a new version; `--skip-duplicate` makes reruns safe.
4. If a remote exists, push: `git push && git push --tags`.

## Notes

- Use the user-local SDK when the machine's PATH still points at .NET 7: the script prepends `%LocalAppData%\Microsoft\dotnet`.
- The NativeAOT step needs Visual Studio's C++ tools; the script runs it inside VsDevCmd automatically.
- Never re-push an existing version: nuget.org rejects it. Always bump.
- Pass `-NoPush` to only pack (for example when the API key is missing) and tell the user the push is pending.
