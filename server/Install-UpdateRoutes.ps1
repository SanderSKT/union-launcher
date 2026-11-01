# One-time, non-disruptive installation of the launcher update API into the existing UNION PHP router.
# Run on the Windows VPS from a trusted checkout. Leaves launcher_updates_enabled OFF by default.
[CmdletBinding()]
param(
    [string]$Backend = 'C:\UNIONV2\source\backend',
    [string]$PhpExe = 'C:\UNIONV2\tools\php84\php.exe'
)
$ErrorActionPreference = 'Stop'
$router = Join-Path $Backend 'public\router.php'
$dest = Join-Path $Backend 'src\union-launcher-distribution.php'
$source = Join-Path $PSScriptRoot 'union-launcher-distribution.php'
$marker = '/* UNION_LAUNCHER_UPDATE_ROUTE_V1 */'
if (!(Test-Path -LiteralPath $router -PathType Leaf) -or !(Test-Path -LiteralPath $source -PathType Leaf) -or !(Test-Path -LiteralPath $PhpExe -PathType Leaf)) {
    throw 'Cannot locate PHP router, update handler, or php.exe.'
}
$old = [IO.File]::ReadAllText($router)
if ($old.Contains($marker)) { Write-Host 'Update route already installed. No changes.'; exit 0 }
if (Test-Path -LiteralPath $dest -PathType Leaf) {
    if ((Get-FileHash $dest -Algorithm SHA256).Hash -ne (Get-FileHash $source -Algorithm SHA256).Hash) {
        throw 'Existing update handler differs; stop and review rather than overwrite.'
    }
}
$anchor = 'declare(strict_types=1);'
if ($old.Split(@($anchor),[StringSplitOptions]::None).Count -ne 2) { throw 'Unexpected router layout. Refusing to edit.' }
$insert = @'

/* UNION_LAUNCHER_UPDATE_ROUTE_V1 */
$unionLauncherUpdatePath = strtolower(rtrim((string)(parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?: '/'), '/') ?: '/');
if (in_array($unionLauncherUpdatePath, ['/api/union/launcher/update', '/api/union/launcher/installer'], true)) {
    require_once __DIR__ . '/../src/bootstrap.php';
    require_once __DIR__ . '/../src/union-launcher-distribution.php';
    union_launcher_update_dispatch();
}
/* END_UNION_LAUNCHER_UPDATE_ROUTE_V1 */

'@
$backup = "$router.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $router -Destination $backup -ErrorAction Stop
$created = !(Test-Path -LiteralPath $dest -PathType Leaf)
try {
    if ($created) { Copy-Item -LiteralPath $source -Destination $dest -ErrorAction Stop }
    $new = $old.Replace($anchor, $anchor + $insert)
    [IO.File]::WriteAllText($router, $new, [Text.UTF8Encoding]::new($false))
    & $PhpExe -l $router
    if ($LASTEXITCODE -ne 0) { throw 'PHP router failed syntax check.' }
    & $PhpExe -l $dest
    if ($LASTEXITCODE -ne 0) { throw 'PHP distribution handler failed syntax check.' }
    Write-Host "Update routes installed; router backup: $backup" -ForegroundColor Green
    Write-Host 'Still disabled until launcher_updates_enabled=true in private config.' -ForegroundColor Yellow
} catch {
    Copy-Item -LiteralPath $backup -Destination $router -Force
    if ($created) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue }
    throw
}
