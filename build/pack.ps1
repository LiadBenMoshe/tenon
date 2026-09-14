<#
.SYNOPSIS
  Creates the NuGet packages into artifacts/packages:
    tenon           the dotnet tool (CLI + prebuilt stubs)
    Tenon.Hooks     SDK for C# setup hooks
    Tenon.Update    auto-update runtime for applications
    Tenon.MSBuild   MSBuild integration: dotnet publish -p:BuildInstaller=true
  Run build/publish-assets.ps1 first so the stubs exist.
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = "artifacts/packages"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($rid in @("win-x64", "win-x86", "win-arm64")) {
    $stub = "src/Tenon.Setup/bin/stub/$rid/tenon-setup.exe"
    if (-not (Test-Path $stub)) { Write-Warning "No setup stub for $rid (run build/publish-assets.ps1 -Rids $rid); the tool will not build Setup.exe for that architecture." }
}

dotnet pack src/Tenon.Hooks/Tenon.Hooks.csproj -c $Configuration -o $out -nologo
dotnet pack src/Tenon.Update/Tenon.Update.csproj -c $Configuration -o $out -nologo
dotnet pack src/Tenon.Cli/Tenon.Cli.csproj -c $Configuration -o $out -nologo

# MSBuild package: targets + a framework-dependent copy of the CLI.
$cli = "artifacts/cli"
Remove-Item $cli -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/Tenon.Cli/Tenon.Cli.csproj -c $Configuration -o $cli -nologo -v q -p:PackAsTool=false
foreach ($rid in @("win-x64", "win-x86", "win-arm64")) {
    foreach ($asset in @("src/Tenon.Setup/bin/stub/$rid/tenon-setup.exe", "src/Tenon.CustomAction/bin/native/$rid/TenonCA.dll", "src/Tenon.HookHost/bin/host/$rid/tenon-hookhost.exe")) {
        if (Test-Path $asset) { New-Item -ItemType Directory -Force "$cli/stub/$rid" | Out-Null; Copy-Item $asset "$cli/stub/$rid/" }
    }
}
dotnet pack src/Tenon.MSBuild/Tenon.MSBuild.csproj -c $Configuration -o $out -nologo -p:TenonCliDir="$root/$cli"

Get-ChildItem $out | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
