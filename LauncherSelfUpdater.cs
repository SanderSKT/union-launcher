using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnionLauncher;

internal sealed record LauncherUpdateManifest(string Version, string DownloadUrl, string Sha256, long SizeBytes, string Signature);

internal static class LauncherSelfUpdater
{
    private const long MaxInstallerBytes = 256L * 1024 * 1024;
    // Injected as signed-off build metadata at publish time.
    // No key => updater is disabled rather than trusting an unsigned update.
    private static string? PublicKey =>
        typeof(LauncherSelfUpdater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "UNIONLauncherUpdatePublicKey")?.Value;

    internal static bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey);

    private static string Canonical(LauncherUpdateManifest m) =>
        "UNION-LAUNCHER-UPDATE-V1\n" + m.Version + "\n" +
        m.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
        m.Sha256 + "\n" + m.DownloadUrl + "\n";

    private static bool AcceptUrl(Uri uri) =>
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static void VerifyManifest(LauncherUpdateManifest m, Uri api)
    {
        if (!Regex.IsMatch(m.Version ?? "", @"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$") ||
            !Version.TryParse(m.Version, out _) ||
            !Regex.IsMatch(m.Sha256 ?? "", @"^[0-9a-f]{64}$") ||
            m.SizeBytes is <= 0 or > MaxInstallerBytes)
            throw new InvalidDataException("Invalid launcher update version, size, or hash.");

        if (!Uri.TryCreate(m.DownloadUrl, UriKind.Absolute, out var url) ||
            !AcceptUrl(url) ||
            !string.Equals(url.GetLeftPart(UriPartial.Authority),
                api.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.Equals("/api/union/launcher/installer", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(url.Query))
            throw new InvalidDataException("Untrusted launcher installer URL.");

        var publicKey = PublicKey ?? throw new InvalidOperationException("Launcher update signing key not configured.");
        byte[] publicBytes;
        byte[] signature;
        try
        {
            publicBytes = Convert.FromBase64String(publicKey);
            signature = Convert.FromBase64String(m.Signature);
        }
        catch (FormatException ex) { throw new InvalidDataException("Malformed launcher update signature or public key.", ex); }

        using var rsa = RSA.Create();
        try { rsa.ImportSubjectPublicKeyInfo(publicBytes, out _); }
        catch (CryptographicException ex) { throw new InvalidDataException("Invalid launcher update public key.", ex); }
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(Canonical(m)), signature,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Launcher update signature verification failed.");
    }

    internal static async Task<LauncherUpdateManifest?> CheckAsync(HttpClient http, Uri api)
    {
        if (!IsConfigured) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await http.GetAsync(
            new Uri(api, "api/union/launcher/update"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);

        // Feature is optional on local backends or while no release has been staged.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
            response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 16384)
            throw new InvalidDataException("Launcher update manifest too large.");
        var raw = await response.Content.ReadAsStringAsync(timeout.Token);
        if (raw.Length > 16384) throw new InvalidDataException("Launcher update manifest too large.");
        var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing launcher update manifest.");
        VerifyManifest(manifest, api);
        var current = typeof(LauncherSelfUpdater).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Launcher assembly version missing.");
        if (Version.Parse(manifest.Version) <= current)
            return null;
        return manifest;
    }

    internal static async Task<string> DownloadVerifiedAsync(
        HttpClient http, Uri api, LauncherUpdateManifest manifest,
        Action<int> progress, Action<string> status)
    {
        VerifyManifest(manifest, api);
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UNION", "LauncherUpdates");
        Directory.CreateDirectory(updateRoot);
        var path = Path.Combine(updateRoot, "UNIONSetup-" + manifest.Version + "-" + Guid.NewGuid().ToString("N") + ".exe");

        try
        {
            status("Downloading launcher v" + manifest.Version + "...");
            using var response = await http.GetAsync(
                manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != manifest.SizeBytes)
                throw new InvalidDataException("Installer download size differs from the signed manifest.");

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long received = 0;
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer)) != 0)
            {
                received += bytesRead;
                if (received > manifest.SizeBytes)
                    throw new InvalidDataException("Installer is larger than the signed manifest.");
                await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                hash.AppendData(buffer.AsSpan(0, bytesRead));
                progress(Math.Clamp((int)(100.0 * received / manifest.SizeBytes), 0, 100));
            }
            await output.FlushAsync();
            if (received != manifest.SizeBytes ||
                !Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer failed signed SHA-256 and byte-count verification.");

            status("Launcher update verified. Opening installer...");
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    internal static void LaunchInstaller(string verifiedPath)
    {
        if (!File.Exists(verifiedPath)) throw new FileNotFoundException("Verified setup not found.", verifiedPath);
        var started = Process.Start(new ProcessStartInfo(verifiedPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(verifiedPath)!
        });
        if (started is null)
            throw new InvalidOperationException("Could not open UNIONSetup.");
        started.Dispose();
    }
}
