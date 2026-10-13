# Jobs and conversations

A job is one piece of work an agent does in its own worktree, and its conversation is where you talk to the agent.

## What it does

- **A job runs in its own git worktree**, on the connection, model and effort you chose or Avala picked, under your repository's rules. It ends ready for review, and nothing reaches your branch until you approve it.
- **The sidebar groups jobs** by what they need: Needs you, Running, Waiting, Ready for review and Done. A row says Running, Starting, Checking, Waiting for its parent, Needs you, Ready for review, Approved, Discarded or Failed, or Held with its reason, such as stalled, session lost, over budget, near its limit or could not start. A job started without a repository carries a **Scratch** tag.
- **The conversation** shows replies as they stream, a quiet row that says what the agent is doing and for how long, each tool as one line, file edits as they are written and code blocks colored by their language. Jump to latest brings you back to the end.
- **The harness chip** in the composer names the connection, model and effort. On the welcome it reads **Auto**, or Default when the machine names a fixed default connection, with the reason for its pick, such as "Most capacity left".
- **Files the agent writes** can be opened from their line: a file edit opens its changes in a popover, an image or SVG shows as a thumbnail that opens in a sheet, and a page opens in your browser or in the [preview](canvas-and-previews.md).

## How to use it

1. **Start a job.** On the welcome, describe the task and press Enter: it runs in a scratch repository (see [Repositories](repositories.md#jobs-without-a-repository)). For a repository of your own, choose **New job** (Ctrl/⌘+N) and fill in Repository, Instruction, Connection and Autonomy, then Start.
2. **Choose where it runs** from the harness chip: Auto or a connection with its usage, then the Model and Effort.
3. **Talk to the agent.** Enter sends and Shift+Enter adds a line. A message sent while the agent works is queued, and Withdraw takes it back. Stop, or Esc, interrupts the agent and keeps the session open.
4. **Reuse a message.** Hover a prompt for Copy, Edit in the composer and Send again; replies and tool output have Copy. Right-click for Copy and Select All.
5. **Copy or export the conversation** from the job's More menu: Copy as Markdown, or Export… to a `.md` file. Stop Session is in the same menu.
6. **Change the model or connection.** When a job is held or ready for review, the chip offers another model or effort, which applies from your next message, or "Continue on" another connection, which starts a new conversation there with a brief of the work so far.
7. **Review the job** with Review (Ctrl/⌘+R); see [Checks and review](checks-and-review.md).

## Settings and files

- **Send key**: Settings, Appearance, **Send with Ctrl+Enter** (⌘+Enter on macOS). When off, Enter sends and Shift+Enter adds a line. Kept in `appearance.json` as `"send": "enter"` or `"commandEnter"`.
- **Reading width**: Settings, Appearance, Comfortable or Wide.
- **`.avala/jobs.json`** in a repository sets its jobs' defaults: `connection` (a name or `"auto"`), `model`, `effort` and `approval` (`"keep"`, the default, or `"merge"`). It also holds the `autopilot`, `delegation`, `limits`, `pullRequest` and `bestOf` sections described on their own pages.

  ```json
  { "connection": "auto", "approval": "merge" }
  ```

## Good to know

- **Attachments are not built yet**: you cannot attach or paste an image or a file into the composer.
- An image previews up to 512 KB as a thumbnail and up to 16 MB in its sheet.
- Code blocks over 300 lines are shown without colors.
- The exported conversation leaves out reasoning, and hides personal details while that setting is on.
- Jobs and their conversations survive a restart; a pending permission request does not.

## See also

- [Permissions and autonomy](permissions-and-autonomy.md)
- [Checks and review](checks-and-review.md)
- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Keyboard shortcuts](keyboard-shortcuts.md)
