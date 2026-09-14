<#
.SYNOPSIS
  Publishes the prebuilt binaries Tenon ships per architecture:
    src/Tenon.Setup/bin/stub/<rid>/tenon-setup.exe        self-contained WPF setup stub
    src/Tenon.CustomAction/bin/native/<rid>/TenonCA.dll   NativeAOT custom action DLL (needs the MSVC toolchain)
    src/Tenon.HookHost/bin/host/<rid>/tenon-hookhost.exe  framework-dependent hook host
  The CLI finds them in these folders during development; pack.ps1 copies them into the tool package.
#>
param(
    [string]$Configuration = "Release",
    [string[]]$Rids = @("win-x64"),
    [switch]$SkipNative
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

foreach ($rid in $Rids) {
    Write-Host "== $rid"
    dotnet publish src/Tenon.Setup/Tenon.Setup.csproj -c $Configuration -r $rid -o "src/Tenon.Setup/bin/stub/$rid" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "stub publish failed for $rid" }
    dotnet publish src/Tenon.HookHost/Tenon.HookHost.csproj -c $Configuration -r $rid -o "src/Tenon.HookHost/bin/host/$rid" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "hook host publish failed for $rid" }

    if (-not $SkipNative) {
        # NativeAOT needs link.exe. Inside a VS developer environment IlcUseEnvironmentalTools avoids the vswhere probing
        # that fails on some machines; outside it the SDK locates the toolchain itself.
        $vsdev = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\Common7\Tools\VsDevCmd.bat" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
        $arch = $rid.Substring(4)
        $vsArch = if ($arch -eq "x86") { "x86" } elseif ($arch -eq "arm64") { "arm64" } else { "amd64" }
        $args = "publish src/Tenon.CustomAction/Tenon.CustomAction.csproj -c $Configuration -r $rid -o src/Tenon.CustomAction/bin/native/$rid -nologo -v q"
        if ($vsdev) {
            cmd.exe /c "call `"$vsdev`" -arch=$vsArch -no_logo && dotnet $args -p:IlcUseEnvironmentalTools=true"
        } else {
            Invoke-Expression "dotnet $args"
        }
        if ($LASTEXITCODE -ne 0) { throw "custom action publish failed for $rid" }
    }
}
Write-Host "Assets published."
