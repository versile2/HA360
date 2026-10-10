// The sheet, its handle, the right button stack and the layout split: AC-02 and AC-04 to AC-11 of 01 section 11 (as restated for D42 and D45), on the
// Demo app behind the ingress proxy. This is S7a's file; `ac-a-shell.spec.ts` belongs to another slice (D72).
//
// First contact with MudXSheet (R-01 to R-03), so every failure has to say what it saw. Three things make that cheap:
//   - every geometry assertion goes through expectNear(), whose message carries the number it got, the number it wanted and the name of the thing;
//   - waiting is polling with the last observation in the error (until(), untilSettled()): "gave up after 10000 ms; last value {...}", never a bare timeout;
//   - after a FAILED test the afterEach hook attaches sheet-diagnostics.json: the popover's HTML, the computed geometry of the popover, the contract
//     element and the right stack, <html>'s attributes and variables, the body's scroll lock, the hooks, and the rectangle of every data-testid.
//
// The numbers are those of the AC rows at 412 x 915 (phone) and are computed from the viewport for the other three sizes: Peek is 19 % of the height with
// a floor of 168 px (174 at 915, 168 at 800), 80 % is 0.8 x the height (732 at 915), the navigation bar is 64 px high. The tolerance is 2 px.
//
// There is no drag (D73, which supersedes the drag half of D42): MudXSheet runs with EnableDragToSize=false, the handle toggles Peek and 80 % by tap or
// key only, and the sheet only ever settles at the two heights. AC-07 now asserts that a press-and-move on the handle resizes nothing.

import type { Locator, Page } from '@playwright/test';

import { demo, expect, readHook, settled, test, type SheetInfo } from '../fixtures.js';

/** Pixels of slack on every geometry assertion. */
const TOL = 2;
/** The bottom navigation bar's height (01 section 3.2). */
const NAV_H = 64;
/** The Peek floor: the navigation bar plus the 48 px handle, the 48 px tabs and 8 px of padding. */
const PEEK_MIN = NAV_H + 104;
/** MudXSheet's CurrentSize at Peek and at 80 % (PresetSizes [19, 80], D42). */
const PEEK_PERCENT = 19;
const TALL_PERCENT = 80;

const peekHeight = (viewportHeight: number): number => Math.max((PEEK_PERCENT / 100) * viewportHeight, PEEK_MIN);
const tallHeight = (viewportHeight: number): number => (TALL_PERCENT / 100) * viewportHeight;

// ---- small helpers -------------------------------------------------------------------------------------------------------------------------

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

function viewport(page: Page): { width: number; height: number } {
  const size = page.viewportSize();
  if (size === null) throw new Error('the page has no viewport size');
  return size;
}

async function boxOf(locator: Locator, what: string): Promise<Box> {
  const box = await locator.boundingBox();
  if (box === null) throw new Error(`${what}: no bounding box (the element is missing, display:none, or detached)`);
  return box;
}

function expectNear(actual: number, expected: number, tolerance: number, what: string): void {
  expect(Math.abs(actual - expected), `${what}: got ${actual.toFixed(2)}, want ${expected.toFixed(2)} (+-${tolerance})`).toBeLessThanOrEqual(tolerance);
}

const popover = (page: Page): Locator => page.locator('div[mudsheet]');
const contract = (page: Page): Locator => page.getByTestId('sheet');
const handle = (page: Page): Locator => page.getByTestId('sheet-handle');
const nav = (page: Page): Locator => page.getByRole('navigation', { name: 'Main' });

/** Polls `read` until `ok` accepts the value; the error of a give-up carries the last value, which a bare timeout never does. */
async function until<T>(what: string, read: () => Promise<T>, ok: (value: T) => boolean, timeoutMs = 10_000): Promise<T> {
  const deadline = Date.now() + timeoutMs;
  let last: T | undefined;
  let lastError: unknown;
  for (;;) {
    try {
      last = await read();
      if (ok(last)) return last;
    } catch (error) {
      lastError = error;
    }
    if (Date.now() > deadline) {
      throw new Error(`${what}: gave up after ${timeoutMs} ms; last value ${JSON.stringify(last)}${lastError === undefined ? '' : `; last error ${String(lastError)}`}`);
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

/** Waits until the sheet reports `state`, is open, has a height, and has not moved for 300 ms: a settled sheet, not one in a transition. */
async function untilSettled(page: Page, state: SheetInfo['state'], what: string): Promise<SheetInfo> {
  let previous: SheetInfo | undefined;
  let since = Date.now();
  return until(
    `${what}: waiting for window.__realm.sheet() to settle at '${state}'`,
    () => readHook(page, 'sheet'),
    (info) => {
      const moved = previous === undefined || previous.state !== info.state || Math.abs(previous.heightPx - info.heightPx) > 0.5 || Math.abs(previous.topPx - info.topPx) > 0.5;
      if (moved) since = Date.now();
      previous = info;
      return info.state === state && info.open && info.heightPx > 0 && Date.now() - since >= 300;
    },
  );
}

// ---- diagnostics on failure ----------------------------------------------------------------------------------------------------------------

test.afterEach(async ({ page }, testInfo) => {
  if (testInfo.status === testInfo.expectedStatus) return;
  const dump = await page
    .evaluate(() => {
      const rect = (element: Element | null) => {
        if (element === null) return null;
        const r = element.getBoundingClientRect();
        return { x: Math.round(r.x * 100) / 100, y: Math.round(r.y * 100) / 100, width: Math.round(r.width * 100) / 100, height: Math.round(r.height * 100) / 100 };
      };
      const computed = (element: Element | null, properties: string[]) => {
        if (element === null) return null;
        const style = getComputedStyle(element);
        return Object.fromEntries(properties.map((property) => [property, style.getPropertyValue(property)]));
      };
      const geometry = ['position', 'top', 'right', 'bottom', 'left', 'width', 'height', 'min-width', 'max-width', 'min-height', 'max-height', 'transform', 'z-index', 'visibility', 'opacity', 'display', 'overflow'];
      const popoverElement = document.querySelector('div[mudsheet]');
      const containerElement = document.querySelector('[data-testid="sheet"]');
      const stackElement = document.querySelector('.realm-right-stack');
      const html = document.documentElement;
      const hooks = (window as Window & { __realm?: Record<string, (() => unknown) | undefined> }).__realm;
      const call = (name: string): unknown => {
        try {
          return hooks?.[name]?.();
        } catch (error) {
          return `threw: ${String(error)}`;
        }
      };
      return {
        viewport: { width: window.innerWidth, height: window.innerHeight },
        popoverCount: document.querySelectorAll('div[mudsheet]').length,
        contractCount: document.querySelectorAll('[data-testid="sheet"]').length,
        popoverRect: rect(popoverElement),
        popoverStyle: computed(popoverElement, geometry),
        popoverInlineStyle: popoverElement?.getAttribute('style') ?? null,
        popoverClasses: popoverElement?.className ?? null,
        containerRect: rect(containerElement),
        containerStyle: computed(containerElement, geometry),
        containerInlineStyle: containerElement?.getAttribute('style') ?? null,
        containerAttributes: containerElement === null ? null : Object.fromEntries([...containerElement.attributes].map((a) => [a.name, a.value])),
        stackRect: rect(stackElement),
        stackStyle: computed(stackElement, ['visibility', 'opacity', 'bottom', 'right']),
        stackInert: stackElement?.hasAttribute('inert') ?? null,
        htmlAttributes: Object.fromEntries([...html.attributes].map((a) => [a.name, a.value])),
        htmlVariables: { sheetH: html.style.getPropertyValue('--realm-sheet-h'), sheetTop: html.style.getPropertyValue('--realm-sheet-top') },
        body: { className: document.body.className, paddingRight: getComputedStyle(document.body).paddingRight, style: document.body.getAttribute('style') },
        hooks: { sheet: call('sheet'), mapPadding: call('mapPadding'), camera: call('camera'), stats: call('stats') },
        testIds: [...document.querySelectorAll('[data-testid]')].map((element) => ({ id: element.getAttribute('data-testid'), rect: rect(element), visibility: getComputedStyle(element).visibility })),
        popoverHtml: (popoverElement?.outerHTML ?? '(no div[mudsheet] in the DOM)').slice(0, 12_000),
      };
    })
    .catch((error: unknown) => ({ error: `could not read the page: ${String(error)}` }));
  await testInfo.attach('sheet-diagnostics.json', { body: JSON.stringify(dump, null, 2), contentType: 'application/json' });
});

// ---- AC-02 ---------------------------------------------------------------------------------------------------------------------------------

test.describe('navigation, right stack, sheet and layout', () => {
  test('[AC-02] the bottom nav has exactly two links, 64 px high at the bottom edge, each at least 48 px tall, and no Safety or Membership', async ({ page }) => {
    await demo(page);
    const { height } = viewport(page);

    await expect(nav(page).getByRole('link'), 'the links inside the navigation named "Main"').toHaveText(['Location', 'Driving']); // the links; Settings is a button (D105)
    const bar = await boxOf(nav(page), 'the bottom nav');
    expectNear(bar.height, NAV_H, TOL, 'nav height');
    expectNear(bar.y, height - NAV_H, TOL, `nav top (y = ${height - NAV_H} at a viewport ${height} high)`);
    for (const id of ['nav-location', 'nav-driving', 'btn-settings']) {
      const link = await boxOf(page.getByTestId(id), id);
      expect(link.height, `${id} is at least 48 px tall`).toBeGreaterThanOrEqual(48 - 0.5);
    }
    await expect(page.getByText(/Safety|Membership/), 'no element mentions Safety or Membership').toHaveCount(0);
  });

  // ---- AC-04 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-04] the right stack has two 48 px buttons, recenter above layers, 12 px apart at x = 352, the layers bottom 12 px above the Peek sheet', async ({ page }) => {
    await demo(page);
    const { width, height } = viewport(page);
    const sheet = await untilSettled(page, 'peek', 'on load');

    await expect(page.locator('.realm-right-stack button'), 'the buttons of the right stack').toHaveCount(2);
    const recenter = await boxOf(page.getByTestId('btn-recenter'), 'btn-recenter');
    const layers = await boxOf(page.getByTestId('btn-layers'), 'btn-layers');
    for (const [name, box] of [['btn-recenter', recenter], ['btn-layers', layers]] as const) {
      expectNear(box.width, 48, TOL, `${name} width`);
      expectNear(box.height, 48, TOL, `${name} height`);
      expectNear(box.x, width - 12 - 48, TOL, `${name} left edge (x = 352 at 412 wide)`);
    }
    expect(recenter.y, 'btn-recenter is above btn-layers').toBeLessThan(layers.y);
    expectNear(layers.y - (recenter.y + recenter.height), 12, TOL, 'the gap between the two buttons');
    expectNear(layers.y + layers.height, height - peekHeight(height) - 12, TOL, 'btn-layers bottom edge (y = 729 at 915): 12 px above the sheet top');
    expectNear(layers.y + layers.height, sheet.topPx - 12, TOL, 'btn-layers bottom edge against window.__realm.sheet().topPx - 12');

    // The "+" slot is reserved and renders nothing, and nothing in the app adds a person, a vehicle or a place (01 section 1 and 5.8).
    await expect(page.getByTestId('slot-add'), 'slot-add is never rendered in v1').toHaveCount(0);
    await expect(page.getByText('+', { exact: true }), 'no element whose text is "+"').toHaveCount(0);
    await expect(page.getByText(/Add a (person|vehicle|place)/i), 'no "Add a ..." element').toHaveCount(0);
    await expect(page.getByRole('button', { name: /^(\+|Add\b.*|Raise a bubble)$/ }), 'no add button by name').toHaveCount(0);
  });

  // ---- AC-05 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-05] on load the sheet is at Peek: 174 high (168 at 800), full width, flush with the bottom; only the handle summary and the tabs show', { tag: ['@phone', '@phone-short'] }, async ({ page }) => {
    await demo(page);
    const { width, height } = viewport(page);
    const sheet = await untilSettled(page, 'peek', 'on load');

    expectNear(sheet.heightPx, peekHeight(height), TOL, `window.__realm.sheet().heightPx at Peek (viewport ${height} high)`);
    const outer = await boxOf(popover(page), 'div[mudsheet] (the popover, the whole sheet)');
    expectNear(outer.height, peekHeight(height), TOL, 'popover height');
    expectNear(outer.width, width, TOL, 'popover width (full viewport width, no cap)');
    expectNear(outer.x, 0, TOL, 'popover left');
    expectNear(outer.y + outer.height, height, TOL, 'popover bottom edge (flush with the viewport bottom)');
    const inner = await boxOf(contract(page), '[data-testid="sheet"] (the contract element)');
    expectNear(inner.width, width, TOL, 'contract element width');
    expectNear(inner.y + inner.height, height, TOL, 'contract element bottom edge');
    expectNear(inner.height, outer.height, TOL, 'contract element height against the popover');

    // Visible contents: the handle with the summary, and the three tabs with Drivers selected, both above the navigation bar that tucks over the lower 64 px.
    const bar = await boxOf(nav(page), 'the bottom nav');
    await expect(page.getByTestId('sheet-summary'), 'the handle summary').toHaveText('4 in the Realm · 1 driving');
    const summary = await boxOf(page.getByTestId('sheet-summary'), 'sheet-summary');
    expect(summary.y + summary.height, 'the summary is above the navigation bar').toBeLessThanOrEqual(bar.y + 1);
    for (const id of ['tab-drivers', 'tab-vehicles', 'tab-places']) {
      await expect(page.getByTestId(id), `${id} is visible`).toBeVisible();
      const tab = await boxOf(page.getByTestId(id), id);
      expect(tab.y + tab.height, `${id} is above the navigation bar (not covered by it)`).toBeLessThanOrEqual(bar.y + 1);
      expect(tab.y, `${id} is below the sheet's top edge`).toBeGreaterThanOrEqual(outer.y - 1);
    }
    await expect(page.getByTestId('tab-drivers'), 'Drivers is selected').toHaveAttribute('aria-selected', 'true');
    await expect(page.getByTestId('tab-vehicles')).toHaveAttribute('aria-selected', 'false');
    await expect(page.getByTestId('tab-places')).toHaveAttribute('aria-selected', 'false');
    await expect(page.locator('[data-testid^="row-"]').filter({ visible: true }), 'no list row is visible at Peek').toHaveCount(0);
  });

  // ---- AC-06 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-06] a tap toggles Peek, 80 %, Peek; the state hook and aria-expanded follow; the sheet stays open, also after Esc', async ({ page }) => {
    await demo(page);
    const { height } = viewport(page);
    const settledHeights: number[] = [];

    settledHeights.push((await untilSettled(page, 'peek', 'on load')).heightPx);
    await expect(handle(page), 'aria-expanded at Peek').toHaveAttribute('aria-expanded', 'false');

    await handle(page).click();
    const tall = await untilSettled(page, '80', 'after the first tap');
    settledHeights.push(tall.heightPx);
    expectNear(tall.heightPx, tallHeight(height), TOL, 'sheet height at 80 % (732 at 915)');
    await expect(handle(page), 'aria-expanded at 80 %').toHaveAttribute('aria-expanded', 'true');
    await expect(popover(page), 'the sheet element is present after the tap').toHaveCount(1);

    await handle(page).click();
    settledHeights.push((await untilSettled(page, 'peek', 'after the second tap')).heightPx);
    await expect(handle(page), 'aria-expanded back at Peek').toHaveAttribute('aria-expanded', 'false');

    // Esc never closes the sheet (CloseOnEscapeKey is false), at Peek and at 80 %.
    await handle(page).focus();
    await page.keyboard.press('Escape');
    const afterEscPeek = await untilSettled(page, 'peek', 'after Esc at Peek');
    expect(afterEscPeek.open, 'the sheet is open after Esc at Peek').toBe(true);
    await handle(page).click();
    await untilSettled(page, '80', 'before Esc at 80 %');
    await page.keyboard.press('Escape');
    const afterEscTall = await untilSettled(page, '80', 'after Esc at 80 %');
    expect(afterEscTall.open, 'the sheet is open after Esc at 80 %').toBe(true);
    settledHeights.push(afterEscTall.heightPx);
    await handle(page).click();
    settledHeights.push((await untilSettled(page, 'peek', 'back at Peek after Esc')).heightPx);
    await expect(popover(page), 'the sheet element is present at the end').toHaveCount(1);

    // No other height is ever settled on (no 512, no 842).
    const allowed = [peekHeight(height), tallHeight(height)];
    for (const settledHeight of settledHeights) {
      expect(
        allowed.some((candidate) => Math.abs(settledHeight - candidate) <= TOL),
        `a settled height of ${settledHeight.toFixed(1)} px is neither Peek (${allowed[0].toFixed(1)}) nor 80 % (${allowed[1].toFixed(1)}); settled heights seen: ${settledHeights.map((h) => h.toFixed(1)).join(', ')}`,
      ).toBe(true);
    }
  });

  test('[AC-06] a tab tapped at Peek opens the sheet to 80 % and switches to that tab; Back returns to Peek', async ({ page }) => {
    await demo(page);
    await untilSettled(page, 'peek', 'on load');

    await page.getByTestId('tab-places').click();
    await untilSettled(page, '80', 'after tapping Places at Peek');
    await expect(page.getByTestId('tab-places'), 'Places is selected').toHaveAttribute('aria-selected', 'true');
    await expect(page.getByTestId('tab-drivers'), 'Drivers is not').toHaveAttribute('aria-selected', 'false');

    await page.goBack();
    await untilSettled(page, 'peek', 'after Back from 80 %');
    await expect(page.getByTestId('tab-places'), 'the tab stays on Places at Peek').toHaveAttribute('aria-selected', 'true');

    await page.getByTestId('tab-vehicles').click();
    await untilSettled(page, '80', 'after tapping Trackers at Peek');
    await expect(page.getByTestId('tab-vehicles'), 'Trackers is selected').toHaveAttribute('aria-selected', 'true');
  });

  test('[AC-06] keyboard on the focused handle: Enter and Space toggle, ArrowUp and End go to 80 %, ArrowDown and Home go to Peek', async ({ page }) => {
    await demo(page);
    await untilSettled(page, 'peek', 'on load');
    await handle(page).focus();

    const steps: Array<[string, SheetInfo['state']]> = [
      ['Enter', '80'],
      ['Space', 'peek'],
      ['ArrowUp', '80'],
      ['ArrowDown', 'peek'],
      ['End', '80'],
      ['Home', 'peek'],
      ['ArrowDown', 'peek'],
      ['ArrowUp', '80'],
      ['ArrowUp', '80'],
      ['Home', 'peek'],
    ];
    for (const [key, expected] of steps) {
      await expect(handle(page), `the handle has focus before ${key} (the re-render must not replace the button)`).toBeFocused();
      await page.keyboard.press(key);
      await untilSettled(page, expected, `after ${key}`);
      await expect(handle(page), `aria-expanded after ${key}`).toHaveAttribute('aria-expanded', expected === '80' ? 'true' : 'false');
    }
  });

  test('[AC-06] ?sheet=80 starts the sheet at 80 % with the right stack hidden', async ({ page }) => {
    await demo(page, { sheet: '80' });
    const { height } = viewport(page);

    const sheet = await untilSettled(page, '80', 'on load with sheet=80');
    expectNear(sheet.heightPx, tallHeight(height), TOL, 'sheet height at 80 %');
    await expect(handle(page)).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByTestId('btn-layers'), 'the right stack is hidden at 80 %').toBeHidden();
  });

  // ---- AC-07 -------------------------------------------------------------------------------------------------------------------------------

  // D73 supersedes the drag half of D42: MudXSheet runs with EnableDragToSize=false, so there is no drag to snap. What this row asserts now is the other
  // side of the same ruling: a press on the handle that moves away (up at Peek, down at 80 %) resizes nothing and takes no pointer capture, and a release
  // away from the handle toggles nothing (the pointerup does not land on the button, so no click).
  const NO_DRAG: Array<{ at: 'peek' | '80'; dy: number; what: string }> = [
    { at: 'peek', dy: -300, what: 'up 300 px at Peek' },
    { at: '80', dy: 300, what: 'down 300 px at 80 %' },
  ];
  for (const { at, dy, what } of NO_DRAG) {
    test(`[AC-07] a press on the handle that moves ${what} resizes nothing (no drag, D73): the sheet stays at ${at === 'peek' ? 'Peek' : '80 %'} and open`, async ({ page }) => {
      await demo(page);
      const { height } = viewport(page);
      await untilSettled(page, 'peek', 'on load');
      if (at === '80') {
        await handle(page).click();
        await untilSettled(page, '80', 'before the press');
      }
      const wantedHeight = at === '80' ? tallHeight(height) : peekHeight(height);

      const box = await boxOf(handle(page), 'sheet-handle');
      const x = box.x + box.width / 2;
      const y = box.y + box.height / 2;
      await page.mouse.move(x, y);
      await page.mouse.down();
      await page.mouse.move(x, y + dy, { steps: 12 });
      // No sleep (review R2-05). What a drag would do is done by script on the pointer events themselves (the pointer capture on the pointerdown, the dragging class and the new height on the
      // first pointermove), and Playwright has dispatched all twelve moves before the call above returns; a frame boundary then lets every handler and style change land, and the hook is read
      // after the style has settled. The assertions retry, so a drag that was merely slow would still be seen, and the one thing a test cannot wait for, a drag that never happens, is what
      // the settled sheet and the absent capture say.
      await page.evaluate(() => new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      await settled(page);
      await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: `window.__realm.sheet().state while the pointer is held ${what}` }).toBe(at);
      const during = await readHook(page, 'sheet');
      expectNear(during.heightPx, wantedHeight, TOL, `the sheet height while the pointer is held ${what}`);
      expect(
        await page.evaluate(() => document.querySelector('.mud-sheet-handle')?.hasPointerCapture(1) ?? false),
        'MudX took no pointer capture on the handle wrapper (it has no drag)',
      ).toBe(false);
      await expect(contract(page), "the sheet container is not in MudX's dragging state").not.toHaveClass(/mud-sheet-dragging/);
      await page.mouse.up();

      const after = await untilSettled(page, at, `after releasing away from the handle (${what})`);
      expectNear(after.heightPx, wantedHeight, TOL, 'the settled height after the release');
      expect(after.open, 'the sheet is open after the release').toBe(true);
      await expect(handle(page), 'aria-expanded after the release').toHaveAttribute('aria-expanded', at === '80' ? 'true' : 'false');
      await expect(popover(page).locator('.mud-sheet-handle'), 'the handle wrapper is not marked draggable').not.toHaveClass(/mud-draggable/);
    });
  }

  // ---- AC-08 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-08] the right stack sits 12 px above the sheet while it is under 50 % of the viewport height, and is hidden and unfocusable from 50 % up', async ({ page }) => {
    await demo(page);
    await untilSettled(page, 'peek', 'on load');
    const layers = page.getByTestId('btn-layers');
    const stackBottom = async (): Promise<number> => {
      const box = await boxOf(layers, 'btn-layers');
      return box.y + box.height;
    };
    const follows = async (what: string): Promise<void> => {
      await until(
        `${what}: btn-layers bottom edge 12 px above the sheet top`,
        async () => ({ bottom: await stackBottom(), top: (await readHook(page, 'sheet')).topPx }),
        (observed) => Math.abs(observed.bottom - (observed.top - 12)) <= TOL,
      );
    };

    await follows('at Peek');

    // The stack is under the ruling of 50 % of the viewport height: at 80 % it is hidden (--realm-sheet-h is written by the script from a ResizeObserver,
    // no server call, so the stack follows the sheet through its 250 ms transition; there is no drag, D73, so no resting height in between).
    await handle(page).click();
    await untilSettled(page, '80', 'after the tap');

    // At 80 %: visibility hidden, inert, and not focusable (so not in the tab order).
    await expect(layers, 'btn-layers at 80 %').toBeHidden();
    await expect(page.getByTestId('btn-recenter'), 'btn-recenter at 80 %').toBeHidden();
    const state = await page.evaluate(() => {
      const button = document.querySelector<HTMLElement>('[data-testid="btn-layers"]');
      if (button === null) return { present: false, visibility: '', inert: false, focusable: false };
      button.focus();
      return { present: true, visibility: getComputedStyle(button).visibility, inert: button.closest('[inert]') !== null, focusable: document.activeElement === button };
    });
    expect(state.present, 'btn-layers stays in the DOM at 80 % (hidden, not removed)').toBe(true);
    expect(state.visibility, 'computed visibility of btn-layers at 80 %').toBe('hidden');
    expect(state.inert, 'btn-layers is inside an inert container at 80 %').toBe(true);
    expect(state.focusable, 'btn-layers cannot take focus at 80 %').toBe(false);

    // It returns when the sheet is back at Peek.
    await handle(page).click();
    await untilSettled(page, 'peek', 'back at Peek');
    await expect(layers, 'the stack is back at Peek').toBeVisible();
    await follows('back at Peek');
  });

  // ---- AC-09 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-09] the map padding is {72, 72, 190, 16} at Peek and {72, 16, 748, 16} at 80 %, and the camera does not move with the sheet', async ({ page }) => {
    await demo(page);
    const { height } = viewport(page);
    await untilSettled(page, 'peek', 'on load');

    const atPeek = await until(
      'window.__realm.mapPadding() at Peek',
      () => readHook(page, 'mapPadding'),
      (padding) => Math.abs(padding.bottom - (peekHeight(height) + 16)) <= TOL,
    );
    expectNear(atPeek.top, 72, TOL, 'padding.top at Peek');
    expectNear(atPeek.right, 72, TOL, 'padding.right at Peek');
    expectNear(atPeek.bottom, 190, TOL, 'padding.bottom at Peek (the sheet 174 + 16)');
    expectNear(atPeek.left, 16, TOL, 'padding.left at Peek');
    await settled(page);
    const cameraAtPeek = await readHook(page, 'camera');

    await handle(page).click();
    await untilSettled(page, '80', 'after the tap');
    const atTall = await until(
      'window.__realm.mapPadding() at 80 %',
      () => readHook(page, 'mapPadding'),
      (padding) => Math.abs(padding.bottom - (tallHeight(height) + 16)) <= TOL,
    );
    expectNear(atTall.top, 72, TOL, 'padding.top at 80 %');
    expectNear(atTall.right, 16, TOL, 'padding.right at 80 % (the right stack is hidden, so its 56 px is not reserved)');
    expectNear(atTall.bottom, 748, TOL, 'padding.bottom at 80 % (the sheet 732 + 16)');
    expectNear(atTall.left, 16, TOL, 'padding.left at 80 %');
    await settled(page);
    const cameraAtTall = await readHook(page, 'camera');

    // Nothing is selected, so the sheet changing size does not move the camera.
    expectNear(cameraAtTall.zoom, cameraAtPeek.zoom, 0.001, `camera zoom, Peek ${cameraAtPeek.zoom} to 80 % ${cameraAtTall.zoom}`);
    expectNear(cameraAtTall.center[0], cameraAtPeek.center[0], 1e-5, `camera centre longitude, Peek ${cameraAtPeek.center[0]} to 80 % ${cameraAtTall.center[0]}`);
    expectNear(cameraAtTall.center[1], cameraAtPeek.center[1], 1e-5, `camera centre latitude, Peek ${cameraAtPeek.center[1]} to 80 % ${cameraAtTall.center[1]}`);
  });

  // ---- AC-10 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-10] the Compact sheet is a bottom sheet at the full viewport width, with no width cap', async ({ page }) => {
    await demo(page);
    const { width, height } = viewport(page);
    await untilSettled(page, 'peek', 'on load');

    const outer = await boxOf(popover(page), 'div[mudsheet]');
    expectNear(outer.width, width, TOL, 'popover width (412 at 412)');
    expectNear(outer.x, 0, TOL, 'popover left');
    expectNear(outer.y + outer.height, height, TOL, 'popover bottom edge');
    const style = await popover(page).evaluate((element) => {
      const computed = getComputedStyle(element);
      return { position: computed.position, maxWidth: computed.maxWidth, left: computed.left, bottom: computed.bottom };
    });
    expect(style.position, 'the popover is fixed to the viewport').toBe('fixed');
    expect(['100%', 'none', `${width}px`], `computed max-width of the popover (the 640 cap is not used in v1): ${style.maxWidth}`).toContain(style.maxWidth);
  });

  test('[AC-10] a wider window keeps the bottom sheet at the full width: 700 x 900 and a landscape phone 915 x 412', async ({ page }) => {
    await demo(page);
    await untilSettled(page, 'peek', 'on load');

    for (const [width, height] of [[700, 900], [915, 412]] as const) {
      await page.setViewportSize({ width, height });
      const sheet = await until(
        `the sheet at ${width} x ${height}: a full-width bottom sheet at Peek (LayoutResolver: Compact below 840 wide or below 560 high)`,
        async () => ({ info: await readHook(page, 'sheet'), box: await boxOf(popover(page), 'div[mudsheet]') }),
        (observed) => observed.info.state === 'peek' && Math.abs(observed.box.width - width) <= TOL && Math.abs(observed.box.height - peekHeight(height)) <= TOL,
      );
      expectNear(sheet.box.x, 0, TOL, `popover left at ${width} x ${height}`);
      expectNear(sheet.box.y + sheet.box.height, height, TOL, `popover bottom edge at ${width} x ${height}`);
      expect(sheet.box.height, `Peek is at least 168 px at ${width} x ${height}`).toBeGreaterThanOrEqual(168 - TOL);
      await expect(handle(page), `the handle exists at ${width} x ${height}: a bottom sheet, not a panel`).toBeVisible();
    }
  });

  // ---- AC-11 -------------------------------------------------------------------------------------------------------------------------------

  test('[AC-11] at 884 wide the sheet is a left panel: 400 wide at x = 16, top 76, 16 px above the nav, no handle, tabs and summary visible, map padding-left 432', { tag: ['@unfolded', '@unfolded-tall'] }, async ({ page }) => {
    await demo(page);
    const { height } = viewport(page);
    const sheet = await untilSettled(page, 'panel', 'on load');

    const panel = await boxOf(popover(page), 'div[mudsheet] (the panel)');
    expectNear(panel.width, 400, 5, 'panel width');
    expectNear(panel.x, 16, TOL, 'panel left');
    expectNear(panel.y, 76, TOL, 'panel top');
    expectNear(panel.y + panel.height, height - NAV_H - 16, TOL, `panel bottom edge (16 px above the nav, y = ${height - NAV_H - 16})`);
    expectNear(sheet.heightPx, panel.height, TOL, 'window.__realm.sheet().heightPx against the panel');

    await expect(handle(page), 'the panel has no sheet-handle').toHaveCount(0);
    await expect(page.getByTestId('sheet-summary'), 'the summary is visible in the panel').toHaveText('4 in the Realm · 1 driving');
    for (const id of ['tab-drivers', 'tab-vehicles', 'tab-places']) {
      await expect(page.getByTestId(id), `${id} is visible`).toBeVisible();
    }
    await expect(page.getByTestId('tab-drivers')).toHaveAttribute('aria-selected', 'true');

    const padding = await until('window.__realm.mapPadding() at the panel layout', () => readHook(page, 'mapPadding'), (p) => Math.abs(p.left - 432) <= 1);
    expectNear(padding.left, 432, 1, 'map padding-left (16 + 400 + 16)');
  });

  test('[AC-11] ?layout=sheet at 884 wide shows the bottom sheet', { tag: ['@unfolded'] }, async ({ page }) => {
    await demo(page, { layout: 'sheet' });
    const { width, height } = viewport(page);

    await untilSettled(page, 'peek', 'on load with layout=sheet');
    const outer = await boxOf(popover(page), 'div[mudsheet]');
    expectNear(outer.width, width, TOL, 'popover width at 884 with layout=sheet');
    expectNear(outer.y + outer.height, height, TOL, 'popover bottom edge');
    await expect(handle(page), 'the handle exists: a bottom sheet').toBeVisible();
  });

  test('[AC-11] ?layout=panel shows the panel also when the window is narrowed to 700 wide', { tag: ['@unfolded'] }, async ({ page }) => {
    await demo(page, { layout: 'panel' });
    await untilSettled(page, 'panel', 'on load with layout=panel');

    await page.setViewportSize({ width: 700, height: 900 });
    const panel = await until(
      'the panel at 700 x 900 with layout=panel: still 400 wide at x = 16',
      async () => ({ info: await readHook(page, 'sheet'), box: await boxOf(popover(page), 'div[mudsheet]') }),
      (observed) => observed.info.state === 'panel' && Math.abs(observed.box.width - 400) <= 5,
    );
    expectNear(panel.box.x, 16, TOL, 'panel left at 700 wide');
    await expect(handle(page), 'no handle in the panel').toHaveCount(0);
  });
});
