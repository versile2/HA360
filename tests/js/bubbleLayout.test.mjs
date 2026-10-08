// Tests for wwwroot/js/bubbleLayout.js: the pure placement of the off-screen member bubbles (01 section 4.10, 03 section 4.6).
// Titles carry the acceptance criteria they cover: AC-19 (a) keep-out slide, (b) cluster with the first avatar and a count, (c) 12 px
// hysteresis, (d) cluster snap-back, and AC-14 (the two Demo fixture bubbles).
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUBBLE_DEFAULTS, layoutBubbles, partitionAnchors } from '../../src/Realm.Web/wwwroot/js/bubbleLayout.js';
import { DEFAULT_LAYOUT, PIN_BODY_ALLOWANCE_PX, computePadding } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';
import { pinHitRect, pinKeepOuts } from '../../src/Realm.Web/wwwroot/js/testHooks.js';

// ---- the 412 x 915 Peek scene of 01 sections 3.3, 3.4.3 and 4.10 ----------------------------------------------------------------

const PHONE = { width: 412, height: 915 };
const PADDING = computePadding(PHONE, DEFAULT_LAYOUT); // { top 72, right 72, bottom 190, left 16 } at Peek
/** R of 01 section 4.10 step 1: the container minus the padding, except top, left and right are 8 (no safe-area inset here). */
const R = { left: 8, top: 8, right: PHONE.width - 8, bottom: PHONE.height - PADDING.bottom }; // x 8..404, y 8..725
/** R inset by the 20 px bubble radius: where a bubble centre may travel. */
const BOX = { left: 28, top: 28, right: 384, bottom: 705 };
const CENTRE = { x: 206, y: 366.5 };

const ATTRIBUTION = { left: 352, top: 12, right: 400, bottom: 60 }; // 48 x 48 hit area, right edge 12 px from the viewport's, AC-03
const STACK = { left: 352, top: 621, right: 400, bottom: 729 }; // two 48 px buttons 12 px apart, bottom edge at 729, left edge x 352, AC-04
const KEEP_OUTS = [ATTRIBUTION, STACK];
const SHEET_TOP = PHONE.height - 174; // 741 at Peek

/** A far anchor on the ray from the centre of R through `q` (a power-of-two scale keeps every number exact). */
const via = (id, q, extra = {}, scale = 8) => ({ id, x: CENTRE.x + (q.x - CENTRE.x) * scale, y: CENTRE.y + (q.y - CENTRE.y) * scale, ...extra });
const bubbleOf = (result, id) => result.bubbles.find((b) => b.ids.includes(id));
const near = (actual, expected, tolerance = 1e-9) => assert.ok(Math.abs(actual - expected) <= tolerance, `${actual} is not within ${tolerance} of ${expected}`);
const onBoxBoundary = (b) =>
  b.x >= BOX.left - 1e-9 && b.x <= BOX.right + 1e-9 && b.y >= BOX.top - 1e-9 && b.y <= BOX.bottom + 1e-9 &&
  (Math.abs(b.x - BOX.left) < 1e-9 || Math.abs(b.x - BOX.right) < 1e-9 || Math.abs(b.y - BOX.top) < 1e-9 || Math.abs(b.y - BOX.bottom) < 1e-9);
/** The gap between the 40 px box of a bubble and a rectangle (negative when they overlap). */
const gapTo = (b, rect) => Math.max(rect.left - (b.x + 20), b.x - 20 - rect.right, rect.top - (b.y + 20), b.y - 20 - rect.bottom);
const deepFreeze = (value) => {
  if (value && typeof value === 'object') Object.values(value).forEach(deepFreeze);
  return Object.freeze(value);
};

// ---- the Demo fixture (01 Appendix A, 02 section 9.3) ----------------------------------------------------------------------------

const MERCATOR_WORLD_PX = 512; // MapLibre's world is 512 px wide at zoom 0
const mercator = ([lon, lat]) => ({ x: (lon + 180) / 360, y: 0.5 - Math.log(Math.tan(Math.PI / 4 + (lat * Math.PI) / 360)) / (2 * Math.PI) });
const DEMO = {
  king: [-85.341, 31.099],
  queen: [-85.4647, 31.056],
  jester: [-85.356, 31.104],
  wagon: [-85.341, 31.09907],
  cryptid: [-82.7291, 31.3382],
  prince: [-92.8214, 38.8339],
};

/** The default camera of 01 section 4.9 at 412 x 915 Peek, as MapLibre's fitBounds computes it for these four in-view points. */
function projectDemo() {
  const inView = ['king', 'queen', 'jester', 'wagon'].map((k) => mercator(DEMO[k]));
  const xs = inView.map((m) => m.x);
  const ys = inView.map((m) => m.y);
  const top = PADDING.top + PIN_BODY_ALLOWANCE_PX; // fitBounds adds the 56 px pin body to the top padding
  const availableWidth = PHONE.width - PADDING.left - PADDING.right;
  const availableHeight = PHONE.height - top - PADDING.bottom;
  const zoom = Math.min(16, Math.log2(availableWidth / (MERCATOR_WORLD_PX * (Math.max(...xs) - Math.min(...xs)))), Math.log2(availableHeight / (MERCATOR_WORLD_PX * (Math.max(...ys) - Math.min(...ys)))));
  const world = MERCATOR_WORLD_PX * 2 ** zoom;
  const middle = { x: (Math.max(...xs) + Math.min(...xs)) / 2, y: (Math.max(...ys) + Math.min(...ys)) / 2 };
  const target = { x: PADDING.left + availableWidth / 2, y: top + availableHeight / 2 }; // the bounds' centre lands mid-padding
  const project = (id) => {
    const m = mercator(DEMO[id]);
    return { x: target.x + (m.x - middle.x) * world, y: target.y + (m.y - middle.y) * world };
  };
  return { zoom, project };
}

test('[AC-14] the Demo fixture at 412 x 915 Peek, with the attribution and the stack as the only keep-outs (v0.1.1: no gear): Dara lands within 12 px of (384, 347) and Elio within 12 px of (28, 142)', () => {
  const { project } = projectDemo();
  const anchors = ['king', 'queen', 'jester', 'cryptid', 'prince'].map((id) => ({ id, ...project(id) }));
  const result = layoutBubbles(R, KEEP_OUTS, anchors);

  assert.deepEqual(result.onScreen, ['king', 'queen', 'jester']);
  assert.deepEqual(result.offScreen, ['cryptid', 'prince']);
  assert.equal(result.bubbles.length, 2);
  const dara = bubbleOf(result, 'cryptid');
  const elio = bubbleOf(result, 'prince');
  assert.ok(Math.hypot(dara.x - 384, dara.y - 347) <= 12, `cryptid at (${dara.x}, ${dara.y})`);
  assert.ok(Math.hypot(elio.x - 28, elio.y - 142) <= 12, `prince at (${elio.x}, ${elio.y})`);
  assert.deepEqual([dara.ids, elio.ids], [['cryptid'], ['prince']]);
  assert.strictEqual(dara.cluster, 1);
  assert.strictEqual(elio.cluster, 1);

  // Each bubble's 40 px box is at least 8 px clear of the attribution, the right stack and the sheet's top edge.
  for (const bubble of [dara, elio]) {
    for (const keepOut of KEEP_OUTS) assert.ok(gapTo(bubble, keepOut) >= 8, `${bubble.ids[0]} is ${gapTo(bubble, keepOut)} px from ${JSON.stringify(keepOut)}`);
    assert.ok(SHEET_TOP - (bubble.y + 20) >= 8);
  }
  // Chevrons face their members: Dara east (within 10 degrees of horizontal), Elio up and to the left.
  assert.ok(Math.abs(dara.angleDeg) <= 10, `cryptid angle ${dara.angleDeg}`);
  assert.ok(elio.angleDeg < -90 && elio.angleDeg > -180, `prince angle ${elio.angleDeg}`);
  // 02 section 9.3 gives the compass bearings 83 and 324 degrees; the screen angle (east 0, y down) is the bearing minus 90.
  near(dara.angleDeg, 83 - 90, 5);
  near(elio.angleDeg, 324 - 90 - 360, 5);
});

test('[AC-14] the fixture bubbles are the same whether the previous frame had them off screen or not', () => {
  const { project } = projectDemo();
  const ids = ['king', 'cryptid', 'prince'];
  const first = layoutBubbles(R, KEEP_OUTS, ids.map((id) => ({ id, ...project(id) })));
  for (const wasOff of [true, false]) {
    const again = layoutBubbles(R, KEEP_OUTS, ids.map((id) => ({ id, ...project(id), wasOff: id === 'king' ? false : wasOff })));
    assert.deepEqual(again, first);
  }
});

// D89 (1) / R1-01: in the default view the wagon is fanned 48 px to the right of the King and sits at the right edge of the map, where Dara's bubble used to cover it. The
// pins are keep-outs now (their hit areas, the wagon's at its shifted place), so Dara's bubble slides along the right edge until it is 8 px clear; Elio's, far from every pin,
// is exactly where it was.
test('[AC-14] [AC-16] the Demo fixture with the four pins as keep-outs: no bubble covers a pin, Elio stays within 12 px of (28, 142), Dara slides up the right edge past the King and the fanned wagon', () => {
  const { project } = projectDemo();
  const anchors = ['king', 'queen', 'jester', 'cryptid', 'prince'].map((id) => ({ id, ...project(id) }));
  const tips = [
    { kind: 'member', id: 'king', ...project('king'), driving: false, sizePx: 48 },
    { kind: 'member', id: 'queen', ...project('queen'), driving: false, sizePx: 48 },
    { kind: 'member', id: 'jester', ...project('jester'), driving: false, sizePx: 48 },
    { kind: 'vehicle', id: 'wagon', ...project('wagon'), driving: false, sizePx: 44 },
  ];
  const view = { left: 0, top: 0, right: PHONE.width, bottom: PHONE.height };
  const pins = pinKeepOuts(tips, null, view);
  assert.equal(pins.length, 4, 'every pin of the default view is on screen');
  // the wagon is fanned by the King (the same point, the member has priority): its rectangle is the shifted one
  assert.deepEqual(pins[3], pinHitRect({ x: Math.round(tips[3].x) + 48, y: Math.round(tips[3].y), sizePx: 44 }));

  const without = layoutBubbles(R, KEEP_OUTS, anchors);
  const withPins = layoutBubbles(R, [...KEEP_OUTS, ...pins], anchors);
  const darasNatural = bubbleOf(without, 'cryptid');
  assert.ok(pins.some((pin) => gapTo(darasNatural, pin) < 0), 'precondition: without the pins, Dara\'s bubble covers a pin (the wagon)');

  assert.deepEqual(withPins.onScreen, ['king', 'queen', 'jester']);
  assert.deepEqual(withPins.offScreen, ['cryptid', 'prince']);
  assert.equal(withPins.bubbles.length, 2, 'no merge: each of them finds a clear place on its edge');
  const dara = bubbleOf(withPins, 'cryptid');
  const elio = bubbleOf(withPins, 'prince');
  for (const bubble of [dara, elio]) {
    for (const pin of pins) assert.ok(gapTo(bubble, pin) >= 8, `${bubble.ids[0]} is ${gapTo(bubble, pin)} px from a pin`);
    for (const keepOut of KEEP_OUTS) assert.ok(gapTo(bubble, keepOut) >= 8);
  }
  assert.equal(dara.x, BOX.right, 'still on the right edge');
  assert.ok(dara.y < darasNatural.y, 'the member lies above the centre line: up');
  assert.equal(dara.y, Math.min(...pins.filter((pin) => pin.right + 28 > dara.x && pin.left - 28 < dara.x).map((pin) => pin.top)) - 28, 'to the top of the highest pin it passed, grown by 8 + 20');
  assert.ok(Math.hypot(elio.x - 28, elio.y - 142) <= 12, `prince at (${elio.x}, ${elio.y})`);
  assert.deepEqual([elio.x, elio.y], [bubbleOf(without, 'prince').x, bubbleOf(without, 'prince').y], 'no pin is near Elio: his bubble did not move');
  near(dara.angleDeg, darasNatural.angleDeg); // the chevron still follows the ray to the member
});

test('[AC-13] who is off screen does not depend on the keep-outs, and partitionAnchors is the verdict layoutBubbles uses', () => {
  const { project } = projectDemo();
  const anchors = ['king', 'queen', 'jester', 'cryptid', 'prince'].map((id) => ({ id, ...project(id) }));
  const part = partitionAnchors(R, anchors);
  assert.deepEqual(part.onScreen, ['king', 'queen', 'jester']);
  assert.deepEqual(part.offScreen, ['cryptid', 'prince']);
  assert.deepEqual(part.off.map((a) => a.id), ['cryptid', 'prince']);
  const wall = [{ left: -100, top: -100, right: 600, bottom: 1100 }]; // everything is a keep-out: still the same verdict
  for (const keepOuts of [[], KEEP_OUTS, wall]) {
    const result = layoutBubbles(R, keepOuts, anchors);
    assert.deepEqual([result.onScreen, result.offScreen], [part.onScreen, part.offScreen]);
  }
});

test('[AC-19c] partitionAnchors keeps the 12 px hysteresis, takes the option, skips non-finite anchors and does not modify its input', () => {
  const at = (id, x, wasOff) => ({ id, x, y: 300, ...(wasOff === undefined ? {} : { wasOff }) });
  const input = [
    at('plain', R.right + 5), // no memory: the plain rectangle, 5 px outside
    at('wasOn', R.right + 11, false), // was on screen: goes off only more than 12 px outside
    at('wasOnFar', R.right + 13, false),
    at('wasOff', R.right - 11, true), // was off: comes back only more than 12 px inside
    at('wasOffFar', R.right - 13, true),
    { id: 'nan', x: Number.NaN, y: 300 },
  ];
  const copy = JSON.parse(JSON.stringify(input.map((a) => (Number.isNaN(a.x) ? { ...a, x: null } : a))));
  const part = partitionAnchors(R, input);
  assert.deepEqual(part.offScreen, ['plain', 'wasOnFar', 'wasOff']);
  assert.deepEqual(part.onScreen, ['wasOn', 'wasOffFar']);
  assert.ok(!part.onScreen.includes('nan') && !part.offScreen.includes('nan'));
  assert.deepEqual(partitionAnchors(R, input, { hysteresis: 20 }).offScreen, ['plain', 'wasOff', 'wasOffFar'], 'a wider hysteresis keeps every remembered member in the state it was in');
  assert.equal(input.length, copy.length);
  assert.ok(!('wasOff' in input[0]) && input[2].wasOff === false);
});

test('the default camera of the fixture fits the four in-view pins (a check on the projection used above)', () => {
  const { zoom, project } = projectDemo();
  assert.ok(zoom > 10.8 && zoom < 10.9, `zoom ${zoom}`); // 01 4.9 says "about 11.8" in a 256 px world, which is 10.8 in MapLibre's 512 px one
  for (const id of ['king', 'queen', 'jester', 'wagon']) {
    const p = project(id);
    assert.ok(p.x >= R.left && p.x <= R.right && p.y >= R.top && p.y <= R.bottom, `${id} at (${p.x}, ${p.y})`);
  }
});

// ---- AC-19 (a): keep-out slide ---------------------------------------------------------------------------------------------------

test('[AC-19a] a bubble inside a keep-out (plus 8 px and its own radius) slides along its edge, the way its member lies', () => {
  const MID = { left: 352, top: 340, right: 400, bottom: 400 }; // on the right edge; grown by 8 + 20 it covers y 312..428
  const up = layoutBubbles(R, [MID], [via('a', { x: 384, y: 350 })]);
  assert.deepEqual([up.bubbles[0].x, up.bubbles[0].y], [384, 312]); // the member is above the centre line: up, to MID.top - 28
  const down = layoutBubbles(R, [MID], [via('a', { x: 384, y: 380 })]);
  assert.deepEqual([down.bubbles[0].x, down.bubbles[0].y], [384, 428]); // below: down, to MID.bottom + 8 + 20
});

test('[AC-19a] a member exactly in line with its bubble slides clockwise (the right edge: down)', () => {
  const MID = { left: 352, top: 340, right: 400, bottom: 400 };
  const result = layoutBubbles(R, [MID], [via('a', { x: 384, y: 366.5 })]);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [384, 428]);
});

test('[AC-19a] the keep-out grows by exactly 8 + 20 px: a bubble on the grown boundary stays, one pixel inside it moves', () => {
  const MID = { left: 352, top: 340, right: 400, bottom: 400 };
  const stays = layoutBubbles(R, [MID], [via('a', { x: 384, y: 312 })]);
  assert.equal(stays.bubbles[0].y, 312);
  const alsoStays = layoutBubbles(R, [MID], [via('a', { x: 384, y: 311 })]);
  assert.equal(alsoStays.bubbles[0].y, 311);
  const moves = layoutBubbles(R, [MID], [via('a', { x: 384, y: 313 })]);
  assert.equal(moves.bubbles[0].y, 312);
  const stays2 = layoutBubbles(R, [MID], [via('a', { x: 384, y: 428 })]);
  assert.equal(stays2.bubbles[0].y, 428);
});

test('[AC-19a] it keeps sliding until clear: a second keep-out behind the first is passed too', () => {
  const first = { left: 352, top: 340, right: 400, bottom: 400 }; // grown: y 312..428
  const second = { left: 352, top: 420, right: 400, bottom: 450 }; // grown: y 392..478
  const result = layoutBubbles(R, [first, second], [via('a', { x: 384, y: 380 })]);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [384, 478]);
});

test('[AC-19a] a bubble slides along the edge it sits on, never onto another edge', () => {
  const MID = { left: 0, top: 340, right: 60, bottom: 400 }; // on the left edge
  const result = layoutBubbles(R, [MID], [via('a', { x: 28, y: 380 })]);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [28, 428]);
  const topEdge = layoutBubbles(R, [{ left: 150, top: 0, right: 210, bottom: 40 }], [via('a', { x: 170, y: 28 })]);
  assert.deepEqual([topEdge.bubbles[0].x, topEdge.bubbles[0].y], [150 - 28, 28]); // the member lies to the left: left, along the top edge
});

test('[AC-19a] the attribution and the right stack in their corners: the bubble slides to the one side that is free', () => {
  // A keep-out in a corner leaves room on one side only: the member's own side (toward the corner) runs off the edge, so it goes the other way.
  const nearAttributionOnTopEdge = layoutBubbles(R, KEEP_OUTS, [via('a', { x: 370, y: 28 })]);
  assert.deepEqual([nearAttributionOnTopEdge.bubbles[0].x, nearAttributionOnTopEdge.bubbles[0].y], [324, 28]); // ATTRIBUTION.left - 28
  const nearAttributionOnRightEdge = layoutBubbles(R, KEEP_OUTS, [via('a', { x: 384, y: 50 })]);
  assert.deepEqual([nearAttributionOnRightEdge.bubbles[0].x, nearAttributionOnRightEdge.bubbles[0].y], [384, 88]); // ATTRIBUTION.bottom + 28
  const nearStack = layoutBubbles(R, KEEP_OUTS, [via('a', { x: 384, y: 650 })]);
  assert.deepEqual([nearStack.bubbles[0].x, nearStack.bubbles[0].y], [384, 593]); // STACK.top - 28
  for (const result of [nearAttributionOnTopEdge, nearAttributionOnRightEdge, nearStack]) {
    for (const keepOut of KEEP_OUTS) assert.ok(gapTo(result.bubbles[0], keepOut) >= 8);
  }
});

test('[AC-19a] the pointer still follows the ray to the member after a slide', () => {
  const MID = { left: 352, top: 340, right: 400, bottom: 400 };
  const result = layoutBubbles(R, [MID], [via('a', { x: 384, y: 380 })]);
  near(result.bubbles[0].angleDeg, (Math.atan2(380 - 366.5, 384 - 206) * 180) / Math.PI);
});

test('[AC-19a] a bubble that cannot clear on its edge joins the nearest bubble as a cluster and leaves it where it is', () => {
  const WALL = { left: 0, top: -100, right: 60, bottom: 1000 }; // the whole left edge is blocked
  const stuck = via('west', { x: 28, y: 600 });
  const bottom = via('south', { x: 200, y: 705 });
  const right = via('east', { x: 384, y: 200 });
  const result = layoutBubbles(R, [WALL], [stuck, right, bottom]);
  assert.equal(result.bubbles.length, 2);
  const joined = bubbleOf(result, 'west');
  assert.deepEqual([...joined.ids].sort(), ['south', 'west']); // (28, 600) is nearer to the bottom bubble than to the right one
  assert.strictEqual(joined.cluster, 2);
  assert.deepEqual([joined.x, joined.y], [200, 705]);
  assert.deepEqual(bubbleOf(result, 'east').ids, ['east']);
});

test('[AC-19a] a bubble that cannot clear and has no neighbour is clamped at the end of its edge', () => {
  const WALL = { left: 0, top: -100, right: 60, bottom: 1000 };
  const up = layoutBubbles(R, [WALL], [via('west', { x: 28, y: 300 })]);
  assert.deepEqual([up.bubbles[0].x, up.bubbles[0].y], [28, 28]); // the member lies above the centre line: the top end
  const down = layoutBubbles(R, [WALL], [via('west', { x: 28, y: 600 })]);
  assert.deepEqual([down.bubbles[0].x, down.bubbles[0].y], [28, 705]);
  const two = layoutBubbles(R, [WALL], [via('a', { x: 28, y: 300 }), via('b', { x: 28, y: 600 })]);
  assert.equal(two.bubbles.length, 1); // the second joins the first (the only bubble there is)
  assert.strictEqual(two.bubbles[0].cluster, 2);
});

// ---- AC-19 (b): clusters ---------------------------------------------------------------------------------------------------------

test('[AC-19b] two anchors with centres under 44 px apart become ONE bubble with the member count (a number, never a boolean)', () => {
  const result = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 343.5 })]);
  assert.equal(result.bubbles.length, 1);
  const [cluster] = result.bubbles;
  assert.deepEqual([...cluster.ids].sort(), ['a', 'b']);
  assert.strictEqual(cluster.cluster, 2);
  assert.equal(typeof cluster.cluster, 'number');
  assert.deepEqual([cluster.x, cluster.y], [384, 321.75]); // the mean of the two anchors
});

test('[AC-19b] the 44 px gap is exclusive: centres exactly 44 px apart stay two bubbles, each with cluster 1', () => {
  for (const gap of [44, 44.5, 60]) {
    const result = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 300 + gap })]);
    assert.equal(result.bubbles.length, 2, `gap ${gap}`);
    for (const bubble of result.bubbles) {
      assert.strictEqual(bubble.cluster, 1);
      assert.equal(typeof bubble.cluster, 'number');
      assert.equal(bubble.ids.length, 1);
    }
  }
  assert.equal(layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 343.5 })]).bubbles.length, 1);
});

test('[AC-19b] a cluster shows the first avatar: the highest priority leads, then the member nearer to the centre, then the id', () => {
  const byPriority = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }, { priority: 200 }), via('b', { x: 384, y: 310 }, { priority: 300 }), via('c', { x: 384, y: 320 })]);
  assert.deepEqual(byPriority.bubbles[0].ids, ['b', 'a', 'c']);
  // No priorities: 'far' is projected farther from the centre than 'close', so 'close' comes first although its id sorts later.
  const byDistance = layoutBubbles(R, [], [via('far', { x: 384, y: 300 }, {}, 16), via('close', { x: 384, y: 310 }, {}, 8)]);
  assert.deepEqual(byDistance.bubbles[0].ids, ['close', 'far']);
  const tie = layoutBubbles(R, [], [{ id: 'z', x: 1000, y: 366.5 }, { id: 'm', x: 1000, y: 366.5 }]);
  assert.deepEqual(tie.bubbles[0].ids, ['m', 'z']);
});

test('[AC-19b] a cluster has no second avatar and no +N pill: the bubble is {ids, x, y, angleDeg, cluster} and nothing else', () => {
  const result = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 310 }), via('c', { x: 384, y: 500 })]);
  assert.equal(result.bubbles.length, 2);
  for (const bubble of result.bubbles) assert.deepEqual(Object.keys(bubble).sort(), ['angleDeg', 'cluster', 'ids', 'x', 'y']);
});

test('[AC-19b] merging is transitive: a chain of bubbles each under 44 px from the next is one cluster of three', () => {
  const result = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 340 }), via('c', { x: 384, y: 380 })]); // 40, 40 and 80 apart
  assert.equal(result.bubbles.length, 1);
  assert.strictEqual(result.bubbles[0].cluster, 3);
  assert.equal(result.bubbles[0].ids.length, 3);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [384, 340]);
});

test('[AC-19b] the count badge is the number of members: 2, 3, 4 and 5 members make cluster 2, 3, 4 and 5', () => {
  for (let n = 2; n <= 5; n += 1) {
    const anchors = Array.from({ length: n }, (_, i) => via(`m${i}`, { x: 384, y: 300 + i * 10 }));
    const result = layoutBubbles(R, [], anchors);
    assert.equal(result.bubbles.length, 1);
    assert.strictEqual(result.bubbles[0].cluster, n);
    assert.equal(result.bubbles[0].ids.length, n);
  }
});

test('[AC-19b] a single bubble has cluster 1, and members far apart never merge', () => {
  const result = layoutBubbles(R, [], [via('n', { x: 206, y: 28 }), via('e', { x: 384, y: 366.5 }), via('s', { x: 206, y: 705 }), via('w', { x: 28, y: 366.5 })]);
  assert.equal(result.bubbles.length, 4);
  for (const bubble of result.bubbles) assert.strictEqual(bubble.cluster, 1);
});

test('[AC-19b] a tighter option merges less: clusterGap 10 keeps 43.5 px apart bubbles apart', () => {
  const result = layoutBubbles(R, [], [via('a', { x: 384, y: 300 }), via('b', { x: 384, y: 343.5 })], { clusterGap: 10 });
  assert.equal(result.bubbles.length, 2);
});

// ---- AC-19 (c): hysteresis -------------------------------------------------------------------------------------------------------

/** One anchor on the vertical middle line, at x, with the previous frame's state. */
const atX = (x, wasOff) => ({ id: 'm', x, y: 366.5, ...(wasOff === undefined ? {} : { wasOff }) });
const isOffAt = (x, wasOff) => layoutBubbles(R, [], [atX(x, wasOff)]).offScreen.length === 1;

test('[AC-19c] with no history the plain rectangle decides (on its boundary is on screen)', () => {
  assert.equal(isOffAt(404, undefined), false);
  assert.equal(isOffAt(404.5, undefined), true);
  assert.equal(isOffAt(8, undefined), false);
  assert.equal(isOffAt(7.5, undefined), true);
});

test('[AC-19c] an on-screen member goes off only beyond 12 px outside R', () => {
  assert.equal(isOffAt(404 + 11, false), false);
  assert.equal(isOffAt(404 + 12, false), false);
  assert.equal(isOffAt(404 + 12.5, false), true);
  assert.equal(isOffAt(8 - 12, false), false);
  assert.equal(isOffAt(8 - 12.5, false), true);
});

test('[AC-19c] an off-screen member comes back only 12 px or more inside R', () => {
  assert.equal(isOffAt(404 - 11, true), true);
  assert.equal(isOffAt(404 - 11.5, true), true);
  assert.equal(isOffAt(404 - 12, true), false);
  assert.equal(isOffAt(404 - 13, true), false);
  assert.equal(isOffAt(8 + 11.5, true), true);
  assert.equal(isOffAt(8 + 12, true), false);
});

test('[AC-19c] the same holds on the top and bottom edges', () => {
  const offAtY = (y, wasOff) => layoutBubbles(R, [], [{ id: 'm', x: 206, y, wasOff }]).offScreen.length === 1;
  assert.equal(offAtY(8 - 12, false), false);
  assert.equal(offAtY(8 - 12.5, false), true);
  assert.equal(offAtY(8 + 11.5, true), true);
  assert.equal(offAtY(8 + 12, true), false);
  assert.equal(offAtY(725 + 12, false), false);
  assert.equal(offAtY(725 + 12.5, false), true);
  assert.equal(offAtY(725 - 11.5, true), true);
  assert.equal(offAtY(725 - 12, true), false);
});

test('[AC-19c] a member jittering across the boundary does not flicker: it changes state only after crossing the 24 px band', () => {
  // Frame by frame, feeding each result's offScreen back as the next frame's wasOff (x on the right edge of R, which is x = 404).
  const xs = [420, 410, 398, 410, 398, 410, 392, 396, 410, 398, 416, 416.5];
  const expectedOff = [true, true, true, true, true, true, false, false, false, false, false, true];
  /** @type {boolean | undefined} */
  let wasOff;
  const seen = [];
  for (const x of xs) {
    const result = layoutBubbles(R, [], [atX(x, wasOff)]);
    wasOff = result.offScreen.includes('m');
    seen.push(wasOff);
  }
  assert.deepEqual(seen, expectedOff);
  assert.equal(seen.filter((s, i) => i > 0 && s !== seen[i - 1]).length, 2); // two crossings; the plain rectangle would flip on nearly every frame
});

test('[AC-19c] a member kept off screen by the hysteresis still has its bubble, on the inset rectangle along the ray', () => {
  const result = layoutBubbles(R, KEEP_OUTS, [atX(398, true)]);
  assert.deepEqual(result.offScreen, ['m']);
  assert.deepEqual(result.onScreen, []);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [384, 366.5]);
  assert.equal(result.bubbles[0].angleDeg, 0);
});

test('[AC-19c] the hysteresis follows the option', () => {
  assert.equal(layoutBubbles(R, [], [atX(404 + 20, false)], { hysteresis: 20 }).offScreen.length, 0);
  assert.equal(layoutBubbles(R, [], [atX(404 + 21, false)], { hysteresis: 20 }).offScreen.length, 1);
});

// ---- AC-19 (d): the cluster anchor snaps back onto the inset rectangle ------------------------------------------------------------

/** Where the ray from the centre of R through `p` meets the inset rectangle (an independent restatement, for the expected values). */
function snapToBox(p) {
  const dx = p.x - CENTRE.x;
  const dy = p.y - CENTRE.y;
  const s = Math.min(dx > 0 ? (BOX.right - CENTRE.x) / dx : (BOX.left - CENTRE.x) / dx, dy > 0 ? (BOX.bottom - CENTRE.y) / dy : (BOX.top - CENTRE.y) / dy);
  return { x: CENTRE.x + dx * s, y: CENTRE.y + dy * s };
}

test('[AC-19d] the mean of two bubbles around a corner lies inside the rectangle; the cluster anchor is snapped back onto its edge', () => {
  const a = { x: 28, y: 680 }; // left edge, near the bottom-left corner
  const b = { x: 50, y: 705 }; // bottom edge
  const result = layoutBubbles(R, [], [via('a', a), via('b', b)]);
  assert.equal(result.bubbles.length, 1);
  const [cluster] = result.bubbles;
  assert.strictEqual(cluster.cluster, 2);
  const mean = { x: 39, y: 692.5 }; // strictly inside the inset rectangle (28..384, 28..705)
  assert.ok(mean.x > BOX.left && mean.y < BOX.bottom);
  const expected = snapToBox(mean);
  assert.ok(onBoxBoundary(cluster), `(${cluster.x}, ${cluster.y}) is not on the inset rectangle`);
  near(cluster.x, expected.x, 1e-6);
  near(cluster.y, expected.y, 1e-6);
  assert.ok(Math.hypot(cluster.x - mean.x, cluster.y - mean.y) > 5, 'the anchor moved onto the rectangle');
  near(cluster.angleDeg, (Math.atan2(mean.y - CENTRE.y, mean.x - CENTRE.x) * 180) / Math.PI, 1e-9); // the pointer follows the same ray
});

test('[AC-19d] snap-back also holds at the other three corners', () => {
  const pairs = [
    [{ x: 384, y: 40 }, { x: 350, y: 28 }], // top right
    [{ x: 28, y: 50 }, { x: 60, y: 28 }], // top left
    [{ x: 384, y: 680 }, { x: 355, y: 705 }], // bottom right
  ];
  for (const [p, q] of pairs) {
    const result = layoutBubbles(R, [], [via('a', p), via('b', q)]);
    assert.equal(result.bubbles.length, 1, JSON.stringify([p, q]));
    const mean = { x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 };
    const expected = snapToBox(mean);
    assert.ok(onBoxBoundary(result.bubbles[0]));
    near(result.bubbles[0].x, expected.x, 1e-6);
    near(result.bubbles[0].y, expected.y, 1e-6);
    assert.ok(Math.hypot(expected.x - mean.x, expected.y - mean.y) > 3, 'the mean was inside the rectangle, so the anchor moved');
  }
});

test('[AC-19d] a cluster on a single edge stays on that edge at the mean', () => {
  const result = layoutBubbles(R, [], [via('a', { x: 384, y: 400 }), via('b', { x: 384, y: 420 })]);
  assert.deepEqual([result.bubbles[0].x, result.bubbles[0].y], [384, 410]);
});

// ---- contract points -------------------------------------------------------------------------------------------------------------

test('the defaults are the values of 01 section 4.10', () => {
  assert.deepEqual({ ...BUBBLE_DEFAULTS }, { radius: 20, hysteresis: 12, clusterGap: 44, keepOutPad: 8 });
});

test('the bubble sits where the ray from the centre of R meets R inset by 20, and the angle is the ray angle (east 0, south 90, north -90)', () => {
  const east = layoutBubbles(R, [], [{ id: 'e', x: 1000, y: 366.5 }]).bubbles[0];
  assert.deepEqual([east.x, east.y, east.angleDeg], [384, 366.5, 0]);
  const south = layoutBubbles(R, [], [{ id: 's', x: 206, y: 3000 }]).bubbles[0];
  assert.deepEqual([south.x, south.y, south.angleDeg], [206, 705, 90]);
  const north = layoutBubbles(R, [], [{ id: 'n', x: 206, y: -3000 }]).bubbles[0];
  assert.deepEqual([north.x, north.y, north.angleDeg], [206, 28, -90]);
  const west = layoutBubbles(R, [], [{ id: 'w', x: -500, y: 366.5 }]).bubbles[0];
  assert.deepEqual([west.x, west.y, Math.abs(west.angleDeg)], [28, 366.5, 180]);
  const diagonal = layoutBubbles(R, [], [{ id: 'd', x: 206 + 1000, y: 366.5 + 1000 }]).bubbles[0];
  near(diagonal.angleDeg, 45);
  assert.deepEqual([diagonal.x, diagonal.y], [384, 366.5 + (384 - 206)]); // 178 px east of the centre is the right edge, and the 45 degree ray is 178 px down by then
});

test('the panel in Expanded moves the left edge: a member to the west lands at panel right + 16 + the radius', () => {
  const expanded = { left: 432, top: 8, right: 884 - 8, bottom: 916 - 80 };
  const result = layoutBubbles(expanded, [], [{ id: 'w', x: 0, y: 400 }]);
  assert.equal(result.bubbles[0].x, 432 + 20);
});

test('bubbles come out clockwise from the top-left of the inset rectangle (the DOM order of 01 section 10.2)', () => {
  const anchors = [via('west', { x: 28, y: 400 }), via('south', { x: 200, y: 705 }), via('east', { x: 384, y: 200 }), via('north', { x: 200, y: 28 })];
  assert.deepEqual(layoutBubbles(R, [], anchors).bubbles.map((b) => b.ids[0]), ['north', 'east', 'south', 'west']);
});

test('pure and deterministic: inputs are not mutated, and the order of the anchors does not matter', () => {
  const anchors = deepFreeze([
    via('a', { x: 384, y: 300 }, { priority: 200 }),
    via('b', { x: 384, y: 320 }, { priority: 300 }),
    via('c', { x: 28, y: 50 }),
    via('d', { x: 200, y: 705 }),
    { id: 'on', x: 200, y: 300 },
  ]);
  const rect = deepFreeze({ ...R });
  const keepOuts = deepFreeze(KEEP_OUTS.map((k) => ({ ...k })));
  const forward = layoutBubbles(rect, keepOuts, anchors);
  assert.deepEqual(layoutBubbles(rect, keepOuts, anchors), forward);
  assert.deepEqual(layoutBubbles(rect, keepOuts, [...anchors].reverse()).bubbles, forward.bubbles);
  assert.deepEqual(forward.onScreen, ['on']);
  assert.deepEqual(forward.offScreen, ['a', 'b', 'c', 'd']);
});

test('an anchor with a non-finite coordinate is ignored (neither on screen nor off)', () => {
  const result = layoutBubbles(R, [], [{ id: 'nan', x: Number.NaN, y: 10 }, { id: 'inf', x: 10, y: Number.POSITIVE_INFINITY }, { id: 'ok', x: 1000, y: 366.5 }]);
  assert.deepEqual(result.offScreen, ['ok']);
  assert.deepEqual(result.onScreen, []);
  assert.equal(result.bubbles.length, 1);
});

test('no anchors, no bubbles; a rectangle too small for the bubbles collapses to its middle without NaN', () => {
  assert.deepEqual(layoutBubbles(R, KEEP_OUTS, []), { bubbles: [], onScreen: [], offScreen: [] });
  const tiny = layoutBubbles({ left: 8, top: 8, right: 38, bottom: 30 }, [], [{ id: 'a', x: 500, y: 500 }, { id: 'b', x: 8, y: 8 }]);
  for (const bubble of tiny.bubbles) assert.ok(Number.isFinite(bubble.x) && Number.isFinite(bubble.y) && Number.isFinite(bubble.angleDeg));
});

test('a member on the very centre of R (only reachable through a huge hysteresis) still gets a finite bubble', () => {
  const result = layoutBubbles(R, [], [{ id: 'c', x: CENTRE.x, y: CENTRE.y, wasOff: true }], { hysteresis: 1000 });
  assert.equal(result.bubbles.length, 1);
  assert.ok(Number.isFinite(result.bubbles[0].x) && Number.isFinite(result.bubbles[0].y));
});

test('property: random layouts keep every member once, every bubble on the inset rectangle, no two bubbles under 44 px apart and cluster = member count', () => {
  let seed = 20261002;
  const random = () => {
    seed = (seed * 1664525 + 1013904223) % 4294967296;
    return seed / 4294967296;
  };
  for (let round = 0; round < 400; round += 1) {
    const count = 1 + Math.floor(random() * 8);
    const anchors = Array.from({ length: count }, (_, i) => ({ id: `m${i}`, x: -600 + random() * 1700, y: -600 + random() * 2200, priority: Math.floor(random() * 3) * 100 }));
    const result = layoutBubbles(R, KEEP_OUTS, anchors);
    const expectedOff = anchors.filter((a) => a.x < R.left || a.x > R.right || a.y < R.top || a.y > R.bottom).map((a) => a.id);
    assert.deepEqual(result.offScreen, expectedOff, `round ${round}`);
    assert.deepEqual(result.bubbles.flatMap((b) => b.ids).sort(), [...expectedOff].sort(), `round ${round}`);
    for (const bubble of result.bubbles) {
      assert.ok(Number.isInteger(bubble.cluster) && bubble.cluster === bubble.ids.length && bubble.cluster >= 1, `round ${round}`);
      assert.ok(onBoxBoundary(bubble), `round ${round}: (${bubble.x}, ${bubble.y})`);
      for (const keepOut of KEEP_OUTS) assert.ok(gapTo(bubble, keepOut) >= 8 - 1e-9, `round ${round}: (${bubble.x}, ${bubble.y}) overlaps ${JSON.stringify(keepOut)}`);
    }
    for (let i = 0; i < result.bubbles.length; i += 1) {
      for (let j = i + 1; j < result.bubbles.length; j += 1) {
        const distance = Math.hypot(result.bubbles[i].x - result.bubbles[j].x, result.bubbles[i].y - result.bubbles[j].y);
        assert.ok(distance >= 44, `round ${round}: bubbles ${i} and ${j} are ${distance} px apart`);
      }
    }
  }
});
