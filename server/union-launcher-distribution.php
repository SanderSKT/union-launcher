<?php
declare(strict_types=1);

/**
 * Private-staged launcher updates. Disabled until explicitly enabled in local.php.
 * The C# launcher authenticates each release with a PINNED RSA-PSS public key.
 * Do not place private signing keys in PHP, Git, or the web root.
 */
function union_launcher_update_dispatch(): never
{
    if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'GET') {
        header('Allow: GET');
        json_response(['error' => 'Method not allowed'], 405);
    }
    header('X-Content-Type-Options: nosniff');
    header('Cache-Control: no-store');
    if (conf('launcher_updates_enabled', false) !== true) {
        json_response(['error' => 'Launcher updates disabled'], 404);
    }
    $root = ROOT . '/var/launcher-distribution';
    $manifestPath = $root . '/manifest.json';
    $installerPath = $root . '/UNIONSetup.exe';
    if (!is_file($manifestPath) || !is_file($installerPath)) {
        json_response(['error' => 'Launcher update not staged'], 503);
    }
    try {
        $raw = file_get_contents($manifestPath);
        if (!is_string($raw) || strlen($raw) > 16384) {
            throw new RuntimeException('Invalid launcher manifest');
        }
        $m = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($m) ||
            !preg_match('/^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$/D', (string)($m['version'] ?? '')) ||
            !preg_match('/^[0-9a-f]{64}$/D', (string)($m['sha256'] ?? '')) ||
            !is_int($m['sizeBytes'] ?? null) ||
            $m['sizeBytes'] <= 0 || $m['sizeBytes'] > 268435456 ||
            !is_string($m['signature'] ?? null) ||
            !is_string($m['downloadUrl'] ?? null) ||
            base64_decode($m['signature'], true) === false ||
            $m['downloadUrl'] !== base_url('/api/union/launcher/installer') ||
            filesize($installerPath) !== $m['sizeBytes'] ||
            hash_file('sha256', $installerPath) !== $m['sha256']) {
            throw new RuntimeException('Launcher manifest validation failed');
        }
    } catch (Throwable $ex) {
        error_log('[UNION launcher update] ' . $ex->getMessage());
        json_response(['error' => 'Launcher release not ready'], 503);
    }

    $path = strtolower(rtrim((string)(parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?: '/'), '/'));
    if ($path === '/api/union/launcher/update') {
        header('Content-Type: application/json; charset=utf-8');
        echo json_encode($m, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
        exit;
    }
    if ($path === '/api/union/launcher/installer') {
        header('Content-Type: application/octet-stream');
        header('Content-Disposition: attachment; filename="UNIONSetup.exe"');
        header('Content-Length: ' . $m['sizeBytes']);
        $fp = @fopen($installerPath, 'rb');
        if ($fp === false) json_response(['error' => 'Installer unavailable'], 503);
        @set_time_limit(0);
        while (!feof($fp)) {
            $chunk = fread($fp, 1024 * 1024);
            if ($chunk === false || $chunk === '') break;
            echo $chunk;
            if (connection_aborted()) break;
            flush();
        }
        fclose($fp);
        exit;
    }
    json_response(['error' => 'Not found'], 404);
}
