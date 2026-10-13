# Privacy and usage statistics

What leaves your computer, how to turn it off, and how to hide personal details on screen.

## What it does

- **Usage statistics.** A released build of Avala sends anonymous usage statistics to [Aptabase](https://aptabase.com) to count installs and daily active installs: an `installed` event once ever and an `active` event at most once per local day, each with Avala's version, your operating system's name, version and architecture, and a random install ID made on first run.
- **Never sent**: repositories, jobs, prompts, paths, harnesses, connections, accounts, costs, the features you use or your locale. The install ID is never derived from your hardware, user name or paths.
- **Builds from source send nothing**: only a packaged release can send.
- **The update check** asks GitHub's public releases API whether a newer version is out, with nothing about you. It never downloads or installs anything on its own: with the Windows installer, the portable zip or the AppImage, the new version downloads only when you click it and installs when you choose to restart.
- **Hide personal details**, on by default, masks emails, sign-in names, account names and your home folder in paths, on screen and in copied or exported conversations, so a screen can be recorded or shared as it is.
- Notifications, pull requests and forge triggers talk only to the services you set them up with.

## How to use it

1. **On a first run**, the welcome tour's **Your privacy** step has both switches: Hide personal details and Share anonymous usage statistics.
2. **Later**, open **Settings**, **About**, **Usage statistics**: clear **Send usage statistics** to stop, see **Exactly what is sent, to Aptabase**, or **Reset ID** for a new install ID.
3. **Hide or show personal details** in **Settings**, **Appearance**, **Hide personal details**, or with the eye in the Accounts header.
4. **Check for updates** with **Check now** in Settings, About, Updates.

## Settings and files

- `DO_NOT_TRACK=1` or `AVALA_TELEMETRY=0` in Avala's environment turns usage statistics off, whatever the setting says.
- **`telemetry.json`** in the data folder keeps your choice (`enabled`) and the install ID.
- **`appearance.json`** keeps `hidePersonalDetails`.
- **`updates.json`** in the data folder: `{ "checkOnStartup": false }` stops the check at startup.

## Good to know

- Connection names are the ones you chose, and are never masked.
- The logs redact secrets from the environment; see [Troubleshooting](troubleshooting.md).
- [Releasing](../release.md#usage-statistics) has the technical details of the statistics.

## See also

- [Getting started](getting-started.md)
- [Notifications](notifications.md)
