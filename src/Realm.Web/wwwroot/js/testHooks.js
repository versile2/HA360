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

import { FAN_PRIORITY, fanOut } from './fanout.js';
import { clampChipShift } from './layoutMath.js';

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

/** 01 section 4.2: the hit area of a pin is 56 x 56 centred on its body, never below the body itself (realm-map.css, `.realm-pin::after`). */
export const PIN_HIT_PX = 56;
/** The pointer under a pin's body (realmMap.js `POINTER_PX`): the marker's anchor, the true point, is the bottom of it. */
export const PIN_POINTER_PX = 8;
/** The most pin rectangles one frame hands to the layout (a family and its cars are a handful of pins). A larger input is cut, not walked in full. */
export const MAX_PIN_KEEP_OUTS = 64;

/**
 * The hit area of one pin in container pixels: a square of `max(56, sizePx)` centred on the body, which sits `PIN_POINTER_PX` above the true point.
 * @param {{ x: number, y: number, sizePx: number }} pin `x, y` is the true point (bottom centre of the marker) with the fan-out shift already in `x`
 * @returns {Rect}
 */
export function pinHitRect(pin) {
  const side = Math.max(PIN_HIT_PX, pin.sizePx);
  const centreY = pin.y - PIN_POINTER_PX - pin.sizePx / 2;
  return { left: pin.x - side / 2, top: centreY - side / 2, right: pin.x + side / 2, bottom: centreY + side / 2 };
}

/** 01 section 4.4: the chip is 36 px high and sits 8 px above the pin body, or 8 px below the pointer when flipped (realmMap.js `CHIP_HEIGHT_PX` and `CHIP_GAP_PX`, realm-map.css `.realm-chip`). */
export const CHIP_HEIGHT_PX = 36;
export const CHIP_GAP_PX = 8;

/**
 * D90: the rectangle of a pin's chip in container pixels, worked out the way `placeChips` and `clampChip` of realmMap.js place it, from numbers alone so that the
 * layout of the bubbles can know it BEFORE the frame draws it (no reading of the chip's box after the fact, so no frame in which a bubble sits on a chip). The chip is
 * centred on the pin and shifted sideways by `clampChipShift` inside `frame.room` (D75, D79); it flips below the pointer when its top would be above the map padding's
 * top (01 section 4.4). The caret lies in the 8 px gap between the chip and the pin, so it adds nothing to the rectangle.
 * @param {{ x: number, y: number, sizePx: number }} pin `x` is the DRAWN centre (fan-out shift included), `y` the true point (the bottom of the pointer)
 * @param {number} widthPx the chip's width as measured (`offsetWidth`)
 * @param {{ paddingTop: number, room: { left: number, right: number } }} frame the map padding's top edge, and where a chip may be (`chipRoom` of layoutMath.js)
 * @returns {Rect}
 */
export function chipRect(pin, widthPx, frame) {
  const bodyTop = pin.y - (pin.sizePx + PIN_POINTER_PX);
  const flipped = bodyTop - CHIP_GAP_PX - CHIP_HEIGHT_PX < frame.paddingTop; // the rule of placeChips, on the true point
  const drawnY = Math.round(pin.y); // the marker is drawn at whole pixels, the chip with it
  const top = flipped ? drawnY + CHIP_GAP_PX : drawnY - (pin.sizePx + PIN_POINTER_PX) - CHIP_GAP_PX - CHIP_HEIGHT_PX;
  const centre = pin.x + Math.round(clampChipShift(pin.x, widthPx, frame.room) * 2) / 2; // clampChip rounds the shift to half a pixel
  return { left: centre - widthPx / 2, top, right: centre + widthPx / 2, bottom: top + CHIP_HEIGHT_PX };
}

/**
 * D89 (1): every pin that is on screen is a keep-out for the edge bubbles, a fanned one where the fan-out puts it (01 section 4.8: the shift is applied here with the
 * same `fanItems` and `fanOut` as the frame's own fan-out, from the TRUE anchors, so the rectangle is the one the pin will occupy). Members and vehicles alike; the
 * caller lists only the pins that exist in this frame (a member that has a bubble has no pin). The result goes to `layoutBubbles` with the other keep-outs, which grows
 * each by the same 8 px and slides a bubble along its edge until it is clear. Nothing here reads the DOM: the rectangles are arithmetic on projected points, rounded to the
 * whole pixels the markers are drawn at.
 *
 * D90: the chip of a pin is part of that pin's footprint. A pin that carries one (`chipWidthPx` above 0, the width the caller measured in its read phase) contributes a second
 * rectangle, right after its own, when `opts.chip` says where chips may be (see `chipRect`); a bubble then keeps 8 px clear of the chip as it does of the pin. Each rectangle is
 * tested against the container on its own: a pin just below the container can have a chip that is inside it.
 * @param {ReadonlyArray<{ kind: 'member' | 'vehicle', id: string, x: number, y: number, driving: boolean, sizePx: number, chipWidthPx?: number }>} pins true points in container pixels
 * @param {string | null} selectedKey `kind:id` of the selected pin, or null (the selected pin keeps its place in a fan)
 * @param {Rect} view the container: a rectangle that is entirely outside it is not on screen and is left out
 * @param {{ fan?: boolean, chip?: { paddingTop: number, room: { left: number, right: number } } }} [opts] `fan: false` when the fan-out is switched off (the pins then stay on their true points); `chip` switches the chip rectangles on
 * @returns {Rect[]} at most `MAX_PIN_KEEP_OUTS` pins' rectangles, a pin and then its chip, in the order of `pins` (so at most twice that many)
 */
export function pinKeepOuts(pins, selectedKey, view, opts = {}) {
  const bounded = pins.slice(0, MAX_PIN_KEEP_OUTS);
  /** @type {Map<string, number>} */
  const shifts = new Map();
  if (opts.fan !== false) for (const result of fanOut(fanItems(bounded, selectedKey))) shifts.set(result.id, result.dx);
  /** @param {Rect} rect @returns {boolean} */
  const onScreen = (rect) => !(rect.right <= view.left || rect.left >= view.right || rect.bottom <= view.top || rect.top >= view.bottom);
  /** @type {Rect[]} */
  const rects = [];
  for (const pin of bounded) {
    if (!Number.isFinite(pin.x) || !Number.isFinite(pin.y)) continue;
    // MapLibre puts a marker at whole pixels once the camera is at rest (it rounds `project(point) + offset`), so the rectangle is the one of the DRAWN pin, not of the fractional point.
    const drawnX = Math.round(pin.x + (shifts.get(`${pin.kind}:${pin.id}`) ?? 0));
    const rect = pinHitRect({ x: drawnX, y: Math.round(pin.y), sizePx: pin.sizePx });
    if (onScreen(rect)) rects.push(rect);
    if (opts.chip && typeof pin.chipWidthPx === 'number' && pin.chipWidthPx > 0) {
      const chip = chipRect({ x: drawnX, y: pin.y, sizePx: pin.sizePx }, pin.chipWidthPx, opts.chip);
      if (onScreen(chip)) rects.push(chip);
    }
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

/**
 * How far outside R a member may sit and still count as inside on the first verdict after a camera command: the default fit puts the outermost member exactly on
 * the edge of the map padding, which is the bottom edge of R itself, and a fraction of a pixel of floating point must not decide whether that member has a pin.
 */
export const BUBBLE_RESEED_TOLERANCE_PX = 1;

/**
 * The anchors of the first frame after a camera command (a fit, a flight, a recentre, a resize; not a person's own pan or zoom). The 12 px hysteresis of
 * `layoutBubbles` is memory of the last frame, and a command does not move the camera gradually: `fitBounds` puts the outermost pins on the edge of the map padding,
 * which is 8 px inside R on the left (padding 16, R 8) and not at all inside it on the bottom, so a member that was off screen before the command lands in the
 * 12 px band, stays a bubble for good and the default view loses a pin. The verdict of that frame is the plain one instead: each anchor's `wasOff` becomes "outside R
 * by more than `tolerance`", and from the next frame the hysteresis memory is the normal one.
 * @template {{ id: string, x: number, y: number, wasOff?: boolean, priority: number }} A
 * @param {ReadonlyArray<A>} anchors
 * @param {Rect} rect R, as `bubbleRect` gives it
 * @param {number} [tolerance]
 * @returns {Array<A & { wasOff: boolean }>} the same anchors, in the same order, with `wasOff` set
 */
export function reseedAnchors(anchors, rect, tolerance = BUBBLE_RESEED_TOLERANCE_PX) {
  return anchors.map((anchor) => {
    const outside =
      anchor.x < rect.left - tolerance || anchor.x > rect.right + tolerance || anchor.y < rect.top - tolerance || anchor.y > rect.bottom + tolerance;
    return { ...anchor, wasOff: outside };
  });
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
