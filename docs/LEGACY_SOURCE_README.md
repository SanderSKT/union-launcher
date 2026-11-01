# UNION Native Launcher (prototype v0.1)

Independent C# / .NET 10 Windows Forms launcher, inspired by the feature set of av7b/carbon-launcher, **not derived from its GPLv3 code**.

## What is implemented

- Graphical Windows `.exe`; no PowerShell required for players.
- `union://play/<64-lowercase-hex>` registration in HKCU.
- Existing UNION PHP API `/api/union/launch/redeem` with origin and ticket checks.
- Installs an authorized Roblox 2018M client from a same-origin HTTPS ZIP manifest; verifies size and SHA-256; limits archive size, extracted size and ZIP paths.
- Installs versioned clients under `%LOCALAPPDATA%\UNION\Versions\2018M\<version>`, stages before activation.
- Uses the known working Mesho launch format: `RobloxPlayerBeta.exe -a <authentication> -j <PlaceLauncher> -t 1`.

## Current limitations

**Prototype not yet built or tested on Windows**. No signed application binary or hosted client download is present. The example manifest contains an invalid placeholder and intentionally cannot install a client. A trusted manifest is REQUIRED: HTTPS plus SHA-256 alone does not establish authenticity if the manifest or origin is compromised. Before releasing to testers, implement signed manifests or pin a trusted manifest signing key in the launcher, file-by-file verification, robust rollback, update signing, and a separate runtime trust review.

The client's archive must place `RobloxPlayerBeta.exe` at the ZIP root. This launcher installs CLIENT files only: RCCService and PHP backend belong on the server, not each player's PC.

When an install is needed, do it BEFORE pressing Play on the website; one-time codes expire in 90 seconds and are not preserved. No external redirects are followed.

## Build on developer Windows

Install [official .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then:

```powershell
cd native-launcher
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Look in `bin/Release/net10.0-windows/win-x64/publish/`. Run as a normal user, save the local API origin and HTTPS manifest URL, then click Register union://. Use a non-production test client archive you are authorized to distribute.

**Registration replaces the current user-level `union://` protocol handler.** It can coexist with the old files but will take over browser links. Back up the old registration before testing, and don't test over the working alpha installation.

## Example manifest

```json
{
  "client": "2018M",
  "version": "2018M-1",
  "downloadUrl": "https://downloads.example.org/union/2018m-client.zip",
  "sha256": "<actual 64-character SHA-256 digest>",
  "sizeBytes": 123456789,
  "executable": "RobloxPlayerBeta.exe"
}
```

Host the manifest and archive at the same HTTPS origin. Only publish game/client assets for which you have appropriate distribution rights.

## Differences from Carbon Launcher

Carbon Launcher uses WinForms/.NET Framework 4.8, Carbon's custom URI scheme, three fixed archive URLs, auto-update via batch replacement, and simple ZIP extraction. UNION implements a separate versioned client layout, checksum checking, and a handoff based on the existing server-side single-use UNION code.
