# Jobs and conversations

A job is one piece of work an agent does in its own worktree, and its conversation is where you talk to the agent.

## What it does

- **A job runs in its own git worktree**, on the connection, model and effort you chose or Avala picked, under your repository's rules. It ends ready for review, and nothing reaches your branch until you approve it.
- **The sidebar groups jobs by project.** **Needs you** stays on top and lists every job waiting for you, whatever its project. Below it, **Projects** lists one project per repository, made with its first job and named after its folder, and any project you make, alphabetically. A project shows its open jobs, newest first, and folds the approved, discarded and failed ones under "N done"; folded, it shows how many jobs are open and an amber ring when one of them needs you. **Chats** lists the jobs started without a repository. A row says Running, Starting, Checking, Waiting for its parent, Needs you, Ready for review, Approved, Discarded or Failed, or Held with its reason, such as stalled, session lost, over budget, near its limit or could not start.
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
8. **Organize your projects.** Click a project's name to fold or unfold it; Avala remembers it. **+** beside **Projects** makes a new project and lets you name it. Right-click a project for **Rename** and **Delete Project**; Enter saves a name and Esc cancels.
9. **Move a conversation.** Right-click a job's row and choose **Move to**, then a project or **New project…**, which makes one, moves the job into it and lets you name it.

## Settings and files

- **Send key**: Settings, Appearance, **Send with Ctrl+Enter** (⌘+Enter on macOS). When off, Enter sends and Shift+Enter adds a line. Kept in `appearance.json` as `"send": "enter"` or `"commandEnter"`.
- **Reading width**: Settings, Appearance, Comfortable or Wide.
- **Folded groups** are kept in `layout.json` in the data folder as `collapsedGroups`. Projects and the conversations you moved are kept in `projects.db`.
- **`.avala/jobs.json`** in a repository sets its jobs' defaults: `connection` (a name or `"auto"`), `model`, `effort` and `approval` (`"keep"`, the default, or `"merge"`). It also holds the `autopilot`, `delegation`, `limits`, `pullRequest` and `bestOf` sections described on their own pages.

  ```json
  { "connection": "auto", "approval": "merge" }
  ```

## Good to know

- **Projects**: a project you made can be deleted at any time, and its conversations return to their repository's project. A repository's project can be deleted once you moved its conversations elsewhere, and comes back when that repository runs a new job. Deleting a project never deletes a job.
- A sub-agent's conversation moves with its parent, and a best-of-N contest stays in its repository's project.
- Dragging a row onto a project is not built yet: use **Move to**.
- **Attachments are not built yet**: you cannot attach or paste an image or a file into the composer.
- An image previews up to 512 KB as a thumbnail and up to 16 MB in its sheet.
- Code blocks over 300 lines are shown without colors.
- The exported conversation leaves out reasoning, and hides personal details while that setting is on.
- **How much context is left** shows in the job's header beside the usage left, such as "30% context left", once the agent's first turn ends. Hover it for the tokens in use against the model's window, and whether the harness compacted the conversation during the last turn; it turns amber when the context is nearly full. A harness that reports no context window shows nothing there.
- Jobs and their conversations survive a restart; a pending permission request does not.
- When the agent does not confirm Stop within 30 seconds, Avala stops its session instead, and the log says so. The job is held with its work kept, and your next message resumes the conversation in a new session.

## See also

- [Permissions and autonomy](permissions-and-autonomy.md)
- [Checks and review](checks-and-review.md)
- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Keyboard shortcuts](keyboard-shortcuts.md)
