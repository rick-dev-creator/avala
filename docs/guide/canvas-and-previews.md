# Canvas and previews

Agents draw diagrams, charts and notes on a canvas in the conversation, and a page or app a job serves shows in a preview beside it.

## What it does

- **The canvas** is a tool Avala offers the agent. It draws three formats in the app's theme:
  - **SVG**, for diagrams, charts, flows, screens and designs, still or animated with SMIL (`<animate>`, `<animateTransform>`, `<animateMotion>`, `<set>`);
  - **Markdown**, for notes, tables, plans and reports;
  - **Mermaid**, for flowcharts and sequence, state, class and entity-relationship diagrams.
- **SVG is sanitized.** Scripts, embedded pages, event handlers and external references are removed, and a note under the drawing says how many were blocked. Images embedded as `data:` PNG, JPEG, GIF or WebP stay.
- **The preview** shows a page the job serves beside its conversation. A server a job starts opens the pane by itself, once; a link in a reply to that server opens in the pane too.
- **A page or a 3D model the agent writes** shows as a card on its line in the conversation. Choosing the card shows it in the preview beside the conversation, and choosing it again reloads it, so you see the latest version. A 3D model, `.glb`, `.gltf`, `.stl` or `.obj`, opens in Avala's own viewer: drag to orbit, scroll to zoom, right-drag to pan, and **Fit**, **Wireframe** and **Grid** in its bar.
- **Previews are confined.** Every page goes through a loopback gateway of the job's own, which keeps other sites out until you allow the network, and a link that leaves the job's pages is offered to your browser instead.

## How to use it

1. **Ask for something visual**, such as "draw the architecture" or "chart the results": the agent draws it on a canvas.
2. **Play an animation again** with the play button that appears on hover, when reduced motion is on.
3. **Look closer** with **Open** in the canvas's header, and close it with Escape. An SVG or Mermaid diagram opens fitted, with the same zoom as the Docs pane: **Zoom out**, the zoom in percent (choose it for the actual size), **Zoom in** and **Fit to view**; the wheel or a pinch zooms where the pointer is and dragging moves it. Markdown scrolls.
4. **Run the project** with **Run** in the job's header: it starts the worktree's `dev` script, or `start`, with the package manager its lock file names (pnpm, yarn, bun or npm), or serves `index.html` when there is no script. Stop ends it.
5. **Open the preview** from the **Running** chip in the job's header. The pane has Back, Reload, Device, Allow network, Open in browser and Close preview.
6. **See it on a device** with **Device** in the pane's toolbar: Responsive fills the pane, and iPhone SE (375 × 667), iPhone 16 (393 × 852), iPhone 16 Pro Max (440 × 956), Pixel 9 (412 × 915), iPhone Duo open (772 × 548) or closed (380 × 548) and iPad Air 11" (820 × 1180) show the page at that device's size inside a drawing of it: its Dynamic Island, punch-hole camera, home button or fold, and its side buttons. **Turn the device** swaps its width and height. A device larger than the pane shrinks to fit it and keeps its shape: the page still lays out at the device's own width, only drawn smaller, so widen the pane to see it at full size.
7. **Turn it in 3D** with **Show the device in 3D**, beside Turn. Facing you, the screen is live: tap, scroll and type in the page and watch the agent's changes arrive. Drag the frame or the space around it to turn the device, with one finger on a touch screen; right-drag, middle-drag or Shift-drag to move it; scroll, pinch or use **−** and **+** to zoom. Turned away, a drag anywhere turns it, and a double-click, **Front** or F brings it back facing you with its screen live. **Fit** frames it again, **Spin** turns it on its own, and on the iPhone Duo **Fold** (or D) folds or unfolds it: the page moves between the inner and the outer screen without reloading, so what you did in it stays. Choose the 3D button again to go back to the flat frame.
8. **Resize the pane** by dragging its edge, and reset it with a double-click.

## Settings and files

- **Reduce motion**: Settings, Appearance, System, On or Off. On holds animations where they settle.
- **`layout.json`** in the data folder keeps the pane's share of the width as `previewShare`: 0.45 by default, between 0.25 and 0.7.
- A script started by Run gets `PORT`, `HOST=127.0.0.1`, `AVALA_PORT` and `BROWSER=none`.

## Good to know

- The 3D viewer draws only when something moves, so a still model costs nothing while you look at it; an animated glTF plays its animations. The viewer comes with Avala and loads nothing from the internet.
- Nothing on a canvas responds to input, and CSS animations and scripts do not run. Anything interactive, such as an app or a three.js scene, belongs in a page the agent serves for the preview.
- A canvas in any other format shows its source. A Mermaid diagram that cannot be drawn shows its source with a note.
- Allow network lasts for that job until Avala restarts.
- The iPhone, Pixel and iPad models are drawn by Avala from each device's measurements. The iPhone Duo is a model by PikkmeStudios under CC BY 4.0, credited in the 3D view's bar.
- While the iPhone Duo folds, the page leaves its screen and comes back when the fold ends: a live page cannot bend with a flexible screen.
- The device you choose stays for every job until Avala restarts. Device sets the page's size only: the page still gets a desktop user agent and mouse events, not touches.
- Leaving the jobs page, for Overview or Settings, closes the page's web view so nothing keeps running unseen; coming back opens it again at its first address.
- The preview uses the system's web view: WebView2 on Windows, WKWebView on macOS, WPE WebKit or WebKitGTK on Linux. Where the page cannot be embedded, it opens in a window of its own, or the pane offers your browser; see [Troubleshooting](troubleshooting.md#the-preview-is-blank-or-opens-in-its-own-window).
- An SVG is at most 4 million characters.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Troubleshooting](troubleshooting.md)
