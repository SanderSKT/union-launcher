# Run on the Windows VPS after manually transferring a signed release folder.
# Checks version/size/hash and stages atomically with previous-release rollback.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [string]$Backend='C:\UNIONV2\source\backend',
    [switch]$ReplacePrevious
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $SourceDirectory 'manifest.json'
$installerPath = Join-Path $SourceDirectory 'UNIONSetup.exe'
foreach ($p in @($manifestPath,$installerPath)) { if (!(Test-Path -LiteralPath $p -PathType Leaf)) {throw "Missing: $p"} }
$m = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($m.version -notmatch '^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$' -or
    $m.downloadUrl -cne 'https://union.cool/api/union/launcher/installer' -or
    $m.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
    !$m.signature -or
    $m.sizeBytes -le 0 -or $m.sizeBytes -gt 268435456) {
    throw 'Malformed or unexpected launcher manifest.'
}
$f = Get-Item -LiteralPath $installerPath
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($f.Length -ne $m.sizeBytes -or $hash -cne $m.sha256) {
    throw 'Installer does not match the signed manifest bytes/hash.'
}
$var = Join-Path $Backend 'var'
$active = Join-Path $var 'launcher-distribution'
if ((Test-Path -LiteralPath $active) -and !$ReplacePrevious) {
    throw "A release is already active at $active. Pass -ReplacePrevious to archive it."
}
$stage=Join-Path $var ('.launcher-update-stage-'+[Guid]::NewGuid().ToString('N'))
$prior=$null
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stage 'manifest.json')
    Copy-Item -LiteralPath $installerPath -Destination (Join-Path $stage 'UNIONSetup.exe')
    $stageHash=(Get-FileHash (Join-Path $stage 'UNIONSetup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($stageHash -cne $m.sha256) { throw 'File changed during copy.' }
    if (Test-Path -LiteralPath $active) {
        $prior=Join-Path $var ('launcher-distribution-previous-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
        Move-Item -LiteralPath $active -Destination $prior
    }
    try { Move-Item -LiteralPath $stage -Destination $active }
    catch { if ($prior -and !(Test-Path -LiteralPath $active)) { Move-Item -LiteralPath $prior -Destination $active }; throw }
    Write-Host "Staged launcher v$($m.version) at $active" -ForegroundColor Green
    if ($prior) { Write-Host "Previous release saved: $prior" }
    Write-Warning 'The PHP endpoint remains disabled unless explicitly enabled in local.php.'
    Write-Warning 'This script verifies transfer integrity but not RSA signature. The launcher verifies the pinned key.'
} finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force } }
