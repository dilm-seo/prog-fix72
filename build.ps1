# Fix72 Agent - Build complet
# Usage : .\build.ps1
#
# Etapes :
#   1. Compile le projet C# en mode Release, x64, self-contained, single-file
#   2. Si Inno Setup est installe : genere l'installateur dans .\dist\
#
# Prerequis :
#   - .NET 8 SDK : https://dotnet.microsoft.com/download/dotnet/8.0
#   - Inno Setup 6 (optionnel) : https://jrsoftware.org/isdl.php

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host ""
Write-Host "=== Fix72 Agent - Build ===" -ForegroundColor Cyan
Write-Host ""

# 1. Verification du SDK .NET
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}
$dotnetVersion = & $dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "X .NET SDK non trouve. Installez .NET 8 SDK : https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}
Write-Host "  .NET SDK $dotnetVersion detecte" -ForegroundColor Green

# 2. Build + publish
Write-Host ""
Write-Host "=== Compilation (Release, x64, self-contained) ===" -ForegroundColor Cyan
Push-Location "$root\Fix72Agent"
try {
    & $dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o "bin\Release\net8.0-windows\win-x64\publish"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "X Echec de la compilation." -ForegroundColor Red
        exit 1
    }

    $exe = "bin\Release\net8.0-windows\win-x64\publish\Fix72Agent.exe"
    if (Test-Path $exe) {
        $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host ""
        Write-Host ("  Binaire genere : Fix72Agent\{0} ({1} Mo)" -f $exe, $size) -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

# 3. Installateur Inno Setup (optionnel)
Write-Host ""
Write-Host "=== Installateur (Inno Setup) ===" -ForegroundColor Cyan
$issPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $issPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "$root\Fix72Agent\Setup\Fix72Agent.iss"
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "  Installateur cree dans : $root\dist\" -ForegroundColor Green
    } else {
        Write-Host "X Echec de l'installateur." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  Inno Setup non detecte - installateur non genere." -ForegroundColor Yellow
    Write-Host "    Telechargez : https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "    Le binaire compile reste disponible dans Fix72Agent\bin\Release\..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Termine ===" -ForegroundColor Cyan
