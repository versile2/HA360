// v0.1.1 bug 1 (D103 finding 1): at phone width the open map credits spanned the screen over the gear, swallowed the first tap on the gear and on the layers
// button and folded only 10 to 20 s after load. The credits take no tap meant for another control, and fold after a fixed
// 5 s from the map being made (D81), or on the first pointer or key press anywhere. (D105: the gear moved into the bottom nav as the Settings tab.)
//
// The style document of OpenFreeMap is answered in the page (as ac-b-style.spec.ts does) with a style whose geojson source carries the long OpenFreeMap credit, so the
// credits are as wide as the real ones and no request leaves the ingress proxy.
import type { Page } from '@playwright/test';

import { expect, mapReady, test, waitForInteractive } from '../fixtures.js';

const STYLE_DOCUMENT_PREFIX = 'https://tiles.openfreemap.org/styles/';
const CREDIT = 'OpenFreeMap © OpenMapTiles Data from OpenStreetMap';
const STUB_STYLE = {
  version: 8,
  name: 'e2e-stub',
  sources: { 'e2e-credit': { type: 'geojson', data: { type: 'FeatureCollection', features: [] }, attribution: CREDIT } },
  layers: [{ id: 'e2e-background', type: 'background' }],
};

/** The side margin of the map controls (AC-03). */
const SIDE_MARGIN_PX = 12;
/** D81 says 5 s; the test allows the timer's slack but nowhere near the 10 to 20 s of the bug. */
const FOLDED_WITHIN_MS = 8000;

async function stubStyles(page: Page): Promise<void> {
  await page.addInitScript(
    ({ prefix, style }) => {
      const realFetch = window.fetch.bind(window);
      window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
        const url = input instanceof Request ? input.url : String(input);
        if (!url.startsWith(prefix)) return realFetch(input, init);
        return Promise.resolve(new Response(JSON.stringify(style), { status: 200, headers: { 'Content-Type': 'application/json' } }));
      };
    },
    { prefix: STYLE_DOCUMENT_PREFIX, style: STUB_STYLE },
  );
}

/** Opens Location on Night (no `style` parameter) and returns as soon as the map has its first payloads, without waiting for the credits to fold. */
async function open(page: Page): Promise<void> {
  await stubStyles(page);
  const url = '?demo=1&sheet=peek';
  await page.goto(url);
  await waitForInteractive(page, url);
  await page.waitForFunction(() => (window as Window & { __realm?: unknown }).__realm !== undefined, undefined, { timeout: 30_000 });
  await mapReady(page);
}

const credits = (page: Page) => page.locator('.maplibregl-ctrl-attrib');

test.describe('acceptance B: the map credits on a phone', () => {
  test('the open credits stay inside the side margins, and the first tap on Settings in the nav opens Settings', { tag: ['@phone'] }, async ({ page }) => {
    await open(page);
    await expect(credits(page), 'the credits are open on load (D81)').toHaveClass(/maplibregl-compact-show/);
    const pill = await credits(page).boundingBox();
    expect(pill, 'the credits have a box').not.toBeNull();
    if (pill) {
      // D105: the gear is gone, so the pill may use the left of the screen, but never past either 12 px margin.
      const viewport = page.viewportSize();
      expect(pill.x, 'the credits start inside the left margin').toBeGreaterThanOrEqual(SIDE_MARGIN_PX - 1);
      expect(pill.x + pill.width, 'and end inside the right margin').toBeLessThanOrEqual((viewport?.width ?? 412) - SIDE_MARGIN_PX + 0.5);
    }
    // The (i) button is part of the pill and stays a target: nothing else sits over its centre.
    const info = await page.getByTestId('map-attribution').boundingBox();
    expect(info, 'the (i) button has a box').not.toBeNull();
    // The first tap, immediately: it reaches Settings in the nav.
    await page.getByTestId('btn-settings').click({ timeout: 2000 });
    await expect(page.getByTestId('settings-dialog'), 'the first tap on Settings opened it').toBeVisible();
  });

  test('the first tap on the layers button opens the popover while the credits are still open', { tag: ['@phone'] }, async ({ page }) => {
    await open(page);
    await expect(credits(page)).toHaveClass(/maplibregl-compact-show/);
    await page.getByTestId('btn-layers').click({ timeout: 2000 });
    await expect(page.getByTestId('map-style-tile-night'), 'the first tap on layers opened the popover').toBeVisible();
  });

  test('left alone, the credits fold to the (i) button on a fixed timer', { tag: ['@phone'] }, async ({ page }) => {
    await open(page);
    await expect(credits(page)).toHaveClass(/maplibregl-compact-show/);
    await expect(credits(page), 'folded within the fixed countdown').not.toHaveClass(/maplibregl-compact-show/, { timeout: FOLDED_WITHIN_MS });
    await expect(page.getByTestId('map-attribution'), 'the (i) button stays').toBeVisible();
  });
});
