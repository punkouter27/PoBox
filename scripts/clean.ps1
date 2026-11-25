<#
.SYNOPSIS
  Deletes what the editor and the tests generate, so a clone-sized tree is what is left.

.DESCRIPTION
  Everything here is in .gitignore and is rebuilt on demand: the Unity Library-adjacent build output (Build/,
  Builds/, Logs/, Temp/), the stray WebGL burst artifact the browser build leaves at the repo root (Data/Plugins/),
  MuJoCo's own log, and the Test Runner's leftover InitTestScene files under Assets/. Nothing tracked is touched:
  the script prints each path with its size before deleting it, and refuses any path git knows about.

  It does not touch Training/checkpoints (the training data; ../PoBox_quarantine_<date>/ is where abandoned runs go,
  see AGENTS.md) and it does not touch the Unity Library (deleting it reimports the whole project).

.EXAMPLE
  ./scripts/clean.ps1 -DryRun      # what would go, and how much
  ./scripts/clean.ps1              # delete it
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $targets = @('Build', 'Builds', 'Logs', 'Temp', 'Data/Plugins', 'MUJOCO_LOG.TXT')
    $targets += Get-ChildItem 'Assets' -Filter 'InitTestScene*.unity*' -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') }

    $freed = 0
    foreach ($t in $targets) {
        if (-not (Test-Path $t)) { continue }
        $tracked = git ls-files -- $t
        if ($tracked) { Write-Host "skip (tracked by git): $t" -ForegroundColor Yellow; continue }
        $size = if (Test-Path $t -PathType Leaf) { (Get-Item $t).Length }
                else { (Get-ChildItem $t -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum }
        if ($null -eq $size) { $size = 0 }
        $freed += $size
        $mb = [math]::Round($size / 1MB, 1)
        if ($DryRun) { Write-Host "would delete $t ($mb MB)" }
        else { Remove-Item $t -Recurse -Force; Write-Host "deleted $t ($mb MB)" }
    }
    $verb = if ($DryRun) { 'would free' } else { 'freed' }
    Write-Host "$verb $([math]::Round($freed / 1MB, 1)) MB."
}
finally { Pop-Location }
