// Tests for wwwroot/js/geo.js: the geometry helpers of the map (03 section 4.1, research map-stack 1.4).
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { EARTH_RADIUS_M, TILE_SIZE_PX, boundsOf, circlePolygon, growBounds, metersPerPixel } from '../../src/Realm.Web/wwwroot/js/geo.js';
import { haversine } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';

// The demo cast of 02 section 9.3 (fictional): [lon, lat].
const KING = [-85.341, 31.099];
const QUEEN = [-85.4647, 31.056];
const CRYPTID = [-82.7291, 31.3382];
const PRINCE = [-92.8214, 38.8339];

const closeTo = (actual, expected, tolerance, message) => assert.ok(Math.abs(actual - expected) <= tolerance, `${message ?? 'value'}: ${actual} is not within ${tolerance} of ${expected}`);

test('circlePolygon: every vertex is within half a metre of the radius (100 m and 5 km, four latitudes)', () => {
  for (const center of [KING, QUEEN, CRYPTID, PRINCE]) {
    for (const radius of [100, 5000]) {
      const [ring] = circlePolygon(center, radius).geometry.coordinates;
      for (const vertex of ring) closeTo(haversine(center, vertex), radius, 0.5, `${radius} m around ${center}`);
    }
  }
});

test('circlePolygon: a closed ring of 64 steps plus the closing vertex, a Polygon feature', () => {
  const feature = circlePolygon(KING, 250);
  assert.equal(feature.type, 'Feature');
  assert.equal(feature.geometry.type, 'Polygon');
  assert.equal(feature.geometry.coordinates.length, 1);
  const [ring] = feature.geometry.coordinates;
  assert.equal(ring.length, 65);
  assert.deepEqual(ring[0], ring[64]);
  assert.equal(new Set(ring.slice(0, 64).map(([lon, lat]) => `${lon},${lat}`)).size, 64, 'no repeated vertex');
});

test('circlePolygon: the steps argument sets the vertex count', () => {
  assert.equal(circlePolygon(KING, 250, 128).geometry.coordinates[0].length, 129);
  assert.equal(circlePolygon(KING, 250, 8).geometry.coordinates[0].length, 9);
});

test('circlePolygon: vertex 0 is due north and vertex 32 due south, 2r apart', () => {
  const [ring] = circlePolygon(KING, 1000).geometry.coordinates;
  closeTo(ring[0][0], KING[0], 1e-9, 'north vertex longitude');
  assert.ok(ring[0][1] > KING[1]);
  closeTo(ring[32][0], KING[0], 1e-9, 'south vertex longitude');
  assert.ok(ring[32][1] < KING[1]);
  closeTo(haversine(ring[0], ring[32]), 2000, 1, 'north to south');
});

test('circlePolygon: properties are copied into the feature, not shared', () => {
  const properties = { id: 'forge', occupied: true };
  const feature = circlePolygon(KING, 100, 64, properties);
  assert.deepEqual(feature.properties, { id: 'forge', occupied: true });
  assert.notEqual(feature.properties, properties);
  assert.deepEqual(circlePolygon(KING, 100).properties, {});
});

test('circlePolygon: longitudes stay in [-180, 180) across the antimeridian', () => {
  for (const lon of [179.9995, -179.9995, 180]) {
    const [ring] = circlePolygon([lon, 10], 500).geometry.coordinates;
    for (const [vertexLon] of ring) assert.ok(vertexLon >= -180 && vertexLon < 180, `longitude ${vertexLon} around ${lon}`);
  }
});

test('boundsOf: the box of the points in [[west, south], [east, north]] order; null without points', () => {
  assert.equal(boundsOf([]), null);
  assert.deepEqual(boundsOf([KING]), [KING, KING]);
  assert.deepEqual(boundsOf([KING, QUEEN, CRYPTID]), [[-85.4647, 31.056], [-82.7291, 31.3382]]);
  assert.deepEqual(boundsOf([[-1, 5], [3, -4], [2, 9]]), [[-1, -4], [3, 9]]);
});

test('growBounds: a ratio of 0.2 grows width and height by 20 percent about the centre', () => {
  const [[west, south], [east, north]] = growBounds([[0, 0], [10, 4]], 0.2);
  closeTo(west, -1, 1e-12);
  closeTo(east, 11, 1e-12);
  closeTo(south, -0.4, 1e-12);
  closeTo(north, 4.4, 1e-12);
  assert.deepEqual(growBounds([[0, 0], [10, 4]], 0), [[0, 0], [10, 4]]);
  assert.deepEqual(growBounds([[0, 0], [10, 4]], -0.5), [[2.5, 1], [7.5, 3]]);
});

test('metersPerPixel: 512 px world, halves per zoom level, shrinks with the cosine of the latitude', () => {
  assert.equal(TILE_SIZE_PX, 512);
  closeTo(metersPerPixel(0, 0), (2 * Math.PI * EARTH_RADIUS_M) / 512, 1e-9, 'zoom 0 equator');
  closeTo(metersPerPixel(0, 0), 78184.04, 0.01, 'zoom 0 equator in metres');
  closeTo(metersPerPixel(15, 0), metersPerPixel(14, 0) / 2, 1e-9, 'one zoom level');
  closeTo(metersPerPixel(12, 60), metersPerPixel(12, 0) / 2, 1e-9, 'sixty degrees');
  closeTo(metersPerPixel(16, KING[1]), 1.0214, 0.0005, 'zoom 16 at the fixture latitude');
});

test('EARTH_RADIUS_M is the sphere MapLibre itself uses', () => {
  assert.equal(EARTH_RADIUS_M, 6371008.8);
});
