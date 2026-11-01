# UNION Launcher

Official source repository for the **UNION** Windows launcher.

UNION is an independent community project focused on preserving and exploring legacy Roblox client experiences. It is **not affiliated with or endorsed by Roblox Corporation**.

## Project status

**Source import pending.** The current native Windows launcher source code and buildable project have not yet been committed to this repository. There is no verified launcher release here at this time.

The legacy PowerShell bootstrapper in UNION V2 is a different component. It is not a substitute for the native launcher or its automatic client installation functionality.

## Planned publication

The native launcher source will be published here after an audit for credentials, private signing keys, embedded tokens, and unintended bundled binaries. A release may be published only after a build is produced and tested from the committed source.

For future releases, verify file hashes and obtain executables only from links published by the project maintainers.

## Development and security

- Never commit private API credentials, signing keys, session tokens, local user data, or production configuration.
- Do not commit Roblox game client binaries, RCCService binaries, or third-party assets without the right to distribute them.
- The launcher must treat remote update manifests and downloaded archives as untrusted until their authenticity and integrity have been verified.
- Source code in this repository does not grant rights to third-party software.

## License

Project-owned code released in this repository is licensed under the [GNU General Public License v3.0](LICENSE), except where otherwise noted. This license does not apply to third-party software or assets.
