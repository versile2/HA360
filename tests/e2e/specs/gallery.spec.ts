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
// S6b writes SC01. Later slices append their scenes to this file (SC02 to SC05 at S7, SC06 and SC07 at S8, SC08 and SC09 at S10, ...).
import fs from 'node:fs';

import { demo, expect, mapReady, onScreenPinTestIds, readHook, saveShot, test } from '../fixtures.js';

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
});

// ---- S11a: SC10-driving (03 section 8.5: `/driving`, This week) ---------------------------------------------------------------------------------
// Self-contained: its own describe block and its own PNG check, so the block merges next to the scenes of the other slices without touching them. The Driving page
// has no map and so no `window.__realm`: demo() does not wait for hooks on a path other than Location, and the scene waits for what it shows instead (the chips of the
// report), then saveShot() takes the shot after the fonts are ready. The checklist for the reader (03 section 8.5): Cinzel H1, the chip grid, the arrow colours.
const drivingPngSize = (file: string): { width: number; height: number } | null => {
  const head = fs.readFileSync(file).subarray(0, 24);
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (head.length < 24 || !head.subarray(0, 8).equals(signature)) return null;
  return { width: head.readUInt32BE(16), height: head.readUInt32BE(20) };
};

test.describe('[GAL] screenshot gallery: Driving', () => {
  test('[GAL] SC10-driving', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving' });

    // The scene is in the state it is named for: This week, the report loaded (the four chips and the two cards of the default fixture), and nothing mid-load.
    await expect(page.getByRole('heading', { level: 1 }), 'the title').toHaveText('Weekly Driving Report');
    await expect(page.getByTestId('week-chip-0'), 'This week is selected').toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('stat-speeding'), 'the Speeding chip of the default fixture').toHaveText(/^\s*56\s*Speeding\s*$/);
    await expect(page.getByTestId('card-topspeed'), 'the Top Speed card').toContainText('96 mph');
    await expect(page.locator('[data-testid^="driver-card-"]'), 'the four driver cards').toHaveCount(4);
    await expect(page.getByText('Sep 28 – Oct 4 · so far', { exact: true }), 'the range line at the frozen clock').toBeVisible();

    const file = await saveShot(page, testInfo, 'SC10-driving');

    // The file exists, is a PNG and has the viewport's size (one device pixel per CSS pixel).
    const viewport = page.viewportSize();
    expect(viewport, `the ${testInfo.project.name} project has a viewport`).not.toBeNull();
    expect(drivingPngSize(file), `${file} is a PNG of the viewport's size`).toEqual(viewport);
  });
});
