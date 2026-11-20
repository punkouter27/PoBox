# Builds PoBox's WebGL player on THIS machine and publishes it as a GitHub Release. Publishing the release
# triggers .github/workflows/webgl.yml, which deploys the release's webgl.zip to the game's Azure Static Web App
# on a GitHub-hosted runner and checks the live page and /diag. CI has no Unity licence and needs none.
#
#   pwsh -File scripts\publish_webgl.ps1               # EditMode tests, build, zip, publish release, prune old ones
#   pwsh -File scripts\publish_webgl.ps1 -NoPublish    # tests, build and zip only
#   pwsh -File scripts\publish_webgl.ps1 -SkipBuild    # publish the existing Builds/WebGL (diag.html is rewritten)
#
# Close the Unity editor on this project first (a batch-mode Unity cannot open a project that is already open).
# The release is tagged at HEAD, so HEAD must already be pushed to origin. Unity must be the exact version in
# ProjectSettings/ProjectVersion.txt. The build method and the /diag checks come from scripts/webgl.json.
#
# One version stamp: UTC yyMMdd, computed once here. Unity gets it through PO_BUILD_STAMP (WebGLBuildStamp sets
# PlayerSettings.bundleVersion from it, so Application.version and index.html's v{PRODUCT_VERSION} carry it) and
# diag.html is written with the same value.

param(
    [switch] $SkipBuild,
    [switch] $NoPublish,
    [switch] $SkipTests,     # only to publish a build whose tests already ran; say why in the release notes
    [int] $Keep = 3          # webgl-* releases to keep, the new one included
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$cfg = Get-Content (Join-Path $PSScriptRoot 'webgl.json') -Raw | ConvertFrom-Json
$buildDir = Join-Path $root 'Builds\WebGL'
$zip = Join-Path $root 'Builds\webgl.zip'
$now = [DateTime]::UtcNow
$stamp = $now.ToString('yyMMdd')
$env:PO_BUILD_STAMP = $stamp

$sha = (git rev-parse HEAD).Trim()
if (-not $NoPublish) {
    git fetch --quiet origin
    if (-not (git branch -r --contains $sha)) {
        throw "HEAD $($sha.Substring(0,7)) is not on origin. Push it first: the release is tagged at HEAD."
    }
    if (git status --porcelain --untracked-files=no) {
        Write-Warning 'Uncommitted changes: the build will include them, but the release is tagged at HEAD.'
    }
}

function Find-Unity {
    $want = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Select-String 'm_EditorVersion:') -split ':\s*')[1].Trim()
    $exe = Join-Path 'C:\Program Files\Unity\Hub\Editor' "$want\Editor\Unity.exe"
    if (-not (Test-Path $exe)) { throw "Unity $want is not installed. Install exactly that version (with WebGL) in Unity Hub." }
    if (-not (Test-Path (Join-Path (Split-Path $exe) 'Data\PlaybackEngines\WebGLSupport'))) { throw "Unity $want has no WebGL module." }
    return $exe
}

function Invoke-Unity([string[]] $unityArgs, [string] $log) {
    $p = Start-Process -FilePath $script:unity -PassThru -Wait -NoNewWindow -ArgumentList (@('-batchmode', '-projectPath', "`"$root`"", '-logFile', "`"$log`"") + $unityArgs)
    return $p.ExitCode
}

New-Item -ItemType Directory -Force (Split-Path $buildDir) | Out-Null
if (-not $SkipBuild) {
    $script:unity = Find-Unity

    if (-not $SkipTests) {
        $results = Join-Path $root 'Builds\editmode-results.xml'
        $tlog = Join-Path $root 'Builds\editmode-tests.log'
        if (Test-Path $results) { Remove-Item $results -Force }
        "EditMode tests first; log: $tlog"
        $code = Invoke-Unity @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$results`"") $tlog
        if (-not (Test-Path $results)) { Get-Content $tlog -Tail 40; throw "EditMode tests did not run (exit $code). Log: $tlog" }
        $run = ([xml](Get-Content $results -Raw)).'test-run'
        "EditMode: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
        if ([int]$run.failed -gt 0 -or $code -ne 0) { throw "EditMode tests failed ($($run.failed)); not building. Results: $results" }
    }

    $log = Join-Path $root 'Builds\webgl-build.log'
    "Building WebGL v$stamp ($($cfg.buildMethod)); log: $log"
    $code = Invoke-Unity @('-quit', '-buildTarget', 'WebGL', '-executeMethod', $cfg.buildMethod) $log
    if ($code -ne 0) { Get-Content $log -Tail 40; throw "Unity build failed (exit $code). Full log: $log" }
}

$index = Join-Path $buildDir 'index.html'
if (-not (Test-Path $index)) { throw "No WebGL build at $buildDir." }
$pageVersion = [regex]::Match((Get-Content $index -Raw), 'data-version="v(\d{6})"').Groups[1].Value
if (-not $SkipBuild -and $pageVersion -ne $stamp) { throw "index.html says v$pageVersion, this publish is v$stamp: the stamp did not reach the build." }
if ($SkipBuild -and $pageVersion) { $stamp = $pageVersion }

# Static Web Apps 404s .unityweb without this MIME config.
Copy-Item (Join-Path $PSScriptRoot 'webgl\staticwebapp.config.json') $buildDir -Force

# /diag: the repo's own checks plus every file of this build, all probed live by the browser.
$checks = [System.Collections.Generic.List[object]]::new()
$checks.Add([ordered]@{ section = 'API'; name = 'Game page (/)'; url = '/'; expect = 'ok' })
foreach ($c in $cfg.diagChecks) { $checks.Add($c) }
Get-ChildItem (Join-Path $buildDir 'Build') -File | Sort-Object Name | ForEach-Object {
    $checks.Add([ordered]@{ section = 'Data'; name = "Build bundle: $($_.Name)"; url = "/Build/$($_.Name)"; method = 'HEAD'; expect = 'ok' })
}
$checks.Add([ordered]@{ section = 'Services'; name = '3D renderer (WebGL2 in this browser)'; expect = 'webgl2' })
$checksJson = ConvertTo-Json -InputObject @($checks) -Depth 4 -Compress
$diag = (Get-Content (Join-Path $PSScriptRoot 'webgl\diag.html') -Raw).
    Replace('__TITLE__', $cfg.title).Replace('__VERSION__', $stamp).Replace('__CHECKS__', $checksJson)
[IO.File]::WriteAllText((Join-Path $buildDir 'diag.html'), $diag)

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $buildDir '*') -DestinationPath $zip -CompressionLevel Optimal
'{0}: {1:N1} MB, v{2}' -f $zip, ((Get-Item $zip).Length / 1MB), $stamp

if ($NoPublish) { 'Built and zipped; not published (-NoPublish).'; return }

$repo = (gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
$tag = "webgl-$($now.ToString('yyMMdd-HHmm'))-$($sha.Substring(0,7))"
$notes = "Local WebGL build v$stamp of $($sha.Substring(0,7)). Publishing this release deploys it to Azure (.github/workflows/webgl.yml)."
if ($SkipTests) { $notes += ' EditMode tests were skipped for this build (-SkipTests).' }
gh release create $tag $zip -R $repo --target $sha --title "WebGL v$stamp ($($now.ToString('HHmm')) UTC)" --latest=false --notes $notes
if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)." }
"PUBLISHED $tag  ->  deploy: https://github.com/$repo/actions/workflows/webgl.yml"

# Keep only the newest $Keep webgl-* releases (and their tags).
$old = gh release list -R $repo --limit 100 --json tagName,createdAt |
    ConvertFrom-Json | Where-Object { $_.tagName -like 'webgl-*' } |
    Sort-Object createdAt -Descending | Select-Object -Skip $Keep
foreach ($r in $old) { gh release delete $r.tagName -R $repo --yes --cleanup-tag; "pruned $($r.tagName)" }
