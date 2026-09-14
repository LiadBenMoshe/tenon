# tenon.json reference

Every property has a description in the JSON schema (`tenon schema`), which editors show inline. This page explains the concepts and shows the complete shape.

## Path constants

Inno-style constants make paths readable and portable:

| Constant | Meaning |
|---|---|
| `{app}` | The installation folder (`install.dir`) |
| `{autopf}` | Program Files for per-machine installs, `%LocalAppData%\Programs` for per-user installs |
| `{pf}`, `{pf32}` | Program Files (64-bit), Program Files (x86) |
| `{localappdata}`, `{appdata}`, `{commonappdata}` | Per-user local data, roaming data, ProgramData |
| `{group}` | The product's Start Menu folder |
| `{desktop}`, `{startup}`, `{startmenu}` | Desktop, Startup, Start Menu programs |
| `{sys}`, `{win}`, `{temp}`, `{fonts}` | System folders |
| `{publish}`, `{src}` | Build time only: the publish output and the folder of tenon.json |

## Branding: icon and logo

| Property | Effect |
|---|---|
| `product.icon` (an `.ico` file) | Icon of the Setup.exe file in Explorer, icon in Add/Remove Programs, and the wizard logo when no `ui.theme.logo` is set. |
| `ui.theme.logo` (png/jpg) | Logo shown in the wizard's side panel and title bar; overrides the icon there. |
| `ui.theme.accent`, `ui.theme.background`, `ui.theme.font`, `ui.theme.darkMode` | Colors and font of the wizard; `darkMode` is `auto` (follow Windows), `light` or `dark`. |
| `ui.license` (rtf/txt) | Adds the license page. |

Without `product.icon` the Setup.exe keeps the generic Tenon icon.

## Macros

`$(Name)` values are replaced at build time: `$(AssemblyVersion)`, `$(AssemblyInformationalVersion)`, `$(ProductName)`, `$(Publisher)`, `$(Version)`, `$(Arch)`, `$(Configuration)`, `$(RuntimeIdentifier)`, `$(env.NAME)` (environment variable, empty when unset).

## Sections

```jsonc
{
  "$schema": "https://tenon.dev/schema/v1/tenon.schema.json",

  "build": {
    "project": "src/MyApp/MyApp.csproj",   // published with dotnet publish
    "configuration": "Release",
    "runtimeIdentifier": "win-x64",        // default from product.arch
    "selfContained": false,
    "publishArgs": "",                     // extra dotnet publish arguments
    "publishDir": null                     // use an existing folder instead of publishing
  },

  "product": {
    "name": "Contoso Notes",
    "version": "$(AssemblyVersion)",       // major.minor.patch; the 4th part is ignored by Windows Installer
    "publisher": "Contoso",
    "upgradeCode": "GUID",                 // never change it for a product
    "description": "...", "url": "...", "supportUrl": "...",
    "icon": "Assets/app.ico",
    "languages": ["en-US"],
    "arch": "x64",                         // x64 | x86 | arm64
    "allowSameVersionUpgrades": false
  },

  "install": {
    "scope": "either",                     // perMachine | perUser | either
    "defaultScope": "perUser",
    "dir": "{autopf}\\Contoso\\Notes",
    "allowChangeDir": true,
    "minWindows": "10.0.17763",
    "mainExe": "ContosoNotes.exe",
    "closeRunningApp": "prompt",
    "runAfterInstall": true,
    "deleteAppDataOnUninstall": ["{localappdata}\\Contoso\\Notes"],
    "arp": { "noModify": false, "noRepair": false, "hidden": false }
  },

  "files": [
    { "from": "{publish}\\**", "exclude": ["*.pdb"], "to": "{app}" },
    { "from": "docs\\readme.txt", "to": "{app}\\docs", "task": "docs", "permanent": false }
  ],

  "shortcuts": [
    { "name": "Contoso Notes", "target": "{app}\\ContosoNotes.exe", "in": "{group}", "description": "...", "args": "", "workingDir": null }
  ],

  "tasks": [                               // checkboxes on the Options page; each becomes an MSI feature
    { "id": "desktopIcon", "title": "Create a desktop shortcut", "default": true }
  ],

  "fileAssociations": [
    { "extension": ".cnote", "progId": "Contoso.Notes.Document", "description": "Contoso Note",
      "icon": "{app}\\ContosoNotes.exe,0", "open": "\"{app}\\ContosoNotes.exe\" \"%1\"", "mime": "application/x-cnote" }
  ],

  "registry": [
    { "root": "HKMU", "key": "Software\\Contoso\\Notes", "name": "InstallDir", "value": "{app}", "type": "string" }
  ],

  "environment": [
    { "name": "PATH", "value": "{app}\\cli", "action": "append", "scope": "auto" }
  ],

  "services": [
    { "name": "ContosoSync", "displayName": "Contoso Sync", "exe": "{app}\\Sync.exe", "start": "auto",
      "account": "LocalService", "recovery": { "firstFailure": "restart" } }
  ],

  "firewall": [
    { "name": "Sync", "program": "{app}\\Sync.exe", "direction": "in", "protocol": "tcp", "port": "8765", "profiles": ["private", "domain"] }
  ],

  "prerequisites": [
    { "id": "dotnet-desktop", "version": "8.0", "rollForward": "latestMinor", "download": false },
    { "id": "vcredist-x64" },
    { "id": "my-driver", "file": "prereqs\\driver.msi", "detect": { "productCode": "{...}" }, "requires": "perMachine" }
  ],

  "hooks": { "project": "MyApp.Hooks\\MyApp.Hooks.csproj", "runtime": "shared" },

  "ui": {
    "style": "modern",                     // modern | oneClick
    "pages": ["welcome", "license", "folder", "options", "progress", "finish"],
    "license": "LICENSE.rtf",
    "theme": { "accent": "#2563EB", "background": null, "darkMode": "auto", "logo": "Assets/logo.png", "font": null }
  },

  "signing": {
    "mode": "signtool",                    // none | signtool | command
    "certificate": { "thumbprint": "..." },            // or { "file": "cert.pfx", "passwordEnv": "SIGN_PWD" }
    "timestampUrl": "http://timestamp.digicert.com",
    "command": "signtool sign /fd SHA256 /tr {ts} /td SHA256 /dlib ... {file}"   // for mode "command"
  },

  "update": { "feed": "https://downloads.contoso.com/notes/{channel}/releases.json", "channel": "stable" },

  "output": { "dir": "artifacts\\installer", "msi": "$(ProductName)-$(Version)-$(Arch).msi", "setup": "$(ProductName)-Setup-$(Version).exe", "extraMsi": true }
}
```

## How sections map to the MSI

| Section | Windows Installer |
|---|---|
| `product` | Property table (ProductName, ProductVersion, Manufacturer, UpgradeCode, ARP*), Upgrade table, summary information |
| `install.scope` | `ALLUSERS` / `MSIINSTALLPERUSER` and the elevation flag in the summary information |
| `install.dir` | Directory tree ending in the public `INSTALLDIR` property |
| `files` | One component per file with a stable GUID derived from the target path; File, Media (embedded cabinet), MsiFileHash |
| `shortcuts` | Shortcut table; per-user-safe registry key paths; RemoveFolder for the Start Menu folder |
| `tasks` | Features (level 1 when default, 2 otherwise); Setup.exe passes ADDLOCAL/REMOVE |
| `fileAssociations` | Extension, ProgId, Verb, MIME tables plus registry values for the icon |
| `registry` | Registry table (HKMU becomes HKLM or HKCU by scope) |
| `environment` | Environment table |
| `services` | ServiceInstall, ServiceControl, MsiServiceConfig tables |
| `firewall`, `deleteAppDataOnUninstall`, `hooks`, `.NET check` | Tenon's native custom action DLL |
| `prerequisites` | Setup.exe manifest; the standalone MSI also gets a launch condition for the .NET runtime |

## Lint

`tenon validate` (and every build) reports problems with stable ids, a message and a fix, for example:

```
TN0070 error: Shortcut 'Notes' points to '{app}\Notes.exe', which is not an installed file. (at shortcuts[0])
    fix: Use a path such as {app}\MyApp.exe that matches a file in 'files'.
```
