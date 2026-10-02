// Acceptance tests B of 01 section 11 (03 section 7.5): the map. S6b writes the map-side halves that the S5 map already satisfies:
// [AC-13a] the pins on screen, [AC-16a] pin anatomy, [AC-17] the Here-for chip and [AC-18] the zones. The halves that need later slices are the
// `b` tests of S9 (bubbles, fan-out, the halo, off-screen pins gone) and the assertions that need a selection (S8) or the Layers popover (S10); each
// test says below what it leaves to them. S10 adds AC-21 to this file. S9b adds [AC-13b], [AC-14], [AC-15] and [AC-16b] in the block
// "S9b: the edge bubbles and the fan-out"; S8c adds [AC-17b], [AC-20], [AC-22], [AC-23] and [AC-24] in the block at the end ("S8c: selection, the chip that
// follows it, recenter and the Back chain") and extends [AC-13b] and [AC-14] with the chips of D90.
//
// Every name, place and zone comes from tests/e2e/fixtures/demo-cast.json (the DemoCast, written by `export-demo-cast`; 03 section 8.1 rule 2); a
// role id (`king`, `queen`, `jester`, `wagon`, ...) is the only literal about the cast in this file. Numbers are those of 01 section 11 and 4.2 to 4.6.
// The Demo style is `demo-offline`, whose appearance is `light` (01 section 4.12): the zone alpha below follows it (see ZONE_FILL_ALPHA).
import type { Page } from '@playwright/test';

import {
  DEFAULT_VIEW_PINS,
  PEEK_CENTRE_TOLERANCE_PX,
  PEEK_INSET,
  castMember,
  castPlace,
  demo,
  emptyMapPoint,
  expect,
  expectApprox,
  expectHistoryDepth,
  expectSelectionCentred,
  loadDemoCast,
  mapReady,
  onScreenPinTestIds,
  peekCentre,
  pinDistanceFromPeekCentre,
  readHook,
  settled,
  tapEmptyMap,
  test,
  type BubbleInfo,
  type PinInfo,
  type Rect,
  type ZoneInfo,
} from '../fixtures.js';

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

// ---- S9b: the edge bubbles and the fan-out (01 sections 4.8 and 4.10) ----------------------------------------------------------------------------
// The numbers are 01 section 11's AC-13 to AC-16 and section 3.4.3 (the Peek rectangle). Positions come from the `bubbles()` and `pins()` hooks and from the DOM, never from
// a coordinate in this file, so the Demo fixture can move (D82, D85) without touching a test; names come from the cast. The screen-pixel numbers of the spec are at the phone
// project's 412 x 915 (these tests carry no viewport tag, so they run there only).

/** 01 section 4.10 step 4: a bubble keeps 8 px clear of the gear, the attribution, the right stack and the sheet. */
const KEEP_OUT_GAP_PX = 8;
/** What the browser's own 1/64 px layout units and the 0.01 px rounding of a bubble's position may take off a measured gap (the layout places the bubble to the pixel). */
const GAP_NOISE_PX = 0.1;
/** D90: the chip's keep-out rectangle is computed arithmetically before the bubbles are laid out (36 px high, 8 px above the pin, its measured width), the drawn chip is CSS; they agree to the pixel. */
const CHIP_SLACK_PX = 1;
/** AC-15: the camera is at zoom 13 (+-0.1) within 1,000 ms of the tap; the flight itself is 900 ms (01 section 4.13). */
const FAR_ZOOM = 13;
const FAR_ZOOM_TOLERANCE = 0.1;
const FAR_FLIGHT_MS = 900;
const TAP_TO_ZOOM_MS = 1_000;
/** The budget of AC-15 is a laptop's: the tap reaches the server over a websocket and the flight starts after the round trip, on a shared CI core with a software GL. The flight's own 900 ms is asserted exactly (`lastDurationMs`). */
const CI_ROUND_TRIP_SLACK_MS = 500;
/** 01 section 4.10 step 6: the cluster fit takes 700 ms and `maxZoom` 15. */
const CLUSTER_FIT_MS = 700;
const CLUSTER_FIT_MAX_ZOOM = 15;
/** 01 section 4.8: the shift of a fanned-out pin, and the distance below which two anchors are fanned. */
const FAN_STEP_PX = 48;
const FAN_THRESHOLD_PX = 36;
/** 02 section 9.5, `poor-accuracy`: the Jester's accuracy in metres, and so the halo's radius (01 section 11, AC-16). */
const HALO_RADIUS_M = 800;
/** MapLibre's 512 px world: metres per pixel at zoom 0 on the equator (the circumference over 512). */
const METRES_PER_PIXEL_AT_ZOOM_0 = 78_271.516964;

/** The bounding box of a set of pixels, and how many there are. */
interface PixelBounds { minX: number; maxX: number; minY: number; maxY: number; count: number }

interface BubbleDom {
  /** The 48 x 48 button. */
  hit: Rect;
  /** The 40 px avatar (its ring and outline are painted around it): "the bubble's box" of AC-14. */
  disc: Rect;
  /** Where the chevron points, in degrees clockwise from east (y down): from the chevron's box against the avatar's. */
  pointerDeg: number;
  /** The Home badge: shown, and its colour next to the colour of `--realm-primary` (the gold) as the browser computes both. */
  home: { shown: boolean; color: string; gold: string };
  /** The count badge of a cluster: its text, or null while hidden. */
  count: string | null;
  tag: string;
  tabIndex: number;
  label: string | null;
  tooltip: string;
}

/** What a person sees of one bubble: its boxes, where its chevron points, its badges and its accessible name. */
async function readBubbleDom(page: Page, testId: string): Promise<BubbleDom> {
  return page.getByTestId(testId).evaluate((element) => {
    const rect = (target: Element) => {
      const box = target.getBoundingClientRect();
      return { x: box.x, y: box.y, width: box.width, height: box.height };
    };
    const part = (selector: string): HTMLElement => {
      const found = element.querySelector<HTMLElement>(selector);
      if (found === null) throw new Error(`the bubble has no ${selector}`);
      return found;
    };
    const disc = rect(part('.realm-bubble__disc'));
    const tip = rect(part('.realm-bubble__pointer svg'));
    const home = part('.realm-bubble__badge--home');
    const count = part('.realm-bubble__badge--count');
    const probe = document.createElement('span');
    probe.style.color = 'var(--realm-primary)';
    document.body.appendChild(probe);
    const gold = getComputedStyle(probe).color;
    probe.remove();
    return {
      hit: rect(element),
      disc,
      pointerDeg: (Math.atan2(tip.y + tip.height / 2 - (disc.y + disc.height / 2), tip.x + tip.width / 2 - (disc.x + disc.width / 2)) * 180) / Math.PI,
      home: { shown: !home.hidden && home.getBoundingClientRect().width > 0, color: getComputedStyle(home).color, gold },
      count: count.hidden ? null : count.textContent,
      tag: element.tagName,
      tabIndex: (element as HTMLElement).tabIndex,
      label: element.getAttribute('aria-label'),
      tooltip: (element as HTMLElement).title,
    };
  });
}

const centreOf = (box: Rect) => ({ x: box.x + box.width / 2, y: box.y + box.height / 2 });

/** The gap between two boxes: the larger of the gaps along each axis, negative when they overlap. */
function gapBetween(a: Rect, b: Rect): number {
  return Math.max(b.x - (a.x + a.width), a.x - (b.x + b.width), b.y - (a.y + a.height), a.y - (b.y + b.height));
}

/** One pin as it is drawn: its button (the body and the pointer), its 56 x 56 hit area (the `::after` box, which is what the bubbles keep clear of, D89 (1)) and, when it carries one, its chip (D90). */
interface PinBoxes { id: string; body: Rect; hit: Rect; chip: Rect | null }
/** One bubble as it is drawn: the 48 x 48 button and the 40 px avatar. */
interface BubbleBoxes { id: string; hit: Rect; disc: Rect }

/** The boxes of every pin that is on screen (its button meets the viewport) and of every bubble that is not leaving, read in the page in one pass (no sleeps, no hook arithmetic). */
async function readPinAndBubbleBoxes(page: Page): Promise<{ pins: PinBoxes[]; bubbles: BubbleBoxes[] }> {
  return page.evaluate(() => {
    const rect = (box: DOMRect) => ({ x: box.x, y: box.y, width: box.width, height: box.height });
    const pins = [...document.querySelectorAll<HTMLElement>('button.realm-pin[data-testid^="pin-"]')]
      .map((pin) => {
        const body = pin.getBoundingClientRect();
        const after = getComputedStyle(pin, '::after');
        const move = new DOMMatrixReadOnly(after.transform);
        const hit = { x: body.x + Number.parseFloat(after.left) + move.m41, y: body.y + Number.parseFloat(after.top) + move.m42, width: Number.parseFloat(after.width), height: Number.parseFloat(after.height) };
        const chip = pin.querySelector('[data-testid="chip-here-for"]');
        return { id: pin.getAttribute('data-testid') ?? '', body: rect(body), hit, chip: chip === null ? null : rect(chip.getBoundingClientRect()) };
      })
      .filter((pin) => pin.body.x + pin.body.width > 0 && pin.body.y + pin.body.height > 0 && pin.body.x < window.innerWidth && pin.body.y < window.innerHeight);
    const bubbles = [...document.querySelectorAll<HTMLElement>('button.realm-bubble[data-testid^="bubble-"]:not(.realm-bubble--leave)')].map((bubble) => {
      const disc = bubble.querySelector('.realm-bubble__disc');
      if (disc === null) throw new Error('the bubble has no .realm-bubble__disc');
      return { id: bubble.getAttribute('data-testid') ?? '', hit: rect(bubble.getBoundingClientRect()), disc: rect(disc.getBoundingClientRect()) };
    });
    return { pins, bubbles };
  });
}

const bubbleOf = (bubbles: BubbleInfo[], id: string): BubbleInfo => {
  const found = bubbles.find((bubble) => bubble.id === id);
  if (!found) throw new Error(`bubbles() has no '${id}' (it has ${bubbles.map((bubble) => bubble.id).join(', ') || 'none'})`);
  return found;
};

/**
 * Arms a watch in the page that resolves when, after the next click, the camera first comes within `FAR_ZOOM_TOLERANCE` of zoom 13 (the time is the page's own clock, from the click
 * event to an animation frame). It is started BEFORE the click and awaited after it, so the polling interval of a test cannot add to the measured time.
 */
function watchZoomAfterNextClick(page: Page): Promise<{ elapsedMs: number; zoom: number }> {
  return page.evaluate(
    ({ zoom, tolerance, timeoutMs }) =>
      new Promise<{ elapsedMs: number; zoom: number }>((resolve, reject) => {
        let clickedAt: number | null = null;
        document.addEventListener('click', () => (clickedAt = performance.now()), { capture: true, once: true });
        const hooks = (window as Window & { __realm?: { camera?: () => { zoom: number } } }).__realm;
        const frame = () => {
          const now = performance.now();
          const current = hooks?.camera?.().zoom ?? Number.NaN;
          if (clickedAt !== null && Math.abs(current - zoom) <= tolerance) return resolve({ elapsedMs: now - clickedAt, zoom: current });
          if (clickedAt !== null && now - clickedAt > timeoutMs) return reject(new Error(`the camera was at zoom ${current} ${Math.round(now - clickedAt)} ms after the tap, not within ${tolerance} of ${zoom}`));
          requestAnimationFrame(frame);
        };
        requestAnimationFrame(frame);
      }),
    { zoom: FAR_ZOOM, tolerance: FAR_ZOOM_TOLERANCE, timeoutMs: 8_000 },
  );
}

/**
 * The bounding box, in page pixels, of the pixels in `area` that differ between two viewport screenshots of the same page, or null when none does. The PNGs are decoded in the
 * page (createImageBitmap on a Blob: no request leaves it), because the specs have no image library.
 */
async function changedBounds(page: Page, before: Buffer, after: Buffer, area: Rect): Promise<PixelBounds | null> {
  return page.evaluate(
    async ({ a, b, box }) => {
      const decode = async (base64: string) => {
        const bytes = Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
        const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
        const context = new OffscreenCanvas(bitmap.width, bitmap.height).getContext('2d', { willReadFrequently: true });
        if (context === null) throw new Error('no 2d context to read the screenshot with');
        context.drawImage(bitmap, 0, 0);
        return context.getImageData(0, 0, bitmap.width, bitmap.height);
      };
      const [first, second] = await Promise.all([decode(a), decode(b)]);
      let minX = Number.POSITIVE_INFINITY;
      let maxX = Number.NEGATIVE_INFINITY;
      let minY = Number.POSITIVE_INFINITY;
      let maxY = Number.NEGATIVE_INFINITY;
      let count = 0;
      const x0 = Math.max(0, Math.floor(box.x));
      const y0 = Math.max(0, Math.floor(box.y));
      const x1 = Math.min(first.width, Math.ceil(box.x + box.width));
      const y1 = Math.min(first.height, Math.ceil(box.y + box.height));
      for (let y = y0; y < y1; y += 1) {
        for (let x = x0; x < x1; x += 1) {
          const i = (y * first.width + x) * 4;
          const difference = Math.abs(first.data[i] - second.data[i]) + Math.abs(first.data[i + 1] - second.data[i + 1]) + Math.abs(first.data[i + 2] - second.data[i + 2]);
          if (difference <= 6) continue;
          count += 1;
          minX = Math.min(minX, x);
          maxX = Math.max(maxX, x);
          minY = Math.min(minY, y);
          maxY = Math.max(maxY, y);
        }
      }
      return count === 0 ? null : { minX, maxX, minY, maxY, count };
    },
    { a: before.toString('base64'), b: after.toString('base64'), box: area },
  );
}

test.describe('acceptance B: the map', () => {
  // [AC-13a] The pins half of AC-13. The bubbles half (`bubble-cryptid`, `bubble-prince` exist) is S9b's [AC-13b], and with it the removal of the
  // off-screen pins; this test keeps its own, weaker form ("not on screen") and [AC-13b] asserts that `pin-member-cryptid` and `pin-member-prince` do not exist.
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

  // ---- S9b: the edge bubbles and the fan-out ----------------------------------------------------------------------------------------------------

  // [AC-13b] The bubbles half of AC-13: the two far members have bubbles and no pins. `pins()` lists every pin the map holds, so with the bubbles it is exactly the four pins of the
  // default view; `bubbles()` is exactly the two, and `cluster` is the member COUNT (a number, 1 for a single bubble), never a boolean (O-10).
  test('[AC-13b] bubble-cryptid and bubble-prince exist, pin-member-cryptid and pin-member-prince do not', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();

    for (const id of ['cryptid', 'prince']) {
      const who = `${castMember(cast, id).name} (${id})`;
      await expect(page.getByTestId(`bubble-${id}`), `bubble-${id}: ${who} has a bubble`).toBeVisible();
      await expect(page.getByTestId(`pin-member-${id}`), `pin-member-${id}: ${who} has no pin`).toHaveCount(0);
    }
    // Nobody else is off screen, and a vehicle never has a bubble.
    for (const id of ['king', 'queen', 'jester', 'wagon']) await expect(page.getByTestId(`bubble-${id}`), `bubble-${id} does not exist`).toHaveCount(0);

    const pins = await readHook(page, 'pins');
    expect(pins.map((pin) => `${pin.kind}-${pin.id}`).sort(), 'the pins the map holds (__realm.pins())').toEqual([...DEFAULT_VIEW_PINS].sort());

    const bubbles = await readHook(page, 'bubbles');
    expect(bubbles.map((bubble) => bubble.id).sort(), 'the bubbles (__realm.bubbles())').toEqual(['cryptid', 'prince']);
    for (const bubble of bubbles) {
      expect(typeof bubble.cluster, `bubbles() ${bubble.id}: cluster is a number`).toBe('number');
      expect(bubble.cluster, `bubbles() ${bubble.id}: a single bubble has a member count of 1`).toBe(1);
      expect(bubble.ids, `bubbles() ${bubble.id}: the members it stands for`).toEqual([bubble.id]);
    }
  });

  // [AC-14] Where the two fixture bubbles sit and what they look like. The centres are the spec's pixels (within 12 px); the clearances are measured on the boxes the browser draws.
  // Two rulings move one of them. D89 (1): the spec's (384, 347) for Dara is where her bubble covered the fanned wagon (R1-01), and an on-screen pin, a fanned one included, is a
  // keep-out for the edge bubbles (the same slide as the gear). D90 extends it to the pin's chip: the King's "Here for" chip is 36 px high, 8 px above his pin, clamped to the right edge
  // of the map, which is where the slide of fix/W8-3 left Dara's bubble ((384, 280)), so her bubble keeps 8 px clear of the chip too and slides on up the right edge, the way she lies,
  // to (384, 240). D90 says the test asserts "no overlap with any pin footprint" (the next test, and the clearances below) and a 12 px tolerance on the new position. Elio's bubble has
  // no pin near it and stays at the spec's (28, 142).
  test('[AC-14] the two bubbles sit at (384, 240) and (28, 142), 8 px clear of the gear, the right stack and the sheet, 48 px to hit, pointing at their members', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    const bubbles = await readHook(page, 'bubbles');
    const sheet = await readHook(page, 'sheet');
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').not.toBeNull();
    if (viewport === null) return;
    expect([viewport.width, viewport.height], 'the spec numbers are those of the phone project').toEqual([412, 915]);

    // The keep-outs as the browser draws them: the gear, the attribution (i), the two buttons of the right stack; and the sheet's top edge from the `sheet()` hook.
    const keepOuts: Array<[string, Rect]> = [];
    for (const testId of ['btn-settings', 'map-attribution', 'btn-recenter', 'btn-layers']) {
      await expect(page.getByTestId(testId), `${testId} is on screen at Peek`).toBeVisible();
      const box = await page.getByTestId(testId).boundingBox();
      expect(box, `${testId} has a bounding box`).not.toBeNull();
      if (box !== null) keepOuts.push([testId, box]);
    }
    const sheetTop: Rect = { x: 0, y: sheet.topPx, width: viewport.width, height: viewport.height - sheet.topPx };

    const wanted: Array<{ id: string; x: number; y: number }> = [
      { id: 'cryptid', x: 384, y: 240 }, // D89 (1) and D90: 347 in 01 section 11, moved up past the King's pin and its chip by the slide rule
      { id: 'prince', x: 28, y: 142 },
    ];
    for (const { id, x, y } of wanted) {
      const who = `${castMember(cast, id).name} (${id})`;
      const row = bubbleOf(bubbles, id);
      const dom = await readBubbleDom(page, `bubble-${id}`);
      const centre = centreOf(dom.disc);

      // Position: the spec's pixels within 12 px, by the hook and by the box the browser draws.
      expectApprox(row.x, x, 12, `${who} bubbles() x`);
      expectApprox(row.y, y, 12, `${who} bubbles() y`);
      expectApprox(centre.x, x, 12, `${who} avatar centre x in the page`);
      expectApprox(centre.y, y, 12, `${who} avatar centre y in the page`);
      expectApprox(centre.x, row.x, 1, `${who} the page against the hook, x`);
      expectApprox(centre.y, row.y, 1, `${who} the page against the hook, y`);

      // Size: a 48 x 48 hit area around a 40 px avatar, a button that the keyboard reaches.
      expectApprox(dom.hit.width, 48, 0.5, `${who} hit area width`);
      expectApprox(dom.hit.height, 48, 0.5, `${who} hit area height`);
      expectApprox(dom.disc.width, 40, 0.5, `${who} avatar width`);
      expect.soft(dom.tag, `${who} is a button`).toBe('BUTTON');
      expect.soft(dom.tabIndex, `${who} is in the tab order`).toBe(0);

      // Clearance: the avatar's box is at least 8 px from each keep-out (negative would be an overlap).
      for (const [name, box] of [...keepOuts, ['the sheet top', sheetTop] as [string, Rect]]) {
        expect.soft(gapBetween(dom.disc, box), `${who}: gap between the avatar and ${name}, at least ${KEEP_OUT_GAP_PX} px`).toBeGreaterThanOrEqual(KEEP_OUT_GAP_PX);
      }

      // Text: the accessible name and the tooltip carry the person's name from the cast (the distance is a fixture number and is not asserted).
      expect.soft(dom.label, `${who} accessible name`).toMatch(new RegExp(`^${escapeRegExp(castMember(cast, id).name)}, [\\d.]+ (?:feet|miles?) (?:north|north-east|east|south-east|south|south-west|west|north-west), off screen\\. Double tap to include on the map\\.$`));
      expect.soft(dom.tooltip, `${who} tooltip`).toMatch(new RegExp(`^${escapeRegExp(castMember(cast, id).name)} · .+ · tap to include on the map$`));
      expect.soft(dom.count, `${who} is one person: no count badge`).toBeNull();
    }

    // The chevron faces the member: the cryptid east (within 10 degrees of horizontal), the prince up and to the left. The DOM and the hook agree.
    const cryptid = await readBubbleDom(page, 'bubble-cryptid');
    const prince = await readBubbleDom(page, 'bubble-prince');
    expect.soft(Math.abs(cryptid.pointerDeg), `cryptid chevron points east: ${cryptid.pointerDeg.toFixed(1)} degrees from horizontal`).toBeLessThanOrEqual(10);
    expect.soft(prince.pointerDeg, `prince chevron points up and to the left: ${prince.pointerDeg.toFixed(1)} degrees (east 0, south 90, west 180, north -90)`).toBeLessThan(-90);
    expect.soft(prince.pointerDeg, `prince chevron points up and to the left: ${prince.pointerDeg.toFixed(1)} degrees`).toBeGreaterThan(-180);
    expectApprox(cryptid.pointerDeg, bubbleOf(bubbles, 'cryptid').angleDeg, 1, 'cryptid chevron against bubbles() angleDeg');
    expectApprox(prince.pointerDeg, bubbleOf(bubbles, 'prince').angleDeg, 1, 'prince chevron against bubbles() angleDeg');

    // The Home badge: the static prince wears it, in gold; the cryptid does not.
    expect.soft(prince.home.shown, 'bubble-prince shows the Home badge').toBe(true);
    expect.soft(prince.home.color, 'the Home badge is gold (--realm-primary)').toBe(prince.home.gold);
    expect.soft(cryptid.home.shown, 'bubble-cryptid has no Home badge').toBe(false);
  });

  // [AC-13b] D89 (1), the ruling on R1-01: an on-screen pin, a fanned one included, is a keep-out rectangle for the edge bubbles. In the default view the King's pin sits at the right edge of the
  // map and the wagon is fanned 48 px to its right, which is where Dara's bubble used to be drawn over the wagon. Both rectangles are read from the DOM: the button of every pin on screen
  // and its 56 x 56 hit area (`::after`), against the 48 px button and the 40 px avatar of every bubble. The layout keeps the avatar 8 px clear of the hit area, the gap it keeps from the gear.
  // D90 adds the chip: a pin's "Here for" chip is part of that pin's footprint, so the avatar is 8 px clear of the chip's box too. The layout computes the chip's rectangle from the same
  // numbers the CSS uses (36 px high, 8 px above the pin, shifted inside the map) and not from the DOM, so the box the browser draws may differ from it by a pixel at most (CHIP_SLACK_PX).
  test('[AC-13b] no edge bubble covers a pin or its chip on screen: every avatar is 8 px clear of the hit area and of the chip of every pin, the fanned wagon included (D89 (1), D90)', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const pins = await readHook(page, 'pins');
    expect(pinOf(pins, 'vehicle', 'wagon').fanned, 'the wagon is fanned out beside the King (the case R1-01 found)').toBe(true);

    const boxes = await readPinAndBubbleBoxes(page);
    expect(boxes.pins.map((pin) => pin.id).sort(), 'the pins on screen').toEqual(DEFAULT_VIEW_PINS.map((key) => `pin-${key}`).sort());
    expect(boxes.bubbles.length, 'the default view has edge bubbles (there is something to keep off the pins)').toBeGreaterThan(0);
    const chips = boxes.pins.filter((pin) => pin.chip !== null);
    expect(chips.map((pin) => pin.id), 'the one pin with a chip in the default view is the King\'s (AC-17)').toEqual(['pin-member-king']);
    for (const bubble of boxes.bubbles) {
      for (const pin of boxes.pins) {
        expect.soft(gapBetween(bubble.hit, pin.body), `${bubble.id} (48 px button) does not touch ${pin.id}`).toBeGreaterThan(0);
        expect.soft(gapBetween(bubble.disc, pin.hit), `${bubble.id}: gap between the avatar and the hit area of ${pin.id}, at least ${KEEP_OUT_GAP_PX} px`).toBeGreaterThanOrEqual(KEEP_OUT_GAP_PX - GAP_NOISE_PX);
        if (pin.chip === null) continue;
        expect.soft(gapBetween(bubble.hit, pin.chip), `${bubble.id} (48 px button) does not touch the chip of ${pin.id}`).toBeGreaterThan(0);
        expect.soft(gapBetween(bubble.disc, pin.chip), `${bubble.id}: gap between the avatar and the chip of ${pin.id}, at least ${KEEP_OUT_GAP_PX} px (D90)`).toBeGreaterThanOrEqual(KEEP_OUT_GAP_PX - CHIP_SLACK_PX);
      }
    }
  });

  // [AC-15] A single-member bubble selects (D45, D84): the tap reaches the server, the member is selected, the sheet stays at Peek and the camera flies to zoom 13 with her pin in the
  // Peek rectangle. What is asserted here is what is on the map; the selection header (name, lore, line, battery) is the second test below, which waits for S8b. D86 rules on the
  // sentence "`bubble-king` exists": it means the bubble whose member set includes `king` (the King, the Queen and the Jester are one group west of Dara, so that bubble is the
  // cluster `queen-king-jester`), which the test finds in `bubbles()` by `ids`; the viewer is not exempt from clustering. "Within 1,000 ms" starts at the tap, so the round trip to
  // the server is inside it (the slack above).
  test('[AC-15] tapping bubble-cryptid selects Dara: Peek, zoom 13 within 1,000 ms, her pin in the Peek rectangle, her bubble gone and the King\'s bubble there', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    const dara = castMember(cast, 'cryptid');
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').not.toBeNull();
    if (viewport === null) return;
    expect((await readHook(page, 'sheet')).state, 'the sheet starts at Peek').toBe('peek');

    const arrival = watchZoomAfterNextClick(page);
    await page.getByTestId('bubble-cryptid').click();
    const { elapsedMs } = await arrival;
    test.info().annotations.push({ type: 'info', description: `[AC-15] ms from the tap on bubble-cryptid to zoom ${FAR_ZOOM} +-${FAR_ZOOM_TOLERANCE}: ${Math.round(elapsedMs)} (limit ${TAP_TO_ZOOM_MS}, slack ${CI_ROUND_TRIP_SLACK_MS})` });
    expect(elapsedMs, `ms from the tap to zoom ${FAR_ZOOM}`).toBeLessThanOrEqual(TAP_TO_ZOOM_MS + CI_ROUND_TRIP_SLACK_MS);
    await settled(page);

    // The flight: 900 ms, ending at zoom 13 with the sheet still at Peek (a selection never changes the size by itself).
    const camera = await readHook(page, 'camera');
    expect(camera.lastDurationMs, 'camera().lastDurationMs of the far flight').toBe(FAR_FLIGHT_MS);
    expectApprox(camera.zoom, FAR_ZOOM, FAR_ZOOM_TOLERANCE, 'camera().zoom at the end of the flight');
    expect((await readHook(page, 'sheet')).state, 'the sheet is at Peek after the tap').toBe('peek');

    // Her pin: on the map, selected (60 px), in the Peek rectangle within 24 px of its centre; her ring is dashed grey with the clock badge (AC-16, stale).
    await expect(page.getByTestId('bubble-cryptid'), `bubble-cryptid is removed: ${dara.name} is on screen`).toHaveCount(0);
    await expect(page.getByTestId('pin-member-cryptid'), `pin-member-cryptid exists: ${dara.name}`).toBeVisible();
    const pin = pinOf(await readHook(page, 'pins'), 'member', 'cryptid');
    const centre = peekCentre(viewport);
    expect(pin.anchorX, 'her true point is inside the Peek rectangle, x').toBeGreaterThanOrEqual(PEEK_INSET.left);
    expect(pin.anchorX, 'her true point is inside the Peek rectangle, x').toBeLessThanOrEqual(viewport.width - PEEK_INSET.right);
    expect(pin.anchorY, 'her true point is inside the Peek rectangle, y').toBeGreaterThanOrEqual(PEEK_INSET.top);
    expect(pin.anchorY, 'her true point is inside the Peek rectangle, y').toBeLessThanOrEqual(viewport.height - PEEK_INSET.bottom);
    expect(Math.hypot(pin.anchorX - centre.x, pin.anchorY - centre.y), `distance of her pin from the centre of the Peek rectangle (${centre.x}, ${centre.y})`).toBeLessThanOrEqual(24);
    expect(pin.sizePx, 'a selected pin is 60 px').toBe(60);
    expect(pin.dashed, 'her ring is dashed').toBe(true);
    expect(pin.ring.toUpperCase(), 'her ring colour').toBe('#9AA0BD');
    expect(pin.badge, 'her clock badge').toBe('stale');

    // Alden is now far to the west: the bubble whose member set includes `king` exists (D86: alone as `bubble-king`, or with the people at home, a cluster such as `queen-king-jester`).
    const bubbles = await readHook(page, 'bubbles');
    const kings = bubbles.find((bubble) => bubble.ids.includes('king'));
    expect(kings, `a bubble whose member set includes 'king' (bubbles(): ${bubbles.map((bubble) => `${bubble.id} [${bubble.ids.join(', ')}]`).join('; ')})`).toBeDefined();
    if (kings === undefined) return;
    expect(kings.id, 'its test id is its member ids joined (a single member: bubble-king)').toBe(kings.ids.join('-'));
    await expect(page.getByTestId(`bubble-${kings.id}`), `bubble-${kings.id} is on screen`).toBeVisible();
    test.info().annotations.push({ type: 'info', description: `[AC-15] the bubble that holds the King after the flight: bubble-${kings.id} (cluster of ${kings.cluster})` });
  });

  // [AC-15] The last sentence: a tap on a cluster selects nothing and leaves the sheet where it is; JavaScript runs the `fitBounds` of "me plus the members" (700 ms, maxZoom 15). The
  // default fixture has no cluster, so the test makes one: after Dara is selected the three people at home are one group on the left edge.
  test('[AC-15] a cluster bubble tap fits me and its members in 700 ms, selects nothing and leaves the sheet and the selection as they were', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    await page.getByTestId('bubble-cryptid').click();
    await expect.poll(async () => Math.abs((await readHook(page, 'camera')).zoom - FAR_ZOOM) <= FAR_ZOOM_TOLERANCE, { message: 'the flight to Dara has not reached zoom 13' }).toBe(true);
    await settled(page);

    // The cluster the flight made: two or more people on one bubble, with a count badge that is that number.
    await expect
      .poll(async () => (await readHook(page, 'bubbles')).some((bubble) => bubble.cluster >= 2), { message: 'no cluster bubble appeared after the flight to Dara' })
      .toBe(true);
    const cluster = (await readHook(page, 'bubbles')).find((bubble) => bubble.cluster >= 2);
    expect(cluster, 'a cluster bubble').toBeDefined();
    if (cluster === undefined) return;
    expect(cluster.cluster, 'the member count is the number of ids').toBe(cluster.ids.length);
    expect(cluster.ids, 'the cluster holds me').toContain('king');
    const clusterDom = await readBubbleDom(page, `bubble-${cluster.id}`);
    expect(clusterDom.count, 'the count badge of the cluster').toBe(String(cluster.cluster));
    expect(clusterDom.home.shown, 'a cluster has no Home badge').toBe(false);

    const sheetBefore = await readHook(page, 'sheet');
    await page.getByTestId(`bubble-${cluster.id}`).click();
    await settled(page);

    // The fit: 700 ms, never closer than zoom 15, and the viewer is on screen again with a pin.
    const camera = await readHook(page, 'camera');
    expect(camera.lastDurationMs, 'camera().lastDurationMs of the cluster fit').toBe(CLUSTER_FIT_MS);
    expect(camera.zoom, `camera().zoom after the fit (maxZoom ${CLUSTER_FIT_MAX_ZOOM})`).toBeLessThanOrEqual(CLUSTER_FIT_MAX_ZOOM + 0.01);
    expect(Math.abs(camera.zoom - FAR_ZOOM), 'the camera left the flight\'s zoom').toBeGreaterThan(FAR_ZOOM_TOLERANCE);
    await expect(page.getByTestId('pin-member-king'), 'the viewer is on the map after the fit').toBeVisible();

    // Nothing was selected and nothing changed in the sheet: Dara is still the selection (far away again, so her bubble carries the gold glow of the selected member).
    expect(await readHook(page, 'sheet'), 'the sheet is as it was').toEqual(sheetBefore);
    await expect(page.getByTestId('bubble-cryptid'), "Dara's bubble is back: she is still the selection").toHaveClass(/realm-bubble--selected/);
    for (const id of cluster.ids) await expect(page.getByTestId(`bubble-${id}`).and(page.locator('.realm-bubble--selected')), `${id} was not selected by the tap`).toHaveCount(0);
  });

  // [AC-15] The header half of the sentence ("Dara", "The Court Cryptid", the line, the battery badge "10%" in the low style). The selection header (`sheet-selection-header`, S8b) is
  // wired to the page's selection by S8c, which un-parked this test (review R1-02, R2-06). The line is built from the cast (the street, the city and the region of the member's
  // address) and the frozen clock's 42 minutes.
  test('[AC-15] the selection header of Dara reads her name, her lore, her line and her battery', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const dara = castMember(loadDemoCast(), 'cryptid');
    const [street, city, region] = (dara.address ?? '').split(', ');

    await page.getByTestId('bubble-cryptid').click();
    const header = page.getByTestId('sheet-selection-header');
    await expect(header, 'the selection header').toBeVisible();
    await expect(header, 'her name').toContainText(dara.name);
    await expect(header, 'her lore title').toContainText(dara.lore);
    await expect(header, 'her line: the street, the city and region, and when she was last seen').toContainText(`${street} · ${city}, ${region} · Last seen 42 min ago`);
    await expect(page.getByTestId('sheet-selection-battery'), 'her battery badge').toHaveText(/10%/);
    await expect(page.getByTestId('sheet-selection-battery'), 'her battery badge is in the low style').toHaveAttribute('data-low', 'true');
    await expect(page.getByTestId('detail-back'), 'no detail at Peek').toHaveCount(0);
    expect((await readHook(page, 'sheet')).state, 'the sheet is at Peek').toBe('peek');
  });

  // [AC-16b] The fan-out half of AC-16: the wagon is parked at the King's point, so it is drawn 48 px to the right of its true anchor and the King stays; no leader line joins them (D35, C-10).
  test('[AC-16b] pin-vehicle-wagon is drawn 48 px (+-2) to the right of its true anchor, the King stays, and there is no leader line', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const pins = await readHook(page, 'pins');
    const wagon = pinOf(pins, 'vehicle', 'wagon');
    const king = pinOf(pins, 'member', 'king');

    // The cause: the two true points are closer than 36 px.
    expect(Math.hypot(wagon.anchorX - king.anchorX, wagon.anchorY - king.anchorY), `distance between the true points of the wagon and the King (fan-out below ${FAN_THRESHOLD_PX} px)`).toBeLessThan(FAN_THRESHOLD_PX);
    // The effect, by the hook: the wagon is fanned by 48 px to the right and not up or down; the King is not moved.
    expect(wagon.fanned, 'the wagon is fanned out').toBe(true);
    expectApprox(wagon.x - wagon.anchorX, FAN_STEP_PX, 2, 'wagon x against its true anchor');
    expectApprox(wagon.y - wagon.anchorY, 0, 0.5, 'wagon y against its true anchor');
    expect(king.fanned, 'the King stays on his true point').toBe(false);
    expectApprox(king.x - king.anchorX, 0, 0.01, 'king x against his true anchor');

    // The effect, by the DOM: the bottom centre of each pin button is where the hook says (the wagon 48 px right of its anchor, the King on his).
    const wagonDom = await readPinDom(page, 'pin-vehicle-wagon');
    const kingDom = await readPinDom(page, 'pin-member-king');
    expectApprox(wagonDom.box.x + wagonDom.box.width / 2 - wagon.anchorX, FAN_STEP_PX, 2, 'wagon pin button: drawn right of its true anchor');
    expectApprox(wagonDom.box.y + wagonDom.box.height, wagon.anchorY, 2, 'wagon pin button: tip on the anchor row');
    expectApprox(kingDom.box.x + kingDom.box.width / 2, king.anchorX, 2, "king pin button: on his true point");

    // No leader line in v1: nothing in the page is named for one.
    const leaders = await page.evaluate(() => document.querySelectorAll('[class*="leader" i], [data-testid*="leader" i], [id*="leader" i]').length);
    expect(leaders, 'elements named for a leader line').toBe(0);
  });

  // [AC-16b] The halo half: with `?variant=poor-accuracy` the Jester (800 m) shows a halo of that radius under the pin. The halo is a map layer, so the test reads pixels: it compares a
  // screenshot of the default view with one of the variant (same camera, same pins) and looks at what changed BELOW the Jester's true point, where his pin does not cover the halo. The
  // changed area is centred on the point and as wide as the halo is: 800 m over the metres per pixel of the camera's zoom (the latitude of the camera centre stands in for his; the
  // difference is a ten-thousandth).
  test('[AC-16b] with ?variant=poor-accuracy the Jester shows a halo of 800 m under his pin', async ({ page }) => {
    const shoot = () => page.screenshot({ animations: 'disabled', caret: 'hide', scale: 'css' });

    await demo(page);
    await mapReady(page);
    const baseline = await shoot();
    const baselinePin = pinOf(await readHook(page, 'pins'), 'member', 'jester');

    await demo(page, { variant: 'poor-accuracy' });
    await mapReady(page);
    const camera = await readHook(page, 'camera');
    const pin = pinOf(await readHook(page, 'pins'), 'member', 'jester');
    expectApprox(pin.anchorX, baselinePin.anchorX, 1, 'the Jester\'s true point x, variant against default (the same camera)');
    expectApprox(pin.anchorY, baselinePin.anchorY, 1, 'the Jester\'s true point y, variant against default (the same camera)');
    const radiusPx = HALO_RADIUS_M / ((METRES_PER_PIXEL_AT_ZOOM_0 * Math.cos((camera.center[1] * Math.PI) / 180)) / 2 ** camera.zoom);

    // The area under the true point: from 2 px below it, so the pin's tip and body are out of it, as wide as the halo plus 20 px on each side.
    const area = { x: pin.anchorX - radiusPx - 20, y: pin.anchorY + 2, width: 2 * (radiusPx + 20), height: radiusPx + 20 };
    const found: { bounds: PixelBounds | null } = { bounds: null };
    await expect(async () => {
      found.bounds = await changedBounds(page, baseline, await shoot(), area);
      expect(found.bounds, 'pixels that differ from the default view under the Jester\'s true point (the halo is not drawn yet, or at all)').not.toBeNull();
    }).toPass({ timeout: 10_000 });
    const changed = found.bounds;
    if (changed === null) return;
    const { minX, maxX, maxY } = changed;
    test.info().annotations.push({ type: 'info', description: `[AC-16b] halo radius expected ${radiusPx.toFixed(1)} px; changed pixels x ${minX} to ${maxX}, bottom row ${maxY}, true point (${pin.anchorX.toFixed(1)}, ${pin.anchorY.toFixed(1)})` });
    expectApprox(maxY - pin.anchorY, radiusPx, 3, 'halo: the lowest changed row below the true point (the radius)');
    expectApprox((maxX - minX) / 2, radiusPx, 3, 'halo: half the width of the changed area (the radius)');
    expectApprox((maxX + minX) / 2, pin.anchorX, 3, 'halo: the changed area is centred on the true point');
  });
});

// ==== S8c: selection, the chip that follows it, recenter and the Back chain (01 section 11 AC-17b, AC-20, AC-22, AC-23, AC-24; D45, D46, D47, D90) ==================================
// The numbers of the AC rows at 412 x 915 (the phone project: these tests carry no viewport tag unless they say so). Every name comes from the cast; every position is read from the
// `__realm` hooks or the DOM, or projected from the cast with the camera (`emptyMapPoint`), so no test types a coordinate (D82). Nothing sleeps: a tap reaches the server over the
// websocket and the flight starts after it, so a test waits for the END state it is about (a poll on the number), calls `settled()`, and asserts the number again.

/** 01 section 11 AC-22: the sheet at Peek is 174 px high at 915 (19 %, floor 168). */
const PEEK_HEIGHT_PX = 174;
/** AC-22: a selection flight ends at zoom 15 or closer. */
const SELECTION_MIN_ZOOM = 15;
/** AC-12: the map centre moves by less than this (degrees) when the layout changes. */
const CENTRE_TOLERANCE_DEG = 0.0005;
/** What the Drivers section's handle summary reads while the Demo's four live people are in the Realm (01 section 8.2, AC-25 to AC-27). */
const DRIVERS_SUMMARY = '4 in the Realm · 1 driving';
/** AC-22: Cass's chip at the frozen clock (he arrived at 9:06 pm, it is 9:25 pm). */
const JESTER_CHIP_TEXT = 'Here for 19 mins';
/** AC-17: Briar's chip while she is selected, driving. */
const QUEEN_CHIP_TEXT = 'Driving · 54 mph';
/** The proxy's own page: an earlier history entry than the app, so "Back leaves the page" is something a test can see. */
const BEFORE_THE_APP = '/__proxy/health';

const selectionHeader = (page: Page) => page.getByTestId('sheet-selection-header');
const sheetHandle = (page: Page) => page.getByTestId('sheet-handle');
const sectionTabs = (page: Page) => page.getByTestId('sheet-segments');

/** `sheet().state` as a value a poll can compare. */
async function sheetState(page: Page): Promise<string> {
  return (await readHook(page, 'sheet')).state;
}

/**
 * The end state of 01 section 11 AC-22 that every way of selecting shares, wherever the sheet was before: the selection header is shown in place of the tabs, the sheet is at Peek
 * (174 px), there is no detail, the handle summary is the Drivers one, the right stack is visible and the pin's true point is within 24 px of the centre of the Peek rectangle.
 * The flight starts after the server round trip, so the distance is POLLED for, then `settled()` runs and the numbers are read again.
 */
async function expectPeekSelection(page: Page, who: { kind: 'member' | 'vehicle'; id: string }, label: string): Promise<void> {
  await expect(selectionHeader(page), `${label}: the selection header shows`).toBeVisible();
  await expect.poll(() => sheetState(page), { message: `${label}: sheet().state` }).toBe('peek');
  await expect
    .poll(() => pinDistanceFromPeekCentre(page, who.kind, who.id), { message: `${label}: distance of pin-${who.kind}-${who.id} from the centre of the Peek rectangle`, timeout: 10_000 })
    .toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
  await settled(page);

  const sheet = await readHook(page, 'sheet');
  expect(sheet.state, `${label}: sheet().state after the flight`).toBe('peek');
  expectApprox(sheet.heightPx, PEEK_HEIGHT_PX, 2, `${label}: the Peek height`);
  expect(await pinDistanceFromPeekCentre(page, who.kind, who.id), `${label}: the pin's distance from the Peek centre after the flight`).toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
  await expect(sectionTabs(page), `${label}: the tab control is hidden`).toBeHidden();
  await expect(page.getByTestId('detail-back'), `${label}: no detail at Peek`).toHaveCount(0);
  await expect(page.getByTestId('btn-layers'), `${label}: the right stack stays visible`).toBeVisible();
}

/** Peek with a selection, whatever the camera did: the header, no tabs, no detail. */
async function expectPeekHeader(page: Page, label: string): Promise<void> {
  await expect(selectionHeader(page), `${label}: the selection header`).toBeVisible();
  await expect.poll(() => sheetState(page), { message: `${label}: sheet().state` }).toBe('peek');
  await expect(sectionTabs(page), `${label}: the tab control is hidden`).toBeHidden();
  await expect(page.getByTestId('detail-back'), `${label}: no detail at Peek`).toHaveCount(0);
}

/** The list at Peek with nothing selected: the tab control is back, there is no header and no detail. */
async function expectListAtPeek(page: Page, label: string): Promise<void> {
  await expect(selectionHeader(page), `${label}: no selection header`).toHaveCount(0);
  await expect(page.getByTestId('tab-drivers'), `${label}: the tab control is back`).toBeVisible();
  await expect.poll(() => sheetState(page), { message: `${label}: sheet().state` }).toBe('peek');
  await expect(page.getByTestId('detail-back'), `${label}: no detail`).toHaveCount(0);
}

/** The detail of a selection at 80 %: the back arrow shows, the header is gone, the sheet reports `80`. */
async function expectDetailAtTall(page: Page, label: string): Promise<void> {
  await expect(page.getByTestId('detail-back'), `${label}: the detail's back arrow shows`).toBeVisible();
  await expect.poll(() => sheetState(page), { message: `${label}: sheet().state` }).toBe('80');
  await expect(selectionHeader(page), `${label}: the header is replaced by the detail`).toHaveCount(0);
}

/** Waits for the camera to report `state` (the script computes it against the default targets) and for the move to end. */
async function expectRecenterState(page: Page, state: 'default' | 'me' | 'away', label: string): Promise<void> {
  await expect
    .poll(async () => ((await readHook(page, 'camera')) as { recenter?: string }).recenter, { message: `${label}: camera().recenter`, timeout: 10_000 })
    .toBe(state);
  await settled(page);
}

test.describe('acceptance B: selection, chip, recenter and Back (S8c)', () => {
  // [AC-17b] The second sentence of AC-17: the chip follows the selection (01 section 4.4). Briar, driving, shows "Driving · 54 mph" above HER pin (8 px clear of it, centred: she is
  // in the middle of the Peek rectangle, so nothing clips it) and Alden's chip is removed; clearing the selection brings his back. His pin is off screen while the camera is on Briar,
  // so "his chip is gone" is read as: the page holds exactly one chip and it is hers; the return of his is read after the recenter button put the default view back.
  test("[AC-17b] selecting Briar shows Driving · 54 mph above her pin and removes Alden's chip, and clearing the selection brings his chip back", async ({ page }) => {
    test.slow(); // two flights and a recentre
    await demo(page);
    await mapReady(page);
    const chips = page.getByTestId('chip-here-for');
    await expect(chips, "before the selection the one chip is Alden's (AC-17)").toHaveCount(1);
    await expect(page.getByTestId('pin-member-king').getByTestId('chip-here-for'), "Alden's chip").toHaveText(KING_CHIP_TEXT);

    await page.getByTestId('pin-member-queen').click();
    await expectPeekSelection(page, { kind: 'member', id: 'queen' }, 'Briar selected');
    const briar = page.getByTestId('pin-member-queen').getByTestId('chip-here-for');
    await expect(briar, "Briar's chip reads her status line without the street").toHaveText(QUEEN_CHIP_TEXT);
    await expect(chips, 'one chip on the page, and it is hers').toHaveCount(1);
    await expect(page.getByTestId('pin-member-king').getByTestId('chip-here-for'), "Alden's chip is removed").toHaveCount(0);

    // Above her pin (8 px clear of its top, never below it), centred on it, the caret over its centre (D79).
    const pinDom = await readPinDom(page, 'pin-member-queen');
    const chipBox = await briar.boundingBox();
    expect(chipBox, "Briar's chip has a bounding box").not.toBeNull();
    if (chipBox === null) return;
    expectApprox(pinDom.box.y - (chipBox.y + chipBox.height), 8, 2, "gap between Briar's chip and the top of her pin");
    expectApprox(chipBox.x + chipBox.width / 2, pinDom.box.x + pinDom.box.width / 2, 2, "Briar's chip is centred on her pin (it fits)");
    const caretBox = await page.getByTestId('pin-member-queen').getByTestId('chip-caret').boundingBox();
    expect(caretBox, "Briar's caret has a bounding box").not.toBeNull();
    if (caretBox !== null) expectApprox(caretBox.x + caretBox.width / 2, pinDom.box.x + pinDom.box.width / 2, 2, 'the caret is over her pin centre');

    // The ✕ clears, and with nothing selected the chip is Alden's again (his pin is back on screen with the default view).
    await page.getByTestId('sheet-selection-clear').click();
    await expect(selectionHeader(page), 'the selection is cleared').toHaveCount(0);
    await page.getByTestId('btn-recenter').click();
    await expectRecenterState(page, 'default', 'after the recenter tap');
    await expect(chips, 'one chip on the page again').toHaveCount(1);
    await expect(page.getByTestId('pin-member-king').getByTestId('chip-here-for'), "Alden's chip is back").toHaveText(KING_CHIP_TEXT);
    await expect(page.getByTestId('pin-member-queen').getByTestId('chip-here-for'), 'Briar has none').toHaveCount(0);
  });

  // [AC-20] Recenter: the filled crosshair (GpsFixed) at the default view, the outlined one (MyLocation) away; away -> the default camera; at the default view -> Alden alone at zoom 16; then
  // the default camera again; the selection is never cleared by it. The two icons are told apart by the markup MudBlazor draws and not by a path typed here: the icon at the default view
  // is the filled one, the icon after a pan has to differ from it, and the icon on "me alone" (filled too) has to be the first again.
  test('[AC-20] recenter cycles default, Alden alone at zoom 16, default; the icon is filled at the default view and outlined after a pan; the selection is never cleared', async ({ page }) => {
    test.slow(); // six recentre flights, a pan and a selection
    await demo(page);
    await mapReady(page);
    const recenter = page.getByTestId('btn-recenter');
    const iconOf = (): Promise<string> => recenter.locator('svg').innerHTML();

    await expectRecenterState(page, 'default', 'on load');
    const filled = await iconOf();

    // A pan (a drag on the empty map, towards the side that has room) puts the camera away from the default view and the icon changes.
    const viewport = page.viewportSize();
    expect(viewport, 'the phone project').not.toBeNull();
    if (viewport === null) return;
    await settled(page);
    const grab = await emptyMapPoint(page);
    const towards = grab.x > viewport.width / 2 ? -120 : 120;
    await page.mouse.move(grab.x, grab.y);
    await page.mouse.down();
    await page.mouse.move(grab.x + towards, grab.y, { steps: 20 });
    await page.mouse.up();
    await expectRecenterState(page, 'away', 'after a pan');
    await expect.poll(iconOf, { message: 'the icon after a pan is not the filled crosshair' }).not.toBe(filled);

    // Away: the tap runs the default camera (the icon is the filled one again).
    await recenter.click();
    await expectRecenterState(page, 'default', 'first tap, from away');
    await expect.poll(iconOf, { message: 'the icon at the default view is the filled crosshair' }).toBe(filled);

    // At the default view: Alden alone at zoom 16, his pin in the middle of the visible map.
    await recenter.click();
    await expectRecenterState(page, 'me', 'second tap, at the default view');
    const camera = await readHook(page, 'camera');
    expectApprox(camera.zoom, 16, 0.1, 'camera().zoom on Alden alone');
    const padding = await readHook(page, 'mapPadding');
    const king = pinOf(await readHook(page, 'pins'), 'member', 'king');
    expectApprox(king.anchorX, (padding.left + viewport.width - padding.right) / 2, 12, "Alden's true point x against the middle of the visible map");
    expectApprox(king.anchorY, (padding.top + viewport.height - padding.bottom) / 2, 12, "Alden's true point y against the middle of the visible map");
    await expect.poll(iconOf, { message: 'the icon on "me alone" is the filled crosshair' }).toBe(filled);

    // A third tap goes back to the default camera.
    await recenter.click();
    await expectRecenterState(page, 'default', 'third tap');

    // The selection is never cleared by it: select Cass, then run the whole cycle.
    await page.getByTestId('pin-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected');
    for (const [step, state] of [['first', 'default'], ['second', 'me'], ['third', 'default']] as const) {
      await recenter.click();
      await expectRecenterState(page, state, `${step} tap with Cass selected`);
      await expect(selectionHeader(page), `${step} tap: Cass is still selected`).toBeVisible();
      await expect(selectionHeader(page).locator('.realm-row-name'), `${step} tap: the selection is still Cass`).toHaveText(castMember(loadDemoCast(), 'jester').name);
      expect(await sheetState(page), `${step} tap: the sheet stays at Peek`).toBe('peek');
    }
  });

  // [AC-22] A pin tap selects at Peek (D45): the header, no tabs, no detail, the Drivers summary, the right stack, the zoom, the pin in the Peek rectangle, Cass's own chip; then the handle opens the detail.
  test('[AC-22] tapping pin-member-jester selects Cass at Peek: his header and 12% badge, the pin in the Peek rectangle at zoom 15 or more, his chip; the handle then opens the detail at 80 %', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    const cast = loadDemoCast();
    const jester = castMember(cast, 'jester');
    const hall = castPlace(cast, 'jester_hall');
    await expect(selectionHeader(page), 'nothing is selected yet').toHaveCount(0);

    await page.getByTestId('pin-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected by his pin');

    const header = selectionHeader(page);
    await expect(header.locator('.realm-row-name'), 'the name').toHaveText(jester.name);
    await expect(header.locator('.realm-row-lore'), 'the lore title').toHaveText(jester.lore);
    await expect(header.locator('.realm-sel-line'), 'the status line and since').toHaveText(`At ${hall.name} · Since 9:06 pm`);
    await expect(page.getByTestId('sheet-selection-battery'), 'the battery badge').toHaveText('12%');
    await expect(page.getByTestId('sheet-selection-battery'), 'the battery badge is in the low style').toHaveAttribute('data-low', 'true');
    await expect(page.getByTestId('sheet-summary'), 'the handle summary is unchanged').toHaveText(DRIVERS_SUMMARY);
    expect((await readHook(page, 'camera')).zoom, 'camera().zoom is at least 15').toBeGreaterThanOrEqual(SELECTION_MIN_ZOOM - 0.01);
    await expect(page.getByTestId('pin-member-jester').getByTestId('chip-here-for'), "Cass's chip, above his pin").toHaveText(JESTER_CHIP_TEXT);
    await expect(page.getByTestId('chip-here-for'), 'and the only chip').toHaveCount(1);

    // The handle then shows his detail.
    await sheetHandle(page).click();
    await expectDetailAtTall(page, 'after the handle tap');
    await expect(page.locator('.realm-detail-name'), "the detail is Cass's").toHaveText(jester.name);
  });

  // [AC-22] The same end state from a row tapped at 80 %: the collapse and the flight run together, and the flight uses the TARGET Peek padding and not the measured one (03 section 4.3), which is
  // why the distance is asserted after settled() and not while the sheet is still on its way down.
  test('[AC-22] tapping the Cass row with the sheet at 80 % ends in the same state: Peek, his header and the pin in the Peek rectangle', async ({ page }) => {
    await demo(page, { sheet: '80' });
    await mapReady(page);
    await expect(page.getByTestId('row-member-jester'), 'the Drivers list is showing').toBeVisible();
    expect(await sheetState(page), 'the sheet starts at 80 %').toBe('80');

    await page.getByTestId('row-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected from his row at 80 %');
    await expect(selectionHeader(page).locator('.realm-row-name'), 'the header is his').toHaveText(castMember(loadDemoCast(), 'jester').name);
    expect((await readHook(page, 'camera')).zoom, 'camera().zoom is at least 15').toBeGreaterThanOrEqual(SELECTION_MIN_ZOOM - 0.01);
  });

  // [AC-23] D45 as amended by D46. At Peek the empty map clears the selection. The header is not inert any more (D46): a tap on it, anywhere but the ✕, is the handle's tap (80 %, the detail,
  // the selection kept); the ✕ clears and does not expand. At 80 % (the detail) the empty map collapses to Peek and KEEPS the selection; a second tap clears it.
  test('[AC-23] empty map at Peek clears the selection; a header tap opens the detail at 80 % with the selection kept; there the empty map collapses to Peek and a second tap clears it; the ✕ clears and does not expand', async ({ page }) => {
    test.slow(); // five selection and collapse flights with their re-centres
    await demo(page);
    await mapReady(page);
    const jester = castMember(loadDemoCast(), 'jester');

    // At Peek the empty map clears: the header is gone, the tabs are back, and with nothing selected the chip is Alden's alone (his pin is on screen once the camera is back at the default view).
    await page.getByTestId('pin-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected');
    await tapEmptyMap(page);
    await expectListAtPeek(page, 'after the empty-map tap at Peek');
    await page.getByTestId('btn-recenter').click();
    await expectRecenterState(page, 'default', 'back at the default view');
    await expect(page.getByTestId('chip-here-for'), 'one chip on the page').toHaveCount(1);
    await expect(page.getByTestId('pin-member-king').getByTestId('chip-here-for'), "the chip is Alden's again").toHaveText(KING_CHIP_TEXT);

    // D46: a tap on the header (the name, not the ✕) goes to 80 %, shows the detail and keeps the selection.
    await page.getByTestId('pin-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected again');
    await selectionHeader(page).locator('.realm-row-name').click();
    await expectDetailAtTall(page, 'after a tap on the header');
    await expect(page.locator('.realm-detail-name'), 'the selection is kept: the detail is Cass\'s').toHaveText(jester.name);
    await expectSelectionCentred(page, { kind: 'member', id: 'jester' }, true, 'the detail at 80 %');

    // At 80 % the empty map collapses to Peek and keeps the selection (the header with its ✕, no tabs) ...
    await tapEmptyMap(page);
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'after the empty-map tap at 80 %');
    await expect(selectionHeader(page).locator('.realm-row-name'), 'the selection is kept').toHaveText(jester.name);
    await expect(page.getByTestId('sheet-selection-clear'), 'the header has its ✕').toBeVisible();
    // ... and a second tap clears it.
    await tapEmptyMap(page);
    await expectListAtPeek(page, 'after the second empty-map tap');

    // The ✕ clears and does not expand: the sheet stays at Peek, the tabs return, no detail.
    await page.getByTestId('pin-member-jester').click();
    await expectPeekSelection(page, { kind: 'member', id: 'jester' }, 'Cass selected a third time');
    await page.getByTestId('sheet-selection-clear').click();
    await expectListAtPeek(page, 'after the ✕');
    await settled(page);
    expect(await sheetState(page), 'the ✕ did not expand the sheet').toBe('peek');
  });

  // [AC-23] The panel: there is no Peek, so one tap on the empty map clears the selection at once (the detail is replaced by the list).
  test('[AC-23] in the 884 × 916 panel one tap on the empty map clears the selection', { tag: ['@unfolded'] }, async ({ page }) => {
    await demo(page);
    await mapReady(page);
    expect(await sheetState(page), 'the panel layout').toBe('panel');

    await page.getByTestId('pin-member-jester').click();
    await expect(page.getByTestId('detail-back'), "the panel shows Cass's detail").toBeVisible();
    await expect(page.locator('.realm-detail-name'), 'the detail is Cass\'s').toHaveText(castMember(loadDemoCast(), 'jester').name);
    await expectSelectionCentred(page, { kind: 'member', id: 'jester' }, false, 'the panel with Cass selected');

    await tapEmptyMap(page);
    await expect(page.getByTestId('detail-back'), 'the detail is gone at once').toHaveCount(0);
    await expect(page.getByTestId('tab-drivers'), 'the tabs are back').toBeVisible();
    await expect(page.locator('[data-testid^="row-member-"]'), 'the Drivers list is back').toHaveCount(5);
    expect(await sheetState(page), 'still the panel').toBe('panel');
  });

  // [AC-24] The Back chain with the depth tokens on (`Realm:Ui:HistoryTokens`, default true; D45 restated, D31, D47): a token entry `#r<n>` per level (an overlay, the selection, the Tall size).
  // The tests open the proxy's own page first, so the entry before the app is one they can see, and press Back only after the depth they expect has been pushed (the push follows the
  // server's decision).
  test('[AC-24] Back from the detail: Peek with Cass still selected, then the selection cleared with the list at Peek, then the page is left', async ({ page }) => {
    await page.goto(BEFORE_THE_APP);
    await demo(page);
    await mapReady(page);

    await page.getByTestId('pin-member-jester').click();
    await expectPeekHeader(page, 'Cass selected');
    await expectHistoryDepth(page, 1, 'with a selection at Peek');
    await sheetHandle(page).click();
    await expectDetailAtTall(page, 'the detail');
    await expectHistoryDepth(page, 2, 'with the detail at 80 %');

    await page.goBack();
    await expectPeekHeader(page, 'first Back');
    await expect(selectionHeader(page).locator('.realm-row-name'), 'first Back keeps Cass selected').toHaveText(castMember(loadDemoCast(), 'jester').name);
    await expectHistoryDepth(page, 1, 'after the first Back');

    await page.goBack();
    await expectListAtPeek(page, 'second Back');
    await expectHistoryDepth(page, 0, 'after the second Back');

    await page.goBack();
    await expect(page, 'the third Back leaves the page').toHaveURL(new RegExp(`${BEFORE_THE_APP}$`));
  });

  test('[AC-24] Back after a row tapped at 80 %: the list at Peek with nothing selected (not 80 %), then the page is left', async ({ page }) => {
    await page.goto(BEFORE_THE_APP);
    await demo(page, { sheet: '80' });
    await mapReady(page);
    await expectHistoryDepth(page, 1, 'with the list at 80 %');

    await page.getByTestId('row-member-jester').click();
    await expectPeekHeader(page, 'Cass selected from his row');
    await expectHistoryDepth(page, 1, 'the row tap keeps the depth (the Tall step is replaced by the selection)');

    await page.goBack();
    await expectListAtPeek(page, 'first Back');
    await expectHistoryDepth(page, 0, 'after the first Back');

    await page.goBack();
    await expect(page, 'the second Back leaves the page').toHaveURL(new RegExp(`${BEFORE_THE_APP}$`));
  });

  test('[AC-24] Back with the list at 80 % and nothing selected: Peek, then the page is left', async ({ page }) => {
    await page.goto(BEFORE_THE_APP);
    await demo(page, { sheet: '80' });
    await mapReady(page);
    expect(await sheetState(page), 'the sheet starts at 80 %').toBe('80');
    await expectHistoryDepth(page, 1, 'with the list at 80 %');

    await page.goBack();
    await expect.poll(() => sheetState(page), { message: 'sheet().state after Back' }).toBe('peek');
    await expectListAtPeek(page, 'first Back');
    await expectHistoryDepth(page, 0, 'after the first Back');

    await page.goBack();
    await expect(page, 'the second Back leaves the page').toHaveURL(new RegExp(`${BEFORE_THE_APP}$`));
  });

  test('[AC-24] with Settings open the first Back closes it and leaves the sheet state as it was; the next Back clears the selection', async ({ page }) => {
    await page.goto(BEFORE_THE_APP);
    await demo(page);
    await mapReady(page);

    await page.getByTestId('pin-member-jester').click();
    await expectPeekHeader(page, 'Cass selected');
    await expectHistoryDepth(page, 1, 'with a selection at Peek');
    await page.getByTestId('btn-settings').click();
    await expect(page.getByTestId('settings-dialog'), 'Settings is open').toBeVisible();
    await expectHistoryDepth(page, 2, 'with Settings open over the selection');

    await page.goBack();
    await expect(page.getByTestId('settings-dialog'), 'the first Back closes Settings').toHaveCount(0);
    await expectPeekHeader(page, 'after closing Settings');
    await expect(selectionHeader(page).locator('.realm-row-name'), 'the sheet state is unchanged: Cass is still selected').toHaveText(castMember(loadDemoCast(), 'jester').name);
    await expectHistoryDepth(page, 1, 'after closing Settings');

    await page.goBack();
    await expectListAtPeek(page, 'second Back');
  });

  // The detail opened by a tap on the HEADER (D46) is the same level of depth as the one opened by the handle: Back returns to Peek with the selection kept (01 section 5.7).
  test('[AC-24] Back after a detail opened by a tap on the header returns to Peek with the selection kept', async ({ page }) => {
    await page.goto(BEFORE_THE_APP);
    await demo(page);
    await mapReady(page);

    await page.getByTestId('pin-member-jester').click();
    await expectPeekHeader(page, 'Cass selected');
    await expectHistoryDepth(page, 1, 'with a selection at Peek');
    await selectionHeader(page).locator('.realm-row-name').click();
    await expectDetailAtTall(page, 'the detail opened by the header');
    await expectHistoryDepth(page, 2, 'with the detail at 80 %');

    await page.goBack();
    await expectPeekHeader(page, 'Back from the header-opened detail');
    await expect(selectionHeader(page).locator('.realm-row-name'), 'Cass is still selected').toHaveText(castMember(loadDemoCast(), 'jester').name);
    await expectHistoryDepth(page, 1, 'after Back');
  });
});
