# Jobs and conversations

A job is one piece of work an agent does in its own worktree, and its conversation is where you talk to the agent.

## What it does

- **A job runs in its own git worktree**, on the connection, model and effort you chose or Avala picked, under your repository's rules. It ends ready for review, and nothing reaches your branch until you approve it.
- **The sidebar groups jobs by project.** **Needs you** stays on top and lists every job waiting for you, whatever its project. Below it, **Projects** lists one project per repository, made with its first job and named after its folder, and any project you make, alphabetically. A project shows its open jobs, newest first, a job delivered as a pull request among them until it is merged or closed, and folds the approved, discarded and failed ones under "N done"; folded, it shows how many jobs are open and an amber ring when one of them needs you. **Chats** lists the jobs started without a repository. A row says Running, Starting, Checking, Waiting for its parent, Needs you, Ready for review, Delivered, Approved, Discarded or Failed, or Held with its reason, such as stalled, session lost, over budget, near its limit or could not start.
- **The conversation** shows replies as they stream, a quiet row that says what the agent is doing and for how long, each tool as one line, file edits as they are written and code blocks colored by their language. Jump to latest, or End, brings you back to the end, and Alt+↑ and Alt+↓ move turn by turn. A finished command with long output opens on its last 20 lines, with "Show N earlier lines"; a failed one lists the lines that look like errors in a Problems block above its output. The end of each turn says how long it took, its tokens and its cost; hover it for the tokens by type and the model. The last turn of each attempt also shows its checkpoint, the files that attempt changed; when the job waits for you, **Restore to here** returns the worktree to that checkpoint after you confirm, keeps what came after in a checkpoint of its own, and puts a message in the composer telling the agent what changed back.
- **The harness chip** in the composer names the connection, model and effort. On the welcome it reads **Auto**, or Default when the machine names a fixed default connection, with the reason for its pick, such as "Most capacity left".
- **Files the agent writes** can be opened from their line: a file edit opens its changes in a popover, an image or SVG shows as a thumbnail that opens in a sheet, and a page opens in your browser or in the [preview](canvas-and-previews.md).

## How to use it

1. **Start a job.** On the welcome, describe the task and press Enter: it runs in a scratch repository (see [Repositories](repositories.md#jobs-without-a-repository)). For a repository of your own, choose **New job** (Ctrl/⌘+N) and fill in Repository, Instruction, Connection and Autonomy, then Start.
2. **Choose where it runs** from the harness chip: Auto or a connection with its usage, then the Model and Effort.
3. **Talk to the agent.** Enter sends and Shift+Enter adds a line. A message sent while the agent works is queued, and Withdraw takes it back. Stop, or Esc, interrupts the agent and keeps the session open.
4. **Your draft waits for you.** What you type stays in its conversation, with its attachments and mentions, when you switch to another one or close Avala; the sidebar marks a conversation with an unsent draft with a pencil. In an empty composer, ↑ brings back the messages you sent in that conversation, newest first, and ↓ goes back.
5. **Attach files.** The paperclip in the composer picks files; you can also drop them on the composer or paste them. A pasted image, or a pasted text over 4,000 characters, becomes an attachment of its own. Each one shows as a chip above the draft, with a thumbnail for an image, and × removes it. The prompt it was sent with shows the same chips. The paperclip appears only when the job's connection takes attachments, and a file it does not take is refused with the reason.
6. **Mention a file, a folder, a commit or another job.** Type `@` and a few letters: up to eight suggestions from the job's worktree, its latest commits and your other jobs appear above the composer. ↑ and ↓ choose, Enter or Tab writes the mention and Esc closes the list. When you send, the message ends with a **Referenced:** list that tells the agent what each mention still in the text is: the file or folder in its worktree, the commit with its subject, or the other job's instruction, status and last reply.
7. **Reuse a message.** Hover a prompt for Copy, Edit in the composer and Send again; replies and tool output have Copy. Right-click for Copy and Select All.
8. **Find in the conversation** with Ctrl/⌘+F: the rows whose message, prompt, tool title or output hold the text are tinted, the current one more, with "n of m". Enter and Shift+Enter step through them, scrolling each into view and unfolding a folded tool row that holds it; Esc closes the bar.
9. **Copy or export the conversation** from the job's More menu: Copy as Markdown, or Export… to a `.md` file. Stop Session is in the same menu.
10. **Change the model or connection.** When a job is held or ready for review, the chip offers another model or effort, which applies from your next message, or "Continue on" another connection, which starts a new conversation there with a brief of the work so far.
11. **Review the job** with Review (Ctrl/⌘+R); see [Checks and review](checks-and-review.md).
12. **Organize your projects.** Click a project's name to fold or unfold it; Avala remembers it. **+** beside **Projects** makes a new project and lets you name it. Right-click a project for **Rename** and **Delete Project**; Enter saves a name and Esc cancels.
13. **Move a conversation.** Right-click a job's row and choose **Move to**, then a project or **New project…**, which makes one, moves the job into it and lets you name it.
14. **Continue on your phone.** In the header of a job you started, the phone glyph opens **Continue on your phone**: turn it on and scan the code, or open the link, in the Claude app or any browser. Messages you send from there run in the job's conversation, marked "From your phone", and one sent while the job waits for review continues it. The glyph shows a dot while a device is connected, and the sidebar marks the job with a phone. It needs a harness that offers it, such as Claude Code signed in with claude.ai, never an API key.
15. **Archive a conversation.** Point at a row that is ready for review, approved, discarded or failed and click the archive glyph at its end, or press Ctrl/⌘+Shift+A on the selected row, or right-click it and choose **Archive**. It leaves the sidebar, and the selection moves to the next row of its group. **Archive Done** in a project's menu, or the glyph on its "N done" fold, archives every ended conversation of that project or of Chats. **Archived** at the foot of the sidebar shows how many are archived and opens the Archived page, grouped by project, where the restore glyph puts a conversation back and clicking one opens it with an **Archived** chip in its header.

## Settings and files

- **Send key**: Settings, Appearance, **Send with Ctrl+Enter** (⌘+Enter on macOS). When off, Enter sends and Shift+Enter adds a line. Kept in `appearance.json` as `"send": "enter"` or `"commandEnter"`.
- **Reading width**: Settings, Appearance, Comfortable or Wide.
- **Start jobs on your phone**: Settings, Claude Code, one switch for every login, off by default. With it on, every job you start and every chat begins with Remote Control on. Kept in `claude-code.json` in the data folder as `"startJobsOnPhone"`.
- **Folded groups** are kept in `layout.json` in the data folder as `collapsedGroups`. Projects, the conversations you moved and the archive are kept in `projects.db`.
- **`.avala/jobs.json`** in a repository sets its jobs' defaults: `connection` (a name or `"auto"`), `model`, `effort` and `approval` (`"keep"`, the default, `"merge"` or `"pull-request"`). The review names what approving does. An approved job still takes a message: sending one reopens it in its worktree, resuming its conversation, and it comes back for review; a discarded or failed job's composer reads "This job was discarded" or "This job failed". It also holds the `autopilot`, `delegation`, `limits`, `pullRequest` and `bestOf` sections described on their own pages.

  ```json
  { "connection": "auto", "approval": "merge" }
  ```

## Good to know

- **Projects**: a project you made can be deleted at any time, and its conversations return to their repository's project. A repository's project can be deleted once you moved its conversations elsewhere or archived them, and comes back when that repository runs a new job or you restore one of its archived conversations. Deleting a project never deletes a job.
- A sub-agent's conversation moves with its parent, and a best-of-N contest stays in its repository's project.
- Dragging a row onto a project is not built yet: use **Move to**.
- **Archiving** changes where a conversation is listed, never the job: its transcript, records, usage and worktree stay as they are. A conversation that is working cannot be archived. One in **Needs you** can, once it is not working: a held job or a contest left to decide. An archived one stays archived when it is held again, and an archived one that works again, because you sent it a message, reopened, resumed or sent it back, returns to its project by itself. A sub-agent is archived with its parent and a contest as one row. Nothing is archived automatically, and there is no search in the Archived page yet.
- **Attachments** are copied into the `attachments` folder of the data folder when you add them, so the agent gets them even if you move the original. One file can be up to 20 MB. Claude Code takes PNG, JPEG, GIF and WebP images, PDFs and text files. The welcome's composer does not take attachments yet: start the job, then attach in its conversation.
- **Mentions** suggest at most 5,000 files of the worktree, ignored files left out, and its last 30 commits. The list is read again after 30 seconds. A job without a repository suggests only your other jobs.
- An image previews up to 512 KB as a thumbnail and up to 16 MB in its sheet.
- Code blocks over 300 lines are shown without colors.
- The exported conversation leaves out reasoning, and hides personal details while that setting is on.
- **How much context is left** shows in the job's header beside the usage left, such as "30% context left", once the agent's first turn ends. Hover it for the tokens in use against the model's window, and whether the harness compacted the conversation during the last turn; it turns amber when the context is nearly full. A harness that reports no context window shows nothing there.
- **On your phone**, Avala still decides: its permission rules, budgets and review apply, and a permission request waits in Avala, not on the phone. A delegated job is never on the phone; follow it through the job that started it. While Avala holds a job, its Remote Control is paused, and it comes back when the job runs again.
- Jobs and their conversations survive a restart; a pending permission request does not.
- When the agent does not confirm Stop within 30 seconds, Avala stops its session instead, and the log says so. The job is held with its work kept, and your next message resumes the conversation in a new session.

## See also

- [Permissions and autonomy](permissions-and-autonomy.md)
- [Checks and review](checks-and-review.md)
- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Keyboard shortcuts](keyboard-shortcuts.md)
