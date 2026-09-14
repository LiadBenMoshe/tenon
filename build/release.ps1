<#
.SYNOPSIS
  Releases a new Tenon version: sets the version, builds and tests, publishes the prebuilt assets,
  packs the four NuGet packages, pushes them to nuget.org, then commits and tags.

.EXAMPLE
  ./build/release.ps1 -Version 0.1.2
  ./build/release.ps1 -Bump patch          # 0.1.1 -> 0.1.2
  ./build/release.ps1 -Bump minor -NoPush  # pack only
#>
param(
    [string]$Version,
    [ValidateSet("patch", "minor", "major")][string]$Bump = "patch",
    [switch]$NoPush,
    [switch]$NoCommit,
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") { $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH" }

# 1. Version
$props = "Directory.Build.props"
$content = Get-Content $props -Raw
$current = [regex]::Match($content, "<VersionPrefix>([^<]+)</VersionPrefix>").Groups[1].Value
if (-not $Version) {
    $v = [Version]$current
    $Version = switch ($Bump) {
        "major" { "$($v.Major + 1).0.0" }
        "minor" { "$($v.Major).$($v.Minor + 1).0" }
        default { "$($v.Major).$($v.Minor).$($v.Build + 1)" }
    }
}
Write-Host "== Tenon $current -> $Version"
$content = $content -replace "<VersionPrefix>[^<]+</VersionPrefix>", "<VersionPrefix>$Version</VersionPrefix>"
Set-Content $props $content -NoNewline -Encoding utf8

# Changelog: move Unreleased into a dated section.
$changelog = "CHANGELOG.md"
if (Test-Path $changelog) {
    $cl = Get-Content $changelog -Raw
    if ($cl -match "## \[Unreleased\]" -and $cl -notmatch "## \[$([regex]::Escape($Version))\]") {
        $cl = $cl -replace "## \[Unreleased\]", "## [Unreleased]`r`n`r`n## [$Version] - $(Get-Date -Format yyyy-MM-dd)"
        Set-Content $changelog $cl -NoNewline -Encoding utf8
    }
}

# 2. Build and test
dotnet build Tenon.sln -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if (-not $SkipTests) {
    dotnet test Tenon.sln -c Release --no-build -nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# 3. Assets and packages
& "$PSScriptRoot/publish-assets.ps1" -Configuration Release
Remove-Item artifacts/packages -Recurse -Force -ErrorAction SilentlyContinue
& "$PSScriptRoot/pack.ps1" -Configuration Release

# 4. Push
if (-not $NoPush) {
    $key = $env:NUGET_API_KEY
    if (-not $key) { $key = [Environment]::GetEnvironmentVariable("NUGET_API_KEY", "User") }
    if (-not $key) { throw "NUGET_API_KEY is not set. Create a key at https://www.nuget.org/account/apikeys and run: [Environment]::SetEnvironmentVariable('NUGET_API_KEY', '<key>', 'User')" }
    foreach ($pkg in Get-ChildItem artifacts/packages/*.nupkg) {
        dotnet nuget push $pkg.FullName --api-key $key --source https://api.nuget.org/v3/index.json --skip-duplicate
        if ($LASTEXITCODE -ne 0) { throw "push failed for $($pkg.Name)" }
    }
}

# 5. Commit and tag
if (-not $NoCommit) {
    # git writes warnings (line endings) to stderr; judge by exit codes.
    $ErrorActionPreference = "Continue"
    git -c core.safecrlf=false add -A 2>&1 | Out-Null
    git commit -q -m "Release $Version" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
    git tag -f "v$Version" 2>&1 | Out-Null
    $ErrorActionPreference = "Stop"
    Write-Host "Committed and tagged v$Version (push with: git push && git push --tags)"
}
Write-Host "== Released Tenon $Version"
