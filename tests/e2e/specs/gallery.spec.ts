// The screenshot gallery (03 section 7.5, `[GAL]`): each scene is captured per required viewport (`phone` and `unfolded`) as
// `ci-out/shots/<project>/<scene>.png`, which the e2e job uploads and `tools/ci/make-summary.mjs` copies to `shots/<project>/<scene>.png` of the run
// folder on `ci-artifacts` (with its sha256 in SUMMARY.md). No scene is a pixel assertion: the images are read by the orchestrator against the 03
// section 8.5 checklist. What a scene does assert is that it was taken in the state it is named for, so a regression shows as a failed test and not as a
// picture that quietly changed.
//
// Determinism (03 section 7.5): `reducedMotion: 'reduce'` (no driving pulse, no flight mid-air), `animations: 'disabled'` and the caret hidden in
// `saveShot`, the Demo's frozen clock (21:25 CDT, the default of `demo()`; the chip text below proves it), the hidden `demo-offline` style (no tiles, no
// fonts), `settled()` and `document.fonts.ready` before the capture. Only SwiftShader's anti-aliasing can still differ between runs.
//
// S6b writes SC01. S7b appends SC02 to SC05 (the lists of the sheet). Later slices append theirs (SC06 and SC07 at S8, SC08 and SC09 at S10, ...).
import fs from 'node:fs';

import type { Page } from '@playwright/test';

import { castPlace, demo, expect, loadDemoCast, mapReady, onScreenPinTestIds, readHook, saveShot, test } from '../fixtures.js';

test.use({ reducedMotion: 'reduce' });

/** 01 Appendix A.1: the King's chip at the frozen clock. */
const KING_CHIP_TEXT = 'Here for 3 hrs, 33 mins';

/** The size a PNG declares in its IHDR chunk (bytes 16 to 23), or null when the file does not start with the PNG signature. */
function pngSize(file: string): { width: number; height: number } | null {
  const head = fs.readFileSync(file).subarray(0, 24);
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (head.length < 24 || !head.subarray(0, 8).equals(signature)) return null;
  return { width: head.readUInt32BE(16), height: head.readUInt32BE(20) };
}

/** Below this width the layout is Compact, a bottom sheet; from it (and 560 high) the Expanded left panel (01 section 3.1). */
const EXPANDED_FROM = 840;

/** The size of the viewport the test runs in. */
function viewportOf(page: Page): { width: number; height: number } {
  const size = page.viewportSize();
  if (size === null) throw new Error('the page has no viewport size');
  return size;
}

/**
 * Opens Location with the sheet showing a list: `?sheet=80` in a bottom sheet (the list is hidden at Peek), nothing extra in the panel (it has no sizes), then the
 * map's first payloads. `section` is the tab to show once the Drivers list has rendered.
 */
async function openListScene(page: Page, section: 'drivers' | 'vehicles' | 'places'): Promise<void> {
  await demo(page, viewportOf(page).width < EXPANDED_FROM ? { sheet: '80' } : {});
  await mapReady(page);
  await expect(page.getByTestId('row-member-king'), 'the Drivers list shows').toBeVisible();
  if (section !== 'drivers') {
    await page.getByTestId(`tab-${section}`).click();
    await expect(page.getByTestId(`tab-${section}`), `tab-${section} is selected`).toHaveAttribute('aria-selected', 'true');
  }
}

/** Asserts a scene's PNG was written at the viewport's size, as SC01 does. */
function expectViewportPng(file: string, page: Page, project: string): void {
  const viewport = page.viewportSize();
  expect(viewport, `the ${project} project has a viewport`).not.toBeNull();
  expect(pngSize(file), `${file} is a PNG of the viewport's size`).toEqual(viewport);
}

test.describe('[GAL] screenshot gallery', () => {
  // SC01-location-peek: the default view with the pins, the Here-for chip and the attribution (03 section 8.5, row SC01). The bubbles (S9) and the
  // Peek sheet (S7) are not built yet, so the scene shows the map as it is today; their parts are read when those slices land.
  test('[GAL] SC01-location-peek', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page);
    await mapReady(page);

    // The scene is in the state it is named for: the Demo style, the default view without animation, the four pins of the default view on
    // screen, and the one chip that proves the frozen clock.
    expect(await readHook(page, 'styleId'), 'the Demo style').toBe('demo-offline');
    const camera = await readHook(page, 'camera');
    expect(camera.animated, 'no camera animation at capture').toBe(false);
    expect(await onScreenPinTestIds(page), 'pins on screen').toEqual(['pin-member-jester', 'pin-member-king', 'pin-member-queen', 'pin-vehicle-wagon']);
    await expect(page.getByTestId('chip-here-for'), 'the King chip, at the frozen clock').toHaveText(KING_CHIP_TEXT);
    await expect(page.getByTestId('map-attribution'), 'the attribution control').toBeVisible();

    const file = await saveShot(page, testInfo, 'SC01-location-peek');

    // The file exists, is a PNG and has the viewport's size (one device pixel per CSS pixel).
    const viewport = page.viewportSize();
    expect(viewport, `the ${testInfo.project.name} project has a viewport`).not.toBeNull();
    expect(pngSize(file), `${file} is a PNG of the viewport's size`).toEqual(viewport);
  });

  // SC02-drivers-peek: the Drivers section as the first screen shows it. In the bottom sheet that is Peek (the handle with the summary, the tabs with Drivers selected,
  // no row: the list is under the navigation bar); in the Expanded panel it is the open panel with the five rows (03 section 8.5, row SC02).
  test('[GAL] SC02-drivers-peek', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page);
    await mapReady(page);

    const sheet = await readHook(page, 'sheet');
    await expect(page.getByTestId('tab-drivers'), 'Drivers is the selected section').toHaveAttribute('aria-selected', 'true');
    await expect(page.getByTestId('sheet-summary'), 'the section summary').toHaveText('4 in the Realm · 1 driving');
    if (viewportOf(page).width < EXPANDED_FROM) {
      expect(sheet.state, 'a bottom sheet at Peek').toBe('peek');
      await expect(page.locator('[data-testid^="row-"]').filter({ visible: true }), 'no list row shows at Peek').toHaveCount(0);
    } else {
      expect(sheet.state, 'the open left panel').toBe('panel');
      await expect(page.locator('[data-testid^="row-member-"]'), 'the panel shows the five people').toHaveCount(5);
      await expect(page.getByTestId('row-member-king'), 'the first row is on screen').toBeVisible();
    }

    const file = await saveShot(page, testInfo, 'SC02-drivers-peek');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC03-drivers-tall: the Drivers list at 80 %, the five rows with their battery pills, the stale row dimmed and the warning line (03 section 8.5, row SC03). Phone only:
  // the unfolded panel has no sizes, and SC02 already shows its list.
  test('[GAL] SC03-drivers-tall', { tag: ['@phone'] }, async ({ page }, testInfo) => {
    await openListScene(page, 'drivers');

    expect((await readHook(page, 'sheet')).state, 'the sheet at 80 %').toBe('80');
    await expect(page.locator('[data-testid^="row-member-"]'), 'five people are listed').toHaveCount(5);
    for (const id of ['king', 'queen', 'jester', 'cryptid', 'prince']) {
      await expect(page.getByTestId(`row-member-${id}`), `row-member-${id} is on screen`).toBeVisible();
    }
    await expect(page.getByTestId('row-member-king').locator('.realm-row-detail-text'), 'the frozen clock reaches the rows (the king at home since 5:52 pm)').toHaveText('Since 5:52 pm');
    await expect(page.getByTestId('row-member-cryptid'), 'the stale row is dimmed').toHaveCSS('opacity', '0.72');

    const file = await saveShot(page, testInfo, 'SC03-drivers-tall');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC04-vehicles-list: the Vehicles list with the pickup on four lines and the hatchback placeholder, dimmed, with its note and the info button (03 section 8.5, row SC04).
  test('[GAL] SC04-vehicles-list', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    const cast = loadDemoCast();
    await openListScene(page, 'vehicles');

    await expect(page.locator('[data-testid^="row-vehicle-"]'), 'two vehicles are listed').toHaveCount(2);
    await expect(page.getByTestId('row-vehicle-wagon'), 'the pickup is on screen').toBeVisible();
    await expect(page.getByTestId('row-vehicle-chariot'), 'the placeholder is on screen').toBeVisible();
    await expect(page.getByTestId('row-vehicle-chariot').locator('.realm-row-note'), 'the placeholder note of the Demo cast').toHaveText(cast.chariotNote);
    await expect(page.getByTestId('row-vehicle-chariot'), 'the placeholder is disabled').toHaveAttribute('aria-disabled', 'true');
    await expect(page.locator('.realm-row-info'), 'the placeholder has its info button').toBeVisible();
    await expect(page.getByTestId('sheet-summary'), 'the Vehicles summary').toHaveText('2 vehicles · all parked');

    const file = await saveShot(page, testInfo, 'SC04-vehicles-list');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC05-places-list: the Places list, the two occupied places first with their mini avatars and "1 here", then the empty ones A to Z (03 section 8.5, row SC05).
  test('[GAL] SC05-places-list', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    const cast = loadDemoCast();
    await openListScene(page, 'places');

    await expect(page.locator('[data-testid^="row-place-"]'), 'fourteen places are listed').toHaveCount(14);
    const ids = await page.locator('[data-testid^="row-place-"]').evaluateAll((rows) => rows.slice(0, 2).map((row) => row.getAttribute('data-testid')));
    expect(ids, 'the two occupied places come first').toEqual(['home', 'jester_hall'].map((id) => `row-place-${id}`));
    for (const id of ['home', 'jester_hall']) {
      await expect(page.getByTestId(`row-place-${id}`).locator('.realm-row-name'), `${id} display name`).toHaveText(castPlace(cast, id).name);
      await expect(page.getByTestId(`row-place-${id}`).locator('.realm-row-count'), `${id} count`).toHaveText('1 here');
    }
    await expect(page.getByTestId('sheet-summary'), 'the Places summary').toHaveText('14 places · 2 occupied');

    const file = await saveShot(page, testInfo, 'SC05-places-list');
    expectViewportPng(file, page, testInfo.project.name);
  });
});
