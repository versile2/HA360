// Tests for wwwroot/js/fanout.js: pin fan-out (01 section 4.8, 03 section 4.6). The 48 px shift is also AC-16's "drawn 48 px (+-2) to the
// right of its true anchor"; the 36 px threshold and "no leader line in v1" are AC-19's fan-out sentence (D35, C-10).
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FAN_DEFAULTS, FAN_PRIORITY, fanOut } from '../../src/Realm.Web/wwwroot/js/fanout.js';
import { metersPerPixel } from '../../src/Realm.Web/wwwroot/js/geo.js';
import { haversine } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';

const KING = [-85.341, 31.099]; // 01 Appendix A.1
const WAGON = [-85.341, 31.09907]; // 02 section 9.3: 7.78 m from the king, inside Hearth Haven

const item = (id, x, y, priority) => ({ id, x, y, priority });
const dxOf = (results, id) => results.find((r) => r.id === id)?.dx;

test('[AC-16][AC-19] two markers on one point: the lower priority shifts exactly 48 px to the right, the other stays', () => {
  const results = fanOut([item('king', 100, 200, FAN_PRIORITY.member), item('wagon', 100, 200, FAN_PRIORITY.vehicle)]);
  assert.deepEqual(results, [
    { id: 'king', dx: 0, dy: 0, fanned: false },
    { id: 'wagon', dx: 48, dy: 0, fanned: true },
  ]);
});

test('[AC-16] the fixture: the pickup, 7.8 m from the king, is a fraction of a pixel away at the default zoom, so it fans out 48 px to the right', () => {
  const metres = haversine(KING, WAGON);
  assert.ok(Math.abs(metres - 7.78) < 0.1, `${metres} m`);
  const pixels = metres / metersPerPixel(10.85, KING[1]); // the default camera at 412 x 915 Peek is about zoom 10.85 (MapLibre's 512 px world)
  assert.ok(pixels < 1, `${pixels} px`);
  // the pickup is 7.78 m north of the king: its anchor is `pixels` above his
  const results = fanOut([item('wagon', 340, 368.4 - pixels, FAN_PRIORITY.vehicle), item('king', 340, 368.4, FAN_PRIORITY.member)]);
  assert.equal(dxOf(results, 'king'), 0);
  assert.equal(dxOf(results, 'wagon'), 48);
});

test('[AC-19] the threshold is 36 px and exclusive: 35.9 px apart fan out, exactly 36 px apart do not', () => {
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 35.9, 0, 100)]), 'b'), 48);
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 36, 0, 100)]), 'b'), 0);
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 40, 0, 100)]), 'b'), 0);
  // the distance is Euclidean, not per axis: 25 px right and 25 px down is 35.36 px
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 25, 25, 100)]), 'b'), 48);
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 26, 26, 100)]), 'b'), 0);
});

test('[AC-19] the shift disappears as soon as the true points are 36 px or more apart (recomputed from scratch each frame)', () => {
  const close = fanOut([item('a', 0, 0, 200), item('b', 20, 0, 100)]);
  assert.equal(dxOf(close, 'b'), 48);
  const apart = fanOut([item('a', 0, 0, 200), item('b', 36, 0, 100)]);
  assert.deepEqual(apart, [
    { id: 'a', dx: 0, dy: 0, fanned: false },
    { id: 'b', dx: 0, dy: 0, fanned: false },
  ]);
});

test('[AC-16] each further marker steps another 48 px: 0, 48, 96, 144', () => {
  const results = fanOut([item('a', 50, 50, 400), item('b', 50, 50, 300), item('c', 50, 50, 200), item('d', 50, 50, 100)]);
  assert.deepEqual(results.map((r) => r.dx), [0, 48, 96, 144]);
  assert.deepEqual(results.map((r) => r.fanned), [false, true, true, true]);
});

test('the group is the already-processed markers near THIS marker\'s true anchor: a chain of near neighbours does not stack', () => {
  // a, b and c are 30 px apart in a row: b is near a (48), c is near b but not a (also 48, since its true point is 60 px from a's)
  const results = fanOut([item('a', 0, 0, 300), item('b', 30, 0, 200), item('c', 60, 0, 100)]);
  assert.deepEqual(results.map((r) => r.dx), [0, 48, 48]);
});

test('priority decides who stays: selected, then a driving member, then a member, then a vehicle; ties go to the lower id', () => {
  assert.ok(FAN_PRIORITY.selected > FAN_PRIORITY.drivingMember);
  assert.ok(FAN_PRIORITY.drivingMember > FAN_PRIORITY.member);
  assert.ok(FAN_PRIORITY.member > FAN_PRIORITY.vehicle);
  assert.deepEqual({ ...FAN_PRIORITY }, { selected: 1000, drivingMember: 300, member: 200, vehicle: 100 });

  const all = fanOut([
    item('wagon', 10, 10, FAN_PRIORITY.vehicle),
    item('cass', 10, 10, FAN_PRIORITY.member),
    item('briar', 10, 10, FAN_PRIORITY.drivingMember),
    item('alden', 10, 10, FAN_PRIORITY.selected),
  ]);
  assert.deepEqual(Object.fromEntries(all.map((r) => [r.id, r.dx])), { alden: 0, briar: 48, cass: 96, wagon: 144 });

  const tie = fanOut([item('b', 0, 0, 200), item('a', 0, 0, 200)]);
  assert.deepEqual(Object.fromEntries(tie.map((r) => [r.id, r.dx])), { a: 0, b: 48 });
});

test('[AC-19] no leader-line geometry in v1: a result is {id, dx, dy, fanned} and dy is always 0', () => {
  const results = fanOut([item('a', 0, 0, 200), item('b', 0, 0, 100), item('c', 500, 500, 100)]);
  for (const result of results) {
    assert.deepEqual(Object.keys(result).sort(), ['dx', 'dy', 'fanned', 'id']);
    assert.strictEqual(result.dy, 0);
    assert.equal(result.fanned, result.dx > 0);
  }
});

test('markers far apart and a single marker are never shifted', () => {
  assert.deepEqual(fanOut([item('a', 0, 0, 200)]), [{ id: 'a', dx: 0, dy: 0, fanned: false }]);
  assert.deepEqual(fanOut([]), []);
  assert.deepEqual(fanOut([item('a', 0, 0, 200), item('b', 100, 100, 100)]).map((r) => r.dx), [0, 0]);
});

test('results keep the order of the input, the input is not mutated, and the input order does not change the shifts', () => {
  const items = Object.freeze([
    Object.freeze(item('wagon', 5, 5, 100)),
    Object.freeze(item('king', 5, 5, 200)),
    Object.freeze(item('cass', 300, 5, 200)),
  ]);
  const forward = fanOut(items);
  assert.deepEqual(forward.map((r) => r.id), ['wagon', 'king', 'cass']);
  assert.deepEqual(forward.map((r) => r.dx), [48, 0, 0]);
  const reversed = fanOut([...items].reverse());
  assert.deepEqual(Object.fromEntries(reversed.map((r) => [r.id, r.dx])), Object.fromEntries(forward.map((r) => [r.id, r.dx])));
  assert.deepEqual(fanOut(items), forward);
});

test('the defaults are 36 px and 48 px, and the options override them', () => {
  assert.deepEqual({ ...FAN_DEFAULTS }, { threshold: 36, step: 48 });
  const wide = fanOut([item('a', 0, 0, 200), item('b', 50, 0, 100)], { threshold: 60, step: 10 });
  assert.equal(dxOf(wide, 'b'), 10);
  const narrow = fanOut([item('a', 0, 0, 200), item('b', 20, 0, 100)], { threshold: 10 });
  assert.equal(dxOf(narrow, 'b'), 0);
  assert.equal(dxOf(fanOut([item('a', 0, 0, 200), item('b', 0, 0, 100)], { step: 12 }), 'b'), 12);
});

test('a marker with a non-finite anchor is never grouped with another (it just stays put)', () => {
  const results = fanOut([item('a', 0, 0, 200), item('nan', Number.NaN, 0, 100)]);
  assert.deepEqual(results.map((r) => r.dx), [0, 0]);
});
