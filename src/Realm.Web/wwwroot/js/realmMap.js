// @ts-check
// realmMap.js: the entry module of MapInterop (03 section 4). It creates and owns the MapLibre map, keeps the retained state
// (members, vehicles, zones, layout, targets), draws the pins, rings, badges, chips, zones and accuracy halos, performs the camera
// commands and reports back to .NET through the DotNetObjectReference. Pure maths lives in geo.js and layoutMath.js, styles in
// mapStyles.js, the test hooks in testHooks.js.
//
// IMPORT APPROACH (S5a; 03 section 4.1; risk "relative URLs and the worker under a path base"):
//   Every module specifier in this file is a plain relative URL: './geo.js' and the other siblings as static imports, and
//   '../lib/maplibre-gl/maplibre-gl.mjs' through one dynamic import() in init. No @Assets, no leading slash, no absolute URL, so
//   a specifier resolves against this file's own URL and the Ingress path prefix (the <base href>) never appears in the code.
//   .NET 10's MapStaticAssets serves every wwwroot file under its original name (ETag, no-cache) AND under a fingerprinted name,
//   and <ImportMap /> rewrites './js/x.js' and './lib/maplibre-gl/*.mjs' to the fingerprinted names (checked on a .NET 10 build):
//   with the import map the browser fetches the fingerprinted copies, without it the original names, and this file works unchanged
//   either way (an import map applies to dynamic import() as it does to static imports). The MapLibre worker is created by the
//   vendored entry as new URL('./maplibre-gl-worker.mjs', import.meta.url), beside the entry, so the three vendored .mjs files stay
//   together under their original names and the worker URL resolves whether or not the entry was loaded under a fingerprinted name.
//
// TSC APPROACH FOR THE MAPLIBRE TYPES (03 section 1.4, the first slice picks and records it): maplibre-gl is a devDependency only
//   for its maplibre-gl.d.ts. Of the two options of 1.4, a static `import ... from '../lib/maplibre-gl/maplibre-gl.mjs'` with
//   `// @ts-ignore` fails under allowJs + checkJs, because tsc follows the literal specifier into the 590 KB vendored file and
//   checks it (hundreds of implicit-any errors; `exclude` only keeps it out of the root files), and a `paths` entry does not apply
//   to a relative specifier. So the entry is loaded by `import(MAPLIBRE_ENTRY)` with the specifier held in a constant, which tsc
//   cannot follow, and the namespace is typed once by the JSDoc cast `/** @type {typeof import('maplibre-gl')} */` in loadMapLibre:
//   from there on Map, Marker, AttributionControl and the rest are the real 6.11.2 types and `tsc --noEmit` checks every call.
//   tsconfig.json also excludes **/lib/**. A failed load is caught in init and reported like a WebGL failure (OnWebGlUnavailable).
//
// Contract in brief: every setter stores its payload and schedules one animation frame (retained state, so nothing is lost while a
// style loads or switches); `init` is idempotent; every export catches its own errors and reports them through OnError, never
// rethrowing into Blazor. Per-frame traffic to .NET is zero. S5a creates the thirteen exports below; S8a adds the selection ones and
// S9 the bubbles in the commented region at the end of this file, leaving the code above untouched (04 section 1.8).

import { circlePolygon, metersPerPixel } from './geo.js';
import { DEFAULT_LAYOUT, PIN_BODY_ALLOWANCE_PX, computePadding, haversine, isAtDefault, quantizeZoom } from './layoutMath.js';
import {
  FALLBACK_ZONE_APPEARANCE,
  HALO_SOURCE,
  STYLES,
  ZONE_SOURCE,
  appearanceOf,
  buildStyle,
  isStyleId,
  overlaySpec,
  transformStyle,
} from './mapStyles.js';
import { installTestHooks, removeTestHooks } from './testHooks.js';
import { readSheet, sheetHeightForPadding, sheetMetrics } from './realmShell.js';

/**
 * The vendored entry by its plain relative URL, resolved against this module's own URL (see the header). It sits in a variable,
 * not in a literal `import` specifier, so that tsc, which follows every literal specifier of a .js file into the 590 KB vendored
 * .mjs and then checks it, never opens it; the namespace is typed by the JSDoc cast in {@link loadMapLibre}.
 */
const MAPLIBRE_ENTRY = '../lib/maplibre-gl/maplibre-gl.mjs';

/** @type {typeof import('maplibre-gl') | null} */
let maplibreLib = null;

/** The library, once init has loaded it (every caller runs inside a live runtime, so it has). @returns {typeof import('maplibre-gl')} */
function lib() {
  if (!maplibreLib) throw new Error('MapLibre is not loaded');
  return maplibreLib;
}

/** Loads the vendored MapLibre once. A failure is caught by init and reported like any other construction failure. @returns {Promise<typeof import('maplibre-gl')>} */
async function loadMapLibre() {
  if (!maplibreLib) maplibreLib = /** @type {typeof import('maplibre-gl')} */ (await import(MAPLIBRE_ENTRY));
  return maplibreLib;
}

/** @typedef {import('maplibre-gl').Map} MapLibreMap */
/** @typedef {import('./geo.js').LngLat} LngLat */
/** @typedef {import('./geo.js').Bounds} Bounds */
/** @typedef {import('./layoutMath.js').Padding} Padding */
/** @typedef {import('./layoutMath.js').LayoutPayload} LayoutPayload */
/** @typedef {import('./mapStyles.js').StyleId} StyleId */
/** @typedef {import('./mapStyles.js').Appearance} Appearance */

// ---- payload types (03 section 4.5; realm.d.ts is not part of this slice, so the shapes live here as JSDoc) ----------------------

/** @typedef {'away' | 'default' | 'me'} RecenterState */
/** @typedef {{ color: string, dashed: boolean, widthPx: number }} Ring */
/** @typedef {'driving' | 'stale' | 'offline' | 'home' | null} Badge */
/**
 * @typedef {object} MemberPayloadItem
 * @property {string} id
 * @property {string} name
 * @property {string} initial
 * @property {string} color
 * @property {number | null} lat
 * @property {number | null} lon
 * @property {number | null} accuracyM
 * @property {boolean} poorAccuracy
 * @property {'static' | 'offline' | 'stale' | 'driving' | 'atPlace' | 'out' | 'nofix'} status
 * @property {Ring} ring
 * @property {Badge} badge
 * @property {boolean} lowBattery
 * @property {boolean} drivingFresh
 * @property {boolean} far
 * @property {boolean} isStatic
 * @property {boolean} isMe
 * @property {0 | 1 | 2 | 3 | 4} zClass
 * @property {string | null} avatarUrl
 * @property {string | null} chip
 * @property {number} chipMinute
 * @property {string} ariaLabel
 * @property {string} tooltip
 * @property {string} bubbleLabel
 * @property {string} bubbleTooltip
 */
/** @typedef {{ version: number, meId: string, members: MemberPayloadItem[] }} MembersPayload */
/**
 * @typedef {object} VehiclePayloadItem
 * @property {string} id
 * @property {string} name
 * @property {'pickup' | 'car'} glyph
 * @property {number | null} lat
 * @property {number | null} lon
 * @property {Ring} ring
 * @property {boolean} stale
 * @property {string | null} chip
 * @property {string} ariaLabel
 * @property {string} tooltip
 */
/** @typedef {{ version: number, vehicles: VehiclePayloadItem[] }} VehiclesPayload */
/** @typedef {{ id: string, name: string, lat: number, lon: number, radiusM: number, occupied: boolean }} ZoneItem */
/** @typedef {{ lineColor: string, fillAlpha: number, fillAlphaOccupied: number, casing: boolean }} ZoneAppearance */
/**
 * `maxRadiusM` is optional and not part of 03 section 4.5: when absent JS applies the 5 km default of 01 section 4.1, so a payload
 * that still carries the 32,187 m arrival zone never draws it.
 * @typedef {{ version: number, show: boolean, zones: ZoneItem[], maxRadiusM?: number,
 *             appearances: { dark: ZoneAppearance, light: ZoneAppearance, imagery: ZoneAppearance } }} ZonesPayload
 */
/** @typedef {{ version: number, default: { bounds: Bounds, maxZoom: number }, me: { center: LngLat, zoom: number } | null }} DefaultTargets */
/**
 * @typedef {object} CameraState
 * @property {LngLat} center
 * @property {number} zoom
 * @property {Bounds} bounds
 * @property {boolean} animated the last camera move used a duration above zero
 * @property {number} lastDurationMs the duration of the last camera move (0 for a jump, a gesture or reduced motion)
 * @property {RecenterState} recenter computed here against the default targets (within 40 m and 0.3 zoom, 01 section 4.11)
 * @property {boolean} userInitiated the last move was a user gesture
 */
/** @typedef {{ styleId: StyleId, ok: boolean, error?: string }} StyleResult */
/**
 * @typedef {object} InitOptions
 * @property {string} containerId
 * @property {StyleId} styleId
 * @property {LngLat} center
 * @property {number} zoom
 * @property {number} [minZoom]
 * @property {number} [maxZoom]
 * @property {boolean} reducedMotion
 * @property {boolean} testHooks
 * @property {CameraState | null} [restoreCamera]
 * @property {{ mapLabel: string, attributionLabel: string, clusterName: string, clusterTooltip: string, demoAttribution: string }} strings
 * @property {{ fanout: boolean, bubbles: boolean }} [features]
 */
/** @typedef {{ jsVersion: string, payloadSchema: number, maplibre: string }} InitResult */
/** @typedef {{ invokeMethodAsync: (method: string, ...args: any[]) => Promise<any> }} DotNetRef */

// ---- constants --------------------------------------------------------------------------------------------------------------

const JS_VERSION = '1.0.0';
/** Must equal C#'s MapInterop.PayloadSchema (03 section 4.1). */
const PAYLOAD_SCHEMA = 1;
const MIN_ZOOM = 3;
const MAX_ZOOM = 19;
const FIT_MAX_ZOOM = 16;
const FIT_DURATION_MS = 600;
/** Pins glide between snapshots over 800 ms, linear (01 section 4.2). */
const GLIDE_MS = 800;
/** A pointer that moved more than this since pointerdown made a pan, not a tap (03 section 4.6). */
const TAP_SLOP_PX = 6;
const CAMERA_DEBOUNCE_MS = 120;
const STYLE_TIMEOUT_MS = 8000;
const ERROR_WINDOW_MS = 10000;
const ZONE_MIN_RADIUS_PX = 14;
const ZONE_LABEL_MIN_ZOOM = 14;
/** 01 section 4.1 (`ui_max_zone_radius_km` default): a zone with a larger radius, such as the arrival zone, is never drawn. */
const DEFAULT_MAX_ZONE_RADIUS_M = 5000;
const MEMBER_SIZE_PX = 48;
const MEMBER_SELECTED_SIZE_PX = 60;
const VEHICLE_SIZE_PX = 44;
/** The pointer below the body (12 wide, 8 tall) and the chip's height and gap (01 sections 4.2 and 4.4). */
const POINTER_PX = 8;
const CHIP_HEIGHT_PX = 36;
const CHIP_GAP_PX = 8;
const CAMERA_STORAGE_KEY = 'realm.camera';
const MAPLIBRE_CREDIT = '<a href="https://maplibre.org/" target="_blank" rel="noopener noreferrer">MapLibre</a>';

// ---- icons: Material Icons (Apache-2.0), the same glyphs as MudBlazor's Icons.Material.Filled; the pickup is custom (01 4.7) -------

/** @type {Record<string, string[]>} */
const ICONS = {
  car: ['M18.92 6.01C18.72 5.42 18.16 5 17.5 5h-11c-.66 0-1.21.42-1.42 1.01L3 12v8c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-1h12v1c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-8l-2.08-5.99zM6.5 16c-.83 0-1.5-.67-1.5-1.5S5.67 13 6.5 13s1.5.67 1.5 1.5S7.33 16 6.5 16zm11 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zM5 11l1.5-4.5h11L19 11H5z'],
  schedule: ['M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8z', 'M12.5 7H11v6l5.25 3.15.75-1.23-4.5-2.67z'],
  cloudOff: ['M19.35 10.04A7.49 7.49 0 0 0 12 4c-1.48 0-2.85.43-4.01 1.17l1.46 1.46a5.497 5.497 0 0 1 8.05 4.87v.5H19c1.66 0 3 1.34 3 3 0 1.13-.64 2.11-1.56 2.62l1.45 1.45C23.16 18.16 24 16.68 24 15c0-2.64-2.05-4.78-4.65-4.96zM3 5.27l2.75 2.74C2.56 8.15 0 10.77 0 14c0 3.31 2.69 6 6 6h11.73l2 2L21 20.73 4.27 4 3 5.27zM7.73 10l8 8H6c-2.21 0-4-1.79-4-4s1.79-4 4-4h1.73z'],
  home: ['M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z'],
  batteryAlert: ['M15.67 4H14V2h-4v2H8.33C7.6 4 7 4.6 7 5.33v15.33C7 21.4 7.6 22 8.33 22h7.33c.74 0 1.34-.6 1.34-1.33V5.33C17 4.6 16.4 4 15.67 4zM13 18h-2v-2h2v2zm0-4h-2V9h2v5z'],
  place: ['M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 0 1 0-5 2.5 2.5 0 0 1 0 5z'],
  // A pickup truck seen from the side, facing right: open bed, cab with a windshield, two wheels. The window is wound the other way
  // round, so the default non-zero fill rule cuts it out of the cab.
  pickup: ['M2 9.5h9.5v-3H16l3.3 4H21.2c.44 0 .8.36.8.8V16H2z', 'M13 8v2.2h4.6L15.8 8z', 'M6.2 14.2a2.4 2.4 0 0 1 0 4.8 2.4 2.4 0 0 1 0-4.8z', 'M17.6 14.2a2.4 2.4 0 0 1 0 4.8 2.4 2.4 0 0 1 0-4.8z'],
};

/**
 * Static markup only (no payload text ever goes through innerHTML).
 * @param {string} name
 * @returns {string}
 */
function icon(name) {
  const paths = (ICONS[name] ?? []).map((d) => `<path d="${d}"/>`).join('');
  return `<svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">${paths}</svg>`;
}

const BADGE_ICON = /** @type {const} */ ({ driving: 'car', stale: 'schedule', offline: 'cloudOff', home: 'home' });

const PIN_TEMPLATE =
  '<span class="realm-pin__pulse"></span>' +
  '<span class="realm-pin__disc"><span class="realm-pin__face"></span></span>' +
  '<svg class="realm-pin__tip" viewBox="0 0 12 8" width="12" height="8" focusable="false" aria-hidden="true"><path d="M0 0H12L6 8Z"/></svg>' +
  '<span class="realm-pin__badge realm-pin__badge--status" hidden></span>' +
  `<span class="realm-pin__badge realm-pin__badge--battery" hidden>${icon('batteryAlert')}</span>`;

// ---- runtime state ----------------------------------------------------------------------------------------------------------

/**
 * @typedef {object} Pin
 * @property {'member' | 'vehicle'} kind
 * @property {string} id
 * @property {HTMLButtonElement} el
 * @property {import('maplibre-gl').Marker} marker
 * @property {Record<string, unknown>} last the values last written to the DOM, per aspect (so an unchanged payload touches nothing)
 * @property {LngLat | null} shown where the marker is drawn now
 * @property {LngLat | null} target where it is gliding to
 * @property {{ t0: number, from: LngLat, to: LngLat } | null} glide
 * @property {number} sizePx
 * @property {string} ring
 * @property {boolean} dashed
 * @property {string | null} badge
 * @property {number} dx the fan-out shift in px (S9; always 0 until then)
 * @property {HTMLElement | null} chipEl
 * @property {boolean} chipBelow
 */
/** @typedef {{ drawn: boolean, occupied: boolean, fillAlpha: number, dashed: boolean, drawnRadiusM: number, lat: number, lon: number, name: string }} ZoneState */
/** @typedef {{ styleId: StyleId, revert: boolean, notifyOnSuccess: boolean, resolve: (result: StyleResult) => void, timer: ReturnType<typeof setTimeout> }} PendingStyle */
/**
 * @typedef {object} Runtime
 * @property {InitOptions} opts
 * @property {DotNetRef | null} dotnet
 * @property {MapLibreMap} map
 * @property {HTMLElement} container
 * @property {StyleId | null} styleId the style that is loaded (the last good one); null until the first load
 * @property {Appearance} appearance
 * @property {boolean} styleReady
 * @property {boolean} loadFired the map's first `load` has happened (OnReady was sent)
 * @property {PendingStyle | null} pending
 * @property {LayoutPayload} layout
 * @property {Padding | null} forcedPadding
 * @property {number | null} sheetHeightPx the measured sheet height (the sheetMetrics feed, S7a); null while there is no sheet (then the Peek height is assumed)
 * @property {Padding} appliedPadding
 * @property {MembersPayload | null} members
 * @property {VehiclesPayload | null} vehicles
 * @property {ZonesPayload | null} zones
 * @property {DefaultTargets | null} targets
 * @property {{ members: number, vehicles: number, zones: number, targets: number }} versions
 * @property {{ selected: boolean } & Record<string, unknown> | null} selection S8a sets it; S5a only reads it
 * @property {Map<string, Pin>} pins keyed `member:{id}` and `vehicle:{id}`
 * @property {Map<string, { marker: import('maplibre-gl').Marker, el: HTMLElement }>} zoneLabels
 * @property {Map<string, ZoneState>} zoneState
 * @property {number} zoneQz
 * @property {{ resize: boolean, members: boolean, vehicles: boolean, zones: boolean, halos: boolean }} dirty
 * @property {number} raf
 * @property {boolean} reducedMotion
 * @property {{ animate: boolean } | null} wantFit a fitDefault that arrived before the targets or a measurable container
 * @property {{ lastDurationMs: number }} cam
 * @property {boolean} moveUser
 * @property {boolean} lastMoveUser
 * @property {ReturnType<typeof setTimeout> | null} cameraTimer
 * @property {{ center: LngLat, zoom: number, recenter: RecenterState } | null} lastReported
 * @property {ResizeObserver | null} resizeObserver
 * @property {() => void} onVisibility
 * @property {{ frames: number, setterCalls: number, callbacksSent: number }} stats
 */

/** @type {Runtime | null} */
let rt = null;

/**
 * Hooks that later slices add to `window.__realm` (sheet, bubbles, layoutBubbles): they put their observer here from their own
 * region at the end of this file, and `buildProbe` hands them to testHooks.js.
 * @type {Record<string, unknown>}
 */
const extraHooks = {};

/** @type {Map<string, number>} */
const recentErrors = new Map();

// ---- errors and callbacks into .NET -----------------------------------------------------------------------------------------

/**
 * @param {DotNetRef | null | undefined} dotnet
 * @param {string} name
 * @param {any[]} args
 */
function send(dotnet, name, args) {
  if (!dotnet) return;
  try {
    const pending = dotnet.invokeMethodAsync(name, ...args);
    if (pending && typeof pending.catch === 'function') pending.catch(() => {});
  } catch {
    // the reference was disposed or the circuit is gone: a late event must never throw (03 section 4.7)
  }
}

/**
 * One call into .NET (counted by stats().callbacksSent). Does nothing after dispose.
 * @param {string} name
 * @param {...any} args
 */
function notify(name, ...args) {
  const r = rt;
  if (!r) return;
  r.stats.callbacksSent += 1;
  send(r.dotnet, name, args);
}

/**
 * Reports a caught exception once per distinct message per 10 s through OnError (message cut to 300 characters, no payload data)
 * and to the console, where the E2E guard fixture fails the test on it. Never throws.
 * @param {string} where the export or area that failed
 * @param {unknown} error
 * @param {Runtime | null} [runtime]
 */
function reportError(where, error, runtime = rt) {
  try {
    const message = String(error instanceof Error ? error.message : error).slice(0, 300);
    const key = `${where}\n${message}`;
    const now = performance.now();
    const last = recentErrors.get(key);
    if (last !== undefined && now - last < ERROR_WINDOW_MS) return;
    recentErrors.set(key, now);
    console.error(`[realmMap] ${where}: ${message}`);
    if (runtime) {
      runtime.stats.callbacksSent += 1;
      send(runtime.dotnet, 'OnError', [where, message]);
    }
  } catch {
    // reporting must never fail
  }
}

/**
 * Runs a synchronous export body: counts it, and keeps any exception out of Blazor.
 * @template T
 * @param {string} where
 * @param {() => T} body
 * @param {boolean} [setter]
 * @returns {T | undefined}
 */
function call(where, body, setter = false) {
  try {
    if (setter && rt) rt.stats.setterCalls += 1;
    return body();
  } catch (error) {
    reportError(where, error);
    return undefined;
  }
}

// ---- pins: DOM, glide, reconcile --------------------------------------------------------------------------------------------

/**
 * @param {Pin} pin
 * @param {string} key
 * @param {unknown} value
 * @returns {boolean} true when the value differs from the one last applied (and records it)
 */
function changed(pin, key, value) {
  if (pin.last[key] === value) return false;
  pin.last[key] = value;
  return true;
}

/** @param {HTMLElement} el @param {string} cls @param {boolean} on */
function toggle(el, cls, on) {
  el.classList.toggle(cls, on);
}

/**
 * @param {Runtime} r
 * @param {'member' | 'vehicle'} kind
 * @param {string} id
 * @returns {boolean}
 */
function isSelected(r, kind, id) {
  const selection = /** @type {{ kind?: string, id?: string } | null} */ (r.selection);
  return selection !== null && selection.kind === kind && selection.id === id;
}

/**
 * @param {Runtime} r
 * @param {'member' | 'vehicle'} kind
 * @param {string} id
 * @returns {Pin}
 */
function createPin(r, kind, id) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = `realm-pin realm-ui realm-pin--${kind}`;
  el.setAttribute('data-testid', `pin-${kind}-${id}`);
  el.setAttribute('role', 'button');
  el.tabIndex = -1;
  el.innerHTML = PIN_TEMPLATE;

  const marker = new (lib().Marker)({ element: el, anchor: 'bottom', offset: [0, 0] });
  /** @type {Pin} */
  const pin = {
    kind, id, el, marker, last: {}, shown: null, target: null, glide: null,
    sizePx: kind === 'vehicle' ? VEHICLE_SIZE_PX : MEMBER_SIZE_PX, ring: '', dashed: false, badge: null, dx: 0, chipEl: null, chipBelow: false,
  };

  // The pin's own tap handling: a pan that ends over a pin is not a tap, and the click never reaches the map (03 section 4.6).
  /** @type {{ x: number, y: number } | null} */
  let down = null;
  el.addEventListener('pointerdown', (event) => {
    down = { x: event.clientX, y: event.clientY };
  });
  el.addEventListener('click', (event) => {
    event.stopPropagation();
    const moved = down !== null && event.detail !== 0 && Math.hypot(event.clientX - down.x, event.clientY - down.y) > TAP_SLOP_PX;
    down = null;
    if (!moved) notify('OnPinTap', kind, id);
  });
  return pin;
}

/**
 * The chip above (or, near the top padding, below) the pin (01 section 4.4). Its text is decided in C#.
 * @param {Pin} pin
 * @param {string | null} text
 */
function applyChip(pin, text) {
  if (text === null || text === '') {
    if (pin.chipEl) {
      pin.chipEl.remove();
      pin.chipEl = null;
      pin.chipBelow = false;
    }
    return;
  }
  if (!pin.chipEl) {
    const chip = document.createElement('span');
    chip.className = 'realm-chip';
    chip.setAttribute('data-testid', 'chip-here-for');
    chip.innerHTML = `${icon('place')}<span class="realm-chip__text"></span>`;
    pin.el.appendChild(chip);
    pin.chipEl = chip;
  }
  const label = pin.chipEl.querySelector('.realm-chip__text');
  if (label && label.textContent !== text) label.textContent = text;
}

/**
 * @param {Pin} pin
 * @param {string} color
 * @param {boolean} dashed
 * @param {number} widthPx
 */
function applyRing(pin, color, dashed, widthPx) {
  if (changed(pin, 'ringColor', color)) pin.el.style.setProperty('--realm-pin-ring', color);
  if (changed(pin, 'ringWidth', widthPx)) pin.el.style.setProperty('--realm-pin-ring-w', `${widthPx}px`);
  if (changed(pin, 'ringDashed', dashed)) toggle(pin.el, 'realm-pin--dashed', dashed);
  pin.ring = color;
  pin.dashed = dashed;
}

/**
 * @param {Pin} pin
 * @param {Badge} badge
 */
function applyStatusBadge(pin, badge) {
  pin.badge = badge;
  if (!changed(pin, 'badge', badge)) return;
  const el = /** @type {HTMLElement} */ (pin.el.querySelector('.realm-pin__badge--status'));
  el.hidden = badge === null;
  el.className = `realm-pin__badge realm-pin__badge--status${badge ? ` realm-pin__badge--${badge}` : ''}`;
  el.innerHTML = badge ? icon(BADGE_ICON[badge]) : '';
}

/**
 * The face of a member pin: the photo over the initials on the member colour; a failed photo removes itself (01 section 4.2).
 * The static pin without a photo shows the Home glyph instead of initials (01 section 4.7).
 * @param {Pin} pin
 * @param {MemberPayloadItem} item
 */
function applyFace(pin, item) {
  const url = item.avatarUrl ? new URL(item.avatarUrl, document.baseURI).href : null;
  if (!changed(pin, 'face', `${url}|${item.initial}|${item.isStatic}`)) return;
  const face = /** @type {HTMLElement} */ (pin.el.querySelector('.realm-pin__face'));
  face.replaceChildren();
  const initials = document.createElement('span');
  initials.className = 'realm-pin__initials';
  if (item.isStatic) initials.innerHTML = icon('home');
  else initials.textContent = item.initial;
  face.appendChild(initials);
  if (url) {
    const photo = document.createElement('img');
    photo.className = 'realm-pin__photo';
    photo.alt = '';
    photo.decoding = 'async';
    photo.addEventListener('error', () => photo.remove(), { once: true });
    photo.src = url;
    face.appendChild(photo);
  }
}

/**
 * @param {Runtime} r
 * @param {Pin} pin
 * @param {MemberPayloadItem} item
 */
function applyMember(r, pin, item) {
  const selected = isSelected(r, 'member', item.id);
  pin.sizePx = selected ? MEMBER_SELECTED_SIZE_PX : MEMBER_SIZE_PX;
  if (changed(pin, 'selected', selected)) toggle(pin.el, 'realm-pin--selected', selected);
  applyRing(pin, item.ring.color, item.ring.dashed, item.ring.widthPx + (selected ? 1 : 0));
  if (changed(pin, 'color', item.color)) pin.el.style.setProperty('--realm-pin-member', item.color);
  if (changed(pin, 'status', item.status)) {
    for (const status of ['driving', 'stale', 'offline', 'static']) toggle(pin.el, `realm-pin--${status}`, status === item.status);
  }
  applyFace(pin, item);
  applyStatusBadge(pin, item.badge);
  const battery = /** @type {HTMLElement} */ (pin.el.querySelector('.realm-pin__badge--battery'));
  const showBattery = item.lowBattery && !item.isStatic;
  if (changed(pin, 'battery', showBattery)) battery.hidden = !showBattery;
  if (changed(pin, 'aria', item.ariaLabel)) pin.el.setAttribute('aria-label', item.ariaLabel);
  if (changed(pin, 'title', item.tooltip)) pin.el.title = item.tooltip;
  const z = selected ? 4 : item.zClass;
  if (changed(pin, 'z', z)) pin.el.style.zIndex = String(z);
  applyChip(pin, item.chip);
}

/**
 * @param {Runtime} r
 * @param {Pin} pin
 * @param {VehiclePayloadItem} item
 */
function applyVehicle(r, pin, item) {
  const selected = isSelected(r, 'vehicle', item.id);
  if (changed(pin, 'selected', selected)) toggle(pin.el, 'realm-pin--selected', selected);
  applyRing(pin, item.ring.color, item.ring.dashed, item.ring.widthPx);
  if (changed(pin, 'glyph', item.glyph)) {
    const face = /** @type {HTMLElement} */ (pin.el.querySelector('.realm-pin__face'));
    face.innerHTML = `<span class="realm-pin__glyph">${icon(item.glyph === 'pickup' ? 'pickup' : 'car')}</span>`;
  }
  if (changed(pin, 'stale', item.stale)) toggle(pin.el, 'realm-pin--stale', item.stale);
  if (changed(pin, 'aria', item.ariaLabel)) pin.el.setAttribute('aria-label', item.ariaLabel);
  if (changed(pin, 'title', item.tooltip)) pin.el.title = item.tooltip;
  const z = selected ? 4 : 1;
  if (changed(pin, 'z', z)) pin.el.style.zIndex = String(z);
  applyChip(pin, item.chip);
}

/**
 * Points the pin at a position: a first position and reduced motion jump, any later change glides over 800 ms.
 * @param {Runtime} r
 * @param {Pin} pin
 * @param {number} lon
 * @param {number} lat
 */
function aim(r, pin, lon, lat) {
  /** @type {LngLat} */
  const to = [lon, lat];
  if (pin.shown === null) {
    pin.shown = to;
    pin.target = to;
    pin.marker.setLngLat(to).addTo(r.map);
    return;
  }
  if (pin.target && pin.target[0] === lon && pin.target[1] === lat) return;
  pin.target = to;
  if (r.reducedMotion || document.hidden) {
    pin.shown = to;
    pin.glide = null;
    pin.marker.setLngLat(to);
  } else {
    pin.glide = { t0: performance.now(), from: pin.shown, to };
  }
}

/**
 * Advances the glides to `now`.
 * @param {Runtime} r
 * @param {number} now
 * @returns {boolean} true while any pin is still gliding
 */
function advanceGlides(r, now) {
  let active = false;
  for (const pin of r.pins.values()) {
    const glide = pin.glide;
    if (!glide) continue;
    const t = Math.min(1, Math.max(0, (now - glide.t0) / GLIDE_MS));
    pin.shown = t >= 1 ? glide.to : [glide.from[0] + (glide.to[0] - glide.from[0]) * t, glide.from[1] + (glide.to[1] - glide.from[1]) * t];
    pin.marker.setLngLat(pin.shown);
    if (t >= 1) pin.glide = null;
    else active = true;
  }
  return active;
}

/** Jumps every glide to its end (reduced motion switched on, the tab became visible again). @param {Runtime} r */
function finishGlides(r) {
  for (const pin of r.pins.values()) {
    if (!pin.glide) continue;
    pin.shown = pin.glide.to;
    pin.glide = null;
    pin.marker.setLngLat(pin.shown);
  }
}

/** @param {Runtime} r @param {Pin} pin */
function removePin(r, pin) {
  pin.marker.remove();
  r.pins.delete(`${pin.kind}:${pin.id}`);
}

/**
 * Makes the pins of one kind match the payload: create, update, remove (a member without a fix has no pin).
 * @param {Runtime} r
 * @param {'member' | 'vehicle'} kind
 */
function reconcilePins(r, kind) {
  const items = kind === 'member' ? (r.members?.members ?? []) : (r.vehicles?.vehicles ?? []);
  const seen = new Set();
  for (const item of items) {
    if (typeof item.lat !== 'number' || typeof item.lon !== 'number') continue;
    const key = `${kind}:${item.id}`;
    seen.add(key);
    let pin = r.pins.get(key);
    if (!pin) {
      pin = createPin(r, kind, item.id);
      r.pins.set(key, pin);
    }
    if (kind === 'member') applyMember(r, pin, /** @type {MemberPayloadItem} */ (item));
    else applyVehicle(r, pin, /** @type {VehiclePayloadItem} */ (item));
    aim(r, pin, item.lon, item.lat);
  }
  for (const pin of [...r.pins.values()]) {
    if (pin.kind === kind && !seen.has(`${kind}:${pin.id}`)) removePin(r, pin);
  }
}

/**
 * A chip flips below the pointer when above the body it would cross the top map padding (01 section 4.4).
 * @param {Runtime} r
 */
function placeChips(r) {
  for (const pin of r.pins.values()) {
    if (!pin.chipEl || !pin.shown) continue;
    const tip = r.map.project(pin.shown);
    const chipTop = tip.y - (pin.sizePx + POINTER_PX) - CHIP_GAP_PX - CHIP_HEIGHT_PX;
    const below = chipTop < r.appliedPadding.top;
    if (below !== pin.chipBelow) {
      pin.chipBelow = below;
      pin.chipEl.classList.toggle('realm-chip--below', below);
    }
  }
}

// ---- overlay: accuracy halos, zone circles, zone labels ------------------------------------------------------------------------

/**
 * @param {Runtime} r
 * @param {string} sourceId
 * @param {object[]} features
 */
function pushFeatures(r, sourceId, features) {
  const source = /** @type {import('maplibre-gl').GeoJSONSource | undefined} */ (r.map.getSource(sourceId));
  if (source) source.setData(/** @type {any} */ ({ type: 'FeatureCollection', features }));
}

/** Accuracy halos: a member colour disc of radius `accuracyM` for every member the payload flags as poorly located. @param {Runtime} r */
function updateHalos(r) {
  const features = [];
  for (const member of r.members?.members ?? []) {
    if (typeof member.lat !== 'number' || typeof member.lon !== 'number') continue;
    if (!member.poorAccuracy || typeof member.accuracyM !== 'number' || !(member.accuracyM > 0)) continue;
    features.push(circlePolygon([member.lon, member.lat], member.accuracyM, 64, { id: member.id, color: member.color }));
  }
  pushFeatures(r, HALO_SOURCE, features);
}

/**
 * Zone circles as geodesic 64-gons (01 section 4.6): none above the maximum radius (the arrival zone), none while "Show places" is
 * off, never below 14 px on screen (the radius is rebuilt on a zoom quantised to 0.05, rounded down so the circle is never under
 * 14 px at the actual zoom). Colours and alphas come from the payload's appearance for the active style.
 * @param {Runtime} r
 */
function updateZones(r) {
  const payload = r.zones;
  const zoom = r.map.getZoom();
  const qz = quantizeZoom(zoom - 0.025);
  r.zoneQz = qz;
  /** @type {object[]} */
  const features = [];
  /** @type {Map<string, ZoneState>} */
  const state = new Map();
  if (payload && Array.isArray(payload.zones)) {
    const appearance = payload.appearances?.[r.appearance] ?? FALLBACK_ZONE_APPEARANCE;
    const maxRadiusM = typeof payload.maxRadiusM === 'number' ? payload.maxRadiusM : DEFAULT_MAX_ZONE_RADIUS_M;
    for (const zone of payload.zones) {
      const drawn = payload.show === true && zone.radiusM <= maxRadiusM;
      const fillAlpha = zone.occupied ? appearance.fillAlphaOccupied : appearance.fillAlpha;
      let drawnRadiusM = 0;
      if (drawn) {
        drawnRadiusM = Math.max(zone.radiusM, ZONE_MIN_RADIUS_PX * metersPerPixel(qz, zone.lat));
        features.push(
          circlePolygon([zone.lon, zone.lat], drawnRadiusM, 64, {
            id: zone.id, name: zone.name, occupied: zone.occupied, radiusM: zone.radiusM,
            lineColor: appearance.lineColor, fillAlpha, casing: appearance.casing,
          }),
        );
      }
      state.set(zone.id, { drawn, occupied: zone.occupied, fillAlpha, dashed: !zone.occupied, drawnRadiusM, lat: zone.lat, lon: zone.lon, name: zone.name });
    }
  }
  r.zoneState = state;
  pushFeatures(r, ZONE_SOURCE, features);
}

/** Zone names are HTML (12 px, text halo) from zoom 14, so no style needs glyphs for our overlay. @param {Runtime} r */
function updateZoneLabels(r) {
  const wanted = r.map.getZoom() >= ZONE_LABEL_MIN_ZOOM;
  for (const [id, label] of [...r.zoneLabels]) {
    const zone = r.zoneState.get(id);
    if (wanted && zone?.drawn) continue;
    label.marker.remove();
    r.zoneLabels.delete(id);
  }
  if (!wanted) return;
  for (const [id, zone] of r.zoneState) {
    if (!zone.drawn) continue;
    let label = r.zoneLabels.get(id);
    if (!label) {
      const el = document.createElement('div');
      el.className = 'realm-zone-label';
      el.setAttribute('data-zone-id', id);
      el.textContent = zone.name;
      const marker = new (lib().Marker)({ element: el, anchor: 'center' }).setLngLat([zone.lon, zone.lat]).addTo(r.map);
      label = { marker, el };
      r.zoneLabels.set(id, label);
    } else if (label.el.textContent !== zone.name) {
      label.el.textContent = zone.name;
    }
  }
}

/** Idempotent: adds whatever part of the overlay the loaded style lacks (03 section 4.9, the safety net after transformStyle). @param {Runtime} r */
function ensureOverlay(r) {
  const spec = overlaySpec();
  for (const [id, source] of Object.entries(spec.sources)) {
    if (!r.map.getSource(id)) r.map.addSource(id, source);
  }
  for (const layer of spec.layers) {
    if (!r.map.getLayer(layer.id)) r.map.addLayer(layer);
  }
}

// ---- styles -----------------------------------------------------------------------------------------------------------------

/**
 * Hands a style to MapLibre. `diff: false` on purpose: a diffed switch never fires `style.load`, which is what resolves setStyle.
 * The transform keeps the live overlay data across a switch; it is skipped while the current style is not loaded, because MapLibre
 * then defers the whole switch until the old style finishes loading, which a failed style never does (ensureOverlay covers it).
 * @param {Runtime} r
 * @param {StyleId} styleId
 * @param {boolean} withTransform
 */
function applyStyle(r, styleId, withTransform) {
  const style = buildStyle(styleId, { demoAttribution: r.opts.strings?.demoAttribution });
  r.styleReady = false;
  r.map.setStyle(style, withTransform ? { diff: false, transformStyle: (prev, next) => transformStyle(prev, next) } : { diff: false });
}

/**
 * Re-applies a known-good style after a failed switch (and demo-offline when even that fails). Silent: nobody awaits it.
 * @param {Runtime} r
 * @param {StyleId} styleId
 */
function revertTo(r, styleId) {
  /** @type {PendingStyle} */
  const pending = {
    styleId,
    revert: true,
    notifyOnSuccess: false,
    resolve: () => {},
    timer: setTimeout(() => failPending(r, pending, 'the style did not load'), STYLE_TIMEOUT_MS),
  };
  r.pending = pending;
  applyStyle(r, styleId, false);
}

/**
 * @param {Runtime} r
 * @param {PendingStyle} pending
 * @param {string} error
 */
function failPending(r, pending, error) {
  if (rt !== r || r.pending !== pending) return;
  clearTimeout(pending.timer);
  r.pending = null;
  if (pending.revert) {
    if (pending.styleId !== 'demo-offline') revertTo(r, 'demo-offline');
    else reportError('setStyle', `the fallback style failed: ${error}`, r);
    return;
  }
  /** @type {StyleResult} */
  const result = { styleId: pending.styleId, ok: false, error };
  // A switch that fails goes back to the last good style; a first load that fails falls back to demo-offline (03 section 4.9).
  revertTo(r, r.styleId ?? 'demo-offline');
  pending.resolve(result);
  if (pending.notifyOnSuccess || r.styleId === null) notify('OnStyleResult', result);
}

/** @param {Runtime} r */
function onStyleLoad(r) {
  ensureOverlay(r);
  r.styleReady = true;
  r.dirty.zones = true;
  r.dirty.halos = true;
  const pending = r.pending;
  if (pending) {
    clearTimeout(pending.timer);
    r.pending = null;
    r.styleId = pending.styleId;
    r.appearance = appearanceOf(pending.styleId);
    /** @type {StyleResult} */
    const result = { styleId: pending.styleId, ok: true };
    pending.resolve(result);
    if (pending.notifyOnSuccess) notify('OnStyleResult', result);
  }
  scheduleRender();
}

/**
 * Only an error that belongs to the pending style fails it: the style document's own request (its URL) or a parse or validation
 * error. Tile, sprite and glyph errors, which carry another URL or a source, never do (03 section 4.9).
 * @param {Runtime} r
 * @param {any} event
 */
function onMapError(r, event) {
  const pending = r.pending;
  if (!pending) return;
  if (event?.sourceId !== undefined || event?.tile !== undefined) return;
  const error = event?.error;
  const url = typeof error?.url === 'string' ? error.url : null;
  const styleUrl = STYLES[pending.styleId]?.url ?? null;
  if (url !== null && url !== styleUrl) return;
  failPending(r, pending, String(error?.message ?? error ?? 'the style failed to load'));
}

// ---- camera -----------------------------------------------------------------------------------------------------------------

/** @param {Runtime} r @returns {{ width: number, height: number }} */
function containerSize(r) {
  return { width: r.container.clientWidth, height: r.container.clientHeight };
}

/**
 * @param {Padding} a
 * @param {Padding} b
 * @returns {boolean}
 */
function samePadding(a, b) {
  return a.top === b.top && a.right === b.right && a.bottom === b.bottom && a.left === b.left;
}

/**
 * Applies the map padding: the forced one, or the measured one (01 section 3.4.3). The geographic centre stays, so the view is
 * centred in the visible rectangle that the padding leaves.
 * @param {Runtime} r
 */
function applyPadding(r) {
  const next = r.forcedPadding ?? computePadding(containerSize(r), r.layout, r.sheetHeightPx);
  if (samePadding(next, r.appliedPadding)) return;
  r.map.setPadding(next);
  r.appliedPadding = { ...next };
}

/** map.resize() with the centre kept (01 section 3.1; MapLibre alone may keep another anchor). @param {Runtime} r */
function applyResize(r) {
  const before = r.map.getCenter();
  r.map.resize();
  const after = r.map.getCenter();
  if (haversine([before.lng, before.lat], [after.lng, after.lat]) > 0.5) r.map.jumpTo({ center: before });
}

/** Pending resize, then padding: what any camera command needs to be true first. @param {Runtime} r */
function flushLayout(r) {
  if (r.dirty.resize) {
    r.dirty.resize = false;
    applyResize(r);
  }
  applyPadding(r);
}

/**
 * The camera `fitDefault` would produce now: the default bounds inside the map padding plus 56 px on top for the pin bodies,
 * `maxZoom` 16 (01 sections 3.4.3 and 4.9).
 * @param {Runtime} r
 * @returns {{ center: LngLat, zoom: number } | null}
 */
function defaultPose(r) {
  const target = r.targets?.default;
  if (!target) return null;
  const pose = r.map.cameraForBounds(target.bounds, {
    padding: { top: PIN_BODY_ALLOWANCE_PX, right: 0, bottom: 0, left: 0 },
    maxZoom: target.maxZoom ?? FIT_MAX_ZOOM,
  });
  if (!pose) return null;
  const center = lib().LngLat.convert(pose.center ?? r.map.getCenter());
  return { center: [center.lng, center.lat], zoom: pose.zoom ?? r.map.getZoom() };
}

/**
 * The one place a command moves the camera: records the duration (0 under reduced motion) that getCamera reports.
 * @param {Runtime} r
 * @param {{ center: LngLat, zoom: number }} pose
 * @param {number} durationMs
 */
function moveCamera(r, pose, durationMs) {
  const duration = r.reducedMotion ? 0 : durationMs;
  r.cam.lastDurationMs = duration;
  if (duration > 0) r.map.easeTo({ center: pose.center, zoom: pose.zoom, duration, essential: true });
  else r.map.jumpTo({ center: pose.center, zoom: pose.zoom });
}

/**
 * @param {Runtime} r
 * @param {boolean} animate
 * @returns {boolean} false when it cannot run yet (no targets, or a container without size)
 */
function fitNow(r, animate) {
  flushLayout(r);
  const pose = defaultPose(r);
  if (!pose) return false;
  moveCamera(r, pose, animate ? FIT_DURATION_MS : 0);
  return true;
}

/**
 * Where the camera is relative to the C#-computed targets (01 section 4.11).
 * @param {Runtime} r
 * @param {LngLat} center
 * @param {number} zoom
 * @returns {RecenterState}
 */
function recenterState(r, center, zoom) {
  const camera = { center, zoom };
  let pose = null;
  try {
    pose = defaultPose(r);
  } catch {
    pose = null;
  }
  if (isAtDefault(camera, pose)) return 'default';
  const me = r.targets?.me;
  if (me && isAtDefault(camera, { center: me.center, zoom: me.zoom })) return 'me';
  return 'away';
}

/**
 * @param {Runtime} r
 * @returns {CameraState}
 */
function cameraState(r) {
  const c = r.map.getCenter();
  const b = r.map.getBounds();
  /** @type {LngLat} */
  const center = [c.lng, c.lat];
  const zoom = r.map.getZoom();
  return {
    center,
    zoom,
    bounds: [[b.getWest(), b.getSouth()], [b.getEast(), b.getNorth()]],
    animated: r.cam.lastDurationMs > 0,
    lastDurationMs: r.cam.lastDurationMs,
    recenter: recenterState(r, center, zoom),
    userInitiated: r.lastMoveUser,
  };
}

/** OnCameraChanged, debounced 120 ms, only when something that matters moved (03 section 4.7). @param {Runtime} r */
function scheduleCameraReport(r) {
  if (r.cameraTimer !== null) clearTimeout(r.cameraTimer);
  r.cameraTimer = setTimeout(() => {
    r.cameraTimer = null;
    if (rt !== r) return;
    const state = cameraState(r);
    const last = r.lastReported;
    if (last && haversine(last.center, state.center) < 1 && Math.abs(last.zoom - state.zoom) < 0.01 && last.recenter === state.recenter) return;
    r.lastReported = { center: state.center, zoom: state.zoom, recenter: state.recenter };
    try {
      sessionStorage.setItem(CAMERA_STORAGE_KEY, JSON.stringify(state));
    } catch {
      // storage can be blocked; the camera is then simply not restored after a reload
    }
    notify('OnCameraChanged', state);
  }, CAMERA_DEBOUNCE_MS);
}

// ---- render loop ------------------------------------------------------------------------------------------------------------

function scheduleRender() {
  const r = rt;
  if (!r || r.raf) return;
  r.raf = requestAnimationFrame(renderFrame);
}

/** @param {Runtime} r @returns {boolean} */
function hasPendingWork(r) {
  const d = r.dirty;
  return d.resize || d.members || d.vehicles || d.zones || d.halos || r.wantFit !== null || r.raf !== 0;
}

/** One coalesced frame: applies whatever the setters retained, in a fixed order. @param {number} timestamp */
function renderFrame(timestamp) {
  const r = rt;
  if (!r) return;
  r.raf = 0;
  let gliding = false;
  try {
    r.stats.frames += 1;
    flushLayout(r);
    if (r.wantFit) {
      const { animate } = r.wantFit;
      if (fitNow(r, animate)) r.wantFit = null;
    }
    if (r.dirty.members) {
      r.dirty.members = false;
      reconcilePins(r, 'member');
    }
    if (r.dirty.vehicles) {
      r.dirty.vehicles = false;
      reconcilePins(r, 'vehicle');
    }
    gliding = advanceGlides(r, timestamp);
    if (r.styleReady) {
      if (r.dirty.halos) {
        r.dirty.halos = false;
        updateHalos(r);
      }
      if (r.dirty.zones || quantizeZoom(r.map.getZoom() - 0.025) !== r.zoneQz) {
        r.dirty.zones = false;
        updateZones(r);
      }
      updateZoneLabels(r);
    }
    placeChips(r);
  } catch (error) {
    reportError('render', error, r);
  }
  if (gliding) scheduleRender();
}

/**
 * Whether the last state has been drawn: style loaded, nothing pending, no glide or camera animation running.
 * @param {Runtime} r
 * @returns {boolean}
 */
function isSettled(r) {
  if (!r.styleReady || r.pending || hasPendingWork(r)) return false;
  for (const pin of r.pins.values()) if (pin.glide) return false;
  return r.loadFired && !r.map.isMoving() && r.map.loaded();
}

// ---- test hooks (03 section 4.10) -------------------------------------------------------------------------------------------

/** @returns {Record<string, unknown>} */
function buildProbe() {
  const need = () => {
    if (!rt) throw new Error('the map is not initialised');
    return rt;
  };
  return {
    mapPadding: () => ({ ...need().appliedPadding }),
    camera: () => cameraState(need()),
    styleId: () => need().styleId ?? need().opts.styleId,
    stats: () => ({ ...need().stats }),
    pins: () => {
      const r = need();
      return [...r.pins.values()].map((pin) => {
        const anchor = r.map.project(pin.shown ?? r.map.getCenter().toArray());
        return {
          id: pin.id, kind: pin.kind, x: anchor.x + pin.dx, y: anchor.y, anchorX: anchor.x, anchorY: anchor.y,
          ring: pin.ring, dashed: pin.dashed, badge: pin.badge, fanned: pin.dx !== 0, sizePx: pin.sizePx,
        };
      });
    },
    zones: () => {
      const r = need();
      const zoom = r.map.getZoom();
      return [...r.zoneState].map(([id, zone]) => ({
        id, drawn: zone.drawn, occupied: zone.occupied, fillAlpha: zone.fillAlpha, dashed: zone.dashed,
        radiusPx: zone.drawn ? zone.drawnRadiusM / metersPerPixel(zoom, zone.lat) : 0,
      }));
    },
    settled: () =>
      new Promise((resolve, reject) => {
        const r = need();
        const started = performance.now();
        const check = () => {
          if (rt !== r) return resolve(undefined);
          if (isSettled(r)) {
            // Everything is loaded and applied: let MapLibre paint it (idle follows the render that had nothing left to load).
            let done = false;
            const finish = () => {
              if (done) return;
              done = true;
              clearTimeout(fallback);
              requestAnimationFrame(() => resolve(undefined));
            };
            const fallback = setTimeout(finish, 1500);
            r.map.once('idle', finish);
            r.map.triggerRepaint();
            return undefined;
          }
          if (performance.now() - started > 15000) {
            return reject(new Error(`settled() timed out (style ready ${r.styleReady}, pending ${r.pending !== null}, work ${hasPendingWork(r)}, loaded ${r.map.loaded()})`));
          }
          return requestAnimationFrame(check);
        };
        check();
      }),
    ...extraHooks,
  };
}

// ---- lifecycle --------------------------------------------------------------------------------------------------------------

/**
 * Builds the MapLibre map (03 section 4.4). Throws when WebGL2 is unavailable.
 * @param {InitOptions} opts
 * @param {HTMLElement} container
 * @param {CameraState | null} restore
 * @returns {MapLibreMap}
 */
function createMap(opts, container, restore) {
  const strings = opts.strings;
  const map = new (lib().Map)({
    container,
    style: buildStyle(opts.styleId, { demoAttribution: strings?.demoAttribution }),
    center: restore ? restore.center : opts.center,
    zoom: restore ? restore.zoom : opts.zoom,
    minZoom: opts.minZoom ?? MIN_ZOOM,
    maxZoom: opts.maxZoom ?? MAX_ZOOM,
    attributionControl: false,
    dragRotate: false,
    pitchWithRotate: false,
    maxPitch: 0,
    touchPitch: false,
    bearing: 0,
    trackResize: false,
    fadeDuration: opts.reducedMotion ? 0 : 300,
    locale: {
      'Map.Title': strings?.mapLabel ?? 'Map',
      'AttributionControl.ToggleAttribution': strings?.attributionLabel ?? 'Map data attribution',
    },
    transformRequest: (url) => ({ url, referrerPolicy: 'no-referrer' }),
  });
  map.touchZoomRotate.disableRotation();
  map.keyboard.disableRotation();
  // A non-empty customAttribution makes MapLibre build the compact control at once (it also credits MapLibre itself); the sources'
  // own attributions (OpenFreeMap, USGS, the demo text) are collected into it as their styles load.
  map.addControl(new (lib().AttributionControl)({ compact: true, customAttribution: MAPLIBRE_CREDIT }), 'top-right');
  container.querySelector('.maplibregl-ctrl-attrib-button')?.setAttribute('data-testid', 'map-attribution');
  // MapLibre starts a compact control expanded and folds it on the first drag; ours starts as the 48 px (i) button (01 section 3.3).
  const attribution = container.querySelector('.maplibregl-ctrl-attrib');
  attribution?.classList.remove('maplibregl-compact-show');
  attribution?.setAttribute('open', '');
  return map;
}

/**
 * @param {InitOptions} opts
 * @param {DotNetRef | null} dotnet
 * @param {MapLibreMap} map
 * @param {HTMLElement} container
 * @returns {Runtime}
 */
function createRuntime(opts, dotnet, map, container) {
  return {
    opts, dotnet, map, container,
    styleId: null,
    appearance: appearanceOf(opts.styleId),
    styleReady: false,
    loadFired: false,
    pending: null,
    layout: { ...DEFAULT_LAYOUT, safe: { ...DEFAULT_LAYOUT.safe } },
    forcedPadding: null,
    sheetHeightPx: sheetHeightForPadding(sheetMetrics.current().heightPx),
    appliedPadding: { top: 0, right: 0, bottom: 0, left: 0 },
    members: null, vehicles: null, zones: null, targets: null,
    versions: { members: -Infinity, vehicles: -Infinity, zones: -Infinity, targets: -Infinity },
    selection: null,
    pins: new Map(),
    zoneLabels: new Map(),
    zoneState: new Map(),
    zoneQz: NaN,
    dirty: { resize: false, members: false, vehicles: false, zones: false, halos: false },
    raf: 0,
    reducedMotion: opts.reducedMotion === true,
    wantFit: null,
    cam: { lastDurationMs: 0 },
    moveUser: false,
    lastMoveUser: false,
    cameraTimer: null,
    lastReported: null,
    resizeObserver: null,
    onVisibility: () => {},
    stats: { frames: 0, setterCalls: 0, callbacksSent: 0 },
  };
}

/** Wires the map's events to the runtime. @param {Runtime} r */
function bindMap(r) {
  const { map, container } = r;
  map.on('style.load', () => onStyleLoad(r));
  map.on('error', (event) => onMapError(r, event));
  map.once('load', () => {
    r.loadFired = true;
    notify('OnReady', { jsVersion: JS_VERSION, payloadSchema: PAYLOAD_SCHEMA, maplibre: lib().getVersion() });
  });
  map.on('movestart', (event) => {
    r.moveUser = Boolean(event.originalEvent);
    if (r.moveUser) r.cam.lastDurationMs = 0;
  });
  map.on('moveend', () => {
    r.lastMoveUser = r.moveUser;
    scheduleCameraReport(r);
  });
  map.on('move', scheduleRender);
  map.on('zoom', scheduleRender);

  // The map-level click only ever sees empty map and zone taps: pins stop their own clicks, and anything inside a .realm-ui
  // element (pins, later the bubbles and the chip) is ignored here (03 section 4.6).
  map.on('click', (event) => {
    const target = event.originalEvent?.target;
    if (target instanceof Element && target.closest('.realm-ui')) return;
    if (r.styleReady && map.getLayer('realm-zones-fill')) {
      const hits = map.queryRenderedFeatures(event.point, { layers: ['realm-zones-fill'] });
      if (hits.length > 0) {
        // The smallest circle wins where zones overlap.
        const smallest = hits.reduce((a, b) => (Number(b.properties?.radiusM) < Number(a.properties?.radiusM) ? b : a));
        notify('OnPinTap', 'place', String(smallest.properties?.id));
        return;
      }
    }
    notify('OnMapTap');
  });

  r.resizeObserver = new ResizeObserver(() => {
    r.dirty.resize = true;
    scheduleRender();
  });
  r.resizeObserver.observe(container);

  r.onVisibility = () => {
    if (document.hidden) return;
    finishGlides(r);
    scheduleRender();
  };
  document.addEventListener('visibilitychange', r.onVisibility);
}

/** Disposes the map, the markers, the observers and the listeners, and drops the .NET reference. */
function teardown() {
  const r = rt;
  rt = null;
  if (!r) return;
  if (r.raf) cancelAnimationFrame(r.raf);
  if (r.cameraTimer !== null) clearTimeout(r.cameraTimer);
  if (r.pending) clearTimeout(r.pending.timer);
  r.resizeObserver?.disconnect();
  document.removeEventListener('visibilitychange', r.onVisibility);
  for (const pin of r.pins.values()) pin.marker.remove();
  for (const label of r.zoneLabels.values()) label.marker.remove();
  r.pins.clear();
  r.zoneLabels.clear();
  try {
    r.map.remove();
  } catch (error) {
    console.error(`[realmMap] teardown: ${String(error)}`);
  }
  r.container.classList.remove('realm-map', 'realm-map--reduced');
  r.dotnet = null;
  removeTestHooks(window);
}

// ---- exports (03 section 4.3) -----------------------------------------------------------------------------------------------

/**
 * Creates the map. Idempotent: a map that already exists is torn down first, so a resumed circuit, a hot reload or a double
 * first render never leaves two.
 * @param {InitOptions} opts
 * @param {DotNetRef} dotnet
 * @returns {Promise<InitResult>}
 */
export async function init(opts, dotnet) {
  /** @type {InitResult} */
  const info = { jsVersion: JS_VERSION, payloadSchema: PAYLOAD_SCHEMA, maplibre: 'unknown' };
  try {
    // The only await comes first: from the teardown on, init runs synchronously, so two overlapping inits cannot both build a map.
    try {
      await loadMapLibre();
      info.maplibre = safeVersion();
    } catch (error) {
      reportError('init', error, null);
      send(dotnet, 'OnWebGlUnavailable', []);
      return info;
    }
    teardown();
    const container = document.getElementById(opts.containerId);
    if (!container) throw new Error(`no element with id '${opts.containerId}'`);
    container.classList.add('realm-map');
    container.classList.toggle('realm-map--reduced', opts.reducedMotion === true);
    container.setAttribute('role', 'application');
    container.setAttribute('aria-label', opts.strings?.mapLabel ?? 'Map');

    /** @type {MapLibreMap} */
    let map;
    try {
      map = createMap(opts, container, opts.restoreCamera ?? null);
    } catch (error) {
      // WebGL2 is missing (v6 requires it): C# swaps the map for a static message and keeps the sheet working (03 section 4.4).
      container.classList.remove('realm-map', 'realm-map--reduced');
      reportError('init', error, null);
      send(dotnet, 'OnWebGlUnavailable', []);
      return info;
    }

    const r = createRuntime(opts, dotnet, map, container);
    rt = r;
    bindMap(r);
    // Initial style: a failure falls back to demo-offline (the map never shows a void with live pins) and tells C#.
    /** @type {PendingStyle} */
    const first = {
      styleId: opts.styleId,
      revert: false,
      notifyOnSuccess: false,
      resolve: () => {},
      timer: setTimeout(() => failPending(r, first, 'the style did not load'), STYLE_TIMEOUT_MS),
    };
    r.pending = first;
    flushLayout(r);
    if (opts.testHooks) installTestHooks(window, buildProbe());
    else removeTestHooks(window);
    scheduleRender();
  } catch (error) {
    reportError('init', error);
  }
  return info;
}

/** @returns {string} */
function safeVersion() {
  try {
    return lib().getVersion();
  } catch {
    return 'unknown';
  }
}

/** Tears everything down and drops the .NET reference. @returns {Promise<void>} */
export async function dispose() {
  try {
    teardown();
  } catch (error) {
    reportError('dispose', error, null);
  }
}

/**
 * Switches the style. Resolves (never rejects) on style.load, on failure or after `timeoutMs` (8 s); the outcome is also sent as
 * OnStyleResult. A failed switch re-applies the last good style (demo-offline when there was none). A switch that a newer setStyle
 * overtakes resolves `{ ok: false, error: 'superseded' }` without an OnStyleResult.
 * @param {StyleId} styleId
 * @param {{ timeoutMs?: number }} [opts]
 * @returns {Promise<StyleResult>}
 */
export function setStyle(styleId, opts = {}) {
  return new Promise((resolve) => {
    try {
      const r = rt;
      if (r) r.stats.setterCalls += 1;
      if (!r) return resolve({ styleId, ok: false, error: 'the map is not initialised' });
      if (!isStyleId(styleId)) {
        /** @type {StyleResult} */
        const unknown = { styleId, ok: false, error: `unknown style '${String(styleId)}'` };
        notify('OnStyleResult', unknown);
        return resolve(unknown);
      }
      if (!r.pending && r.styleReady && r.styleId === styleId) {
        /** @type {StyleResult} */
        const same = { styleId, ok: true };
        notify('OnStyleResult', same);
        return resolve(same);
      }
      if (r.pending) {
        clearTimeout(r.pending.timer);
        r.pending.resolve({ styleId: r.pending.styleId, ok: false, error: 'superseded' });
      }
      const timeoutMs = typeof opts.timeoutMs === 'number' && opts.timeoutMs > 0 ? opts.timeoutMs : STYLE_TIMEOUT_MS;
      /** @type {PendingStyle} */
      const pending = {
        styleId,
        revert: false,
        notifyOnSuccess: true,
        resolve,
        timer: setTimeout(() => failPending(r, pending, `the style did not load within ${timeoutMs} ms`), timeoutMs),
      };
      const withTransform = r.styleReady;
      r.pending = pending;
      applyStyle(r, styleId, withTransform);
    } catch (error) {
      reportError('setStyle', error);
      resolve({ styleId, ok: false, error: String(error instanceof Error ? error.message : error).slice(0, 300) });
    }
  });
}

/**
 * The layout facts that decide the map padding (mode, panel geometry, right stack, safe insets).
 * @param {LayoutPayload} layout
 */
export function setLayout(layout) {
  call('setLayout', () => {
    const r = rt;
    if (!r || !layout) return;
    r.layout = { ...DEFAULT_LAYOUT, ...layout, safe: { ...DEFAULT_LAYOUT.safe, ...layout.safe } };
    scheduleRender();
  }, true);
}

/**
 * `null` = measured mode (the padding follows the layout); an object = forced (tests, the aside host).
 * @param {Padding | null} padding
 */
export function setPadding(padding) {
  call('setPadding', () => {
    const r = rt;
    if (!r) return;
    r.forcedPadding = padding ? { top: padding.top, right: padding.right, bottom: padding.bottom, left: padding.left } : null;
    scheduleRender();
  }, true);
}

/**
 * @param {MembersPayload} payload the full list each time; JS diffs by id
 */
export function upsertMembers(payload) {
  call('upsertMembers', () => {
    const r = rt;
    if (!r || !payload || payload.version < r.versions.members) return;
    r.versions.members = payload.version;
    r.members = payload;
    r.dirty.members = true;
    r.dirty.halos = true;
    scheduleRender();
  }, true);
}

/**
 * @param {VehiclesPayload} payload
 */
export function upsertVehicles(payload) {
  call('upsertVehicles', () => {
    const r = rt;
    if (!r || !payload || payload.version < r.versions.vehicles) return;
    r.versions.vehicles = payload.version;
    r.vehicles = payload;
    r.dirty.vehicles = true;
    scheduleRender();
  }, true);
}

/**
 * @param {ZonesPayload} payload
 */
export function setZones(payload) {
  call('setZones', () => {
    const r = rt;
    if (!r || !payload || payload.version < r.versions.zones) return;
    r.versions.zones = payload.version;
    r.zones = payload;
    r.dirty.zones = true;
    scheduleRender();
  }, true);
}

/**
 * The C#-computed default view and "me alone" targets (01 section 4.9). Does not move the camera by itself, except to run a
 * fitDefault that was waiting for them.
 * @param {DefaultTargets} targets
 */
export function setDefaultTargets(targets) {
  call('setDefaultTargets', () => {
    const r = rt;
    if (!r || !targets || targets.version < r.versions.targets) return;
    r.versions.targets = targets.version;
    r.targets = targets;
    if (r.wantFit) scheduleRender();
    else scheduleCameraReport(r); // the camera did not move, but what "default" means did: recenter may have changed
  }, true);
}

/**
 * Runs the default camera: the default bounds inside the map padding plus 56 px on top, maxZoom 16; the first load does not
 * animate, later runs ease over 600 ms (0 under reduced motion). Without targets, or while the container has no size, the request
 * is kept and runs as soon as it can.
 * @param {{ animate?: boolean }} [opts]
 */
export function fitDefault(opts = {}) {
  call('fitDefault', () => {
    const r = rt;
    if (!r) return;
    const animate = opts.animate !== false;
    r.wantFit = null;
    if (!fitNow(r, animate)) {
      r.wantFit = { animate };
      scheduleRender();
    }
  });
}

/** Re-measures the container after a change that its ResizeObserver cannot see; the centre is kept. */
export function resize() {
  call('resize', () => {
    const r = rt;
    if (!r) return;
    r.dirty.resize = true;
    scheduleRender();
  });
}

/**
 * Camera durations become 0 and pulses stop (01 section 9); the glides finish at once. `fadeDuration` is fixed at construction.
 * @param {boolean} on
 */
export function setReducedMotion(on) {
  call('setReducedMotion', () => {
    const r = rt;
    if (!r) return;
    r.reducedMotion = on === true;
    r.container.classList.toggle('realm-map--reduced', r.reducedMotion);
    if (r.reducedMotion) finishGlides(r);
    scheduleRender();
  }, true);
}

/** @returns {CameraState} */
export function getCamera() {
  const r = rt;
  if (r) {
    try {
      return cameraState(r);
    } catch (error) {
      reportError('getCamera', error);
    }
  }
  return { center: [0, 0], zoom: 0, bounds: [[0, 0], [0, 0]], animated: false, lastDurationMs: 0, recenter: 'away', userInitiated: false };
}

// ---- S8a (selection and flights), S7a (sheet metrics), S9 (bubbles) add their exports and state below this line ----------------
// One clearly commented region per slice; nothing above this line is edited by them (04 section 1.8).

// ---- S7a: the sheet metrics feed and the `sheet` hook (03 sections 4.3 and 4.8, 01 section 3.4.3) -----------------------------------
// The sheet's measured height is the live input of computePadding: realmShell.js observes the sheet element (client side, no server call) and
// notifies `sheetMetrics` subscribers, and the one subscription below hands the number to the runtime that exists. The padding therefore follows a
// drag or a toggle by itself, and AC-08 and AC-09 read `mapPadding()` after settled(). Selection flights (S8a) pass their own target padding and
// never read this number. `createRuntime` seeds a new map from the current metrics, so a map that starts after the sheet needs no event.

sheetMetrics.subscribe((metrics) => {
  const r = rt;
  if (!r) return;
  const next = sheetHeightForPadding(metrics.heightPx);
  if (next === r.sheetHeightPx) return;
  r.sheetHeightPx = next;
  scheduleRender();
});

extraHooks.sheet = () => readSheet();
