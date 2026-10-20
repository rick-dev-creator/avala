# Canvas and previews

Agents draw diagrams, charts and notes on a canvas in the conversation, and a page or app a job serves shows in a preview beside it.

## What it does

- **The canvas** is a tool Avala offers the agent. It draws three formats in the app's theme:
  - **SVG**, for diagrams, charts, flows, screens and designs, still or animated with SMIL (`<animate>`, `<animateTransform>`, `<animateMotion>`, `<set>`);
  - **Markdown**, for notes, tables, plans and reports;
  - **Mermaid**, for flowcharts and sequence, state, class and entity-relationship diagrams.
- **SVG is sanitized.** Scripts, embedded pages, event handlers and external references are removed, and a note under the drawing says how many were blocked. Images embedded as `data:` PNG, JPEG, GIF or WebP stay.
- **The preview** shows a page the job serves beside its conversation. A server a job starts opens the pane by itself, once; a link in a reply to that server opens in the pane too.
- **Previews are confined.** Every page goes through a loopback gateway of the job's own, which keeps other sites out until you allow the network, and a link that leaves the job's pages is offered to your browser instead.

## How to use it

1. **Ask for something visual**, such as "draw the architecture" or "chart the results": the agent draws it on a canvas.
2. **Play an animation again** with the play button that appears on hover, when reduced motion is on.
3. **Look closer** with **Open** in the canvas's header, and close it with Escape. An SVG or Mermaid diagram opens fitted, with the same zoom as the Docs pane: **Zoom out**, the zoom in percent (choose it for the actual size), **Zoom in** and **Fit to view**; the wheel or a pinch zooms where the pointer is and dragging moves it. Markdown scrolls.
4. **Run the project** with **Run** in the job's header: it starts the worktree's `dev` script, or `start`, with the package manager its lock file names (pnpm, yarn, bun or npm), or serves `index.html` when there is no script. Stop ends it.
5. **Open the preview** from the **Running** chip in the job's header. The pane has Back, Reload, Allow network, Open in browser and Close preview.
6. **Resize the pane** by dragging its edge, and reset it with a double-click.

## Settings and files

- **Reduce motion**: Settings, Appearance, System, On or Off. On holds animations where they settle.
- **`layout.json`** in the data folder keeps the pane's share of the width as `previewShare`: 0.45 by default, between 0.25 and 0.7.
- A script started by Run gets `PORT`, `HOST=127.0.0.1`, `AVALA_PORT` and `BROWSER=none`.

## Good to know

- Nothing on a canvas responds to input, and CSS animations and scripts do not run. Anything interactive, such as an app or a three.js scene, belongs in a page the agent serves for the preview.
- A canvas in any other format shows its source. A Mermaid diagram that cannot be drawn shows its source with a note.
- Allow network lasts for that job until Avala restarts.
- The preview uses the system's web view: WebView2 on Windows, WKWebView on macOS, WPE WebKit or WebKitGTK on Linux. Where the page cannot be embedded, it opens in a window of its own, or the pane offers your browser; see [Troubleshooting](troubleshooting.md#the-preview-is-blank-or-opens-in-its-own-window).
- An SVG is at most 4 million characters.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Troubleshooting](troubleshooting.md)
