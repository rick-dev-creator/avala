# Repositories

Pick, clone, create or publish the git repositories your jobs work in, or let a job make a scratch repository of its own.

## What it does

- **Every job runs against a git repository**, in a worktree of its own, so your checkout is never touched while the agent works.
- **Repositories come from you, never from an agent**: no agent is offered cloning or publishing as a tool.
- **Your forge accounts are found on their own**, from the `gh` and `tea` logins and token variables you already have, and listed with their repositories, most recently changed first.
- Avala uses the git credentials you already have; it never holds a token for git.
- **What a repository needs is told when it matters.** A repository's first job opens with a card that lists, with one action each, what it still needs: "Connect github.com as octocat through gh to open pull requests and follow their checks" (**Connect**), the account to choose when there are several, or the sign-in command and **Check again** when there is no login; ".avala/jobs.json has changes jobs do not see until they are committed" (**Commit**); "Without .avala/checks.json Avala cannot verify the work" (**Declare checks**, which opens its editor on the repository's **Checks** tab in Settings). A fixed item turns into a check mark in place. Avala never connects a forge without you. The welcome tour's **Your code** step says what each account is for.

## How to use it

1. **Open a folder.** On the welcome, or in New job's Repository pop-up, choose **Open folder…** and pick a folder. A folder that is not a repository yet can be made one.
2. **Clone.** Choose **Clone…**, search your accounts' repositories or paste a URL, and confirm. The clone goes into your projects folder; Change picks another.
3. **Make a new one.** Choose **New repository…**, give the folder a name and Create. Tick **Publish it to a forge** to also create it on a forge and push its first commit.
4. **Publish an existing one.** Choose **Publish repository…**, pick Public or Private and a name on the forge, then Publish. Avala creates it under the account of the connection's token, adds it as `origin` and pushes the current branch.

### Jobs without a repository

Start a job from the welcome, choose **No repository** in New job's Repository pop-up, or press Ctrl/⌘+Shift+Enter in the instruction. The job runs in a **scratch repository** of its own in the data folder's `scratch` folder, named after the day and the first words of the instruction, such as `scratch/2026-10-12-sketch-a-csv-parser-3f9a1c`. Approving the job merges its work there; the inspector's worktree section names the folder and opens it. Such a job is listed under **Chats** in the sidebar until you move it to a project.

## Settings and files

- **`workspaces.json`** in the data folder holds `projectsFolder`, the folder clones and new repositories go into. Until you choose one, Avala suggests the first that exists of `~/Projects`, `~/projects`, `~/code`, `~/src`, `~/dev` and `~/source/repos`.
- **`forges.json`** in the data folder lists your forge accounts, written for you by Connect…:

  ```json
  {
    "pollSeconds": 60,
    "forges": [
      { "name": "github", "forge": "github", "url": "https://github.com", "credential": { "source": "cli" } },
      { "name": "home", "forge": "forgejo", "url": "https://git.example.com", "credential": { "source": "env", "reference": "GITEA_TOKEN" } }
    ]
  }
  ```

  `forge` is `github`, `gitea` or `forgejo`. A credential's `source` is `cli`, the command-line tool's login, or `env`, with the name of an environment variable as its `reference`. `pollSeconds`, from 15 to 3600 and 60 by default, is how often forge events are polled.
- **Logins found**: `gh auth status` and the variables `GH_TOKEN` and `GITHUB_TOKEN` for GitHub; `tea login list` and `GITEA_TOKEN` for Gitea and Forgejo.

## Good to know

- A published repository is private unless you choose Public.
- Publishing again when `origin` already is that repository only pushes, so a push refused for credentials can be retried once they are fixed.
- A failed or cancelled clone removes only the folder Avala made for it.
- Scratch repositories are kept: Avala never deletes anything in `scratch`. To keep one as a regular repository, move it out yourself.
- Scratch repositories declare no checks, so their review shows the diff with no verdict from checks, and their jobs open with no readiness card.
- The card does not show for a repository with nothing missing, nor for any job but its first; an `origin` on a host no forge plugin signs in to, or on a local path, shows no forge item.
- Making a repository of your own needs a git identity (`user.name` and `user.email`); a scratch repository does not.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Pull requests and CI](pull-requests-and-ci.md)
- [Getting started](getting-started.md)
