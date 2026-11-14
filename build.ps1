#requires -Version 5.1
<#
    Builds, tests and packages KSP Mesh Mixer.

      .\build.ps1
      .\build.ps1 -Ksp2Dir "D:\Steam\steamapps\common\Kerbal Space Program 2"
      .\build.ps1 -SkipTests -SkipPackage

    Output: dist\mods\KSPMeshMixer\  (swinfo.json + KSPMeshMixer.dll)
#>
param(
    [string]$Ksp2Dir = "",
    [switch]$SkipTests,
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$version = "0.1.0"

if (-not $SkipTests) {
    Write-Host ""
    Write-Host "==> Running tests" -ForegroundColor Cyan
    Push-Location (Join-Path $root "tests\KSPMeshMixer.Tests")
    try {
        $testArgs = @("run", "-c", "Release")
        if ($Ksp2Dir -ne "") {
            $testArgs += "-p:Ksp2Dir=$Ksp2Dir"
        }

        dotnet @testArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

Push-Location $root
try {
    Write-Host ""
    Write-Host "==> Building KSP Mesh Mixer" -ForegroundColor Cyan
    $buildArgs = @("build", "KSPMeshMixer.csproj", "-c", "Release", "-v", "m")
    if ($Ksp2Dir -ne "") {
        $buildArgs += "-p:Ksp2Dir=$Ksp2Dir"
    }

    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

if (-not $SkipPackage) {
    Write-Host ""
    Write-Host "==> Packaging mod zip" -ForegroundColor Cyan
    $zip = Join-Path $dist "KSPMeshMixer-$version.zip"
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }

    Compress-Archive -Path (Join-Path $dist "mods\KSPMeshMixer") -DestinationPath $zip
    Write-Host "Created $zip"
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Mod: $(Join-Path $dist 'mods\KSPMeshMixer')"
Write-Host "  Install with: .\install.ps1"
