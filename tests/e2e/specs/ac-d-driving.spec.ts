// Acceptance tests D of 01 section 11 (03 section 7.5): the Weekly Driving Report. S11a writes AC-32 to AC-36 (the page, the week chips, the stat chips, the Top Speed
// and Drives cards); S11b adds AC-37 (it needs DriverWeekPage and its route) and AC-38 to AC-41 (the popups) to this file.
//
// The Demo app behind the ingress proxy, frozen at Wed 2026-09-30 21:25 CDT: This week is Sep 28 to Oct 4. Every figure is the one of 02 section 9.4 (01 Appendix A.4),
// and every name is read from tests/e2e/fixtures/demo-cast.json (loadDemoCast), never retyped. Geometry is read to within 2 px unless the AC gives its own tolerance.
//
// The page is prerendered, so for a moment after the load it is static HTML that nothing listens to. A click, a key press or a hover that needs the circuit is therefore
// repeated until it has had its effect (toPass); an action that was lost is harmless to repeat, and one that did land is idempotent (a chip chooses a week, it does not step).
import type { Locator, Page } from '@playwright/test';

import { INGRESS_PREFIX, castMember, demo, expect, expectApprox, expectRectApprox, loadDemoCast, test, type DemoOptions, type Rect } from '../fixtures.js';

const TOLERANCE_PX = 2;

/** The stat chips in card order (01 section 6.3). */
const CHIPS = ['speeding', 'phone', 'accel', 'braking'] as const;
type ChipKey = (typeof CHIPS)[number];

/** The driver cards in the order of 01 Appendix A.4: Alden, Cass, Dara, Briar. */
const DRIVER_IDS = ['king', 'jester', 'cryptid', 'queen'] as const;

/** The popover MudTooltip opens: `.mud-tooltip` is its own class and `.mud-popover-open` is what MudBlazor adds while it is shown. */
const TOOLTIP = '.mud-popover-open.mud-tooltip';

const DASH = '—';
const UNAVAILABLE_TIP = "The Realm hasn't recorded this yet";
const PHONE_TIP = 'Only 1 of 4 drivers shared this';

// ---- helpers --------------------------------------------------------------------------------------------------------------------------------

/** Opens `/driving` (never with a leading slash) and waits until the report is on screen: the chips only exist once it has loaded. */
async function openDriving(page: Page, options: Omit<DemoOptions, 'path'> = {}): Promise<void> {
  await demo(page, { ...options, path: 'driving' });
  await expect(page.getByTestId('stat-speeding'), 'the report has loaded').toBeVisible();
}

async function boxOf(locator: Locator, label: string): Promise<Rect> {
  await expect(locator, `${label} is rendered`).toBeVisible();
  const box = await locator.boundingBox();
  expect(box, `${label} has a bounding box`).not.toBeNull();
  return box as Rect;
}

/**
 * `^\s*<value>\s*<label>\s*$`: the number or dash of a chip, then its label. Playwright normalises whitespace only when it is given a string; a RegExp is matched against
 * the raw `textContent`, and the chip's text starts with the whitespace-only text node Razor keeps between the icon span and the text span ("\n        56\n   Speeding").
 * So the pattern allows whitespace at both ends as well as between the two; it still rejects any other text (a leaked icon name, a stray asterisk, a wrong number).
 */
function chipText(value: string, label: string): RegExp {
  return new RegExp(`^\\s*${value.replace(/[*.]/g, '\\$&')}\\s*${label.replace(/[*.]/g, '\\$&')}\\s*$`);
}

/** The week offset the URL asks for, or null when there is no `week` parameter (This week is the default and leaves none). */
function weekOf(page: Page): string | null {
  return new URL(page.url()).searchParams.get('week');
}

/** Chooses a week with the chip, repeating the click until the chip is the checked one (see the note at the top of the file). */
async function chooseWeek(page: Page, offset: number): Promise<void> {
  const chip = page.getByTestId(`week-chip-${offset}`);
  await expect(async () => {
    await chip.click();
    await expect(chip).toHaveAttribute('aria-checked', 'true', { timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
}

/** The computed `color` a token resolves to, as the browser writes it (`rgb(...)`), read through a probe element so the token's own notation does not matter. */
async function tokenColor(page: Page, token: string): Promise<string> {
  return page.evaluate((name) => {
    const probe = document.createElement('span');
    probe.style.color = `var(${name})`;
    document.body.appendChild(probe);
    const colour = getComputedStyle(probe).color;
    probe.remove();
    return colour;
  }, token);
}

const colorOf = (locator: Locator): Promise<string> => locator.evaluate((element) => getComputedStyle(element).color);

type Trend = 'up' | 'down';

/** The arrow of a chip must exist and carry the token's colour; the direction is the class the chip draws, and the colour is what the person sees. */
async function expectArrow(page: Page, key: ChipKey, direction: Trend): Promise<void> {
  const arrow = page.getByTestId(`trend-${key}`);
  await expect(arrow, `the ${key} chip has an arrow`).toBeVisible();
  await expect(arrow, `the ${key} arrow points ${direction}`).toHaveClass(new RegExp(`realm-stat-chip__trend--${direction}`));
  // More events is worse (01 section 6.3): up is the error colour, down the success colour.
  const token = direction === 'up' ? '--realm-error' : '--realm-success';
  expect(await colorOf(arrow), `the ${key} arrow ${direction}: computed colour is ${token}`).toBe(await tokenColor(page, token));
}

/** The chips that must carry no arrow at all. */
async function expectNoArrow(page: Page, ...keys: ChipKey[]): Promise<void> {
  for (const key of keys) await expect(page.getByTestId(`trend-${key}`), `the ${key} chip has no arrow`).toHaveCount(0);
}

/**
 * The text of the tooltip a chip shows on hover or on keyboard focus. The first hover or focus can land before the circuit has connected (nothing listens yet), so it is
 * repeated until exactly one tooltip is open; then the pointer leaves, or the focus moves on, and the tooltip is closed again for the next chip.
 */
async function tooltipOf(page: Page, trigger: Locator, how: 'hover' | 'focus'): Promise<string> {
  const open = page.locator(TOOLTIP);
  await expect(async () => {
    if (how === 'hover') await trigger.hover();
    else await trigger.focus();
    await expect(open).toHaveCount(1, { timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  const text = (await open.first().innerText()).trim();
  if (how === 'hover') await page.mouse.move(0, 0);
  else await trigger.evaluate((element) => (element as HTMLElement).blur());
  await expect(open, 'the tooltip closes again').toHaveCount(0);
  return text;
}

// ---- the tests ------------------------------------------------------------------------------------------------------------------------------

test.describe('acceptance D: driving', () => {
  test('[AC-32] /driving shows no map and no sheet, nav-driving is current, and the gear is at (12, 12)', async ({ page }) => {
    await openDriving(page);

    // The route is the ingress prefix plus `driving`; there is no map host and no sheet on it.
    expect(new URL(page.url()).pathname).toBe(`${INGRESS_PREFIX}/driving`);
    await expect(page.getByTestId('map-canvas'), 'no map on Driving (absent or not visible)').toBeHidden();
    await expect(page.getByTestId('sheet'), 'no sheet on Driving').toHaveCount(0);
    await expect(page.getByTestId('sheet-handle'), 'no sheet handle on Driving').toHaveCount(0);
    await expect(page.locator('canvas.maplibregl-canvas'), 'no MapLibre canvas on Driving').toHaveCount(0);

    // The bottom navigation marks Driving as the current page and Location as not.
    await expect(page.getByTestId('nav-driving')).toHaveAttribute('aria-current', 'page');
    await expect(page.getByTestId('nav-location')).not.toHaveAttribute('aria-current');

    // The gear is the shell's: 48 x 48 at (12, 12), as on Location (AC-03). That it opens Settings is not asserted here: the Settings dialog is S10's and this
    // slice's branch has none, so the click assertion belongs to the slice that adds the dialog.
    expectRectApprox(await boxOf(page.getByTestId('btn-settings'), 'btn-settings'), { x: 12, y: 12, width: 48, height: 48 }, TOLERANCE_PX, 'btn-settings');
  });

  test('[AC-33] four week chips with the exact text, This week selected and 48 px high, and the range line follows the choice', async ({ page }) => {
    await openDriving(page);

    // The four chips, in order, with the exact text. The separator of the date ranges is an en dash with spaces by design (R-005).
    const chips = page.locator('[data-testid^="week-chip-"]');
    await expect(chips).toHaveText(['This week', 'Last week', 'Sep 14 – Sep 20', 'Sep 7 – Sep 13']);
    await expect(chips).toHaveCount(4);
    await expect(page.getByTestId('week-chips'), 'the chips are one radio group').toHaveAttribute('role', 'radiogroup');

    // This week is the selected one, and every chip is 48 px high.
    await expect(page.getByTestId('week-chip-0')).toHaveAttribute('aria-checked', 'true');
    for (const offset of [1, 2, 3]) await expect(page.getByTestId(`week-chip-${offset}`)).toHaveAttribute('aria-checked', 'false');
    for (const offset of [0, 1, 2, 3]) {
      expectApprox((await boxOf(page.getByTestId(`week-chip-${offset}`), `week-chip-${offset}`)).height, 48, TOLERANCE_PX, `week-chip-${offset} height`);
    }

    // The range line of the current week reads "so far"; its values are the week 0 values of Appendix A.4.
    await expect(page.getByText('Sep 28 – Oct 4 · so far', { exact: true })).toBeVisible();
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('56', 'Speeding'));
    await expect(page.getByTestId('card-drives')).toHaveText(/^Drives\s*64\s*Total mi\s*781$/);
    expect(weekOf(page), 'This week leaves no week parameter').toBeNull();

    // Selecting Last week: the URL asks for week 1, the range line loses "so far", and the values are the week 1 values of Appendix A.4.
    await chooseWeek(page, 1);
    await expect(page).toHaveURL(/[?&]week=1(&|$)/);
    await expect(page.getByTestId('week-chip-0')).toHaveAttribute('aria-checked', 'false');
    await expect(page.getByText('Sep 21 – Sep 27', { exact: true })).toBeVisible();
    await expect(page.getByText('Sep 28 – Oct 4 · so far', { exact: true })).toHaveCount(0);
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('63', 'Speeding'));
    await expect(page.getByTestId('stat-phone')).toHaveText(chipText('64*', 'Phone use'));
    await expect(page.getByTestId('card-drives')).toHaveText(/^Drives\s*71\s*Total mi\s*843$/);
    await expect(page.getByTestId('card-topspeed')).toHaveText(/92 mph$/);

    // Going back to This week removes the parameter again.
    await chooseWeek(page, 0);
    await expect(page.getByText('Sep 28 – Oct 4 · so far', { exact: true })).toBeVisible();
    await expect.poll(() => weekOf(page), { message: 'This week leaves no week parameter' }).toBeNull();
  });

  test('[AC-33] the arrow keys move the week selection and the focus with it', async ({ page }) => {
    await openDriving(page);
    const first = page.getByTestId('week-chip-0');
    const second = page.getByTestId('week-chip-1');

    // Pressed on the first chip, ArrowRight always chooses the second: repeating it until the circuit has answered cannot overshoot.
    await expect(async () => {
      await first.focus();
      await page.keyboard.press('ArrowRight');
      await expect(second).toHaveAttribute('aria-checked', 'true', { timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    await expect(page).toHaveURL(/[?&]week=1(&|$)/);
    await expect(second, 'focus follows the selection').toBeFocused();
    await expect(second, 'the selected chip is the one tab stop').toHaveAttribute('tabindex', '0');
    await expect(first).toHaveAttribute('tabindex', '-1');
  });

  test('[AC-34] the title and the four stat chips of the default fixture, with arrow colours and tooltips', async ({ page }) => {
    await openDriving(page);

    // The title (H1) and the subtitle.
    const title = page.getByRole('heading', { level: 1 });
    await expect(title).toHaveText('Weekly Driving Report');
    // Typography is asserted by S10b (D77).
    await expect(page.getByText("The scribes' tally of the Realm's roads", { exact: true })).toBeVisible();

    // This week: Speeding 56 up, Phone use 60* down, accel and braking "—" with no arrow.
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('56', 'Speeding'));
    await expect(page.getByTestId('stat-phone')).toHaveText(chipText('60*', 'Phone use'));
    await expect(page.getByTestId('stat-accel')).toHaveText(chipText(DASH, 'Rapid accel.'));
    await expect(page.getByTestId('stat-braking')).toHaveText(chipText(DASH, 'Hard braking'));
    await expectArrow(page, 'speeding', 'up');
    await expectArrow(page, 'phone', 'down');
    await expectNoArrow(page, 'accel', 'braking');

    // The tooltips: the partial phone total, and the two types the Realm does not record yet (on hover, and on keyboard focus).
    expect(await tooltipOf(page, page.getByTestId('stat-phone'), 'hover'), 'Phone use tooltip').toBe(PHONE_TIP);
    expect(await tooltipOf(page, page.getByTestId('stat-accel'), 'hover'), 'Rapid accel. tooltip').toBe(UNAVAILABLE_TIP);
    expect(await tooltipOf(page, page.getByTestId('stat-braking'), 'focus'), 'Hard braking tooltip (keyboard focus)').toBe(UNAVAILABLE_TIP);

    // The accessible names (01 section 10.3).
    await expect(page.getByTestId('stat-speeding')).toHaveAttribute('aria-label', 'Speeding: 56 events this week, up 7 from last week, which is worse. Double tap for details.');
    await expect(page.getByTestId('stat-phone')).toHaveAttribute(
      'aria-label',
      'Phone use: 60 events this week, from 1 of 4 drivers, down 11 from last week, which is better. Double tap for details.',
    );
    await expect(page.getByTestId('stat-accel')).toHaveAttribute('aria-label', 'Rapid acceleration: not recorded yet. Double tap for details.');
  });

  test('[AC-34] last week and the oldest week of the default fixture', async ({ page }) => {
    // Last week: Speeding 63 up, Phone use 64* down, accel and braking "—".
    await openDriving(page, { week: 1 });
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('63', 'Speeding'));
    await expect(page.getByTestId('stat-phone')).toHaveText(chipText('64*', 'Phone use'));
    await expect(page.getByTestId('stat-accel')).toHaveText(chipText(DASH, 'Rapid accel.'));
    await expect(page.getByTestId('stat-braking')).toHaveText(chipText(DASH, 'Hard braking'));
    await expectArrow(page, 'speeding', 'up');
    await expectArrow(page, 'phone', 'down');
    await expectNoArrow(page, 'accel', 'braking');

    // The oldest week (Sep 7 – Sep 13): Speeding 44, Phone use 61*, and no arrow on any chip (there is no week before it to compare with).
    await openDriving(page, { week: 3 });
    await expect(page.getByTestId('week-chip-3')).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByText('Sep 7 – Sep 13', { exact: true }).last()).toBeVisible();
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('44', 'Speeding'));
    await expect(page.getByTestId('stat-phone')).toHaveText(chipText('61*', 'Phone use'));
    await expectNoArrow(page, ...CHIPS);
  });

  test('[AC-34] with variant all-sources every chip has a value and an arrow, and no asterisk', async ({ page }) => {
    await openDriving(page, { variant: 'all-sources' });

    // Speeding 56 up, Phone use 250 down, Rapid accel. 18 up, Hard braking 5 down; more events is worse, so up is red and down is green.
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('56', 'Speeding'));
    await expect(page.getByTestId('stat-phone')).toHaveText(chipText('250', 'Phone use'));
    await expect(page.getByTestId('stat-accel')).toHaveText(chipText('18', 'Rapid accel.'));
    await expect(page.getByTestId('stat-braking')).toHaveText(chipText('5', 'Hard braking'));
    await expectArrow(page, 'speeding', 'up');
    await expectArrow(page, 'phone', 'down');
    await expectArrow(page, 'accel', 'up');
    await expectArrow(page, 'braking', 'down');
    for (const key of CHIPS) await expect(page.getByTestId(`stat-${key}`), `the ${key} chip carries no asterisk`).not.toContainText('*');

    // Cass's pill is the sum over all four types.
    await expect(page.getByTestId('driver-pill-jester')).toHaveText('167 events');
  });

  test('[AC-35] the stat chips are 2 x 2 at 412 and one row of four at 884, the driver cards one per row', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openDriving(page);
    const viewport = page.viewportSize();
    expect(viewport, 'a viewport project').not.toBeNull();
    const width = viewport?.width ?? 412;

    const chips = new Map<ChipKey, Rect>();
    for (const key of CHIPS) chips.set(key, await boxOf(page.getByTestId(`stat-${key}`), `stat-${key}`));
    const [speeding, phone, accel, braking] = CHIPS.map((key) => chips.get(key) as Rect);

    // Every chip is 64 px high.
    for (const key of CHIPS) expectApprox((chips.get(key) as Rect).height, 64, TOLERANCE_PX, `stat-${key} height`);

    if (width < 720) {
      // 2 x 2 at 412: two rows of two, each chip 186 +- 4 wide with 8 px gaps.
      for (const key of CHIPS) expectApprox((chips.get(key) as Rect).width, 186, 4, `stat-${key} width`);
      expectApprox(speeding.y, phone.y, TOLERANCE_PX, 'speeding and phone share the first row');
      expectApprox(accel.y, braking.y, TOLERANCE_PX, 'accel and braking share the second row');
      expectApprox(accel.y - (speeding.y + speeding.height), 8, TOLERANCE_PX, 'gap between the rows');
      expectApprox(phone.x - (speeding.x + speeding.width), 8, TOLERANCE_PX, 'gap between the columns');
      expectApprox(accel.x, speeding.x, TOLERANCE_PX, 'accel is under speeding');
      expectApprox(braking.x, phone.x, TOLERANCE_PX, 'braking is under phone');
    } else {
      // One row of four at 884, inside a content column of at most 720 px that is centred.
      const rows = new Set([speeding, phone, accel, braking].map((box) => Math.round(box.y)));
      expect(rows.size, `the four chips share one row (tops ${[...rows].join(', ')})`).toBe(1);
      expect(speeding.x).toBeLessThan(phone.x);
      expect(phone.x).toBeLessThan(accel.x);
      expect(accel.x).toBeLessThan(braking.x);
      const left = speeding.x;
      const right = braking.x + braking.width;
      expect(right - left, `the chip row (${left} to ${right}) lies inside a column of at most 720`).toBeLessThanOrEqual(720 + TOLERANCE_PX);
      const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);
      expectApprox((left + right) / 2, clientWidth / 2, TOLERANCE_PX, 'the column is centred');
    }

    // The driver cards stay one per row at every width, in the order of the report (Alden, Cass, Dara, Briar), all of one width and one left edge.
    const cast = loadDemoCast();
    await expect(page.locator('[data-testid^="driver-card-"]')).toHaveCount(DRIVER_IDS.length);
    await expect(page.locator('[data-testid^="driver-card-"] .realm-driver-card__name')).toHaveText(DRIVER_IDS.map((id) => castMember(cast, id).name));
    const cards: Rect[] = [];
    for (const id of DRIVER_IDS) cards.push(await boxOf(page.getByTestId(`driver-card-${id}`), `driver-card-${id}`));
    cards.forEach((card, index) => {
      const first = cards[0] as Rect;
      expectApprox(card.x, first.x, TOLERANCE_PX, `driver card ${index} left edge`);
      expectApprox(card.width, first.width, TOLERANCE_PX, `driver card ${index} width`);
      if (index > 0) {
        const above = cards[index - 1] as Rect;
        expect.soft(card.y, `driver card ${index} sits below driver card ${index - 1}`).toBeGreaterThanOrEqual(above.y + above.height - TOLERANCE_PX);
      }
    });
    const chipRowWidth = braking.x + braking.width - speeding.x;
    expectApprox((cards[0] as Rect).width, chipRowWidth, TOLERANCE_PX, 'a driver card is as wide as the chip row (one card per row)');

    // The page never scrolls sideways (the week chips scroll inside their own row).
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'no horizontal page scroll').toBeLessThanOrEqual(0);
  });

  test('[AC-36] the Top Speed card shows the driver, "Top Speed" and "96 mph"; the Drives card shows 64 and 781', async ({ page }) => {
    await openDriving(page);
    const alden = castMember(loadDemoCast(), 'king');

    // Alden's avatar (the initial on his colour: the Demo has no photos), the label and the speed.
    const topSpeed = page.getByTestId('card-topspeed');
    await expect(topSpeed).toBeVisible();
    await expect(topSpeed.locator('.realm-avatar'), "Alden's avatar").toBeVisible();
    await expect(topSpeed.locator('.realm-avatar')).toHaveText(alden.name.slice(0, 1).toUpperCase());
    await expect(topSpeed).toContainText('Top Speed');
    await expect(topSpeed).toHaveText(new RegExp(`^${alden.name.slice(0, 1).toUpperCase()}\\s*Top Speed\\s*96 mph$`));
    await expect(topSpeed).toHaveAttribute('aria-label', `Top speed: 96 miles per hour, ${alden.name}. Double tap for details.`);

    // Drives 64 and Total mi 781, in miles: the metric rendering is v1.1.
    const drives = page.getByTestId('card-drives');
    await expect(drives).toHaveText(/^Drives\s*64\s*Total mi\s*781$/);
    await expect(drives).toHaveAttribute('aria-label', 'Drives: 64. Total miles: 781. Double tap for details.');
    await expect(page.getByTestId('card-topspeed')).not.toContainText('km');
    await expect(page.getByTestId('card-drives')).not.toContainText('km');

    // The two cards sit side by side under the chips.
    const topBox = await boxOf(topSpeed, 'card-topspeed');
    const drivesBox = await boxOf(drives, 'card-drives');
    expectApprox(topBox.y, drivesBox.y, TOLERANCE_PX, 'the two cards share a row');
    expect.soft(topBox.x + topBox.width, 'Top Speed is left of Drives').toBeLessThanOrEqual(drivesBox.x + TOLERANCE_PX);
    const chipsBottom = (await boxOf(page.getByTestId('stat-braking'), 'stat-braking')).y + 64;
    expect.soft(topBox.y, 'the cards are under the chips').toBeGreaterThanOrEqual(chipsBottom - TOLERANCE_PX);
  });
});
