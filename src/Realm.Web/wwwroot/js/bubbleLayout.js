// @ts-check
// bubbleLayout.js: the pure placement of the off-screen member bubbles (01 section 4.10, 03 section 4.6). No DOM, no MapLibre: the
// caller passes projected pixel coordinates and rectangles, and Node tests import this file directly.
//
// Members only: a vehicle or a place never gets a bubble, so the caller never passes one. All lengths are CSS pixels, y grows down.

/** @typedef {import('./layoutMath.js').Rect} Rect */
/**
 * One member, projected to container pixels (outside the container is fine). `wasOff` is the previous frame's state of this member
 * (hysteresis; absent on the first frame); `priority` only orders the members inside a cluster, the first one shows its avatar.
 * @typedef {{ id: string, x: number, y: number, wasOff?: boolean, priority?: number }} BubbleAnchor
 */
/** @typedef {{ radius?: number, hysteresis?: number, clusterGap?: number, keepOutPad?: number }} BubbleOptions */
/**
 * `x, y` is the bubble centre. `angleDeg` is the pointer direction, `atan2(dy, dx)` with y down: east 0, south 90, west 180, north -90.
 * `cluster` is the MEMBER COUNT, `ids.length`: 1 for a single bubble, 2 or more for a cluster, never a boolean (O-10). A cluster
 * shows the avatar of `ids[0]` and a count badge equal to `cluster` (no second avatar, no `+N` pill in v1: D35, C-10).
 * @typedef {{ ids: string[], x: number, y: number, angleDeg: number, cluster: number }} Bubble
 */
/**
 * `bubbles` run clockwise from the top-left corner of the inset rectangle (the DOM order of 01 section 10.2). `onScreen` and
 * `offScreen` list the anchor ids in input order; an anchor with a non-finite coordinate is in neither.
 * @typedef {{ bubbles: Bubble[], onScreen: string[], offScreen: string[] }} BubbleResult
 */

/** @typedef {'top' | 'right' | 'bottom' | 'left'} Edge */
/** @typedef {{ x: number, y: number }} Point */
/**
 * @typedef {object} Member
 * @property {BubbleAnchor} anchor
 * @property {number} priority
 * @property {number} centreDist distance from the centre of the rectangle to the projected position
 * @property {Point} want where the ray meets the inset rectangle, before any keep-out slide
 * @property {Point} pos where the member sits alone: `want` after the keep-out slide (the end of the edge when nothing clears)
 * @property {Edge} edge
 * @property {number} angleDeg
 * @property {boolean} cleared false when no coordinate on the edge is clear of the keep-outs
 */
/** @typedef {{ members: Member[], x: number, y: number, edge: Edge, angleDeg: number }} Group */

/** The values of 01 section 4.10: the 40 px avatar's radius, the hysteresis, the merge distance and the keep-out growth. */
export const BUBBLE_DEFAULTS = Object.freeze({ radius: 20, hysteresis: 12, clusterGap: 44, keepOutPad: 8 });

/** The slide direction when the member is exactly in line with the bubble: clockwise (03 section 4.6 item 4). */
const CLOCKWISE = Object.freeze({ top: 1, right: 1, bottom: -1, left: -1 });

const clamp = (/** @type {number} */ v, /** @type {number} */ lo, /** @type {number} */ hi) => Math.min(hi, Math.max(lo, v));
const compareText = (/** @type {string} */ a, /** @type {string} */ b) => (a < b ? -1 : a > b ? 1 : 0);

/**
 * Where a bubble centre may travel: the rectangle inset by the bubble radius (a rectangle too small for that collapses to its midline).
 * @param {Rect} rect
 * @param {number} radius
 * @returns {Rect}
 */
function insetBox(rect, radius) {
  const midX = (rect.left + rect.right) / 2;
  const midY = (rect.top + rect.bottom) / 2;
  return {
    left: Math.min(rect.left + radius, midX),
    right: Math.max(rect.right - radius, midX),
    top: Math.min(rect.top + radius, midY),
    bottom: Math.max(rect.bottom - radius, midY),
  };
}

/**
 * The ray from `centre` through `p` and where it meets the (inset) rectangle. A corner hit belongs to the left or right edge.
 * @param {Point} centre
 * @param {Rect} box
 * @param {Point} p
 * @returns {{ x: number, y: number, edge: Edge, angleDeg: number }}
 */
function rayHit(centre, box, p) {
  const dx = p.x - centre.x;
  let dy = p.y - centre.y;
  if (dx === 0 && dy === 0) dy = -1; // a point on the centre has no direction: say north rather than divide by zero
  const sx = dx > 0 ? (box.right - centre.x) / dx : dx < 0 ? (box.left - centre.x) / dx : Infinity;
  const sy = dy > 0 ? (box.bottom - centre.y) / dy : dy < 0 ? (box.top - centre.y) / dy : Infinity;
  const angleDeg = (Math.atan2(dy, dx) * 180) / Math.PI;
  if (sx <= sy) return { x: dx > 0 ? box.right : box.left, y: clamp(centre.y + dy * sx, box.top, box.bottom), edge: dx > 0 ? 'right' : 'left', angleDeg };
  return { x: clamp(centre.x + dx * sy, box.left, box.right), y: dy > 0 ? box.bottom : box.top, edge: dy > 0 ? 'bottom' : 'top', angleDeg };
}

/**
 * Slides a point along the edge it sits on until its centre is outside every (already grown) keep-out. The first try goes the way the
 * member lies along the edge (`towards`; clockwise when exactly in line); a keep-out in a corner leaves room on one side only, so when
 * that way runs off the edge the other way is tried. Nothing clears: the point goes to the end of the edge in the first direction.
 * @param {{ x: number, y: number, edge: Edge }} hit
 * @param {Point} towards
 * @param {Rect[]} grown
 * @param {Rect} box
 * @returns {{ x: number, y: number, ok: boolean }}
 */
function slide(hit, towards, grown, box) {
  const horizontal = hit.edge === 'top' || hit.edge === 'bottom';
  const lo = horizontal ? box.left : box.top;
  const hi = horizontal ? box.right : box.bottom;
  const start = horizontal ? hit.x : hit.y;
  const lean = horizontal ? towards.x - hit.x : towards.y - hit.y;
  const first = lean > 0 ? 1 : lean < 0 ? -1 : CLOCKWISE[hit.edge];
  const at = (/** @type {number} */ t) => (horizontal ? { x: t, y: hit.y } : { x: hit.x, y: t });
  const blocks = (/** @type {Rect} */ g, /** @type {Point} */ p) => p.x > g.left && p.x < g.right && p.y > g.top && p.y < g.bottom;
  for (const dir of [first, -first]) {
    let t = start;
    // Every move leaves one keep-out behind for good, so there are at most `grown.length` moves.
    for (let pass = 0; pass <= grown.length; pass += 1) {
      const blocker = grown.find((g) => blocks(g, at(t)));
      if (!blocker) return { ...at(t), ok: true };
      t = dir > 0 ? (horizontal ? blocker.right : blocker.bottom) : horizontal ? blocker.left : blocker.top;
      if (t < lo || t > hi) break;
    }
  }
  return { ...at(first > 0 ? hi : lo), ok: false };
}

/**
 * Which anchors are off screen (01 section 4.10 step 1), by the hysteresis rule: an anchor with `wasOff === false` goes off only outside
 * `rect` grown by `hysteresis`; one with `wasOff === true` comes back only inside `rect` shrunk by `hysteresis`; with `wasOff` absent the
 * plain `rect` decides. An anchor with a non-finite coordinate is in neither list. The verdict does not depend on the keep-outs, so a
 * caller that needs to know who has a pin (D89 (1): every on-screen pin is a keep-out) can ask before it builds them.
 * @param {Rect} rect
 * @param {BubbleAnchor[]} anchors
 * @param {{ hysteresis?: number }} [opts]
 * @returns {{ onScreen: string[], offScreen: string[], off: BubbleAnchor[] }} the ids in input order, and the off-screen anchors themselves
 */
export function partitionAnchors(rect, anchors, opts = {}) {
  const hysteresis = opts.hysteresis ?? BUBBLE_DEFAULTS.hysteresis;
  /** @param {BubbleAnchor} a @param {number} margin positive shrinks the rectangle, negative grows it */
  const inside = (a, margin) => a.x >= rect.left + margin && a.x <= rect.right - margin && a.y >= rect.top + margin && a.y <= rect.bottom - margin;
  /** @type {string[]} */
  const onScreen = [];
  /** @type {string[]} */
  const offScreen = [];
  /** @type {BubbleAnchor[]} */
  const off = [];
  for (const a of anchors) {
    if (!Number.isFinite(a.x) || !Number.isFinite(a.y)) continue;
    const isOff = a.wasOff === true ? !inside(a, hysteresis) : a.wasOff === false ? !inside(a, -hysteresis) : !inside(a, 0);
    (isOff ? offScreen : onScreen).push(a.id);
    if (isOff) off.push(a);
  }
  return { onScreen, offScreen, off };
}

/**
 * Places the bubbles of the members that are outside the visible rectangle (01 section 4.10, 03 section 4.6).
 *
 * `rect` is R of 01 section 4.10 step 1 (the caller applies `top = safe-top + 8`, left and right 8, and the panel in Expanded). A bubble
 * centre travels on `rect` inset by `radius`. Each keep-out is grown by `keepOutPad + radius`, so the bubble's own box stays `keepOutPad`
 * clear of the rectangle that was passed: the gear, the attribution, the right stack while visible and, D89 (1), the hit box of every pin
 * that is on screen (a fanned one at its shifted place: see `pinKeepOuts` in testHooks.js) and, D90, the chip of each pin that carries one
 * (the chip is part of its pin's footprint). Pure: nothing is mutated, and the same input in any order gives the same output.
 *
 * 1. Hysteresis: an anchor with `wasOff === false` goes off only outside `rect` grown by `hysteresis`; one with `wasOff === true` comes
 *    back only inside `rect` shrunk by `hysteresis`; with `wasOff` absent the plain `rect` decides.
 * 2. The bubble sits where the ray from the centre of `rect` to the member meets the inset rectangle; `angleDeg` is that ray's angle.
 * 3. A bubble inside a grown keep-out slides along its edge (see `slide`). If no coordinate clears, the member joins the nearest bubble
 *    as a cluster, or, when there is none, sits at the end of the edge.
 * 4. Bubbles whose centres are closer than `clusterGap` merge, transitively, until none is. A cluster is anchored at the mean of its
 *    members' positions snapped back onto the inset rectangle (then slid clear of the keep-outs); its pointer follows that ray.
 *
 * @param {Rect} rect
 * @param {Rect[]} keepOuts
 * @param {BubbleAnchor[]} anchors
 * @param {BubbleOptions} [opts]
 * @returns {BubbleResult}
 */
export function layoutBubbles(rect, keepOuts, anchors, opts = {}) {
  const radius = opts.radius ?? BUBBLE_DEFAULTS.radius;
  const clusterGap = opts.clusterGap ?? BUBBLE_DEFAULTS.clusterGap;
  const growth = (opts.keepOutPad ?? BUBBLE_DEFAULTS.keepOutPad) + radius;

  const centre = { x: (rect.left + rect.right) / 2, y: (rect.top + rect.bottom) / 2 };
  const box = insetBox(rect, radius);
  const grown = keepOuts.map((k) => ({ left: k.left - growth, top: k.top - growth, right: k.right + growth, bottom: k.bottom + growth }));

  const { onScreen, offScreen, off } = partitionAnchors(rect, anchors, opts);

  /** @type {Member[]} */
  const members = [...off]
    .sort((a, b) => compareText(a.id, b.id))
    .map((anchor) => {
      const hit = rayHit(centre, box, anchor);
      const placed = slide(hit, anchor, grown, box);
      return {
        anchor,
        priority: anchor.priority ?? 0,
        centreDist: Math.hypot(anchor.x - centre.x, anchor.y - centre.y),
        want: { x: hit.x, y: hit.y },
        pos: { x: placed.x, y: placed.y },
        edge: hit.edge,
        angleDeg: hit.angleDeg,
        cleared: placed.ok,
      };
    });

  /** @param {Member} m @returns {Group} */
  const alone = (m) => ({ members: [m], x: m.pos.x, y: m.pos.y, edge: m.edge, angleDeg: m.angleDeg });

  /** The mean of the members' positions, snapped back onto the inset rectangle and slid clear of the keep-outs. @param {Member[]} list @returns {Group} */
  const merged = (list) => {
    const mean = { x: list.reduce((s, m) => s + m.pos.x, 0) / list.length, y: list.reduce((s, m) => s + m.pos.y, 0) / list.length };
    const lean = { x: list.reduce((s, m) => s + m.anchor.x, 0) / list.length, y: list.reduce((s, m) => s + m.anchor.y, 0) / list.length };
    const hit = rayHit(centre, box, mean);
    const placed = slide(hit, lean, grown, box);
    return { members: list, x: placed.x, y: placed.y, edge: hit.edge, angleDeg: hit.angleDeg };
  };

  /** @type {Group[]} */
  let groups = members.filter((m) => m.cleared).map(alone);
  // Merge every set of bubbles closer than the gap (transitively). A merge moves the anchor, so look again until none is close.
  for (;;) {
    const parent = groups.map((_, i) => i);
    const find = (/** @type {number} */ i) => {
      while (parent[i] !== i) {
        parent[i] = parent[parent[i]];
        i = parent[i];
      }
      return i;
    };
    for (let i = 0; i < groups.length; i += 1) {
      for (let j = i + 1; j < groups.length; j += 1) {
        if (Math.hypot(groups[i].x - groups[j].x, groups[i].y - groups[j].y) < clusterGap) parent[find(j)] = find(i);
      }
    }
    /** @type {Map<number, Group[]>} */
    const sets = new Map();
    groups.forEach((g, i) => sets.set(find(i), [...(sets.get(find(i)) ?? []), g]));
    if (sets.size === groups.length) break;
    groups = [...sets.values()].map((set) => (set.length === 1 ? set[0] : merged(set.flatMap((g) => g.members))));
  }

  // A member that no coordinate clears joins the nearest bubble (which keeps its place); with no bubble at all it sits at the edge's end.
  for (const m of members.filter((x) => !x.cleared)) {
    if (groups.length === 0) {
      groups.push(alone(m));
      continue;
    }
    const dist = (/** @type {Group} */ g) => Math.hypot(g.x - m.want.x, g.y - m.want.y);
    groups.reduce((best, g) => (dist(g) < dist(best) ? g : best)).members.push(m);
  }

  // Clockwise from the top-left corner of the inset rectangle: the DOM and focus order (01 section 10.2).
  const width = box.right - box.left;
  const height = box.bottom - box.top;
  const along = (/** @type {Group} */ g) =>
    g.edge === 'top' ? g.x - box.left : g.edge === 'right' ? width + (g.y - box.top) : g.edge === 'bottom' ? width + height + (box.right - g.x) : 2 * width + height + (box.bottom - g.y);
  const byPriority = (/** @type {Member} */ a, /** @type {Member} */ b) =>
    b.priority - a.priority || a.centreDist - b.centreDist || compareText(a.anchor.id, b.anchor.id);
  const bubbles = groups
    .map((g) => ({ ...g, members: [...g.members].sort(byPriority) }))
    .sort((a, b) => along(a) - along(b) || compareText(a.members[0].anchor.id, b.members[0].anchor.id))
    .map((g) => ({ ids: g.members.map((m) => m.anchor.id), x: g.x, y: g.y, angleDeg: g.angleDeg, cluster: g.members.length }));

  return { bubbles, onScreen, offScreen };
}
