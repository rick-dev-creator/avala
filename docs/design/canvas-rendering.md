# Canvas rendering

**Accepted**: Avala offers SVG and Markdown; Mermaid and HTML are only possible as optional renderer plugins, see [the decision](#mermaid-and-html-the-decision).

Avala offers, harnesses adapt. Agents draw canvases in the media types Avala's renderers declare, today `image/svg+xml` and `text/markdown`, streamed as throttled `CanvasUpdated` snapshots, see the [core design](core.md#canvas). The canvas tool handed to every harness lists exactly those types, so a diagram is drawn as SVG, and a canvas in any other type is rejected by the Canvas module and shown as its source. Canvas content is untrusted: it renders in an isolated surface with no network access by default. This page records how each offered type is rendered, how the offer is declared and why Mermaid and HTML are left to optional plugins.

## The offer

| Part | Where | Role |
| --- | --- | --- |
| `CanvasFormat` | `Avala.Canvas.Contracts` | One offered format: its media type, a name for people and the model, such as `SVG`, and guidance for the model, such as "Draw every diagram, chart, flow, screen or design as SVG." No UI framework, so a renderer plugin declares it from its core registration, `IPlugin.Register`. |
| `RenderedFormats` | `Avala.Rendering`, `Offer` | The Rendering plugin's declaration: SVG, then Markdown, registered as `CanvasFormat` services. Its renderers in `Avala.Rendering.UI` take their media types from it. |
| `CanvasOffer` | `Avala.Canvas`, `Drawing` | Every registered `CanvasFormat`, in registration order. It decides whether a media type is offered, compared without parameters and case. |
| `CanvasTool` | `Avala.Canvas`, `Drawing` | Built from the offer: the schema's `mediaType` is an `enum` of exactly the offered types, and the description lists each type with its name and guidance and says that any other type is not drawn. |

A unit test of the Rendering plugin proves the two registrations agree: the media types its core registration declares are exactly the media types its `RegisterViews` registers renderers for, both ways. A host test proves the same in the composed application: the canvas tool's `enum` is the declared formats, each has a renderer, and Mermaid and HTML have none. A renderer plugin added later, such as Mermaid, declares its `CanvasFormat` beside its renderer and appears in the offer, and so in the tool, without any change to the Canvas module.

## The surface

| Part | Where | Role |
| --- | --- | --- |
| `CanvasSurfaceViewModel` | `Avala.Components`, `Canvases` | One canvas: its title, kind, status, every snapshot kept as a version, the version shown, and the commands to step back, forward, return to the latest, open and close the focused view. No UI framework. |
| `CanvasSurfaceView` | `Avala.Components.UI` | The inside of the card of the approved design: a caption with the title, the kind, the spinning arc and "Drawing" while it streams, the version stepper and "Open", the drawing below, and the focused view, a popup of 86% of the window, closed with Escape or its close button. The card's frame belongs to its host, so the surface never draws a second border, title or spinner. |
| `CanvasPresenter` | `Avala.Components.UI` | Draws one `CanvasRendering` through the renderer registered for its media type and keeps the previous drawing until the new one is complete. A rendering the Canvas module did not offer is never drawn: it shows its source, through the source view registered for its type when there is one. |
| `ICanvasRenderer` | `Avala.Components.UI`, `Canvases` | The extension point: its `MediaType` and `Render(content)`, which returns `None` when the content cannot be drawn yet. A plugin registers one with `views.AddCanvasRenderer(renderer)` from its `RegisterViews`, as a `CanvasRendererTemplate`, and declares the same media type as a `CanvasFormat` from its `Register`. |
| `CanvasSourceTemplate` | `Avala.Components.UI`, `Canvases` | A highlighted source view for a type that is not offered, registered with `views.AddCanvasSource(mediaType, highlight)`. It is not a renderer and offers nothing. |
| `Avala.Rendering` | `src/Modules/Rendering` | The renderer plugin: Markdown and SVG, offered, and the highlighted source views of Mermaid and HTML, not offered. Its core holds the offer, the SVG sanitizer and the source highlighter, its `.UI` assembly the renderers and the plugin entry. |

The Workbench's `CanvasViewModel` owns a surface and hands it every snapshot of its `CanvasEntry`. The Canvas module's snapshots are the only source of a canvas entry's content and status: the end of the canvas's item does not close the entry, because the module's final snapshot, flushed on that same event, may reach the board after it, and closing the entry first would hand the surface a final version with the content of an earlier, throttled snapshot. `CanvasView` draws the conversation's canvas card, its fill, border and corners, and hosts the surface inside it.

### Registration

`IViewRegistrar.Register(IDataTemplate)` lets a plugin contribute a data template beside its views; the view registry tries the templates registered last first, so a plugin can replace a built-in renderer. `AddCanvasRenderer` wraps a renderer in a template that matches an offered `CanvasRendering` of its media type, compared without parameters and case, so `text/markdown; charset=utf-8` is Markdown; `AddCanvasSource` wraps a highlighter in a template that matches only renderings that were not offered. The presenter finds either with Avalonia's `FindDataTemplate`, the same lookup every view uses, and needs no service.

### A canvas that was not offered

A harness may still send a canvas in a type the tool did not offer. The Canvas module opens it, rejects it with `CanvasError.NotOffered`, which it logs, and keeps it as source: its snapshots carry `IsOffered` false. The Workbench passes that flag through its `CanvasEntry` and the surface's `CanvasDraft` to the `CanvasRendering`, and the presenter shows the source with the note "Avala does not offer Mermaid canvases to agents, so this one is not drawn. Showing its source.", highlighted when a source view is registered for the type, even if some renderer could draw it. Nothing of the canvas is lost, and the conformance kit reports the harness. A canvas started without a media type is opened the same way, as `text/plain` source rejected with `CanvasError.MissingMediaType`, so every canvas the board shows is followed by the module to its final snapshot.

### Streaming without flicker

- Each snapshot whose content differs becomes a version; a snapshot that only closes the canvas finishes the latest version instead of adding one. The surface keeps the 24 most recent versions, and never drops the one shown.
- The surface follows the latest version until a person steps back; new snapshots then add versions without moving the drawing, and the version label returns to the latest.
- The presenter renders a new version off screen. If it renders, it is added above the previous drawing and fades in over `StreamDuration`; the previous drawing is removed when the fade ends, on a timer of the presenter's `Clock`, a `TimeProvider` that scripts replace with a `FakeTimeProvider`. There is never an empty frame.
- If it does not render, such as an SVG cut in the middle of a tag, the previous drawing stays while the canvas streams. A final version that cannot be rendered, a version a person steps to, or the first snapshot of another canvas shows its source with a note instead. Renderings carry their surface, so a presenter switched to another canvas mid-stream never keeps the first canvas's drawing.
- A canvas larger than 512 KB is not rendered: the presenter shows the first 64 KB of its source and how much is hidden.
- A presenter renders only while attached, so the focused view costs nothing until it opens.

### Untrusted content

| Type | What is blocked |
| --- | --- |
| SVG | `SvgSanitizer` parses with DTDs ignored, no resolver and a 4 M character cap, so entities, external or recursive, are never expanded and a document that uses one is rejected. It removes `script`, `foreignObject`, `iframe`, `object`, `embed`, media and listener elements, animations that target a reference or a handler, processing instructions such as `xml-stylesheet`, every `on*` attribute, every `href`, `xlink:href` or `src` that is not a fragment or an embedded PNG, JPEG, GIF or WebP, and every CSS `@import` and `url()` that is not a fragment. The card says how many things it blocked. Only the sanitized markup reaches Svg.Skia. |
| Markdown | Images that are not embedded are replaced by their alternative text before rendering, so nothing is fetched; a link click is handled and opens nothing. Raw HTML in the Markdown is never run, and the images it names are never loaded. |
| Mermaid, HTML and any other type | Not offered: shown as source only, highlighted for Mermaid and HTML. |

## Packages

| Package | Version | License | Why this version |
| --- | --- | --- | --- |
| [MarkView.Avalonia](https://github.com/Kryptos-FR/MarkView.Avalonia) | 12.2.1 | MIT | Built for Avalonia 12, depends only on Avalonia and Markdig. 12.3.0 was published the day before this work; 12.2.1 has been out since August. |
| [Markdig](https://github.com/xoofx/markdig), through MarkView | 1.3.2 | BSD-2-Clause | |
| [Svg.Controls.Skia.Avalonia](https://github.com/wieslawsoltes/Svg.Skia) | 12.0.0.13 | MIT | The last release on Svg.Skia 5.1.x, which uses SkiaSharp 3.119 like Avalonia 12.1.3. From Svg.Skia 5.2 the packages require SkiaSharp 4.148, and plugins share the host's SkiaSharp. |
| Svg.Skia, Svg.Model, Svg.SceneGraph, Svg.Animation, ShimSkiaSharp, through it | 5.1.1 | MIT | |
| Svg.Custom, through it | 5.1.1 | MS-PL | A fork of SVG.NET; permissive, with a notice requirement for redistribution. |
| ExCSS, through it | 4.3.1 | MIT | |

Together they add about 4 MB of managed code to the plugin folder. SkiaSharp and HarfBuzz, with their native libraries, come from the host, which ships them for Avalonia, and the renderer tests reference `Avalonia.Desktop` as the host does; the plugin excludes their native assets, which would otherwise copy half a gigabyte of native libraries for every platform into its folder.

Considered for Markdown and not taken: [LiveMarkdown.Avalonia](https://github.com/DearVa/LiveMarkdown.Avalonia) 2.4.3, Apache-2.0, built for streamed LLM output with append-only updates, but it brings TextMateSharp and the native Onigwrap library; Markdown.Avalonia 12.0.0, MIT, only alpha builds for Avalonia 12. The presenter already avoids flicker by rendering off screen, so MarkView's whole-document rendering is enough at one snapshot per 100 ms.

## Mermaid and HTML: the decision

**Decided**: Avala offers SVG and Markdown and nothing else. A diagram is drawn as SVG, which the agent writes directly and the sanitizer already guards, so no diagram language is needed in the core. Mermaid and HTML remain possible only as optional renderer plugins: such a plugin declares its `CanvasFormat` and registers its renderer, and from then on its type appears in the offer and in the canvas tool automatically, with no change to the Canvas module or to any harness plugin. Until one is installed, a Mermaid or HTML canvas is not offered and shows its highlighted source, see [a canvas that was not offered](#a-canvas-that-was-not-offered).

Both need more than a .NET control: Mermaid's own renderer is JavaScript that lays out against a DOM, and HTML is a browser's job. The options below are kept for whoever builds one of those plugins.

### Mermaid

| Option | License | Size | Isolation and offline | Trade-offs |
| --- | --- | --- | --- | --- |
| A. [Mermaider](https://github.com/nullean/mermaider): Mermaid to SVG in pure .NET, then the SVG renderer | MIT | Small: the library and its Sugiyama layout, no native code | Offline, no JavaScript. Its output is already sanitized by an allow-list, and it goes through `SvgSanitizer` as well | Covers the 24 common diagram types; pre-1.0 (0.15.1, October 2026), young and fast-moving, and its layout is not identical to mermaid.js |
| B. mermaid.js inside a web view | MIT (mermaid) plus the web view's | About 3 MB of bundled JavaScript plus the web view below | As isolated as the web view; mermaid.js would be bundled, never fetched | Exact rendering, every diagram type; needs the HTML web view first |
| C. mermaid.js in a .NET JavaScript engine such as Jint | BSD-2 | Small | Offline | Not workable: mermaid measures text and lays out through a DOM |
| D. Source only, as today | | None | Fully isolated | No diagram |

### HTML

| Option | License | Size | Isolation and offline | Platforms |
| --- | --- | --- | --- | --- |
| A. [Avalonia.Controls.WebView](https://docs.avaloniaui.net/controls/web/nativewebview) 12.1.0, the operating system's web view | MIT, by AvaloniaUI OÜ | Small: nothing bundled | Out of process on every platform. `NavigateToString` renders without a URL; network must be closed by a strict Content-Security-Policy injected into the page and by cancelling requests in `WebResourceRequested`; whether scripts can be turned off differs by platform | WebView2 on Windows, preinstalled on 10 and 11; WKWebView on macOS; WPE WebKit or WebKitGTK on Linux, which must be installed and often is not |
| B. CEF through CefGlue or WebViewControl-Avalonia | BSD-3 (CEF) | 150 to 250 MB per platform | Full control: request interception, scripts off, no network, offline | Same Chromium everywhere, but huge, and Chromium security updates become Avala's job |
| C. [Avalonia.HtmlRenderer](https://www.nuget.org/packages/Avalonia.HtmlRenderer) 12.0.0, a native HTML and CSS renderer | MIT | Small | No JavaScript at all; images and stylesheets load only through a handler Avala controls, so nothing leaves the machine | Everywhere Avalonia runs. HTML 4 and CSS 2.1 only: no flexbox, grid or scripts |
| D. Source only, as today | | None | Fully isolated | Everywhere |

Opening the page in the person's browser is not on the list: it would give untrusted content the network and the person's session.

### If a plugin is ever built

- **Mermaid: A, Mermaider.** It keeps Mermaid inside the SVG pipeline that is already sanitized and tested, adds no JavaScript and no native code, and works offline on every platform. Ship it as its own renderer plugin, pinned, with the source view as the fallback when it cannot parse a diagram, so it can be replaced by B if fidelity ever matters more than size.
- **HTML: C first, A later and only on request.** HtmlRenderer draws the static reports and tables agents usually produce with no JavaScript and no network, which is the isolation the core design asks for. If interactive pages turn out to matter, add the operating system's web view as a second renderer that a person opts into per canvas, with the CSP and the request blocking above, and keep CEF out: its size would exceed the rest of Avala many times over.

Neither is built; each would be an optional plugin, not part of the offer Avala ships.

## Acceptance criteria

The scripts that play them live in `CanvasSurfaceViewModelScripts`, `CanvasSurfaceViewScripts`, `CanvasRendererScripts`, `SvgSanitizerTests`, `SourceHighlighterTests`, the Workbench's `CanvasViewModelScripts` and `CanvasViewScripts`, the Canvas module's `CanvasToolTests`, `CanvasDocumentTests`, `CanvasGalleryTests` and `CanvasFeedTests`, the conformance kit's tests and the host's `CanvasOfferTests`.

```
AC1  Given a new surface, when the first snapshot of a streaming canvas arrives, then it is shown as version 1 of 1, with "Drawing" and a spinning arc.
AC2  Given a streaming canvas, when snapshots that change it arrive, then each becomes a version and the surface shows the latest.
AC3  Given a canvas whose content stops changing, when it closes, then its latest version is finished and no version is added; failed and stopped canvases say so.
AC4  Given several versions, when a person steps back, then the earlier version is drawn and forward and latest are offered; previous is disabled at the first.
AC5  Given a person stepped back, when a new snapshot arrives, then the drawing stays where they put it until they return to the latest.
AC6  Given more snapshots than the limit, then the oldest versions are dropped and the one shown survives.
AC7  Given a drawing, when Open is chosen, then the focused view shows it at 86% of the window, and Escape closes it; Open is disabled before anything is drawn.
AC8  Given a streaming canvas, when a snapshot cannot be rendered yet, then the previous drawing stays; when one can, it fades in over the previous one, which is removed once the new one is opaque.
AC9  Given a finished canvas that cannot be rendered, then its source is shown with a note.
AC10 Given a canvas of a kind no renderer knows, then its source is shown with a note.
AC11 Given a canvas over 512 KB, then the start of its source is shown with its size and how much is hidden, and nothing is rendered.
AC12 Given a view switched to another canvas mid-stream, then it shows only the new canvas, never the first one's later snapshots or drawing.
AC13 Given an SVG with scripts, handlers, external references, entities or imports, then none of them reaches the renderer and the card says how many were blocked; local references and embedded images are kept.
AC14 Given Markdown with a remote image or a link, then nothing is fetched or opened.
AC15 Given a Mermaid or HTML canvas that was not offered, then its highlighted source is shown with the note that Avala does not offer it, and highlighting never changes the text.
AC16 Given the Rendering plugin, then it offers SVG and Markdown, and the media types it declares are exactly the media types it registers renderers for; Mermaid and HTML have no renderer.
AC17 Given the declared formats, when the Canvas module offers its tool, then the schema's mediaType enum is exactly the declared media types in order, and the description lists each with its name and guidance, SVG asking for every diagram, and says any other type is not drawn.
AC18 Given a canvas in a media type that is not offered, when it starts, then the Canvas module rejects it with NotOffered, keeps its content and publishes snapshots with IsOffered false; offered types, with parameters or in any case, open as offered.
AC19 Given a canvas that was not offered, then the surface shows its source with the note "Avala does not offer <type> canvases to agents, so this one is not drawn. Showing its source.", even when a renderer knows its type.
AC20 Given a harness given the canvas tool, when it draws a canvas in a media type the tool does not offer, then the conformance kit reports it; in an offered type, it conforms.
AC21 Given the simulator's canvas scenario in the composed application, then its two SVG diagrams and its Markdown notes are drawn in the conversation; given its unoffered-canvas scenario, the Mermaid canvas is shown as source with the note.
```
