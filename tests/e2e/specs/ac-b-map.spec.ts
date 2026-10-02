// Acceptance tests B of 01 section 11 (03 section 7.5): the map. S6b writes the map-side halves that the S5 map already satisfies:
// [AC-13a] the pins on screen, [AC-16a] pin anatomy, [AC-17] the Here-for chip and [AC-18] the zones. The halves that need later slices are the
// `b` tests of S9 (bubbles, fan-out, the halo, off-screen pins gone) and the assertions that need a selection (S8) or the Layers popover (S10); each
// test says below what it leaves to them. S8 adds AC-20, 22, 23, 24, S10 AC-21 to this file.
//
// Every name, place and zone comes from tests/e2e/fixtures/demo-cast.json (the DemoCast, written by `export-demo-cast`; 03 section 8.1 rule 2); a
// role id (`king`, `queen`, `jester`, `wagon`, ...) is the only literal about the cast in this file. Numbers are those of 01 section 11 and 4.2 to 4.6.
// The Demo style is `demo-offline`, whose appearance is `light` (01 section 4.12): the zone alpha below follows it (see ZONE_FILL_ALPHA).
import type { Page } from '@playwright/test';

import { castMember, castPlace, demo, expect, expectApprox, loadDemoCast, mapReady, onScreenPinTestIds, readHook, test, type PinInfo, type Rect, type ZoneInfo } from '../fixtures.js';

// ---- the DOM of a pin, read in the page --------------------------------------------------------------------------------------------------

interface PinDom {
  /** The pin button (the Marker element); its bottom centre is the true point. */
  box: Rect;
  disc: { box: Rect; borderWidth: number; borderStyle: string; borderColor: string; borderRadius: string };
  /** The status badge: `hidden` when the member has none; `icons` counts the glyph paths inside it. */
  statusBadge: { hidden: boolean; className: string; icons: number };
}

async function readPinDom(page: Page, testId: string): Promise<PinDom> {
  return page.getByTestId(testId).evaluate((element) => {
    const rect = (target: Element) => {
      const box = target.getBoundingClientRect();
      return { x: box.x, y: box.y, width: box.width, height: box.height };
    };
    const disc = element.querySelector<HTMLElement>('.realm-pin__disc');
    const status = element.querySelector<HTMLElement>('.realm-pin__badge--status');
    if (disc === null || status === null) throw new Error('the pin has no .realm-pin__disc or .realm-pin__badge--status');
    const style = getComputedStyle(disc);
    return {
      box: rect(element),
      disc: {
        box: rect(disc),
        borderWidth: Number.parseFloat(style.borderTopWidth),
        borderStyle: style.borderTopStyle,
        borderColor: style.borderTopColor,
        borderRadius: style.borderTopLeftRadius,
      },
      statusBadge: { hidden: status.hidden, className: status.className, icons: status.querySelectorAll('svg path').length },
    };
  });
}

/** `#3DDC84` as the browser computes a colour: `rgb(61, 220, 132)`. */
function rgbOf(hex: string): string {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  if (match === null) throw new Error(`not a #RRGGBB colour: ${hex}`);
  return `rgb(${match.slice(1, 4).map((part) => Number.parseInt(part, 16)).join(', ')})`;
}

/** A border radius is a circle when it is at least half the box: `50%`, or `24px` on 48 px. */
function isCircle(radius: string, sizePx: number): boolean {
  const value = Number.parseFloat(radius);
  return radius.endsWith('%') ? value >= 50 : value >= sizePx / 2;
}

const pinOf = (pins: PinInfo[], kind: PinInfo['kind'], id: string): PinInfo => {
  const found = pins.find((pin) => pin.kind === kind && pin.id === id);
  if (!found) throw new Error(`pins() has no ${kind} '${id}' (it has ${pins.map((pin) => `${pin.kind}-${pin.id}`).join(', ')})`);
  return found;
};

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** 01 Appendix A.1: the Here-for chip of the King in the frozen clock (21:25 CDT, the Demo default). Not in the cast file: it is derived from `since`. */
const KING_CHIP_TEXT = 'Here for 3 hrs, 33 mins';

// The fill alphas of 01 section 4.6 by appearance of the active style: AC-18's 0.10 is the dark and imagery value. The Demo style `demo-offline` is
// `light` (01 section 4.12) and its empty zones are 0.14, so the test follows the style it runs on and the style is asserted to be `demo-offline`.
const OFFLINE_STYLE_APPEARANCE = 'light';
const ZONE_FILL_ALPHA: Record<'dark' | 'light' | 'imagery', { empty: number; occupied: number }> = {
  dark: { empty: 0.1, occupied: 0.22 },
  light: { empty: 0.14, occupied: 0.22 },
  imagery: { empty: 0.1, occupied: 0.22 },
};

test.describe('acceptance B: the map', () => {
  // [AC-13a] The pins half of AC-13. The bubbles half (`bubble-cryptid`, `bubble-prince` exist) is S9b's [AC-13b], and with it the removal of the
  // off-screen pins: until S9 the map still holds `pin-member-cryptid` and `pin-member-prince` far outside the viewport, so "do not exist" is asserted
  // here as "not on screen" and the stricter form arrives with the bubbles.
  test('[AC-13a] on load no camera animation runs and exactly the four pins of the default view are on screen', async ({ page }) => {
    await demo(page);
    await mapReady(page);

    const camera = await readHook(page, 'camera');
    expect(camera.animated, '__realm.camera().animated').toBe(false);
    expect(camera.lastDurationMs, '__realm.camera().lastDurationMs').toBe(0);

    // What a person sees (the DOM): the King, the Jester, the Queen and the wagon, and nobody else.
    const expected = ['pin-member-jester', 'pin-member-king', 'pin-member-queen', 'pin-vehicle-wagon'];
    expect(await onScreenPinTestIds(page), 'pins whose box meets the viewport').toEqual(expected);
    for (const testId of expected) await expect(page.getByTestId(testId), testId).toBeVisible();

    // The hook agrees: the true point of each of the four is inside the viewport, and no other pin's is.
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').not.toBeNull();
    const pins = await readHook(page, 'pins');
    const inside = pins
      .filter((pin) => pin.anchorX >= 0 && pin.anchorX <= (viewport?.width ?? 0) && pin.anchorY >= 0 && pin.anchorY <= (viewport?.height ?? 0))
      .map((pin) => `pin-${pin.kind}-${pin.id}`)
      .sort();
    expect(inside, 'pins whose true point is inside the viewport (__realm.pins())').toEqual(expected);
  });

  // [AC-16a] The anatomy half of AC-16: sizes, ring width and colour, the badge. Left to later slices: the selected size (60 px, ring 5 px; S8), Dara's
  // dashed ring and clock badge "after panning to her" (the pin is not on screen until S8 flies there, and S9 removes it from the map while it is far),
  // the wagon drawn 48 px to the right of its true anchor and the accuracy halo (S9b's [AC-16b]).
  test('[AC-16a] member pins are 48 px circles with a 4 px solid ring, the wagon is a 44 px rounded square', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    const pins = await readHook(page, 'pins');

    // Members: [role id, ring colour, status badge as the hook names it (`driving` is the car badge) or none].
    const members: [string, string, PinInfo['badge']][] = [
      ['queen', '#4DB8FF', 'driving'],
      ['king', '#3DDC84', null],
      ['jester', '#3DDC84', null],
    ];
    for (const [id, ring, badge] of members) {
      const who = `${castMember(cast, id).name} (${id})`;
      const pin = pinOf(pins, 'member', id);
      const dom = await readPinDom(page, `pin-member-${id}`);

      expect(pin.sizePx, `${who} sizePx`).toBe(48);
      expect(pin.dashed, `${who} dashed`).toBe(false);
      expect(pin.ring.toUpperCase(), `${who} ring`).toBe(ring);
      expect(pin.badge, `${who} badge`).toBe(badge);

      expectApprox(dom.disc.box.width, 48, 1, `${who} disc width`);
      expectApprox(dom.disc.box.height, 48, 1, `${who} disc height`);
      expect.soft(isCircle(dom.disc.borderRadius, 48), `${who} disc is a circle (border-radius ${dom.disc.borderRadius})`).toBe(true);
      expectApprox(dom.disc.borderWidth, 4, 0.01, `${who} ring width`);
      expect.soft(dom.disc.borderStyle, `${who} ring style`).toBe('solid');
      expect.soft(dom.disc.borderColor, `${who} ring colour as drawn`).toBe(rgbOf(ring));

      // The DOM sits where the hook says: the pin's bottom centre is the true point (Marker anchor `bottom`).
      expectApprox(dom.box.x + dom.box.width / 2, pin.x, 2, `${who} pin x against the hook`);
      expectApprox(dom.box.y + dom.box.height, pin.y, 2, `${who} pin tip y against the hook`);

      // The badge: the Queen is driving and wears the car badge; the King and the Jester wear no status badge (the Jester's low-battery badge is
      // another element and is not part of this check).
      expect.soft(dom.statusBadge.hidden, `${who} status badge hidden`).toBe(badge === null);
      if (badge === null) continue;
      expect.soft(dom.statusBadge.className, `${who} status badge class`).toContain(`realm-pin__badge--${badge}`);
      expect.soft(dom.statusBadge.icons, `${who} status badge draws a glyph`).toBeGreaterThan(0);
    }

    // The wagon: a 44 px rounded square (not a circle), a vehicle pin.
    const wagon = pinOf(pins, 'vehicle', 'wagon');
    const wagonDom = await readPinDom(page, 'pin-vehicle-wagon');
    expect(wagon.sizePx, 'wagon sizePx').toBe(44);
    expectApprox(wagonDom.disc.box.width, 44, 1, 'wagon disc width');
    expectApprox(wagonDom.disc.box.height, 44, 1, 'wagon disc height');
    expect.soft(isCircle(wagonDom.disc.borderRadius, 44), `wagon disc is not a circle (border-radius ${wagonDom.disc.borderRadius})`).toBe(false);
    expect.soft(Number.parseFloat(wagonDom.disc.borderRadius), `wagon disc has rounded corners (border-radius ${wagonDom.disc.borderRadius})`).toBeGreaterThan(0);
  });

  // [AC-17] The first sentence of AC-17. The second ("Selecting Briar shows `Driving · 54 mph` above her pin and removes Alden's chip") needs the
  // selection of S8 and is added then, as [AC-17b]; the old chip's removal is asserted there too.
  test("[AC-17] with nothing selected one Here-for chip shows above the King's pin and on no other pin", async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    const king = castMember(cast, 'king');

    // Exactly one chip on the whole page, and it belongs to the King's pin (the pin whose name is the King's: "Alden, The King. ...").
    const chips = page.getByTestId('chip-here-for');
    await expect(chips, 'chip-here-for elements on the page').toHaveCount(1);
    await expect(page.locator('button.realm-pin:has([data-testid="chip-here-for"])'), 'pins that carry a chip').toHaveCount(1);
    const kingPin = page.getByTestId('pin-member-king');
    await expect(kingPin.getByTestId('chip-here-for'), "the chip is inside the King's pin").toHaveCount(1);
    await expect(kingPin).toHaveAttribute('aria-label', new RegExp(`^${escapeRegExp(`${king.name}, ${king.lore}`)}`));

    // Its text, in the frozen clock (3 hrs 33 mins since the King arrived).
    const chip = chips.first();
    await expect(chip).toBeVisible();
    await expect(chip).toHaveText(KING_CHIP_TEXT);

    // Above the pin, 8 px clear of its top (01 section 4.4), never below it. D79: centred on the pin unless that would clip, then clamped 8 px inside the
    // visible map (D75) with the caret still over the pin's centre. At the phone default view the King's pin sits at x about 340 of 412, so the chip is clamped.
    const pinDom = await readPinDom(page, 'pin-member-king');
    const chipBox = await chip.boundingBox();
    expect(chipBox, 'the chip has a bounding box').not.toBeNull();
    if (chipBox === null) return;
    const viewport = page.viewportSize();
    expect(viewport, 'the page has a viewport size').not.toBeNull();
    if (viewport === null) return;
    const pinCentreX = pinDom.box.x + pinDom.box.width / 2;
    expectApprox(pinDom.box.y - (chipBox.y + chipBox.height), 8, 2, 'gap between the chip and the top of the pin');
    expect.soft(chipBox.y + chipBox.height, 'the chip ends above the pin').toBeLessThanOrEqual(pinDom.box.y);

    // Inside the viewport, at least 8 px from both edges (the half pixel of the shift's rounding is allowed for).
    const EDGE = 8;
    expect.soft(chipBox.x, `the chip's left edge is at least ${EDGE} px from the left edge of the viewport`).toBeGreaterThanOrEqual(EDGE - 0.5);
    expect.soft(viewport.width - (chipBox.x + chipBox.width), `the chip's right edge is at least ${EDGE} px from the right edge of the viewport`).toBeGreaterThanOrEqual(EDGE - 0.5);

    // Centred on the pin whenever the centred chip fits; otherwise it is clamped, and the test says which of the two it checked.
    const centredWouldClip = pinCentreX - chipBox.width / 2 < EDGE || pinCentreX + chipBox.width / 2 > viewport.width - EDGE;
    test.info().annotations.push({ type: 'chip', description: centredWouldClip ? 'clamped' : 'centred' });
    if (!centredWouldClip) expectApprox(chipBox.x + chipBox.width / 2, pinCentreX, 2, 'chip centre x against the pin (the centred chip fits)');

    // The caret, one per chip, stays over the pin's centre whether or not the chip moved: just under the chip, pointing at the pin and not touching it.
    const caret = kingPin.getByTestId('chip-caret');
    await expect(caret, 'chip-caret elements in the chip').toHaveCount(1);
    await expect(chip.getByTestId('chip-caret'), 'the caret belongs to the chip').toHaveCount(1);
    const caretBox = await caret.boundingBox();
    expect(caretBox, 'the caret has a bounding box').not.toBeNull();
    if (caretBox === null) return;
    expectApprox(caretBox.x + caretBox.width / 2, pinCentreX, 2, 'caret centre x against the pin');
    expectApprox(caretBox.y, chipBox.y + chipBox.height, 2, 'caret top against the bottom edge of the chip');
    expect.soft(caretBox.y + caretBox.height, 'the caret ends above the pin').toBeLessThanOrEqual(pinDom.box.y);
    expect.soft(caretBox.x, "the caret is within the chip's width").toBeGreaterThanOrEqual(chipBox.x);
    expect.soft(caretBox.x + caretBox.width, "the caret is within the chip's width").toBeLessThanOrEqual(chipBox.x + chipBox.width);
  });

  // [AC-18] The zones half of AC-18 that the map itself decides; "Show places" (`map-show-zones`, the Layers popover) is S10's and the
  // toggle row is added to this test then. The outline (2 px dashed, or solid) is MapLibre line paint and not observable from the hooks, which report
  // only `dashed`; the width is checked by 03 section 8.5's SC01 review.
  test('[AC-18] fourteen zone circles are drawn, two occupied, every circle at least 14 px, the arrival zone not drawn', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    expect(await readHook(page, 'styleId'), 'the Demo style').toBe('demo-offline');
    const zones = await readHook(page, 'zones');
    const drawn = zones.filter((zone) => zone.drawn);

    // The count and the identity: the drawn zones are the cast's drawn places, the arrival zone (radius above the 5 km maximum) is none of them.
    const drawnPlaces = cast.places.filter((place) => place.drawn);
    const arrival = cast.places.filter((place) => !place.drawn);
    expect(drawn, 'zones drawn (__realm.zones())').toHaveLength(14);
    expect(drawnPlaces, 'the cast has fourteen drawn places').toHaveLength(14);
    expect(drawn.map((zone) => zone.id).sort(), 'the drawn zones are the cast places').toEqual(drawnPlaces.map((place) => place.id).sort());
    expect(arrival, 'the cast has one place that is never drawn').toHaveLength(1);
    const arrivalPlace = arrival[0];
    expect(arrivalPlace?.radiusM, 'the arrival zone radius, in metres').toBe(32_187);
    expect(zones.some((zone) => zone.id === arrivalPlace?.id && zone.drawn), 'the arrival zone is drawn').toBe(false);

    // Occupied: the home and the Jester's hall (by id; the names come from the cast). Fill alpha 0.22 and a solid outline; the other twelve a dashed one.
    const occupied = drawn.filter((zone) => zone.occupied).map((zone) => zone.id).sort();
    expect(occupied, 'occupied zones').toEqual(['home', 'jester_hall']);
    expect(drawn.filter((zone) => !zone.occupied), 'zones that are not occupied').toHaveLength(12);
    const alphaOf = (zone: ZoneInfo): number => (zone.occupied ? ZONE_FILL_ALPHA[OFFLINE_STYLE_APPEARANCE].occupied : ZONE_FILL_ALPHA[OFFLINE_STYLE_APPEARANCE].empty);
    for (const zone of drawn) {
      const place = castPlace(cast, zone.id);
      const label = `${place.name} (${zone.id})`;
      expectApprox(zone.fillAlpha, alphaOf(zone), 0.001, `${label} fill alpha`);
      expect.soft(zone.dashed, `${label} outline is ${zone.occupied ? 'solid' : 'dashed'}`).toBe(!zone.occupied);

      // Never under 14 px on screen: the 100 m circles of the default view are far below it and sit on the minimum, which is rebuilt on a zoom
      // quantised to 0.05 and rounded down, so it measures 14 px up to 14 x 2^0.05 = 14.49 px (01 section 4.6).
      expect.soft(zone.radiusPx, `${label} radius in px is at least the 14 px minimum`).toBeGreaterThanOrEqual(13.99);
      if (place.radiusM === 100) expect.soft(zone.radiusPx, `${label} (100 m) renders at the 14 px minimum`).toBeLessThanOrEqual(14.5);
    }
    expect(drawnPlaces.some((place) => place.radiusM === 100), 'the cast has 100 m places').toBe(true);
  });
});
