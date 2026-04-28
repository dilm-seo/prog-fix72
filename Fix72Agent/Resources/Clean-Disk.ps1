<#
.SYNOPSIS
  Nettoyage de disque complet pour Windows 10/11.

.DESCRIPTION
  Script portable. Nettoie en deux phases :
   - Phase utilisateur (sans admin) : caches dev (npm, pip, yarn, gradle, nuget,
     cargo registry index, go-build), caches navigateurs (Chrome, Edge, Brave,
     Firefox, Opera, Vivaldi - tous profils), caches IDE (VS Code, Cursor,
     Antigravity), Temp, Explorer, WER user, CrashDumps, corbeille, .cache.
   - Phase systeme (admin)        : SoftwareDistribution\Download, catroot2,
     anciens logs Windows, WER systeme, DISM StartComponentCleanup /ResetBase,
     cleanmgr /sagerun.

  Le script s'auto-eleve en admin si necessaire (sauf -NoElevate).
  Detecte les apps en cours d'execution et saute leurs caches verrouilles.

.PARAMETER DryRun
  Affiche ce qui serait supprime sans rien supprimer.

.PARAMETER Yes
  Ne pose aucune question (utile en mode automatique / planifie).

.PARAMETER SkipAdmin
  Saute la phase admin (utile si tu veux juste nettoyer ton profil).

.PARAMETER NoElevate
  N'essaie pas de relancer en admin. Phase admin sera sautee si pas admin.

.PARAMETER SkipDism
  Saute DISM (qui peut prendre 5-15 minutes).

.PARAMETER SkipCleanmgr
  Saute cleanmgr.

.EXAMPLE
  .\Clean-Disk.ps1
  # Nettoyage interactif standard

.EXAMPLE
  .\Clean-Disk.ps1 -DryRun
  # Apercu sans rien supprimer

.EXAMPLE
  .\Clean-Disk.ps1 -Yes -SkipDism
  # Mode silencieux, sans DISM (plus rapide)
#>

[CmdletBinding()]
param(
  [switch]$DryRun,
  [switch]$Yes,
  [switch]$SkipAdmin,
  [switch]$NoElevate,
  [switch]$SkipDism,
  [switch]$SkipCleanmgr
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
function Test-IsAdmin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  $pri = New-Object Security.Principal.WindowsPrincipal($id)
  return $pri.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-Section { param([string]$Text)
  Write-Host ""
  Write-Host ("=" * 70) -ForegroundColor DarkCyan
  Write-Host $Text -ForegroundColor Cyan
  Write-Host ("=" * 70) -ForegroundColor DarkCyan
}

function Write-Step { param([string]$Text)
  Write-Host "  -> $Text" -ForegroundColor Gray
}

function Get-FolderSize {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return 0 }
  try {
    return (Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
  } catch { return 0 }
}

function Format-Size {
  param([long]$Bytes)
  if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes/1GB) }
  if ($Bytes -ge 1MB) { return "{0:N1} MB" -f ($Bytes/1MB) }
  if ($Bytes -ge 1KB) { return "{0:N0} KB" -f ($Bytes/1KB) }
  return "$Bytes B"
}

function Test-AppRunning {
  param([string[]]$ProcessNames)
  foreach ($n in $ProcessNames) {
    if (Get-Process -Name $n -ErrorAction SilentlyContinue) { return $true }
  }
  return $false
}

# Resultats globaux pour rapport final
$script:results = New-Object System.Collections.ArrayList

function Clean-Path {
  param(
    [string]$Path,
    [string]$Label,
    [switch]$ContentsOnly,    # supprime le contenu, garde le dossier parent
    [string[]]$BlockingProcs  # noms de processus qui bloquent ce nettoyage
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return
  }

  if ($BlockingProcs -and (Test-AppRunning $BlockingProcs)) {
    $procList = $BlockingProcs -join ', '
    Write-Step ("[SKIP] $Label - app en cours: $procList")
    [void]$script:results.Add([PSCustomObject]@{
      Label = $Label; Action = 'skip'; Reason = "running:$procList";
      FreedBytes = 0; LeftBytes = 0; Path = $Path
    })
    return
  }

  $sizeBefore = Get-FolderSize $Path

  if ($DryRun) {
    Write-Step ("[DRY] $Label : " + (Format-Size $sizeBefore) + " -> $Path")
    [void]$script:results.Add([PSCustomObject]@{
      Label = $Label; Action = 'dry'; Reason = '';
      FreedBytes = $sizeBefore; LeftBytes = $sizeBefore; Path = $Path
    })
    return
  }

  try {
    if ($ContentsOnly) {
      Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
      }
    } else {
      Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
  } catch {
    # ignore
  }

  $sizeAfter = Get-FolderSize $Path
  $freed = [math]::Max(0, $sizeBefore - $sizeAfter)

  $leftStr = ""
  if ($sizeAfter -gt 0) { $leftStr = " (reste " + (Format-Size $sizeAfter) + ")" }
  Write-Step ("$Label : libere " + (Format-Size $freed) + $leftStr)
  [void]$script:results.Add([PSCustomObject]@{
    Label = $Label; Action = 'clean'; Reason = '';
    FreedBytes = $freed; LeftBytes = $sizeAfter; Path = $Path
  })
}

# ----------------------------------------------------------------------------
# Auto-elevation
# ----------------------------------------------------------------------------
$isAdmin = Test-IsAdmin
if (-not $isAdmin -and -not $SkipAdmin -and -not $NoElevate) {
  Write-Host "Le script va se relancer en mode administrateur (UAC)..." -ForegroundColor Yellow
  $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
  if ($DryRun) { $argList += '-DryRun' }
  if ($Yes) { $argList += '-Yes' }
  if ($SkipDism) { $argList += '-SkipDism' }
  if ($SkipCleanmgr) { $argList += '-SkipCleanmgr' }
  try {
    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs
    exit 0
  } catch {
    Write-Host "UAC refuse - on continue en mode utilisateur (phase admin sautee)." -ForegroundColor Yellow
    $SkipAdmin = $true
  }
}

# ----------------------------------------------------------------------------
# Bandeau et confirmation
# ----------------------------------------------------------------------------
Write-Section "NETTOYAGE DE DISQUE - Windows"
Write-Host ("Mode admin       : " + $(if (Test-IsAdmin) { 'OUI' } else { 'NON (phase systeme sautee)' }))
Write-Host ("Mode dry-run     : " + $(if ($DryRun) { 'OUI (aucune suppression)' } else { 'NON' }))
Write-Host ("DISM             : " + $(if ($SkipDism) { 'saute' } else { 'inclus' }))
Write-Host ("Cleanmgr         : " + $(if ($SkipCleanmgr) { 'saute' } else { 'inclus' }))

$systemDrive = $env:SystemDrive.TrimEnd(':')
$freeBefore = (Get-PSDrive $systemDrive).Free
Write-Host ("Espace libre     : " + (Format-Size $freeBefore) + " sur $env:SystemDrive") -ForegroundColor Cyan

if (-not $Yes -and -not $DryRun) {
  $confirm = Read-Host "`nLancer le nettoyage ? [O/n]"
  if ($confirm -and $confirm -notmatch '^(o|y|oui|yes)$') {
    Write-Host "Annule." -ForegroundColor Yellow
    exit 0
  }
}

# ----------------------------------------------------------------------------
# PHASE 1 - Caches developpeurs
# ----------------------------------------------------------------------------
Write-Section "[1] Caches developpeurs"

# npm
Clean-Path -Path "$env:LOCALAPPDATA\npm-cache" -Label "npm cache (Local)" -ContentsOnly
Clean-Path -Path "$env:APPDATA\npm-cache" -Label "npm cache (Roaming)" -ContentsOnly

# pnpm
Clean-Path -Path "$env:LOCALAPPDATA\pnpm-cache" -Label "pnpm cache" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\pnpm\store" -Label "pnpm store" -ContentsOnly

# yarn
Clean-Path -Path "$env:LOCALAPPDATA\Yarn\Cache" -Label "Yarn cache" -ContentsOnly
Clean-Path -Path "$env:USERPROFILE\AppData\Local\Yarn\Cache" -Label "Yarn cache (alt)" -ContentsOnly

# pip
Clean-Path -Path "$env:LOCALAPPDATA\pip\Cache" -Label "pip cache" -ContentsOnly

# poetry
Clean-Path -Path "$env:LOCALAPPDATA\pypoetry\Cache" -Label "Poetry cache" -ContentsOnly

# Gradle
Clean-Path -Path "$env:USERPROFILE\.gradle\caches" -Label "Gradle caches" -ContentsOnly -BlockingProcs @('java','gradle','studio64')

# Maven
Clean-Path -Path "$env:USERPROFILE\.m2\repository" -Label "Maven repository" -ContentsOnly

# NuGet
Clean-Path -Path "$env:USERPROFILE\.nuget\packages" -Label "NuGet packages" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\NuGet\v3-cache" -Label "NuGet v3-cache" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\NuGet\Cache" -Label "NuGet cache" -ContentsOnly

# node-gyp
Clean-Path -Path "$env:LOCALAPPDATA\node-gyp" -Label "node-gyp cache" -ContentsOnly

# Cargo (Rust) - SEULEMENT le cache d'index, pas les sources des deps
Clean-Path -Path "$env:USERPROFILE\.cargo\registry\cache" -Label "Cargo registry cache" -ContentsOnly
Clean-Path -Path "$env:USERPROFILE\.cargo\registry\src" -Label "Cargo registry src" -ContentsOnly

# Go
Clean-Path -Path "$env:LOCALAPPDATA\go-build" -Label "Go build cache" -ContentsOnly

# Composer (PHP)
Clean-Path -Path "$env:LOCALAPPDATA\Composer" -Label "Composer cache" -ContentsOnly

# TypeScript
Clean-Path -Path "$env:LOCALAPPDATA\Microsoft\TypeScript" -Label "TypeScript cache" -ContentsOnly

# Generique
Clean-Path -Path "$env:USERPROFILE\.cache" -Label "User .cache" -ContentsOnly

# ----------------------------------------------------------------------------
# PHASE 2 - Caches navigateurs (tous profils)
# ----------------------------------------------------------------------------
Write-Section "[2] Caches navigateurs"

function Clean-Chromium {
  param([string]$Root, [string]$Name, [string[]]$Procs)
  if (-not (Test-Path -LiteralPath $Root)) { return }
  $userData = Join-Path $Root 'User Data'
  if (-not (Test-Path $userData)) { $userData = $Root }
  $profiles = Get-ChildItem -LiteralPath $userData -Directory -Force -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -eq 'Default' -or $_.Name -like 'Profile *' -or $_.Name -like 'Guest Profile' }
  foreach ($p in $profiles) {
    foreach ($sub in @('Cache','Code Cache','GPUCache','ShaderCache','Service Worker\CacheStorage','DawnGraphiteCache','DawnWebGPUCache')) {
      $target = Join-Path $p.FullName $sub
      Clean-Path -Path $target -Label "$Name [$($p.Name)] $sub" -ContentsOnly -BlockingProcs $Procs
    }
  }
  # caches partages
  Clean-Path -Path (Join-Path $userData 'ShaderCache') -Label "$Name ShaderCache (global)" -ContentsOnly -BlockingProcs $Procs
  Clean-Path -Path (Join-Path $userData 'GrShaderCache') -Label "$Name GrShaderCache (global)" -ContentsOnly -BlockingProcs $Procs
}

Clean-Chromium -Root "$env:LOCALAPPDATA\Google\Chrome" -Name "Chrome" -Procs @('chrome')
Clean-Chromium -Root "$env:LOCALAPPDATA\Microsoft\Edge" -Name "Edge" -Procs @('msedge')
Clean-Chromium -Root "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser" -Name "Brave" -Procs @('brave')
Clean-Chromium -Root "$env:LOCALAPPDATA\Vivaldi" -Name "Vivaldi" -Procs @('vivaldi')
Clean-Chromium -Root "$env:APPDATA\Opera Software\Opera Stable" -Name "Opera" -Procs @('opera')
Clean-Chromium -Root "$env:LOCALAPPDATA\Thorium" -Name "Thorium" -Procs @('thorium')

# Firefox - structure differente
$ffProfiles = "$env:APPDATA\Mozilla\Firefox\Profiles"
$ffCacheRoot = "$env:LOCALAPPDATA\Mozilla\Firefox\Profiles"
if (Test-Path $ffCacheRoot) {
  Get-ChildItem -LiteralPath $ffCacheRoot -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
    foreach ($sub in @('cache2','startupCache','jumpListCache','OfflineCache','thumbnails','shader-cache')) {
      Clean-Path -Path (Join-Path $_.FullName $sub) -Label "Firefox [$($_.Name)] $sub" -ContentsOnly -BlockingProcs @('firefox')
    }
  }
}

# ----------------------------------------------------------------------------
# PHASE 3 - Caches IDE / Apps Electron
# ----------------------------------------------------------------------------
Write-Section "[3] Caches IDE et apps Electron"

function Clean-Electron {
  param([string]$Root, [string]$Name, [string[]]$Procs)
  if (-not (Test-Path -LiteralPath $Root)) { return }
  foreach ($sub in @('Cache','Code Cache','GPUCache','ShaderCache','CachedData','CachedExtensions','CachedExtensionVSIXs','Crashpad','logs','DawnGraphiteCache','DawnWebGPUCache','Service Worker\CacheStorage')) {
    $target = Join-Path $Root $sub
    Clean-Path -Path $target -Label "$Name $sub" -ContentsOnly -BlockingProcs $Procs
  }
}

Clean-Electron -Root "$env:APPDATA\Code"           -Name "VS Code"      -Procs @('Code')
Clean-Electron -Root "$env:APPDATA\Code - Insiders" -Name "VSCode Ins."  -Procs @('Code - Insiders')
Clean-Electron -Root "$env:APPDATA\Cursor"         -Name "Cursor"       -Procs @('Cursor')
Clean-Electron -Root "$env:APPDATA\Windsurf"       -Name "Windsurf"     -Procs @('Windsurf')
Clean-Electron -Root "$env:APPDATA\Antigravity"    -Name "Antigravity"  -Procs @('Antigravity')
Clean-Electron -Root "$env:APPDATA\Slack"          -Name "Slack"        -Procs @('slack')
Clean-Electron -Root "$env:APPDATA\discord"        -Name "Discord"      -Procs @('Discord')
Clean-Electron -Root "$env:APPDATA\Microsoft\Teams" -Name "Teams classic" -Procs @('Teams')
Clean-Electron -Root "$env:APPDATA\Spotify"        -Name "Spotify"      -Procs @('Spotify')
Clean-Electron -Root "$env:APPDATA\Notion"         -Name "Notion"       -Procs @('Notion')
Clean-Electron -Root "$env:APPDATA\obsidian"       -Name "Obsidian"     -Procs @('Obsidian')
Clean-Electron -Root "$env:APPDATA\GitHub Desktop" -Name "GitHub Desktop" -Procs @('GitHubDesktop')
Clean-Electron -Root "$env:APPDATA\Claude"         -Name "Claude"       -Procs @('Claude','claude')

# JetBrains - log et caches
if (Test-Path "$env:LOCALAPPDATA\JetBrains") {
  Get-ChildItem "$env:LOCALAPPDATA\JetBrains" -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
    Clean-Path -Path (Join-Path $_.FullName 'log') -Label "JetBrains [$($_.Name)] log" -ContentsOnly
    Clean-Path -Path (Join-Path $_.FullName 'caches') -Label "JetBrains [$($_.Name)] caches" -ContentsOnly -BlockingProcs @('idea64','pycharm64','webstorm64','rider64','clion64','phpstorm64','rubymine64','goland64','datagrip64','studio64')
  }
}

# ----------------------------------------------------------------------------
# PHASE 4 - Temp et caches Windows utilisateur
# ----------------------------------------------------------------------------
Write-Section "[4] Temp et caches Windows utilisateur"

Clean-Path -Path "$env:LOCALAPPDATA\Temp" -Label "User Temp" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\Microsoft\Windows\INetCache" -Label "IE/Edge INetCache" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\Microsoft\Windows\WebCache" -Label "Windows WebCache" -ContentsOnly -BlockingProcs @('explorer')
Clean-Path -Path "$env:LOCALAPPDATA\Microsoft\Windows\Explorer" -Label "Explorer thumb cache" -ContentsOnly -BlockingProcs @('explorer')
Clean-Path -Path "$env:LOCALAPPDATA\Microsoft\Windows\WER" -Label "WER user" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\CrashDumps" -Label "CrashDumps" -ContentsOnly
Clean-Path -Path "$env:LOCALAPPDATA\D3DSCache" -Label "D3D shader cache" -ContentsOnly

# ----------------------------------------------------------------------------
# PHASE 5 - Corbeille
# ----------------------------------------------------------------------------
Write-Section "[5] Corbeille"
if ($DryRun) {
  Write-Step "[DRY] Corbeille (non chiffree en preview)"
} else {
  try {
    Clear-RecycleBin -Force -ErrorAction Stop
    Write-Step "Corbeille videe"
  } catch {
    Write-Step "Corbeille deja vide ou inaccessible"
  }
}

# ----------------------------------------------------------------------------
# PHASE 6 - Systeme (admin requis)
# ----------------------------------------------------------------------------
if ((Test-IsAdmin) -and -not $SkipAdmin) {
  Write-Section "[6] Nettoyage systeme (admin)"

  # Windows Update download cache
  Write-Step "Arret services Windows Update..."
  if (-not $DryRun) {
    Stop-Service wuauserv -Force -ErrorAction SilentlyContinue
    Stop-Service bits -Force -ErrorAction SilentlyContinue
    Stop-Service UsoSvc -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
  }

  Clean-Path -Path "C:\Windows\SoftwareDistribution\Download" -Label "SoftwareDistribution\Download" -ContentsOnly
  Clean-Path -Path "C:\Windows\System32\catroot2" -Label "catroot2" -ContentsOnly

  if (-not $DryRun) {
    Start-Service bits -ErrorAction SilentlyContinue
    Start-Service wuauserv -ErrorAction SilentlyContinue
    Write-Step "Services redemarres"
  }

  # Windows Temp
  Clean-Path -Path "C:\Windows\Temp" -Label "Windows Temp" -ContentsOnly

  # Anciens logs Windows (>7 jours)
  if (-not $DryRun) {
    $logsBefore = Get-FolderSize "C:\Windows\Logs"
    Get-ChildItem -LiteralPath "C:\Windows\Logs" -Recurse -Force -File -ErrorAction SilentlyContinue |
      Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
      Remove-Item -Force -ErrorAction SilentlyContinue
    $logsAfter = Get-FolderSize "C:\Windows\Logs"
    Write-Step ("Anciens logs Windows : libere " + (Format-Size ($logsBefore - $logsAfter)))
    [void]$script:results.Add([PSCustomObject]@{
      Label = 'Windows old logs (>7d)'; Action='clean'; Reason='';
      FreedBytes = ($logsBefore - $logsAfter); LeftBytes = $logsAfter; Path = 'C:\Windows\Logs'
    })
  }

  # Panther (>30 jours)
  if (-not $DryRun -and (Test-Path 'C:\Windows\Panther')) {
    Get-ChildItem -LiteralPath "C:\Windows\Panther" -Recurse -Force -File -ErrorAction SilentlyContinue |
      Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
      Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Step "Anciens fichiers Panther nettoyes"
  }

  # WER systeme
  Clean-Path -Path "C:\ProgramData\Microsoft\Windows\WER\ReportArchive" -Label "WER ReportArchive" -ContentsOnly
  Clean-Path -Path "C:\ProgramData\Microsoft\Windows\WER\ReportQueue" -Label "WER ReportQueue" -ContentsOnly
  Clean-Path -Path "C:\ProgramData\Microsoft\Windows\WER\Temp" -Label "WER Temp" -ContentsOnly

  # Prefetch (regenere automatiquement, ralentit le 1er boot)
  Clean-Path -Path "C:\Windows\Prefetch" -Label "Windows Prefetch" -ContentsOnly

  # Delivery Optimization
  Clean-Path -Path "C:\Windows\SoftwareDistribution\DeliveryOptimization" -Label "Delivery Optimization" -ContentsOnly

  # ----- DISM -----
  if (-not $SkipDism -and -not $DryRun) {
    Write-Section "[7] DISM /StartComponentCleanup /ResetBase"
    Write-Host "Cette etape peut prendre 5-15 minutes..." -ForegroundColor Yellow
    & Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase
    if ($LASTEXITCODE -eq 0) {
      Write-Step "DISM termine avec succes"
    } else {
      Write-Step "DISM exit code: $LASTEXITCODE"
    }
  }

  # ----- Cleanmgr -----
  if (-not $SkipCleanmgr -and -not $DryRun) {
    Write-Section "[8] Cleanmgr (Nettoyage de disque Windows)"
    $tag = '0099'
    $keys = @(
      'Active Setup Temp Folders','BranchCache','D3D Shader Cache',
      'Delivery Optimization Files','Diagnostic Data Viewer database files',
      'Downloaded Program Files','Internet Cache Files','Language Pack',
      'Old ChkDsk Files','Previous Installations','Recycle Bin',
      'RetailDemo Offline Content','Service Pack Cleanup','Setup Log Files',
      'System error memory dump files','System error minidump files',
      'Temporary Files','Temporary Setup Files','Temporary Sync Files',
      'Thumbnail Cache','Update Cleanup','Upgrade Discarded Files',
      'User file versions','Windows Defender','Windows Error Reporting Files',
      'Windows ESD installation files','Windows Reset Log Files',
      'Windows Upgrade Log Files'
    )
    $base = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches'
    foreach ($k in $keys) {
      $p = Join-Path $base $k
      if (Test-Path $p) {
        New-ItemProperty -Path $p -Name "StateFlags$tag" -PropertyType DWord -Value 2 -Force -ErrorAction SilentlyContinue | Out-Null
      }
    }
    Start-Process -FilePath "$env:WINDIR\System32\cleanmgr.exe" -ArgumentList "/sagerun:$tag" -Wait -NoNewWindow
    Write-Step "Cleanmgr termine"
  }

} elseif (-not $SkipAdmin) {
  Write-Section "[6] Phase systeme SAUTEE (pas en mode admin)"
  Write-Host "  Pour beneficier du nettoyage systeme (~1-10 GB), relance en admin." -ForegroundColor Yellow
}

# ----------------------------------------------------------------------------
# Rapport final
# ----------------------------------------------------------------------------
Write-Section "RAPPORT FINAL"

$freeAfter = (Get-PSDrive $systemDrive).Free
$gain = $freeAfter - $freeBefore
$totalCleaned = ($script:results | Where-Object { $_.Action -eq 'clean' } | Measure-Object FreedBytes -Sum).Sum
$totalDry = ($script:results | Where-Object { $_.Action -eq 'dry' } | Measure-Object FreedBytes -Sum).Sum

Write-Host ("Espace libre avant : " + (Format-Size $freeBefore))
Write-Host ("Espace libre apres : " + (Format-Size $freeAfter)) -ForegroundColor Cyan
Write-Host ("Gain disque        : " + (Format-Size $gain)) -ForegroundColor Green

if ($DryRun) {
  Write-Host ("Total recuperable (dry-run) : " + (Format-Size $totalDry)) -ForegroundColor Yellow
  Write-Host "Relance sans -DryRun pour effectuer le nettoyage." -ForegroundColor Yellow
} else {
  Write-Host ("Total libere par le script  : " + (Format-Size $totalCleaned)) -ForegroundColor Green
}

# Top 10 cibles
Write-Host "`nTop 10 plus gros postes :" -ForegroundColor Cyan
$script:results | Sort-Object FreedBytes -Descending | Select-Object -First 10 |
  ForEach-Object {
    "{0,12}  {1}" -f (Format-Size $_.FreedBytes), $_.Label
  } | Write-Host

# Cibles sautees
$skipped = $script:results | Where-Object { $_.Action -eq 'skip' }
if ($skipped) {
  Write-Host "`nCibles sautees (apps en cours) :" -ForegroundColor Yellow
  $skipped | ForEach-Object { "  - $($_.Label) [$($_.Reason)]" } | Write-Host
  Write-Host "  (ferme ces apps et relance pour gagner plus)" -ForegroundColor Gray
}

if (-not $Yes) {
  Write-Host ""
  Read-Host "Appuie sur Entree pour fermer"
}
