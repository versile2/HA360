// @ts-check
// testHooks.js: installs `window.__realm` (03 section 4.10, 01 Appendix B) when init asked for test hooks (a Demo session, or
// Realm:TestHooks). The hooks are read-only observers and pure functions: none of them changes application state, and nothing
// outside the ten names below is ever installed (O-10).
//
// realmMap.js owns the state, so it passes the observers in as `probe`; this file only decides which names exist and freezes them.
// S5a provides `mapPadding`, `camera`, `pins`, `zones`, `styleId`, `settled` and `stats`; `sheet` arrives with S7a, `bubbles` and
// `layoutBubbles` with S9, by adding them to the probe (realmMap.js) and leaving this file untouched.

/** The whole v1 surface of `window.__realm`. */
export const HOOK_NAMES = Object.freeze(['mapPadding', 'camera', 'sheet', 'pins', 'bubbles', 'zones', 'styleId', 'layoutBubbles', 'settled', 'stats']);

/**
 * Installs the hooks the probe provides and returns them.
 * @param {Window} win
 * @param {Record<string, unknown>} probe the observers, by name
 * @returns {Readonly<Record<string, unknown>>}
 */
export function installTestHooks(win, probe) {
  /** @type {Record<string, unknown>} */
  const hooks = {};
  for (const name of HOOK_NAMES) {
    if (typeof probe[name] === 'function') hooks[name] = probe[name];
  }
  const frozen = Object.freeze(hooks);
  Object.defineProperty(win, '__realm', { value: frozen, configurable: true, enumerable: true, writable: false });
  return frozen;
}

/**
 * Removes the hooks (dispose, and a repeated init that does not ask for them).
 * @param {Window} win
 */
export function removeTestHooks(win) {
  try {
    delete (/** @type {any} */ (win)).__realm;
  } catch {
    // a non-configurable property cannot be deleted; a later install replaces nothing and the stale hooks simply stay
  }
}

// ---- S9b: the pure helpers of the edge bubbles and the fan-out (03 sections 4.6 and 4.10, 01 sections 4.8 and 4.10) ------------------------------------
// realmMap.js owns the DOM and the camera; what can be decided from numbers and strings alone lives here, so that Node tests import it (S9 adds no module of
// its own). `describeBubbles` is the content of the `bubbles` hook. Nothing here reads the DOM or MapLibre.

import { FAN_PRIORITY } from './fanout.js';

/** @typedef {import('./layoutMath.js').Rect} Rect */
/** @typedef {{ left: number, top: number, right: number, bottom: number }} Edges */

/** 01 section 4.10 step 1: R keeps this distance from the viewport edge on the top, right and left (the bubble centre then travels 20 px further in). */
export const BUBBLE_EDGE_PX = 8;
/** The hit area of a bubble, a 48 x 48 button centred on the avatar (01 section 4.10). */
export const BUBBLE_HIT_PX = 48;
/** The cluster fit of a bubble tap (01 section 4.10 step 6, 03 section 4.3): `maxZoom` 15 over 700 ms. */
export const CLUSTER_FIT_MAX_ZOOM = 15;
export const CLUSTER_FIT_MS = 700;
/** 01 section 4.10 step 8: a bubble fades and scales in or out over 150 ms and slides between positions over 120 ms (realm-map.css holds the same numbers). */
export const BUBBLE_FADE_MS = 150;
export const BUBBLE_SLIDE_MS = 120;

/**
 * R of 01 section 4.10 step 1: the container minus the map padding, except top = safe-top + 8, right = 8 and left = 8; in Expanded with the panel shown the
 * left edge is the panel's right edge plus 8 (the panel's left margin and width, and the 8 px gap). Only the bottom follows the measured padding (the sheet).
 * @param {{ width: number, height: number }} viewport
 * @param {{ mode: string, panelHidden: boolean, panelLeftPx: number, panelWidthPx: number, safe: { top: number } }} layout
 * @param {{ bottom: number }} padding the map padding in force
 * @returns {Rect}
 */
export function bubbleRect(viewport, layout, padding) {
  const panel = layout.mode === 'expanded' && !layout.panelHidden;
  return {
    left: panel ? layout.panelLeftPx + layout.panelWidthPx + BUBBLE_EDGE_PX : BUBBLE_EDGE_PX,
    top: layout.safe.top + BUBBLE_EDGE_PX,
    right: viewport.width - BUBBLE_EDGE_PX,
    bottom: viewport.height - padding.bottom,
  };
}

/**
 * The keep-out rectangles in container pixels, from the elements' boxes in the page: `null` boxes (an element that is not there, or is hidden, as the right stack
 * is at Tall) and empty ones are left out.
 * @param {ReadonlyArray<{ left: number, top: number, right: number, bottom: number } | null>} boxes
 * @param {{ left: number, top: number }} origin the container's own box in the page
 * @returns {Rect[]}
 */
export function keepOutRects(boxes, origin) {
  /** @type {Rect[]} */
  const rects = [];
  for (const box of boxes) {
    if (!box || !(box.right > box.left) || !(box.bottom > box.top)) continue;
    rects.push({ left: box.left - origin.left, top: box.top - origin.top, right: box.right - origin.left, bottom: box.bottom - origin.top });
  }
  return rects;
}

/**
 * The anchors `layoutBubbles` takes, one per member that has a position. `project` returns the member's position in container pixels (the displayed one while a pin
 * glides) or null when it has none; `previous` is last frame's verdict by id (true: off screen), which is the hysteresis memory: an id that was not seen before
 * has no `wasOff`. Priority orders the members of a cluster and is the one of the fan-out: selected, then driving, then the others.
 * @param {ReadonlyArray<{ id: string, status: string }>} members
 * @param {(member: any) => { x: number, y: number } | null} project
 * @param {ReadonlyMap<string, boolean>} previous
 * @param {string | null} selectedId the selected member
 * @returns {Array<{ id: string, x: number, y: number, wasOff?: boolean, priority: number }>}
 */
export function bubbleAnchors(members, project, previous, selectedId) {
  const anchors = [];
  for (const member of members) {
    const at = project(member);
    if (!at) continue;
    const priority = member.id === selectedId ? FAN_PRIORITY.selected : member.status === 'driving' ? FAN_PRIORITY.drivingMember : FAN_PRIORITY.member;
    const wasOff = previous.get(member.id);
    anchors.push(wasOff === undefined ? { id: member.id, x: at.x, y: at.y, priority } : { id: member.id, x: at.x, y: at.y, wasOff, priority });
  }
  return anchors;
}

/** The key of a bubble and the suffix of its test id: the member id, or `id1-id2` for a cluster (03 section 4.6). @param {ReadonlyArray<string>} ids @returns {string} */
export function bubbleKey(ids) {
  return ids.join('-');
}

/**
 * The content of the `bubbles` hook (01 Appendix B, 03 section 4.10): `id` is the test-id suffix, `ids` the members, `cluster` the MEMBER COUNT (1 for a single
 * bubble, never a boolean, O-10).
 * @param {ReadonlyArray<{ ids: string[], x: number, y: number, angleDeg: number, cluster: number }>} bubbles
 * @returns {Array<{ id: string, ids: string[], x: number, y: number, angleDeg: number, cluster: number }>}
 */
export function describeBubbles(bubbles) {
  return bubbles.map((bubble) => ({ id: bubbleKey(bubble.ids), ids: [...bubble.ids], x: bubble.x, y: bubble.y, angleDeg: bubble.angleDeg, cluster: bubble.cluster }));
}

/**
 * Fills `{name}` placeholders from `values`; a placeholder without a value stays as it is.
 * @param {string} template
 * @param {Record<string, string | number>} values
 * @returns {string}
 */
export function fillTemplate(template, values) {
  return template.replace(/\{(\w+)\}/g, (whole, name) => (Object.hasOwn(values, name) ? String(values[name]) : whole));
}

/**
 * The accessible name and the tooltip of a bubble. A single member's come from C# with the payload; a cluster's are the two templates of `strings` with the count
 * and the names in the order of the bubble (01 section 4.10).
 * @param {ReadonlyArray<string>} ids
 * @param {ReadonlyMap<string, { name: string, bubbleLabel: string, bubbleTooltip: string }>} byId
 * @param {{ clusterName?: string, clusterTooltip?: string } | undefined} strings
 * @returns {{ label: string, tooltip: string }}
 */
export function bubbleTexts(ids, byId, strings) {
  if (ids.length === 1) {
    const only = byId.get(ids[0]);
    return { label: only?.bubbleLabel ?? '', tooltip: only?.bubbleTooltip ?? '' };
  }
  const names = ids.map((id) => byId.get(id)?.name ?? id).join(', ');
  return {
    label: strings?.clusterName ? fillTemplate(strings.clusterName, { n: ids.length, names }) : names,
    tooltip: strings?.clusterTooltip ? fillTemplate(strings.clusterTooltip, { names }) : names,
  };
}

/**
 * Whether a tap reached the viewer's own bubble, which behaves as Recenter and is never reported as a selection (01 section 4.10 step 7).
 * @param {ReadonlyArray<string>} ids
 * @param {string} meId the viewer's member id; empty when there is none
 * @returns {boolean}
 */
export function isOwnBubble(ids, meId) {
  return meId !== '' && ids.length === 1 && ids[0] === meId;
}

/**
 * The points the cluster fit frames (01 section 4.10 step 6): the viewer's member, if it has a position, plus every tapped member that has one.
 * @param {ReadonlyArray<{ id: string, lat: number | null, lon: number | null }>} members
 * @param {string} meId
 * @param {ReadonlyArray<string>} ids the members of the tapped cluster
 * @returns {Array<[number, number]>} `[lon, lat]`
 */
export function clusterFitPoints(members, meId, ids) {
  const wanted = new Set(ids);
  if (meId !== '') wanted.add(meId);
  /** @type {Array<[number, number]>} */
  const points = [];
  for (const member of members) {
    if (wanted.has(member.id) && typeof member.lat === 'number' && typeof member.lon === 'number') points.push([member.lon, member.lat]);
  }
  return points;
}

/**
 * The items `fanOut` takes, one per pin, in container pixels (the TRUE anchors: the shift is applied afterwards). Priority (01 section 4.8): the selected pin,
 * then members with driving first, then vehicles. The id is `kind:id`, the key of the runtime's pin map.
 * @param {ReadonlyArray<{ kind: 'member' | 'vehicle', id: string, x: number, y: number, driving: boolean }>} pins
 * @param {string | null} selectedKey `kind:id` of the selected pin, or null
 * @returns {Array<{ id: string, x: number, y: number, priority: number }>}
 */
export function fanItems(pins, selectedKey) {
  return pins.map((pin) => {
    const id = `${pin.kind}:${pin.id}`;
    const priority = id === selectedKey ? FAN_PRIORITY.selected : pin.kind === 'vehicle' ? FAN_PRIORITY.vehicle : pin.driving ? FAN_PRIORITY.drivingMember : FAN_PRIORITY.member;
    return { id, x: pin.x, y: pin.y, priority };
  });
}
