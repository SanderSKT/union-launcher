# UNION Launcher

Source code for **UNION Launcher v0.2.2** — a Windows x64 / .NET 10 Windows Forms launcher for the independent UNION legacy-client project.

> **Closed alpha / test build.** UNION is not affiliated with or endorsed by Roblox Corporation. This repository does not contain Roblox client binaries or RCCService. A successful build is not a security certification or an official public release.

## Current features

- Dark themed launcher interface with **ROBLOX 2018M**, installed-version display, update check, and a website shortcut.
- An expandable **Developer settings** section for API and manifest overrides.
- Downloads a client ZIP and checks declared size and SHA-256, then installs into `%LOCALAPPDATA%\UNION\Versions\2018M`.
- Installs required 2018M shared content and supports staging / rollback of a versioned install.
- Registers `union://` for the current Windows user and redeems one-time Play codes through the UNION API.
- Detects a running Roblox instance to avoid launching duplicates.
- Includes **optional signed launcher-update support** in `LauncherSelfUpdater.cs`. It is **disabled** unless a trusted RSA update public key is supplied at build time. This GitHub workflow does not supply a key.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```powershell
dotnet publish UnionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false
```

For builds intended to connect to the public UNION API automatically:

```powershell
dotnet publish UnionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:UnionDefaultApiOrigin=https://union.cool
```

The GitHub Actions workflow embeds `https://union.cool` by default and uploads a **test-only, unsigned** Windows x64 build with `SHA256SUMS.txt` to workflow artifacts. The output executable is under `bin/Release/net10.0-windows/win-x64/publish/UnionLauncher.exe`.

A manually installed launcher from an earlier version may already be registered as the `union://` protocol handler. Do not overwrite your working handler before testing a new build in isolation.

## Repository contents

- `LauncherWindow.cs` — UI, client installation and game launch
- `LauncherSelfUpdater.cs` — optional signed launcher-update checks and verified installer downloads
- `Program.cs` and `UnionLauncher.csproj` — application entrypoint and project
- `build-release.cmd` — Windows release build
- `client-manifest.example.json` — nonfunctional sample client manifest
- `docs/LEGACY_SOURCE_README.md` — historical documentation from the older prototype

## Known limitations and security

- The example manifest has placeholders and is **not** a downloadable client release.
- A SHA-256 from the same untrusted client manifest as the archive is **not** an authentic signature of the publisher.
- The launcher is not a full standalone installer and does not guarantee a secure automatic update when no trusted key is configured.
- Independent clean-machine testing, package review and update-signing review are required before wider release.
- Do not distribute third-party clients/assets without the appropriate rights, or publish private credentials, tokens, signing keys, RCCService binaries or server configuration.

## License

Project-owned code is distributed under [GNU GPL v3.0](LICENSE). Third-party software, trademarks and assets are not granted rights under this license.
