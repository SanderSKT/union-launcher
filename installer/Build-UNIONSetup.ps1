# Builds a versioned, self-contained per-user UNION Launcher installer.
# Optional public key enables signature-verified self updates.
[CmdletBinding()]
param(
    [string]$IsccPath = '',
    [string]$UpdatePublicKeyFile = '',
    [string]$ReleaseVersion = '0.2.3'
)
$ErrorActionPreference = 'Stop'

if ($ReleaseVersion -notmatch '^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$') {
    throw 'ReleaseVersion must be numeric major.minor.patch (example: 0.2.3).'
}
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'UnionLauncher.csproj'
$setup = Join-Path $PSScriptRoot 'UNIONSetup.iss'
$payload = Join-Path $PSScriptRoot 'payload'
$exe = Join-Path $payload 'UnionLauncher.exe'
$dotnet = Get-Command dotnet.exe -ErrorAction Stop

if (!$IsccPath) {
    foreach ($candidate in @(
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $IsccPath = $candidate
            break
        }
    }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup is missing. Install from jrsoftware.org, or supply -IsccPath.'
}

New-Item -ItemType Directory -Force -Path $payload | Out-Null
if (Get-Process -Name UnionLauncher -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe }) {
    throw 'Close the UNION Launcher in the installer payload directory before rebuilding.'
}

if (!$UpdatePublicKeyFile) {
    $UpdatePublicKeyFile = Join-Path $env:USERPROFILE 'UNION-Private-Backups\launcher-update-signing\launcher-update-public.spki.b64'
}
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    ('-p:Version=' + $ReleaseVersion),
    '-p:UnionDefaultApiOrigin=https://union.cool',
    '-o', $payload
)
if (Test-Path -LiteralPath $UpdatePublicKeyFile -PathType Leaf) {
    $publicKey = [IO.File]::ReadAllText($UpdatePublicKeyFile).Trim()
    if ($publicKey -notmatch '^[A-Za-z0-9+/=]{100,4096}$') {
        throw 'Release public key content is invalid.'
    }
    try { $null = [Convert]::FromBase64String($publicKey) }
    catch { throw 'Release public key is not valid base64.' }
    $publishArgs += ('-p:UnionUpdatePublicKeyBase64=' + $publicKey)
    Write-Host 'Pinned public key included: signed launcher self-updates enabled.' -ForegroundColor Green
} else {
    throw 'No pinned public key: run scripts/Prepare-UpdateSigning.ps1 first. Refusing to build an unupdatable release installer.'
}

& $dotnet.Source @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; no installer built.' }
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'UnionLauncher.exe was not generated.' }

$appVersion = $ReleaseVersion
& $IsccPath ("/DMyAppVersion=" + $appVersion) $setup
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$artifact = Join-Path $PSScriptRoot 'output\UNIONSetup.exe'
if (!(Test-Path -LiteralPath $artifact -PathType Leaf)) { throw 'UNIONSetup.exe was not generated.' }

# Never overwrite previously archived installer bytes for the same version.
$archiveDir = Join-Path $PSScriptRoot ("releases\" + $ReleaseVersion)
$archive = Join-Path $archiveDir 'UNIONSetup.exe'
if (Test-Path -LiteralPath $archive -PathType Leaf) {
    $builtHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    $oldHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($builtHash -ne $oldHash) {
        throw "Version $ReleaseVersion was archived with different bytes. Choose another version; do not replace the published release."
    }
} else {
    New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null
    Copy-Item -LiteralPath $artifact -Destination $archive
}
Get-Item -LiteralPath $archive | Select-Object FullName, Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256
Write-Host 'Previous release installers remain archived.' -ForegroundColor Green
Write-Warning 'This is a Windows alpha build. Verify setup and rollback before distribution.'
