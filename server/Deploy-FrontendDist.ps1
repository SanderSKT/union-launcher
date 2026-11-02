# Deploy compiled Vite dist artifact (not source) on UNION Windows VPS.
# Rolls back on copy or health-check failure and preserves the previous dist.
[CmdletBinding()]
param(
    [string]$ArtifactDirectory = 'C:\UNIONV2\private-import\union-frontend-launcher-download-v025',
    [string]$LiveDirectory = 'C:\UNIONV2\source\frontend\dist'
)
$ErrorActionPreference = 'Stop'
$index = Join-Path $ArtifactDirectory 'index.html'
$assets = Join-Path $ArtifactDirectory 'assets'
if (!(Test-Path -LiteralPath $index -PathType Leaf) -or !(Test-Path -LiteralPath $assets -PathType Container)) {
    throw "Artifact directory must contain index.html and assets/: $ArtifactDirectory"
}
$bundleFound = $false
foreach ($js in @(Get-ChildItem -LiteralPath $assets -File -Filter '*.js')) {
    if ([IO.File]::ReadAllText($js.FullName).Contains('v0.2.5-alpha/UNIONSetup.exe')) { $bundleFound = $true; break }
}
if (!$bundleFound) { throw 'Compiled assets do not contain the expected verified GitHub Release URL.' }
$parent = Split-Path -Parent $LiveDirectory
if (!(Test-Path $parent -PathType Container)) { throw "Missing frontend directory: $parent" }
$stage = Join-Path $parent ('.dist-stage-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $parent ('dist-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $stage | Out-Null
$hadLive = Test-Path $LiveDirectory
$movedOld = $false
$switched = $false
try {
    foreach ($item in @(Get-ChildItem -LiteralPath $ArtifactDirectory -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse -Force
    }
    if (!(Test-Path (Join-Path $stage 'index.html'))) { throw 'Staging failed.' }
    if ($hadLive) { Move-Item -LiteralPath $LiveDirectory -Destination $backup -ErrorAction Stop; $movedOld = $true }
    Move-Item -LiteralPath $stage -Destination $LiveDirectory -ErrorAction Stop
    $switched = $true

    $resolved = 'union.cool:443:127.0.0.1'
    foreach ($url in @('https://union.cool/', 'https://union.cool/api/health')) {
        $code = (& curl.exe -sS --http1.1 --resolve $resolved --max-time 15 -o NUL -w '%{http_code}' $url).Trim()
        if ($LASTEXITCODE -ne 0 -or $code -ne '200') {
            throw "$url responded with HTTP $code. Rolling back frontend."
        }
    }
    Write-Host 'SUCCESS: frontend deployed with official UNION Launcher download link.' -ForegroundColor Green
    if ($movedOld) { Write-Host "Previous site preserved: $backup" }
    Write-Host "Live frontend: $LiveDirectory"
}
catch {
    if ($switched -and (Test-Path $LiveDirectory)) { Remove-Item -LiteralPath $LiveDirectory -Recurse -Force }
    if ($movedOld -and (Test-Path $backup)) { Move-Item -LiteralPath $backup -Destination $LiveDirectory }
    throw
}
finally {
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
