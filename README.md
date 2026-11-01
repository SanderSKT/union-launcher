# UNION Launcher

Source code for the **UNION native launcher** for Windows, from the 1 November 2026 Roblox 2018M-2 local milestone.

> **Alpha / testing only.** The original Windows setup was reported to have successfully installed and launched 2018M-2, but this repository has **not** passed a clean-machine security and deployment review. There is no validated public release yet.

UNION is an independent community legacy-client project. It is **not affiliated with or endorsed by Roblox Corporation**.

## Features in this source checkpoint

- C# Windows Forms launcher targeting .NET 10 (Windows x64).
- Installs a versioned client under `%LOCALAPPDATA%\\UNION\\Versions\\2018M` after fetching a client manifest and ZIP from an authorized endpoint.
- Checks advertised archive size and SHA-256 before extraction; restricts ZIP paths, symlinks, file counts, and extraction size.
- Installs `shared/content` and `shared/PlatformContent/pc` for 2018M.
- Registers the `union://` scheme for the current Windows user.
- Receives a one-use launch code from the website and redeems it through the configured UNION API.
- Starts `RobloxPlayerBeta.exe` using the authenticated PlaceLauncher arguments.

**Known limitations:** Before using the site Play flow, a fresh installation must be completed separately. This source checkpoint does not contain a tested launcher auto-updater, signed manifests, comprehensive per-file integrity checks, rollback guarantees, or installer/uninstaller. Do not assume it includes later local-only patches such as automatically closing its own window or preventing multiple Roblox processes.

## Source provenance

Copied from the private `union-v2` source checkpoint at Git commit `f1702b5bb280fe598f03330478ac94b39ab38226`, branch `milestone/native-launcher-2018m2-working-2026-11-01`, `native-launcher/` directory. `docs/LEGACY_SOURCE_README.md` contains the original README from that snapshot.

The milestone documented a successful local launch with the 2018M-2 client package; it was **not** a public release certification. The client ZIP, private server configuration, PHP backend, RCCService, session state, and signing keys are not included.

## Build

Requires the official [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```powershell
dotnet publish UnionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false
```

Output: `bin/Release/net10.0-windows/win-x64/publish/UnionLauncher.exe`.

GitHub Actions builds the same source on `windows-latest`. Find the test-only EXE and `SHA256SUMS.txt` in the successful run's **Artifacts**. The workflow does not create an official GitHub Release automatically, sign the binary, or guarantee the binary is safe for distribution.

## Security & distribution

- Never upload credentials, production configs, session tokens, private keys, client archives, or RCCService binaries.
- TLS and a SHA-256 listed **in the same manifest** are not a substitute for a cryptographically authenticated release manifest.
- Review and test the installer, client package and manifest retrieval on a clean Windows installation before public distribution.
- The launcher source license does not grant rights to distribute third-party Roblox binaries or assets.
- Registering `union://` replaces the user's existing handler; avoid testing on a production/working alpha machine.

## License

Project-owned source code is published under the [GNU General Public License v3.0](LICENSE). Third-party code, Roblox client files, trademarks and assets are not licensed by this repository.
