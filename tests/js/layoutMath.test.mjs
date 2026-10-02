// Tests for wwwroot/js/layoutMath.js: the pure layout arithmetic of the map (03 section 4.1, 01 sections 3.2, 3.4.3 and 4.11).
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { EARTH_RADIUS_M, metersPerPixel } from '../../src/Realm.Web/wwwroot/js/geo.js';
import {
  AT_DEFAULT_METERS,
  AT_DEFAULT_ZOOM,
  CHIP_CARET_INSET_PX,
  CHIP_EDGE_PX,
  DEFAULT_LAYOUT,
  FAR_FLIGHT_MS,
  FAR_FLIGHT_ZOOM,
  PLACE_FIT_GROWTH,
  PLACE_FIT_MAX_ZOOM,
  RECENTER_EASE_MS,
  RECENTER_SETTLE_MS,
  RECENTER_SKIP_PX,
  SELECTION_EASE_MS,
  SELECTION_MIN_ZOOM,
  chipCaretX,
  chipRoom,
  clampChipShift,
  computePadding,
  fitCircle,
  haversine,
  isAtDefault,
  isCentered,
  layoutFootprint,
  navTotalPx,
  nextRecenter,
  peekSheetHeight,
  quantizeZoom,
  rectCenter,
  refitNeeded,
  selectionFlightPlan,
  selectionPadding,
  visibleRect,
  zoomToFit,
} from '../../src/Realm.Web/wwwroot/js/layoutMath.js';

const PHONE = { width: 412, height: 915 };
const NO_INSET = { top: 0, right: 0, bottom: 0, left: 0 };
const compact = (over = {}) => ({ ...DEFAULT_LAYOUT, ...over });
const expanded = (over = {}) => ({ ...DEFAULT_LAYOUT, mode: 'expanded', panelLeftPx: 16, panelWidthPx: 400, ...over });

test('Compact at Peek on 412 x 915: {72, 72, 190, 16} (sheet 174 plus the 16 px gutter)', () => {
  assert.deepEqual(computePadding(PHONE, compact()), { top: 72, right: 72, bottom: 190, left: 16 });
});

test('Compact at Tall (a measured 732 px sheet, stack hidden): {72, 16, 748, 16}', () => {
  assert.deepEqual(computePadding(PHONE, compact({ stackVisible: false }), 732), { top: 72, right: 16, bottom: 748, left: 16 });
});

test('Compact: the measured sheet height wins over the Peek default, and a non-finite one falls back to Peek', () => {
  assert.equal(computePadding(PHONE, compact(), 300).bottom, 316);
  assert.equal(computePadding(PHONE, compact(), null).bottom, 190);
  assert.equal(computePadding(PHONE, compact(), Number.NaN).bottom, 190);
  assert.equal(computePadding(PHONE, compact(), Number.POSITIVE_INFINITY).bottom, 190);
});

test('Expanded: the left padding clears the panel (16 + 400 + 16 = 432), the bottom clears the navigation', () => {
  assert.deepEqual(computePadding({ width: 1280, height: 800 }, expanded()), { top: 72, right: 72, bottom: 80, left: 432 });
});

test('Expanded with the panel hidden: the left padding is the plain gutter', () => {
  const padding = computePadding({ width: 1280, height: 800 }, expanded({ panelHidden: true }));
  assert.equal(padding.left, 16);
  assert.equal(computePadding({ width: 1280, height: 800 }, expanded({ panelHidden: true, safe: { ...NO_INSET, left: 8 } })).left, 24);
});

test('Expanded ignores the measured sheet height (there is no sheet)', () => {
  assert.equal(computePadding({ width: 1280, height: 800 }, expanded(), 500).bottom, 80);
});

test('safe-area insets are added to each side', () => {
  const layout = compact({ safe: { top: 24, right: 10, bottom: 34, left: 8 } });
  // navTotal 64 + 34 = 98, so Peek is max(174, 98 + 104) = 202
  assert.deepEqual(computePadding(PHONE, layout), { top: 96, right: 82, bottom: 218, left: 24 });
  const noStack = compact({ stackVisible: false, safe: { top: 0, right: 10, bottom: 0, left: 0 } });
  assert.equal(computePadding(PHONE, noStack).right, 26);
});

test('412 x 800 at Peek: the sheet is held at the NAV_H + 104 = 168 px floor', () => {
  assert.equal(peekSheetHeight(800, 64), 168);
  assert.equal(computePadding({ width: 412, height: 800 }, compact()).bottom, 184);
});

test('peekSheetHeight: max(round(19 percent of the height), navTotal + 104)', () => {
  assert.equal(peekSheetHeight(915, 64), 174);
  assert.equal(peekSheetHeight(1200, 64), 228);
  assert.equal(peekSheetHeight(915, 98), 202);
  assert.equal(peekSheetHeight(0, 64), 168);
});

test('navTotalPx: the bar plus the bottom inset', () => {
  assert.equal(navTotalPx(compact()), 64);
  assert.equal(navTotalPx(compact({ safe: { ...NO_INSET, bottom: 34 } })), 98);
});

test('at least 48 px of map stay visible: the bottom padding gives way', () => {
  const short = { width: 412, height: 300 };
  const padding = computePadding(short, compact(), 250);
  assert.equal(padding.top, 72);
  assert.equal(padding.bottom, 180);
  const rect = visibleRect(short, padding);
  assert.equal(rect.bottom - rect.top, 48);
  // a viewport shorter than the top clearance alone cannot go negative
  assert.equal(computePadding({ width: 412, height: 100 }, compact(), 250).bottom, 0);
  // when it fits, nothing is reduced
  assert.equal(computePadding(PHONE, compact(), 732).bottom, 748);
});

test('computePadding does not modify the layout it reads', () => {
  const layout = compact({ safe: { top: 1, right: 2, bottom: 3, left: 4 } });
  const before = JSON.stringify(layout);
  computePadding(PHONE, layout, 400);
  assert.equal(JSON.stringify(layout), before);
});

test('DEFAULT_LAYOUT: Compact, sheet at Peek, stack visible, no inset; frozen', () => {
  assert.equal(DEFAULT_LAYOUT.mode, 'compact');
  assert.equal(DEFAULT_LAYOUT.stackVisible, true);
  assert.equal(DEFAULT_LAYOUT.panelHidden, false);
  assert.deepEqual(DEFAULT_LAYOUT.safe, NO_INSET);
  assert.equal(DEFAULT_LAYOUT.navHeightPx, 64);
  assert.ok(Object.isFrozen(DEFAULT_LAYOUT));
});

test('visibleRect and rectCenter: the part of the container the padding leaves, and its middle', () => {
  const padding = computePadding(PHONE, compact());
  const rect = visibleRect(PHONE, padding);
  assert.deepEqual(rect, { left: 16, top: 72, right: 340, bottom: 725 });
  assert.deepEqual(rectCenter(rect), { x: 178, y: 398.5 });
});

test('haversine: great-circle metres on the sphere MapLibre uses', () => {
  assert.equal(haversine([-85.341, 31.099], [-85.341, 31.099]), 0);
  const oneDegree = (EARTH_RADIUS_M * Math.PI) / 180;
  assert.ok(Math.abs(haversine([0, 0], [0, 1]) - oneDegree) < 1e-6);
  assert.ok(Math.abs(haversine([0, 0], [1, 0]) - oneDegree) < 1e-6);
  assert.ok(Math.abs(haversine([179.5, 0], [-179.5, 0]) - oneDegree) < 1e-6, 'across the antimeridian');
  assert.equal(haversine([-85.341, 31.099], [-82.7291, 31.3382]), haversine([-82.7291, 31.3382], [-85.341, 31.099]));
});

// A point `meters` due north of `center`.
const north = (center, meters) => [center[0], center[1] + ((meters / EARTH_RADIUS_M) * 180) / Math.PI];

test('isAtDefault: within 40 m and 0.3 zoom of the target', () => {
  const target = { center: [-85.341, 31.099], zoom: 15 };
  assert.equal(AT_DEFAULT_METERS, 40);
  assert.equal(AT_DEFAULT_ZOOM, 0.3);
  assert.equal(isAtDefault(target, target), true);
  assert.equal(isAtDefault({ center: north(target.center, 39), zoom: 15 }, target), true);
  assert.equal(isAtDefault({ center: north(target.center, 41), zoom: 15 }, target), false);
  assert.equal(isAtDefault({ center: target.center, zoom: 15.29 }, target), true);
  assert.equal(isAtDefault({ center: target.center, zoom: 14.71 }, target), true);
  assert.equal(isAtDefault({ center: target.center, zoom: 15.31 }, target), false);
  assert.equal(isAtDefault({ center: target.center, zoom: 14.69 }, target), false);
});

test('isAtDefault: false without a target; the tolerances can be overridden', () => {
  const camera = { center: [-85.341, 31.099], zoom: 15 };
  assert.equal(isAtDefault(camera, null), false);
  assert.equal(isAtDefault(camera, undefined), false);
  const target = { center: north(camera.center, 100), zoom: 15.5 };
  assert.equal(isAtDefault(camera, target), false);
  assert.equal(isAtDefault(camera, target, { meters: 150, zoom: 0.6 }), true);
  assert.equal(isAtDefault(camera, target, { meters: 150 }), false);
});

test('quantizeZoom: rounds to the step (0.05 by default) without float noise', () => {
  assert.equal(quantizeZoom(12.34), 12.35);
  assert.equal(quantizeZoom(12.32), 12.3);
  assert.equal(quantizeZoom(14.1), 14.1);
  assert.equal(quantizeZoom(14), 14);
  assert.equal(quantizeZoom(14.37, 0.25), 14.25);
  assert.equal(quantizeZoom(14.4, 0.25), 14.5);
});

// ---- S8a: selection flights (D45), the refit on a layout change and the chip clamp (D75) -------------------------------------------------------------------------

test('selectionPadding, Compact: the Peek padding {72, 72, 190, 16} at 412 x 915 and {72, 72, 184, 16} at 412 x 800', () => {
  assert.deepEqual(selectionPadding(PHONE, compact()), { top: 72, right: 72, bottom: 190, left: 16 });
  assert.deepEqual(selectionPadding({ width: 412, height: 800 }, compact()), { top: 72, right: 72, bottom: 184, left: 16 });
});

test('selectionPadding, Compact: the TARGET Peek padding, whatever the sheet is at while the flight starts (a row tapped at Tall)', () => {
  // At Tall the measured padding is {72, 16, 748, 16} (stack hidden, a 732 px sheet): the flight must not use it.
  const tall = compact({ stackVisible: false });
  assert.deepEqual(computePadding(PHONE, tall, 732), { top: 72, right: 16, bottom: 748, left: 16 });
  assert.deepEqual(selectionPadding(PHONE, tall), { top: 72, right: 72, bottom: 190, left: 16 });
  assert.deepEqual(selectionPadding(PHONE, tall), computePadding(PHONE, compact(), null));
});

test('selectionPadding, Compact: AC-22, the Peek rectangle is x 16 to 340, y 72 to 725 and its centre is within 24 px of (178, 399)', () => {
  const rect = visibleRect(PHONE, selectionPadding(PHONE, compact()));
  assert.deepEqual(rect, { left: 16, top: 72, right: 340, bottom: 725 });
  const centre = rectCenter(rect);
  assert.ok(Math.abs(centre.x - 178) <= 24 && Math.abs(centre.y - 399) <= 24, `centre (${centre.x}, ${centre.y})`);
  // the strip a flight against the Tall padding would have used is 95 px high: nowhere near
  const strip = rectCenter(visibleRect(PHONE, computePadding(PHONE, compact({ stackVisible: false }), 732)));
  assert.ok(Math.abs(strip.y - 399) > 24);
});

test('selectionPadding, Compact: safe insets are added and the 48 px minimum visible height still applies', () => {
  const layout = compact({ safe: { top: 24, right: 10, bottom: 34, left: 8 } });
  assert.deepEqual(selectionPadding(PHONE, layout), { top: 96, right: 82, bottom: 218, left: 24 });
  const short = { width: 412, height: 200 };
  const padding = selectionPadding(short, compact());
  assert.equal(padding.bottom, 80);
  const rect = visibleRect(short, padding);
  assert.equal(rect.bottom - rect.top, 48);
});

test('selectionPadding, Expanded: the panel padding, which is the measured one: {72, 72, 80, 432}, left 16 with the panel hidden', () => {
  const wide = { width: 1280, height: 800 };
  assert.deepEqual(selectionPadding(wide, expanded()), { top: 72, right: 72, bottom: 80, left: 432 });
  assert.deepEqual(selectionPadding(wide, expanded()), computePadding(wide, expanded()));
  assert.equal(selectionPadding(wide, expanded({ panelHidden: true })).left, 16);
});

test('selectionPadding does not modify the layout it reads', () => {
  const layout = compact({ stackVisible: false });
  const before = JSON.stringify(layout);
  selectionPadding(PHONE, layout);
  assert.equal(JSON.stringify(layout), before);
});

test('selectionFlightPlan: an on-screen member eases to max(current zoom, 15) over 600 ms', () => {
  assert.equal(SELECTION_MIN_ZOOM, 15);
  assert.equal(SELECTION_EASE_MS, 600);
  assert.deepEqual(selectionFlightPlan({ onScreen: true, currentZoom: 11.8 }), { kind: 'ease', zoom: 15, durationMs: 600 });
  assert.deepEqual(selectionFlightPlan({ onScreen: true, currentZoom: 17.2 }), { kind: 'ease', zoom: 17.2, durationMs: 600 });
  assert.equal(selectionFlightPlan({ onScreen: true, currentZoom: 11 }).zoom, 15);
  assert.equal(selectionFlightPlan({ onScreen: true, currentZoom: 11, minZoom: 16 }).zoom, 16);
  assert.equal(selectionFlightPlan({ onScreen: true, currentZoom: 18, minZoom: 16 }).zoom, 18);
});

test('selectionFlightPlan: a far member who is off screen flies to zoom 13 over 900 ms; a far member on screen eases like anyone', () => {
  assert.equal(FAR_FLIGHT_ZOOM, 13);
  assert.equal(FAR_FLIGHT_MS, 900);
  assert.deepEqual(selectionFlightPlan({ far: true, onScreen: false, currentZoom: 11.8 }), { kind: 'fly', zoom: 13, durationMs: 900 });
  assert.deepEqual(selectionFlightPlan({ far: true, onScreen: false, currentZoom: 16 }), { kind: 'fly', zoom: 13, durationMs: 900 });
  assert.deepEqual(selectionFlightPlan({ far: true, onScreen: true, currentZoom: 11.8 }), { kind: 'ease', zoom: 15, durationMs: 600 });
  assert.deepEqual(selectionFlightPlan({ far: false, onScreen: false, currentZoom: 11.8 }), { kind: 'ease', zoom: 15, durationMs: 600 });
});

test('selectionFlightPlan: every duration is 0 under reduced motion', () => {
  assert.equal(selectionFlightPlan({ onScreen: true, currentZoom: 12, reducedMotion: true }).durationMs, 0);
  assert.equal(selectionFlightPlan({ far: true, onScreen: false, currentZoom: 12, reducedMotion: true }).durationMs, 0);
  assert.equal(selectionFlightPlan({ far: true, onScreen: false, currentZoom: 12, reducedMotion: true }).zoom, 13);
});

test('zoomToFit: at the zoom it returns, the circle is exactly sidePx across', () => {
  for (const lat of [0, 31.099, 52, -33.9]) {
    for (const [diameterM, sidePx] of [[360, 324], [2000, 597], [90, 300]]) {
      const zoom = zoomToFit(diameterM, lat, sidePx);
      assert.ok(Math.abs((diameterM / metersPerPixel(zoom, lat)) - sidePx) < 1e-6, `lat ${lat}, ${diameterM} m, ${sidePx} px`);
    }
  }
  assert.equal(zoomToFit(0, 40, 300), Number.POSITIVE_INFINITY);
  assert.equal(zoomToFit(300, 40, 0), Number.POSITIVE_INFINITY);
});

test('fitCircle: the circle grown 20 percent fills the shorter side of the Peek rectangle less the 56 px pin allowance', () => {
  assert.equal(PLACE_FIT_GROWTH, 0.2);
  assert.equal(PLACE_FIT_MAX_ZOOM, 16);
  const rect = visibleRect(PHONE, selectionPadding(PHONE, compact())); // 324 wide, 653 high less 56 = 597
  const lat = 31.099;
  const pose = fitCircle([-85.341, lat], 300, rect);
  assert.ok(Math.abs((2 * 300 * 1.2) / metersPerPixel(pose.zoom, lat) - 324) < 1e-6, 'the width (324) is the shorter side');
  // a wide, low rectangle is limited by its height less the allowance
  const low = { left: 0, top: 0, right: 800, bottom: 256 };
  const lowPose = fitCircle([-85.341, lat], 300, low);
  assert.ok(Math.abs((2 * 300 * 1.2) / metersPerPixel(lowPose.zoom, lat) - 200) < 1e-6, '256 less 56 is 200');
  // the grow option
  const bigger = fitCircle([-85.341, lat], 300, rect, { growth: 0.5 });
  assert.ok(bigger.zoom < pose.zoom);
  assert.ok(Math.abs((2 * 300 * 1.5) / metersPerPixel(bigger.zoom, lat) - 324) < 1e-6);
});

test('fitCircle: never zooms in past 16, and never out past the minimum zoom', () => {
  const rect = visibleRect(PHONE, selectionPadding(PHONE, compact()));
  assert.equal(fitCircle([-85.341, 31.099], 10, rect).zoom, 16);
  assert.equal(fitCircle([-85.341, 31.099], 0, rect).zoom, 16);
  assert.equal(fitCircle([-85.341, 31.099], 10, rect, { maxZoom: 14 }).zoom, 14);
  assert.equal(fitCircle([-85.341, 31.099], 4_000_000, rect, { minZoom: 3 }).zoom, 3);
});

test('fitCircle: the camera centre sits 28 px (half the allowance) above the circle centre, on the same meridian', () => {
  const rect = visibleRect(PHONE, selectionPadding(PHONE, compact()));
  const centre = [-85.341, 31.099];
  const pose = fitCircle(centre, 300, rect);
  assert.equal(pose.center[0], centre[0]);
  assert.ok(pose.center[1] > centre[1], 'north of the circle');
  const px = haversine(pose.center, centre) / metersPerPixel(pose.zoom, centre[1]);
  assert.ok(Math.abs(px - 28) < 0.05, `${px} px`);
  // with no allowance the circle is simply centred
  const unshifted = fitCircle(centre, 300, rect, { topAllowancePx: 0 }).center;
  assert.equal(unshifted[0], centre[0]);
  assert.ok(Math.abs(unshifted[1] - centre[1]) < 1e-9);
});

test('layoutFootprint: the mode and the room the panel takes; sheet, stack and safe insets are not part of it', () => {
  assert.deepEqual(layoutFootprint(compact()), { mode: 'compact', panelWidthPx: 0 });
  assert.deepEqual(layoutFootprint(compact({ stackVisible: false, safe: { top: 24, right: 0, bottom: 34, left: 0 } })), { mode: 'compact', panelWidthPx: 0 });
  assert.deepEqual(layoutFootprint(expanded()), { mode: 'expanded', panelWidthPx: 416 });
  assert.deepEqual(layoutFootprint(expanded({ panelHidden: true })), { mode: 'expanded', panelWidthPx: 0 });
});

test('refitNeeded (D75): the camera is at the default view and the layout switched Compact to Expanded, or back', () => {
  assert.equal(refitNeeded(compact(), expanded(), true), true, 'unfold: the pins must not sit under the 400 px panel');
  assert.equal(refitNeeded(expanded(), compact(), true), true, 'fold');
  assert.equal(refitNeeded(DEFAULT_LAYOUT, expanded(), true), true, 'the layout the map assumes until the first setLayout counts as the previous one');
});

test('refitNeeded (D75): the panel appearing, disappearing or changing width at the default view', () => {
  assert.equal(refitNeeded(expanded({ panelHidden: true }), expanded(), true), true, 'the panel appears');
  assert.equal(refitNeeded(expanded(), expanded({ panelHidden: true }), true), true, 'the panel is folded away');
  assert.equal(refitNeeded(expanded(), expanded({ panelWidthPx: 420 }), true), true, 'the panel width changes');
});

test('refitNeeded (D75): not when the camera was moved away from the default view, and not without a previous layout', () => {
  assert.equal(refitNeeded(compact(), expanded(), false), false);
  assert.equal(refitNeeded(expanded(), compact(), false), false);
  assert.equal(refitNeeded(null, expanded(), true), false);
  assert.equal(refitNeeded(undefined, expanded(), true), false);
});

test('refitNeeded (D75): not when only the sheet, the right stack or the safe insets changed, nor when nothing changed', () => {
  assert.equal(refitNeeded(compact(), compact({ stackVisible: false }), true), false, 'the sheet went Tall');
  assert.equal(refitNeeded(compact(), compact({ safe: { top: 24, right: 0, bottom: 34, left: 0 } }), true), false);
  assert.equal(refitNeeded(compact(), compact(), true), false);
  assert.equal(refitNeeded(expanded(), expanded(), true), false);
  assert.equal(refitNeeded(expanded(), expanded({ safe: { top: 24, right: 0, bottom: 0, left: 0 }, stackVisible: false }), true), false);
});

test('refitNeeded: the layout sequence of a fold and an unfold, with a pan in between', () => {
  // at default: unfold refits, and the refit leaves the camera at default again
  assert.equal(refitNeeded(compact(), expanded(), true), true);
  assert.equal(refitNeeded(expanded(), compact(), true), true);
  // the user panned: neither refits
  assert.equal(refitNeeded(compact(), expanded(), false), false);
});

test('nextRecenter: away goes to the default view, the default view to me alone, me alone back to the default view', () => {
  assert.equal(nextRecenter('away', true), 'default');
  assert.equal(nextRecenter('away', false), 'default');
  assert.equal(nextRecenter('default', true), 'me');
  assert.equal(nextRecenter('default', false), 'default', 'there is no me-alone target');
  assert.equal(nextRecenter('me', true), 'default');
  assert.equal(nextRecenter('me', false), 'default');
});

test('isCentered: within 2 px of the middle of the rectangle; the re-centre after a sheet change skips it', () => {
  assert.equal(RECENTER_SKIP_PX, 2);
  assert.equal(RECENTER_SETTLE_MS, 120);
  assert.equal(RECENTER_EASE_MS, 250);
  const rect = visibleRect(PHONE, selectionPadding(PHONE, compact())); // centre (178, 398.5)
  assert.equal(isCentered({ x: 178, y: 398.5 }, rect), true);
  assert.equal(isCentered({ x: 179.9, y: 398.5 }, rect), true);
  assert.equal(isCentered({ x: 178, y: 400.5 }, rect), true);
  assert.equal(isCentered({ x: 178, y: 400.6 }, rect), false);
  assert.equal(isCentered({ x: 179.5, y: 399.9 }, rect), false, 'the distance, not each axis, is compared');
  // after a handle tap to Tall the same pin is 330 px from the middle of the strip: not skipped
  const strip = visibleRect(PHONE, computePadding(PHONE, compact({ stackVisible: false }), 732));
  assert.equal(isCentered({ x: 178, y: 398.5 }, strip), false);
  assert.equal(isCentered({ x: 178, y: 398.5 }, strip, 400), true, 'the tolerance is a parameter');
});

const CHIP = 196; // "Here for 3 hrs, 33 mins": about 196 px wide

test('chipRoom: the whole map less 8 px each side, and in Expanded with the panel shown only the part right of the panel', () => {
  assert.equal(CHIP_EDGE_PX, 8);
  assert.deepEqual(chipRoom(PHONE, compact(), computePadding(PHONE, compact())), { left: 8, right: 404 });
  const wide = { width: 884, height: 916 };
  assert.deepEqual(chipRoom(wide, expanded(), computePadding(wide, expanded())), { left: 432, right: 876 });
  assert.deepEqual(chipRoom(wide, expanded({ panelHidden: true }), computePadding(wide, expanded({ panelHidden: true }))), { left: 8, right: 876 });
});

test('clampChipShift: a chip that fits is not moved', () => {
  const room = { left: 8, right: 404 };
  assert.equal(clampChipShift(200, CHIP, room), 0);
  assert.equal(clampChipShift(106, CHIP, room), 0, 'its left edge is exactly 8');
  assert.equal(clampChipShift(306, CHIP, room), 0, 'its right edge is exactly 404');
});

test('clampChipShift: a pin near the left edge, the right edge: the chip is brought inside', () => {
  const room = { left: 8, right: 404 };
  assert.equal(clampChipShift(50, CHIP, room), 56, 'left edge would be -48, so 56 to the right');
  assert.equal(clampChipShift(0, CHIP, room), 106);
  assert.equal(clampChipShift(380, CHIP, room), -74, 'right edge would be 478, so 74 to the left');
  assert.equal(clampChipShift(900, CHIP, room), -594, 'a pin far off the right edge still gets a chip at the edge');
});

test('clampChipShift: the Expanded panel keeps the chip right of itself', () => {
  const room = { left: 432, right: 876 };
  // the King's pin just right of the panel edge: the chip would reach 98 px under the panel
  assert.equal(clampChipShift(440, CHIP, room), 90);
  assert.equal(440 - CHIP / 2 + 90, 432);
  assert.equal(clampChipShift(700, CHIP, room), 0);
});

test('clampChipShift: a chip wider than the room is centred in it', () => {
  const room = { left: 8, right: 108 };
  assert.equal(clampChipShift(50, 300, room), 8);
  assert.equal(clampChipShift(58, 100, room), 0, 'exactly as wide as the room is centred on it');
  assert.equal(clampChipShift(0, 100, room), 58);
});

test('clampChipShift: over every centre and width the shifted chip lies inside the room, and a chip that fits is untouched', () => {
  const room = { left: 8, right: 404 };
  for (const width of [60, 120, 196, 300, 396]) {
    for (let centre = -150; centre <= 560; centre += 7) {
      const shift = clampChipShift(centre, width, room);
      const left = centre - width / 2 + shift;
      const right = centre + width / 2 + shift;
      assert.ok(left >= room.left - 1e-9 && right <= room.right + 1e-9, `width ${width}, centre ${centre}: ${left}..${right}`);
      if (centre - width / 2 >= room.left && centre + width / 2 <= room.right) assert.equal(shift, 0, `width ${width}, centre ${centre}`);
      else assert.ok(Math.abs(left - room.left) < 1e-9 || Math.abs(right - room.right) < 1e-9, 'it touches an edge, so the shift is the smallest one');
    }
  }
});

// ---- D79: the caret of a shifted chip stays over the pin ---------------------------------------------------------------------------------------

const CHIP_AT_PHONE = 212; // "Here for 3 hrs, 33 mins" as the CI measured it at 412 px wide (the King's pin at x 340, the chip moved 42 px left)

test('chipCaretX: a chip that is not moved has its caret in the middle', () => {
  assert.equal(CHIP_CARET_INSET_PX, 18, 'the radius of the chip body');
  assert.equal(chipCaretX(CHIP_AT_PHONE, 0), CHIP_AT_PHONE / 2);
  assert.equal(chipCaretX(CHIP, 0), CHIP / 2);
});

test('chipCaretX: the AC-17 case at 412 px wide, the King at x 340, puts the caret on the pin', () => {
  const room = { left: 8, right: 404 };
  const shift = clampChipShift(340, CHIP_AT_PHONE, room);
  assert.equal(shift, -42, 'the chip moves 42 px left');
  const caret = chipCaretX(CHIP_AT_PHONE, shift);
  assert.equal(caret, 148, 'the caret is 42 px right of the middle of the chip');
  assert.equal(340 + shift - CHIP_AT_PHONE / 2 + caret, 340, 'chip left edge plus caret x is the pin x');
});

test('chipCaretX: it moves against the shift, a chip moved left has the caret right of the middle and the other way round', () => {
  assert.equal(chipCaretX(200, -30), 130);
  assert.equal(chipCaretX(200, 30), 70);
});

test('chipCaretX: it stays 18 px from either end, so a pin at the very edge keeps the caret on the straight part of the body', () => {
  assert.equal(chipCaretX(200, -500), 182);
  assert.equal(chipCaretX(200, 500), 18);
  assert.equal(chipCaretX(200, -82), 182, 'exactly at the limit');
  assert.equal(chipCaretX(200, -83), 182);
  assert.equal(chipCaretX(200, 82), 18);
  assert.equal(chipCaretX(200, 20, 10), 80, 'with another inset');
  assert.equal(chipCaretX(200, 500, 10), 10);
});

test('chipCaretX: a chip narrower than twice the inset has its caret in the middle', () => {
  assert.equal(chipCaretX(30, 0), 15);
  assert.equal(chipCaretX(30, -40), 15);
  assert.equal(chipCaretX(30, 40), 15);
  assert.equal(chipCaretX(36, 10), 18);
});

test('chipCaretX: over every pin x and chip width the clamped chip has its caret exactly on the pin, unless the pin is too near an end of the chip', () => {
  const room = { left: 8, right: 404 };
  for (const width of [60, 120, 196, 212, 300, 396]) {
    for (let pinX = -150; pinX <= 560; pinX += 7) {
      const shift = Math.round(clampChipShift(pinX, width, room) * 2) / 2; // as realmMap.js rounds it
      const left = pinX - width / 2 + shift;
      const caret = chipCaretX(width, shift);
      assert.ok(caret >= Math.min(18, width / 2) - 1e-9 && caret <= width - Math.min(18, width / 2) + 1e-9, `width ${width}, pin ${pinX}: caret ${caret} is off the body`);
      const wanted = pinX - left; // where the pin is, from the chip's left edge
      if (wanted >= 18 && wanted <= width - 18) assert.ok(Math.abs(left + caret - pinX) < 1e-9, `width ${width}, pin ${pinX}: the caret is ${left + caret - pinX} px from the pin`);
      else assert.ok(Math.abs(caret - Math.min(width - 18, Math.max(18, wanted))) < 1e-9, `width ${width}, pin ${pinX}: the caret is held at the end of the body`);
    }
  }
});
