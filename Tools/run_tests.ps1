<#
.SYNOPSIS
  Runs every PoBox play-mode fixture, one after another, in the open Unity editor and prints one table.

.DESCRIPTION
  The Unity CLI's run_tests takes one fixture per --filter (a semicolon list matches nothing), so this runs each
  in turn and polls test_status. Before each run it stops play mode if a run left the editor in it (which silently
  blocks compiles and the next run). Exit code 0 when every fixture passed, 1 otherwise.

  The editor must be open on this project (unity open .). Results as JSON: Temp/pobox-tests.json.

.EXAMPLE
  ./Tools/run_tests.ps1                              # every PoBox fixture
  ./Tools/run_tests.ps1 -Only MjGateTests,MjParityTests
  ./Tools/run_tests.ps1 -Plugin                      # then the MuJoCo plugin's own play-mode tests, to prove nothing is left behind
#>
param(
    [string[]]$Only = @(),
    [switch]$Plugin,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $fixtures = Get-ChildItem (Join-Path $root 'Assets/Scripts/Tests') -Filter '*Tests.cs' |
        ForEach-Object { 'PoBox.Tests.' + $_.BaseName } | Sort-Object
    if ($Only.Count -gt 0) {
        $wanted = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $fixtures = $fixtures | Where-Object { $wanted -contains ($_ -replace '^PoBox\.Tests\.', '') -or $wanted -contains $_ }
    }
    if ($Plugin) { $fixtures = @($fixtures) + 'Mujoco.' }
    if (-not $fixtures) { throw 'No fixture matches.' }

    function Invoke-Unity([string[]]$a) {
        $out = & unity command @a --result-only 2>&1 | Out-String
        try { return $out | ConvertFrom-Json } catch { return $null }
    }

    function Wait-Ready([int]$seconds = 180, [switch]$AllowPlaying) {
        $until = (Get-Date).AddSeconds($seconds)
        while ((Get-Date) -lt $until) {
            $s = Invoke-Unity @('editor_status')
            $okStatus = $s -and ($s.status -eq 'ready' -or ($AllowPlaying -and $s.status -eq 'playing'))
            if ($okStatus -and -not $s.compiling -and -not $s.domainReloadInProgress) { return $s }
            Start-Sleep -Seconds 2
        }
        throw 'The Unity editor is not answering. Is it open on this project (unity open .)?'
    }

    function Stop-Play {
        $s = Wait-Ready -AllowPlaying
        if ($s.playMode -ne 'stopped') {
            Write-Host '  (the editor was left in play mode: stopping it)' -ForegroundColor DarkYellow
            Invoke-Unity @('editor_stop') | Out-Null
            Start-Sleep -Seconds 3
            Wait-Ready | Out-Null
        }
    }

    $rows = @()
    $all = @()
    foreach ($f in $fixtures) {
        Stop-Play
        Write-Host "Running $f ..."
        $started = Get-Date
        Invoke-Unity @('run_tests', '--mode', 'PlayMode', '--filter', $f, '--async_tests', 'true') | Out-Null
        Start-Sleep -Seconds 3
        $st = $null
        while (((Get-Date) - $started).TotalMinutes -lt $TimeoutMinutes) {
            $st = Invoke-Unity @('test_status')     # null while the editor reloads its domain going in and out of play mode
            if ($st -and $st.status -and $st.status -ne 'running') { break }
            Start-Sleep -Seconds 4
        }
        if (-not $st -or $st.status -ne 'completed') {
            # One cancel does not always take (the runner goes on to the next test and back into play mode):
            # cancel until the run reports it is no longer running, so the editor is not left playing.
            for ($k = 0; $k -lt 12; $k++) {
                Invoke-Unity @('cancel_tests') | Out-Null
                Start-Sleep -Seconds 5
                $c = Invoke-Unity @('test_status')
                if ($c -and $c.status -ne 'running') { break }
            }
            $rows += [pscustomobject]@{ Fixture = $f; Result = 'TIMEOUT'; Passed = 0; Failed = 0; Skipped = 0; Total = 0; Seconds = [int]((Get-Date) - $started).TotalSeconds }
            continue
        }
        $sum = $st.summary
        $ok = $sum.total -gt 0 -and $sum.failed -eq 0
        $rows += [pscustomobject]@{
            Fixture = $f; Result = $(if ($sum.total -eq 0) { 'NO TESTS' } elseif ($ok) { 'pass' } else { 'FAIL' })
            Passed = $sum.passed; Failed = $sum.failed; Skipped = $sum.skipped; Total = $sum.total; Seconds = [int]$st.duration
        }
        foreach ($r in $st.results) {
            $all += [pscustomobject]@{ Fixture = $f; Name = $r.FullName; Status = $r.Status; Message = $r.Message }
            if ($r.Status -eq 'Failed') {
                Write-Host "  FAILED $($r.FullName)" -ForegroundColor Red
                if ($r.Message) { Write-Host ('    ' + (($r.Message -split "`n")[0..3] -join "`n    ")) -ForegroundColor Red }
            }
        }
    }
    Stop-Play

    New-Item -ItemType Directory -Force (Join-Path $root 'Temp') | Out-Null
    @{ when = (Get-Date).ToString('s'); fixtures = $rows; tests = $all } | ConvertTo-Json -Depth 6 |
        Set-Content (Join-Path $root 'Temp/pobox-tests.json')

    $rows | Format-Table Fixture, Result, Passed, Failed, Skipped, Total, Seconds -AutoSize | Out-String | Write-Host
    $bad = @($rows | Where-Object { $_.Result -ne 'pass' })
    $passed = ($rows | Measure-Object Passed -Sum).Sum; $total = ($rows | Measure-Object Total -Sum).Sum
    if ($bad.Count -eq 0) { Write-Host "ALL PASS: $passed of $total tests in $($rows.Count) fixtures." -ForegroundColor Green; exit 0 }
    Write-Host "NOT ALL PASS: $passed of $total tests; $($bad.Count) of $($rows.Count) fixtures not clean." -ForegroundColor Red
    exit 1
}
finally { Pop-Location }
