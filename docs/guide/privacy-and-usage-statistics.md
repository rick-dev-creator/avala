# Privacy and usage statistics

What leaves your computer, how to turn it off, and how to hide personal details on screen.

## What it does

- **Usage statistics.** A released build of Avala sends anonymous usage statistics to [Aptabase](https://aptabase.com) to count installs and daily active installs: an `installed` event once ever and an `active` event at most once per local day, each with Avala's version, your operating system's name, version and architecture, and a random install ID made on first run.
- **Never sent**: repositories, jobs, prompts, paths, harnesses, connections, accounts, costs, the features you use or your locale. The install ID is never derived from your hardware, user name or paths.
- **Builds from source send nothing**: only a packaged release can send.
- **The update check** asks GitHub's public releases API whether a newer version is out, with nothing about you. It never downloads or installs anything on its own: with the Windows installer, the portable zip or the AppImage, the new version downloads only when you click it and installs when you choose to restart.
- **Hide personal details**, on by default, masks emails, sign-in names, account names and your home folder in paths, on screen and in copied or exported conversations, so a screen can be recorded or shared as it is.
- **A fleet server** of your own, once you connect Avala to it, receives what each job does, about two seconds after it happens, so the fleet's activity and trends add up across computers: the job's repository as its remote's address without credentials, such as `remote:github.com/owner/name`, or its first commit; its statuses, holds, model and effort; its token usage and cost; and its verification results. It never receives a path, a title, an instruction, a prompt, a branch, a file name, anything from a chat without a repository, or anything from before you connected or while sending is paused. The server's key is kept in your system's keychain, never in a file.
- Notifications, pull requests and forge triggers talk only to the services you set them up with.

## How to use it

1. **On a first run**, the welcome tour's **Your privacy** step has both switches: Hide personal details and Share anonymous usage statistics.
2. **Later**, open **Settings**, **About**, **Usage statistics**: clear **Send usage statistics** to stop, see **Exactly what is sent, to Aptabase**, or **Reset ID** for a new install ID.
3. **Hide or show personal details** in **Settings**, **Appearance**, **Hide personal details**, or with the eye in the Accounts header.
4. **Check for updates** with **Check now** in Settings, About, Updates.
5. **Connect a fleet server** in **Settings**, **Fleet** with its address and the instance key its administrator gave you. **Pause** stops sending, and what happens while paused is never sent; **Disconnect** removes the key from the keychain and deletes what was waiting to be sent. The section shows the facts waiting, the last delivery, since when the server has not answered, if it stopped, and anything it refused.

## Settings and files

- `DO_NOT_TRACK=1` or `AVALA_TELEMETRY=0` in Avala's environment turns usage statistics off, whatever the setting says.
- **`telemetry.json`** in the data folder keeps your choice (`enabled`) and the install ID.
- **`appearance.json`** keeps `hidePersonalDetails`.
- **`updates.json`** in the data folder: `{ "checkOnStartup": false }` stops the check at startup.
- **`fleet.json`** in the data folder keeps the fleet server's address, this instance's id and name on it, whether sending is on, and whether the server refused the key; never the key. **`fleet.db`** holds the facts waiting to be sent.

## Good to know

- Connection names are the ones you chose, and are never masked.
- On Linux a fleet needs a Secret Service, such as GNOME Keyring or KWallet, for its key; without one, Settings says so and nothing is sent. On macOS, an update signed differently may make the keychain ask once to let Avala read the key.
- The logs redact secrets from the environment; see [Troubleshooting](troubleshooting.md).
- [Releasing](../release.md#usage-statistics) has the technical details of the statistics.

## See also

- [Getting started](getting-started.md)
- [Notifications](notifications.md)
