<#
.SYNOPSIS
    Publishes MemoryWatchDog Launcher + WPF app for both x64 and x86 architectures.

.DESCRIPTION
    Produces the following layout:
      publish\MemoryWatchDog.Launcher.exe   (AnyCPU launcher, user entry point)
      publish\x64\MemoryWatchDog_x64.exe    (x64 build)
      publish\x86\MemoryWatchDog_x86.exe    (x86 build)

    The launcher detects the target process architecture and starts the correct build.

    Run from solution root:
      .\Publish.ps1
      .\Publish.ps1 -Configuration Debug
      .\Publish.ps1 -SelfContained

    Or right-click -> Run with PowerShell from Windows Explorer.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "publish",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$solutionDir = $PSScriptRoot
$wpfProject = Join-Path $solutionDir "MemoryWatchDog.Wpf\MemoryWatchDog.Wpf.csproj"
$launcherProject = Join-Path $solutionDir "MemoryWatchDog.Launcher\MemoryWatchDog.Launcher.csproj"

if ($SelfContained) { $selfContainedFlag = "true" } else { $selfContainedFlag = "false" }
$runtimes = @("win-x64", "win-x86")

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  MemoryWatchDog - Full Publish" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  SolutionDir   : $solutionDir"
Write-Host "  Configuration : $Configuration"
Write-Host "  Output        : $OutputRoot"
Write-Host "  SelfContained : $selfContainedFlag"
Write-Host ""

# 1. Publish the Launcher (AnyCPU)
$launcherOutput = Join-Path $solutionDir $OutputRoot
Write-Host "Publishing Launcher -> $launcherOutput ..." -ForegroundColor Yellow

dotnet publish $launcherProject --configuration $Configuration --output $launcherOutput /p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Launcher publish failed" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit $LASTEXITCODE
}

Write-Host "  OK" -ForegroundColor Green

# 2. Publish WPF app for each architecture
foreach ($rid in $runtimes) {
    $arch = $rid.Replace("win-", "")
    $outputDir = Join-Path (Join-Path $solutionDir $OutputRoot) $arch

    Write-Host "Publishing WPF $rid -> $outputDir ..." -ForegroundColor Yellow

    dotnet publish $wpfProject --configuration $Configuration --runtime $rid --self-contained $selfContainedFlag --output $outputDir /p:PublishSingleFile=false /p:_IsPublishBothInner=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Publish failed for $rid" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit $LASTEXITCODE
    }

    # Copy exe with architecture suffix for easy identification
    $srcExe = Join-Path $outputDir "MemoryWatchDog.Wpf.exe"
    $destExe = Join-Path $outputDir "MemoryWatchDog_$arch.exe"
    if (Test-Path $srcExe) {
        Copy-Item $srcExe $destExe -Force
    }

    Write-Host "  OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Publish complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  Layout:" -ForegroundColor White
Write-Host "    $OutputRoot\MemoryWatchDog.Launcher.exe  <- start this"
Write-Host "    $OutputRoot\x64\MemoryWatchDog_x64.exe"
Write-Host "    $OutputRoot\x86\MemoryWatchDog_x86.exe"
Write-Host ""
Write-Host "  The launcher selects the correct build" -ForegroundColor White
Write-Host "  based on the target process architecture." -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "Press Enter to exit"
