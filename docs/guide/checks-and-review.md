# Checks and review

A job is done only when your repository's own checks pass in its worktree, and its review shows that verdict first.

## What it does

- **Checks run after every finished turn**, in the job's worktree, in the order the repository declares them. After the first failure the rest are skipped.
- **A failing check sends the agent back** in the same session with the check's name and the end of its output, until it passes or the job runs out of attempts (three by default).
- **The review leads with the verdict**: "Verified on attempt 2 of 3", "Not verified", or "No checks declared: nothing proves the work". Then it lists what deserves a look, such as a failed attempt, a denied request, an assumption the agent made or an edit to a rule file, then the diff.
- **The review names what approving does**: "Keep branch avala/<id>", "Merge into main", or "Open pull request on <connection>" when a forge Avala can use hosts the repository's `origin`. When `origin` is on a forge you have not connected, the review says "Connect github.com in Settings to open and follow pull requests", with **Open Settings**.
- **Approving delivers the work** as the repository chooses: the branch is kept as it is, the work is squashed into the base branch, or a pull request is opened and the job stays among the open jobs as Delivered until it is merged or closed.

## How to use it

1. **Declare checks** in `.avala/checks.json` and commit it.
2. **Open the review** of a job that is Ready for review with Review, or Ctrl/⌘+R.
3. **Decide:**
   - **The primary action** delivers the work and says what happened: "Merged into main as 3f9a1c2.", or "Branch avala/<id> kept. Merge it when you are ready."
   - **Send back…** opens "Feedback for another round": say what to change, then Send feedback. **Send back with this message** sends a message you queued while the agent worked.
   - **Discard** asks "Discard the job and its worktree?"; Keep it or Discard.
   - **Open pull request on <connection>** is offered when the repository declares a pull request, or when a forge connection hosts its `origin`; under `keep` it is the first action. See [Pull requests and CI](pull-requests-and-ci.md).
4. **Resubmit** a job held for an invalid `.avala/checks.json`: fix the file, commit it, then Resubmit, which starts it again as a new job against the fixed file.

## Settings and files

**`.avala/checks.json`**:

```json
{ "checks": [ { "name": "build", "command": "dotnet", "arguments": ["build", "--no-restore"], "timeoutSeconds": 90 } ] }
```

- `command`: required, the program to run.
- `arguments`: optional, a list of strings.
- `name`: optional, shown in the review; the command line otherwise.
- `timeoutSeconds`: optional, up to 86400; 600 by default.

**`.avala/jobs.json`**, `approval`: `"keep"` (the default) leaves the job's branch for you, `"merge"` squashes it into the base branch with an `Avala-Job:` trailer.

## Good to know

- Checks come from the commit the job started from: an agent that edits `.avala/checks.json` is told its change does not apply to this job.
- Without `.avala/checks.json`, a job passes with "no checks declared"; a file that cannot be read holds the job as "invalid checks".
- A merge is refused while the base branch's checkout has uncommitted changes, or when the work conflicts with it.
- A sub-agent's work always merges into its parent's worktree; its review reads "Merge into the parent job".
- A denied request says why in your terms, for example "/tmp/pr-body.md is outside the job's worktree, and writing there needs you."
- A repository's first job opens with a card listing what it still needs: a forge to connect, `.avala/jobs.json` changes to commit, checks to declare. See [Repositories](repositories.md).

## See also

- [Permissions and autonomy](permissions-and-autonomy.md)
- [Best of N](best-of-n.md)
- [Pull requests and CI](pull-requests-and-ci.md)
