using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static string SignedText(string version, long size, string hash, string url) =>
    "UNION-LAUNCHER-UPDATE-V1\n" + version + "\n" + size.ToString(System.Globalization.CultureInfo.InvariantCulture)
    + "\n" + hash + "\n" + url + "\n";

static void ValidateVersion(string value)
{
    if (!Regex.IsMatch(value, @"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$"))
        throw new ArgumentException("Version must use numeric major.minor.patch, for example 0.2.3.");
    _ = Version.Parse(value);
}

try
{
    if (args.Length == 2 && args[0] == "keygen")
    {
        var folder = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(folder);
        var privateKey = Path.Combine(folder, "launcher-update-private.pk8");
        var publicKey = Path.Combine(folder, "launcher-update-public.spki.b64");
        if (File.Exists(privateKey) || File.Exists(publicKey))
            throw new IOException("A key already exists. Refusing to overwrite release signing keys.");
        using var rsa = RSA.Create(3072);
        File.WriteAllBytes(privateKey, rsa.ExportPkcs8PrivateKey());
        File.WriteAllText(publicKey, Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), new UTF8Encoding(false));
        Console.WriteLine("Signing key created. KEEP PRIVATE and OFF GITHUB: " + privateKey);
        Console.WriteLine("Public key for the launcher build: " + publicKey);
    }
    else if (args.Length == 6 && args[0] == "publish")
    {
        var setup = Path.GetFullPath(args[1]);
        var version = args[2];
        var url = args[3];
        var privateKey = Path.GetFullPath(args[4]);
        var dest = Path.GetFullPath(args[5]);
        ValidateVersion(version);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.EndsWith("/api/union/launcher/installer", StringComparison.Ordinal))
            throw new ArgumentException("Installer URL must use HTTPS (HTTP only on localhost) and end with /api/union/launcher/installer.");
        if (!File.Exists(setup) || !File.Exists(privateKey))
            throw new FileNotFoundException("Missing setup or private key.");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            throw new IOException("The target release directory must be empty. Refusing overwrite.");
        using var rsa = RSA.Create();
        var key = File.ReadAllBytes(privateKey);
        rsa.ImportPkcs8PrivateKey(key, out _);
        try { CryptographicOperations.ZeroMemory(key); } catch { }
        var fi = new FileInfo(setup);
        if (fi.Length is <= 0 or > 268435456)
            throw new IOException("Installer must be between 1 byte and 256 MiB.");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup))).ToLowerInvariant();
        var signed = Encoding.UTF8.GetBytes(SignedText(version, fi.Length, hash, url));
        var signature = Convert.ToBase64String(rsa.SignData(signed, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        var manifest = new
        {
            version,
            downloadUrl = url,
            sha256 = hash,
            sizeBytes = fi.Length,
            signature
        };
        Directory.CreateDirectory(dest);
        File.Copy(setup, Path.Combine(dest, "UNIONSetup.exe"), overwrite: false);
        File.WriteAllText(Path.Combine(dest, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Signed launcher update v{version}, {fi.Length} bytes, SHA-256 {hash}");
        Console.WriteLine("Release folder: " + dest);
    }
    else if (args.Length == 4 && args[0] == "verify")
    {
        var manifestPath = Path.GetFullPath(args[1]);
        var setupPath = Path.GetFullPath(args[2]);
        var publicPath = Path.GetFullPath(args[3]);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var json = doc.RootElement;
        var version = json.GetProperty("version").GetString() ?? throw new IOException("Missing version.");
        var url = json.GetProperty("downloadUrl").GetString() ?? throw new IOException("Missing URL.");
        var sha = json.GetProperty("sha256").GetString() ?? throw new IOException("Missing digest.");
        var size = json.GetProperty("sizeBytes").GetInt64();
        var sig = json.GetProperty("signature").GetString() ?? throw new IOException("Missing signature.");
        ValidateVersion(version);
        if (!Regex.IsMatch(sha, @"^[0-9a-f]{64}$")) throw new IOException("Invalid digest.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "union.cool" ||
            uri.AbsolutePath != "/api/union/launcher/installer" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new IOException("Unexpected public installer URL.");
        var fi = new FileInfo(setupPath);
        if (!fi.Exists || fi.Length != size) throw new IOException("Installer size mismatch.");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setupPath))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(sha)))
            throw new IOException("Installer SHA-256 does not match manifest.");
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(File.ReadAllText(publicPath).Trim()), out _);
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(SignedText(version, size, sha, url)),
                Convert.FromBase64String(sig), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new IOException("SIGNATURE VERIFICATION FAILED.");
        Console.WriteLine("VERIFIED: v" + version + ", signed manifest and installer SHA-256 match pinned key.");
    }
    else
    {
        Console.Error.WriteLine("Usage:\n  keygen <private-folder>\n  publish <setup.exe> <version> <https-installer-url> <private.pk8> <empty-output-dir>\n  verify <manifest.json> <setup.exe> <public.spki.b64>");
        Environment.ExitCode = 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
    Environment.ExitCode = 1;
}
