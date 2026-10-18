const svgNs = "http://www.w3.org/2000/svg";
const nodeWidth = 188;
const nodeHeight = 92;
const columnGap = 44;
const rowGap = 76;
const regionGap = 36;
const framePad = 22;
const speeds = [1, 1.5, 2, 0.5];
const stepPause = 1900;
const hopPause = 1400;
const laneGap = 34;
const laneShift = 18;
const clearance = 4;
const along = [0.5, 0.35, 0.65, 0.2, 0.8, 0.1, 0.9];
const zoomStep = 1.25;
const wheelStep = 1.2;
const smallest = 0.25;
const largest = 4;
const largestFit = 1;
const kept = 48;

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
    } else if (Object.hasOwn(kinds, name)) {
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
  model.relations = model.relations.filter(relation => relation.from !== relation.to &&
    model.elements.some(element => element.alias === relation.from) && model.elements.some(element => element.alias === relation.to));
  return model;
}

function ranks(model) {
  const aliases = model.elements.map(element => element.alias);
  const index = new Map(aliases.map((alias, at) => [alias, at]));
  const links = model.relations
    .filter(relation => relation.from !== relation.to && index.has(relation.from) && index.has(relation.to))
    .map(relation => [index.get(relation.from), index.get(relation.to)]);
  const rank = aliases.map(() => 0);
  const incoming = aliases.map(() => 0);
  links.forEach(([, to]) => incoming[to]++);
  const ready = aliases.map((_, at) => at).filter(at => incoming[at] === 0);
  const done = aliases.map(() => false);
  while (ready.length || done.includes(false)) {
    const node = ready.length ? ready.shift() : done.indexOf(false);
    if (done[node]) {
      continue;
    }
    done[node] = true;
    for (const [from, to] of links) {
      if (from === node && !done[to]) {
        rank[to] = Math.max(rank[to], rank[node] + 1);
        if (--incoming[to] === 0) {
          ready.push(to);
        }
      }
    }
  }
  return new Map(aliases.map((alias, at) => [alias, rank[at]]));
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
    const pad = Math.max(6, framePad - nested * 8);
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

function measure(text, font) {
  const drawn = text.getComputedTextLength();
  if (drawn > 0) {
    return drawn;
  }
  measure.context ??= document.createElement("canvas").getContext("2d");
  measure.context.font = `${font} system-ui, -apple-system, "Segoe UI", sans-serif`;
  return measure.context.measureText(text.textContent).width;
}

function middle(box) {
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

function cuts(from, to, box) {
  const left = box.x - clearance;
  const top = box.y - clearance;
  const right = box.x + box.width + clearance;
  const bottom = box.y + box.height + clearance;
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  let enter = 0;
  let leave = 1;
  for (const [p, q] of [[-dx, from.x - left], [dx, right - from.x], [-dy, from.y - top], [dy, bottom - from.y]]) {
    if (Math.abs(p) < 1e-9) {
      if (q <= 0) {
        return false;
      }
      continue;
    }
    const t = q / p;
    if (p < 0) {
      enter = Math.max(enter, t);
    } else {
      leave = Math.min(leave, t);
    }
  }
  return enter < leave;
}

function clear(points, blocking) {
  return points.slice(1).every((point, at) => blocking.every(box => !cuts(points[at], point, box)));
}

function length(points) {
  return points.slice(1).reduce((sum, point, at) => sum + Math.hypot(point.x - points[at].x, point.y - points[at].y), 0);
}

function inside(box, point) {
  return {
    x: Math.min(Math.max(point.x, box.x + 2), box.x + box.width - 2),
    y: Math.min(Math.max(point.y, box.y + 2), box.y + box.height - 2),
  };
}

function exit(box, origin, toward) {
  const dx = toward.x - origin.x;
  const dy = toward.y - origin.y;
  if (Math.abs(dx) < 0.001 && Math.abs(dy) < 0.001) {
    return origin;
  }
  const tx = dx > 0 ? (box.x + box.width - origin.x) / dx : dx < 0 ? (box.x - origin.x) / dx : Infinity;
  const ty = dy > 0 ? (box.y + box.height - origin.y) / dy : dy < 0 ? (box.y - origin.y) / dy : Infinity;
  const t = Math.min(tx, ty);
  return { x: origin.x + dx * t, y: origin.y + dy * t };
}

function route(from, to, boxes, { lane, order }) {
  const blocking = boxes.filter(box => box !== from && box !== to);
  const [a, b] = [middle(from), middle(to)];
  const span = Math.hypot(b.x - a.x, b.y - a.y) || 1;
  const origin = inside(from, { x: a.x - (b.y - a.y) / span * lane, y: a.y + (b.x - a.x) / span * lane });
  const target = inside(to, { x: b.x - (b.y - a.y) / span * lane, y: b.y + (b.x - a.x) / span * lane });
  const straight = [exit(from, origin, target), exit(to, target, origin)];
  if (clear(straight, blocking)) {
    return straight;
  }
  const outward = order * laneShift;
  const downward = b.y >= a.y ? 1 : -1;
  const rightward = b.x >= a.x ? 1 : -1;
  const column = [...blocking.filter(box => box.y < Math.max(a.y, b.y) && box.y + box.height > Math.min(a.y, b.y)), from, to];
  const row = [...blocking.filter(box => box.x < Math.max(a.x, b.x) && box.x + box.width > Math.min(a.x, b.x)), from, to];
  const east = Math.max(...column.map(box => box.x + box.width)) + laneGap + outward;
  const west = Math.min(...column.map(box => box.x)) - laneGap - outward;
  const north = Math.min(...row.map(box => box.y)) - laneGap - outward;
  const south = Math.max(...row.map(box => box.y + box.height)) + laneGap + outward;
  const [ay, by] = [inside(from, { x: a.x, y: a.y - downward * outward / 2 }).y, inside(to, { x: b.x, y: b.y + downward * outward / 2 }).y];
  const [ax, bx] = [inside(from, { x: a.x - rightward * outward / 2, y: a.y }).x, inside(to, { x: b.x + rightward * outward / 2, y: b.y }).x];
  const bends = [];
  if (to.y > from.y + from.height || from.y > to.y + to.height) {
    const down = to.y > from.y;
    const [leave, arrive] = down ? [from.y + from.height, to.y] : [from.y, to.y + to.height];
    const [start, end] = [inside(from, { x: a.x + rightward * outward, y: a.y }).x, inside(to, { x: b.x + rightward * outward, y: b.y }).x];
    for (const y of [(leave + arrive) / 2, arrive + (down ? -laneGap / 2 : laneGap / 2), leave + (down ? laneGap / 2 : -laneGap / 2)]) {
      bends.push([{ x: start, y: leave }, { x: start, y: y - downward * outward }, { x: end, y: y - downward * outward }, { x: end, y: arrive }]);
    }
  }
  if (to.x > from.x + from.width || from.x > to.x + to.width) {
    const right = to.x > from.x;
    const [leave, arrive] = right ? [from.x + from.width, to.x] : [from.x, to.x + to.width];
    const [start, end] = [inside(from, { x: a.x, y: a.y + downward * outward }).y, inside(to, { x: b.x, y: b.y + downward * outward }).y];
    for (const x of [(leave + arrive) / 2, arrive + (right ? -laneGap / 2 : laneGap / 2), leave + (right ? laneGap / 2 : -laneGap / 2)]) {
      bends.push([{ x: leave, y: start }, { x: x - rightward * outward, y: start }, { x: x - rightward * outward, y: end }, { x: arrive, y: end }]);
    }
  }
  const detours = [
    ...bends,
    [{ x: from.x + from.width, y: ay }, { x: east, y: ay }, { x: east, y: by }, { x: to.x + to.width, y: by }],
    [{ x: from.x, y: ay }, { x: west, y: ay }, { x: west, y: by }, { x: to.x, y: by }],
    [{ x: ax, y: from.y }, { x: ax, y: north }, { x: bx, y: north }, { x: bx, y: to.y }],
    [{ x: ax, y: from.y + from.height }, { x: ax, y: south }, { x: bx, y: south }, { x: bx, y: to.y + to.height }],
  ].filter(points => clear(points, blocking));
  return detours.length ? detours.reduce((best, points) => length(points) < length(best) ? points : best) : straight;
}

function spot(points, width, height, taken) {
  const [a, b] = points.slice(1).map((point, at) => [points[at], point])
    .reduce((best, segment) => Math.hypot(segment[1].x - segment[0].x, segment[1].y - segment[0].y) > Math.hypot(best[1].x - best[0].x, best[1].y - best[0].y) ? segment : best);
  const span = Math.hypot(b.x - a.x, b.y - a.y);
  const [nx, ny] = span > 0 ? [-(b.y - a.y) / span, (b.x - a.x) / span] : [1, 0];
  const beside = Math.abs(nx) * width / 2 + Math.abs(ny) * height / 2 + clearance + 2;
  const spots = [0, 1, -1, 2, -2, 3, -3, 4, -4].flatMap(side => along.map(t => ({
    x: a.x + (b.x - a.x) * t + nx * side * beside - width / 2, y: a.y + (b.y - a.y) * t + ny * side * beside - height / 2, width, height,
  })));
  const covered = (one, other) => {
    const across = Math.min(one.x + one.width, other.x + other.width) - Math.max(one.x, other.x) + 2 * clearance;
    const down = Math.min(one.y + one.height, other.y + other.height) - Math.max(one.y, other.y) + 2 * clearance;
    return across > 0 && down > 0 ? across * down : 0;
  };
  const crowding = candidate => taken.reduce((sum, other) => sum + covered(candidate, other), 0);
  return spots.reduce((best, candidate) => crowding(candidate) < crowding(best) ? candidate : best);
}

function lanes(relations) {
  return relations.map(relation => {
    const pair = relations.filter(other => (other.from === relation.from && other.to === relation.to) || (other.from === relation.to && other.to === relation.from));
    const order = pair.indexOf(relation);
    return { lane: (order - (pair.length - 1) / 2) * laneShift * (relation.from <= relation.to ? 1 : -1), order };
  });
}

const styles = `
:host { display: block; margin: 1.25rem 0 1.75rem; color: var(--bs-body-color, #1d1d1f); font: 13px/1.35 system-ui, -apple-system, "Segoe UI", sans-serif; }
.card { border: 1px solid var(--bs-border-color, #d2d2d7); border-radius: 12px; background: var(--bs-tertiary-bg, #f5f5f7); overflow: hidden; }
.head { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; padding: 10px 14px; border-bottom: 1px solid var(--bs-border-color, #d2d2d7); }
.title { font-weight: 600; flex: 1 1 160px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.level { color: var(--bs-secondary-color, #6e6e73); font-size: 12px; }
button { all: unset; cursor: pointer; display: inline-grid; place-items: center; min-width: 28px; height: 28px; padding: 0 6px; border-radius: 7px; color: var(--bs-body-color, #1d1d1f); font-size: 12px; font-variant-numeric: tabular-nums; }
button:hover { background: color-mix(in srgb, currentColor 9%, transparent); }
button:focus-visible { outline: 2px solid var(--bs-primary, #0071e3); outline-offset: 1px; }
button svg { width: 15px; height: 15px; fill: currentColor; }
button:disabled { opacity: .38; cursor: default; background: none; }
.zoom { display: inline-flex; align-items: center; gap: 2px; padding-left: 8px; margin-left: 2px; border-left: 1px solid var(--bs-border-color, #d2d2d7); }
.percent { min-width: 44px; color: var(--bs-secondary-color, #6e6e73); }
.stage { position: relative; overflow: hidden; height: 320px; background: var(--bs-body-bg, #fff); touch-action: pan-y; cursor: grab; }
.stage.zoomed { touch-action: none; }
.stage.dragging { cursor: grabbing; }
.stage:focus-visible { outline: 2px solid var(--bs-primary, #0071e3); outline-offset: -2px; }
svg.diagram { display: block; width: 100%; height: 100%; user-select: none; }
.plate { fill: var(--bs-body-bg, #fff); }
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
  zoomOut: "M3 7.25h10v1.5H3z",
  zoomIn: "M7.25 3h1.5v4.25H13v1.5H8.75V13h-1.5V8.75H3v-1.5h4.25z",
  fit: "M2 2h4.5v1.5h-3v3H2zM9.5 2H14v4.5h-1.5v-3h-3zM2 9.5h1.5v3h3V14H2zM12.5 9.5H14V14H9.5v-1.5h3z",
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
      <button class="speed" aria-label="Speed">1×</button>
      <span class="zoom"><button class="zoom-out" aria-label="Zoom out" title="Zoom out"><svg viewBox="0 0 16 16"><path d="${icons.zoomOut}"/></svg></button>
      <button class="percent" aria-label="Actual size" title="Actual size">100%</button>
      <button class="zoom-in" aria-label="Zoom in" title="Zoom in"><svg viewBox="0 0 16 16"><path d="${icons.zoomIn}"/></svg></button>
      <button class="fit" aria-label="Fit to view" title="Fit to view"><svg viewBox="0 0 16 16"><path d="${icons.fit}"/></svg></button></span></div>
      <div class="progress"><span></span></div>
      <div class="stage" tabindex="0" role="group" aria-label="Diagram. Ctrl or Command with the wheel, a pinch, or the plus and minus keys zoom it; drag or the arrow keys move it; 0 fits it."></div>
      <div class="caption" aria-live="polite"></div></div>`;
    root.querySelector(".title").textContent = this.model.title || "Architecture";
    root.querySelector(".level").textContent = this.model.level === "Dynamic" ? "Flow" : this.model.level;
    root.querySelector(".play").addEventListener("click", () => this.toggle());
    root.querySelector(".previous").addEventListener("click", () => this.go(this.step - 1, true));
    root.querySelector(".next").addEventListener("click", () => this.go(this.step + 1, true));
    root.querySelector(".speed").addEventListener("click", () => {
      this.speed = (this.speed + 1) % speeds.length;
      root.querySelector(".speed").textContent = `${speeds[this.speed]}×`;
    });
    root.querySelector(".zoom-out").addEventListener("click", () => this.zoomBy(1 / zoomStep));
    root.querySelector(".zoom-in").addEventListener("click", () => this.zoomBy(zoomStep));
    root.querySelector(".percent").addEventListener("click", () => this.zoomTo(this.centre(), 1));
    root.querySelector(".fit").addEventListener("click", () => this.fit());
    const stage = root.querySelector(".stage");
    this.draw(stage);
    this.listen(stage);
    this.show();
    this.fit();
  }

  disconnectedCallback() {
    clearTimeout(this.timer);
    this.resizing?.disconnect();
  }

  listen(stage) {
    this.stage = stage;
    this.pointers = new Map();
    stage.addEventListener("wheel", event => {
      if (!(event.ctrlKey || event.metaKey) || !event.deltaY) {
        return;
      }
      event.preventDefault();
      const lines = event.deltaMode === 1 ? 33 : event.deltaMode === 2 ? 400 : 1;
      this.zoomTo(this.pointOf(event), this.scale * Math.pow(wheelStep, -event.deltaY * lines / 100));
    }, { passive: false });
    stage.addEventListener("pointerdown", event => {
      if (event.pointerType === "mouse" && event.button !== 0) {
        return;
      }
      this.pointers.set(event.pointerId, this.pointOf(event));
      this.pinch = this.pointers.size === 2 ? { distance: this.spread(), scale: this.scale } : null;
      this.dragged = false;
    });
    stage.addEventListener("pointermove", event => {
      const last = this.pointers.get(event.pointerId);
      if (!last) {
        return;
      }
      if (event.pointerType === "mouse" && !event.buttons) {
        release(event);
        return;
      }
      if (event.pointerType !== "mouse" && this.fitted && this.pointers.size < 2) {
        return;
      }
      const at = this.pointOf(event);
      this.pointers.set(event.pointerId, at);
      if (this.pinch && this.pointers.size === 2) {
        const [one, other] = [...this.pointers.values()];
        this.zoomTo({ x: (one.x + other.x) / 2, y: (one.y + other.y) / 2 }, this.pinch.scale * this.spread() / this.pinch.distance);
        return;
      }
      if (!this.dragged && Math.hypot(at.x - last.x, at.y - last.y) < 3) {
        this.pointers.set(event.pointerId, last);
        return;
      }
      if (!this.dragged) {
        this.dragged = true;
        if (event.isTrusted) {
          stage.setPointerCapture(event.pointerId);
        }
        stage.classList.add("dragging");
      }
      this.pan(at.x - last.x, at.y - last.y);
    });
    const release = event => {
      this.pointers.delete(event.pointerId);
      this.pinch = null;
      stage.classList.remove("dragging");
    };
    stage.addEventListener("pointerup", release);
    stage.addEventListener("pointercancel", release);
    stage.addEventListener("click", event => {
      if (this.dragged) {
        event.stopPropagation();
        this.dragged = false;
      }
    }, true);
    stage.addEventListener("keydown", event => {
      const moves = { ArrowLeft: [40, 0], ArrowRight: [-40, 0], ArrowUp: [0, 40], ArrowDown: [0, -40] };
      if (event.ctrlKey || event.metaKey || event.altKey) {
        return;
      }
      if (event.key === "+" || event.key === "=") {
        this.zoomBy(zoomStep);
      } else if (event.key === "-") {
        this.zoomBy(1 / zoomStep);
      } else if (event.key === "0") {
        this.fit();
      } else if (event.key === "1") {
        this.zoomTo(this.centre(), 1);
      } else if (moves[event.key] && !this.fitted) {
        this.pan(...moves[event.key]);
      } else {
        return;
      }
      event.preventDefault();
    });
    let width = stage.clientWidth;
    this.resizing = new ResizeObserver(() => requestAnimationFrame(() => {
      if (stage.clientWidth !== width) {
        width = stage.clientWidth;
        if (this.fitted) {
          this.fit();
        } else {
          this.apply();
        }
      }
    }));
    this.resizing.observe(stage);
  }

  pointOf(event) {
    const box = this.stage.getBoundingClientRect();
    return { x: event.clientX - box.left, y: event.clientY - box.top };
  }

  spread() {
    const [one, other] = [...this.pointers.values()];
    return Math.max(1, Math.hypot(one.x - other.x, one.y - other.y));
  }

  centre() {
    return { x: this.stage.clientWidth / 2, y: this.stage.clientHeight / 2 };
  }

  fit() {
    const { width, height } = this.content;
    const room = this.stage.clientWidth || width;
    const tallest = Math.max(320, Math.round(window.innerHeight * 0.7));
    const scale = Math.min(room / width, largestFit, tallest / height);
    this.stage.style.height = `${Math.round(height * scale)}px`;
    this.scale = scale;
    this.floor = Math.min(smallest, scale);
    this.focus = middle(this.content);
    this.fitted = true;
    this.apply();
  }

  zoomBy(factor) {
    this.zoomTo(this.centre(), this.scale * factor);
  }

  zoomTo(point, wanted) {
    const scale = Math.min(Math.max(wanted, Math.min(this.floor, this.scale)), Math.max(largest, this.scale));
    if (Math.abs(scale - this.scale) < 1e-6) {
      return;
    }
    const half = this.centre();
    const under = { x: this.focus.x + (point.x - half.x) / this.scale, y: this.focus.y + (point.y - half.y) / this.scale };
    this.focus = { x: under.x - (point.x - half.x) / scale, y: under.y - (point.y - half.y) / scale };
    this.scale = scale;
    this.fitted = false;
    this.apply();
  }

  pan(dx, dy) {
    this.focus = { x: this.focus.x - dx / this.scale, y: this.focus.y - dy / this.scale };
    this.fitted = false;
    this.apply();
  }

  apply() {
    const half = this.centre();
    const slack = { x: Math.max(0, half.x - kept) / this.scale, y: Math.max(0, half.y - kept) / this.scale };
    const { x, y, width, height } = this.content;
    this.focus = {
      x: Math.min(Math.max(this.focus.x, x - slack.x), x + width + slack.x),
      y: Math.min(Math.max(this.focus.y, y - slack.y), y + height + slack.y),
    };
    const view = { width: (half.x * 2) / this.scale, height: (half.y * 2) / this.scale };
    this.svg.setAttribute("viewBox", `${this.focus.x - view.width / 2} ${this.focus.y - view.height / 2} ${view.width} ${view.height}`);
    const root = this.shadowRoot;
    root.querySelector(".percent").textContent = `${Math.round(this.scale * 100)}%`;
    root.querySelector(".percent").disabled = Math.abs(this.scale - 1) < 0.005;
    root.querySelector(".zoom-in").disabled = this.scale >= largest - 0.005;
    root.querySelector(".zoom-out").disabled = this.scale <= this.floor + 0.005;
    root.querySelector(".fit").disabled = this.fitted;
    this.stage.classList.toggle("zoomed", !this.fitted);
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
    const { positions, frames } = layout(this.model);
    const svg = element("svg", { class: "diagram", role: "img", preserveAspectRatio: "xMidYMid meet" });
    stage.append(svg);
    this.svg = svg;
    element("title", {}, svg).textContent = this.model.title || "C4 diagram";
    const defs = element("defs", {}, svg);
    for (const [id, css] of [["arrow", "arrow"], ["arrow-focus", "arrow focus"]]) {
      const marker = element("marker", { id: `${id}`, viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 7, markerHeight: 7, orient: "auto-start-reverse" }, defs);
      element("path", { d: "M0 0L10 5L0 10z", class: css }, marker);
    }
    this.frames = frames.map(frame => {
      const group = element("g", { class: "frame" }, svg);
      element("rect", { x: frame.x, y: frame.y, width: frame.width, height: frame.height, rx: 14 }, group);
      const caption = element("text", { x: frame.x + 14, y: frame.y + frame.height - 11 }, group);
      caption.textContent = frame.boundary.label;
      if (measure(caption, "600 11.5px") > frame.width - 28) {
        const fits = kept => {
          caption.textContent = `${frame.boundary.label.slice(0, kept).trimEnd()}…`;
          return measure(caption, "600 11.5px") <= frame.width - 28;
        };
        let [low, high] = [0, Math.min(frame.boundary.label.length - 1, 400)];
        while (low < high) {
          const half = Math.ceil((low + high) / 2);
          [low, high] = fits(half) ? [half, high] : [low, half - 1];
        }
        fits(low);
      }
      return { group, frame, label: { x: frame.x + 14, y: frame.y + frame.height - 24, width: measure(caption, "600 11.5px"), height: 16 } };
    });
    const boxes = new Map(this.model.elements.map(item => [item.alias, { ...positions.get(item.alias), width: nodeWidth, height: nodeHeight }]));
    const relationLayer = element("g", {}, svg);
    const nodeLayer = element("g", {}, svg);
    const labelLayer = element("g", {}, svg);
    const taken = [...boxes.values(), ...frames.flatMap(frame => [
      { x: frame.x, y: frame.y, width: frame.width, height: 0 }, { x: frame.x, y: frame.y + frame.height, width: frame.width, height: 0 },
      { x: frame.x, y: frame.y, width: 0, height: frame.height }, { x: frame.x + frame.width, y: frame.y, width: 0, height: frame.height },
    ]), ...this.frames.map(frame => frame.label)];
    const drawn = [...boxes.values(), ...frames];
    const shifts = lanes(this.model.relations);
    this.relations = this.model.relations.map((relation, index) => {
      const from = boxes.get(relation.from);
      const to = boxes.get(relation.to);
      const points = route(from, to, [...boxes.values()], shifts[index]);
      const group = element("g", { class: "relation" }, relationLayer);
      const path = element("path", { d: points.map((point, at) => `${at ? "L" : "M"}${point.x} ${point.y}`).join(""), "marker-end": "url(#arrow)" }, group);
      if (relation.both) {
        path.setAttribute("marker-start", "url(#arrow)");
      }
      const tag = element("g", { class: "relation" }, labelLayer);
      drawn.push(...points.map(point => ({ ...point, width: 0, height: 0 })));
      const label = relation.technology ? `${relation.label} [${relation.technology}]` : relation.label;
      const badged = this.model.level === "Dynamic";
      if (!label && !badged) {
        return { group, tag, path, relation };
      }
      const plate = element("rect", { class: "plate", rx: 9 }, tag);
      const text = element("text", { "text-anchor": "start" }, tag);
      text.textContent = label;
      const textWidth = label ? measure(text, "11.5px") : 0;
      const width = textWidth + (badged ? 22 + (label ? 4 : 0) : 0) + 12;
      const place = spot(points, width, 20, taken);
      taken.push(place);
      drawn.push(place);
      plate.setAttribute("x", place.x);
      plate.setAttribute("y", place.y);
      plate.setAttribute("width", place.width);
      plate.setAttribute("height", place.height);
      text.setAttribute("x", place.x + 6 + (badged ? 26 : 0));
      text.setAttribute("y", place.y + 14);
      if (badged) {
        element("circle", { class: "badge", cx: place.x + 15, cy: place.y + 10, r: 9 }, tag);
        element("text", { class: "index", x: place.x + 15, y: place.y + 13.6, "text-anchor": "middle" }, tag).textContent = index + 1;
      }
      return { group, tag, path, relation };
    });
    if (!drawn.length) {
      drawn.push({ x: 0, y: 0, width: nodeWidth, height: nodeHeight });
    }
    const left = Math.min(...drawn.map(box => box.x)) - framePad;
    const top = Math.min(...drawn.map(box => box.y)) - framePad;
    const right = Math.max(...drawn.map(box => box.x + box.width)) + framePad;
    const bottom = Math.max(...drawn.map(box => box.y + box.height)) + framePad;
    this.content = { x: left, y: top, width: right - left, height: bottom - top };
    this.nodes = new Map(this.model.elements.map(item => {
      const { x, y } = positions.get(item.alias);
      const target = this.closest("article")?.querySelector(`[id="${slug(item.label)}"], [id="${slug(item.alias)}"]`);
      const group = element("g", { class: `node${item.external ? " external" : ""}${target ? " linked" : ""}`, transform: `translate(${x} ${y})` }, nodeLayer);
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
    this.relations.forEach(({ group, tag, path }, index) => {
      for (const part of [group, tag]) {
        part.classList.toggle("hidden", index >= step.relations);
        part.classList.toggle("focus", index === step.hop);
        part.classList.toggle("dim", step.kind === "hop" && index !== step.hop);
      }
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
