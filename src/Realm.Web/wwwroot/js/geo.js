// @ts-check
// geo.js: pure geometry helpers of the map (03 section 4.1). No DOM, no MapLibre: Node tests import this file directly.
// Coordinates are [lon, lat] in degrees (WGS84) and bounds are [[west, south], [east, north]], the MapLibre order (03 section 4.2).

/** @typedef {[number, number]} LngLat */
/** @typedef {[LngLat, LngLat]} Bounds */
/** @typedef {{ type: 'Feature', properties: Record<string, unknown>, geometry: { type: 'Polygon', coordinates: LngLat[][] } }} PolygonFeature */

/** The sphere MapLibre itself uses (mercator_coordinate.ts), so metres here and metres on the map agree. */
export const EARTH_RADIUS_M = 6371008.8;

/** MapLibre's world is 512 px wide at zoom 0 (not the 256 px of Leaflet and OSM). */
export const TILE_SIZE_PX = 512;

const rad = (/** @type {number} */ degrees) => (degrees * Math.PI) / 180;
const deg = (/** @type {number} */ radians) => (radians * 180) / Math.PI;

/**
 * A geodesic circle as a closed GeoJSON polygon (research map-stack 1.4): every vertex lies `radiusM` metres from `center`
 * on the sphere of {@link EARTH_RADIUS_M}.
 * @param {LngLat} center
 * @param {number} radiusM
 * @param {number} [steps] 64 for the zones of up to 5 km (128 above, never reached in v1)
 * @param {Record<string, unknown>} [properties]
 * @returns {PolygonFeature}
 */
export function circlePolygon(center, radiusM, steps = 64, properties = {}) {
  const [lon, lat] = center;
  const d = radiusM / EARTH_RADIUS_M;
  const phi1 = rad(lat);
  const lambda1 = rad(lon);
  /** @type {LngLat[]} */
  const ring = [];
  for (let i = 0; i < steps; i += 1) {
    const theta = (2 * Math.PI * i) / steps;
    const phi2 = Math.asin(Math.sin(phi1) * Math.cos(d) + Math.cos(phi1) * Math.sin(d) * Math.cos(theta));
    const lambda2 = lambda1 + Math.atan2(Math.sin(theta) * Math.sin(d) * Math.cos(phi1), Math.cos(d) - Math.sin(phi1) * Math.sin(phi2));
    ring.push([((deg(lambda2) + 540) % 360) - 180, deg(phi2)]);
  }
  ring.push([ring[0][0], ring[0][1]]);
  return { type: 'Feature', properties: { ...properties }, geometry: { type: 'Polygon', coordinates: [ring] } };
}

/**
 * The bounding box of some points, or null without any.
 * @param {ReadonlyArray<LngLat>} points
 * @returns {Bounds | null}
 */
export function boundsOf(points) {
  if (points.length === 0) return null;
  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;
  for (const [lon, lat] of points) {
    if (lon < west) west = lon;
    if (lon > east) east = lon;
    if (lat < south) south = lat;
    if (lat > north) north = lat;
  }
  return [[west, south], [east, north]];
}

/**
 * Scales a box about its centre so that its width and height grow by `ratio` (0.2 makes a box 20 % wider and 20 % taller, which
 * is 10 % of the old size on each side). A ratio below 0 shrinks it.
 * @param {Bounds} bounds
 * @param {number} ratio
 * @returns {Bounds}
 */
export function growBounds(bounds, ratio) {
  const [[west, south], [east, north]] = bounds;
  const dLon = ((east - west) * ratio) / 2;
  const dLat = ((north - south) * ratio) / 2;
  return [[west - dLon, south - dLat], [east + dLon, north + dLat]];
}

/**
 * Metres on the ground that one CSS pixel covers at a MapLibre zoom and latitude (Web Mercator, 512 px world).
 * @param {number} zoom
 * @param {number} latDeg
 * @returns {number}
 */
export function metersPerPixel(zoom, latDeg) {
  return (2 * Math.PI * EARTH_RADIUS_M * Math.cos(rad(latDeg))) / (TILE_SIZE_PX * 2 ** zoom);
}
