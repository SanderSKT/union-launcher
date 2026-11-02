using System.Reflection;
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
    // Local development retains loopback; closed-alpha installer can embed https://union.cool.
    private static string PackagedApiOrigin =>
        typeof(LauncherWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "UNIONDefaultApiOrigin")?.Value ?? "http://127.0.0.1:8080";
    private readonly TextBox _api = new() { Width = 390, Text = PackagedApiOrigin };
    private readonly TextBox _manifest = new() { Width = 390 };
    private readonly Label _status = new() { AutoSize = false, Width = 510, Height = 44, ForeColor = Color.FromArgb(163, 183, 201), Font = new Font("Segoe UI", 10) };
    private readonly Label _version = new() { AutoSize = false, Width = 450, Height = 28, ForeColor = Color.FromArgb(163, 183, 201) };
    private readonly Label _percent = new() { AutoSize = false, Width = 64, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(87, 192, 244) };
    private readonly ProgressBar _progress = new() { Width = 492, Height = 12, Style = ProgressBarStyle.Continuous };
    private readonly Button _install = new() { Text = "INSTALL CLIENT", Width = 340, Height = 48 };
    private readonly Button _site = new() { Text = "OPEN WEBSITE", Width = 144, Height = 48 };
    private readonly Button _dev = new() { Text = "Developer settings", Width = 150, Height = 30 };
    private readonly Button _save = new() { Text = "Save settings", Width = 170, Height = 34 };
    private readonly Button _register = new() { Text = "Register union://", Width = 170, Height = 34 };
    private bool _busy;
    private static readonly Color Surface = Color.FromArgb(20, 30, 43);
    private static readonly Color Accent = Color.FromArgb(62, 173, 232);

    private static void StyleButton(Button b, Color bg, Color fg)
    {
        b.BackColor = bg;
        b.ForeColor = fg;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
    }
    private static Label UiLabel(string text, int x, int y, int width, int height, float size, Color color, bool bold = false)
        => new() { Text = text, Location = new Point(x,y), Size = new Size(width,height), ForeColor = color,
                   Font = new Font("Segoe UI",size,bold ? FontStyle.Bold : FontStyle.Regular) };

    public LauncherWindow(string? launchUri)
    {
        _launchUri = launchUri;
        Text = "UNION Launcher | Closed Alpha | v" + (typeof(LauncherWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.5");
        ClientSize = new Size(590, 465);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 20, 30);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);

        var brand = UiLabel("UNION", 34, 22, 330, 64, 31, Accent, true);
        var alpha = UiLabel("CLOSED ALPHA  /  CLASSIC 2018M", 38, 82, 410, 23, 9, Color.FromArgb(137, 162, 181));
        var line = new Panel { Location = new Point(35, 116), Size = new Size(520, 1), BackColor = Color.FromArgb(48, 69, 85) };

        var panel = new Panel { Location = new Point(26, 136), Size = new Size(538, 273), BackColor = Surface };
        var game = UiLabel("ROBLOX 2018M", 25, 22, 450, 42, 20, Color.White, true);
        var subtitle = UiLabel("Classic Roblox experience • UNION V2", 27, 68, 470, 26, 10, Color.FromArgb(151, 173, 190));
        _version.Location = new Point(27, 112);
        _status.Location = new Point(27, 145);
        _progress.Location = new Point(27, 196);
        _percent.Location = new Point(445, 175);
        _install.Location = new Point(26, 218);
        _site.Location = new Point(375, 218);
        StyleButton(_install, Color.FromArgb(25, 139, 201), Color.White);
        StyleButton(_site, Color.FromArgb(45, 60, 77), Color.White);
        _progress.ForeColor = Accent;
        panel.Controls.AddRange([game, subtitle, _version, _status, _progress, _percent, _install, _site]);

        _dev.Location = new Point(32, 420);
        _dev.FlatStyle = FlatStyle.Flat;
        _dev.FlatAppearance.BorderSize = 0;
        _dev.ForeColor = Color.FromArgb(151, 173, 190);
        _dev.BackColor = BackColor;
        _dev.Cursor = Cursors.Hand;
        Controls.AddRange([brand, alpha, line, panel, _dev]);

        _install.Click += async (_, _) => await RunAsync(async () =>
        {
            if (IsInstalled()) await CheckForUpdatesAsync(manual: true);
            else await InstallAsync();
        });
        _site.Click += (_, _) => OpenWebsite();
        _dev.Click += (_, _) => OpenDeveloperSettings();
        _save.Click += (_, _) => { try { Save(); Status("Settings saved."); } catch (Exception ex) { Error(ex); } };
        _register.Click += (_, _) => { try { RegisterProtocol(); Status("union:// registered for this Windows user."); } catch (Exception ex) { Error(ex); } };
        LoadSettings();
        RefreshClientState();
        Shown += async (_, _) =>
        {
            if (!IsInstalled())
            {
                // Explicit consent before the first download; never redeem a
                // one-time Play code while the game client is still installing.
                var answer = MessageBox.Show(this,
                    "UNION 2018M is not installed. Download and install the client from the configured UNION server now?",
                    "Install UNION 2018M",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                {
                    await RunAsync(() => InstallAsync());
                    if (_launchUri is not null && IsInstalled())
                        Status("Client installed. Return to the website and click Play again to get a fresh ticket.");
                }
                else
                {
                    Status("Client installation skipped. You can install it from this window.");
                }
                return;
            }

            if (_launchUri is not null)
            {
                await RunAsync(async () =>
                {
                    // Check BEFORE redeeming the one-use Play ticket.
                    if (await CheckForUpdatesAsync(playRequest: true))
                        await LaunchAsync(_launchUri);
                });
            }
            else
            {
                await RunAsync(async () =>
                {
                    await CheckForUpdatesAsync();
                    await CheckLauncherUpdateAsync();
                });
            }
        };
    }

    private async Task CheckLauncherUpdateAsync()
    {
        if (!LauncherSelfUpdater.IsConfigured) return; // Unsigned development builds never self-update.
        LauncherUpdateManifest? release;
        try
        {
            release = await LauncherSelfUpdater.CheckAsync(Http, Origin(_api.Text));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A temporarily offline update service must not block the game client.
            return;
        }

        if (release is null) return;
        var answer = MessageBox.Show(this,
            "A new UNION Launcher is available: v" + release.Version + Environment.NewLine +
            "Installer: " + (release.SizeBytes / 1048576.0).ToString("F1") + " MiB" + Environment.NewLine +
            "The release manifest is signed and checked against the pinned UNION key." + Environment.NewLine +
            "Roblox files and local settings will be kept." + Environment.NewLine + Environment.NewLine +
            "Download the verified installer and update now?",
            "UNION Launcher Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            Status("Launcher update v" + release.Version + " skipped.");
            return;
        }
        EnsureNoRobloxAlreadyRunning();
        var verified = await LauncherSelfUpdater.DownloadVerifiedAsync(
            Http, Origin(_api.Text), release,
            progress => { if (!IsDisposed) { _progress.Value = progress; _percent.Text = progress + "%"; } },
            Status);
        LauncherSelfUpdater.LaunchInstaller(verified);
        Status("Installer opened. Closing launcher.");
        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(Close));
    }

    private void RefreshClientState(bool preserveStatus = false)
    {
        if (IsInstalled())
        {
            string version = "2018M";
            try { version = JsonSerializer.Deserialize<ClientManifest>(File.ReadAllText(ActiveFile))?.Version ?? version; }
            catch { }
            _version.Text = "INSTALLED  •  " + version;
            if (!_busy && !preserveStatus) _status.Text = "Client is ready. Open UNION to join a game.";
            _install.Text = "CHECK FOR UPDATES";
        }
        else
        {
            _version.Text = "NOT INSTALLED";
            if (!_busy && !preserveStatus) _status.Text = "Install the classic client to start playing.";
            _install.Text = "INSTALL CLIENT";
        }
    }
    private void OpenWebsite()
    {
        try
        {
            var api = Origin(_api.Text);
            // In local development Vite serves the frontend on port 3001, not the PHP API (8080).
            // In deployed HTTPS environments the frontend and API are expected on one origin.
            var website = api.IsLoopback && api.Port == 8080
                ? new UriBuilder(api) { Port = 3001, Path = "/games" }.Uri
                : new Uri(api, "games");
            Process.Start(new ProcessStartInfo(website.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { Error(ex); }
    }
    private void OpenDeveloperSettings()
    {
        using var dialog = new Form
        {
            Text = "UNION • Developer settings", ClientSize = new Size(455, 265),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        var l1 = UiLabel("UNION API origin", 23, 17, 390, 24, 10, Color.White);
        _api.Location = new Point(23, 45);
        var l2 = UiLabel("Override manifest (empty = automatic)", 23, 87, 410, 24, 10, Color.White);
        _manifest.Location = new Point(23, 115);
        _save.Location = new Point(23, 167);
        _register.Location = new Point(216, 167);
        StyleButton(_save, Color.FromArgb(25, 139, 201), Color.White);
        StyleButton(_register, Color.FromArgb(45, 60, 77), Color.White);
        var note = UiLabel("Developer-only settings. Leave manifest blank for normal use.", 23, 222, 410, 30, 8, Color.FromArgb(151,173,190));
        dialog.Controls.AddRange([l1, _api, l2, _manifest, _save, _register, note]);
        dialog.ShowDialog(this);
        dialog.Controls.Remove(_api); dialog.Controls.Remove(_manifest);
        dialog.Controls.Remove(_save); dialog.Controls.Remove(_register);
    }
    private void Status(string message) { if (!IsDisposed) _status.Text = message; }
    private void Error(Exception ex) { Status("Error: " + ex.Message); MessageBox.Show(this, ex.Message, "UNION Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _install.Enabled = _site.Enabled = _dev.Enabled = false;
        bool completed = false;
        try { await action(); completed = true; }
        catch (Exception ex) { Error(ex); }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                _install.Enabled = _site.Enabled = _dev.Enabled = true;
                if (completed && _launchUri is null) RefreshClientState(preserveStatus: true);
            }
        }
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
            !Directory.Exists(Path.Combine(bundled, "PlatformContent", "pc")) ||
            !File.Exists(Path.Combine(bundled, "shaderM", "shaderM_glsl3.pack")))
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
                Directory.Exists(Path.Combine(Root, "shared", "PlatformContent", "pc")) &&
                File.Exists(Path.Combine(Root, "shared", "shaderM", "shaderM_glsl3.pack"));
        }
        catch { return false; }
    }
    private Uri ClientManifestUri() =>
        TrustedDownloadUri(string.IsNullOrWhiteSpace(_manifest.Text)
            ? new Uri(Origin(_api.Text), "api/union/client/manifest").AbsoluteUri
            : _manifest.Text.Trim());

    private static ClientManifest? InstalledManifest()
    {
        try { return File.Exists(ActiveFile) ? JsonSerializer.Deserialize<ClientManifest>(File.ReadAllText(ActiveFile)) : null; }
        catch (JsonException) { return null; }
    }

    private async Task<ClientManifest> FetchClientManifestAsync()
    {
        var manifestUrl = ClientManifestUri();
        Status("Checking for client updates...");
        // The Play ticket expires quickly. Limit manifest checks to eight seconds,
        // never run a full client download after redeeming the ticket.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var mr = await Http.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        mr.EnsureSuccessStatusCode();
        if (mr.Content.Headers.ContentLength is > 32768) throw new InvalidDataException("Manifest is too large.");
        var raw = await mr.Content.ReadAsStringAsync(timeout.Token);
        if (raw.Length > 32768) throw new InvalidDataException("Manifest is too large.");
        var m = JsonSerializer.Deserialize<ClientManifest>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing client manifest.");
        if (m.Client != "2018M" || !Regex.IsMatch(m.Version ?? "", @"^2018M-[0-9]{1,8}$") ||
            m.Executable != "RobloxPlayerBeta.exe" || !Regex.IsMatch(m.Sha256 ?? "", "^[a-fA-F0-9]{64}$") ||
            m.SizeBytes <= 0 || m.SizeBytes > MaxArchiveSize)
            throw new InvalidDataException("Invalid client manifest.");
        var archiveUri = TrustedDownloadUri(m.DownloadUrl);
        if (!string.Equals(archiveUri.GetLeftPart(UriPartial.Authority),
             manifestUrl.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive and manifest must share the same server origin.");
        return m;
    }

    private static int VersionNumber(string version)
    {
        var match = Regex.Match(version ?? "", @"^2018M-([0-9]{1,8})$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var value))
            throw new InvalidDataException("Unsupported installed client version; expected 2018M-N.");
        return value;
    }

    // Returns true when the installed client is ready for Play.
    // A new release requires explicit confirmation. When Play was requested,
    // installation deliberately consumes NO Play ticket; the user clicks Play again.
    private async Task<bool> CheckForUpdatesAsync(bool playRequest = false, bool manual = false)
    {
        var installed = InstalledManifest();
        if (installed is null || !IsInstalled()) return false;
        ClientManifest available;
        try { available = await FetchClientManifestAsync(); }
        catch (Exception ex) when (!manual && (ex is HttpRequestException || ex is TaskCanceledException))
        {
            Status("Update check unavailable. Using installed " + installed.Version + ".");
            return true;
        }

        if (string.Equals(available.Version, installed.Version, StringComparison.Ordinal))
        {
            if (!string.Equals(available.Sha256, installed.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The server changed an existing release. Publish a new version instead.");
            Status(installed.Version + " is up to date.");
            return true;
        }

        if (VersionNumber(available.Version) <= VersionNumber(installed.Version))
        {
            Status("Server version " + available.Version + " is not newer than " + installed.Version + ".");
            return true;
        }

        var answer = MessageBox.Show(this,
            "A new UNION client is available." + Environment.NewLine +
            "Installed: " + installed.Version + Environment.NewLine +
            "Available: " + available.Version + Environment.NewLine +
            "Download size: " + (available.SizeBytes / 1048576.0).ToString("F1") + " MiB" +
            Environment.NewLine + Environment.NewLine +
            (playRequest ? "The current Play request will need to be repeated after installation." + Environment.NewLine : "") +
            "Download and install this update?",
            "UNION Client Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            Status("Update " + available.Version + " is available; installation skipped.");
            return !playRequest;
        }

        // Do not update shared resources while any Roblox instance is active.
        EnsureNoRobloxAlreadyRunning();
        await InstallAsync(available);
        Status(available.Version + " installed. Please click Play on the website again.");
        return false;
    }

    private static void ActivateClient(ClientManifest manifest)
    {
        Directory.CreateDirectory(Versions);
        var temporary = Path.Combine(Versions, ".installed-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest));
            if (File.Exists(ActiveFile))
                File.Replace(temporary, ActiveFile, Path.Combine(Versions, "installed.previous.json"));
            else
                File.Move(temporary, ActiveFile);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task InstallAsync(ClientManifest? selected = null)
    {
        Save();
        var m = selected ?? await FetchClientManifestAsync();
        var archiveUri = TrustedDownloadUri(m.DownloadUrl);
        Directory.CreateDirectory(Versions);
        var final = Path.Combine(Versions, m.Version);
        if (Directory.Exists(final))
            throw new IOException("Version " + m.Version + " already has a folder. Refusing to trust a partially installed release.");
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
                    _percent.Text = _progress.Value + "%";
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
                !Directory.Exists(Path.Combine(extract, "shared", "PlatformContent", "pc")) ||
                !File.Exists(Path.Combine(extract, "shared", "shaderM", "shaderM_glsl3.pack")))
                throw new InvalidDataException("Client archive is incomplete: shared content/PlatformContent not found.");
            if (Directory.Exists(final)) throw new IOException("Target version folder already exists, refusing overwrite.");
            Directory.Move(extract, final);
            InstallSharedResources(final);
            ActivateClient(m);
            _progress.Value = 100;
            _percent.Text = "100%";
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
    // Prevent another Roblox instance from being launched by this Windows launcher.
    // This is a client-side convenience check, not a server-side account limit.
    private static void EnsureNoRobloxAlreadyRunning()
    {
        foreach (var processName in new[] { "RobloxPlayerBeta", "RobloxPlayer", "CarbonPlayerBeta" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    bool running;
                    try { running = !process.HasExited; }
                    catch (InvalidOperationException) { continue; } // Process exited during enumeration.
                    if (running)
                        throw new InvalidOperationException(
                            $"Roblox is already running (PID {process.Id}). Close the existing game before pressing Play again.");
                }
            }
        }
    }

    private sealed record LaunchResponse(string Client, int PlaceId, string AuthenticationUrl, string AuthenticationTicket, string JoinScriptUrl);
    private async Task LaunchAsync(string value)
    {
        // A named Windows semaphore serializes simultaneous union:// handoffs
        // across launcher processes. Unlike a mutex, it can be released on a
        // different thread after an await.
        var code = LaunchCode(value);
        using var launchGate = new Semaphore(1, 1, @"Local\UNION_2018M_LaunchGuard");
        if (!launchGate.WaitOne(0))
            throw new InvalidOperationException("Another UNION Play launch is in progress. Try again after it finishes.");

        try
        {
            // Check BEFORE redeeming the 90-second single-use play ticket.
            EnsureNoRobloxAlreadyRunning();
            await StartClientWithTicketAsync(code);
        }
        finally
        {
            launchGate.Release();
        }
    }

    private async Task StartClientWithTicketAsync(string code)
    {
        var api = Origin(_api.Text);
        var m = JsonSerializer.Deserialize<ClientManifest>(File.ReadAllText(ActiveFile)) ?? throw new InvalidDataException("Missing installed manifest.");
        var player = Path.Combine(Versions, m.Version, "RobloxPlayerBeta.exe");
        if (!File.Exists(player)) throw new FileNotFoundException("Client not installed.", player);
        if (!Directory.Exists(Path.Combine(Root, "shared", "content")) ||
            !Directory.Exists(Path.Combine(Root, "shared", "PlatformContent", "pc")) ||
            !File.Exists(Path.Combine(Root, "shared", "shaderM", "shaderM_glsl3.pack")))
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
        // A Roblox client may have appeared while the API ticket was fetched.
        EnsureNoRobloxAlreadyRunning();
        using var clientProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start Roblox 2018M.");

        // Check for an immediate launch failure before closing the bootstrapper.
        // This confirms a running process, NOT a completed game-server join.
        await Task.Delay(1000);
        if (clientProcess.HasExited)
            throw new InvalidOperationException("Roblox 2018M exited immediately after launch. Check Roblox logs.");

        Status("Roblox 2018M started. Closing launcher.");
        // Post the close so RunAsync can complete its cleanup first.
        if (!IsDisposed && IsHandleCreated)
            BeginInvoke(new Action(Close));
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
