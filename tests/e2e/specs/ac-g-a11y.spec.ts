// Acceptance tests AC-44 to AC-48 of 01 section 11 (03 section 7.5, 04 card S15a): accessibility. The contrast half of AC-44 is the xUnit test ContrastTests ([AC-44a]); this file is the browser half.
//   [AC-44b] axe-core (4.13.0, pinned) reports no serious or critical violation, `color-contrast` included, on the screens of the criterion, in the dark theme. Each scan's full result is
//            written to ci-out/axe/<project>/<scene>.json (the e2e job uploads ci-out whole).
//   [AC-45]  every visible interactive element is 48 x 48 or more and 8 px or more from its neighbours; no text under 12 px; no horizontal page scroll at 320 px and 200 % font size.
//   [AC-46]  the accessible names of 01 section 10.3 that the criterion quotes, and the roles of the landmarks.
//   [AC-47b] the Tab sequence of Location (01 section 10.2): attribution, bubbles, recenter, layers, map canvas, sheet handle, section tabs, content; pins are not tab stops. The focus flow is [AC-47a]
//            in ac-f-states.spec.ts and the trap of the sheet is [X-13] in appendix-c.spec.ts.
//   [AC-48]  prefers-reduced-motion: reduce: camera moves report 0, no ring pulse, the sheet's transitions are 0.01 ms or less, bars are at full width at once, dialogs have no enter animation.
// Reduced motion is on for the whole file: a popup that is still fading in would be scanned at half opacity, and the criterion's motion half needs it anyway.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { AxeBuilder } from '@axe-core/playwright';
import type { Locator, Page, TestInfo } from '@playwright/test';

import { castMember, castPlace, demo, expect, loadDemoCast, mapReady, readHook, settled, test } from '../fixtures.js';

test.use({ reducedMotion: 'reduce' });

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

// ---- the screens of AC-44 and AC-45 ---------------------------------------------------------------------------------------------------------------

interface Scene {
  /** File-name safe scene id. */
  id: string;
  /** What 01 section 11 calls it. */
  label: string;
  /** Puts the page in the state of the scene and waits until it shows. */
  open: (page: Page) => Promise<void>;
}

const dialog = (page: Page): Locator => page.getByRole('dialog');

/** Clicks the opener until the target shows (a tap before the circuit is interactive does nothing; once the dialog is open it is not clicked again). */
async function openFrom(page: Page, opener: Locator, target: Locator): Promise<void> {
  await expect(async () => {
    if ((await dialog(page).count()) === 0) await opener.click({ timeout: 2_000 });
    await expect(target).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 15_000 });
}

/** Location at 80 % with the given section showing. */
async function sheetAt80(page: Page, section: 'drivers' | 'vehicles' | 'places'): Promise<void> {
  await demo(page, { sheet: '80' });
  await mapReady(page);
  await page.getByTestId(`tab-${section}`).click();
  await expect(page.getByTestId(`tab-${section}`), `tab-${section} is selected`).toHaveAttribute('aria-selected', 'true');
}

const SCENES: readonly Scene[] = [
  {
    id: 'location-peek',
    label: 'Location at Peek',
    open: async (page) => {
      await demo(page);
      await mapReady(page);
      await expect(page.getByTestId('sheet-handle')).toBeVisible();
    },
  },
  {
    id: 'location-peek-member',
    label: 'Location at Peek with a member selected',
    open: async (page) => {
      await demo(page);
      await mapReady(page);
      await page.getByTestId('pin-member-jester').click();
      await expect(page.getByTestId('sheet-selection-header')).toBeVisible();
    },
  },
  {
    id: 'location-peek-vehicle',
    label: 'Location at Peek with the vehicle selected',
    open: async (page) => {
      await demo(page);
      await mapReady(page);
      await page.getByTestId('pin-vehicle-wagon').click();
      await expect(page.getByTestId('sheet-selection-header')).toBeVisible();
    },
  },
  {
    id: 'location-peek-place',
    label: 'Location at Peek with a place selected',
    open: async (page) => {
      await sheetAt80(page, 'places');
      await page.getByTestId('row-place-home').click();
      await expect(page.getByTestId('sheet-selection-header')).toBeVisible();
    },
  },
  { id: 'sheet-80-drivers', label: 'the 80 % state, Drivers', open: (page) => sheetAt80(page, 'drivers') },
  { id: 'sheet-80-vehicles', label: 'the 80 % state, Vehicles', open: (page) => sheetAt80(page, 'vehicles') },
  { id: 'sheet-80-places', label: 'the 80 % state, Places', open: (page) => sheetAt80(page, 'places') },
  {
    id: 'person-detail',
    label: 'person detail',
    open: async (page) => {
      await sheetAt80(page, 'drivers');
      await page.getByTestId('row-member-jester').click();
      await expect(page.getByTestId('sheet-selection-header')).toBeVisible();
      await page.getByTestId('sheet-handle').press('Enter');
      await expect(page.getByTestId('detail-back')).toBeVisible();
    },
  },
  {
    id: 'driving',
    label: 'Driving',
    open: async (page) => {
      await demo(page, { path: 'driving' });
      await expect(page.getByTestId('stat-speeding')).toBeVisible();
    },
  },
  {
    id: 'popup',
    label: 'a popup',
    open: async (page) => {
      await demo(page, { path: 'driving' });
      await openFrom(page, page.getByTestId('stat-speeding'), page.getByTestId('popup-speeding'));
    },
  },
  {
    id: 'settings',
    label: 'Settings',
    open: async (page) => {
      await demo(page);
      await mapReady(page);
      await openFrom(page, page.getByTestId('btn-settings'), page.getByTestId('settings-dialog'));
    },
  },
];

// ---- AC-44b: axe ----------------------------------------------------------------------------------------------------------------------------------

const AXE_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

function writeAxeJson(testInfo: TestInfo, scene: string, body: unknown): void {
  const file = path.join(repositoryRoot, 'ci-out', 'axe', testInfo.project.name, `${scene}.json`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(body, null, 2)}\n`);
}

test.describe('[AC-44b] axe-core finds no serious or critical violation (dark theme)', () => {
  for (const scene of SCENES) {
    test(`[AC-44b] ${scene.label}`, async ({ page }, testInfo) => {
      await scene.open(page);
      const results = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
      writeAxeJson(testInfo, scene.id, {
        scene: scene.id,
        axe: results.testEngine,
        url: results.url,
        violations: results.violations,
        incomplete: results.incomplete.map((rule) => rule.id),
      });
      expect(results.testEngine.version, 'axe-core is pinned at 4.13.0').toBe('4.13.0');
      const blocking = results.violations.filter((violation) => violation.impact === 'serious' || violation.impact === 'critical');
      const summary = blocking.map((violation) => `${violation.id} (${violation.impact}): ${violation.nodes.slice(0, 4).map((node) => node.target.join(' ')).join(' | ')}`);
      expect(blocking, `${scene.label}: serious or critical axe violations (color-contrast is among the rules run)\n${summary.join('\n')}`).toEqual([]);
    });
  }
});

// ---- AC-45: targets, gaps, text size, reflow ------------------------------------------------------------------------------------------------------

/** Tolerance for sub-pixel layout, in CSS px. */
const TOLERANCE = 0.5;

interface BoxReport {
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * The visible interactive elements of the page, as boxes. Visible means the element has a box and is what a pointer at its centre would hit (so a row scrolled out of the sheet, or an
 * element under another layer, is not counted). Not counted, by 01 section 10.1 and 10.2: pins and the map's own canvas (pins are `tabindex=-1`, their hit areas of 56 and 48 have their own
 * tests, AC-03 and AC-05), and the links inside the attribution text (inline text links are the criterion's one exception).
 */
async function interactiveBoxes(page: Page): Promise<BoxReport[]> {
  return page.evaluate(() => {
    const selector =
      'button, a[href], input, select, textarea, summary, [role="button"], [role="tab"], [role="radio"], [role="switch"], [role="checkbox"], [role="link"], [tabindex]:not([tabindex="-1"])';
    const skip = '.realm-pin, .maplibregl-marker, .maplibregl-canvas, .maplibregl-ctrl-attrib-inner, [data-testid="map-canvas"], .mud-overlay, [data-testid="sheet-announce"]';
    const found: Array<{ name: string; x: number; y: number; width: number; height: number }> = [];
    for (const element of document.querySelectorAll(selector)) {
      if (!(element instanceof HTMLElement) || element.matches(skip) || element.closest('.realm-pin, .maplibregl-ctrl-attrib-inner') !== null) continue;
      const style = getComputedStyle(element);
      if (style.visibility === 'hidden' || style.display === 'none' || style.display === 'inline') continue;
      const rect = element.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) continue;
      const cx = rect.left + rect.width / 2;
      const cy = rect.top + rect.height / 2;
      if (cx < 0 || cy < 0 || cx > window.innerWidth || cy > window.innerHeight) continue;
      const hit = document.elementFromPoint(cx, cy);
      if (hit === null || !(element.contains(hit) || hit.contains(element))) continue;
      const testId = element.getAttribute('data-testid');
      const firstClass = typeof element.className === 'string' ? element.className.split(' ')[0] : '';
      found.push({ name: testId ?? `${element.tagName.toLowerCase()}${firstClass ? `.${firstClass}` : ''}`, x: rect.x, y: rect.y, width: rect.width, height: rect.height });
    }
    return found;
  });
}

/** The distance between two boxes: 0 when they overlap or touch, otherwise the straight gap between their nearest edges. */
function distance(a: BoxReport, b: BoxReport): number {
  const dx = Math.max(a.x - (b.x + b.width), b.x - (a.x + a.width), 0);
  const dy = Math.max(a.y - (b.y + b.height), b.y - (a.y + a.height), 0);
  return Math.hypot(dx, dy);
}

const contains = (outer: BoxReport, inner: BoxReport): boolean =>
  outer.x <= inner.x + TOLERANCE &&
  outer.y <= inner.y + TOLERANCE &&
  outer.x + outer.width >= inner.x + inner.width - TOLERANCE &&
  outer.y + outer.height >= inner.y + inner.height - TOLERANCE;

/** List rows (72 px and more, 01 section 10.1) touch their neighbours by design: they are rows of one list, not neighbouring controls. */
const isListRow = (box: BoxReport): boolean => /^(row-|drive-row-)/.test(box.name);

test.describe('[AC-45] targets, gaps, text size and reflow', () => {
  for (const scene of SCENES) {
    test(`[AC-45] ${scene.label}: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px`, async ({ page }) => {
      await scene.open(page);
      const boxes = await interactiveBoxes(page);
      expect(boxes.length, `${scene.label}: the scan found interactive elements`).toBeGreaterThan(0);

      const small = boxes.filter((box) => box.width < 48 - TOLERANCE || box.height < 48 - TOLERANCE).map((box) => `${box.name} ${box.width.toFixed(1)} x ${box.height.toFixed(1)}`);
      expect(small, `${scene.label}: elements under 48 x 48`).toEqual([]);

      const crowded: string[] = [];
      for (let i = 0; i < boxes.length; i += 1) {
        for (let j = i + 1; j < boxes.length; j += 1) {
          const a = boxes[i]!;
          const b = boxes[j]!;
          if (isListRow(a) || isListRow(b) || contains(a, b) || contains(b, a)) continue;
          const gap = distance(a, b);
          if (gap < 8 - TOLERANCE) crowded.push(`${a.name} and ${b.name}: ${gap.toFixed(1)} px`);
        }
      }
      expect(crowded, `${scene.label}: neighbours closer than 8 px`).toEqual([]);

      const tiny = await page.evaluate(() => {
        const found: string[] = [];
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
          const parent = node.parentElement;
          const text = (node.textContent ?? '').trim();
          if (parent === null || text === '' || parent.closest('script, style, canvas, [aria-hidden="true"]') !== null) continue;
          const style = getComputedStyle(parent);
          if (style.display === 'none' || style.visibility === 'hidden') continue;
          const rect = parent.getBoundingClientRect();
          if (rect.width === 0 || rect.height === 0) continue;
          if (parseFloat(style.fontSize) < 12) found.push(`"${text.slice(0, 30)}" ${style.fontSize} in ${parent.getAttribute('data-testid') ?? parent.tagName.toLowerCase()}`);
        }
        return found;
      });
      expect(tiny, `${scene.label}: text under 12 px`).toEqual([]);
    });
  }

  // At 320 px and 200 % text there is no horizontal page scroll. The 200 % is the root font size doubled (everything is rem based), set after the page has loaded at 320 px.
  // The Location scenes open without waiting for the map's pins and `settled()`: at 320 x 640 the default view's pins are not all on screen and the software-rendered map may still be drawing, and
  // the criterion is about the page's layout, not the map.
  const reflowScenes: Scene[] = [
    { id: 'location-peek', label: 'Location at Peek', open: async (page) => { await demo(page, { hooks: false }); await expect(page.getByTestId('sheet-handle')).toBeVisible(); } },
    {
      id: 'sheet-80-drivers',
      label: 'the 80 % state, Drivers',
      open: async (page) => {
        await demo(page, { sheet: '80', hooks: false });
        await expect(page.getByTestId('tab-drivers')).toBeVisible();
      },
    },
    ...SCENES.filter((candidate) => ['driving', 'popup'].includes(candidate.id)),
    {
      id: 'settings',
      label: 'Settings',
      open: async (page) => {
        await demo(page, { hooks: false });
        await openFrom(page, page.getByTestId('btn-settings'), page.getByTestId('settings-dialog'));
      },
    },
  ];
  for (const scene of reflowScenes) {
    test(`[AC-45] ${scene.label}: no horizontal scroll at 320 px and 200 % text`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 640 });
      await scene.open(page);
      await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
      await page.evaluate(() => document.fonts.ready.then(() => undefined));
      const overflow = await page.evaluate(() => ({
        page: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        body: document.body.scrollWidth - document.documentElement.clientWidth,
        dialog: Math.max(0, ...[...document.querySelectorAll('[role="dialog"]')].map((element) => element.scrollWidth - element.clientWidth)),
      }));
      expect(overflow.page, `${scene.label}: the page scrolls ${overflow.page} px sideways`).toBeLessThanOrEqual(0);
      expect(overflow.body, `${scene.label}: the body is ${overflow.body} px wider than the window`).toBeLessThanOrEqual(0);
      expect(overflow.dialog, `${scene.label}: a dialog scrolls ${overflow.dialog} px sideways`).toBeLessThanOrEqual(0);
    });
  }
});

// ---- AC-46: accessible names ----------------------------------------------------------------------------------------------------------------------

test.describe('[AC-46] accessible names of 01 section 10.3', () => {
  test('[AC-46] the pin, the bubble and the landmarks of Location carry their exact names and roles', async ({ page }) => {
    const cast = loadDemoCast();
    const jester = castMember(cast, 'jester');
    const hall = castPlace(cast, 'jester_hall');
    await demo(page);
    await mapReady(page);

    await expect(page.getByTestId('pin-member-jester'), 'member pin').toHaveAccessibleName(`${jester.name}, ${jester.lore}. At ${hall.name} since 9:06 pm. Battery 12 percent, low. 1.0 mile away.`);
    await expect(page.getByTestId('bubble-cryptid'), 'edge bubble').toHaveAccessibleName('Dara, 155 miles east, off screen. Double tap to include on the map.');
    await expect(page.getByTestId('map-canvas'), 'map container').toHaveAccessibleName('Map of the Realm. Use the list for details.');
    await expect(page.getByTestId('map-canvas')).toHaveAttribute('role', 'application');
    await expect(page.getByTestId('sheet'), 'sheet').toHaveAccessibleName('Realm list');
    await expect(page.getByTestId('sheet')).toHaveAttribute('role', 'region');
    await expect(page.getByTestId('sheet-handle'), 'handle at Peek').toHaveAccessibleName('Resize list. Currently peek height.');
    await expect(page.getByTestId('sheet-segments'), 'section tabs').toHaveAttribute('role', 'tablist');
    await expect(page.getByRole('tab'), 'three section tabs').toHaveCount(3);
    await expect(page.getByRole('navigation', { name: 'Main' }), 'bottom nav').toBeVisible();
    await expect(page.getByTestId('nav-location'), 'the current page').toHaveAttribute('aria-current', 'page');
  });

  test('[AC-46] the Driving stat chips are named as the criterion quotes them', async ({ page }) => {
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('stat-speeding')).toHaveAccessibleName('Speeding: 56 events this week, up 7 from last week, which is worse. Double tap for details.');
    await expect(page.getByTestId('stat-phone')).toHaveAccessibleName('Phone use: 60 events this week, from 1 of 4 drivers, down 11 from last week, which is better. Double tap for details.');
    await expect(page.getByTestId('stat-accel')).toHaveAccessibleName('Rapid acceleration: not recorded yet. Double tap for details.');
    await expect(page.getByTestId('nav-driving'), 'the current page').toHaveAttribute('aria-current', 'page');
  });
});

// ---- AC-47b: the Tab sequence ---------------------------------------------------------------------------------------------------------------------

/** A name for the focused element: its test id, or `canvas` for the map's canvas, which has none. */
async function focusedStop(page: Page): Promise<string> {
  return page.evaluate(() => {
    const element = document.activeElement;
    if (element === null || element === document.body) return 'body';
    if (element.classList.contains('maplibregl-canvas')) return 'canvas';
    const own = element.getAttribute('data-testid');
    if (own !== null) return own;
    const ancestor = element.closest('[data-testid]');
    return `${element.tagName.toLowerCase()} in ${ancestor?.getAttribute('data-testid') ?? 'page'}`;
  });
}

test.describe('[AC-47b] the Tab sequence of Location', () => {
  test('[AC-47b] Tab visits the attribution, the bubbles, recenter, layers, the map canvas, the sheet handle, the section tabs and then the content, and never a pin', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    await expect(page.getByTestId('bubble-cryptid'), 'the default view has an off-screen member, so there is a bubble to tab to').toBeVisible();

    // Start from the top of the document, as a keyboard user does after the page loads.
    await page.evaluate(() => {
      (document.activeElement as HTMLElement | null)?.blur();
      window.scrollTo(0, 0);
    });
    const visited: string[] = [];
    const MAX_TABS = 14;
    let pressesAfterTabs = -1;
    for (let press = 0; press < MAX_TABS; press += 1) {
      await page.keyboard.press('Tab');
      const stop = await focusedStop(page);
      visited.push(stop);
      if (stop.startsWith('tab-') && pressesAfterTabs < 0) pressesAfterTabs = 0;
      else if (pressesAfterTabs >= 0) pressesAfterTabs += 1;
      if (pressesAfterTabs >= 1) break; // one stop after the section tabs: the content
    }
    const sequence = visited.join(' > ');
    test.info().annotations.push({ type: 'info', description: `[AC-47b] Tab order: ${sequence}` });

    expect(
      visited.filter((stop) => stop.startsWith('pin-')),
      `pins are not tab stops (${sequence})`,
    ).toEqual([]);

    // The order of 01 section 10.2, as indexes into what was visited: each group comes after the one before it.
    const at = (match: (stop: string) => boolean): number[] => visited.flatMap((stop, index) => (match(stop) ? [index] : []));
    const chain: Array<[string, number[]]> = [
      ['the attribution', at((stop) => stop === 'map-attribution')],
      ['the bubbles', at((stop) => stop.startsWith('bubble-'))],
      ['recenter', at((stop) => stop === 'btn-recenter')],
      ['layers', at((stop) => stop === 'btn-layers')],
      ['the map canvas', at((stop) => stop === 'canvas')],
      ['the sheet handle', at((stop) => stop === 'sheet-handle')],
      ['the section tabs', at((stop) => stop.startsWith('tab-'))],
    ];
    // The sheet's focus trap (R-032, [X-13]) is nondeterministic about where the first Tab into the sheet lands: usually the handle, sometimes straight on the section tab
    // (the trap's bumper div comes first and redirects). The handle is therefore not required here; when it is visited its place in the order is still checked.
    const optional = new Set(['the sheet handle']);
    for (const [name, found] of chain) {
      if (optional.has(name) && found.length === 0) test.info().annotations.push({ type: 'info', description: `[AC-47b] the sheet handle was skipped by the focus trap (${sequence})` });
      else expect(found.length, `Tab reaches ${name} (${sequence})`).toBeGreaterThan(0);
    }
    expect(chain[0]![1][0], `the attribution is the first stop (${sequence})`).toBe(0);
    for (let i = 1; i < chain.length; i += 1) {
      const [beforeName, before] = chain[i - 1]!;
      const [afterName, after] = chain[i]!;
      if (before.length === 0 || after.length === 0) continue; // only the optional handle can be empty here (checked above)
      // First visits are compared: the sheet traps Tab (R-032, [X-13]), so after the section tab the cycle wraps back to the handle, a second visit that says nothing about the order.
      expect(Math.min(...after), `${afterName} comes after ${beforeName} (${sequence})`).toBeGreaterThan(Math.min(...before));
    }
    const bubbles = chain[1]![1];
    expect(Math.max(...bubbles) - Math.min(...bubbles), `the bubbles are consecutive stops (${sequence})`).toBe(bubbles.length - 1);

    // D105: the nav is the last group, in the order of the DOM: Location, Driving, Settings, after the map canvas and the sheet. Settings is the nav's last item, a button.
    const order = await page.evaluate(() => {
      const index = (testId: string) => {
        const element = document.querySelector(`[data-testid="${testId}"]`);
        return element === null ? null : Array.from(document.querySelectorAll('*')).indexOf(element);
      };
      return { canvas: index('map-canvas'), sheet: index('sheet'), location: index('nav-location'), driving: index('nav-driving'), settings: index('btn-settings') };
    });
    expect(order.location, 'the nav is in the DOM').not.toBeNull();
    expect(order.canvas! < order.location! && order.sheet! < order.location!, `the nav follows the map canvas and the sheet (${JSON.stringify(order)})`).toBe(true);
    expect(order.location! < order.driving! && order.driving! < order.settings!, `Location, Driving, Settings (${JSON.stringify(order)})`).toBe(true);
    await page.getByTestId('nav-driving').focus();
    await page.keyboard.press('Tab');
    expect(await focusedStop(page), 'Tab from Driving reaches Settings').toBe('btn-settings');
  });
});

// ---- AC-48: reduced motion ------------------------------------------------------------------------------------------------------------------------

/** The longest duration, in ms, of a computed `transition-duration` or `animation-duration` list ("0.00001s, 1e-5s"). */
const longestMs = (value: string): number => Math.max(0, ...value.split(',').map((part) => (part.trim().endsWith('ms') ? parseFloat(part) : parseFloat(part) * 1000)));

test.describe('[AC-48] prefers-reduced-motion: reduce', () => {
  test('[AC-48] the emulation is on, camera moves report duration 0, no pin pulses and the sheet snaps in 0.01 ms or less', async ({ page }) => {
    await demo(page, { sheet: '80' });
    await mapReady(page);
    expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches), 'the emulation is on').toBe(true);

    // A selection flies the camera to the member: under reduced motion the move is a jump.
    await page.getByTestId('row-member-jester').click();
    await expect(page.getByTestId('sheet-selection-header')).toBeVisible();
    await settled(page);
    const camera = await readHook(page, 'camera');
    expect(camera.lastDurationMs, 'the last camera move had duration 0').toBe(0);
    expect(camera.animated, 'the camera is not animated').toBe(false);

    // Ring pulse: none runs (the element is not drawn and has no animation).
    const pulses = await page.evaluate(() =>
      [...document.querySelectorAll('.realm-pin__pulse')].map((element) => ({ display: getComputedStyle(element).display, animation: getComputedStyle(element).animationName })),
    );
    for (const pulse of pulses) {
      expect(pulse.display === 'none' || pulse.animation === 'none', `a ring pulse is neither hidden nor stopped (${JSON.stringify(pulse)})`).toBe(true);
    }

    // The sheet's snap transition.
    const sheet = await page.evaluate(() => {
      const style = getComputedStyle(document.querySelector('[data-testid="sheet"]')!);
      return { transition: style.transitionDuration, animation: style.animationDuration };
    });
    expect(longestMs(sheet.transition), `the sheet's transition-duration is ${sheet.transition}`).toBeLessThanOrEqual(0.01);
    expect(longestMs(sheet.animation), `the sheet's animation-duration is ${sheet.animation}`).toBeLessThanOrEqual(0.01);
  });

  test('[AC-48] a popup has no enter animation and its bars are at full width at once', async ({ page }) => {
    await demo(page, { path: 'driving' });
    await openFrom(page, page.getByTestId('stat-speeding'), page.getByTestId('popup-speeding'));
    const popup = dialog(page);
    const first = await popup.locator('.realm-bar__fill').evaluateAll((fills) => fills.map((fill) => ({ width: fill.getBoundingClientRect().width, animation: getComputedStyle(fill).animationName })));
    expect(first.length, 'the popup has bars').toBeGreaterThan(0);
    for (const bar of first) expect(bar.animation, 'a bar has no grow animation').toBe('none');

    const dialogAnimation = await popup.evaluate((element) => getComputedStyle(element).animationName);
    expect(dialogAnimation, 'the popup has no enter animation').toBe('none');

    // The widths read at once are the final ones: a bar that grew over the next 600 ms (the full-motion grow takes 400) would differ.
    await page.waitForTimeout(600);
    const later = await popup.locator('.realm-bar__fill').evaluateAll((fills) => fills.map((fill) => fill.getBoundingClientRect().width));
    expect(first.map((bar) => Math.round(bar.width)), 'the bars did not grow after the first read').toEqual(later.map((width) => Math.round(width)));
  });

  test('[AC-48] the Settings dialog has no enter animation', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    await openFrom(page, page.getByTestId('btn-settings'), page.getByTestId('settings-dialog'));
    const animation = await page.getByTestId('settings-dialog').evaluate((element) => getComputedStyle(element.closest('.mud-dialog') ?? element).animationName);
    expect(animation, 'Settings has no enter animation').toBe('none');
  });
});
