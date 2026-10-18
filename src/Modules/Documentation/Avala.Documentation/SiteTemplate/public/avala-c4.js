const svgNs = "http://www.w3.org/2000/svg";
const nodeWidth = 188;
const nodeHeight = 92;
const columnGap = 44;
const rowGap = 112;
const regionGap = 36;
const framePad = 22;
const speeds = [1, 1.5, 2, 0.5];
const stepPause = 1900;
const hopPause = 1400;

const kinds = {
  Person: ["Person", "person", false], Person_Ext: ["Person", "person", true],
  System: ["Software system", "box", false], System_Ext: ["Software system", "box", true],
  SystemDb: ["Database", "db", false], SystemDb_Ext: ["Database", "db", true],
  SystemQueue: ["Queue", "queue", false], SystemQueue_Ext: ["Queue", "queue", true],
  Container: ["Container", "box", false], Container_Ext: ["Container", "box", true],
  ContainerDb: ["Container", "db", false], ContainerDb_Ext: ["Container", "db", true],
  ContainerQueue: ["Container", "queue", false], ContainerQueue_Ext: ["Container", "queue", true],
  Component: ["Component", "box", false], Component_Ext: ["Component", "box", true],
  ComponentDb: ["Component", "db", false], ComponentDb_Ext: ["Component", "db", true],
  ComponentQueue: ["Component", "queue", false], ComponentQueue_Ext: ["Component", "queue", true],
};
const technical = /^(Container|Component)/;

function argumentsOf(text) {
  const values = [];
  let current = "";
  let quoted = false;
  for (const character of text) {
    if (character === "\"") {
      quoted = !quoted;
    } else if (character === "," && !quoted) {
      values.push(current.trim());
      current = "";
    } else {
      current += character;
    }
  }
  values.push(current.trim());
  return values.filter(value => !value.startsWith("$"));
}

function keep(declared, declaration) {
  const earlier = declared.findIndex(found => found.alias === declaration.alias);
  if (earlier >= 0) {
    declared[earlier] = { ...declaration, order: declared[earlier].order };
  } else {
    declared.push(declaration);
  }
}

function read(source) {
  const model = { level: "", title: "", elements: [], boundaries: [], relations: [] };
  const open = [];
  for (const raw of source.split("\n")) {
    const line = raw.trim();
    const header = /^C4(Context|Container|Component|Dynamic)\b/.exec(line);
    if (header) {
      model.level = header[1];
      continue;
    }
    if (line.startsWith("title ")) {
      model.title = line.slice(6).trim();
      continue;
    }
    if (line === "}") {
      open.pop();
      continue;
    }
    const call = /^([A-Za-z_]+)\((.*)\)\s*(\{)?$/.exec(line);
    if (!call) {
      continue;
    }
    const [, name, body, opens] = call;
    const values = argumentsOf(body);
    if (name.endsWith("Boundary")) {
      const earlier = model.boundaries.find(found => found.alias === values[0]);
      const boundary = { alias: values[0], label: values[1] || values[0], parent: earlier ? earlier.parent : open.at(-1) || null };
      keep(model.boundaries, boundary);
      if (opens) {
        open.push(boundary.alias);
      }
    } else if (kinds[name]) {
      const [kind, shape, external] = kinds[name];
      const hasTechnology = technical.test(name);
      keep(model.elements, {
        alias: values[0], label: values[1] || values[0], kind, shape, external,
        technology: hasTechnology ? values[2] || "" : "",
        description: (hasTechnology ? values[3] : values[2]) || "",
        boundary: open.at(-1) || null, order: model.elements.length,
      });
    } else if (/^(Rel|BiRel|Rel_[A-Za-z]+|RelIndex)$/.test(name)) {
      const indexed = name === "RelIndex" ? values.slice(1) : values;
      const backwards = name === "Rel_Back";
      model.relations.push({
        from: backwards ? indexed[1] : indexed[0], to: backwards ? indexed[0] : indexed[1],
        label: indexed[2] || "", technology: indexed[3] || "", both: name === "BiRel",
      });
    }
  }
  model.relations = model.relations.filter(relation =>
    model.elements.some(element => element.alias === relation.from) && model.elements.some(element => element.alias === relation.to));
  return model;
}

function ranks(model) {
  const rank = new Map(model.elements.map(element => [element.alias, 0]));
  const edges = model.relations.filter(relation => relation.from !== relation.to);
  for (let pass = 0; pass < model.elements.length; pass++) {
    let changed = false;
    for (const edge of edges) {
      const next = rank.get(edge.from) + 1;
      if (next > rank.get(edge.to) && next < model.elements.length) {
        rank.set(edge.to, next);
        changed = true;
      }
    }
    if (!changed) {
      break;
    }
  }
  return rank;
}

function topBoundary(model, alias) {
  let current = alias;
  let boundary = model.boundaries.find(candidate => candidate.alias === current);
  while (boundary && boundary.parent) {
    current = boundary.parent;
    boundary = model.boundaries.find(candidate => candidate.alias === current);
  }
  return current;
}

function layout(model) {
  const rank = ranks(model);
  const regions = [];
  for (const element of model.elements) {
    const key = element.boundary ? topBoundary(model, element.boundary) : `outside:${regions.length}`;
    const region = element.boundary ? regions.find(candidate => candidate.key === key) : null;
    if (region) {
      region.members.push(element);
    } else if (!element.boundary && regions.length && regions.at(-1).outside) {
      regions.at(-1).members.push(element);
    } else {
      regions.push({ key, outside: !element.boundary, members: [element] });
    }
  }
  const depth = Math.max(0, ...rank.values()) + 1;
  const positions = new Map();
  let left = framePad;
  for (const region of regions) {
    const rows = Array.from({ length: depth }, () => []);
    region.members.forEach(element => rows[rank.get(element.alias)].push(element));
    const columns = Math.max(...rows.map(row => row.length));
    const width = columns * nodeWidth + (columns - 1) * columnGap;
    rows.forEach((row, index) => {
      const used = row.length * nodeWidth + (row.length - 1) * columnGap;
      row.forEach((element, column) => positions.set(element.alias, {
        x: left + (width - used) / 2 + column * (nodeWidth + columnGap),
        y: framePad + 8 + index * (nodeHeight + rowGap),
      }));
    });
    left += width + regionGap + (region.outside ? 0 : framePad);
  }
  const frames = model.boundaries.map(boundary => {
    const inside = model.elements.filter(element => within(model, element.boundary, boundary.alias));
    if (!inside.length) {
      return null;
    }
    const nested = nesting(model, boundary.alias);
    const pad = framePad - nested * 8;
    const xs = inside.map(element => positions.get(element.alias).x);
    const ys = inside.map(element => positions.get(element.alias).y);
    return {
      boundary, x: Math.min(...xs) - pad, y: Math.min(...ys) - pad,
      width: Math.max(...xs) - Math.min(...xs) + nodeWidth + pad * 2,
      height: Math.max(...ys) - Math.min(...ys) + nodeHeight + pad * 2 + 22,
    };
  }).filter(Boolean);
  const width = Math.max(left - regionGap + framePad, nodeWidth + framePad * 2);
  const height = framePad * 2 + 30 + depth * nodeHeight + (depth - 1) * rowGap;
  return { positions, frames, width, height };
}

function within(model, alias, ancestor) {
  let current = alias;
  while (current) {
    if (current === ancestor) {
      return true;
    }
    current = (model.boundaries.find(boundary => boundary.alias === current) || {}).parent;
  }
  return false;
}

function nesting(model, alias) {
  let depth = 0;
  let current = (model.boundaries.find(boundary => boundary.alias === alias) || {}).parent;
  while (current) {
    depth++;
    current = (model.boundaries.find(boundary => boundary.alias === current) || {}).parent;
  }
  return depth;
}

function slug(text) {
  return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function element(name, attributes, parent) {
  const node = document.createElementNS(svgNs, name);
  for (const [key, value] of Object.entries(attributes)) {
    node.setAttribute(key, value);
  }
  if (parent) {
    parent.append(node);
  }
  return node;
}

function wrap(text, limit) {
  const lines = [];
  let line = "";
  for (const word of text.split(/\s+/).filter(Boolean)) {
    if ((line + " " + word).trim().length > limit && line) {
      lines.push(line);
      line = word;
    } else {
      line = (line + " " + word).trim();
    }
  }
  if (line) {
    lines.push(line);
  }
  return lines.length > 2 ? [lines[0], lines[1].replace(/.{0,2}$/, "…")] : lines;
}

function border(from, to) {
  const cx = from.x + nodeWidth / 2;
  const cy = from.y + nodeHeight / 2;
  const dx = to.x + nodeWidth / 2 - cx;
  const dy = to.y + nodeHeight / 2 - cy;
  const scale = Math.min(Math.abs(nodeWidth / 2 / (dx || 1e-6)), Math.abs(nodeHeight / 2 / (dy || 1e-6)));
  return { x: cx + dx * scale, y: cy + dy * scale };
}

const styles = `
:host { display: block; margin: 1.25rem 0 1.75rem; color: var(--bs-body-color, #1d1d1f); font: 13px/1.35 system-ui, -apple-system, "Segoe UI", sans-serif; }
.card { border: 1px solid var(--bs-border-color, #d2d2d7); border-radius: 12px; background: var(--bs-tertiary-bg, #f5f5f7); overflow: hidden; }
.head { display: flex; align-items: center; gap: 10px; padding: 10px 14px; border-bottom: 1px solid var(--bs-border-color, #d2d2d7); }
.title { font-weight: 600; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.level { color: var(--bs-secondary-color, #6e6e73); font-size: 12px; }
button { all: unset; cursor: pointer; display: inline-grid; place-items: center; min-width: 28px; height: 28px; padding: 0 6px; border-radius: 7px; color: var(--bs-body-color, #1d1d1f); font-size: 12px; font-variant-numeric: tabular-nums; }
button:hover { background: color-mix(in srgb, currentColor 9%, transparent); }
button:focus-visible { outline: 2px solid var(--bs-primary, #0071e3); outline-offset: 1px; }
button svg { width: 15px; height: 15px; fill: currentColor; }
.stage { overflow-x: auto; padding: 8px 6px 4px; background: var(--bs-body-bg, #fff); }
svg.diagram { display: block; margin: 0 auto; max-width: 100%; height: auto; }
.caption { min-height: 22px; padding: 9px 14px 11px; border-top: 1px solid var(--bs-border-color, #d2d2d7); color: var(--bs-secondary-color, #6e6e73); }
.caption strong { color: var(--bs-body-color, #1d1d1f); font-weight: 600; }
.progress { height: 2px; background: color-mix(in srgb, var(--bs-primary, #0071e3) 18%, transparent); }
.progress span { display: block; height: 100%; width: 0; background: var(--bs-primary, #0071e3); transition: width .3s ease; }
.node rect, .node path.shape, .node ellipse { fill: var(--bs-body-bg, #fff); stroke: color-mix(in srgb, var(--bs-body-color, #1d1d1f) 28%, transparent); stroke-width: 1.2; }
.node.external rect, .node.external path.shape, .node.external ellipse { fill: var(--bs-tertiary-bg, #f5f5f7); stroke-dasharray: 4 3; }
.node .label { font-weight: 600; font-size: 13.5px; fill: var(--bs-body-color, #1d1d1f); }
.node .kind { font-size: 11px; fill: var(--bs-secondary-color, #6e6e73); }
.node .text { font-size: 11.5px; fill: var(--bs-secondary-color, #6e6e73); }
.node.linked { cursor: pointer; }
.node.linked:hover rect, .node.linked:hover path.shape { stroke: var(--bs-primary, #0071e3); }
.node, .relation, .frame { transition: opacity .45s ease; }
.node.dim, .relation.dim { opacity: .3; }
.node.hidden, .relation.hidden, .frame.hidden { opacity: 0; }
.node.focus rect, .node.focus path.shape, .node.focus ellipse { stroke: var(--bs-primary, #0071e3); stroke-width: 2.2; }
.frame rect { fill: none; stroke: color-mix(in srgb, var(--bs-body-color, #1d1d1f) 30%, transparent); stroke-dasharray: 6 4; stroke-width: 1.1; }
.frame text { font-size: 11.5px; font-weight: 600; fill: var(--bs-secondary-color, #6e6e73); }
.relation path { fill: none; stroke: color-mix(in srgb, var(--bs-body-color, #1d1d1f) 45%, transparent); stroke-width: 1.3; }
.relation.focus path { stroke: var(--bs-primary, #0071e3); stroke-width: 2; stroke-dasharray: 7 5; animation: flow .9s linear infinite; }
.relation text { font-size: 11.5px; fill: var(--bs-body-color, #1d1d1f); paint-order: stroke; stroke: var(--bs-body-bg, #fff); stroke-width: 5px; stroke-linejoin: round; }
.relation .index { font-size: 10.5px; font-weight: 700; fill: #fff; stroke: none; }
.relation .badge { fill: var(--bs-primary, #0071e3); }
.relation.focus text:not(.index) { fill: var(--bs-primary, #0071e3); }
.arrow { fill: color-mix(in srgb, var(--bs-body-color, #1d1d1f) 45%, transparent); }
.arrow.focus { fill: var(--bs-primary, #0071e3); }
@keyframes flow { to { stroke-dashoffset: -24; } }
@media (prefers-reduced-motion: reduce) { .node, .relation, .frame, .progress span { transition: none; } .relation.focus path { animation: none; } }
`;

const icons = {
  play: "M4 2.5v11l9.5-5.5z",
  pause: "M3.5 2.5h3v11h-3zM9.5 2.5h3v11h-3z",
  previous: "M3 2.5h2v11H3zM13.5 2.5v11L6 8z",
  next: "M11 2.5h2v11h-2zM2.5 2.5v11L10 8z",
};

class AvalaC4 extends HTMLElement {
  connectedCallback() {
    if (this.shadowRoot) {
      return;
    }
    this.model = read(this.getAttribute("source") || this.textContent || "");
    this.steps = this.storyOf(this.model);
    this.step = this.steps.length - 1;
    this.speed = 0;
    this.timer = 0;
    this.playing = false;
    const root = this.attachShadow({ mode: "open" });
    root.innerHTML = `<style>${styles}</style><div class="card"><div class="head"><span class="title"></span><span class="level"></span>
      <button class="previous" aria-label="Previous step"><svg viewBox="0 0 16 16"><path d="${icons.previous}"/></svg></button>
      <button class="play" aria-label="Play"><svg viewBox="0 0 16 16"><path d="${icons.play}"/></svg></button>
      <button class="next" aria-label="Next step"><svg viewBox="0 0 16 16"><path d="${icons.next}"/></svg></button>
      <button class="speed" aria-label="Speed">1×</button></div>
      <div class="progress"><span></span></div><div class="stage"></div><div class="caption" aria-live="polite"></div></div>`;
    root.querySelector(".title").textContent = this.model.title || "Architecture";
    root.querySelector(".level").textContent = this.model.level === "Dynamic" ? "Flow" : this.model.level;
    root.querySelector(".play").addEventListener("click", () => this.toggle());
    root.querySelector(".previous").addEventListener("click", () => this.go(this.step - 1, true));
    root.querySelector(".next").addEventListener("click", () => this.go(this.step + 1, true));
    root.querySelector(".speed").addEventListener("click", () => {
      this.speed = (this.speed + 1) % speeds.length;
      root.querySelector(".speed").textContent = `${speeds[this.speed]}×`;
    });
    this.draw(root.querySelector(".stage"));
    this.show();
  }

  disconnectedCallback() {
    clearTimeout(this.timer);
  }

  storyOf(model) {
    const steps = [];
    const rank = ranks(model);
    const levels = [...new Set(model.elements.map(element => rank.get(element.alias)))].sort((a, b) => a - b);
    const shown = new Set();
    for (const level of levels) {
      const added = model.elements.filter(element => rank.get(element.alias) === level);
      added.forEach(element => shown.add(element.alias));
      steps.push({ kind: "elements", shown: new Set(shown), relations: 0, focus: added.map(element => element.alias), hop: -1 });
    }
    model.relations.forEach((relation, index) =>
      steps.push({ kind: "hop", shown, relations: index + 1, focus: [relation.from, relation.to], hop: index }));
    steps.push({ kind: "whole", shown, relations: model.relations.length, focus: [], hop: -1 });
    return steps;
  }

  draw(stage) {
    const { positions, frames, width, height } = layout(this.model);
    const svg = element("svg", { class: "diagram", viewBox: `0 0 ${width} ${height}`, width, height, role: "img" });
    element("title", {}, svg).textContent = this.model.title || "C4 diagram";
    const defs = element("defs", {}, svg);
    for (const [id, css] of [["arrow", "arrow"], ["arrow-focus", "arrow focus"]]) {
      const marker = element("marker", { id: `${id}`, viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 7, markerHeight: 7, orient: "auto-start-reverse" }, defs);
      element("path", { d: "M0 0L10 5L0 10z", class: css }, marker);
    }
    this.frames = frames.map(frame => {
      const group = element("g", { class: "frame" }, svg);
      element("rect", { x: frame.x, y: frame.y, width: frame.width, height: frame.height, rx: 14 }, group);
      element("text", { x: frame.x + 14, y: frame.y + frame.height - 11 }, group).textContent = frame.boundary.label;
      return { group, frame };
    });
    this.relations = this.model.relations.map((relation, index) => {
      const from = positions.get(relation.from);
      const to = positions.get(relation.to);
      const group = element("g", { class: "relation" }, svg);
      const start = border(from, to);
      const end = border(to, from);
      const path = element("path", { d: `M${start.x} ${start.y}L${end.x} ${end.y}`, "marker-end": "url(#arrow)" }, group);
      if (relation.both) {
        path.setAttribute("marker-start", "url(#arrow)");
      }
      const mx = start.x + (end.x - start.x) * 0.42;
      const my = start.y + (end.y - start.y) * 0.42;
      const label = relation.technology ? `${relation.label} [${relation.technology}]` : relation.label;
      element("text", { x: mx, y: my + 4, "text-anchor": "middle" }, group).textContent = label;
      if (this.model.level === "Dynamic") {
        element("circle", { class: "badge", cx: mx - label.length * 3.1 - 14, cy: my, r: 9 }, group);
        element("text", { class: "index", x: mx - label.length * 3.1 - 14, y: my + 3.6, "text-anchor": "middle" }, group).textContent = index + 1;
      }
      return { group, path, relation };
    });
    this.nodes = new Map(this.model.elements.map(item => {
      const { x, y } = positions.get(item.alias);
      const target = this.closest("article")?.querySelector(`[id="${slug(item.label)}"], [id="${slug(item.alias)}"]`);
      const group = element("g", { class: `node${item.external ? " external" : ""}${target ? " linked" : ""}`, transform: `translate(${x} ${y})` }, svg);
      if (item.shape === "db") {
        element("path", { class: "shape", d: `M0 10a${nodeWidth / 2} 10 0 0 1 ${nodeWidth} 0v${nodeHeight - 20}a${nodeWidth / 2} 10 0 0 1 -${nodeWidth} 0z` }, group);
        element("path", { class: "shape", d: `M0 10a${nodeWidth / 2} 10 0 0 0 ${nodeWidth} 0`, fill: "none" }, group);
      } else {
        element("rect", { width: nodeWidth, height: nodeHeight, rx: item.shape === "queue" ? nodeHeight / 2 : item.shape === "person" ? 22 : 12 }, group);
      }
      const top = item.shape === "db" ? 32 : 24;
      element("text", { class: "label", x: nodeWidth / 2, y: top, "text-anchor": "middle" }, group).textContent = item.label;
      element("text", { class: "kind", x: nodeWidth / 2, y: top + 16, "text-anchor": "middle" }, group).textContent =
        item.technology ? `[${item.kind}: ${item.technology}]` : `[${item.kind}${item.external ? ", external" : ""}]`;
      wrap(item.description, 30).forEach((line, index) =>
        element("text", { class: "text", x: nodeWidth / 2, y: top + 35 + index * 14, "text-anchor": "middle" }, group).textContent = line);
      if (target) {
        group.setAttribute("tabindex", "0");
        group.setAttribute("role", "link");
        group.addEventListener("click", () => target.scrollIntoView({ behavior: "smooth", block: "start" }));
        group.addEventListener("keydown", event => event.key === "Enter" && target.scrollIntoView({ behavior: "smooth", block: "start" }));
      }
      return [item.alias, group];
    }));
    stage.append(svg);
  }

  show() {
    const step = this.steps[this.step];
    const root = this.shadowRoot;
    const focus = new Set(step.focus);
    const whole = step.kind === "whole";
    for (const [alias, group] of this.nodes) {
      group.classList.toggle("hidden", !step.shown.has(alias));
      group.classList.toggle("focus", focus.has(alias) && !whole);
      group.classList.toggle("dim", step.kind === "hop" && !focus.has(alias));
    }
    this.relations.forEach(({ group, path }, index) => {
      group.classList.toggle("hidden", index >= step.relations);
      group.classList.toggle("focus", index === step.hop);
      group.classList.toggle("dim", step.kind === "hop" && index !== step.hop);
      path.setAttribute("marker-end", index === step.hop ? "url(#arrow-focus)" : "url(#arrow)");
    });
    for (const { group, frame } of this.frames) {
      const members = this.model.elements.filter(item => within(this.model, item.boundary, frame.boundary.alias));
      group.classList.toggle("hidden", !members.some(item => step.shown.has(item.alias)));
    }
    root.querySelector(".progress span").style.width = `${((this.step + 1) / this.steps.length) * 100}%`;
    root.querySelector(".caption").innerHTML = this.captionOf(step);
    const icon = root.querySelector(".play path");
    icon.setAttribute("d", this.playing ? icons.pause : icons.play);
    root.querySelector(".play").setAttribute("aria-label", this.playing ? "Pause" : "Play");
  }

  captionOf(step) {
    const named = alias => this.escape(this.model.elements.find(item => item.alias === alias).label);
    if (step.kind === "hop") {
      const relation = this.model.relations[step.hop];
      const number = this.model.level === "Dynamic" ? `${step.hop + 1}. ` : "";
      return `${number}<strong>${named(relation.from)}</strong> → <strong>${named(relation.to)}</strong>${relation.label ? `: ${this.escape(relation.label)}` : ""}`;
    }
    if (step.kind === "elements") {
      return step.focus.map(alias => `<strong>${named(alias)}</strong>`).join(", ");
    }
    return `${this.model.elements.length} elements, ${this.model.relations.length} relations. Press play to walk through it.`;
  }

  escape(text) {
    return text.replace(/[&<>"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" })[character]);
  }

  go(index, stop) {
    if (stop) {
      this.pause();
    }
    this.step = Math.max(0, Math.min(this.steps.length - 1, index));
    this.show();
  }

  toggle() {
    if (this.playing) {
      this.pause();
      return;
    }
    this.playing = true;
    this.go(this.step >= this.steps.length - 1 ? 0 : this.step + 1, false);
    this.schedule();
  }

  schedule() {
    clearTimeout(this.timer);
    const step = this.steps[this.step];
    const wait = (step.kind === "hop" ? hopPause : stepPause) / speeds[this.speed];
    this.timer = setTimeout(() => {
      if (this.step >= this.steps.length - 1) {
        this.pause();
        return;
      }
      this.go(this.step + 1, false);
      this.schedule();
    }, wait);
  }

  pause() {
    clearTimeout(this.timer);
    this.playing = false;
    this.show();
  }
}

if (!customElements.get("avala-c4")) {
  customElements.define("avala-c4", AvalaC4);
}
