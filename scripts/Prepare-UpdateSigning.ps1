# Generate signing keys on your trusted Windows machine and stage PUBLIC key for GitHub.
# NEVER put the private key, key backups, or a whole signing directory inside the repository.
[CmdletBinding()]
param([string]$PrivateDirectory = (Join-Path $env:USERPROFILE 'UNION-Private-Backups\launcher-update-signing'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$private = Join-Path $PrivateDirectory 'launcher-update-private.pk8'
$public = Join-Path $PrivateDirectory 'launcher-update-public.spki.b64'
$repoPublic = Join-Path $root 'update-signing\launcher-update-public.spki.b64'
$signer = Join-Path $root 'tools\launcher-update-signer\LauncherUpdateSigner.csproj'
if (!(Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {throw '.NET 10 SDK is required.'}
if (!(Test-Path $private) -and !(Test-Path $public)) {
    & dotnet run --project $signer -- keygen $PrivateDirectory
    if ($LASTEXITCODE -ne 0) {throw 'Signing key generation failed.'}
} elseif (!(Test-Path $private) -or !(Test-Path $public)) {
    throw 'Only one key file exists. Stop: do not regenerate a key without recovering the matching pair.'
} else {
    Write-Host 'Existing signing key pair found; keeping it unchanged.' -ForegroundColor Yellow
}
if (Test-Path -LiteralPath $repoPublic) {
    if (([IO.File]::ReadAllText($repoPublic).Trim()) -cne ([IO.File]::ReadAllText($public).Trim())) {
        throw 'Repository already pins a DIFFERENT key. Refusing automatic key rotation.'
    }
} else {
    New-Item -ItemType Directory -Force -Path (Split-Path $repoPublic -Parent) | Out-Null
    Copy-Item -LiteralPath $public -Destination $repoPublic -ErrorAction Stop
}
Write-Host 'Public key ready for commit:' $repoPublic -ForegroundColor Green
Write-Host 'PRIVATE KEY (NEVER COMMIT, SEND OR PUT ON VPS):' $private -ForegroundColor Yellow
Write-Warning 'The PKCS#8 private key is unencrypted. Store an encrypted offline backup and restrict access to your account.'
Write-Host 'Commit only update-signing/launcher-update-public.spki.b64, then build a pinned-key installer.' -ForegroundColor Cyan
