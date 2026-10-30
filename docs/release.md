# Releasing

How a version of Avala reaches people: the packages, how a release is cut, its usage statistics, signing, and how a running Avala learns that a newer one is out. Nothing here publishes on its own: a release is a draft until the owner publishes it.

## Versions

A version is a tag `v<major>.<minor>.<patch>`, with an optional prerelease such as `v0.1.0-beta.1`. The tag is the only source of a release's version: the packaging script passes it to every project as `-p:Version`, so the informational version, which About shows and the update check compares, reads `0.1.0-beta.1+<commit>`. A build from a checkout without a tag is `0.2.0-dev`, from `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`; raise the prefix after a release.

A tag with a hyphen is drafted as a prerelease. A stable build of Avala is only ever offered stable releases, and a prerelease build is offered prereleases too.

## The packages

Each release holds these files, each with a `.sha256` next to it:

| Platform | File | What it is | Smoke run in CI |
| --- | --- | --- | --- |
| Windows x64 | `avala-<version>-win-x64-setup.exe` | the installer, with Velopack | no, it installs |
| Windows x64 | `avala-<version>-win-x64-portable.zip` | the same app to unzip anywhere, updating itself in place | the folder inside, yes |
| Linux x64 | `avala-<version>-linux-x64.AppImage` | one file to run, updating itself in place | yes |
| Linux x64 | `avala-<version>-linux-x64.tar.gz` | the plain folder, for package managers and by hand | yes |
| Arch Linux | `avala-bin-<version>.PKGBUILD`, `avala-bin-<version>.SRCINFO` | the AUR recipe of `avala-bin`, built from the tar.gz | no |

and the Velopack update feed: `releases.win.json`, `releases.linux.json`, the full packages `Avala.Desktop-<version>-full.nupkg` and `Avala.Desktop-<version>-linux-full.nupkg`, and their deltas `…-delta.nupkg` once an earlier release has a package. A running Avala reads only the feed; people never download it themselves.

The archives the installers are made from:

| Platform | Runner | Archive | What is inside |
| --- | --- | --- | --- |
| Windows x64 | `windows-latest` | `avala-<version>-win-x64.zip`, not attached to the release | `Avala/`: `Avala.exe`, the runtime, `plugins/` |
| Linux x64 | `ubuntu-latest` | `avala-<version>-linux-x64.tar.gz` | `avala-<version>/`: `Avala`, the runtime, `plugins/`, `avala.png`, `icons/hicolor/` (16 to 512 px and scalable), `avala.desktop`, `LICENSE` |

The release workflow packages Windows and Linux only, starting with `v0.1.0-beta.1`. The script still builds the macOS bundles below, and they return to the release once they are signed and notarized: add their two entries back to the workflow's `package` matrix, `osx-arm64` and `osx-x64`, both on `macos-latest`.

| Platform | Archive | What is inside | Smoke run |
| --- | --- | --- | --- |
| macOS Apple silicon | `avala-<version>-osx-arm64.zip` | `Avala.app`, with `Contents/Info.plist`, `Contents/Resources/avala.icns` and everything else in `Contents/MacOS` | yes, on Apple silicon |
| macOS Intel | `avala-<version>-osx-x64.zip` | the same bundle for x64 | no, cross-built on Apple silicon |

Every archive has a `.sha256` next to it. Each package is self-contained, so nothing has to be installed first, and is a folder rather than a single file: the plugins are folders the host loads at startup, and the host's own assemblies must stay visible to them. The `plugins` folder sits next to the executable, where the host looks first.

Everything is precompiled with ReadyToRun so Avala starts without compiling most of its code first: the host is published as one composite image with the .NET runtime and its own dependencies, such as Avalonia, and each plugin is published for the package's runtime with `PublishReadyToRun`, which precompiles the plugin and its own dependencies, such as EF Core, Markdig and MarkView. Crossgen2 targets any platform from any other, so every package still builds on any runner. Measured on Linux, the window's first frame came after about 2,700 methods were compiled instead of 19,600, in 0.9 s instead of 2.0 s, and opening a job took half as long; the archive grew from 55 MB to 84 MB.

To keep the packages small, the script removes what the plugin loader never reads: native libraries for other platforms, a plugin's copy of an assembly the host already ships, and a file identical to one an earlier plugin already holds. With ReadyToRun the Linux package is 84 MB compressed, macOS on Apple silicon 82 MB and Windows 111 MB.

### The production plugin set

A package holds every plugin project under `src/Modules` that declares `<AvalaPlugin>true</AvalaPlugin>`, except those that also declare `<AvalaDevelopmentOnly>true</AvalaDevelopmentOnly>`. Those exist only to develop and test Avala, and no package, installer, AUR package or Flatpak holds them, since all of them are made from the archives `scripts/package.cs` builds:

| Plugin | What it is for |
| --- | --- |
| `Avala.Simulator`, with `Avala.Simulator.Workload` | the simulated harnesses that play scripted sessions without a model or tokens, and the processes their scenarios run |
| `Avala.ForgeSimulator` | the simulated forge, for pull requests and forge triggers without GitHub or Gitea |
| `Avala.Recording` | records real sessions as files the simulator replays |

A development build keeps them: `dotnet build` copies every plugin to `artifacts/plugins`, which the tests, the screen tests and `dotnet run --project src/Avala.Host` load. The script names the plugins it leaves out as it packages, and `PackagingTests` in the architecture tests fails when the development-only set changes without this table. Claude Code is the only harness a package offers.

### The installers

The Windows installer, the portable zip and the AppImage are made with [Velopack](https://velopack.io) (MIT) by `scripts/installer.cs`, from the archive `scripts/package.cs` made and smoke tested. The script unpacks the archive and runs `vpk pack` on its folder with the pack id `Avala.Desktop`, the title Avala, the brand icon, the release notes from `docs/releases/v<version>.md` when they exist, and the channel `win` or `linux`. It first asks `vpk download github` for the full package of the newest published release on that channel, a prerelease too when this version is one, so `vpk pack` can make the delta from it; the first release with Velopack has no delta. It then copies to the output the installer, the portable package, renamed after the archives, the full and delta packages, and `releases.<channel>.json` holding only this version, since a running Avala looks in each release for its own packages.

`vpk` is a .NET tool pinned in `dotnet-tools.json`; restore it with `dotnet tool restore`. It packs Windows from any system, as the `[win]` directive when it is not Windows. An AppImage needs `mksquashfs` on Linux, which the `ubuntu-latest` runners have through `snapd`; on Arch it is `squashfs-tools`, and on Windows `vpk` brings its own, so the `[linux]` directive packs the AppImage there if a runner ever lacks it. `--smoke` runs the AppImage's `--version` and `--smoke` on Linux with `APPIMAGE_EXTRACT_AND_RUN=1`, so it needs no FUSE.

| Package | Measured for 0.1.0-test.1 |
| --- | --- |
| `avala-<version>-win-x64-setup.exe` | 99.6 MB |
| `avala-<version>-win-x64-portable.zip` | 92.1 MB |
| `Avala.Desktop-<version>-full.nupkg` | 92.1 MB |
| `avala-<version>-linux-x64.AppImage` | 89.7 MB |
| `Avala.Desktop-<version>-linux-full.nupkg` | 88.7 MB |
| `avala-<version>-linux-x64.tar.gz`, for comparison | 86.4 MB |
| the AUR package `makepkg` built from it | 82.8 MB |
| the Flatpak bundle built from it | 61.3 MB |
| a delta between two builds of the same code | under 1 MB |

The installers are unsigned. Velopack signs `Setup.exe`, `Update.exe` and the app with `vpk pack --signParams` on Windows, or `--signTemplate` for Azure Trusted Signing: add it to `Velopack.PackAsync` behind `--sign` when the Windows certificate exists. macOS can be added the same way: `vpk pack` on macOS takes the `.app`, signs and notarizes it with `--signAppIdentity`, `--signInstallIdentity` and `--notaryProfile`, and makes a `.pkg` installer; add `osx-arm64` and `osx-x64` to the script's targets and its `TargetOs`.

### Where Avala is installed

| How | Where the app goes | Where its data goes | Updates |
| --- | --- | --- | --- |
| Windows installer | `%LocalAppData%\Avala.Desktop`: `Update.exe`, a small `Avala.exe` that starts `current\Avala.exe`, and `current\` with the app and `plugins\`; shortcuts on the desktop and in the Start menu; uninstalled from Settings, Apps | `%LocalAppData%\Avala`, kept when Avala is uninstalled | in place |
| Windows portable zip | wherever it is unzipped, the same layout with a `.portable` marker | `%LocalAppData%\Avala` | in place |
| AppImage | wherever the file is kept; downloaded updates wait in `/var/tmp/velopack/Avala.Desktop` and replace the file on restart | `~/.local/share/Avala` | in place |
| tar.gz | wherever it is unpacked; `avala.desktop` and `icons/hicolor` (or `avala.png`) are there to install by hand, the icons into `~/.local/share/icons/hicolor` | `~/.local/share/Avala` | the release page |
| AUR `avala-bin` | `/opt/avala`, with `/usr/bin/avala`, `/usr/share/applications/avala.desktop` and the icon in every size | `~/.local/share/Avala` | `pacman` and an AUR helper |
| Flatpak | `/app/avala` inside the sandbox | `~/.var/app/io.github.rick_dev_creator.Avala/data/Avala` | `flatpak update` |

The pack id is `Avala.Desktop` rather than `Avala` because Velopack installs into `%LocalAppData%\<pack id>` and removes that folder when Avala is uninstalled, and `%LocalAppData%\Avala` is Avala's data folder. `AVALA_DATA_PATH` still chooses another data folder.

### Arch Linux: the AUR package

`packaging/aur/PKGBUILD` is the recipe of `avala-bin`, with `@VERSION@`, `@PKGVER@` and `@SHA256@` where a release's values go. `scripts/aur.cs` fills them from the release's `avala-<version>-linux-x64.tar.gz` and writes the matching `.SRCINFO`; the release workflow attaches both as `avala-bin-<version>.PKGBUILD` and `avala-bin-<version>.SRCINFO`. The package installs the tar.gz to `/opt/avala`, links `/usr/bin/avala` to it, installs the desktop entry with `Exec=avala`, the icon and the license, depends on `git`, the libraries Avalonia and .NET load, and `libsecret`, through which a [fleet](architecture.md#the-fleet) keeps its instance key in the Secret Service, and suggests `claude-code`, `github-cli` and `tea`. The version's hyphen becomes an underscore, `0.1.0-beta.1` as `0.1.0_beta.1`, which `pacman` orders before `0.1.0`.

To publish it, once, with an AUR account and an SSH key added to it:

```
git clone ssh://aur@aur.archlinux.org/avala-bin.git ~/aur/avala-bin
```

Then for each published release:

```
dotnet run scripts/aur.cs -- --archive <the downloaded avala-<version>-linux-x64.tar.gz> --into ~/aur/avala-bin
cd ~/aur/avala-bin
makepkg
git add PKGBUILD .SRCINFO
git commit -m "Update to <version>"
git push
```

`makepkg` builds the package without installing it, `makepkg -si` also installs it; either finds the archive next to the PKGBUILD or downloads it from the release, which must be published first, and checks its checksum. `makepkg --printsrcinfo` prints the same `.SRCINFO` the script writes. The release assets `avala-bin-<version>.PKGBUILD` and `.SRCINFO` are the same two files, to copy into the clone as `PKGBUILD` and `.SRCINFO` instead of running the script.

### Flatpak

`packaging/flatpak/io.github.rick_dev_creator.Avala.yml` builds the Flatpak from the release's tar.gz on the freedesktop 26.08 runtime, with the metainfo next to it. The app id follows Flathub's rule for a GitHub account, `io.github.<account>` with the hyphens as underscores, and matches the macOS bundle identifier `io.github.rick-dev-creator.avala`.

Avala drives git, the `claude` CLI, `gh`, `tea`, `npm` and the person's own scripts in the person's repositories, so the sandbox is open where it must be: `--filesystem=home` for the repositories, `--share=network`, `--socket=wayland` with `--socket=fallback-x11`, `--device=dri`, `--share=ipc`, `--talk-name=org.freedesktop.Notifications`, and `--talk-name=org.freedesktop.Flatpak` so every child process runs on the host. Inside Flatpak, which Avala knows from `/.flatpak-info`, `FlatpakHost` in the SDK starts each child as `flatpak-spawn --host --watch-bus --directory=<its folder>`, passes the variables Avala set or removed with `--env` and `--unset-env`, and runs the program through `sh -lc` so the person's login profile puts `~/.local/bin` and the like on `PATH`. `--watch-bus` ends the host process when Avala's side ends. Stopping a job's processes reaches the host through `flatpak-spawn`, which forwards the termination; the process tree only sees the `flatpak-spawn` side, so a host process that outlives it is reclaimed by `--watch-bus` rather than by the tree.

A fleet's instance key goes to the system keychain through `libsecret-1.so.0`, which the freedesktop 26.08 runtime ships with `libglib-2.0.so.0`, so the manifest adds no module for it. Inside the sandbox libsecret keeps secrets through the Secret portal of `xdg-desktop-portal`, in a file of the app's own data folder encrypted with a key the portal's backend, such as GNOME Keyring, holds; portals need no `--talk-name`, so the sandbox is not opened for it. Where no portal backend answers, Settings says the system has no keychain Avala can use, and no fleet can be connected.

Building it needs `flatpak`, `flatpak-builder` or the `org.flatpak.Builder` Flatpak, and the SDK, once:

```
flatpak install --user flathub org.freedesktop.Platform//26.08 org.freedesktop.Sdk//26.08 org.flatpak.Builder
dotnet run scripts/flatpak.cs -- --archive <avala-<version>-linux-x64.tar.gz or its folder>
flatpak install --user artifacts/packages/avala-<version>-linux-x64.flatpak
flatpak run io.github.rick_dev_creator.Avala
```

The script copies the manifest, the metainfo and the archive, as `avala-linux-x64.tar.gz`, to `artifacts/flatpak/sources`, runs `flatpak-builder` into a local repository and makes the single-file bundle with `flatpak build-bundle`, pointing at Flathub for the runtime. By hand, the same is:

```
cp packaging/flatpak/* <folder>/ && cp avala-<version>-linux-x64.tar.gz <folder>/avala-linux-x64.tar.gz
flatpak-builder --force-clean --repo=<repo> <build> <folder>/io.github.rick_dev_creator.Avala.yml
flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo <repo> avala.flatpak io.github.rick_dev_creator.Avala
```

The release workflow does not build it: CI steps run only `dotnet`, and the runners have neither `flatpak` nor the SDK. Publishing on Flathub comes later: it builds from the manifest in its own repository, with the archive as a `url` and its `sha256` instead of a `path`, a `<releases>` list in the metainfo, and Flathub's review of `--filesystem=home` and `--talk-name=org.freedesktop.Flatpak`, which a tool that runs the person's programs needs.

### Building one locally

```
dotnet run scripts/package.cs -- --rid linux-x64 --smoke
```

`--rid` is one of `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`, and any of them can be built from any system, but signing needs its own. `--version 0.1.0-beta.1` sets the version; without it the script takes the tag the workflow was started by, then the tag on `HEAD`, then `0.2.0-dev`. `--output <folder>` chooses where the archives go, `artifacts/packages` by default, and the work folder is `artifacts/package/<rid>`. `--smoke` runs the packaged `--version` and `--smoke` when the platform is the one running the script. `--sign` signs, see below.

Then the installers, from that archive:

```
dotnet tool restore
dotnet run scripts/installer.cs -- --rid linux-x64 --archive artifacts/packages --smoke
```

`--rid` is `win-x64` or `linux-x64`; `--archive` is the archive or the folder holding it; `--output` chooses where the installers and the feed go, `artifacts/packages` by default, and the work folder is `artifacts/installer/<rid>`. Both Windows and Linux installers can be made on Linux, given `mksquashfs` for the AppImage.

### The smoke run

`Avala --smoke` starts the real application on Avalonia's headless platform, so it needs no display: it composes every plugin in the package, runs the startup tasks and the migrations in a temporary data folder, shows the main window, prints `Avala <version> composed <n> pages and showed its main window.` and exits 0. With no plugin found it exits 2, and it exits 1 when the window cannot be shown or the runtime stops before its startup completes. It exits only once Avalonia's main loop runs, however fast the startup was. It never checks for updates or sends usage statistics. `Avala --version` prints `Avala <version> (<commit>)`. Use both to check a package on a machine before trusting it.

## The fleet server image

The release workflow's `server-image` job publishes the fleet server as `ghcr.io/rick-dev-creator/avala-server`, for `linux/amd64` and `linux/arm64`, tagged with the version and, for a stable version, `latest`.

- **How it is built.** `dotnet run scripts/server-image.cs -- --tag <tag>` runs `dotnet publish -t:PublishContainer` with the .NET SDK's own container support, without a Dockerfile, on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`. The `extra` image keeps the time-zone data the Fleet dashboard needs; it has no shell and runs as the non-root user `app`, listening on 8080. The job authenticates with `SDK_CONTAINER_REGISTRY_UNAME` and `SDK_CONTAINER_REGISTRY_PWORD` set to the actor and `GITHUB_TOKEN`, with `packages: write`, so it uses only `dotnet` steps.
- **Once, after the first release:** GHCR creates the package private, so `docker compose pull` answers `unauthorized`. Make it public in GitHub, Packages, `avala-server`, Package settings.
- **Building it locally:** `dotnet publish backend/Fleet/Avala.Server -t:PublishContainer -p:ContainerRuntimeIdentifiers=linux-x64 -p:ContainerImageTag=local`, adding `-p:LocalRegistry=Podman` on Podman. Set `AVALA_SERVER_IMAGE=localhost/avala-server` and `AVALA_SERVER_VERSION=local` in `.env` to run it.
- **When it is published:** after every desktop package is built, so a release that fails to package publishes no image.
- **Before a release:** run `dotnet run scripts/fleet-deploy-check.cs` on Linux with Docker Compose, or `docker-compose` on Podman's socket. It runs as a compose project of its own, so a fleet running on the same machine is never touched, and it refuses to start while ports 8080 or 8443 are taken. It needs no running Avala and leaves nothing behind. It checks:
  - the compose in `deploy/fleet/`, built from the working tree;
  - an upgrade from the previous release's image;
  - the real desktop, connecting over HTTPS with validation on;
  - restarts;
  - a backup and restore after deleting the volumes;
  - that no key reaches the logs.

  Pass `--previous <commit>` to choose the version upgraded from. A check interrupted with Ctrl-C leaves its own project behind, never a real fleet's; `docker compose ls` names it, and `docker compose -p <name> down -v` removes it.

## Release notes

The notes of a release are written by hand in `docs/releases/<tag>.md`, such as [`docs/releases/v0.1.0-beta.1.md`](releases/v0.1.0-beta.1.md), and committed before the tag. The release workflow uses that file as the draft's notes when it exists; without one, the draft gets notes generated from the merged pull requests. The file is the release's body, so it starts without a title, which the release already has.

## Cutting a release

1. Make sure `main` is green in CI and holds everything the release should.
2. Choose the version. The first public one is `v0.1.0-beta.1`.
3. Write its notes in `docs/releases/<tag>.md` and merge them to `main`.
4. Tag the commit and push the tag:

   ```
   git tag -a v0.1.0-beta.1 -m "Avala 0.1.0-beta.1"
   git push origin v0.1.0-beta.1
   ```

5. The [release workflow](../.github/workflows/release.yml) runs on that tag only. It builds and tests the tagged commit, packages each platform on its own runner with the smoke run, makes the Velopack installers and update feed from those archives, smoke testing the AppImage, generates the AUR recipe, and creates a **draft** release named after the tag with all of them, their checksums and the notes from `docs/releases/<tag>.md`.
6. Open the draft on GitHub, read the notes, download each package and run `--smoke` on it, then publish it. Publishing is the only step that makes it visible, and the only one the update check sees.
7. Publish `avala-bin` to the AUR, as described above, and build the Flatpak by hand if it is wanted.
8. Replace "Coming soon" in the README's Install section with a link to the release, and raise `VersionPrefix` in `Directory.Build.props`.

To redo a release before publishing it, delete the draft and the tag, locally and on GitHub, fix what was wrong and tag again.

## Usage statistics

A packaged release sends anonymous usage statistics to [Aptabase](https://aptabase.com); a build without the key sends nothing. The key is never in the source: it is the repository variable `AVALA_TELEMETRY_KEY`, an Aptabase app key such as `A-US-…`, which the release workflow passes to `scripts/package.cs` as an environment variable, and the script bakes into the host's assembly metadata as the build property `AvalaTelemetryKey`. A self-hosted Aptabase takes an `A-SH-…` key and its address in the repository variable `AVALA_TELEMETRY_HOST`, baked as `AvalaTelemetryHost`. The script says whether it packages with usage statistics, naming the region and never the key. Builds from a checkout, CI's test runs, the tests and `--smoke` never send anything.

- **Where they go.** The key decides the region: `A-EU-…` to `https://eu.aptabase.com`, `A-US-…` to `https://us.aptabase.com`, `A-SH-…` to `AVALA_TELEMETRY_HOST`. Each event is `POST <host>/api/v0/event` with the headers `App-Key: <key>`, `Content-Type: application/json` and an `Avala` user agent.
- **What is sent.** `installed` once ever and `active` at most once per local day, at startup and at local midnight while Avala runs:

  ```json
  {
    "timestamp": "2026-10-12T09:30:00.000Z",
    "sessionId": "3f2b8c1e-7d4a-4e09-9b6f-52c1a0d8e7f4",
    "eventName": "active",
    "systemProps": { "osName": "Linux", "osVersion": "6.10.2", "appVersion": "0.1.0-beta.1", "sdkVersion": "avala" },
    "props": { "architecture": "x64" }
  }
  ```

  The session ID is the install ID, a random GUID made on first run and kept in `telemetry.json` in the data folder. Nothing else is sent: no locale, no debug flag, no repository, job, prompt, path, harness, connection, account, cost or feature.
- **Reading the dashboard.** The count of `installed` events is the number of installs, and the count of `active` events on a day is that day's active installs. Since the session is the install ID, Aptabase's sessions over a week or a month count the distinct installs active in it. Aptabase identifies a visitor by a hash of the address and user agent with a salt it rotates daily, per its privacy policy; that is Aptabase's matter, not Avala's.
- **Turning it off.** Turn off on the first-run notice, Send usage statistics in Settings, About, `DO_NOT_TRACK=1`, or `AVALA_TELEMETRY=0`. `telemetry.json` can also be edited:

  ```json
  { "enabled": false }
  ```

  A file that cannot be read turns sending off and is left as it is.
- **Failures.** Sending runs in the background and never delays the window, waits at most 5 seconds, logs a failure at debug level and tries again at the next start. An event is marked sent only once Aptabase accepted it.
- **Where it lives.** `UsageTelemetry` in `Avala.Runtime.Telemetry`, behind the SDK's `ITelemetry`; only the host's real composition gives it the HTTP sender, and only when the build holds a key. The update check below is separate and stays anonymous.

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

Avala tells a person when a newer version is out. Installed with Velopack, by the Windows installer, the portable zip or the AppImage, it also downloads that version when the person clicks and installs it when they choose to restart; it never downloads or installs on its own. Installed any other way, by the tar.gz, the AUR or Flatpak, it opens the release page instead, and the package manager updates it.

- **When.** Once at startup, in the background: the startup task returns at once and the window never waits on the network. Again whenever the person chooses Check now in Settings, About.
- **Which releases.** Published releases only, never drafts. A stable build is offered stable releases; a prerelease build, such as `0.1.0-beta.1`, is offered prereleases too.
- **What it shows.** A newer version appears in the sidebar's footer, "Version x is available", and in About with Download. About also says when Avala is up to date, when GitHub could not be reached, and when the startup check is off.
- **Updating in place.** Velopack's `VelopackApp` runs first in `Main`, so an installed Avala answers its installer's hooks and exits at once, and a plain folder or a build from a checkout carries on as before; it is told not to apply a downloaded update at startup by itself. Clicking the notice, or Download in About, downloads the update: "Downloading version x…", the delta when Velopack has the previous full package and the full package otherwise, checked against the SHA-256 in the feed. When it is downloaded, the notice says "Restart to update" and About offers Restart to update. Choosing it starts Velopack's updater, which waits for Avala to close, applies the update and starts the new version, and Avala closes itself the usual way, ending its jobs' sessions and saving its state. A failed download says so and can be tried again. An update downloaded but not yet applied is offered as Restart to update at the next start, without asking GitHub again.
- **What it asks, notifying only.** `GET https://api.github.com/repos/rick-dev-creator/avala/releases?per_page=30`, with an `Avala` user agent and nothing else: no identifier, no usage, no telemetry.
- **What it asks, installed with Velopack.** Velopack's `GithubSource`, anonymous, with Velopack's `Velopack/<library version>` user agent: `GET https://api.github.com/repos/rick-dev-creator/avala/releases?per_page=10&page=1` with `Accept: application/vnd.github.v3+json`, then for each of those releases the browser download of its `releases.<channel>.json` with `Accept: application/octet-stream`, and, only when the person clicks, the browser download of the full or delta package. No token, no install identifier, no version, operating system or other query parameter is sent. Velopack keeps a random staging identifier on disk for staged rollouts, but the GitHub source never sends it. `VelopackApp` and the update manager write Velopack's own log: `velopack_Avala.Desktop.log` in the temporary folder for the AppImage, `velopack.log` there for a build Velopack did not install, and both in `%LocalAppData%\velopack` on Windows.
- **Where downloads go.** Velopack's own folder: `%LocalAppData%\Avala.Desktop\packages` on Windows and `/var/tmp/velopack/Avala.Desktop/packages` for the AppImage.
- **Turning it off.** A machine setting, `updates.json` in the data folder:

  ```json
  { "checkOnStartup": false }
  ```

  A file that cannot be read, or holds anything but that object, also turns the startup check off. Check now in About still asks, since the person asked.
- **Where it lives.** `UpdateCheck` in `Avala.Runtime.Updates`, behind the SDK's `IUpdates`, with `CheckAsync`, `DownloadAsync` and `RestartAsync`. Only the host's real composition gives it the HTTP feed and, when Velopack installed this Avala, `VelopackInstaller` behind the `IUpdateInstaller` port, so tests, the simulation and the smoke run never reach the network. The host closes the window on `RestartingToUpdate`.

## The window's identity

The desktop shows Avala's icon for its running window only when it can match the window to the desktop entry. On Linux the window names itself `avala`, the desktop entry's name: Wayland's `app_id` (`WaylandPlatformOptions.AppId`, Avalonia 12.1.4) and X11's `WM_CLASS` (`X11PlatformOptions.WmClass`), and `avala.desktop` declares `StartupWMClass=avala`. Inside Flatpak the window takes `FLATPAK_ID`, `io.github.rick_dev_creator.Avala`, the name of the Flatpak's desktop entry and icons. On Windows the executable embeds `avala.ico` (16 to 256 px) and the window shows it; on macOS the bundle's `Info.plist` names `avala.icns`, built with every size Apple defines, retina included.
