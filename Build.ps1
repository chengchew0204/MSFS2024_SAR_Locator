param(
    [string]$SimConnectDll = ""
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFile = Join-Path $projectDir "Program.cs"
$outputFile = Join-Path $projectDir "MSFS2024SARLocator.exe"
$localManagedSimConnect = Join-Path $projectDir "Microsoft.FlightSimulator.SimConnect.dll"
$localNativeSimConnect = Join-Path $projectDir "SimConnect.dll"

function Test-ValidDllPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    if ($Path.Contains("<") -or $Path.Contains(">")) {
        return $false
    }

    try {
        return (Test-Path -LiteralPath $Path -PathType Leaf)
    }
    catch {
        return $false
    }
}

function Find-SimConnectDll {
    param([string]$ExplicitPath)

    if (Test-ValidDllPath $ExplicitPath) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $relativePath = "SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
    $candidateFiles = New-Object System.Collections.Generic.List[string]

    if ($env:MSFS_SDK) {
        $candidateFiles.Add((Join-Path $env:MSFS_SDK $relativePath))
    }

    if ($env:MSFS2024_SDK) {
        $candidateFiles.Add((Join-Path $env:MSFS2024_SDK $relativePath))
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        if (-not $drive.Root) { continue }

        # Official MSFS 2024 SDK default folder name.
        $candidateFiles.Add((Join-Path $drive.Root ("MSFS 2024 SDK\" + $relativePath)))

        # Keep a compatibility fallback for custom/older folder naming.
        $candidateFiles.Add((Join-Path $drive.Root ("Microsoft Flight Simulator 2024 SDK\" + $relativePath)))
    }

    if ($env:ProgramFiles) {
        $candidateFiles.Add((Join-Path $env:ProgramFiles ("MSFS 2024 SDK\" + $relativePath)))
        $candidateFiles.Add((Join-Path $env:ProgramFiles ("Microsoft Flight Simulator 2024 SDK\" + $relativePath)))
    }

    if (${env:ProgramFiles(x86)}) {
        $candidateFiles.Add((Join-Path ${env:ProgramFiles(x86)} ("MSFS 2024 SDK\" + $relativePath)))
        $candidateFiles.Add((Join-Path ${env:ProgramFiles(x86)} ("Microsoft Flight Simulator 2024 SDK\" + $relativePath)))
    }

    foreach ($candidate in ($candidateFiles | Select-Object -Unique)) {
        if (Test-ValidDllPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    Write-Host ""
    Write-Host "SimConnect DLL was not found automatically." -ForegroundColor Yellow
    Write-Host "Do NOT type the placeholder <MSFS SDK>. Paste a real Windows path." -ForegroundColor Yellow
    Write-Host "The official default is usually:" -ForegroundColor Yellow
    Write-Host "C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" -ForegroundColor Cyan
    Write-Host ""

    while ($true) {
        $manual = Read-Host "Enter the full path to Microsoft.FlightSimulator.SimConnect.dll, or type Q to quit"

        if ($manual -match '^[Qq]$') {
            throw "Build cancelled. Install the MSFS 2024 SDK first, then run Build.ps1 again."
        }

        $manual = $manual.Trim().Trim('"')

        if ($manual.Contains("<") -or $manual.Contains(">")) {
            Write-Host "That is still a placeholder path. Remove <MSFS SDK> and use the actual installed folder." -ForegroundColor Red
            continue
        }

        if (Test-ValidDllPath $manual) {
            return (Resolve-Path -LiteralPath $manual).Path
        }

        Write-Host "File not found at that path. Try again." -ForegroundColor Red
    }
}

function Find-CSharpCompiler {
    $paths = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )

    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    throw ".NET Framework C# compiler was not found. Install .NET Framework 4.8 or build Program.cs in Visual Studio."
}

$simConnect = Find-SimConnectDll -ExplicitPath $SimConnectDll
$managedDir = Split-Path -Parent $simConnect
$libDir = Split-Path -Parent $managedDir
$nativeSimConnect = Join-Path $libDir "SimConnect.dll"

if (-not (Test-Path -LiteralPath $nativeSimConnect -PathType Leaf)) {
    throw "Native SimConnect.dll was not found at: $nativeSimConnect"
}

$csc = Find-CSharpCompiler

Write-Host "Managed SimConnect reference: $simConnect"
Write-Host "Native SimConnect runtime: $nativeSimConnect"
Write-Host "Compiler: $csc"
Write-Host "Building..."

& $csc `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /out:"$outputFile" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$simConnect" `
    "$sourceFile"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

# Copy both the managed wrapper and its native x64 runtime dependency beside the EXE.
Copy-Item -LiteralPath $simConnect -Destination $localManagedSimConnect -Force
Copy-Item -LiteralPath $nativeSimConnect -Destination $localNativeSimConnect -Force

Write-Host ""
Write-Host "Build complete: $outputFile" -ForegroundColor Green
Write-Host "Copied managed wrapper: $localManagedSimConnect" -ForegroundColor Green
Write-Host "Copied native runtime: $localNativeSimConnect" -ForegroundColor Green
Write-Host "Start MSFS 2024 first, enter a Career SAR mission, then run the locator. It will connect automatically."
