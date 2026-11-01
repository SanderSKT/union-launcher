using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace UnionLauncher;

internal sealed record ClientManifest(string Client, string Version, string DownloadUrl, string Sha256, long SizeBytes, string Executable);
internal sealed record LauncherSettings(string ApiUrl = "http://127.0.0.1:8080", string ManifestUrl = "");

internal sealed class LauncherWindow : Form
{
    private const long MaxArchiveSize = 2L * 1024 * 1024 * 1024;
    private const long MaxExtractedSize = 4L * 1024 * 1024 * 1024;
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UNION");
    private static readonly string Versions = Path.Combine(Root, "Versions", "2018M");
    private static readonly string SettingsFile = Path.Combine(Root, "native-launcher-settings.json");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(20) };
    private readonly string? _launchUri;
    private readonly TextBox _api = new() { Width = 430, Text = "http://127.0.0.1:8080" };
    private readonly TextBox _manifest = new() { Width = 430 };
    private readonly Label _status = new() { AutoSize = false, Width = 485, Height = 48, ForeColor = Color.Gainsboro };
    private readonly ProgressBar _progress = new() { Width = 475, Height = 14 };
    private readonly Button _install = new() { Text = "Install / repair 2018M", Width = 205, Height = 38 };
    private readonly Button _save = new() { Text = "Save settings", Width = 160, Height = 38 };
    private readonly Button _register = new() { Text = "Register union://", Width = 160, Height = 38 };
    private bool _busy;

    public LauncherWindow(string? launchUri)
    {
        _launchUri = launchUri;
        Text = "UNION Launcher | 2018M Alpha";
        ClientSize = new Size(530, 335);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 23, 32);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);
        var title = new Label { Text = "UNION", Font = new Font("Segoe UI", 26, FontStyle.Bold), ForeColor = Color.FromArgb(72, 184, 241), Location = new Point(26, 16), Size = new Size(280, 52) };
        var caption = new Label { Text = "NATIVE LAUNCHER  •  2018M  •  CLOSED ALPHA", Location = new Point(29, 70), Size = new Size(460, 21), Font = new Font("Segoe UI", 8) };
        var apiLabel = new Label { Text = "UNION API origin", Location = new Point(28, 101), AutoSize = true };
        _api.Location = new Point(28, 122);
        var manifestLabel = new Label { Text = "HTTPS client manifest URL", Location = new Point(28, 152), AutoSize = true };
        _manifest.Location = new Point(28, 174);
        _manifest.PlaceholderText = "Automatically derived from UNION API";
        _install.Location = new Point(28, 213);
        _save.Location = new Point(246, 213);
        _register.Location = new Point(28, 258);
        _status.Location = new Point(199, 266);
        _progress.Location = new Point(28, 304);
        Controls.AddRange([title, caption, apiLabel, _api, manifestLabel, _manifest, _install, _save, _register, _status, _progress]);
        _install.Click += async (_, _) => await RunAsync(InstallAsync);
        _save.Click += (_, _) => { try { Save(); Status("Settings saved."); } catch (Exception ex) { Error(ex); } };
        _register.Click += (_, _) => { try { RegisterProtocol(); Status("union:// registered for this Windows user."); } catch (Exception ex) { Error(ex); } };
        LoadSettings();
        Shown += async (_, _) =>
        {
            if (_launchUri is not null)
            {
                // Installation may take minutes. Do not redeem the 90-second code until the client is ready.
                if (!IsInstalled()) { Status("Install the client first, then click Play on the website again."); return; }
                await RunAsync(() => LaunchAsync(_launchUri));
            }
        };
    }

    private void Status(string message) { if (!IsDisposed) _status.Text = message; }
    private void Error(Exception ex) { Status("Error: " + ex.Message); MessageBox.Show(this, ex.Message, "UNION Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _install.Enabled = _save.Enabled = _register.Enabled = false;
        try { await action(); } catch (Exception ex) { Error(ex); }
        finally { _busy = false; _install.Enabled = _save.Enabled = _register.Enabled = true; }
    }

    private static Uri Origin(string url)
    {
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.UserInfo) ||
            u.AbsolutePath != "/" || u.Query.Length != 0 || u.Fragment.Length != 0 ||
            (u.Scheme != Uri.UriSchemeHttps && !(u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)))
            throw new InvalidOperationException("API must be HTTPS (HTTP only on localhost), with no path or credentials.");
        return u;
    }
    private static Uri Https(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Fragment)) throw new InvalidOperationException("Client archive and manifest must use HTTPS.");
        return u;
    }
    private static Uri TrustedDownloadUri(string url)
    {
        var u = new Uri(url, UriKind.Absolute);
        if (u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Fragment)) return u;
        if (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Fragment)) return u;
        throw new InvalidOperationException("HTTPS required except for localhost development.");
    }
    private void LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return;
        var s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsFile));
        if (s is null) return;
        _api.Text = s.ApiUrl; _manifest.Text = s.ManifestUrl;
    }
    private void Save()
    {
        _ = Origin(_api.Text);
        if (_manifest.Text.Length > 0) _ = TrustedDownloadUri(_manifest.Text);
        Directory.CreateDirectory(Root);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new LauncherSettings(_api.Text.TrimEnd('/'), _manifest.Text.Trim())));
    }
    // Roblox 2018M resolves shared resources relative to %LOCALAPPDATA%\UNION.
    // Both directories must be shipped in each complete client release.
    private static void InstallSharedResources(string versionDirectory)
    {
        var bundled = Path.Combine(versionDirectory, "shared");
        if (!Directory.Exists(Path.Combine(bundled, "content")) ||
            !Directory.Exists(Path.Combine(bundled, "PlatformContent", "pc")))
            throw new InvalidDataException("Client release lacks shared/content or shared/PlatformContent/pc.");

        Directory.CreateDirectory(Root);
        var stage = Path.Combine(Root, ".shared-stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Root, ".shared-backup-" + Guid.NewGuid().ToString("N"));
        var active = Path.Combine(Root, "shared");
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var directory in Directory.EnumerateDirectories(bundled, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Shared package contains a reparse point.");
                Directory.CreateDirectory(Path.Combine(stage, Path.GetRelativePath(bundled, directory)));
            }
            foreach (var file in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Shared package contains a reparse point.");
                var destination = Path.Combine(stage, Path.GetRelativePath(bundled, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            bool movedOld = false;
            if (Directory.Exists(active))
            {
                Directory.Move(active, backup);
                movedOld = true;
            }
            try { Directory.Move(stage, active); }
            catch
            {
                if (movedOld && !Directory.Exists(active) && Directory.Exists(backup))
                    Directory.Move(backup, active);
                throw;
            }
            // Retain the previous shared files as a local rollback backup.
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }
    private static string ActiveFile => Path.Combine(Versions, "installed.json");
    private static bool IsInstalled()
    {
        if (!File.Exists(ActiveFile)) return false;
        try
        {
            var m = JsonSerializer.Deserialize<ClientManifest>(File.ReadAllText(ActiveFile));
            return m is not null && Regex.IsMatch(m.Version, @"^[A-Za-z0-9._-]{1,64}$") &&
                File.Exists(Path.Combine(Versions, m.Version, "RobloxPlayerBeta.exe")) &&
                Directory.Exists(Path.Combine(Root, "shared", "content")) &&
                Directory.Exists(Path.Combine(Root, "shared", "PlatformContent", "pc"));
        }
        catch { return false; }
    }
    private async Task InstallAsync()
    {
        Save();
        var manifestUrl = TrustedDownloadUri(string.IsNullOrWhiteSpace(_manifest.Text)
            ? new Uri(Origin(_api.Text), "api/union/client/manifest").AbsoluteUri
            : _manifest.Text);
        Status("Reading client manifest...");
        using var mr = await Http.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead);
        mr.EnsureSuccessStatusCode();
        if (mr.Content.Headers.ContentLength is > 32768) throw new InvalidOperationException("Manifest is too large.");
        var manifestRaw = await mr.Content.ReadAsStringAsync();
        if (manifestRaw.Length > 32768) throw new InvalidOperationException("Manifest is too large.");
        var m = JsonSerializer.Deserialize<ClientManifest>(manifestRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing manifest.");
        if (m.Client != "2018M" || !Regex.IsMatch(m.Version ?? "", @"^[A-Za-z0-9._-]{1,64}$") ||
            m.Executable != "RobloxPlayerBeta.exe" || !Regex.IsMatch(m.Sha256 ?? "", "^[a-fA-F0-9]{64}$") ||
            m.SizeBytes <= 0 || m.SizeBytes > MaxArchiveSize) throw new InvalidDataException("Invalid client manifest.");
        var archiveUri = TrustedDownloadUri(m.DownloadUrl);
        // Only download from the same HTTPS origin as the manifest.
        if (archiveUri.GetLeftPart(UriPartial.Authority) != manifestUrl.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Archive origin differs from manifest origin.");
        Directory.CreateDirectory(Versions);
        var final = Path.Combine(Versions, m.Version!);
        if (File.Exists(Path.Combine(final, m.Executable)))
        {
            InstallSharedResources(final);
            File.WriteAllText(ActiveFile, JsonSerializer.Serialize(m));
            Status("Client resources refreshed. Click Play again on UNION.");
            return;
        }
        var stage = Path.Combine(Versions, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var zipPath = Path.Combine(stage, "client.zip");
        try
        {
            Status("Downloading 2018M...");
            using var res = await Http.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            if (res.Content.Headers.ContentLength is long length && length != m.SizeBytes)
                throw new InvalidDataException("Archive size differs from manifest.");
            using var input = await res.Content.ReadAsStreamAsync();
            await using (var output = File.Create(zipPath))
            using (var sha = SHA256.Create())
            {
                byte[] buffer = new byte[65536];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    readTotal += read;
                    if (readTotal > m.SizeBytes) throw new InvalidDataException("Archive exceeds declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    _progress.Value = Math.Clamp((int)(100.0 * readTotal / m.SizeBytes), 0, 100);
                }
                sha.TransformFinalBlock([], 0, 0);
                if (readTotal != m.SizeBytes || !Convert.ToHexString(sha.Hash!).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Archive failed size or SHA-256 verification.");
            }
            Status("Verifying and extracting archive...");
            var extract = Path.Combine(stage, "extracted");
            Directory.CreateDirectory(extract);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > 20000) throw new InvalidDataException("Too many archive entries.");
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    total = checked(total + entry.Length);
                    if (total > MaxExtractedSize) throw new InvalidDataException("Archive expands beyond allowed size.");
                    var entryName = entry.FullName.Replace('/', '\\');
                    if (entryName.StartsWith('\\') || entryName.Contains(':') || entryName.Split('\\').Any(x => x is "." or ".."))
                        throw new InvalidDataException("Unsafe ZIP entry path.");
                    var target = Path.GetFullPath(Path.Combine(extract, entryName));
                    if (!target.StartsWith(Path.GetFullPath(extract) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("ZIP path escapes destination.");
                    // Block symlinks (Unix file mode 0120000).
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Symlink entries are not supported.");
                }
                zip.ExtractToDirectory(extract);
            }
            if (!File.Exists(Path.Combine(extract, m.Executable))) throw new InvalidDataException("Client executable missing at archive root.");
            if (!Directory.Exists(Path.Combine(extract, "shared", "content")) ||
                !Directory.Exists(Path.Combine(extract, "shared", "PlatformContent", "pc")))
                throw new InvalidDataException("Client archive is incomplete: shared content/PlatformContent not found.");
            if (Directory.Exists(final)) throw new IOException("Target version folder already exists, refusing overwrite.");
            Directory.Move(extract, final);
            InstallSharedResources(final);
            File.WriteAllText(ActiveFile, JsonSerializer.Serialize(m));
            _progress.Value = 100;
            Status("2018M installed. Go back to UNION and click Play again.");
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static string LaunchCode(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "union" ||
            uri.Host != "play" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "" ||
            !Regex.IsMatch(uri.AbsolutePath, "^/[0-9a-f]{64}$")) throw new InvalidDataException("Invalid union:// link.");
        return uri.AbsolutePath[1..];
    }
    private sealed record LaunchResponse(string Client, int PlaceId, string AuthenticationUrl, string AuthenticationTicket, string JoinScriptUrl);
    private async Task LaunchAsync(string value)
    {
        var code = LaunchCode(value);
        var api = Origin(_api.Text);
        var m = JsonSerializer.Deserialize<ClientManifest>(File.ReadAllText(ActiveFile)) ?? throw new InvalidDataException("Missing installed manifest.");
        var player = Path.Combine(Versions, m.Version, "RobloxPlayerBeta.exe");
        if (!File.Exists(player)) throw new FileNotFoundException("Client not installed.", player);
        if (!Directory.Exists(Path.Combine(Root, "shared", "content")) ||
            !Directory.Exists(Path.Combine(Root, "shared", "PlatformContent", "pc")))
            throw new InvalidOperationException("Roblox shared resources are missing. Install or repair the client before Play.");
        Status("Redeeming one-time Play code...");
        using var response = await Http.PostAsJsonAsync(new Uri(api, "api/union/launch/redeem"), new { code });
        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<LaunchResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid game response.");
        if (item.Client != "2018M" || item.PlaceId != 1818 || !Regex.IsMatch(item.AuthenticationTicket ?? "", "^[0-9a-f]{64}$"))
            throw new InvalidDataException("Unexpected game ticket.");
        var auth = new Uri(item.AuthenticationUrl);
        var join = new Uri(item.JoinScriptUrl);
        if (auth.GetLeftPart(UriPartial.Authority) != api.GetLeftPart(UriPartial.Authority) ||
            join.GetLeftPart(UriPartial.Authority) != api.GetLeftPart(UriPartial.Authority) ||
            !auth.AbsolutePath.Equals("/Login/Negotiate.ashx", StringComparison.OrdinalIgnoreCase) ||
            !join.AbsolutePath.Equals("/game/Join.ashx", StringComparison.OrdinalIgnoreCase) ||
            join.Query != "?ticket=" + item.AuthenticationTicket || auth.Query != "" ||
            auth.Fragment != "" || join.Fragment != "")
            throw new InvalidDataException("Unexpected game URL.");
        var launcherUrl = new Uri(api, "game/PlaceLauncher.ashx?ticket=" + item.AuthenticationTicket).AbsoluteUri;
        var psi = new ProcessStartInfo(player)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(player)!,
            ArgumentList = { "-a", auth.AbsoluteUri, "-j", launcherUrl, "-t", "1" }
        };
        if (Process.Start(psi) is null)
            throw new InvalidOperationException("Could not start client.");
        Status("Roblox 2018M started.");
    }
    private static void RegisterProtocol()
    {
        var exe = Application.ExecutablePath;
        using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\union");
        root.SetValue("", "URL:UNION Launcher");
        root.SetValue("URL Protocol", "");
        using var command = root.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exe}\" \"%1\"");
    }
}
