# Releasing

How a version of Avala reaches people: the packages, how a release is cut, signing, and how a running Avala learns that a newer one is out. Nothing here publishes on its own: a release is a draft until the owner publishes it.

## Versions

A version is a tag `v<major>.<minor>.<patch>`, with an optional prerelease such as `v0.1.0-beta.1`. The tag is the only source of a release's version: the packaging script passes it to every project as `-p:Version`, so the informational version, which About shows and the update check compares, reads `0.1.0-beta.1+<commit>`. A build from a checkout without a tag is `0.1.0-dev`, from `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`; raise the prefix after a release.

A tag with a hyphen is drafted as a prerelease. A stable build of Avala is only ever offered stable releases, and a prerelease build is offered prereleases too.

## The packages

| Platform | Runner | Archive | What is inside | Smoke run in CI |
| --- | --- | --- | --- | --- |
| Windows x64 | `windows-latest` | `avala-<version>-win-x64.zip` | `Avala/`: `Avala.exe`, the runtime, `plugins/` | yes |
| Linux x64 | `ubuntu-latest` | `avala-<version>-linux-x64.tar.gz` | `avala-<version>/`: `Avala`, the runtime, `plugins/`, `avala.png`, `avala.desktop` | yes |
| macOS Apple silicon | `macos-latest` | `avala-<version>-osx-arm64.zip` | `Avala.app`, with `Contents/Info.plist`, `Contents/Resources/avala.icns` and everything else in `Contents/MacOS` | yes |
| macOS Intel | `macos-latest` | `avala-<version>-osx-x64.zip` | the same bundle for x64 | no, cross-built on Apple silicon |

Every archive has a `.sha256` next to it. Each package is self-contained, so nothing has to be installed first, and is a folder rather than a single file: the plugins are folders the host loads at startup, and the host's own assemblies must stay visible to them. The `plugins` folder sits next to the executable, where the host looks first.

To keep the packages small, the script removes what the plugin loader never reads: native libraries for other platforms, a plugin's copy of an assembly the host already ships, and a file identical to one an earlier plugin already holds. The Linux package was 51 MB compressed when this was written, macOS 52 MB and Windows 79 MB.

An AppImage, a Windows installer and a macOS disk image come later. An AppImage is a SquashFS image behind the AppImage runtime, which the script would have to write from C# without calling `mksquashfs` or `appimagetool`, so it waits until that is worth its code.

### Building one locally

```
dotnet run scripts/package.cs -- --rid linux-x64 --smoke
```

`--rid` is one of `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`, and any of them can be built from any system, but signing needs its own. `--version 0.1.0-beta.1` sets the version; without it the script takes the tag the workflow was started by, then the tag on `HEAD`, then `0.1.0-dev`. `--output <folder>` chooses where the archives go, `artifacts/packages` by default, and the work folder is `artifacts/package/<rid>`. `--smoke` runs the packaged `--version` and `--smoke` when the platform is the one running the script. `--sign` signs, see below.

### The smoke run

`Avala --smoke` starts the real application on Avalonia's headless platform, so it needs no display: it composes every plugin, runs the startup tasks and the migrations in a temporary data folder, shows the main window, prints `Avala <version> composed <n> pages and showed its main window.` and exits 0. With no plugin found it exits 2, on a failure 1. It never checks for updates. `Avala --version` prints `Avala <version> (<commit>)`. Use both to check a package on a machine before trusting it.

## Cutting a release

1. Make sure `main` is green in CI and holds everything the release should.
2. Choose the version. The first public one is suggested as `v0.1.0-beta.1`.
3. Tag the commit and push the tag:

   ```
   git tag -a v0.1.0-beta.1 -m "Avala 0.1.0-beta.1"
   git push origin v0.1.0-beta.1
   ```

4. The [release workflow](../.github/workflows/release.yml) runs on that tag only. It builds and tests the tagged commit, packages each platform on its own runner with the smoke run, and creates a **draft** release named after the tag with the archives, their checksums and notes generated from the merged pull requests.
5. Open the draft on GitHub, edit the notes, download a package or two and run `--smoke` on them, then publish it. Publishing is the only step that makes it visible, and the only one the update check sees.
6. Replace "Coming soon" in the README's Install section with a link to the release, and raise `VersionPrefix` in `Directory.Build.props`.

To redo a release before publishing it, delete the draft and the tag, locally and on GitHub, fix what was wrong and tag again.

## Signing

The packages are unsigned until the owner provides certificates. Until then Windows SmartScreen warns about an unknown publisher, and macOS refuses an app downloaded from the internet that is not notarized: open it with right-click, Open, or clear the quarantine with `xattr -dr com.apple.quarantine Avala.app`. Linux packages are not signed; their checksums are in the release.

The workflow and `scripts/package.cs` already hold the signing steps. They run only when the repository variable `AVALA_SIGNING` is `true`; otherwise the unsigned step runs. With `--sign`, a missing secret stops the build and names it.

### Windows

An Authenticode code signing certificate, ideally an EV or an Azure Trusted Signing identity, exported as a PFX.

| Secret | Value |
| --- | --- |
| `AVALA_WINDOWS_CERTIFICATE` | the PFX, base64 encoded |
| `AVALA_WINDOWS_CERTIFICATE_PASSWORD` | its password |

The script signs `Avala.exe` and Avala's own assemblies with `signtool` from the Windows SDK, SHA-256, timestamped by DigiCert. A certificate kept in a hardware token or in Azure Trusted Signing needs that provider's signing step instead; replace `SignWindowsAsync` in the script, keeping the step behind the same variable.

### macOS

An Apple Developer ID Application certificate, and an app-specific password of the Apple ID that notarizes.

| Secret | Value |
| --- | --- |
| `AVALA_MACOS_CERTIFICATE` | the Developer ID Application certificate and its key, exported as `.p12`, base64 encoded |
| `AVALA_MACOS_CERTIFICATE_PASSWORD` | the `.p12` password |
| `AVALA_MACOS_SIGNING_IDENTITY` | the identity, such as `Developer ID Application: Name (TEAMID)` |
| `AVALA_APPLE_ID` | the Apple ID that notarizes |
| `AVALA_APPLE_TEAM_ID` | the team identifier |
| `AVALA_APPLE_APP_PASSWORD` | an app-specific password of that Apple ID |

The script imports the certificate into a temporary keychain, signs every native library and the executable with the hardened runtime and the entitlements .NET needs, JIT, unsigned executable memory and libraries signed by others, since the plugins load their own, then the bundle; it submits the bundle to the notary service, waits for the verdict and staples the ticket before the zip is made.

To encode a certificate for a secret: `base64 -i certificate.p12 | pbcopy` on macOS, or `[Convert]::ToBase64String([IO.File]::ReadAllBytes("certificate.pfx"))` in PowerShell. Then set the secrets under Settings, Secrets and variables, Actions, and the variable `AVALA_SIGNING` to `true`.

## The update check

Avala tells a person when a newer version is out; it never downloads or installs one.

- **When.** Once at startup, in the background: the startup task returns at once and the window never waits on the network. Again whenever the person chooses Check now in Settings, About.
- **What it asks.** `GET https://api.github.com/repos/rick-dev-creator/avala/releases`, with an `Avala` user agent and nothing else: no identifier, no usage, no telemetry. Drafts are never listed to anonymous requests and are ignored anyway.
- **What it shows.** A newer version appears in the sidebar's footer, "Version x is available", and in About with Download; both open the release's page in the browser through `ILinkOpener`. About also says when Avala is up to date, when GitHub could not be reached, and when the startup check is off.
- **Turning it off.** A machine setting, `updates.json` in the data folder:

  ```json
  { "checkOnStartup": false }
  ```

  A file that cannot be read, or holds anything but that object, also turns the startup check off. Check now in About still asks, since the person asked.
- **Where it lives.** `UpdateCheck` in `Avala.Runtime.Updates`, behind the SDK's `IUpdates`; only the host's real composition gives it the HTTP feed, so tests, the simulation and the smoke run never reach the network. The design is in [release prep](design/core.md#release-prep).
