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
// S6b writes SC01. S7b appends SC02 to SC05 (the lists of the sheet). S9b appends SC15 at the end. Later slices append theirs (SC06 and SC07 at S8c, SC08 and SC09 at S10, ...).
import fs from 'node:fs';

import type { Locator, Page } from '@playwright/test';

import { PEEK_CENTRE_TOLERANCE_PX, castMember, castPlace, demo, expect, expectSelectionCentred, loadDemoCast, mapReady, onScreenPinTestIds, pinDistanceFromPeekCentre, readHook, saveShot, test } from '../fixtures.js';

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

// ---- S11b: SC11 to SC14 and SC17 (03 section 8.5: the popups, the driver week and two variants of the Driving page) ---------------------------------
// Appended as a block of its own, after S11a's. It reuses the viewport PNG check of the top of the file (`expectViewportPng`) and keeps its helper local. The popups open
// from the page the way the person opens them (a tap on the chip or the card); the page is prerendered, so a tap before the circuit is up does nothing, and the helper
// repeats it until the dialog is there (and never taps again once it is, because the scrim would be in the way). The checklist for the reader (03 section 8.5, SC11 to SC13):
// Cinzel H1, chip grid, arrow colours, bar widths and the 56 px minimum, avatar at the bar end, footnotes; SC14 and SC17: "—" with the information icon, no row height
// change, the partial-week range-line suffix and the no-record states.

/** Opens the popup of `key` with `opener` and returns the dialog (MudBlazor gives it `role="dialog"`). */
async function openDrivingPopup(page: Page, opener: Locator, key: string): Promise<Locator> {
  const dialog = page.getByRole('dialog');
  await expect(async () => {
    if ((await dialog.count()) === 0) await opener.click({ timeout: 3_000 });
    await expect(dialog.getByTestId(`popup-${key}`), `the ${key} popup is open`).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  return dialog;
}

/**
 * R3-12: the selected week chip must be inside the row's visible box, clear of the 16 px edge fade. The chip is scrolled into view only after the circuit is up (the prerendered
 * row is not), so this polls: it passes once the circuit has done it, which is also when the picture is fit to be taken.
 */
async function expectSelectedChipVisibleInRow(page: Page, offset: number): Promise<void> {
  const FADE_PX = 16;
  await expect(async () => {
    const row = await page.getByTestId('week-chips').boundingBox();
    const chip = await page.getByTestId(`week-chip-${offset}`).boundingBox();
    expect(row, 'the chip row has a box').not.toBeNull();
    expect(chip, 'the selected chip has a box').not.toBeNull();
    expect(chip!.x, 'the chip starts clear of the left fade').toBeGreaterThanOrEqual(row!.x + FADE_PX - 1);
    expect(chip!.x + chip!.width, 'the chip ends clear of the right fade').toBeLessThanOrEqual(row!.x + row!.width - FADE_PX + 1);
  }, 'the selected week chip is scrolled into the visible row').toPass({ timeout: 20_000 });
}

test.describe('[GAL] screenshot gallery: Driving popups, the driver week and the variants', () => {
  // SC11-popup-drives: the Total Drives popup (the four bars by drives, the footnote, "Got it"), then the Miles toggle (03 section 8.5, row SC11): two shots, the second named
  // SC11-popup-drives-miles.
  test('[GAL] SC11-popup-drives', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('card-drives'), 'the Drives card of the report').toHaveText(/^Drives\s*64\s*Total mi\s*781$/);

    const dialog = await openDrivingPopup(page, page.getByTestId('card-drives'), 'drives');
    await expect(dialog.locator('.realm-popup__title'), 'the title').toHaveText('Total Drives');
    await expect(dialog.locator('.realm-popup__summary'), 'the one-sentence summary').toHaveText('64 drives, 781 miles on the road.');
    await expect(dialog.getByTestId('popup-toggle-drives'), 'the popup starts on Drives').toHaveAttribute('aria-pressed', 'true');
    await expect(dialog.locator('.realm-bar'), 'one bar per driver').toHaveCount(4);
    await expect(dialog.locator('.realm-bars--wide'), 'drives are narrow numbers').toHaveCount(0);
    await expect(dialog.getByTestId('popup-gotit'), 'the full-width "Got it"').toBeVisible();

    const drives = await saveShot(page, testInfo, 'SC11-popup-drives');
    expectViewportPng(drives, page, testInfo.project.name);

    await dialog.getByTestId('popup-toggle-miles').click();
    await expect(dialog.getByTestId('popup-toggle-miles'), 'the Miles toggle is on').toHaveAttribute('aria-pressed', 'true');
    await expect(dialog.getByTestId('popup-toggle-drives'), 'the Drives toggle is off').toHaveAttribute('aria-pressed', 'false');
    await expect(dialog.locator('.realm-bars--wide'), 'miles are wide numbers (the 72 px pill)').toHaveCount(1);
    await expect(dialog.locator('.realm-bar__value').first(), 'the first bar reads in miles').toContainText('mi');

    const miles = await saveShot(page, testInfo, 'SC11-popup-drives-miles');
    expectViewportPng(miles, page, testInfo.project.name);
  });

  // SC12-popup-speeding: the Speeding popup (the bars by speeding events, the "sampled" footnote) (03 section 8.5, row SC12).
  test('[GAL] SC12-popup-speeding', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('stat-speeding'), 'the Speeding chip of the default fixture').toHaveText(/^\s*56\s*Speeding\s*$/);

    const dialog = await openDrivingPopup(page, page.getByTestId('stat-speeding'), 'speeding');
    await expect(dialog.locator('.realm-popup__title'), 'the title').toHaveText('Speeding');
    await expect(dialog.locator('.realm-popup__summary'), 'the one-sentence summary').toHaveText('56 speeding events this week, 7 more than last week.');
    await expect(dialog.locator('.realm-bar'), 'one bar per driver').toHaveCount(4);
    await expect(dialog.locator('.realm-popup__footnote'), 'the footnote says the count is sampled (D39)').toContainText('sampled');
    await expect(dialog.locator('.realm-popup__footnote'), 'the footnote names no other source (D26)').not.toContainText('Life360');

    const file = await saveShot(page, testInfo, 'SC12-popup-speeding');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC13-driver-week: the `jester` member's week detail: the header, the four tiles, the event row, and the drives by day (03 section 8.5, row SC13).
  test('[GAL] SC13-driver-week', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving/jester', week: 0 });
    await expect(page.getByTestId('drive-row-0'), 'the newest drive is listed').toBeVisible();

    await expect(page.getByRole('heading', { level: 1 }), 'the driver name').toHaveText(castMember(loadDemoCast(), 'jester').name);
    await expect(page.locator('.realm-driver-week__week'), 'the week, at the frozen clock').toHaveText('This week · Sep 28 – Oct 4');
    await expect(page.locator('.realm-week-tile__value'), 'the four tiles').toHaveText(['18', '202.6', '88 mph', '38 speeding events']);
    await expect(page.locator('li.realm-drive-row'), 'the 18 drives of the week').toHaveCount(18);
    await expect(page.getByTestId('detail-back'), 'the Back link returns to this week').toHaveAttribute('href', 'driving?week=0');

    const file = await saveShot(page, testInfo, 'SC13-driver-week');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC14-variant-phone-unavailable: Driving with `?variant=phone-unavailable`: the Phone use chip reads "—" with its information icon and no arrow, the rows keep their height (03 section 8.5, row SC14).
  test('[GAL] SC14-variant-phone-unavailable', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving', variant: 'phone-unavailable' });

    await expect(page.getByTestId('stat-phone'), 'the Phone use chip reads a dash').toHaveText(/^\s*—\s*Phone use\s*$/);
    await expect(page.getByTestId('stat-phone').locator('.realm-stat-chip__info'), 'the information icon').toBeVisible();
    await expect(page.getByTestId('trend-phone'), 'no arrow on the Phone use chip').toHaveCount(0);
    await expect(page.getByTestId('stat-speeding'), 'the other chips are unchanged').toHaveText(/^\s*56\s*Speeding\s*$/);
    await expect(page.getByTestId('driver-pill-king'), "Alden's pill lists only what was recorded").toHaveText('6 speeding events');

    const file = await saveShot(page, testInfo, 'SC14-variant-phone-unavailable');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC17-variant-fresh-install: Driving with `?variant=fresh-install&week=2`: weeks 2 and 3 of a new database have no record (the sentence in place of the range, the dashes, "No record of
  // this week" on the cards). The "recorded from {weekday}" suffix of the partial week is on week 1 of the same variant, so the scene also saves that week as
  // SC17-variant-fresh-install-partial (03 section 8.5, row SC17; 01 sections 6.2 and 8.7).
  test('[GAL] SC17-variant-fresh-install', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { path: 'driving', variant: 'fresh-install', week: 2 });

    await expect(page.getByTestId('week-chip-2'), 'the third chip is selected').toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('p.realm-driving__range'), 'the empty-state sentence stands in for the range').toHaveText('The scribes have no record of this week.');
    await expect(page.getByTestId('stat-speeding'), 'the chips read a dash').toHaveText(/^\s*—\s*Speeding\s*$/);
    await expect(page.getByTestId('driver-card-jester'), 'the driver card says there is no record').toContainText('No record of this week');

    await expectSelectedChipVisibleInRow(page, 2);   // R3-12: wait for the circuit's scroll before the shot
    const file = await saveShot(page, testInfo, 'SC17-variant-fresh-install');
    expectViewportPng(file, page, testInfo.project.name);

    await demo(page, { path: 'driving', variant: 'fresh-install', week: 1 });
    await expect(page.getByTestId('week-chip-1'), 'the second chip is selected').toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('p.realm-driving__range'), 'the partial week says where the record begins').toHaveText('Sep 21 – Sep 27 · recorded from Wed');
    await expect(page.getByTestId('stat-speeding'), 'the scaled speeding total of the partial week').toHaveText(/^\s*40\s*Speeding\s*$/);

    await expectSelectedChipVisibleInRow(page, 1);
    const partial = await saveShot(page, testInfo, 'SC17-variant-fresh-install-partial');
    expectViewportPng(partial, page, testInfo.project.name);
  });
});

// ---- S9b: SC15 (03 section 8.5, row SC15: Location with `?variant=poor-accuracy`, the accuracy halo) ----------------------------------------------
// Appended as a block of its own at the end. The halo is a map layer under the Jester's pin: its radius (800 m) is asserted from pixels by [AC-16b] in ac-b-map.spec.ts, and this scene
// is the picture. The checklist for the reader (03 section 8.5, SC15): a soft disc of the member colour under the Jester's pin with a dashed edge, a radius of about 22 px at the default
// view, the pin and its ring drawn over it, the two bubbles on their edges, and nothing else different from SC01.
test.describe('[GAL] screenshot gallery: the accuracy halo', () => {
  test('[GAL] SC15-variant-poor-accuracy', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { variant: 'poor-accuracy' });
    await mapReady(page);

    // The scene is in the state it is named for: the default view, still, with the same four pins on screen as SC01, the two far members on their edges, and the Jester's pin there.
    expect(await readHook(page, 'styleId'), 'the Demo style').toBe('demo-offline');
    expect((await readHook(page, 'camera')).animated, 'no camera animation at capture').toBe(false);
    expect(await onScreenPinTestIds(page), 'pins on screen').toEqual(['pin-member-jester', 'pin-member-king', 'pin-member-queen', 'pin-vehicle-wagon']);
    expect((await readHook(page, 'bubbles')).map((bubble) => bubble.id).sort(), 'the bubbles on the edges').toEqual(['cryptid', 'prince']);
    await expect(page.getByTestId('pin-member-jester'), "the Jester's pin, over the halo").toBeVisible();

    const file = await saveShot(page, testInfo, 'SC15-variant-poor-accuracy');
    expectViewportPng(file, page, testInfo.project.name);
  });
});

// ---- S10a: SC08-style-popover and SC09-settings (03 section 8.5). Their own describe block, so it merges next to the scenes of the other slices without touching them. ----
test.describe('[GAL] screenshot gallery: the Layers popover and the Settings dialog', () => {
  // SC08-style-popover (S10a): the Layers popover open over the Demo map, four tiles and the Show places switch (03 section 8.5, row SC08; 01 section 4.12). The gallery
  // runs on the hidden demo-offline style, which no tile stands for, so no tile carries the ring here: the ring is AC-21's, which runs a real style choice.
  test('[GAL] SC08-style-popover', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page);
    await mapReady(page);
    await page.getByTestId('btn-layers').click();

    const popover = page.getByTestId('map-style-popover');
    await expect(popover, 'the Layers popover is open').toBeVisible();
    await expect(popover.getByRole('heading', { name: 'Map style' }), 'the popover title').toBeVisible();
    await expect(popover.locator('[data-testid^="map-style-tile-"]'), 'four styles, no Parchment in v1').toHaveCount(4);
    for (const id of ['night', 'day', 'streets', 'satellite']) {
      await expect(page.getByTestId(`map-style-tile-${id}`), `map-style-tile-${id} is on screen`).toBeVisible();
    }
    await expect(page.getByTestId('map-show-zones'), 'the Show places switch is on by default').toHaveAttribute('aria-checked', 'true');

    // Where it is: 296 wide, wholly inside the window, to the left of the Layers button and growing upward from it.
    const shell = page.locator('.realm-style-popover');
    await expect(shell, 'the popover is placed (MudBlazor marks it open once it is positioned)').toHaveClass(/mud-popover-open/);
    const box = await shell.boundingBox();
    const layers = await page.getByTestId('btn-layers').boundingBox();
    expect(box, 'the popover has a box').not.toBeNull();
    expect(layers, 'btn-layers has a box').not.toBeNull();
    if (box !== null && layers !== null) {
      const viewport = viewportOf(page);
      expect(box.width, 'popover width (01 section 4.12: 296)').toBeGreaterThanOrEqual(295);
      expect(box.width, 'popover width (01 section 4.12: 296)').toBeLessThanOrEqual(297);
      expect(box.x, 'popover left edge is inside the window').toBeGreaterThanOrEqual(0);
      expect(box.y, 'popover top edge is inside the window').toBeGreaterThanOrEqual(0);
      expect(box.x + box.width, 'popover right edge is inside the window').toBeLessThanOrEqual(viewport.width);
      expect(box.x + box.width, 'popover opens to the left of btn-layers').toBeLessThanOrEqual(layers.x + 1);
      expect(box.y + box.height, 'popover opens upward: its bottom is not below the bottom of btn-layers').toBeLessThanOrEqual(layers.y + layers.height + 2);
      expect(box.y, 'popover is taller than the button and rises above it').toBeLessThan(layers.y);
    }

    const file = await saveShot(page, testInfo, 'SC08-style-popover');
    expectViewportPng(file, page, testInfo.project.name);
  });

  // SC09-settings (S10a): the Settings dialog opened from the gear: full-screen below 600 px, 480 px wide and centred from there up (03 section 8.5, row SC09; 01 section 7.9).
  // Map, Appearance, Connections and About, with the 48 px Diagnostics row and no Theme, Units, Week starts on or Reset rows (D35). The scene is taken twice: the top of the
  // dialog (SC09-settings) and the bottom, scrolled to the About section (SC09-settings-about).
  test('[GAL] SC09-settings', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page);
    await mapReady(page);
    await page.getByTestId('btn-settings').click();

    // MudBlazor puts the dialog's attributes (the test id) on the content and its class on the dialog box that holds the title bar and the content.
    const dialog = page.getByTestId('settings-dialog');
    const frame = page.locator('.mud-dialog.realm-settings');
    await expect(dialog, 'the Settings dialog is open').toBeVisible();
    await expect(frame.locator('.realm-popup__title'), 'the title').toHaveText('Settings');
    await expect(dialog.locator('.realm-settings__section h3'), 'the four sections, in order').toHaveText(['Map', 'Appearance', 'Connections', 'About']);

    // Its size: the whole window below 600 px, a 480 px column in the middle from there up.
    const viewport = viewportOf(page);
    const box = await frame.boundingBox();
    expect(box, 'the dialog has a box').not.toBeNull();
    if (box !== null) {
      if (viewport.width < 600) {
        expect(box.width, 'a full-screen dialog is as wide as the window').toBeGreaterThanOrEqual(viewport.width - 1);
        expect(box.height, 'a full-screen dialog is as high as the window').toBeGreaterThanOrEqual(viewport.height - 1);
      } else {
        expect(box.width, 'the dialog is 480 px wide from 600 px up').toBeGreaterThanOrEqual(479);
        expect(box.width, 'the dialog is 480 px wide from 600 px up').toBeLessThanOrEqual(481);
        expect(Math.abs(box.x + box.width / 2 - viewport.width / 2), 'the dialog is centred horizontally').toBeLessThanOrEqual(2);
      }
    }

    // What it holds: the Map section's controls, the one Appearance row, the four connections of the Demo, and nothing of 01 section 7.9's dropped rows.
    await expect(dialog.getByRole('radio', { name: 'Night' }), 'Night is the style when none was ever chosen').toHaveAttribute('aria-checked', 'true');
    await expect(dialog.getByRole('switch', { name: /Show places/ }), 'Show places is on by default').toHaveAttribute('aria-checked', 'true');
    await expect(dialog.getByRole('radio', { name: 'Auto' }), 'the layout is Auto by default').toHaveAttribute('aria-checked', 'true');
    await expect(dialog.locator('.realm-settings__connection-name'), 'the four connections, by their names in Settings').toHaveText(['Home Assistant', 'Life360', 'FordPass', "Second vehicle (maker's app)"]);
    await expect(dialog.locator('.realm-settings__chip'), 'their states in the Demo').toHaveText(['Connected', 'Connected', 'Connected', 'Not connected']);
    for (const dropped of ['Theme', 'Units', 'Week starts', 'Reset']) {
      await expect(dialog.getByText(dropped), `no "${dropped}" row in v1`).toHaveCount(0);
    }

    const file = await saveShot(page, testInfo, 'SC09-settings');
    expectViewportPng(file, page, testInfo.project.name);

    // The bottom: the version, the credits and the Diagnostics row that opens diagnostics.json in a new tab, 48 px high at least, with its promise under it.
    const diagnostics = dialog.locator('a.realm-settings__link');
    await diagnostics.scrollIntoViewIfNeeded();
    await expect(diagnostics, 'the Diagnostics row is a link to the diagnostics file, by a relative URL').toHaveAttribute('href', 'diagnostics.json');
    await expect(diagnostics, 'it opens in a new tab').toHaveAttribute('target', '_blank');
    await expect(diagnostics, 'what the file holds').toContainText('States and counts only. No locations.');
    const row = await diagnostics.boundingBox();
    expect(row, 'the Diagnostics row has a box').not.toBeNull();
    expect(row?.height ?? 0, 'the Diagnostics row is at least 48 px high').toBeGreaterThanOrEqual(47.5);

    const about = await saveShot(page, testInfo, 'SC09-settings-about');
    expectViewportPng(about, page, testInfo.project.name);
  });
});

// ---- S8c: SC06-member-detail and SC07-peek-selection (03 section 8.5; D45). Their own describe block, appended at the end. ----------------------------------------------------------
// A selection lands at Peek (D45), so both scenes start with a tap on the Jester's pin. SC06 then opens the detail the way a person does (the handle; in the Expanded panel the detail is already
// there) and SC07 stays at Peek. The checklist for the reader (03 section 8.5): SC06: the detail header, the This-week tiles and the Full report link at 80 %, no timeline and no trail in v1; SC07:
// the Peek selection header with its battery badge and the pin centred in the Peek rectangle (phone only: the panel has no Peek).
test.describe('[GAL] screenshot gallery: the selection', () => {
  test('[GAL] SC06-member-detail', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    const jester = castMember(loadDemoCast(), 'jester');
    const compact = viewportOf(page).width < EXPANDED_FROM;
    await demo(page);
    await mapReady(page);

    await page.getByTestId('pin-member-jester').click();
    if (compact) {
      // D45: the pin selected at Peek; the handle then opens the detail at 80 %.
      await expect(page.getByTestId('sheet-selection-header'), 'the Peek selection header').toBeVisible();
      await page.getByTestId('sheet-handle').click();
      await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: 'sheet().state after the handle tap' }).toBe('80');
    } else {
      await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: 'sheet().state in the Expanded layout' }).toBe('panel');
    }

    // The scene is in the state it is named for: Cass's detail, the week's tiles loaded, the link to the full report, and neither a timeline nor a trail button (v1.1).
    await expect(page.getByTestId('detail-back'), 'the detail shows').toBeVisible();
    await expect(page.locator('.realm-detail-name'), 'the detail is the Jester\'s').toHaveText(jester.name);
    await expect(page.locator('.realm-detail-tile-value'), 'the This-week tiles').toHaveText(['18', '202.6', '88 mph']);
    await expect(page.locator('.realm-detail-link'), 'the Full report link').toBeVisible();
    await expect(page.getByTestId('btn-show-trail'), 'no Show trail button in v1').toHaveCount(0);
    await expectSelectionCentred(page, { kind: 'member', id: 'jester' }, compact, 'the selected pin before the capture');

    const file = await saveShot(page, testInfo, 'SC06-member-detail');
    expectViewportPng(file, page, testInfo.project.name);
  });

  test('[GAL] SC07-peek-selection', { tag: ['@phone'] }, async ({ page }, testInfo) => {
    const jester = castMember(loadDemoCast(), 'jester');
    await demo(page);
    await mapReady(page);

    await page.getByTestId('pin-member-jester').click();
    const header = page.getByTestId('sheet-selection-header');
    await expect(header, 'the Peek selection header').toBeVisible();

    // The scene is in the state it is named for: Peek, the header with the name, the lore title, the status line and the low battery badge, no tabs and no detail, and the pin centred in the Peek rectangle.
    await expect(header.locator('.realm-row-name'), 'the name').toHaveText(jester.name);
    await expect(header.locator('.realm-row-lore'), 'the lore title').toHaveText(jester.lore);
    await expect(page.getByTestId('sheet-selection-battery'), 'the battery badge').toHaveText('12%');
    await expect(page.getByTestId('sheet-segments'), 'the tab control is replaced by the header').toBeHidden();
    await expect(page.getByTestId('detail-back'), 'no detail at Peek').toHaveCount(0);
    expect((await readHook(page, 'sheet')).state, 'the sheet is at Peek').toBe('peek');
    await expect.poll(() => pinDistanceFromPeekCentre(page, 'member', 'jester'), { message: 'the flight has not centred the pin in the Peek rectangle', timeout: 10_000 }).toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);

    const file = await saveShot(page, testInfo, 'SC07-peek-selection');
    expectViewportPng(file, page, testInfo.project.name);
  });
});
