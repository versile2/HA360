// Acceptance tests A of 01 section 11 (03 section 7.5): the shell. S6b writes AC-01 and AC-03; S7a adds AC-02, S7b AC-05 to AC-11 and S8c AC-12 to this file.
// Both tests run in the default `phone` project (412 x 915, one device pixel per CSS pixel) and read the Demo app through the ingress proxy.
// 01 section 11 states every geometry to within 2 px unless it says otherwise.
import type { Locator } from '@playwright/test';

import { INGRESS_PREFIX, demo, expect, expectApprox, expectRectApprox, test, type Rect } from '../fixtures.js';

const TOLERANCE_PX = 2;

/** 01 section 4.4: the top-right control zone is 72 px high (12 px margin, the 48 px target and 12 px below it). */
const TOP_ZONE_PX = 72;

async function boxOf(locator: Locator, label: string): Promise<Rect> {
  await expect(locator, `${label} is rendered`).toBeVisible();
  const box = await locator.boundingBox();
  expect(box, `${label} has a bounding box`).not.toBeNull();
  return box as Rect;
}

/** True when `inner` lies inside `outer`, every edge to within `tolerance` px. */
function covers(outer: Rect, inner: Rect, tolerance: number): boolean {
  return (
    inner.x >= outer.x - tolerance &&
    inner.y >= outer.y - tolerance &&
    inner.x + inner.width <= outer.x + outer.width + tolerance &&
    inner.y + inner.height <= outer.y + outer.height + tolerance
  );
}

/**
 * Hit-tests a control: the point under the centre and under the middle of each edge (3 px inside it, so the circle of a round button is still hit)
 * must belong to the control itself. Returns what was hit instead, per miss; an empty list is a full hit area.
 */
async function hitMisses(locator: Locator): Promise<string[]> {
  return locator.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const inset = 3;
    const centreX = box.x + box.width / 2;
    const centreY = box.y + box.height / 2;
    const points: [string, number, number][] = [
      ['centre', centreX, centreY],
      ['top', centreX, box.y + inset],
      ['bottom', centreX, box.y + box.height - inset],
      ['left', box.x + inset, centreY],
      ['right', box.x + box.width - inset, centreY],
    ];
    const misses: string[] = [];
    for (const [name, x, y] of points) {
      const hit = document.elementFromPoint(x, y);
      if (hit !== null && (hit === element || element.contains(hit))) continue;
      const what = hit === null ? 'nothing' : `<${hit.tagName.toLowerCase()} class="${hit.getAttribute('class') ?? ''}">`;
      misses.push(`${name} (${Math.round(x)}, ${Math.round(y)}) hits ${what}`);
    }
    return misses;
  });
}

test.describe('acceptance A: the shell', () => {
  test('[AC-01] opening / shows Location and the map canvas covers the whole viewport', async ({ page }) => {
    await demo(page);

    // `/` through the ingress prefix is Location: the URL is the prefix plus one slash and the Location link is the current page, Driving is not.
    expect(new URL(page.url()).pathname).toBe(`${INGRESS_PREFIX}/`);
    await expect(page.getByTestId('nav-location')).toHaveAttribute('aria-current', 'page');
    await expect(page.getByTestId('nav-driving')).not.toHaveAttribute('aria-current');

    // The phone project is the AC's viewport: (0, 0, 412, 915).
    expect(page.viewportSize(), 'the phone project').toEqual({ width: 412, height: 915 });
    const canvas = page.getByTestId('map-canvas');
    const canvasBox = await boxOf(canvas, 'map-canvas');
    expectRectApprox(canvasBox, { x: 0, y: 0, width: 412, height: 915 }, TOLERANCE_PX, 'map-canvas');

    // MapLibre's own canvas fills the same box, and the map is drawn behind the navigation (and, from S7, behind the sheet): the canvas box
    // contains the whole of the Location link.
    const drawn = canvas.locator('canvas.maplibregl-canvas');
    await expect(drawn, 'the map host holds one MapLibre canvas').toHaveCount(1);
    expectRectApprox(await drawn.boundingBox(), { x: 0, y: 0, width: 412, height: 915 }, TOLERANCE_PX, 'the MapLibre canvas');
    const navBox = await boxOf(page.getByTestId('nav-location'), 'nav-location');
    expect(covers(canvasBox, navBox, TOLERANCE_PX), `the canvas ${JSON.stringify(canvasBox)} covers the Location link ${JSON.stringify(navBox)}`).toBe(true);
  });

  test('[AC-03] the settings button is 48 x 48 at (12, 12) and the attribution control is a 48 x 48 target in the top-right corner', async ({ page }) => {
    await demo(page);
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').toEqual({ width: 412, height: 915 });
    const viewportWidth = viewport?.width ?? 412;

    // The gear: 48 x 48 at (12, 12).
    const gear = page.getByTestId('btn-settings');
    expectRectApprox(await boxOf(gear, 'btn-settings'), { x: 12, y: 12, width: 48, height: 48 }, TOLERANCE_PX, 'btn-settings');

    // The attribution's hit area: 48 x 48, its right edge 12 px from the viewport's right edge, inside the top band (01 section 4.4).
    const attribution = page.getByTestId('map-attribution');
    const box = await boxOf(attribution, 'map-attribution');
    expectApprox(box.width, 48, TOLERANCE_PX, 'map-attribution width');
    expectApprox(box.height, 48, TOLERANCE_PX, 'map-attribution height');
    expectApprox(viewportWidth - (box.x + box.width), 12, TOLERANCE_PX, 'map-attribution distance from the right edge');
    expect.soft(box.y, 'map-attribution is not above the viewport').toBeGreaterThanOrEqual(-TOLERANCE_PX);
    expect.soft(box.y + box.height, `map-attribution lies in the top ${TOP_ZONE_PX} px`).toBeLessThanOrEqual(TOP_ZONE_PX + TOLERANCE_PX);

    // The boxes are real targets: nothing else sits over their centre or the middle of their edges.
    expect(await hitMisses(gear), 'points of btn-settings that something else receives').toEqual([]);
    expect(await hitMisses(attribution), 'points of map-attribution that something else receives').toEqual([]);
  });
});
