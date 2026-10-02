// Acceptance test B of 01 section 11 (03 section 7.5) that S10a writes: [AC-21], the Layers popover and the map style. It lives in its own file so that
// ac-b-map.spec.ts (S6b, S8, S9) stays untouched; the first line of that file says S10 adds AC-21 to the B tests, and this is where it is.
//
// The three Demo styles that draw OpenFreeMap (night, day, streets) fetch a style document from https://tiles.openfreemap.org, and the guard fixture fails a test
// on any request that leaves the ingress proxy, routed or not (fixtures.ts). So the page's own `fetch` is replaced before any script runs (`addInitScript`): a
// request for a style document of OpenFreeMap is answered in the page with a one-layer style and never becomes a network request. What the test proves is the
// application's own work: the tile, the existing `setStyle` of realmMap.js (the style the map reports through window.__realm.styleId()), the store, the popover
// closing, and the selection and camera that stay as they were. The style documents themselves, and the map the real ones draw, are the box smoke's business.
//
// `demo()` always sends `style=` (the hidden `demo-offline` by default), and a Demo `style` overrides the stored one (01 Appendix B), which is the opposite of
// what AC-21 needs ("with no stored value the style is Night", "a reload keeps Day"). So the first test opens `?demo=1&sheet=80` itself, with no `style`.
import type { Page } from '@playwright/test';

import { demo, expect, expectApprox, mapReady, readHook, settled, test, waitForInteractive } from '../fixtures.js';

/** Where the three OpenFreeMap styles live (mapStyles.js): `dark`, `positron` and `liberty` under it. */
const STYLE_DOCUMENT_PREFIX = 'https://tiles.openfreemap.org/styles/';

/** AC-21: the popover closes within this long of the tap on the tile, measured in the page from the click event to the popover leaving the DOM. */
const CLOSE_WITHIN_MS = 200;

/** The one layer a stubbed style document has: the overlay (zones, halos) that realmMap.js adds is fill and line layers, so nothing else is needed. */
const STUB_STYLE = { version: 8, name: 'e2e-stub', sources: {}, layers: [{ id: 'e2e-background', type: 'background' }] };

type StyleProbe = Window & { __styleRequests?: string[]; __styleClosedAfterMs?: Promise<number> };

/** Answers every style-document request of OpenFreeMap in the page and records the URLs it answered (`window.__styleRequests`). Call before the first navigation. */
async function stubOpenFreeMapStyles(page: Page): Promise<void> {
  await page.addInitScript(
    ({ prefix, style }) => {
      const probe = window as StyleProbe;
      probe.__styleRequests = [];
      const realFetch = window.fetch.bind(window);
      window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
        const url = input instanceof Request ? input.url : String(input);
        if (!url.startsWith(prefix)) return realFetch(input, init);
        probe.__styleRequests?.push(url);
        return Promise.resolve(new Response(JSON.stringify(style), { status: 200, headers: { 'Content-Type': 'application/json' } }));
      };
    },
    { prefix: STYLE_DOCUMENT_PREFIX, style: STUB_STYLE },
  );
}

/** The style documents the page asked OpenFreeMap for, as their last path segment (`dark`, `positron`, ...), in order. */
async function requestedStyles(page: Page): Promise<string[]> {
  const urls = await page.evaluate(() => (window as StyleProbe).__styleRequests ?? []);
  return urls.map((url) => url.slice(STYLE_DOCUMENT_PREFIX.length));
}

/** `localStorage[key]` of the page, or null. */
async function stored(page: Page, key: string): Promise<string | null> {
  return page.evaluate((name) => window.localStorage.getItem(name), key);
}

/** Opens Location with the sheet at 80% and no `style` parameter, and waits as `demo()` does: for the circuit, then for the map's first payloads. */
async function openWithoutStyleParameter(page: Page): Promise<void> {
  const url = '?demo=1&sheet=80';
  await page.goto(url);
  await waitForInteractive(page, url);
  await page.waitForFunction(() => (window as Window & { __realm?: unknown }).__realm !== undefined, undefined, { timeout: 30_000 });
  await mapReady(page);
}

/**
 * Taps a tile and measures, in the page, how long the popover takes to leave the DOM from the click event: the delay of the component, the round trip of the
 * circuit and the render, with no Playwright round trip in the number. The probe is armed first, so the click cannot be missed.
 */
async function tapTileAndTimeTheClose(page: Page, tileTestId: string): Promise<number> {
  await page.evaluate((testId) => {
    const probe = window as StyleProbe;
    probe.__styleClosedAfterMs = new Promise<number>((resolve) => {
      const tile = document.querySelector(`[data-testid="${testId}"]`);
      if (tile === null) {
        resolve(Number.NaN);
        return;
      }
      tile.addEventListener(
        'click',
        () => {
          const started = performance.now();
          const poll = () => {
            const elapsed = performance.now() - started;
            if (document.querySelector('[data-testid="map-style-popover"]') === null) resolve(elapsed);
            else if (elapsed > 5000) resolve(Number.POSITIVE_INFINITY);
            else setTimeout(poll, 4);
          };
          poll();
        },
        { once: true, capture: true },
      );
    });
  }, tileTestId);
  await page.getByTestId(tileTestId).click();
  return page.evaluate(() => (window as StyleProbe).__styleClosedAfterMs ?? Promise.resolve(Number.NaN));
}

test.describe('acceptance B: the map style', () => {
  test('[AC-21] btn-layers opens the four tiles and the Show places switch; Day is stored, closes the popover within 200 ms, keeps selection and camera, and survives a reload', { tag: ['@phone'] }, async ({ page }) => {
    await stubOpenFreeMapStyles(page);
    await openWithoutStyleParameter(page);

    // With no stored value the style is Night.
    expect(await stored(page, 'realm.mapStyle'), 'localStorage["realm.mapStyle"] before anything is chosen').toBeNull();
    expect(await readHook(page, 'styleId'), 'the style with no stored value (window.__realm.styleId())').toBe('night');
    const startup = await requestedStyles(page);
    expect(startup[0], 'the first style document the map asked for at start-up (dark is OpenFreeMap Night)').toBe('dark');
    expect(startup, 'no other style was asked for at start-up').not.toContain('positron');

    // A selection to leave alone: a tap on a row selects (the sheet goes to Peek, which brings the right stack back, 01 section 4.13).
    await page.getByTestId('row-member-king').click();
    const header = page.getByTestId('sheet-selection-header');
    await expect(header, 'a tap on a row selects: the selection header shows').toBeVisible();
    await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: 'sheet().state after the selection' }).toBe('peek');
    await settled(page);
    const headerBefore = await header.innerText();
    const cameraBefore = await readHook(page, 'camera');

    // The popover: closed until the tap, then the four tiles in the order of the spec and the switch.
    const popover = page.getByTestId('map-style-popover');
    await expect(popover, 'the popover is closed to begin with').toHaveCount(0);
    await page.getByTestId('btn-layers').click();
    await expect(popover, 'btn-layers opens the popover').toBeVisible();
    const tileIds = await popover.locator('[data-testid^="map-style-tile-"]').evaluateAll((tiles) => tiles.map((tile) => tile.getAttribute('data-testid')));
    expect(tileIds, 'the tiles of the popover, in order (Night, Day, Streets, Satellite; no Parchment in v1)').toEqual([
      'map-style-tile-night',
      'map-style-tile-day',
      'map-style-tile-streets',
      'map-style-tile-satellite',
    ]);
    await expect(page.getByTestId('map-style-tile-night'), 'Night carries the ring: it is the style in force').toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('map-style-tile-day'), 'Day is not chosen yet').toHaveAttribute('aria-checked', 'false');
    await expect(page.getByTestId('map-show-zones'), 'the Show places switch is in the popover').toBeVisible();
    await expect(page.getByTestId('map-show-zones'), 'Show places is on by default').toHaveAttribute('aria-checked', 'true');

    // Choosing Day: the popover closes within 200 ms, the style is stored and applied, and the selection and the camera are as they were.
    const closedAfterMs = await tapTileAndTimeTheClose(page, 'map-style-tile-day');
    expect(closedAfterMs, `the popover leaves the DOM ${CLOSE_WITHIN_MS} ms after the tap on map-style-tile-day at the latest (ms measured in the page)`).toBeLessThan(CLOSE_WITHIN_MS);
    await expect(popover, 'the popover stays closed').toHaveCount(0);
    await expect.poll(() => stored(page, 'realm.mapStyle'), { message: 'localStorage["realm.mapStyle"] after Day was chosen' }).toBe('day');
    await expect.poll(() => readHook(page, 'styleId'), { message: 'the style the map reports (window.__realm.styleId()) after Day was chosen' }).toBe('day');
    await settled(page);
    expect((await requestedStyles(page)).at(-1), 'Day is the OpenFreeMap style positron, asked for through the existing setStyle').toBe('positron');

    await expect(header, 'the selection header is still there').toBeVisible();
    expect(await header.innerText(), 'the selection header says what it said').toBe(headerBefore);
    const cameraAfter = await readHook(page, 'camera');
    expectApprox(cameraAfter.zoom, cameraBefore.zoom, 1e-6, 'camera().zoom after the style change');
    expectApprox(cameraAfter.center[0], cameraBefore.center[0], 1e-9, 'camera().center longitude after the style change');
    expectApprox(cameraAfter.center[1], cameraBefore.center[1], 1e-9, 'camera().center latitude after the style change');

    // Esc and the tile: the popover is a dialog that Esc closes (01 section 4.12), and it shows the new ring when it opens again.
    await page.getByTestId('btn-layers').click();
    await expect(popover, 'btn-layers opens the popover again').toBeVisible();
    await expect(page.getByTestId('map-style-tile-day'), 'Day now carries the ring').toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('map-style-tile-night'), 'Night no longer does').toHaveAttribute('aria-checked', 'false');
    await expect(popover, 'the focus moves into the popover when it opens, which is where Esc is heard').toBeFocused();
    await page.keyboard.press('Escape');
    await expect(popover, 'Esc closes the popover').toHaveCount(0);

    // A reload keeps Day: the circuit is new, the stored value is read after the first render, and the map starts on it.
    await page.reload();
    await waitForInteractive(page, page.url());
    await mapReady(page);
    expect(await stored(page, 'realm.mapStyle'), 'localStorage["realm.mapStyle"] after the reload').toBe('day');
    await expect.poll(() => readHook(page, 'styleId'), { message: 'the style after the reload (window.__realm.styleId())' }).toBe('day');
    // The URL still says sheet=80, where the right stack is hidden: a tap on a row selects and brings the sheet down to Peek, and the stack with it.
    await page.getByTestId('row-member-king').click();
    await expect(page.getByTestId('btn-layers'), 'the right stack is back once the sheet is at Peek').toBeVisible();
    await page.getByTestId('btn-layers').click();
    await expect(page.getByTestId('map-style-tile-day'), 'after the reload the popover shows Day chosen').toHaveAttribute('aria-checked', 'true');
  });

  // AC-18 ends with "Turning off Show places (map-show-zones) removes all circles"; the switch is the popover's, so this half is written here. It runs on the
  // hidden demo-offline style, which needs no network at all.
  test('[AC-21] the Show places switch removes the 14 zone circles, is stored as realm.showZones, and a reload keeps it off', { tag: ['@phone'] }, async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const drawn = async () => ((await readHook(page, 'zones')) ?? []).filter((zone) => zone.drawn).length;
    expect(await drawn(), 'zone circles drawn with Show places on (the default)').toBe(14);
    expect(await stored(page, 'realm.showZones'), 'localStorage["realm.showZones"] before the switch is used').toBeNull();

    await page.getByTestId('btn-layers').click();
    const zones = page.getByTestId('map-show-zones');
    await expect(zones, 'the switch is on').toHaveAttribute('aria-checked', 'true');
    await zones.click();
    await expect(zones, 'the switch is off after the tap').toHaveAttribute('aria-checked', 'false');
    await expect(page.getByTestId('map-style-popover'), 'the switch keeps the popover open').toBeVisible();
    await expect.poll(drawn, { message: 'zone circles drawn with Show places off' }).toBe(0);
    await expect.poll(() => stored(page, 'realm.showZones'), { message: 'localStorage["realm.showZones"] after the switch' }).toBe('false');

    // The reload comes back with the circles off, and the switch tells so.
    await demo(page);
    await mapReady(page);
    await expect.poll(drawn, { message: 'zone circles drawn after the reload with Show places stored off' }).toBe(0);
    await page.getByTestId('btn-layers').click();
    await expect(page.getByTestId('map-show-zones'), 'the switch is off after the reload').toHaveAttribute('aria-checked', 'false');
    await page.getByTestId('map-show-zones').click();
    await expect.poll(drawn, { message: 'zone circles drawn once Show places is on again' }).toBe(14);
    await expect.poll(() => stored(page, 'realm.showZones'), { message: 'localStorage["realm.showZones"] after it was switched on again' }).toBe('true');
  });
});
