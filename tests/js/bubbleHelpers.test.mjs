// Tests for the S9b helpers of wwwroot/js/testHooks.js: what the edge bubbles and the fan-out of realmMap.js decide from numbers and strings alone
// (01 sections 4.8 and 4.10, 03 sections 4.6 and 4.10). Titles carry the acceptance criteria they cover. No test here names a place: positions are
// pixels or synthetic numbers, so the demo fixture can move (D82) without touching this file.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { layoutBubbles } from '../../src/Realm.Web/wwwroot/js/bubbleLayout.js';
import { FAN_DEFAULTS, FAN_PRIORITY, fanOut } from '../../src/Realm.Web/wwwroot/js/fanout.js';
import { DEFAULT_LAYOUT, computePadding } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';
import {
  BUBBLE_EDGE_PX,
  BUBBLE_FADE_MS,
  BUBBLE_HIT_PX,
  BUBBLE_RESEED_TOLERANCE_PX,
  BUBBLE_SLIDE_MS,
  CLUSTER_FIT_MAX_ZOOM,
  CLUSTER_FIT_MS,
  HOOK_NAMES,
  MAX_PIN_KEEP_OUTS,
  PIN_HIT_PX,
  PIN_POINTER_PX,
  bubbleAnchors,
  bubbleKey,
  bubbleRect,
  bubbleTexts,
  clusterFitPoints,
  describeBubbles,
  fanItems,
  fillTemplate,
  isOwnBubble,
  keepOutRects,
  pinHitRect,
  pinKeepOuts,
  reseedAnchors,
} from '../../src/Realm.Web/wwwroot/js/testHooks.js';

const PHONE = { width: 412, height: 915 };
const PEEK = computePadding(PHONE, DEFAULT_LAYOUT); // top 72, right 72, bottom 190, left 16 at Peek

// ---- the numbers of 01 section 4.10 ---------------------------------------------------------------------------------------------

test('the numbers of 01 section 4.10: 8 px from the edge, a 48 px hit area, the cluster fit of 700 ms to zoom 15, 150 ms fades and 120 ms slides', () => {
  assert.equal(BUBBLE_EDGE_PX, 8);
  assert.equal(BUBBLE_HIT_PX, 48);
  assert.equal(CLUSTER_FIT_MAX_ZOOM, 15);
  assert.equal(CLUSTER_FIT_MS, 700);
  assert.equal(BUBBLE_FADE_MS, 150);
  assert.equal(BUBBLE_SLIDE_MS, 120);
});

test('the helpers add no name to window.__realm: the ten hooks of O-10 are unchanged', () => {
  assert.equal(HOOK_NAMES.length, 10);
  assert.ok(HOOK_NAMES.includes('bubbles') && HOOK_NAMES.includes('layoutBubbles'));
});

// ---- R ---------------------------------------------------------------------------------------------------------------------------

test('[AC-14] R at 412 x 915 Peek is x 8..404, y 8..725: 8 px from the top, right and left, the bottom at the sheet padding', () => {
  assert.deepEqual(bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK), { left: 8, top: 8, right: 404, bottom: 725 });
});

test('R starts below the safe-area inset at the top (safe-top + 8) and follows the padding at the bottom', () => {
  const layout = { ...DEFAULT_LAYOUT, safe: { ...DEFAULT_LAYOUT.safe, top: 24 } };
  assert.deepEqual(bubbleRect(PHONE, layout, { bottom: 300 }), { left: 8, top: 32, right: 404, bottom: 615 });
});

test('in Expanded with the panel shown the left edge of R is the panel\'s right edge plus 8; with the panel hidden it is 8 again', () => {
  const panel = { ...DEFAULT_LAYOUT, mode: 'expanded', panelLeftPx: 16, panelWidthPx: 360 };
  assert.equal(bubbleRect({ width: 1000, height: 700 }, panel, { bottom: 0 }).left, 384);
  assert.equal(bubbleRect({ width: 1000, height: 700 }, { ...panel, panelHidden: true }, { bottom: 0 }).left, 8);
  // the panel only matters in Expanded
  assert.equal(bubbleRect({ width: 1000, height: 700 }, { ...panel, mode: 'compact' }, { bottom: 0 }).left, 8);
});

// ---- keep-outs -------------------------------------------------------------------------------------------------------------------

test('keep-outs: boxes come in page pixels, leave in container pixels; a missing, hidden or empty box is dropped', () => {
  const rects = keepOutRects(
    [
      { left: 112, top: 62, right: 160, bottom: 110 },
      null,
      { left: 300, top: 300, right: 300, bottom: 340 }, // zero width: an element that is not laid out
      { left: 452, top: 671, right: 500, bottom: 779 },
    ],
    { left: 100, top: 50 },
  );
  assert.deepEqual(rects, [
    { left: 12, top: 12, right: 60, bottom: 60 },
    { left: 352, top: 621, right: 400, bottom: 729 },
  ]);
  assert.deepEqual(keepOutRects([], { left: 0, top: 0 }), []);
});

// ---- anchors and the hysteresis memory -----------------------------------------------------------------------------------------

test('[AC-19c] an id seen for the first time has no wasOff; one seen last frame carries its verdict', () => {
  const members = [{ id: 'a', status: 'idle' }, { id: 'b', status: 'idle' }, { id: 'c', status: 'idle' }];
  const at = () => ({ x: 1, y: 2 });
  const anchors = bubbleAnchors(members, at, new Map([['b', true], ['c', false]]), null);
  assert.ok(!('wasOff' in anchors[0]), 'a is new');
  assert.equal(anchors[1].wasOff, true);
  assert.equal(anchors[2].wasOff, false);
});

test('a member without a position (no fix) has no anchor, hence neither a pin nor a bubble', () => {
  const members = [{ id: 'a', status: 'idle' }, { id: 'b', status: 'idle' }];
  const anchors = bubbleAnchors(members, (m) => (m.id === 'a' ? { x: 5, y: 6 } : null), new Map(), null);
  assert.deepEqual(anchors, [{ id: 'a', x: 5, y: 6, priority: FAN_PRIORITY.member }]);
});

test('[AC-19b] the priority of a cluster is the fan-out\'s: selected, then driving, then the others', () => {
  const members = [{ id: 'a', status: 'idle' }, { id: 'b', status: 'driving' }, { id: 'c', status: 'idle' }];
  const at = () => ({ x: 0, y: 0 });
  const plain = bubbleAnchors(members, at, new Map(), null).map((a) => a.priority);
  assert.deepEqual(plain, [FAN_PRIORITY.member, FAN_PRIORITY.drivingMember, FAN_PRIORITY.member]);
  const chosen = bubbleAnchors(members, at, new Map(), 'c').map((a) => a.priority);
  assert.deepEqual(chosen, [FAN_PRIORITY.member, FAN_PRIORITY.drivingMember, FAN_PRIORITY.selected]);
});

// ---- the first verdict after a camera command (the default view keeps every pin) ---------------------------------------------------------

// The default fit (01 section 4.9) puts the outermost pins on the edge of the map padding: x 16 on the left, which is 8 px inside R (x 8), and y 725 on the bottom,
// which is R's own bottom edge. A member that was off screen before the fit is remembered as off, and the 12 px hysteresis of 01 section 4.10 step 2 keeps it a bubble
// unless it is 12 px inside R: the default view lost the pin of the member farthest to the west (every E2E spec that waits for the four default pins failed).
test('[AC-13] a camera command decides afresh: a member that was off screen and is now 8 px inside R is on screen, where the hysteresis alone keeps it off', () => {
  const rect = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
  const members = [{ id: 'west', status: 'idle' }, { id: 'far', status: 'idle' }];
  const places = { west: { x: PEEK.left, y: 500 }, far: { x: 5000, y: 366 } };
  const previous = new Map([['west', true], ['far', true]]); // both were off screen before the command
  const remembered = bubbleAnchors(members, (m) => places[m.id], previous, null);
  assert.deepEqual(layoutBubbles(rect, [], remembered).offScreen.sort(), ['far', 'west'], 'the hysteresis alone: west stays a bubble (the defect)');

  const fresh = reseedAnchors(remembered, rect);
  const result = layoutBubbles(rect, [], fresh);
  assert.deepEqual(result.onScreen, ['west'], 'the plain verdict: 8 px inside R is on screen');
  assert.deepEqual(result.offScreen, ['far']);
});

test('[AC-13] the plain verdict is R grown by a pixel: the pin the fit leaves exactly on the bottom edge (or a hair past it) is on screen, 2 px past it is not', () => {
  assert.equal(BUBBLE_RESEED_TOLERANCE_PX, 1);
  const rect = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
  const anchor = (id, x, y) => ({ id, x, y, priority: FAN_PRIORITY.member });
  const verdict = (anchors) => Object.fromEntries(reseedAnchors(anchors, rect).map((a) => [a.id, a.wasOff]));
  assert.deepEqual(
    verdict([
      anchor('onEdge', 200, rect.bottom),
      anchor('floatNoise', 200, rect.bottom + 1e-6),
      anchor('withinTolerance', rect.left - 1, 300),
      anchor('twoPast', 200, rect.bottom + 2),
      anchor('farRight', rect.right + 40, 300),
      anchor('farUp', 200, rect.top - 40),
    ]),
    { onEdge: false, floatNoise: false, withinTolerance: false, twoPast: true, farRight: true, farUp: true },
  );
});

test('reseeding keeps the anchors: same order, same ids, positions and priorities, the input untouched, and a member without a previous verdict gets one', () => {
  const rect = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
  const input = [
    { id: 'b', x: 50, y: 60, priority: FAN_PRIORITY.selected },
    { id: 'a', x: -300, y: 60, wasOff: false, priority: FAN_PRIORITY.drivingMember },
  ];
  const output = reseedAnchors(input, rect);
  assert.deepEqual(output, [
    { id: 'b', x: 50, y: 60, priority: FAN_PRIORITY.selected, wasOff: false },
    { id: 'a', x: -300, y: 60, priority: FAN_PRIORITY.drivingMember, wasOff: true },
  ]);
  assert.ok(!('wasOff' in input[0]) && input[1].wasOff === false, 'the input is not modified');
});

test('after the fresh frame the memory is the normal one again: a pin 8 px inside R that came back stays on screen, and goes off only 12 px outside R', () => {
  const rect = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
  const members = [{ id: 'west', status: 'idle' }];
  const at = (x) => () => ({ x, y: 500 });
  const fresh = layoutBubbles(rect, [], reseedAnchors(bubbleAnchors(members, at(rect.left + 8), new Map([['west', true]]), null), rect));
  assert.deepEqual(fresh.onScreen, ['west']);
  const next = (x) => layoutBubbles(rect, [], bubbleAnchors(members, at(x), new Map([['west', false]]), null));
  assert.deepEqual(next(rect.left + 8).onScreen, ['west'], 'the next frame, nothing moved');
  assert.deepEqual(next(rect.left - 11).onScreen, ['west'], 'a pan that takes it 11 px outside R does not flicker it off');
  assert.deepEqual(next(rect.left - 13).offScreen, ['west'], '13 px outside R it is a bubble');
});

// ---- the bubbles hook ----------------------------------------------------------------------------------------------------------

test('[O-10] the bubbles hook: id is the test-id suffix and cluster is the member COUNT (a number, 1 for a single bubble), never a boolean', () => {
  const rows = describeBubbles([
    { ids: ['a'], x: 10, y: 20, angleDeg: 0, cluster: 1 },
    { ids: ['b', 'c'], x: 30, y: 40, angleDeg: 90, cluster: 2 },
  ]);
  assert.deepEqual(rows, [
    { id: 'a', ids: ['a'], x: 10, y: 20, angleDeg: 0, cluster: 1 },
    { id: 'b-c', ids: ['b', 'c'], x: 30, y: 40, angleDeg: 90, cluster: 2 },
  ]);
  assert.ok(rows.every((row) => typeof row.cluster === 'number'));
  assert.equal(bubbleKey(['b', 'c', 'd']), 'b-c-d');
});

test('the rows of the hook are copies: changing one does not change the layout it came from', () => {
  const source = [{ ids: ['a', 'b'], x: 1, y: 2, angleDeg: 3, cluster: 2 }];
  describeBubbles(source)[0].ids.push('z');
  assert.deepEqual(source[0].ids, ['a', 'b']);
});

test('[AC-14] the hook of a real layout: one bubble per off-screen member, cluster 1, in the order of the layout', () => {
  const rect = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
  const members = [{ id: 'east', status: 'idle' }, { id: 'northwest', status: 'idle' }, { id: 'here', status: 'idle' }];
  const places = { east: { x: 5000, y: 366 }, northwest: { x: -4000, y: -2000 }, here: { x: 206, y: 366 } };
  const result = layoutBubbles(rect, [], bubbleAnchors(members, (m) => places[m.id], new Map(), null));
  const rows = describeBubbles(result.bubbles);
  assert.deepEqual(rows.map((row) => row.id).sort(), ['east', 'northwest']);
  assert.ok(rows.every((row) => row.cluster === 1));
  assert.equal(result.onScreen.length, 1);
  const east = rows.find((row) => row.id === 'east');
  assert.equal(east.x, rect.right - 20); // the centre travels 20 px inside R
});

// ---- texts ---------------------------------------------------------------------------------------------------------------------

test('fillTemplate fills {n} and {names} and leaves an unknown placeholder alone', () => {
  assert.equal(fillTemplate('{n} people off screen: {names}.', { n: 2, names: 'A, B' }), '2 people off screen: A, B.');
  assert.equal(fillTemplate('{n} and {other}', { n: 3 }), '3 and {other}');
  assert.equal(fillTemplate('no placeholder', {}), 'no placeholder');
});

test('a single bubble uses the label and tooltip C# sent; a cluster fills the two templates with the count and the names in the order of the bubble', () => {
  const byId = new Map([
    ['a', { name: 'Ann', bubbleLabel: 'label A', bubbleTooltip: 'tip A' }],
    ['b', { name: 'Bo', bubbleLabel: 'label B', bubbleTooltip: 'tip B' }],
  ]);
  const strings = { clusterName: '{n} people off screen: {names}.', clusterTooltip: '{names} · tap' };
  assert.deepEqual(bubbleTexts(['a'], byId, strings), { label: 'label A', tooltip: 'tip A' });
  assert.deepEqual(bubbleTexts(['b', 'a'], byId, strings), { label: '2 people off screen: Bo, Ann.', tooltip: 'Bo, Ann · tap' });
});

test('texts degrade to the names when the templates are missing, and to empty text for an id nobody knows', () => {
  const byId = new Map([['a', { name: 'Ann', bubbleLabel: 'x', bubbleTooltip: 'y' }]]);
  assert.deepEqual(bubbleTexts(['a', 'q'], byId, undefined), { label: 'Ann, q', tooltip: 'Ann, q' });
  assert.deepEqual(bubbleTexts(['q'], byId, undefined), { label: '', tooltip: '' });
});

// ---- the tap ------------------------------------------------------------------------------------------------------------------

test('[AC-15] the viewer\'s own single bubble is Recenter and is never reported; any cluster, and any other member, is', () => {
  assert.equal(isOwnBubble(['me'], 'me'), true);
  assert.equal(isOwnBubble(['other'], 'me'), false);
  assert.equal(isOwnBubble(['me', 'other'], 'me'), false);
  assert.equal(isOwnBubble(['me'], ''), false);
});

test('the cluster fit frames the viewer\'s member plus the tapped members that have a position, and nobody else (synthetic points)', () => {
  const members = [
    { id: 'me', lat: 1, lon: 2 },
    { id: 'a', lat: 3, lon: 4 },
    { id: 'b', lat: 5, lon: 6 },
    { id: 'c', lat: 7, lon: 8 },
    { id: 'nofix', lat: null, lon: null },
  ];
  assert.deepEqual(clusterFitPoints(members, 'me', ['a', 'nofix']), [[2, 1], [4, 3]]);
  assert.deepEqual(clusterFitPoints(members, '', ['b', 'c']), [[6, 5], [8, 7]]);
  assert.deepEqual(clusterFitPoints(members, 'me', ['me', 'a']), [[2, 1], [4, 3]], 'the viewer is not framed twice');
});

// ---- the fan-out -------------------------------------------------------------------------------------------------------------

test('[AC-16] fan-out items: the id is kind:id, the priority is selected, then driving, then members, then vehicles', () => {
  const items = fanItems(
    [
      { kind: 'vehicle', id: 'v', x: 0, y: 0, driving: false },
      { kind: 'member', id: 'm', x: 0, y: 0, driving: false },
      { kind: 'member', id: 'd', x: 0, y: 0, driving: true },
      { kind: 'member', id: 's', x: 0, y: 0, driving: false },
    ],
    'member:s',
  );
  assert.deepEqual(items.map((i) => [i.id, i.priority]), [
    ['vehicle:v', FAN_PRIORITY.vehicle],
    ['member:m', FAN_PRIORITY.member],
    ['member:d', FAN_PRIORITY.drivingMember],
    ['member:s', FAN_PRIORITY.selected],
  ]);
});

test('[AC-16] a vehicle on its member\'s point is drawn 48 px to the right and the member stays; nothing connects them (no leader, dy 0)', () => {
  const results = fanOut(fanItems(
    [
      { kind: 'vehicle', id: 'v', x: 120, y: 340, driving: false },
      { kind: 'member', id: 'm', x: 120, y: 340, driving: false },
    ],
    null,
  ));
  assert.deepEqual(results, [
    { id: 'vehicle:v', dx: 48, dy: 0, fanned: true },
    { id: 'member:m', dx: 0, dy: 0, fanned: false },
  ]);
});

test('[AC-16] a selected vehicle keeps its true place and the member beside it steps aside', () => {
  const results = fanOut(fanItems(
    [
      { kind: 'vehicle', id: 'v', x: 50, y: 60, driving: false },
      { kind: 'member', id: 'm', x: 50, y: 60, driving: false },
    ],
    'vehicle:v',
  ));
  assert.deepEqual(results.map((r) => [r.id, r.dx]), [['vehicle:v', 0], ['member:m', 48]]);
});

// ---- D89 (1): an on-screen pin, a fanned one included, is a keep-out rectangle for the edge bubbles ---------------------------------------------
// The scene is synthetic pixels at the 412 x 915 Peek rectangle (R x 8..404, y 8..725, bubble centres on x 28..384, y 28..705). No test here names a place.

const VIEW = { left: 0, top: 0, right: PHONE.width, bottom: PHONE.height };
const RECT_R = bubbleRect(PHONE, DEFAULT_LAYOUT, PEEK);
const CENTRE_R = { x: (RECT_R.left + RECT_R.right) / 2, y: (RECT_R.top + RECT_R.bottom) / 2 };
/** The right edge of the bubble centres: R inset by the 20 px radius. */
const RIGHT_EDGE_X = RECT_R.right - 20;
/** A far anchor on the ray from the centre of R through `q`: the bubble lands on the edge at `q` (a power-of-two scale keeps the numbers exact). */
const farVia = (id, q) => ({ id, x: CENTRE_R.x + (q.x - CENTRE_R.x) * 8, y: CENTRE_R.y + (q.y - CENTRE_R.y) * 8, priority: FAN_PRIORITY.member });
/** The gap between the 40 px box of a bubble and a rectangle (negative when they overlap). */
const gapFrom = (b, rect) => Math.max(rect.left - (b.x + 20), b.x - 20 - rect.right, rect.top - (b.y + 20), b.y - 20 - rect.bottom);
const pinAt = (kind, id, x, y, extra = {}) => ({ kind, id, x, y, driving: false, sizePx: kind === 'vehicle' ? 44 : 48, ...extra });

test('[AC-16] the hit area of a pin is 56 x 56 centred on its body, which sits 8 px above the true point; a 60 px selected pin is its own hit area', () => {
  assert.equal(PIN_HIT_PX, 56);
  assert.equal(PIN_POINTER_PX, 8);
  // member 48: the body is y-56..y-8, centre y-32, so the hit area is y-60..y-4
  assert.deepEqual(pinHitRect({ x: 100, y: 200, sizePx: 48 }), { left: 72, top: 140, right: 128, bottom: 196 });
  // vehicle 44: centre y-30
  assert.deepEqual(pinHitRect({ x: 100, y: 200, sizePx: 44 }), { left: 72, top: 142, right: 128, bottom: 198 });
  // selected 60: the hit area is the body (60), centre y-38
  assert.deepEqual(pinHitRect({ x: 100, y: 200, sizePx: 60 }), { left: 70, top: 132, right: 130, bottom: 192 });
});

test('[AC-16] a pin that is fanned out has the SHIFTED rectangle: the second pin on one point is 48 px to the right, the first stays', () => {
  const rects = pinKeepOuts([pinAt('member', 'm', 200, 400), pinAt('vehicle', 'v', 200, 400)], null, VIEW);
  assert.deepEqual(rects[0], pinHitRect({ x: 200, y: 400, sizePx: 48 }), 'the member keeps its true place');
  assert.deepEqual(rects[1], pinHitRect({ x: 200 + FAN_DEFAULTS.step, y: 400, sizePx: 44 }), 'the vehicle is drawn 48 px to the right');
  assert.equal(rects[1].left - pinHitRect({ x: 200, y: 400, sizePx: 44 }).left, 48);
});

test('[AC-16] the rectangles use the frame\'s own fan-out: the selected pin stays and the other steps aside, and `fan: false` leaves every pin on its true point', () => {
  const pins = [pinAt('member', 'm', 200, 400), pinAt('vehicle', 'v', 215, 400)];
  const selectedVehicle = pinKeepOuts(pins, 'vehicle:v', VIEW);
  assert.deepEqual(selectedVehicle[1], pinHitRect({ x: 215, y: 400, sizePx: 44 }));
  assert.deepEqual(selectedVehicle[0], pinHitRect({ x: 200 + FAN_DEFAULTS.step, y: 400, sizePx: 48 }));
  const items = fanItems(pins, 'vehicle:v');
  assert.deepEqual(fanOut(items).map((r) => r.dx), [48, 0], 'the same shifts as fanOut gives');
  const unfanned = pinKeepOuts(pins, null, VIEW, { fan: false });
  assert.deepEqual(unfanned, [pinHitRect({ x: 200, y: 400, sizePx: 48 }), pinHitRect({ x: 215, y: 400, sizePx: 44 })]);
  // 36 px or more apart is no fan-out
  const apart = pinKeepOuts([pinAt('member', 'm', 200, 400), pinAt('vehicle', 'v', 200 + FAN_DEFAULTS.threshold, 400)], null, VIEW);
  assert.deepEqual(apart[1], pinHitRect({ x: 200 + FAN_DEFAULTS.threshold, y: 400, sizePx: 44 }));
});

test('the rectangle is the one of the DRAWN pin: MapLibre rounds a marker to whole pixels, so a fractional true point (fanned or not) is rounded', () => {
  const [plain] = pinKeepOuts([pinAt('member', 'm', 200.4, 400.6)], null, VIEW);
  assert.deepEqual(plain, pinHitRect({ x: 200, y: 401, sizePx: 48 }));
  const [first, second] = pinKeepOuts([pinAt('member', 'm', 200.4, 400.6), pinAt('vehicle', 'v', 200.4, 400.6)], null, VIEW);
  assert.deepEqual(first, plain);
  assert.deepEqual(second, pinHitRect({ x: 200 + FAN_DEFAULTS.step, y: 401, sizePx: 44 }), 'the shift is added before the rounding, which is what the marker does with its offset');
});

test('a pin whose hit area is entirely outside the container is not on screen and has no rectangle; one that is partly inside has', () => {
  const rects = pinKeepOuts(
    [
      pinAt('member', 'inside', 200, 400),
      pinAt('vehicle', 'edge', PHONE.width + 20, 400), // the hit area spans x 404..460 at the right edge: 8 px of it is on screen
      pinAt('vehicle', 'gone', PHONE.width + 40, 400), // x 424..480: none of it is
      pinAt('vehicle', 'above', 200, 0), // the body is above the container, the hit area y -60..-4
      pinAt('member', 'bad', Number.NaN, 400),
    ],
    null,
    VIEW,
    { fan: false },
  );
  assert.deepEqual(rects, [pinHitRect({ x: 200, y: 400, sizePx: 48 }), pinHitRect({ x: PHONE.width + 20, y: 400, sizePx: 44 })]);
});

test('the input is bounded: more than MAX_PIN_KEEP_OUTS pins give at most that many rectangles, the first ones in order, and the input is not changed', () => {
  const pins = Array.from({ length: MAX_PIN_KEEP_OUTS + 25 }, (_, i) => pinAt('member', `m${String(i).padStart(3, '0')}`, 20 + (i % 8) * 50, 40 + Math.floor(i / 8) * 70));
  const copy = JSON.parse(JSON.stringify(pins));
  const rects = pinKeepOuts(pins, null, { left: -1000, top: -1000, right: 5000, bottom: 5000 }, { fan: false });
  assert.equal(rects.length, MAX_PIN_KEEP_OUTS);
  assert.deepEqual(rects[0], pinHitRect(pins[0]));
  assert.deepEqual(pins, copy);
  assert.deepEqual(pinKeepOuts([], null, VIEW), []);
});

test('[AC-14] with no pin near a bubble the positions are exactly the ones without pin keep-outs', () => {
  const anchors = [farVia('east', { x: RIGHT_EDGE_X, y: 300 }), farVia('west', { x: 28, y: 500 })];
  const bare = layoutBubbles(RECT_R, [], anchors);
  // pins in the middle of the map: their grown rectangles (28 px) stay clear of both edge columns
  const pins = [pinAt('member', 'a', 200, 360), pinAt('member', 'b', 260, 420), pinAt('vehicle', 'v', 150, 500)];
  assert.deepEqual(layoutBubbles(RECT_R, pinKeepOuts(pins, null, VIEW), anchors), bare);
  assert.deepEqual(layoutBubbles(RECT_R, [], anchors), bare);
});

test('[AC-13] [AC-16] a bubble whose natural spot overlaps a pin slides along its edge until its box is 8 px clear of the pin\'s hit area (the way its member lies)', () => {
  const pin = pinAt('member', 'm', 350, 382); // body centre y 350, hit area x 322..378, y 322..378: it reaches the right-edge column (x 364..404 is the bubble box)
  const [rect] = pinKeepOuts([pin], null, VIEW);
  const natural = layoutBubbles(RECT_R, [], [farVia('east', { x: RIGHT_EDGE_X, y: 350 })]).bubbles[0];
  assert.equal(natural.x, RIGHT_EDGE_X);
  assert.ok(gapFrom(natural, rect) < 0, 'precondition: the natural spot overlaps the pin');

  const result = layoutBubbles(RECT_R, [rect], [farVia('east', { x: RIGHT_EDGE_X, y: 350 })]);
  const [bubble] = result.bubbles;
  assert.equal(bubble.x, RIGHT_EDGE_X, 'it stays on its edge');
  assert.equal(bubble.y, rect.top - 28, 'the member lies above the centre line of R: up, to the top of the rectangle grown by 8 + 20');
  assert.ok(gapFrom(bubble, rect) >= 8);
  assert.equal(bubble.cluster, 1);
  // a member below the centre line slides down instead
  const below = layoutBubbles(RECT_R, [rect], [farVia('east', { x: RIGHT_EDGE_X, y: 390 })]).bubbles[0];
  assert.equal(below.y, rect.bottom + 28);
  assert.ok(gapFrom(below, rect) >= 8);
});

test('[AC-16] the bubble keeps clear of the FANNED rectangle, not of the true point: the same scene slides with the fan-out and stays without it', () => {
  // The first pin is clear of the right column on its own; the second shares its place, is drawn 48 px to the right and so reaches the column.
  const pins = [pinAt('member', 'first', 300, 382), pinAt('vehicle', 'second', 312, 382)];
  const anchors = [farVia('east', { x: RIGHT_EDGE_X, y: 350 })];
  const bare = layoutBubbles(RECT_R, [], anchors).bubbles[0];
  const unfanned = layoutBubbles(RECT_R, pinKeepOuts(pins, null, VIEW, { fan: false }), anchors).bubbles[0];
  assert.deepEqual([unfanned.x, unfanned.y], [bare.x, bare.y], 'at their true points neither pin reaches the bubble');
  const fannedRects = pinKeepOuts(pins, null, VIEW);
  const fanned = layoutBubbles(RECT_R, fannedRects, anchors).bubbles[0];
  assert.notEqual(fanned.y, bare.y, 'the drawn (shifted) pin reaches it, so it moves');
  assert.ok(fannedRects.every((rect) => gapFrom(fanned, rect) >= 8));
});

test('[AC-13] when the other direction is the only one that clears, the bubble goes that way (D89 (3)), still clear of every pin', () => {
  // A pin right at the top end of the right edge: the member lies above, so up runs off the edge and the bubble slides down past the pin.
  const rects = pinKeepOuts([pinAt('member', 'm', 360, 80)], null, VIEW);
  const [bubble] = layoutBubbles(RECT_R, rects, [farVia('east', { x: RIGHT_EDGE_X, y: 60 })]).bubbles;
  assert.equal(bubble.x, RIGHT_EDGE_X);
  assert.equal(bubble.y, rects[0].bottom + 28);
  assert.ok(gapFrom(bubble, rects[0]) >= 8);
});

test('[AC-19a] pins that leave no clear coordinate on the edge: the bubble joins the nearest one as a cluster (the existing rule), or sits at the end of the edge when alone', () => {
  // A column of pins down the whole right edge (40 px apart: 36 or more, so no fan-out): no coordinate of the edge is clear.
  const column = Array.from({ length: 20 }, (_, i) => pinAt('member', `p${String(i).padStart(2, '0')}`, 380, 60 + i * 40));
  const wall = pinKeepOuts(column, null, VIEW);
  assert.equal(wall.length, column.length);
  const stuck = farVia('east', { x: RIGHT_EDGE_X, y: 300 });
  const bottom = farVia('south', { x: 200, y: RECT_R.bottom - 20 });
  const joined = layoutBubbles(RECT_R, wall, [stuck, bottom]);
  assert.equal(joined.bubbles.length, 1);
  assert.deepEqual([...joined.bubbles[0].ids].sort(), ['east', 'south']);
  assert.strictEqual(joined.bubbles[0].cluster, 2);
  const alone = layoutBubbles(RECT_R, wall, [stuck]);
  assert.equal(alone.bubbles.length, 1);
  assert.equal(alone.bubbles[0].x, RIGHT_EDGE_X);
  assert.ok(alone.bubbles[0].y === 28 || alone.bubbles[0].y === 705, `at an end of the edge: ${alone.bubbles[0].y}`);
});
