// @ts-check
// layoutMath.js: the pure layout arithmetic of the map (03 section 4.1, 01 sections 3.4.3 and 4.11). No DOM, no MapLibre.
// All lengths are CSS pixels, all coordinates [lon, lat].
//
// S5a owns the region below. A later slice adds its exported functions in one commented region at the end of the file and leaves
// the code above untouched (04 section 1.8): S8a adds `selectionPadding` there.

import { EARTH_RADIUS_M } from './geo.js';

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
