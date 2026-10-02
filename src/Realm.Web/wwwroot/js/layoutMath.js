// @ts-check
// layoutMath.js: the pure layout arithmetic of the map (03 section 4.1, 01 sections 3.4.3 and 4.11). No DOM, no MapLibre.
// All lengths are CSS pixels, all coordinates [lon, lat].
//
// S5a owns the region below. A later slice adds its exported functions in one commented region at the end of the file and leaves
// the code above untouched (04 section 1.8): S8a adds `selectionPadding` there.

import { EARTH_RADIUS_M, TILE_SIZE_PX, metersPerPixel } from './geo.js';

/** @typedef {import('./geo.js').LngLat} LngLat */
/** @typedef {{ top: number, right: number, bottom: number, left: number }} Padding */
/** @typedef {{ left: number, top: number, right: number, bottom: number }} Rect */
/** @typedef {{ width: number, height: number }} Viewport */
/**
 * 03 section 4.5.
 * @typedef {{ mode: 'compact' | 'expanded', panelLeftPx: number, panelWidthPx: number, panelHidden: boolean,
 *             stackVisible: boolean, safe: Padding, navHeightPx: number }} LayoutPayload
 */
/** @typedef {{ center: LngLat, zoom: number }} CameraPose */

/** `safe-top + 72` clears the settings gear (01 section 3.4.3). */
export const TOP_CLEARANCE_PX = 72;
/** 12 + 48 + 12: the right-hand button stack while it is visible. */
export const STACK_CLEARANCE_PX = 72;
/** The plain gutter of the map padding. */
export const EDGE_PX = 16;
/** Less visible map than this and the bottom padding is reduced (01 section 3.4.3). */
export const MIN_VISIBLE_HEIGHT_PX = 48;
/** A pin body extends 56 px above its anchor (48 circle + 8 pointer); `fitBounds` adds it to the top padding. */
export const PIN_BODY_ALLOWANCE_PX = 56;
/** Peek is `max(19 % of the viewport height, NAV_H + 104)` (01 section 3.2). */
export const PEEK_FRACTION = 0.19;
export const PEEK_CONTENT_PX = 104;
/** "At the default view": within 40 m of the centre and 0.3 zoom (01 section 4.11). */
export const AT_DEFAULT_METERS = 40;
export const AT_DEFAULT_ZOOM = 0.3;

/** @type {Readonly<LayoutPayload>} What the map assumes until the first `setLayout`: Compact with the sheet at Peek and no inset. */
export const DEFAULT_LAYOUT = Object.freeze({
  mode: 'compact',
  panelLeftPx: EDGE_PX,
  panelWidthPx: 0,
  panelHidden: false,
  stackVisible: true,
  safe: Object.freeze({ top: 0, right: 0, bottom: 0, left: 0 }),
  navHeightPx: 64,
});

const rad = (/** @type {number} */ degrees) => (degrees * Math.PI) / 180;

/**
 * Great-circle distance in metres between two [lon, lat] points.
 * @param {LngLat} a
 * @param {LngLat} b
 * @returns {number}
 */
export function haversine(a, b) {
  const dPhi = rad(b[1] - a[1]);
  const dLambda = rad(b[0] - a[0]);
  const h = Math.sin(dPhi / 2) ** 2 + Math.cos(rad(a[1])) * Math.cos(rad(b[1])) * Math.sin(dLambda / 2) ** 2;
  return 2 * EARTH_RADIUS_M * Math.asin(Math.min(1, Math.sqrt(h)));
}

/**
 * Rounds a zoom to a step (0.05 by default) so a continuous gesture does not rebuild geometry on every frame.
 * @param {number} zoom
 * @param {number} [step]
 * @returns {number}
 */
export function quantizeZoom(zoom, step = 0.05) {
  return Number((Math.round(zoom / step) * step).toFixed(10));
}

/**
 * The bottom navigation height including the bottom safe-area inset.
 * @param {LayoutPayload} layout
 * @returns {number}
 */
export function navTotalPx(layout) {
  return layout.navHeightPx + layout.safe.bottom;
}

/**
 * The height of the sheet at Peek: `max(round(19 % of the viewport height), navTotal + 104)`, so 174 px at 412 x 915 and the
 * 168 px floor at 412 x 800 (01 section 3.2).
 * @param {number} viewportHeight
 * @param {number} navTotal the navigation bar with its safe inset
 * @returns {number}
 */
export function peekSheetHeight(viewportHeight, navTotal) {
  return Math.max(Math.round(PEEK_FRACTION * viewportHeight), navTotal + PEEK_CONTENT_PX);
}

/**
 * The map padding of 01 section 3.4.3.
 *
 * Compact: top `safe-top + 72`; right 72 while the right stack is visible, else 16 (both plus the right inset); bottom is the
 * measured sheet height plus 16 (the sheet includes the navigation), 190 at Peek and 748 at 80 % on 412 x 915; left `16 + safe-left`.
 * Expanded: top as above, right 72, bottom `NAV_H + 16`, left `panel left + panel width + 16` (432), or `16 + safe-left` with the
 * panel hidden. When top and bottom would leave less than 48 px of visible height the bottom is reduced so that 48 px remain.
 * @param {Viewport} viewport
 * @param {LayoutPayload} layout
 * @param {number | null} [sheetHeightPx] the measured sheet height; null (or not finite) means the Peek height of this viewport
 * @returns {Padding}
 */
export function computePadding(viewport, layout, sheetHeightPx = null) {
  const { safe } = layout;
  const navTotal = navTotalPx(layout);
  const top = safe.top + TOP_CLEARANCE_PX;
  /** @type {number} */
  let right;
  /** @type {number} */
  let bottom;
  /** @type {number} */
  let left;
  if (layout.mode === 'expanded') {
    right = STACK_CLEARANCE_PX + safe.right;
    bottom = navTotal + EDGE_PX;
    left = layout.panelHidden ? EDGE_PX + safe.left : layout.panelLeftPx + layout.panelWidthPx + EDGE_PX;
  } else {
    right = (layout.stackVisible ? STACK_CLEARANCE_PX : EDGE_PX) + safe.right;
    const sheet = sheetHeightPx !== null && Number.isFinite(sheetHeightPx) ? sheetHeightPx : peekSheetHeight(viewport.height, navTotal);
    bottom = sheet + EDGE_PX;
    left = EDGE_PX + safe.left;
  }
  bottom = Math.min(bottom, Math.max(0, viewport.height - top - MIN_VISIBLE_HEIGHT_PX));
  return { top, right, bottom, left };
}

/**
 * The part of the container that the padding leaves visible.
 * @param {Viewport} viewport
 * @param {Padding} padding
 * @returns {Rect}
 */
export function visibleRect(viewport, padding) {
  return { left: padding.left, top: padding.top, right: viewport.width - padding.right, bottom: viewport.height - padding.bottom };
}

/**
 * @param {Rect} rect
 * @returns {{ x: number, y: number }}
 */
export function rectCenter(rect) {
  return { x: (rect.left + rect.right) / 2, y: (rect.top + rect.bottom) / 2 };
}

/**
 * Whether the camera sits at a target view: within 40 m of its centre and 0.3 zoom (01 section 4.11). False without a target.
 * @param {CameraPose} camera
 * @param {CameraPose | null | undefined} target
 * @param {{ meters?: number, zoom?: number }} [tolerance]
 * @returns {boolean}
 */
export function isAtDefault(camera, target, tolerance = {}) {
  if (!target) return false;
  const meters = tolerance.meters ?? AT_DEFAULT_METERS;
  const zoom = tolerance.zoom ?? AT_DEFAULT_ZOOM;
  return haversine(camera.center, target.center) <= meters && Math.abs(camera.zoom - target.zoom) <= zoom;
}

// ---- S8a: selection flights, the refit on a layout change, the chip clamp (D45, D75; 03 section 4.3, 01 sections 4.4, 4.11 and 4.13) -----------------------------
// Pure arithmetic only: realmMap.js owns every MapLibre call and passes these functions what it measured.

/** A selection flight zooms in to at least this (01 section 4.13); a higher current zoom is kept. */
export const SELECTION_MIN_ZOOM = 15;
/** A far member that is off screen is flown to at this zoom (01 section 4.13). */
export const FAR_FLIGHT_ZOOM = 13;
/** The ease of a selection flight, and of `fitPlace`. */
export const SELECTION_EASE_MS = 600;
/** The `flyTo` of a far, off-screen member. */
export const FAR_FLIGHT_MS = 900;
/** The sheet height must be stable this long before the selection is re-centred (03 section 4.3). */
export const RECENTER_SETTLE_MS = 120;
/** The ease of that re-centre. */
export const RECENTER_EASE_MS = 250;
/** No re-centre when the selection is already this close to the centre of the settled rectangle. */
export const RECENTER_SKIP_PX = 2;
/** `fitPlace` fits the zone circle grown by this much (01 section 4.13). */
export const PLACE_FIT_GROWTH = 0.2;
/** `fitPlace` never zooms in past this (the `fitBounds` rule of 03 section 4.3). */
export const PLACE_FIT_MAX_ZOOM = 16;
/** A pin chip keeps this far from the left and right edge of the map (D75). */
export const CHIP_EDGE_PX = 8;
/** The caret of a pin chip stays this far from either end of the chip: the radius of its rounded body (D79). */
export const CHIP_CARET_INSET_PX = 18;

const deg = (/** @type {number} */ radians) => (radians * 180) / Math.PI;

/**
 * The padding that a selection flight aims at (D45; 03 section 4.3): in Compact the padding of the sheet at Peek with the right stack visible,
 * `{ safe-top + 72, 72, peekHeight + 16, 16 + safe-left }` and so `{72, 72, 190, 16}` at 412 x 915, whatever the sheet measures right now (a row tapped at
 * Tall starts the flight while the measured sheet is still Tall). In Expanded the panel padding, which is the measured one. The 48 px minimum visible height
 * of 01 section 3.4.3 applies as in {@link computePadding}.
 * @param {Viewport} viewport
 * @param {LayoutPayload} layout
 * @returns {Padding}
 */
export function selectionPadding(viewport, layout) {
  const target = layout.mode === 'expanded' ? layout : { ...layout, stackVisible: true };
  return computePadding(viewport, target, null);
}

/**
 * How a member flight goes (03 section 4.3, 01 section 4.13): a far member who is off screen gets a `flyTo` to zoom 13 over 900 ms; everyone else an `easeTo`
 * to `max(current zoom, minZoom ?? 15)` over 600 ms. Under reduced motion every duration is 0.
 * @param {{ far?: boolean, onScreen: boolean, currentZoom: number, minZoom?: number | null, reducedMotion?: boolean }} input
 * @returns {{ kind: 'ease' | 'fly', zoom: number, durationMs: number }}
 */
export function selectionFlightPlan(input) {
  const far = input.far === true && !input.onScreen;
  const zoom = far ? FAR_FLIGHT_ZOOM : Math.max(input.currentZoom, input.minZoom ?? SELECTION_MIN_ZOOM);
  const durationMs = input.reducedMotion === true ? 0 : far ? FAR_FLIGHT_MS : SELECTION_EASE_MS;
  return { kind: far ? 'fly' : 'ease', zoom, durationMs };
}

/**
 * The MapLibre zoom at which a circle of this diameter, centred at this latitude, is `sidePx` CSS pixels across (Web Mercator, 512 px world). Infinity for a
 * circle without a diameter.
 * @param {number} diameterM
 * @param {number} latDeg
 * @param {number} sidePx
 * @returns {number}
 */
export function zoomToFit(diameterM, latDeg, sidePx) {
  if (!(diameterM > 0) || !(sidePx > 0)) return Number.POSITIVE_INFINITY;
  return Math.log2(metersPerPixel(0, latDeg) / (diameterM / sidePx));
}

/** @param {number} lat @returns {number} the Web Mercator y of a latitude, 0 at the north edge of the world and 1 at the south edge */
const mercatorY = (lat) => 0.5 - Math.log(Math.tan(Math.PI / 4 + rad(lat) / 2)) / (2 * Math.PI);
/** @param {number} y @returns {number} */
const latitudeOfMercatorY = (y) => deg(2 * Math.atan(Math.exp((0.5 - y) * 2 * Math.PI)) - Math.PI / 2);

/**
 * The camera that fits a zone circle, grown by `growth`, into a visible rectangle (`fitPlace`; 01 section 4.13): the largest zoom at which the grown circle fits
 * the rectangle less `topAllowancePx` at the top (the pin body, 56 px; 03 section 4.3), at most `maxZoom`. The camera centre is the middle of the whole
 * rectangle once the padding is applied, so it is placed `topAllowancePx / 2` above the circle's centre: the circle then sits in the part below the allowance.
 * @param {import('./geo.js').LngLat} center the circle centre
 * @param {number} radiusM
 * @param {Rect} rect the visible rectangle of the target padding
 * @param {{ growth?: number, topAllowancePx?: number, minZoom?: number, maxZoom?: number }} [options]
 * @returns {CameraPose}
 */
export function fitCircle(center, radiusM, rect, options = {}) {
  const growth = options.growth ?? PLACE_FIT_GROWTH;
  const allowance = options.topAllowancePx ?? PIN_BODY_ALLOWANCE_PX;
  const maxZoom = options.maxZoom ?? PLACE_FIT_MAX_ZOOM;
  const minZoom = options.minZoom ?? 0;
  const side = Math.max(1, Math.min(rect.right - rect.left, rect.bottom - rect.top - allowance));
  const zoom = Math.min(maxZoom, Math.max(minZoom, zoomToFit(2 * Math.max(0, radiusM) * (1 + growth), center[1], side)));
  const y = mercatorY(center[1]) - allowance / 2 / (TILE_SIZE_PX * 2 ** zoom);
  return { center: [center[0], latitudeOfMercatorY(y)], zoom };
}

/**
 * The part of a layout that decides where the default view fits (D75): the mode and the room the side panel takes. The sheet height, the right stack and the
 * safe insets are not part of it, so a sheet that opens or closes never refits the camera.
 * @param {LayoutPayload} layout
 * @returns {{ mode: 'compact' | 'expanded', panelWidthPx: number }} `panelWidthPx` is 0 unless the panel is shown (Expanded and not hidden), else its left gutter plus its width
 */
export function layoutFootprint(layout) {
  const panel = layout.mode === 'expanded' && !layout.panelHidden;
  return { mode: layout.mode, panelWidthPx: panel ? layout.panelLeftPx + layout.panelWidthPx : 0 };
}

/**
 * Whether the default view has to be fitted again (D75): the camera is at the default view and the layout switched between Compact and Expanded, or the side
 * panel appeared, disappeared or changed width, so the old fit would leave pins under the panel or a gap where it was.
 * @param {LayoutPayload | null | undefined} previous the layout the camera was last fitted for; none means nothing to compare with
 * @param {LayoutPayload} next
 * @param {boolean} atDefault the camera sits at the default view (it was fitted and nothing moved it away since)
 * @returns {boolean}
 */
export function refitNeeded(previous, next, atDefault) {
  if (!atDefault || !previous) return false;
  const a = layoutFootprint(previous);
  const b = layoutFootprint(next);
  return a.mode !== b.mode || a.panelWidthPx !== b.panelWidthPx;
}

/**
 * Where recentre goes next (01 section 4.11): away to the default view, the default view to "me alone" (when there is one), "me alone" back to the default.
 * @param {'away' | 'default' | 'me'} state where the camera is now
 * @param {boolean} hasMe a "me alone" target exists
 * @returns {'default' | 'me'}
 */
export function nextRecenter(state, hasMe) {
  return state === 'default' && hasMe ? 'me' : 'default';
}

/**
 * Whether a point is within {@link RECENTER_SKIP_PX} of the middle of a rectangle (so the re-centre after a sheet change would move nothing).
 * @param {{ x: number, y: number }} point
 * @param {Rect} rect
 * @param {number} [tolerancePx]
 * @returns {boolean}
 */
export function isCentered(point, rect, tolerancePx = RECENTER_SKIP_PX) {
  const centre = rectCenter(rect);
  return Math.hypot(point.x - centre.x, point.y - centre.y) <= tolerancePx;
}

/**
 * The horizontal room a pin chip may use (D75): the whole map less {@link CHIP_EDGE_PX} on each side, and in Expanded with the panel shown only the part right
 * of the panel (the left map padding), so a chip never clips at the viewport edge or hides under the panel.
 * @param {Viewport} viewport
 * @param {LayoutPayload} layout
 * @param {Padding} padding the map padding in force (its left side is the panel's right edge plus the gutter)
 * @returns {{ left: number, right: number }}
 */
export function chipRoom(viewport, layout, padding) {
  const panel = layout.mode === 'expanded' && !layout.panelHidden;
  return { left: panel ? padding.left : CHIP_EDGE_PX, right: viewport.width - CHIP_EDGE_PX };
}

/**
 * The horizontal shift that brings a chip, centred on `centerX`, inside the room. A chip that is wider than the room is centred in it.
 * @param {number} centerX the x of the middle of the chip (the pin's x)
 * @param {number} chipWidthPx
 * @param {{ left: number, right: number }} room
 * @returns {number} the shift in px: negative moves the chip left, 0 when it already fits
 */
export function clampChipShift(centerX, chipWidthPx, room) {
  if (chipWidthPx >= room.right - room.left) return (room.left + room.right) / 2 - centerX;
  const left = centerX - chipWidthPx / 2;
  if (left < room.left) return room.left - left;
  const right = centerX + chipWidthPx / 2;
  return right > room.right ? room.right - right : 0;
}

/**
 * Where the caret of a shifted chip sits (D79): over the pin centre, which the clamp shift moved the chip away from, so `shiftPx` the other way from the middle
 * of the chip. It never goes closer than `insetPx` to an end of the chip, so it stays on the straight part of the rounded body; a chip narrower than twice the
 * inset gets its caret in the middle.
 * @param {number} chipWidthPx
 * @param {number} shiftPx the shift {@link clampChipShift} gave the chip (negative: moved left, so the pin is right of the middle)
 * @param {number} [insetPx]
 * @returns {number} the x of the caret's centre, from the left edge of the chip
 */
export function chipCaretX(chipWidthPx, shiftPx, insetPx = CHIP_CARET_INSET_PX) {
  const inset = Math.min(insetPx, chipWidthPx / 2);
  return Math.min(chipWidthPx - inset, Math.max(inset, chipWidthPx / 2 - shiftPx));
}
