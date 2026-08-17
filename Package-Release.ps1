param(
    [string]$Version = "1.0.0",
    [string]$SimConnectDll = ""
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $projectDir "Build.ps1"
$distDir = Join-Path $projectDir "dist"
$packageName = "MSFS-SAR-Locator-v$Version"
$stageDir = Join-Path $distDir $packageName
$zipPath = Join-Path $distDir "$packageName.zip"

$payload = @(
    "MSFS2024SARLocator.exe",
    "Microsoft.FlightSimulator.SimConnect.dll",
    "SimConnect.dll"
)

Write-Host "Packaging $packageName"
Write-Host ""

if ($SimConnectDll) {
    & $buildScript -SimConnectDll $SimConnectDll
}
else {
    & $buildScript
}

if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    throw "Build failed. Release package was not created."
}

foreach ($file in $payload) {
    $source = Join-Path $projectDir $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Expected build output is missing: $source"
    }
}

# Rebuild the staging folder from scratch so a stale file can never ship.
if (Test-Path -LiteralPath $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

foreach ($file in $payload) {
    Copy-Item -LiteralPath (Join-Path $projectDir $file) -Destination (Join-Path $stageDir $file) -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Archive the folder itself so extracting never scatters loose files.
Compress-Archive -Path $stageDir -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Release package: $zipPath" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
Write-Host "Contents: $($payload -join ', ')"
Write-Host "Upload this zip as a GitHub release asset for v$Version."
