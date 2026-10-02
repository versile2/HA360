// @ts-check
// mapStyles.js: the map style registry, the styles built in JavaScript and the overlay (zones and accuracy halos) that is
// injected into every style (03 section 4.9). Pure data and functions: no DOM and no MapLibre import, so Node can load this file
// (tools/ci/validate-styles.mjs validates what it builds).
//
// This is the only script that holds colour literals (guards.allow.json): a MapLibre style is a JSON object and cannot read the
// CSS variables of tokens.css. The overlay itself is data-driven: zone and halo colours arrive with each feature from the
// payloads (RealmPalette through C#), so only the neutral demo-offline paper, its graticule and the imagery casing live here.

/** @typedef {import('maplibre-gl').StyleSpecification} StyleSpecification */
/** @typedef {import('maplibre-gl').LayerSpecification} LayerSpecification */
/** @typedef {import('maplibre-gl').GeoJSONSourceSpecification} GeoJSONSourceSpecification */
/** @typedef {'night' | 'day' | 'streets' | 'satellite' | 'demo-offline'} StyleId */
/** @typedef {'dark' | 'light' | 'imagery'} Appearance */
/** @typedef {{ id: StyleId, name: string, appearance: Appearance, url: string | null, hidden: boolean }} StyleInfo */

/** The style a fresh install shows (dark is the only theme in v1, 01 section 4.12). */
export const DEFAULT_STYLE_ID = 'night';

/** The fixture centre [lon, lat]: the graticule of demo-offline is drawn within one degree of it (02 section 9.3). */
export const FIXTURE_CENTER = Object.freeze(/** @type {[number, number]} */ ([-85.341, 31.099]));

/** The three OpenFreeMap styles share sprite, glyph and source definitions, so switching between them is a cheap diff. */
const OPENFREEMAP = 'https://tiles.openfreemap.org/styles';

/** USGS Imagery Only (the National Map): public domain, no key, no CORS promise (UNVERIFIED, box-smoke item R-098). */
const USGS_TILES = 'https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}';
const USGS_ATTRIBUTION = 'USGS The National Map';
/** The United States with Alaska, Hawaii and the territories, [west, south, east, north]; tiles outside it are not requested. */
const USGS_BOUNDS = /** @type {[number, number, number, number]} */ ([-179.3, 17.6, -64.5, 71.5]);

/** @type {Readonly<Record<StyleId, StyleInfo>>} */
export const STYLES = Object.freeze({
  night: { id: 'night', name: 'Night', appearance: 'dark', url: `${OPENFREEMAP}/dark`, hidden: false },
  day: { id: 'day', name: 'Day', appearance: 'light', url: `${OPENFREEMAP}/positron`, hidden: false },
  streets: { id: 'streets', name: 'Streets', appearance: 'light', url: `${OPENFREEMAP}/liberty`, hidden: false },
  satellite: { id: 'satellite', name: 'Satellite', appearance: 'imagery', url: null, hidden: false },
  'demo-offline': { id: 'demo-offline', name: 'Demo', appearance: 'light', url: null, hidden: true },
});

/** The ids in registry order. */
export const STYLE_IDS = /** @type {ReadonlyArray<StyleId>} */ (Object.freeze(Object.keys(STYLES)));

/**
 * @param {unknown} value
 * @returns {value is StyleId}
 */
export function isStyleId(value) {
  return typeof value === 'string' && Object.prototype.hasOwnProperty.call(STYLES, value);
}

/**
 * The appearance that drives the zone colours; an unknown id counts as dark (the default).
 * @param {string} styleId
 * @returns {Appearance}
 */
export function appearanceOf(styleId) {
  return isStyleId(styleId) ? STYLES[styleId].appearance : 'dark';
}

// ---- the map credits (D81, the OSMF Attribution Guidelines) --------------------------------------------------------------

/** How long the credits of a style with third-party data stay open once the map has loaded, when nobody touches the map first. */
export const ATTRIBUTION_FOLD_MS = 5000;

/**
 * Whether the map credits start expanded. Every style that draws third-party data (OpenFreeMap with OpenStreetMap, USGS) shows them open
 * when the map opens; only demo-offline, which draws none, starts as the (i) button (D81). An unknown id counts as third-party data: showing
 * the credits is the safe side.
 * @param {string} styleId
 * @returns {boolean}
 */
export function attributionStartsExpanded(styleId) {
  return styleId !== 'demo-offline';
}

/**
 * The one-way fold of the credits from open to the (i) button, driven by its caller and by a timer the caller supplies (so Node can test it
 * with a fake clock; this file never reads the wall clock). `arm()` starts the countdown, `fold()` is the first map interaction, `release()`
 * is the person using the (i) button themselves and `dispose()` is the map going away. Whichever comes first ends it: after that every call
 * does nothing, so the countdown can never close credits the person has opened again.
 * @param {{ schedule: (callback: () => void, delayMs: number) => unknown, cancel: (handle: unknown) => void, collapse: () => void, delayMs?: number }} deps
 * @returns {{ arm: () => void, fold: () => void, release: () => void, dispose: () => void, isDone: () => boolean }}
 */
export function createAttributionFold({ schedule, cancel, collapse, delayMs = ATTRIBUTION_FOLD_MS }) {
  /** @type {unknown} */
  let handle = null;
  let done = false;

  /** Ends the fold without collapsing anything: the countdown is cancelled and nothing else will run. */
  function finish() {
    done = true;
    if (handle !== null) cancel(handle);
    handle = null;
  }

  return {
    arm() {
      if (done || handle !== null) return;
      handle = schedule(() => {
        if (done) return; // a callback that was already queued when the fold ended some other way
        handle = null; // fired: nothing left to cancel
        finish();
        collapse();
      }, delayMs);
    },
    fold() {
      if (done) return;
      finish();
      collapse();
    },
    release: finish,
    dispose: finish,
    isDone: () => done,
  };
}

// ---- demo-offline: no network at all ------------------------------------------------------------------------------------

const DEMO_PAPER = '#F1ECE0';
const DEMO_GRATICULE_MINOR = '#DDD6C4';
const DEMO_GRATICULE_MAJOR = '#BFB69C';
/** Degrees on either side of the fixture centre that carry graticule lines. */
const DEMO_RADIUS_DEG = 1;

/**
 * Meridians and parallels every 0.01 degrees within one degree of `center`, `major` on every tenth (so every 0.1 degrees).
 * Whole hundredths are counted as integers, so a line sits exactly on its latitude or longitude and the major test is exact.
 * @param {ReadonlyArray<number>} [center] [lon, lat]
 * @returns {{ type: 'FeatureCollection', features: Array<{ type: 'Feature', properties: { major: boolean }, geometry: { type: 'LineString', coordinates: number[][] } }> }}
 */
export function demoGraticule(center = FIXTURE_CENTER) {
  const [lon, lat] = center;
  const west = Math.floor((lon - DEMO_RADIUS_DEG) * 100);
  const east = Math.ceil((lon + DEMO_RADIUS_DEG) * 100);
  const south = Math.floor((lat - DEMO_RADIUS_DEG) * 100);
  const north = Math.ceil((lat + DEMO_RADIUS_DEG) * 100);
  /** @type {Array<{ type: 'Feature', properties: { major: boolean }, geometry: { type: 'LineString', coordinates: number[][] } }>} */
  const features = [];
  for (let x = west; x <= east; x += 1) {
    features.push({ type: 'Feature', properties: { major: x % 10 === 0 }, geometry: { type: 'LineString', coordinates: [[x / 100, south / 100], [x / 100, north / 100]] } });
  }
  for (let y = south; y <= north; y += 1) {
    features.push({ type: 'Feature', properties: { major: y % 10 === 0 }, geometry: { type: 'LineString', coordinates: [[west / 100, y / 100], [east / 100, y / 100]] } });
  }
  return { type: 'FeatureCollection', features };
}

/**
 * @param {string} [attribution] the `demoAttribution` string of init
 * @returns {StyleSpecification}
 */
function demoOfflineStyle(attribution) {
  /** @type {GeoJSONSourceSpecification} */
  const graticule = { type: 'geojson', data: /** @type {any} */ (demoGraticule()) };
  if (attribution) graticule.attribution = attribution;
  return {
    version: 8,
    name: 'realm-demo-offline',
    sources: { 'demo-graticule': graticule },
    layers: [
      { id: 'demo-paper', type: 'background', paint: { 'background-color': DEMO_PAPER } },
      { id: 'demo-graticule-minor', type: 'line', source: 'demo-graticule', filter: ['!=', ['get', 'major'], true], paint: { 'line-color': DEMO_GRATICULE_MINOR, 'line-width': 0.6 } },
      { id: 'demo-graticule-major', type: 'line', source: 'demo-graticule', filter: ['==', ['get', 'major'], true], paint: { 'line-color': DEMO_GRATICULE_MAJOR, 'line-width': 1.4 } },
    ],
  };
}

/** @returns {StyleSpecification} */
function satelliteStyle() {
  return {
    version: 8,
    name: 'realm-satellite',
    sources: {
      'usgs-imagery': {
        type: 'raster',
        tiles: [USGS_TILES],
        tileSize: 256,
        maxzoom: 16,
        bounds: USGS_BOUNDS,
        attribution: USGS_ATTRIBUTION,
      },
    },
    layers: [{ id: 'usgs-imagery', type: 'raster', source: 'usgs-imagery' }],
  };
}

/**
 * The base style of an id: the URL of an OpenFreeMap style (MapLibre fetches it) or a style object built here.
 * Overlay sources and layers are not part of it; {@link transformStyle} and {@link overlaySpec} add them.
 * @param {StyleId} styleId
 * @param {{ demoAttribution?: string }} [opts]
 * @returns {string | StyleSpecification}
 */
export function buildStyle(styleId, opts = {}) {
  const info = STYLES[styleId];
  if (!info) throw new Error(`unknown style id '${String(styleId)}'`);
  if (info.url) return info.url;
  return styleId === 'satellite' ? satelliteStyle() : demoOfflineStyle(opts.demoAttribution);
}

// ---- the overlay: accuracy halos and zone circles ---------------------------------------------------------------------------

export const HALO_SOURCE = 'realm-halos';
export const ZONE_SOURCE = 'realm-zones';
export const HALO_LAYERS = Object.freeze(['realm-halos-fill', 'realm-halos-line']);
export const ZONE_LAYERS = Object.freeze(['realm-zones-fill', 'realm-zones-casing', 'realm-zones-line-empty', 'realm-zones-line-occupied']);

/** The dark 1 px casing under a zone outline on imagery (01 section 4.6); opacity 0 on every other style. */
const CASING_COLOR = '#0B0E1F';
/** What a feature without a colour property draws in (a payload that lost a field must not paint black). */
export const FALLBACK_COLOR = '#E8BC4E';

/** The zone appearance used when a payload carries none for the active style: the dark-style values of 01 section 4.6. */
export const FALLBACK_ZONE_APPEARANCE = Object.freeze({ lineColor: FALLBACK_COLOR, fillAlpha: 0.1, fillAlphaOccupied: 0.22, casing: false });

/** @type {(key: string) => import('maplibre-gl').ExpressionSpecification} */
const featureColor = (key) => ['coalesce', ['get', key], FALLBACK_COLOR];

/**
 * The overlay's sources (empty collections) and layers, bottom to top. Zone and halo features carry their own colours and
 * alphas, so the layers do not depend on the active style; `line-dasharray` is not data-driven, hence one line layer for
 * empty (dashed) zones and one for occupied (solid) zones.
 * @returns {{ sources: Record<string, GeoJSONSourceSpecification>, layers: LayerSpecification[] }}
 */
export function overlaySpec() {
  /** @type {any} */
  const empty = { type: 'FeatureCollection', features: [] };
  return {
    sources: {
      [HALO_SOURCE]: { type: 'geojson', data: { ...empty } },
      [ZONE_SOURCE]: { type: 'geojson', data: { ...empty } },
    },
    layers: [
      { id: 'realm-halos-fill', type: 'fill', source: HALO_SOURCE, paint: { 'fill-color': featureColor('color'), 'fill-opacity': 0.12 } },
      { id: 'realm-halos-line', type: 'line', source: HALO_SOURCE, paint: { 'line-color': featureColor('color'), 'line-width': 1.5, 'line-dasharray': [3, 2] } },
      { id: 'realm-zones-fill', type: 'fill', source: ZONE_SOURCE, paint: { 'fill-color': featureColor('lineColor'), 'fill-opacity': ['coalesce', ['get', 'fillAlpha'], 0.1] } },
      {
        id: 'realm-zones-casing',
        type: 'line',
        source: ZONE_SOURCE,
        layout: { 'line-join': 'round' },
        paint: { 'line-color': CASING_COLOR, 'line-width': 4, 'line-opacity': ['case', ['==', ['get', 'casing'], true], 0.85, 0] },
      },
      {
        id: 'realm-zones-line-empty',
        type: 'line',
        source: ZONE_SOURCE,
        filter: ['!=', ['get', 'occupied'], true],
        layout: { 'line-join': 'round' },
        paint: { 'line-color': featureColor('lineColor'), 'line-width': 2, 'line-dasharray': [2, 2] },
      },
      {
        id: 'realm-zones-line-occupied',
        type: 'line',
        source: ZONE_SOURCE,
        filter: ['==', ['get', 'occupied'], true],
        layout: { 'line-join': 'round' },
        paint: { 'line-color': featureColor('lineColor'), 'line-width': 2 },
      },
    ],
  };
}

/**
 * Appends the overlay to a style, reusing the live GeoJSON of `prev` (the style being replaced, when it carries the overlay) so
 * the circles do not flash empty while a style switches. Idempotent: sources and layers that `next` already has are kept.
 * Pass this as the `transformStyle` option of `map.setStyle`; it returns a new object and leaves `next` untouched.
 * @param {StyleSpecification | undefined} prev
 * @param {StyleSpecification} next
 * @returns {StyleSpecification}
 */
export function transformStyle(prev, next) {
  const overlay = overlaySpec();
  const sources = { ...next.sources };
  for (const [id, source] of Object.entries(overlay.sources)) {
    if (sources[id]) continue;
    const live = prev?.sources?.[id];
    sources[id] = live && live.type === 'geojson' ? { ...source, data: live.data ?? source.data } : source;
  }
  const have = new Set(next.layers.map((layer) => layer.id));
  return { ...next, sources, layers: [...next.layers, ...overlay.layers.filter((layer) => !have.has(layer.id))] };
}
