# UNION Launcher

Official source for the Windows **UNION Launcher** — an independent, closed-alpha legacy-client project. Not affiliated with Roblox Corporation.

## Features

- C# WinForms / .NET 10 (Windows x64), classic Roblox 2018M install and `union://` website Play.
- Client archive download with size/SHA-256 verification, versioned installation and shared resources.
- Installed-client status, update checks, Developer settings and duplicate-process guard.
- **Launcher self-update:** verifies a signed manifest with an embedded RSA-PSS/SHA-256 public key, verifies the downloaded Inno Setup installer's size/hash, then asks permission to install it.
- `https://union.cool` is the default API for CI-built binaries. Developer builds can still use a local origin.

## Self-update bootstrap (v0.2.3)

**Signing is disabled until the project owner generates and commits the PUBLIC verification key.**

On a trusted Windows PC with .NET 10 SDK and Git, clone this repo. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-UpdateSigning.ps1
```

This generates an RSA key pair **outside this repository**, under your user profile's `UNION-Private-Backups/launcher-update-signing/`. It copies **only the public key** to `update-signing/launcher-update-public.spki.b64` inside the repo. Verify `git status` and commit only this PUBLIC file. Back up the private key encrypted and keep it offline. It is needed to sign all subsequent updates, and losing it means existing launchers will not trust replacement keys.

The next GitHub Actions build will embed the public key. It will produce both a portable test EXE and an unsigned **Inno Setup installer** (each with SHA-256 checksums). Download `union-setup-win-x64-unsigned` from Actions, extract `UNIONSetup.exe`, and test it on the existing Windows VM or a separate Windows account. This is a **one-time manual installation** for existing launchers without an embedded key. The installer preserves client files in `%LOCALAPPDATA%/UNION`, uses the same AppId for subsequent upgrades, and registers `union://` with the user's consent if another handler already owns it.

## Subsequent update releases (e.g. v0.2.4)

1. Update the source and bump `<Version>` in `UnionLauncher.csproj` (never reuse an existing version number for different bytes).
2. Build/download the versioned `UNIONSetup.exe` with the **same pinned public key**.
3. On the trusted PC, sign a manifest offline:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Sign-LauncherRelease.ps1 -Version 0.2.4 -SetupFile "C:\path\to\UNIONSetup.exe"
```

4. Copy only `UNIONSetup.exe` and `manifest.json` from `installer/signed-releases/0.2.4/` to the VPS. **Never upload the private key.**
5. On the VPS, install the isolated PHP routes once using `server/Install-UpdateRoutes.ps1`, then use `server/Stage-UpdateRelease.ps1 -SourceDirectory ...` to stage a release. The update API defaults to **disabled**. Only after verifying the deployment should you set `launcher_updates_enabled => true` in your private PHP config.
6. Launch the signed-key bootstrap installer normally. On subsequent starts it checks `https://union.cool/api/union/launcher/update`. If an update is available and its signature verifies, the user can install it.

The official public endpoint paths are `GET /api/union/launcher/update` and `GET /api/union/launcher/installer`. The release installer and manifest remain outside the backend web root. The manifest signature covers version, size, SHA-256 and URL and uses a **fixed key embedded in the launcher**, not a key provided by the server.

## Build locally

```powershell
dotnet publish UnionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:UnionDefaultApiOrigin=https://union.cool
```

Without an embedded signing key this is a **development-only build** and cannot self-update. The project version is `0.2.3` for the bootstrap candidate; no release has been certified merely by committing these files.

## Security boundaries

- Keep the private RSA signing key outside GitHub, CI and the VPS. Only the public key is distributed.
- GitHub Actions artifacts and Inno Setup output are **not Authenticode signed**; SmartScreen warnings may still occur.
- A client ZIP hash from the same unsigned manifest does not establish publisher authenticity. Review client distribution separately.
- Updating only replaces the launcher installation. It must not delete Roblox client files, RCCService or user settings.
- A previous unsigned/unkeyed launcher cannot self-update into the signed-key updater. One manual installer upgrade is required.
- Review third-party client and asset distribution rights before public release.

License: [GNU GPL v3.0](LICENSE) for project-owned code only.
