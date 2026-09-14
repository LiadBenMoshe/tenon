# Tenon vs WiX

| | WiX | Tenon |
|---|---|---|
| Authoring | XML with Windows Installer vocabulary (Component, KeyPath, Feature, Directory, Sequence) | JSON with app vocabulary (files, shortcuts, tasks, registry, services) |
| GUIDs | Upgrade code, product code, component GUIDs | Upgrade code only |
| Files | `Files` element (v5+) or heat harvesting | Globs over the publish folder |
| User interface | Win32 dialog tables or Burn theme XML; custom dialogs need a whole dialog set | WPF wizard that follows the Windows theme; accent, logo, license and pages from JSON |
| .NET runtime | Burn bundle + `DotNetCoreSearch` + `ExePackage` + detect condition | `{ "id": "dotnet-desktop", "version": "8.0" }` |
| Custom actions in C# | DTF, .NET Framework only, `MakeSfxCA` | `[SetupHook]` methods on modern .NET |
| Per-user or per-machine | `WixUI_Advanced`, `Package/@Scope`, ICE38/57/64 rules to follow by hand | `"scope": "either"` and the rules are applied for you |
| Elevation | Burn: one prompt; plain MSI: prompt per package | One prompt for prerequisites and MSI together; none for per-user |
| Signing | cabs, `wix msi inscribe`, `wix burn detach/reattach`, signtool | `signing` section; cab, MSI and Setup.exe signed in order |
| Auto-update | Not included | `Tenon.Update` + `tenon release` |
| Errors | ICE codes and MSI error numbers | Lint ids with a message and a fix; friendly Setup error page with the log one click away |
| License | MS-RL plus Open Source Maintenance Fee (v6+) and EULA acceptance (v7+) | MIT; uses DTF 5.0.2 (MS-RL) unmodified |
| Output | MSI, bundle EXE, MSM, MSP, MSIX (HeatWave) | MSI and Setup.exe |

## When WiX is still the better tool

- Merge modules, patches (`.msp`) and transforms as first-class outputs.
- Bundles that chain many arbitrary packages with complex detect logic.
- IIS, SQL, COM+, MSMQ and other server extensions.
- MSIX packaging.

## Migrating

Most desktop `.wxs` files map one-to-one onto `tenon.json` sections: `Package` to `product`/`install`, `Files`/`Component` to `files`, `Shortcut` to `shortcuts`, `RegistryValue` to `registry`, `ServiceInstall` to `services`, `Feature` with `Level=2` to `tasks`, `CustomAction` to `hooks`. Keep the same `UpgradeCode` so Tenon-built versions upgrade WiX-built ones.
