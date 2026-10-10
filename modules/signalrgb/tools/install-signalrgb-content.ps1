<#
.SYNOPSIS
  Installs active effects/ and plugins/ from this module into SignalRGB's user folders.
.EXAMPLE
  .\install-signalrgb-content.ps1            # one-shot install
  .\install-signalrgb-content.ps1 -Watch     # re-install whenever a file changes
  .\install-signalrgb-content.ps1 -Effects   # only effects
  .\install-signalrgb-content.ps1 -Plugins   # only plugins
#>
param(
  [switch]$Watch,
  [switch]$Effects,
  [switch]$Plugins
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# SignalRGB reads from Documents\WhirlwindFX; Documents may be redirected to OneDrive.
$docs = [Environment]::GetFolderPath('MyDocuments')
$base = Join-Path $docs 'WhirlwindFX'

if (-not $Effects -and -not $Plugins) { $Effects = $true; $Plugins = $true }

$jobs = @()
if ($Effects) { $jobs += @{ src = Join-Path $repo 'effects'; dst = Join-Path $base 'Effects'; filter = @('*.html','*.png') } }
if ($Plugins) { $jobs += @{ src = Join-Path $repo 'plugins'; dst = Join-Path $base 'Plugins'; filter = @('*.js') } }

function Sync-Once {
  foreach ($j in $jobs) {
    if (-not (Test-Path $j.dst)) { New-Item -ItemType Directory -Force -Path $j.dst | Out-Null }
    $files = Get-ChildItem -Path $j.src -Include $j.filter -File -Recurse
    foreach ($f in $files) {
      $target = Join-Path $j.dst $f.Name
      $changed = -not (Test-Path $target) -or ((Get-Item $target).LastWriteTimeUtc -lt $f.LastWriteTimeUtc)
      if ($changed) {
        Copy-Item -Path $f.FullName -Destination $target -Force
        Write-Host ("[{0}] {1} -> {2}" -f (Get-Date -Format 'HH:mm:ss'), $f.Name, $j.dst)
      }
    }
  }
}

Write-Host "SignalRGB user folder: $base"
Sync-Once

if ($Watch) {
  Write-Host "Watching for changes (Ctrl+C to stop)..."
  $watchers = @()
  foreach ($j in $jobs) {
    $w = New-Object System.IO.FileSystemWatcher $j.src
    $w.IncludeSubdirectories = $true
    $w.EnableRaisingEvents = $true
    $watchers += $w
  }
  try {
    while ($true) {
      Start-Sleep -Milliseconds 500
      Sync-Once
    }
  } finally {
    $watchers | ForEach-Object { $_.Dispose() }
  }
}
