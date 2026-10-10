// Tests for wwwroot/js/historyMap.js: the pure part that turns a day payload into the GeoJSON the map draws (Location History, 0.3.0). The map itself is covered by the Playwright spec.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { trailCollection } from '../../src/Realm.Web/wwwroot/js/historyMap.js';

const day = {
  key: 'king|2026-09-29',
  color: 'var-free-colour',
  casing: 'white',
  stops: [],
  pickable: true,
  trail: [
    { id: 'd-1', dashed: false, points: [[-85.34, 31.1], [-85.35, 31.11], [-85.36, 31.12]] },
    { id: 'd-2', dashed: true, points: [[-85.4, 31.15], [-85.41, 31.16]] },
    { id: 'd-3', dashed: false, points: [[-85.5, 31.2]] },
  ],
};

test('trailCollection: one line per segment with two or more points, in order', () => {
  const collection = trailCollection(day, null);
  assert.equal(collection.type, 'FeatureCollection');
  assert.deepEqual(collection.features.map((f) => f.properties.id), ['d-1', 'd-2']);
  assert.ok(collection.features.every((f) => f.geometry.type === 'LineString'));
  assert.deepEqual(collection.features[0].geometry.coordinates, day.trail[0].points);
});

test('trailCollection: dashed segments are marked, every feature carries the colours', () => {
  const [solid, dashed] = trailCollection(day, null).features;
  assert.equal(solid.properties.dashed, 0);
  assert.equal(dashed.properties.dashed, 1);
  assert.equal(solid.properties.color, day.color);
  assert.equal(solid.properties.casing, 'white');
});

test('trailCollection: with no active drive nothing is active or dimmed', () => {
  assert.ok(trailCollection(day, null).features.every((f) => f.properties.active === 0 && f.properties.dim === 0));
});

test('trailCollection: the active drive is highlighted and the others are dimmed', () => {
  const [first, second] = trailCollection(day, 'd-2').features;
  assert.equal(first.properties.active, 0);
  assert.equal(first.properties.dim, 1);
  assert.equal(second.properties.active, 1);
  assert.equal(second.properties.dim, 0);
});

test('trailCollection: a day with no trail is an empty collection', () => {
  assert.deepEqual(trailCollection({ ...day, trail: [] }, null).features, []);
});
