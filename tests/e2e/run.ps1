<#
.SYNOPSIS
  End-to-end check of a Tenon Setup.exe: silent install, verification, silent uninstall, clean-up check.
  Runs as the current user (per-user install); add -PerMachine from an elevated shell to test per-machine.
#>
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [switch]$PerMachine
)
$ErrorActionPreference = "Stop"
$failures = 0
function Check($name, $ok) { if ($ok) { Write-Host "  [ok]   $name" } else { Write-Host "  [FAIL] $name"; $script:failures++ } }

# Read the manifest through tenon inspect-like logic: the payload is the same for every build.
Add-Type -AssemblyName System.IO.Compression
$log = Join-Path $env:TEMP "tenon-e2e.log"
$scopeArg = if ($PerMachine) { "ALLUSERS=1" } else { "ALLUSERS=0" }

Write-Host "Installing $Setup ..."
$p = Start-Process $Setup -ArgumentList "/quiet /log `"$log`" $scopeArg" -Wait -PassThru
Check "install exit code 0" ($p.ExitCode -eq 0)

$installLog = Get-Content $log
$installDir = ($installLog | Select-String -Pattern 'INSTALLDIR="([^"]+)\\"' | Select-Object -First 1).Matches[0].Groups[1].Value
Write-Host "  install dir: $installDir"
Check "install folder exists" (Test-Path $installDir)
Check "at least one exe installed" ((Get-ChildItem $installDir -Filter *.exe -Recurse | Measure-Object).Count -gt 0)

$arp = Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall","HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -like "Tenon_*" -and (Get-ItemProperty $_.PSPath).InstallLocation -eq $installDir }
Check "Add/Remove Programs entry registered" ($arp -ne $null)
$cached = (Get-ItemProperty $arp.PSPath).TenonSetupPath
Check "setup cached for uninstall" (Test-Path $cached)
$startMenu = Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs","$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue |
    Where-Object { (New-Object -ComObject WScript.Shell).CreateShortcut($_.FullName).TargetPath -like "$installDir*" }
Check "Start Menu shortcut points into install folder" ($startMenu -ne $null)

Write-Host "Uninstalling ..."
$p = Start-Process $cached -ArgumentList "/uninstall /quiet /log `"$log.un.log`"" -Wait -PassThru
Check "uninstall exit code 0" ($p.ExitCode -eq 0)
Start-Sleep -Seconds 8
Check "install folder removed" (-not (Test-Path $installDir))
Check "Add/Remove Programs entry removed" (-not (Test-Path $arp.PSPath))
Check "package cache removed" (-not (Test-Path (Split-Path $cached)))
Check "shortcut removed" (-not (Test-Path $startMenu.FullName))

if ($failures -gt 0) { Write-Host "$failures check(s) failed"; exit 1 }
Write-Host "All checks passed"
