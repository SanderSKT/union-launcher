# On the trusted Windows developer PC. Signs an already built installer OFFLINE.
# Produces ONLY the signed distribution files for manual transfer to the VPS.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$ApiOrigin = 'https://union.cool',
    [string]$PrivateKeyFile = (Join-Path $env:USERPROFILE 'UNION-Private-Backups\launcher-update-signing\launcher-update-private.pk8'),
    [string]$SetupFile = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$') { throw 'Expected numeric version 0.2.4 etc.' }
$origin = $null
if (![Uri]::TryCreate($ApiOrigin.TrimEnd('/') + '/', [UriKind]::Absolute, [ref]$origin) -or
    $origin.Scheme -ne 'https' -or $origin.Host -ne 'union.cool' -or
    $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query -or $origin.Fragment -or
    !$origin.IsDefaultPort) {
    throw 'For public staging, ApiOrigin must be exactly https://union.cool'
}
$root = Split-Path -Parent $PSScriptRoot
if (!$SetupFile) { $SetupFile = Join-Path $root ("installer\releases\" + $Version + "\UNIONSetup.exe") }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("installer\signed-releases\" + $Version) }
if (!(Test-Path -LiteralPath $PrivateKeyFile -PathType Leaf)) { throw 'Private signing key missing. Do not generate a replacement if a key has been released.' }
if (!(Test-Path -LiteralPath $SetupFile -PathType Leaf)) { throw "Installer missing: $SetupFile" }
if ((Test-Path $OutputDirectory) -and (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0)) {
    throw 'Output directory is not empty. Do not overwrite a signed release.'
}
$project = Join-Path $root 'tools\launcher-update-signer\LauncherUpdateSigner.csproj'
$url = $origin.AbsoluteUri.TrimEnd('/') + '/api/union/launcher/installer'
& dotnet run --project $project -- publish $SetupFile $Version $url $PrivateKeyFile $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Offline signing failed.' }
$manifest = Get-Content (Join-Path $OutputDirectory 'manifest.json') -Raw | ConvertFrom-Json
$hash = (Get-FileHash (Join-Path $OutputDirectory 'UNIONSetup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $manifest.sha256) { throw 'Signed distribution hash mismatch.' }
Write-Host 'READY FOR PRIVATE TRANSFER TO VPS:' $OutputDirectory -ForegroundColor Green
Write-Warning 'Copy ONLY UNIONSetup.exe and manifest.json. NEVER copy the private signing key to the server.'
