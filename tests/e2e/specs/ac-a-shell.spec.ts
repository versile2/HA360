// Acceptance tests A of 01 section 11 (03 section 7.5): the shell. S6b writes AC-01 and AC-03; S7a adds AC-02, S7b AC-05 to AC-11 and S8c AC-12 to this file.
// The tests run in the default `phone` project (412 x 915, one device pixel per CSS pixel) and read the Demo app through the ingress proxy; AC-12 resizes the window itself.
// 01 section 11 states every geometry to within 2 px unless it says otherwise.
import type { Locator } from '@playwright/test';

import {
  INGRESS_PREFIX,
  PEEK_CENTRE_TOLERANCE_PX,
  castMember,
  demo,
  expect,
  expectApprox,
  expectRectApprox,
  loadDemoCast,
  mapReady,
  pinDistanceFromPeekCentre,
  readHook,
  settled,
  test,
  type Rect,
} from '../fixtures.js';

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

  test('[AC-03] there is no floating settings button on the map and the attribution control is a 48 x 48 target in the top-right corner', async ({ page }) => {
    await demo(page);
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').toEqual({ width: 412, height: 915 });
    const viewportWidth = viewport?.width ?? 412;

    // D105: no gear floats at (12, 12) any more; Settings is the third tab of the bottom nav, to the right of Driving.
    const settings = page.getByTestId('btn-settings');
    const settingsBox = await boxOf(settings, 'btn-settings');
    const drivingBox = await boxOf(page.getByTestId('nav-driving'), 'nav-driving');
    expect(settingsBox.y, 'btn-settings is in the nav, not at the top').toBeGreaterThan(viewport!.height - 80);
    expect(settingsBox.x, 'btn-settings is right of nav-driving').toBeGreaterThanOrEqual(drivingBox.x + drivingBox.width + 8 - TOLERANCE_PX);
    expect(settingsBox.height, 'btn-settings is at least 48 px tall').toBeGreaterThanOrEqual(48 - TOLERANCE_PX);

    // The attribution's hit area: 48 x 48, its right edge 12 px from the viewport's right edge, inside the top band (01 section 4.4).
    const attribution = page.getByTestId('map-attribution');
    const box = await boxOf(attribution, 'map-attribution');
    expectApprox(box.width, 48, TOLERANCE_PX, 'map-attribution width');
    expectApprox(box.height, 48, TOLERANCE_PX, 'map-attribution height');
    expectApprox(viewportWidth - (box.x + box.width), 12, TOLERANCE_PX, 'map-attribution distance from the right edge');
    expect.soft(box.y, 'map-attribution is not above the viewport').toBeGreaterThanOrEqual(-TOLERANCE_PX);
    expect.soft(box.y + box.height, `map-attribution lies in the top ${TOP_ZONE_PX} px`).toBeLessThanOrEqual(TOP_ZONE_PX + TOLERANCE_PX);

    // The boxes are real targets: nothing else sits over their centre or the middle of their edges.
    expect(await hitMisses(settings), 'points of btn-settings that something else receives').toEqual([]);
    expect(await hitMisses(attribution), 'points of map-attribution that something else receives').toEqual([]);
  });

  // [AC-12] D45 restated (R3-024): with Cass selected at Peek the layout changes under him. At 884 x 916 the Compact sheet becomes the Expanded panel, which has no Peek, so the header is
  // replaced by his detail; the selection, the Drivers section and the map centre (within 0.0005 degrees) are kept. Back at 412 x 915 the sheet is at Peek again (SheetSize stays Peek in
  // both layouts, so the way back is deterministic) with his header. The Drivers section is read from its summary, which only the Drivers section has, and from its tab.
  test('[AC-12] resizing 412 × 915 to 884 × 916 with Cass selected keeps the selection, the Drivers section and the map centre, the panel shows his detail, and resizing back shows the Peek header again', async ({ page }) => {
    test.slow(); // a selection flight, two layout changes and their re-centres: three times the 45 s budget of a plain test
    await demo(page);
    await mapReady(page);
    const jester = castMember(loadDemoCast(), 'jester');
    const header = page.getByTestId('sheet-selection-header');
    const summary = page.getByTestId('sheet-summary');
    const expectCentreKept = async (from: [number, number], label: string): Promise<void> => {
      await settled(page);
      const centre = (await readHook(page, 'camera')).center;
      expectApprox(centre[0], from[0], 0.0005, `${label}: the map centre, longitude`);
      expectApprox(centre[1], from[1], 0.0005, `${label}: the map centre, latitude`);
    };

    // Cass selected from his pin: Peek, his header, the camera on him.
    await page.getByTestId('pin-member-jester').click();
    await expect(header, 'the selection header shows at 412 x 915').toBeVisible();
    await expect.poll(() => pinDistanceFromPeekCentre(page, 'member', 'jester'), { message: "the flight to Cass has not centred his pin", timeout: 10_000 }).toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
    await settled(page);
    const centre = (await readHook(page, 'camera')).center;
    await expect(summary, 'the Drivers summary at Peek').toHaveText('4 in the Realm · 1 driving');

    // 884 x 916: the panel, his detail in place of the header, the same section, the same centre.
    await page.setViewportSize({ width: 884, height: 916 });
    await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: 'sheet().state after the resize to 884 x 916' }).toBe('panel');
    await expect(page.getByTestId('detail-back'), "the panel shows Cass's detail").toBeVisible();
    await expect(page.locator('.realm-detail-name'), 'the detail is his').toHaveText(jester.name);
    await expect(header, 'the Peek header is replaced by the detail').toHaveCount(0);
    await expect(page.getByTestId('tab-drivers'), 'the Drivers section is kept').toHaveAttribute('aria-selected', 'true');
    await expect(summary, 'the panel header is the Drivers summary').toHaveText('4 in the Realm · 1 driving');
    await expectCentreKept(centre, 'at 884 x 916');

    // Back to 412 x 915: Peek, his header again.
    await page.setViewportSize({ width: 412, height: 915 });
    await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: 'sheet().state after the resize back to 412 x 915' }).toBe('peek');
    await expect(header, 'the selection header is back').toBeVisible();
    await expect(header.locator('.realm-row-name'), 'it is Cass\'s').toHaveText(jester.name);
    await expect(page.getByTestId('detail-back'), 'no detail at Peek').toHaveCount(0);
    await expect(summary, 'the Drivers summary at Peek again').toHaveText('4 in the Realm · 1 driving');
    await expectCentreKept(centre, 'back at 412 x 915');

    // The ✕ returns to the section that was kept: Drivers.
    await page.getByTestId('sheet-selection-clear').click();
    await expect(page.getByTestId('tab-drivers'), 'the Drivers tab is the selected one once the selection is cleared').toHaveAttribute('aria-selected', 'true');
  });
});
