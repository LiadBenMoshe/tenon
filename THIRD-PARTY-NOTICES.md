# Third-party notices

Tenon is licensed under the MIT License (see LICENSE). It depends on the following third-party components,
which keep their own licenses.

## WiX Toolset Deployment Tools Foundation (DTF) 5.0.2

Packages: `WixToolset.Dtf.WindowsInstaller`, `WixToolset.Dtf.Compression`, `WixToolset.Dtf.Compression.Cab`
License: Microsoft Reciprocal License (MS-RL)
Source: https://github.com/wixtoolset/wix

Tenon consumes these binaries unmodified to access `msi.dll` and to create cabinet files. Version 5.0.2 is the
last release published without the WiX Open Source Maintenance Fee terms; do not upgrade without reviewing them.

## Other NuGet dependencies (MIT, .NET Foundation / Microsoft)

- System.Text.Json
- Microsoft.Extensions.FileSystemGlobbing
- System.Reflection.MetadataLoadContext
- Microsoft.Win32.Registry
- xunit, Microsoft.NET.Test.Sdk (tests only)

## Trademarks

Windows, Windows Installer, WiX and .NET are trademarks of their respective owners. Tenon is not affiliated
with Microsoft or FireGiant.
