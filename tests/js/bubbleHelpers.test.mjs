// Tests for the S9b helpers of wwwroot/js/testHooks.js: what the edge bubbles and the fan-out of realmMap.js decide from numbers and strings alone
// (01 sections 4.8 and 4.10, 03 sections 4.6 and 4.10). Titles carry the acceptance criteria they cover. No test here names a place: positions are
// pixels or synthetic numbers, so the demo fixture can move (D82) without touching this file.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { layoutBubbles } from '../../src/Realm.Web/wwwroot/js/bubbleLayout.js';
import { FAN_PRIORITY, fanOut } from '../../src/Realm.Web/wwwroot/js/fanout.js';
import { DEFAULT_LAYOUT, computePadding } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';
import {
  BUBBLE_EDGE_PX,
  BUBBLE_FADE_MS,
  BUBBLE_HIT_PX,
  BUBBLE_SLIDE_MS,
  CLUSTER_FIT_MAX_ZOOM,
  CLUSTER_FIT_MS,
  HOOK_NAMES,
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
