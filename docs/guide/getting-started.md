# Getting started

Install Avala, take the welcome tour, and connect your agents and accounts.

## What it does

Avala runs coding agents for you, each in its own git worktree, and checks their work before you review it. It does not include an agent of its own: it drives the Claude Code logins already on your computer. Claude Code is the only harness Avala officially supports.

On a first run, a **welcome tour** of four steps shows what Avala found on your computer and sets your privacy choices.

## How to use it

1. **Install Avala.** See [Install](../../README.md#install) in the README; the packages are described in [Releasing](../release.md). To build from source, see [Contributing](../../CONTRIBUTING.md).
2. **Install Claude Code.** Install the `claude` CLI, put it on your `PATH` and sign in once.
3. **Start Avala and follow the tour.** Move on with Continue or Enter, and leave at any step with Skip or Esc:
   1. **Welcome to Avala**: what Avala is, in one line.
   2. **Your agents**: the coding agents signed in on this computer, each marked Signed in, At its limit, Signed out, Key not set or Not usable. With none found, the step shows the command that installs one and the one that signs in, and Check again looks once more.
   3. **Your code**: the **Projects folder** where new and cloned repositories go, with Change…, and the GitHub, Gitea or Forgejo sign-ins found on this computer, each added with one click.
   4. **Your privacy**: Hide personal details and Share anonymous usage statistics. Start finishes the tour.
4. **Start your first job** from the welcome: type what you want under "What should we work on?" and press Enter. See [Jobs and conversations](jobs-and-conversations.md).

To add accounts later, open **Settings**, **Accounts**:

- **Agents**, **Add…** adds a connection: a Name, a Harness, a Credential and Where it is kept. A row says whether its connection is "declared in connections.json" or "discovered on this machine".
- **Source control**, **Connect…** opens **Connect an account**: sign in with the command-line tool (`gh` or `tea`) or with a token variable, the name of an environment variable that holds the token.

To see the tour again, choose **Show welcome tour** in **Settings**, **About**.

## Settings and files

- **Claude Code logins** are found on their own: `~/.claude`, every `~/.claude-*` folder (`~/.claude-work` becomes the connection `claude-work`), the folder in `CLAUDE_CONFIG_DIR`, and the variables `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_USE_BEDROCK` and `CLAUDE_CODE_USE_VERTEX`.
- **`connections.json`** in the data folder declares connections of your own. `default` names the connection for jobs that name none, or `"auto"` to pick the one with the most capacity left:

  ```json
  {
    "default": "auto",
    "connections": [
      { "name": "claude-team", "provider": "claude-code", "credential": { "source": "apiKey", "reference": "TEAM_ANTHROPIC_KEY" } }
    ]
  }
  ```

  A credential's `source` is `login`, with a sign-in folder as its `reference`, or `apiKey`, with the name of an environment variable. Avala stores names and references, never a key.
- **`forges.json`** holds your source control accounts; see [Repositories](repositories.md#settings-and-files).
- **`onboarding.json`** records that the tour was finished or skipped, as `{ "onboarded": true }`.
- **The data folder** is the folder in `AVALA_DATA_PATH`, or `Avala` in your local application data folder; see [where files live](../README.md#where-files-live).

## Good to know

- Claude Code is the only harness Avala supports.
- Only one Avala runs on a data folder at a time. To run a second one, set `AVALA_DATA_PATH` to another folder.
- A login made while Avala runs is found by Check again, without a restart.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Repositories](repositories.md)
- [Privacy and usage statistics](privacy-and-usage-statistics.md)
- [Troubleshooting](troubleshooting.md)
