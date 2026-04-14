<#
.SYNOPSIS
    Publishes MemoryWatchDog.Wpf for both x64 and x86 architectures side by side.

.DESCRIPTION
    Produces the following layout required for automatic architecture switching:
      publish\x64\MemoryWatchDog.Wpf.exe
      publish\x86\MemoryWatchDog.Wpf.exe

    When the app detects an architecture mismatch with the target process,
    it automatically relaunches the sibling exe with -attach <pid>.

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER OutputRoot
    Root output folder. Default: publish (relative to solution root)

.PARAMETER SelfContained
    If set, publishes as self-contained (bundles the .NET runtime).
    Default: false (framework-dependent)

.EXAMPLE
    .\Publish.ps1
    .\Publish.ps1 -Configuration Debug
    .\Publish.ps1 -SelfContained
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "publish",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$solutionDir = $PSScriptRoot
$project = Join-Path $solutionDir "MemoryWatchDog.Wpf\MemoryWatchDog.Wpf.csproj"

$selfContainedFlag = if ($SelfContained) { "true" } else { "false" }
$runtimes = @("win-x64", "win-x86")

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  MemoryWatchDog - Dual Architecture Publish" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Configuration : $Configuration"
Write-Host "  Output        : $OutputRoot\x64, $OutputRoot\x86"
Write-Host "  SelfContained : $selfContainedFlag"
Write-Host ""

foreach ($rid in $runtimes) {
    $arch = $rid.Replace("win-", "")
    $outputDir = Join-Path $solutionDir $OutputRoot $arch

    Write-Host "Publishing $rid -> $outputDir ..." -ForegroundColor Yellow

    dotnet publish $project `
        --configuration $Configuration `
        --runtime $rid `
        --self-contained $selfContainedFlag `
        --output $outputDir `
        /p:PublishSingleFile=false

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Publish failed for $rid" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "  OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Publish complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  Layout:" -ForegroundColor White
Write-Host "    $OutputRoot\x64\MemoryWatchDog.Wpf.exe"
Write-Host "    $OutputRoot\x86\MemoryWatchDog.Wpf.exe"
Write-Host ""
Write-Host "  The app will auto-relaunch the correct" -ForegroundColor White
Write-Host "  architecture when a mismatch is detected." -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan
