# Permissions and autonomy

Every edit, command, question and plan an agent proposes goes through rules it cannot change, at the autonomy you allow.

## What it does

- **Two levels of autonomy.** **Supervised**: anything a rule does not allow asks you first. **Autonomous**: edits and commands inside the worktree run without asking, anything else is denied, and forms are answered by policy. A repository that declares nothing is Supervised.
- **A job can tighten its autonomy, never loosen it** beyond what the repository declares.
- **Rules are checked in a fixed order**, and the first that matches decides: the read-only guard, the guards, the repository's rules, your answers in this job, the defaults, then, under Autonomous, the unattended rules.
- **Guards keep some things for a person**: an edit to `.avala/permissions.json` and any edit outside the worktree always ask.
- **Read-only commands run without asking.** A line whose every part only reads inside the worktree, such as `ls`, `cat`, `grep`, `rg`, `find`, `git status`, `git log` or `git diff`, runs and is audited as "read-only allowed", unless a repository rule says otherwise. A redirection to a file, an option that writes, a `$` or a path outside the worktree makes it ask.
- **Temporary files are fine.** Each job's `TMPDIR`, `TEMP` and `TMP` point to a scratch folder of its own, which counts as inside the worktree and never shows in the diff.
- **Every decision is audited** with the reason it was made, in the job inspector's Audit section.

## How to use it

1. **Answer a request** on its card, such as "Wants to run a command": choose **Allow** or **Deny**, and Add a note for the agent if you like.
2. **Choose how far the answer reaches** before you answer:
   - **Once**: this request only.
   - **Always** *program* **in this job**: one rule per program of the line, or per host for the web, or per tool.
   - **Everything in this job**: the rest of the job runs at Autonomous, with the guards still first. It is not offered when the repository declares itself supervised.
   - **…**, **Always in this repository**: adds a rule for exactly this target to `.avala/permissions.json`. It applies to new jobs once you commit it; this job stops asking now.
3. **Answer a question or a plan** on its card with **Answer** or **Decline**, or reply in your own words.
4. **Under Autonomous, recover a denial.** A denied request leaves a **Denied** card that never holds the turn. When a job rule could allow it, **Allow and retry** allows it for the rest of the job and tells the agent it may retry; a request a guard keeps for a person offers nothing.
5. **Answer many at once** from **Decisions** (Ctrl/⌘+D): Up/Down or J/K move, 1–9 choose an option, Enter answers and Backspace denies.

## Settings and files

**`.avala/permissions.json`**, committed in the repository:

```json
{
  "autonomy": "supervised",
  "formAnswers": "recommended",
  "rules": [
    { "name": "run the tests", "kind": "command", "target": "dotnet test*", "answer": "allow" },
    { "kind": "web", "answer": "deny" },
    { "kind": "fileEdit", "within": "workspace", "target": "docs/*", "answer": "ask" }
  ]
}
```

- `autonomy`: `"supervised"` (the default) or `"autonomous"`.
- `formAnswers`: how an Autonomous job answers a question nobody is there for: `"recommended"` (the default) takes the recommended option, `"bestJudgment"` lets the agent decide and state its assumption.
- `rules`: each rule has an `answer`, `"allow"`, `"deny"` or `"ask"`, and optionally:
  - `kind`: `command`, `fileEdit`, `web`, `mcp`, `search`, `subagent`, `message`, `reasoning` or `other`; without it, any kind.
  - `target`: a pattern where `*` matches any run of characters and `?` one character. Each part of a command line is matched on its own; a Deny on any part wins, then an Ask.
  - `within`: `"anywhere"` (the default) or `"workspace"`, for `fileEdit` rules only.
  - `name`: shown in the audit; `rule N` otherwise.

The file is at most 64 KB, and an unknown or duplicate key rejects it. A rejected file leaves the built-in rules, at Supervised. **Settings**, **Repository rules** shows the rules of every repository a job ran in, read from each job's base commit.

## Good to know

- Rules come from the commit a job started from, so an agent's edit to them never applies to its own job.
- Under Autonomous, a question that is a permission is declined, and a confirmation is confirmed.
- A pending request does not survive a restart; the job asks again.

## See also

- [Checks and review](checks-and-review.md)
- [Autopilot and triggers](autopilot-and-triggers.md)
- [Keyboard shortcuts](keyboard-shortcuts.md)
