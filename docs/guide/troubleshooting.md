# Troubleshooting

What to do when a job will not start, the window looks wrong, or a preview stays blank, and where the logs are.

## What it does

Avala holds a job that cannot go on instead of letting it hang, and says why in the sidebar and the inspector. It writes what went wrong to its logs, with secrets redacted.

## How to use it

### A job says "could not start"

A job whose start failed, for example because git could not prepare its worktree, is held as **could not start** with the error's message.

1. Select the job and open the inspector's worktree section, which shows the message.
2. Fix the cause, such as a repository that is locked or a missing git identity.
3. Choose **Retry**: it removes what the failed start left and starts the job again from its repository.

Such a job takes no message until it has started.

### Carry on from a terminal

The inspector's worktree section has a copy button beside the branch and beside the path. Copy the path, then `cd` into it in a terminal: the worktree is a plain git checkout of the job's branch, so you can keep working there if Avala cannot.

### A job failed

Its composer says why, such as "This job failed · no connection could run it: check its connection in Settings and the repository's .avala/jobs.json", "This job failed · its agent's turn failed" or "This job failed · its agent could not take the message". A failed job takes no message: start a new one from its instruction once the cause is fixed. A job that failed before Avala kept the reason reads only "This job failed"; its log lines name the job.

### A job is held

The sidebar says why, such as "held: stalled", "held: session lost", "held: over budget" or "held: near its usage limit". See [Budgets and usage limits](budgets-and-usage-limits.md) for raising a budget or continuing on another connection, and continue a stalled or lost job by sending it a message: its composer names the hold, such as "Held · stalled · send a message to continue the job…". The message goes to the job's session when it is still open, or to a new session that resumes the conversation. A message the job refuses stays in the composer with the reason, and the log records every hold and every refused message.

### Find the logs

1. Open **Settings**, **About**, **Diagnostics**.
2. Choose **Open** next to **Log folder**.
3. Attach the latest file when you report a problem.

### Check that Avala starts

From the folder Avala is installed in, `Avala --version` prints its version and commit, and `Avala --smoke` starts it headless in a temporary data folder and exits 0 when it composed its pages and showed its window. On Windows, pipe them to `Out-Host` to see the output.

### The window is blurry or clicks land in the wrong place on Linux

On a Wayland session, Avala draws through Wayland at the screen's own scale. If that misbehaves on your compositor, start it with `AVALA_X11=1` to draw through X11 instead.

### The preview is blank or opens in its own window

The preview uses your system's web view:

- **Windows** uses WebView2 and **macOS** WKWebView, both embedded in the pane.
- **Linux** embeds the page with **WPE WebKit** when it is built with its libwpe backend: `libWPEWebKit-2.0.so.1` with `libwpe-1.0.so.1` and `libWPEBackend-fdo-1.0.so.1`. A WPE WebKit without them, such as Arch's `wpewebkit` 2.54 without `libwpe` and `wpebackend-fdo`, counts as not installed.
- Otherwise **WebKitGTK** opens the page in a window of its own, "Avala preview", since under Wayland it cannot be embedded.
- With no web view at all, the pane says so and offers **Open in browser**.

A blank WebKitGTK window under Wayland comes from its DMA-BUF renderer. Avala sets `WEBKIT_DISABLE_DMABUF_RENDERER=1` on Linux when the variable is unset, so if you set it to something else yourself, unset it.

## Settings and files

- **Logs**: the `logs` folder of the data folder, as `avala-YYYYMMDD-NNN.log`, each up to 4 MB, the newest ten kept.
- **Data folder**: `AVALA_DATA_PATH`, or `Avala` in your local application data folder; see [where files live](../README.md#where-files-live).
- **Environment variables**: `AVALA_X11=1` forces X11, `AVALA_DATA_PATH` moves the data folder, and `WEBKIT_DISABLE_DMABUF_RENDERER` is described above.

## Good to know

- "Another Avala already runs on this data folder" means a second Avala was started on the same data folder: switch to the open one, or set `AVALA_DATA_PATH` to another folder.
- Closing the last WebKitGTK preview leaves its network process, about 40 MB, until Avala exits.
- A settings or rules file that cannot be read is reported, never silently ignored: Settings shows it as rejected, or the job is held, such as "held: invalid checks".
- Report a problem at [github.com/rick-dev-creator/avala/issues](https://github.com/rick-dev-creator/avala/issues) with your platform, the output of `Avala --version` and the latest log.

## See also

- [Getting started](getting-started.md)
- [Canvas and previews](canvas-and-previews.md)
- [Budgets and usage limits](budgets-and-usage-limits.md)
