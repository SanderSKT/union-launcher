# Only switch the UNION 2018M client ZIP endpoint from PHP to Caddy's static file server.
# Does not modify RCC, other Caddy virtual hosts or the PHP manifest endpoint.
[CmdletBinding()]
param(
    [string]$Caddyfile = 'C:\caddy\Caddyfile',
    [string]$CaddyExe = 'C:\caddy\caddy.exe',
    [string]$Backend = 'C:\UNIONV2\source\backend',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$marker = '# UNION_DIRECT_CLIENT_ARCHIVE_V1'
$folder = Join-Path $Backend 'var\client-distribution'
$manifestFile = Join-Path $folder 'manifest.json'
$zipFile = Join-Path $folder 'client-2018m.zip'
foreach ($path in @($Caddyfile,$CaddyExe,$manifestFile,$zipFile)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing: $path" }
}
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$file = Get-Item -LiteralPath $zipFile
$hash = (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash
if ($file.Length -ne $manifest.sizeBytes -or $hash -ine $manifest.sha256) {
    throw 'Client ZIP size/SHA256 does not match PHP manifest. No changes.'
}
$rootForCaddy = $folder.Replace('\','/')
$block = @"
    $marker
    handle /api/union/client/archive {
        root * $rootForCaddy
        rewrite * /client-2018m.zip
        header Content-Type application/zip
        header Content-Disposition "attachment; filename=client-2018m.zip"
        file_server
    }

"@
$old = [IO.File]::ReadAllText($Caddyfile)
if ($old.Contains($marker)) { Write-Host 'Caddy direct archive route is already configured. No changes.'; exit 0 }
$site = [regex]::Match($old, '(?m)^union\.cool\s*\{\s*$')
if (!$site.Success) { throw 'Could not find exactly the expected union.cool site block.' }
$next = $old.IndexOf('http://union.cool {', $site.Index + $site.Length, [StringComparison]::Ordinal)
if ($next -lt 0) { throw 'Could not identify end of union.cool block. No changes.' }
$segment = $old.Substring($site.Index,$next-$site.Index)
$anchor = '    handle @backend {'
if ($segment.Split(@($anchor),[StringSplitOptions]::None).Count -ne 2) {
    throw 'Could not find a unique backend handle in union.cool; review Caddyfile manually.'
}
$offset = $segment.IndexOf($anchor,[StringComparison]::Ordinal)
$updated = $old.Insert($site.Index+$offset,$block)
if (!$Apply) {
    Write-Host "DRY RUN: would insert below route into $Caddyfile" -ForegroundColor Cyan
    Write-Host $block
    Write-Host "Client ZIP validated: $($file.Length) bytes"
    Write-Host 'To apply: rerun this script with -Apply.' -ForegroundColor Yellow
    exit 0
}
$main = @(Get-CimInstance Win32_Process -Filter "Name = 'caddy.exe'" |
    Where-Object { $_.CommandLine -match '(?i)run\s+--config\s+["'']?C:\\caddy\\Caddyfile' })
$admin = @(Get-NetTCPConnection -LocalPort 2019 -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique)
if ($main.Count -ne 1 -or $main[0].ProcessId -notin $admin) {
    throw 'Caddy admin API port 2019 is not confirmed to belong to the main Caddy instance. No changes.'
}
$backup = "$Caddyfile.backup-archive-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $Caddyfile -Destination $backup -ErrorAction Stop
$utf8 = [Text.UTF8Encoding]::new($false)
$reloaded = $false
try {
    [IO.File]::WriteAllText($Caddyfile,$updated,$utf8)
    & $CaddyExe validate --config $Caddyfile --adapter caddyfile
    if ($LASTEXITCODE -ne 0) { throw 'Caddy validation failed.' }
    & $CaddyExe reload --config $Caddyfile --adapter caddyfile
    if ($LASTEXITCODE -ne 0) { throw 'Caddy reload failed.' }
    $reloaded = $true

    $origin = 'https://union.cool'
    $resolve = 'union.cool:443:127.0.0.1'
    $head = (& curl.exe --http1.1 -sS -D - -o NUL --max-time 15 --resolve $resolve -I "$origin/api/union/client/archive" 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '(?im)^HTTP/1\.1 200\b' -or
        $head -notmatch ('(?im)^Content-Length:\s*'+$file.Length+'\s*$')) {
        throw "Archive HEAD check failed: $head"
    }
    $range = (& curl.exe --http1.1 -sS -D - -o NUL --max-time 15 --max-filesize 2048 --resolve $resolve -H 'Range: bytes=0-1023' -H 'Accept-Encoding: identity' "$origin/api/union/client/archive" 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $range -notmatch '(?im)^HTTP/1\.1 206\b' -or
        $range -notmatch ('(?im)^Content-Range:\s*bytes 0-1023/'+$file.Length+'\s*$')) {
        throw "Archive range check failed: $range"
    }
    foreach ($endpoint in @('api/health','api/union/client/manifest')) {
        $status = (& curl.exe --http1.1 -sS -o NUL -w '%{http_code}' --max-time 15 --resolve $resolve "$origin/$endpoint").Trim()
        if ($LASTEXITCODE -ne 0 -or $status -ne '200') { throw "Backend API regression: $endpoint returned HTTP $status" }
    }
    Write-Host 'SUCCESS: Caddy serves client ZIP directly (HEAD 200, Range 206); PHP API still 200.' -ForegroundColor Green
    Write-Host "Caddyfile backup: $backup"
} catch {
    Write-Warning "Validation or HTTP smoke test failed: $($_.Exception.Message). Rolling back config..."
    Copy-Item -LiteralPath $backup -Destination $Caddyfile -Force
    if ($reloaded) {
        & $CaddyExe reload --config $Caddyfile --adapter caddyfile
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Rollback reload failed. Restore Caddy with backup manually.' }
    }
    throw
}
