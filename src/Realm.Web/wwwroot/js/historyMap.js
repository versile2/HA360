// @ts-check
// historyMap.js: the map of the Location History screen (0.3.0, D123). One MapLibre map that draws a member's day: the trail of the drives as lines (solid where the fixes were
// close, dashed where the path is a guess, with a gap where there is no data), a marker for each place visit and for the two ends of the day, and one highlighted entry (a drive's
// path, a visit's marker) that the timeline selects. It is a module of its own, not part of realmMap.js: the Location map's runtime (pins, bubbles, the sheet's padding) has nothing to
// do with a day's trail, and the two screens never exist together. It shares only the style registry (mapStyles.js), so the style a person chose is the style they see here.
//
// The map has its own element above the timeline (a column in Compact, beside it in Expanded), so no padding arithmetic is needed: the camera fits the whole element.
// Every setter stores its payload; the style may still be loading. The state a test reads is on the element: data-history-active (the id of the highlighted entry),
// data-history-segments (the number of trail segments drawn), data-history-stops and data-history-fit ("done" once the day has been fitted).
//
// Same rules as realmMap.js: plain relative module URLs (no leading slash, so the Ingress prefix never appears in the code), every export catches its own errors and
// reports them through OnError, and no colour is written here: the colours of the trail arrive with the payload and the markers are styled by realm-history.css.

import { boundsOf } from './geo.js';
import { buildStyle, isStyleId, styleDocumentUrl } from './mapStyles.js';

const MAPLIBRE_ENTRY = '../lib/maplibre-gl/maplibre-gl.mjs';
const MAPLIBRE_CREDIT = '<a href="https://maplibre.org/" target="_blank" rel="noopener noreferrer">MapLibre</a>';
const SOURCE = 'realm-history-trail';
const LAYER_CASING = 'realm-history-casing';
const LAYER_SOLID = 'realm-history-solid';
const LAYER_DASHED = 'realm-history-dashed';
const LAYER_ACTIVE = 'realm-history-active';
const PICK_LAYERS = [LAYER_ACTIVE, LAYER_SOLID, LAYER_DASHED, LAYER_CASING];
const FIT_PADDING_PX = 48;
const FIT_MAX_ZOOM = 16;
const STAY_ZOOM = 15;
const EASE_MS = 600;
const PICK_SLOP_PX = 10;
const STYLE_TIMEOUT_MS = 8000;
const DEFAULT_CENTER = /** @type {[number, number]} */ ([-85.341, 31.099]);

/** @typedef {import('maplibre-gl').Map} MapLibreMap */
/** @typedef {import('maplibre-gl').Marker} MapLibreMarker */
/** @typedef {{ invokeMethodAsync: (name: string, ...args: any[]) => Promise<any> }} DotNetRef */
/** @typedef {{ mapLabel?: string, attributionLabel?: string, demoAttribution?: string }} Strings */
/** @typedef {{ containerId: string, styleId: string, reducedMotion?: boolean, strings?: Strings }} InitOptions */
/** @typedef {{ id: string, dashed: boolean, points: Array<[number, number]> }} TrailSegment */
/**
 * A marker of the day. `kind` is 'stay' (a place visit), 'start' or 'end' (where the day began or ended). `id` is the id of the timeline entry it stands for ('' for the ends).
 * @typedef {{ id: string, kind: 'stay' | 'start' | 'end', lat: number, lon: number, label: string, number?: number }} Stop
 */
/**
 * @typedef {object} DayPayload
 * @property {string} key identifies the day shown (member and date): a new key fits the camera, the same key does not
 * @property {string} color the member's colour
 * @property {string} casing the colour of the line's outline
 * @property {TrailSegment[]} trail
 * @property {Stop[]} stops
 * @property {boolean} pickable false in the range view, where a marker is not a timeline entry
 */
/**
 * @typedef {object} Focus
 * @property {string | null} id the entry to highlight, or null for none
 * @property {'drive' | 'stay' | null} kind
 */

/** @typedef {typeof import('maplibre-gl')} MapLibreLib */

/**
 * @typedef {object} Runtime
 * @property {InitOptions} opts
 * @property {DotNetRef | null} dotnet
 * @property {MapLibreMap} map
 * @property {HTMLElement} container
 * @property {string} styleId
 * @property {string | null} styleBlob
 * @property {boolean} styleReady
 * @property {DayPayload | null} day
 * @property {Focus} focus
 * @property {string | null} fittedKey
 * @property {Map<string, MapLibreMarker>} markers
 * @property {ResizeObserver | null} observer
 * @property {number} timer
 */

/** @type {MapLibreLib | null} */
let maplibreLib = null;
/** @type {Runtime | null} */
let rt = null;

/** @returns {MapLibreLib} */
function lib() {
  if (!maplibreLib) throw new Error('MapLibre is not loaded');
  return maplibreLib;
}

/** @returns {Promise<MapLibreLib>} */
async function loadMapLibre() {
  if (!maplibreLib) maplibreLib = /** @type {MapLibreLib} */ (await import(MAPLIBRE_ENTRY));
  return maplibreLib;
}

/**
 * One call into .NET. A late event after dispose, or from a circuit that is gone, never throws.
 * @param {DotNetRef | null} dotnet
 * @param {string} name
 * @param {...any} args
 */
function send(dotnet, name, args) {
  if (!dotnet) return;
  try {
    const pending = dotnet.invokeMethodAsync(name, ...args);
    if (pending && typeof pending.catch === 'function') pending.catch(() => {});
  } catch {
    // the reference was disposed: nothing to tell
  }
}

/**
 * @param {string} where
 * @param {unknown} error
 */
function reportError(where, error) {
  const message = String(error instanceof Error ? error.message : error).slice(0, 300);
  console.error(`[historyMap] ${where}: ${message}`);
  if (rt) send(rt.dotnet, 'OnError', [where, message]);
}

/**
 * Runs a body and reports (never rethrows) its error.
 * @param {string} where
 * @param {() => void} body
 */
function call(where, body) {
  try {
    body();
  } catch (error) {
    reportError(where, error);
  }
}

/** @returns {boolean} */
function prefersReducedMotion() {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** @param {Runtime} r @returns {number} */
function duration(r) {
  return r.opts.reducedMotion === true || prefersReducedMotion() ? 0 : EASE_MS;
}

// ---- the map -------------------------------------------------------------------------------------------------------------------

/**
 * Creates the map in the element named by `opts.containerId`. Idempotent: a map that exists is removed first. A browser without WebGL 2 is reported through OnWebGlUnavailable
 * and the page shows a plain message instead.
 * @param {InitOptions} opts
 * @param {DotNetRef} dotnet
 * @returns {Promise<{ ok: boolean }>}
 */
export async function init(opts, dotnet) {
  try {
    try {
      await loadMapLibre();
    } catch (error) {
      console.warn(`[historyMap] MapLibre did not load: ${String(error)}`);
      send(dotnet, 'OnWebGlUnavailable', []);
      return { ok: false };
    }

    teardown();
    const container = document.getElementById(opts.containerId);
    if (!container) throw new Error(`no element with id '${opts.containerId}'`);
    const styleId = isStyleId(opts.styleId) ? opts.styleId : 'night';
    const built = buildStyle(styleId, { demoAttribution: opts.strings?.demoAttribution });
    const first = styleDocumentUrl(built, { createObjectURL: (blob) => URL.createObjectURL(blob), Blob });
    /** @type {MapLibreMap} */
    let map;
    try {
      map = new (lib().Map)({
        container,
        style: first,
        center: DEFAULT_CENTER,
        zoom: 11,
        minZoom: 3,
        maxZoom: 19,
        attributionControl: false,
        dragRotate: false,
        pitchWithRotate: false,
        maxPitch: 0,
        touchPitch: false,
        bearing: 0,
        fadeDuration: opts.reducedMotion || prefersReducedMotion() ? 0 : 300,
        locale: {
          'Map.Title': opts.strings?.mapLabel ?? 'Map',
          'AttributionControl.ToggleAttribution': opts.strings?.attributionLabel ?? 'Map data attribution',
        },
        transformRequest: (url) => ({ url, referrerPolicy: 'no-referrer' }),
      });
    } catch (error) {
      if (typeof built !== 'string') URL.revokeObjectURL(first);
      console.warn(`[historyMap] the map could not be created: ${String(error)}`);
      send(dotnet, 'OnWebGlUnavailable', []);
      return { ok: false };
    }

    map.touchZoomRotate.disableRotation();
    map.keyboard.disableRotation();
    map.addControl(new (lib().AttributionControl)({ compact: true, customAttribution: MAPLIBRE_CREDIT }), 'bottom-right');
    const attribution = container.querySelector('.maplibregl-ctrl-attrib-button');
    attribution?.setAttribute('data-testid', 'history-map-attribution');

    /** @type {Runtime} */
    const r = {
      opts,
      dotnet,
      map,
      container,
      styleId,
      styleBlob: typeof built === 'string' ? null : first,
      styleReady: false,
      day: null,
      focus: { id: null, kind: null },
      fittedKey: null,
      markers: new Map(),
      observer: null,
      timer: 0,
    };
    rt = r;
    container.dataset.historyReady = 'false';
    map.on('style.load', () => onStyleLoad(r));
    map.on('error', (event) => onMapError(r, event));
    map.on('click', (event) => onMapClick(r, event));
    map.on('movestart', (event) => {
      if (rt === r && event.originalEvent) container.dataset.historyMoved = 'true';
    });
    map.once('load', () => {
      container.dataset.historyReady = 'true';
      send(r.dotnet, 'OnReady', []);
    });
    r.timer = window.setTimeout(() => {
      if (rt === r && !r.styleReady) console.warn('[historyMap] the style did not load in time');
    }, STYLE_TIMEOUT_MS);
    r.observer = new ResizeObserver(() => onResize(r));
    r.observer.observe(container);
    return { ok: true };
  } catch (error) {
    reportError('init', error);
    return { ok: false };
  }
}

/** Removes the map, the markers and the observers, and drops the .NET reference. @returns {Promise<void>} */
export async function dispose() {
  call('dispose', teardown);
}

function teardown() {
  const r = rt;
  rt = null;
  if (!r) return;
  window.clearTimeout(r.timer);
  r.observer?.disconnect();
  for (const marker of r.markers.values()) marker.remove();
  r.markers.clear();
  try {
    r.map.remove();
  } catch (error) {
    console.warn(`[historyMap] teardown: ${String(error)}`);
  }
  if (r.styleBlob) URL.revokeObjectURL(r.styleBlob);
  for (const name of ['historyReady', 'historyActive', 'historySegments', 'historyStops', 'historyFit']) delete r.container.dataset[name];
  r.dotnet = null;
}

/** @param {Runtime} r @param {{ error?: unknown }} event */
function onMapError(r, event) {
  if (rt !== r) return;
  // A tile or a glyph that cannot be fetched (offline, blocked) is not a reason to fail the screen: the trail is drawn from data that is already here.
  console.warn(`[historyMap] ${String(event.error instanceof Error ? event.error.message : event.error).slice(0, 200)}`);
}

/** @param {Runtime} r */
function onStyleLoad(r) {
  if (rt !== r) return;
  window.clearTimeout(r.timer);
  r.styleReady = true;
  ensureLayers(r);
  apply(r);
}

/** @param {Runtime} r */
function onResize(r) {
  if (rt !== r) return;
  call('resize', () => {
    r.map.resize();
    // The first fit may have been made at a size that was not final (the panel was still settling): the day is fitted again until a person moves the camera or picks an entry.
    if (r.day && r.focus.id === null && r.fittedKey === r.day.key && !r.container.dataset.historyMoved) fitDay(r, false);
  });
}

// ---- layers -----------------------------------------------------------------------------------------------------------------------

/** Adds the trail's source and layers to the style that has just loaded (every style load starts without them). @param {Runtime} r */
function ensureLayers(r) {
  const { map } = r;
  if (!map.getSource(SOURCE)) {
    map.addSource(SOURCE, { type: 'geojson', data: emptyCollection() });
  }

  /** @type {any} */
  const color = ['coalesce', ['get', 'color'], 'white'];
  /** @type {any} */
  const casing = ['coalesce', ['get', 'casing'], 'black'];
  /** @type {any} */
  const dim = ['case', ['==', ['get', 'active'], 1], 1, ['==', ['get', 'dim'], 1], 0.4, 1];
  if (!map.getLayer(LAYER_CASING)) {
    map.addLayer({ id: LAYER_CASING, type: 'line', source: SOURCE, layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': casing, 'line-width': 8, 'line-opacity': dim } });
  }

  if (!map.getLayer(LAYER_SOLID)) {
    map.addLayer({ id: LAYER_SOLID, type: 'line', source: SOURCE, filter: ['==', ['get', 'dashed'], 0], layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': color, 'line-width': 4.5, 'line-opacity': dim } });
  }

  if (!map.getLayer(LAYER_DASHED)) {
    map.addLayer({ id: LAYER_DASHED, type: 'line', source: SOURCE, filter: ['==', ['get', 'dashed'], 1], layout: { 'line-join': 'round' }, paint: { 'line-color': color, 'line-width': 3.5, 'line-dasharray': [1.2, 1.8], 'line-opacity': dim } });
  }

  if (!map.getLayer(LAYER_ACTIVE)) {
    map.addLayer({ id: LAYER_ACTIVE, type: 'line', source: SOURCE, filter: ['==', ['get', 'active'], 1], layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': color, 'line-width': 8, 'line-opacity': 0.5 } });
  }
}

/** @returns {any} */
function emptyCollection() {
  return { type: 'FeatureCollection', features: [] };
}

/**
 * The trail as GeoJSON lines: one feature per segment, carrying the colours and whether it is the highlighted drive or dimmed by one.
 * @param {DayPayload} day
 * @param {string | null} activeId
 * @returns {any}
 */
export function trailCollection(day, activeId) {
  return {
    type: 'FeatureCollection',
    features: day.trail
      .filter((segment) => segment.points.length >= 2)
      .map((segment) => ({
        type: 'Feature',
        properties: {
          id: segment.id,
          dashed: segment.dashed ? 1 : 0,
          color: day.color,
          casing: day.casing,
          active: activeId !== null && segment.id === activeId ? 1 : 0,
          dim: activeId !== null && segment.id !== activeId ? 1 : 0,
        },
        geometry: { type: 'LineString', coordinates: segment.points },
      })),
  };
}

// ---- state -> map -----------------------------------------------------------------------------------------------------------------

/** Draws what is stored, once the style is ready. @param {Runtime} r */
function apply(r) {
  call('apply', () => {
    if (!r.styleReady) return;
    const day = r.day;
    /** @type {import('maplibre-gl').GeoJSONSource | undefined} */
    const source = /** @type {any} */ (r.map.getSource(SOURCE));
    source?.setData(day ? trailCollection(day, r.focus.kind === 'drive' ? r.focus.id : null) : emptyCollection());
    drawMarkers(r);
    r.container.dataset.historySegments = String(day ? day.trail.filter((segment) => segment.points.length >= 2).length : 0);
    r.container.dataset.historyStops = String(day ? day.stops.length : 0);
    r.container.dataset.historyActive = r.focus.id ?? '';
    if (day && r.fittedKey !== day.key) fitDay(r, true);
  });
}

/** @param {Runtime} r */
function drawMarkers(r) {
  const day = r.day;
  const wanted = new Map((day ? day.stops : []).map((stop) => [markerKey(stop), stop]));
  for (const [key, marker] of [...r.markers]) {
    if (!wanted.has(key)) {
      marker.remove();
      r.markers.delete(key);
    }
  }

  for (const [key, stop] of wanted) {
    let marker = r.markers.get(key);
    if (!marker) {
      marker = new (lib().Marker)({ element: markerElement(r, stop), anchor: 'center' }).setLngLat([stop.lon, stop.lat]).addTo(r.map);
      r.markers.set(key, marker);
    } else {
      marker.setLngLat([stop.lon, stop.lat]);
      updateMarkerElement(marker.getElement(), stop, r.day?.pickable === true);
    }

    const element = marker.getElement();
    const active = stop.id !== '' && r.focus.kind === 'stay' && r.focus.id === stop.id;
    element.classList.toggle('is-active', active);
    if (element instanceof HTMLButtonElement) element.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
}

/** @param {Stop} stop @returns {string} */
function markerKey(stop) {
  return stop.kind === 'stay' ? `stay:${stop.id}` : stop.kind;
}

/**
 * A stay is a button (a person can pick it, and the timeline follows); the two ends are plain pins.
 * @param {Runtime} r
 * @param {Stop} stop
 * @returns {HTMLElement}
 */
function markerElement(r, stop) {
  if (stop.kind !== 'stay') {
    const pin = document.createElement('span');
    pin.className = `realm-history-end realm-history-end--${stop.kind}`;
    pin.setAttribute('role', 'img');
    pin.dataset.testid = `history-end-${stop.kind}`;
    updateMarkerElement(pin, stop, false);
    return pin;
  }

  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'realm-history-stop';
  button.dataset.testid = `history-stop-${stop.id}`;
  button.addEventListener('click', (event) => {
    event.stopPropagation();
    if (rt === r && r.day?.pickable === true) send(r.dotnet, 'OnPick', [stop.id]);
  });
  updateMarkerElement(button, stop, r.day?.pickable === true);
  return button;
}

/** @param {HTMLElement} element @param {Stop} stop @param {boolean} pickable */
function updateMarkerElement(element, stop, pickable) {
  element.setAttribute('aria-label', stop.label);
  if (element instanceof HTMLButtonElement) {
    element.disabled = !pickable;
    element.textContent = stop.number === undefined ? '' : String(stop.number);
  }
}

/**
 * Fits the whole day: every trail point and every marker. A day with a single point is shown at a street-level zoom.
 * @param {Runtime} r
 * @param {boolean} first the day has just been set (the key is recorded)
 */
function fitDay(r, first) {
  const day = r.day;
  if (!day) return;
  /** @type {Array<[number, number]>} */
  const points = [];
  for (const segment of day.trail) for (const point of segment.points) points.push(point);
  for (const stop of day.stops) points.push([stop.lon, stop.lat]);
  if (first) {
    r.fittedKey = day.key;
    delete r.container.dataset.historyMoved;
  }

  const box = boundsOf(points);
  if (box) {
    const [[west, south], [east, north]] = box;
    const single = east - west < 1e-5 && north - south < 1e-5;
    if (single) r.map.jumpTo({ center: [west, south], zoom: STAY_ZOOM });
    else r.map.fitBounds(box, { padding: FIT_PADDING_PX, maxZoom: FIT_MAX_ZOOM, duration: first ? 0 : duration(r) });
  }

  r.container.dataset.historyFit = 'done';
}

// ---- exports ----------------------------------------------------------------------------------------------------------------------

/**
 * Shows a day: the trail, the markers and the fit. The same `key` again only refreshes the data (a refresh of the session), a new key fits the camera to the day.
 * @param {DayPayload | null} day null empties the map
 */
export function setDay(day) {
  call('setDay', () => {
    const r = rt;
    if (!r) return;
    const changed = !day || !r.day || r.day.key !== day.key;
    r.day = day;
    if (changed) {
      r.focus = { id: null, kind: null };
      r.fittedKey = null;
      r.container.dataset.historyFit = 'pending';
    }

    apply(r);
  });
}

/**
 * Highlights an entry of the timeline and moves the camera to it: a drive's path is fitted into the map, a visit is centred. A null id clears the highlight and fits the day again.
 * @param {Focus} focus
 */
export function focusEntry(focus) {
  call('focusEntry', () => {
    const r = rt;
    if (!r) return;
    r.focus = focus && focus.id ? { id: focus.id, kind: focus.kind } : { id: null, kind: null };
    apply(r);
    const day = r.day;
    if (!day || !r.styleReady) return;
    if (r.focus.id === null) {
      fitDay(r, false);
      return;
    }

    if (r.focus.kind === 'drive') {
      /** @type {Array<[number, number]>} */
      const points = [];
      for (const segment of day.trail) if (segment.id === r.focus.id) for (const point of segment.points) points.push(point);
      const box = boundsOf(points);
      if (box) r.map.fitBounds(box, { padding: FIT_PADDING_PX, maxZoom: FIT_MAX_ZOOM, duration: duration(r) });
    } else {
      const stop = day.stops.find((candidate) => candidate.kind === 'stay' && candidate.id === r.focus.id);
      if (stop) r.map.easeTo({ center: [stop.lon, stop.lat], zoom: Math.max(r.map.getZoom(), STAY_ZOOM), duration: duration(r) });
    }
  });
}

/** Tells the map its element changed size (the sheet was expanded or collapsed). */
export function resize() {
  call('resize', () => {
    if (rt) onResize(rt);
  });
}

// ---- picking ----------------------------------------------------------------------------------------------------------------------

/** A tap on a trail line picks its drive; a tap on empty map clears the highlight. @param {Runtime} r @param {import('maplibre-gl').MapMouseEvent} event */
function onMapClick(r, event) {
  if (rt !== r || !r.styleReady) return;
  call('click', () => {
    if (r.day?.pickable !== true) return;
    const target = event.originalEvent?.target;
    if (target instanceof Element && target.closest('.realm-history-stop')) return;
    const { x, y } = event.point;
    const layers = PICK_LAYERS.filter((id) => r.map.getLayer(id));
    const hits = layers.length > 0 ? r.map.queryRenderedFeatures([[x - PICK_SLOP_PX, y - PICK_SLOP_PX], [x + PICK_SLOP_PX, y + PICK_SLOP_PX]], { layers }) : [];
    const id = hits.length > 0 ? String(hits[0].properties?.id ?? '') : '';
    send(r.dotnet, 'OnPick', [id]);
  });
}

/**
 * Scrolls a list to a position: the timeline, when the entry that was picked on the map is not in view.
 * @param {string} selector
 * @param {number} top
 */
export function scrollListTo(selector, top) {
  call('scrollListTo', () => {
    const element = document.querySelector(selector);
    if (element instanceof HTMLElement) element.scrollTo({ top: Math.max(0, top), behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
  });
}
