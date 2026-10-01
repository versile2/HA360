// Tests for wwwroot/js/layoutMath.js: the pure layout arithmetic of the map (03 section 4.1, 01 sections 3.2, 3.4.3 and 4.11).
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { EARTH_RADIUS_M } from '../../src/Realm.Web/wwwroot/js/geo.js';
import {
  AT_DEFAULT_METERS,
  AT_DEFAULT_ZOOM,
  DEFAULT_LAYOUT,
  computePadding,
  haversine,
  isAtDefault,
  navTotalPx,
  peekSheetHeight,
  quantizeZoom,
  rectCenter,
  visibleRect,
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
  assert.equal(haversine([-97.341, 31.099], [-97.341, 31.099]), 0);
  const oneDegree = (EARTH_RADIUS_M * Math.PI) / 180;
  assert.ok(Math.abs(haversine([0, 0], [0, 1]) - oneDegree) < 1e-6);
  assert.ok(Math.abs(haversine([0, 0], [1, 0]) - oneDegree) < 1e-6);
  assert.ok(Math.abs(haversine([179.5, 0], [-179.5, 0]) - oneDegree) < 1e-6, 'across the antimeridian');
  assert.equal(haversine([-97.341, 31.099], [-94.7291, 31.3382]), haversine([-94.7291, 31.3382], [-97.341, 31.099]));
});

// A point `meters` due north of `center`.
const north = (center, meters) => [center[0], center[1] + ((meters / EARTH_RADIUS_M) * 180) / Math.PI];

test('isAtDefault: within 40 m and 0.3 zoom of the target', () => {
  const target = { center: [-97.341, 31.099], zoom: 15 };
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
  const camera = { center: [-97.341, 31.099], zoom: 15 };
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
