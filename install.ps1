#requires -Version 5.1
<#
    Installs the built KSP Mesh Mixer into the game's mods folder.

      .\install.ps1
      .\install.ps1 -Ksp2Dir "D:\Steam\steamapps\common\Kerbal Space Program 2"

    Build first: .\build.ps1
#>
param(
    [string]$Ksp2Dir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root "dist\mods\KSPMeshMixer"
$folderName = "KSPMeshMixer"

if ($Ksp2Dir -eq "") {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program 2",
        "C:\Program Files\Steam\steamapps\common\Kerbal Space Program 2",
        "D:\SteamLibrary\steamapps\common\Kerbal Space Program 2",
        "E:\SteamLibrary\steamapps\common\Kerbal Space Program 2"
    )
    $Ksp2Dir = $candidates | Where-Object { Test-Path (Join-Path $_ "KSP2_x64.exe") } | Select-Object -First 1
}

if (-not $Ksp2Dir -or -not (Test-Path (Join-Path $Ksp2Dir "KSP2_x64.exe"))) {
    throw "Could not find a KSP 2 install. Pass -Ksp2Dir `"<path to Kerbal Space Program 2>`"."
}

if (-not (Test-Path $source)) {
    throw "No build output at $source. Run .\build.ps1 first."
}

$managed = Join-Path $Ksp2Dir "KSP2_x64_Data\Managed"
if (-not (Test-Path (Join-Path $managed "SpaceWarp2.dll"))) {
    Write-Host "SpaceWarp2.dll is missing from $managed - this build of KSP 2 has no mod loader." -ForegroundColor Yellow
}

$modsRoot = Join-Path $Ksp2Dir "mods"
$target = Join-Path $modsRoot $folderName

if (Get-Process -Name KSP2_x64 -ErrorAction SilentlyContinue) {
    Write-Host "KSP 2 is running: the mod dll is locked until you quit the game." -ForegroundColor Yellow
}

$failed = $false

# The settings file is user data: keep it across upgrades.
$preserved = @()
if (Test-Path $target) {
    $existing = Join-Path $target "KSPMeshMixer-config.json"
    if (Test-Path $existing) {
        $backup = Join-Path $env:TEMP ("kspmeshmixer-" + [guid]::NewGuid().ToString("N"))
        Copy-Item $existing $backup
        $preserved += [pscustomobject]@{ Name = "KSPMeshMixer-config.json"; Backup = $backup }
    }
}

try {
    if (-not (Test-Path $modsRoot)) {
        New-Item -ItemType Directory -Force -Path $modsRoot | Out-Null
    }

    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
    }

    Copy-Item $source $target -Recurse

    foreach ($item in $preserved) {
        Copy-Item $item.Backup (Join-Path $target $item.Name) -Force
        Remove-Item $item.Backup -Force
    }

    Write-Host "Mod installed -> $target" -ForegroundColor Green
    if ($preserved.Count -gt 0) {
        Write-Host ("Kept existing " + (($preserved | ForEach-Object { $_.Name }) -join ", "))
    }
}
catch {
    $failed = $true
    Write-Host "Mod NOT installed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Quit KSP 2 and run this script again." -ForegroundColor Yellow
    foreach ($item in $preserved) {
        if (Test-Path $item.Backup) {
            Remove-Item $item.Backup -Force
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "Settings live in: $(Join-Path $target 'KSPMeshMixer-config.json')" -ForegroundColor Green
Write-Host "Log lines are tagged [KSP Mesh Mixer] in Ksp2.log." -ForegroundColor Green
