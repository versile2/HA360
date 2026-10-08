// Acceptance tests D of 01 section 11 (03 section 7.5): the Weekly Driving Report. S11a writes AC-32 to AC-36 (the page, the week chips, the stat chips, the Top Speed
// and Drives cards); S11b adds AC-37 (it needs DriverWeekPage and its route), AC-38 to AC-40 (the popups) and AC-41 (the driver week) in the second block of this file.
//
// The Demo app behind the ingress proxy, frozen at Wed 2026-09-30 21:25 CDT: This week is Sep 28 to Oct 4. Every figure is the one of 02 section 9.4 (01 Appendix A.4),
// and every name is read from tests/e2e/fixtures/demo-cast.json (loadDemoCast), never retyped. Geometry is read to within 2 px unless the AC gives its own tolerance.
//
// The page is prerendered, so for a moment after the load it is static HTML that nothing listens to. A click, a key press or a hover that needs the circuit is therefore
// repeated until it has had its effect (toPass); an action that was lost is harmless to repeat, and one that did land is idempotent (a chip chooses a week, it does not step).
import type { Locator, Page } from '@playwright/test';

import { INGRESS_PREFIX, castMember, demo, expect, expectApprox, expectHistoryDepth, expectRectApprox, loadDemoCast, test, type DemoOptions, type Rect } from '../fixtures.js';

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
  test('[AC-32] /driving shows no map and no sheet, nav-driving is current, and Settings is the third item of the nav, not a floating gear', async ({ page }) => {
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

    // Settings is the shell's third nav item (D105), as on Location (AC-03): it sits in the nav, right of Driving, and nothing floats at the top left over the page.
    const settings = await boxOf(page.getByTestId('btn-settings'), 'btn-settings');
    const driving = await boxOf(page.getByTestId('nav-driving'), 'nav-driving');
    expect(settings.x, 'btn-settings is right of nav-driving').toBeGreaterThanOrEqual(driving.x + driving.width + 8 - TOLERANCE_PX);
    expect(Math.abs(settings.y - driving.y), 'btn-settings is on the row of the nav').toBeLessThanOrEqual(TOLERANCE_PX);
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

    // The title (H1, Cinzel) and the subtitle.
    const title = page.getByRole('heading', { level: 1 });
    await expect(title).toHaveText('Weekly Driving Report');
    expect(await title.evaluate((element) => getComputedStyle(element).fontFamily), 'the H1 is set in Cinzel').toMatch(/^"?Cinzel"?\s*(,|$)/);
    // The wordmark above the title is display text too (01 section 7.4, D77).
    expect(await page.locator('.realm-driving__wordmark').evaluate((element) => getComputedStyle(element).fontFamily), 'the wordmark is set in Cinzel').toMatch(/^"?Cinzel"?\s*(,|$)/);
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

// ---- S11b: the popups and the driver week ---------------------------------------------------------------------------------------------------------

/** What the popup of one of the six items says for the default fixture (01 section 6.7 and Appendix A.4), and the bars in the order of 6.7.1. */
interface PopupSpec {
  key: string;
  opener: string;
  title: string;
  lore: string;
  summary: string;
  bars: readonly string[];
}

const UNAVAILABLE_SENTENCE = `${UNAVAILABLE_TIP}.`;
const ANDROID_AUTO_TIP = "Screen time while Android Auto is connected isn't counted, so navigation doesn't count as phone use.";
const SOURCE_FOOTNOTE = /^\s*Source: The Realm's own records/;

/** The bars of every popup that has no value of its own to sort by: card order (drives, then name). */
const CARD_ORDER = ['king', 'jester', 'cryptid', 'queen'] as const;

function popupSpecs(): PopupSpec[] {
  const alden = castMember(loadDemoCast(), 'king').name;
  return [
    { key: 'speeding', opener: 'stat-speeding', title: 'Speeding', lore: 'Heralds of haste', summary: '56 speeding events this week, 7 more than last week.', bars: ['jester', 'cryptid', 'king', 'queen'] },
    {
      key: 'phone',
      opener: 'stat-phone',
      title: 'Phone use',
      lore: 'Eyes on the road, good sirs',
      summary: '60 phone-use events this week from the 1 driver tracked, 11 fewer than last week.',
      bars: CARD_ORDER,
    },
    { key: 'accel', opener: 'stat-accel', title: 'Rapid acceleration', lore: 'The sudden gallop', summary: UNAVAILABLE_SENTENCE, bars: CARD_ORDER },
    { key: 'braking', opener: 'stat-braking', title: 'Hard braking', lore: 'Whoa, steed!', summary: UNAVAILABLE_SENTENCE, bars: CARD_ORDER },
    { key: 'topspeed', opener: 'card-topspeed', title: 'Top Speed', lore: 'The fastest charge', summary: `${alden} hit 96 mph on Tue, Sep 29.`, bars: CARD_ORDER },
    { key: 'drives', opener: 'card-drives', title: 'Total Drives', lore: 'Leagues travelled', summary: '64 drives, 781 miles on the road.', bars: CARD_ORDER },
  ];
}

/**
 * Waits until the circuit answers. The page is prerendered, so for a moment nothing listens to a hover, a click or a key; the first hover that opens a tooltip proves the
 * circuit is there, and it is idempotent. A driver card is a link, so it must not be tapped before this: a tap on static HTML would load a page of its own, without the
 * Demo parameters of the circuit.
 */
async function circuitReady(page: Page): Promise<void> {
  await tooltipOf(page, page.getByTestId('stat-accel'), 'hover');
}

/** The dialog that is open (MudBlazor gives it `role="dialog"`, named by its title through `aria-labelledby`). */
const dialogOf = (page: Page): Locator => page.getByRole('dialog');

/**
 * Opens a popup with its opener and returns the dialog. A tap before the circuit is up does nothing, so it is repeated until the popup is there; once it is open (it may
 * open after a tap the loop already gave up on) the loop never taps again, because the scrim would be in the way.
 */
async function openPopup(page: Page, opener: Locator, key: string): Promise<Locator> {
  const dialog = dialogOf(page);
  await expect(async () => {
    if ((await dialog.count()) === 0) await opener.click({ timeout: 3_000 });
    await expect(dialog.getByTestId(`popup-${key}`), `the ${key} popup is open`).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  return dialog;
}

type Closer = 'got-it' | 'close' | 'escape';

/** Closes the open popup the way the person does, and waits until it is gone. Esc is heard from inside the dialog, so the title's focus is checked first. */
async function closePopup(page: Page, dialog: Locator, how: Closer): Promise<void> {
  if (how === 'got-it') await dialog.getByTestId('popup-gotit').click();
  else if (how === 'close') await dialog.getByTestId('popup-close').click();
  else {
    await expect(dialog.locator('.realm-popup__title'), 'the title has the focus, inside the dialog, so Esc is heard').toBeFocused();
    await page.keyboard.press('Escape');
  }
  await expect(dialogOf(page), `the popup is closed by ${how}`).toHaveCount(0);
}

/** The test ids of the bars in the order they are drawn. */
const barIds = (dialog: Locator): Promise<string[]> => dialog.locator('.realm-bar').evaluateAll((bars) => bars.map((bar) => bar.getAttribute('data-testid') ?? ''));

interface BarGeometry {
  track: Rect;
  fill: Rect;
  avatar: Rect;
  pill: Rect;
  value: Rect;
}

async function barGeometry(dialog: Locator, memberId: string): Promise<BarGeometry> {
  const row = dialog.getByTestId(`bar-${memberId}`);
  return {
    track: await boxOf(row, `bar-${memberId}`),
    fill: await boxOf(row.locator('.realm-bar__fill'), `bar-${memberId} fill`),
    avatar: await boxOf(row.locator('.realm-avatar'), `bar-${memberId} avatar`),
    pill: await boxOf(row.locator('.realm-bar__pill'), `bar-${memberId} pill`),
    value: await boxOf(row.locator('.realm-bar__value'), `bar-${memberId} value`),
  };
}

/** True when the text of a pill is inside the pill: nothing overflows it (a long value such as "366.0 mi" widens the pill past its minimum instead of spilling out). */
async function pillTextFits(dialog: Locator, memberId: string): Promise<{ fits: boolean; scroll: number; client: number }> {
  return dialog
    .getByTestId(`bar-${memberId}`)
    .locator('.realm-bar__pill')
    .evaluate((pill) => ({ fits: pill.scrollWidth <= pill.clientWidth + 1, scroll: pill.scrollWidth, client: pill.clientWidth }));
}

/**
 * 01 section 6.7.1 and AC-39: rows of 56 px; the pill is "min-width 56 (72 for miles and mph values)", a MINIMUM: it grows with its text (the "366.0 mi" pill is wider than 72 px),
 * so what is asserted is that it is at least `pillMin` wide, that its text fits inside it and that it is no wider than that text needs; the fill is value over the largest value
 * of the room left of the pill as it is drawn (track minus pill minus 8 px, so the pill's own width), never under 56 px, so the largest bar is at 100 % of that room and every
 * other one is within 2 % of its proportion of it; the avatar sits at the right end of its fill, inset 8 px, and the fill ends before its pill.
 */
async function expectBars(dialog: Locator, rows: ReadonlyArray<{ id: string; value: number }>, pillMin: 56 | 72): Promise<void> {
  const largest = Math.max(...rows.map((row) => row.value));
  const largestId = (rows.find((row) => row.value === largest) as { id: string }).id;
  const geometry = new Map<string, BarGeometry>();
  for (const row of rows) geometry.set(row.id, await barGeometry(dialog, row.id));
  const widest = (geometry.get(largestId) as BarGeometry).fill.width;
  for (const row of rows) {
    const { track, fill, avatar, pill, value } = geometry.get(row.id) as BarGeometry;
    const room = track.width - pill.width - 8;
    expectApprox(track.height, 56, TOLERANCE_PX, `${row.id} row height`);
    expect.soft(pill.width, `${row.id} pill is at least ${pillMin} px wide (min-width)`).toBeGreaterThanOrEqual(pillMin - 0.5);
    expect.soft(pill.width, `${row.id} pill is no wider than its text needs, or ${pillMin} px`).toBeLessThanOrEqual(Math.max(pillMin, value.width + 16) + TOLERANCE_PX);
    const { fits, scroll, client } = await pillTextFits(dialog, row.id);
    expect.soft(fits, `${row.id} pill text fits (scrollWidth ${scroll}, clientWidth ${client})`).toBe(true);
    expectApprox(fill.width, Math.max(56, (row.value / largest) * room), Math.max(TOLERANCE_PX, 0.02 * room), `${row.id} fill width`);
    expect.soft(fill.width, `${row.id} fill is never under 56 px`).toBeGreaterThanOrEqual(56 - 1);
    if ((row.value / largest) * room > 56) expect.soft(Math.abs(fill.width / widest - row.value / largest), `${row.id} fill is proportional to its value (within 2 %)`).toBeLessThanOrEqual(0.02);
    expectApprox(fill.x + fill.width - (avatar.x + avatar.width), 8, TOLERANCE_PX, `${row.id} avatar sits at the right end of its fill, inset 8 px`);
    expectApprox(avatar.width, 40, TOLERANCE_PX, `${row.id} avatar diameter`);
    expect.soft(fill.x + fill.width, `${row.id} fill ends before its pill`).toBeLessThanOrEqual(pill.x + TOLERANCE_PX);
  }
  const top = geometry.get(largestId) as BarGeometry;
  expectApprox(widest, top.track.width - top.pill.width - 8, TOLERANCE_PX, 'the largest bar takes all the room left of its pill (100 %)');
}

/** The test ids and texts of the pills, in the order drawn. */
const pillTexts = (dialog: Locator): Promise<string[]> => dialog.locator('.realm-bar__value').allTextContents();

test.describe('acceptance D: driving popups and the driver week', () => {
  // The popups grow and fade in (150 ms) and the bars grow (400 ms); with reduced motion both are instant, so a measurement is the final one.
  test.use({ reducedMotion: 'reduce' });

  // ---- AC-37 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-37] the driver cards read as in the fixture, and tapping Cass opens driving/jester with Back returning to driving', async ({ page }) => {
    await openDriving(page);
    const cast = loadDemoCast();

    // Order Alden, Cass, Dara, Briar; Cass's line and pill; Alden's pill; a chevron on every card and no lock.
    await expect(page.locator('[data-testid^="driver-card-"] .realm-driver-card__name')).toHaveText(DRIVER_IDS.map((id) => castMember(cast, id).name));
    const cass = page.getByTestId('driver-card-jester');
    await expect(cass.locator('.realm-driver-card__line')).toHaveText('18 drives • 202.6 miles');
    await expect(page.getByTestId('driver-pill-jester')).toHaveText('38 speeding events');
    await expect(page.getByTestId('driver-pill-king')).toHaveText('6 speeding · 60 phone');
    await expect(page.getByTestId('driver-pill-king'), "Alden's pill has no asterisk").not.toContainText('*');
    for (const id of DRIVER_IDS) {
      const card = page.getByTestId(`driver-card-${id}`);
      await expect(card.locator('.realm-driver-card__chevron'), `${id}: a chevron`).toBeVisible();
      await expect(card.locator('[class*="lock" i], [data-testid*="lock" i], [aria-label*="lock" i]'), `${id}: no lock icon`).toHaveCount(0);
    }
    await expect(cass).toHaveAttribute('href', 'driving/jester?week=0');

    // Tapping the card opens Cass's week (the circuit is up, so the tap is an in-app navigation and the Demo session stays).
    await circuitReady(page);
    await cass.click();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving/jester`);
    expect(weekOf(page), 'the card carries the week').toBe('0');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(castMember(cast, 'jester').name);
    await expect(page.getByTestId('drive-row-0'), "Cass's drives are listed").toBeVisible();

    // The Back link returns to driving with This week still selected.
    await page.getByTestId('detail-back').click();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving`);
    expect(weekOf(page), 'Back carries the week').toBe('0');
    await expect(page.getByTestId('week-chip-0'), 'This week is still selected').toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('stat-speeding')).toBeVisible();

    // So does the browser's Back, after another visit.
    await page.getByTestId('driver-card-jester').click();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving/jester`);
    // The URL changes at the tap (the client intercepts the link and pushes the entry); the circuit renders the page a moment later. A browser Back taken before that render is
    // overtaken by the circuit's own location update and leaves the URL on jester (the CI flake), so the week page is waited for, as after the first visit.
    await expect(page.getByTestId('drive-row-0'), "Cass's drives are listed before the browser's Back").toBeVisible();
    await page.goBack();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving`);
    await expect(page.getByTestId('week-chip-0'), 'This week is still selected').toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('stat-speeding')).toBeVisible();
  });

  test('[AC-37] the week survives the round trip: Last week, Cass, Back', async ({ page }) => {
    await openDriving(page, { week: 1 });
    await circuitReady(page);

    await page.getByTestId('driver-card-jester').click();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving/jester`);
    expect(weekOf(page), 'the card carries Last week').toBe('1');
    await expect(page.locator('.realm-driver-week__week')).toHaveText('Last week · Sep 21 – Sep 27');

    await page.getByTestId('detail-back').click();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving`);
    expect(weekOf(page)).toBe('1');
    await expect(page.getByTestId('week-chip-1'), 'Last week is still selected').toHaveAttribute('aria-checked', 'true');
  });

  test('[AC-37] a static member and an unknown id return to driving, replacing the history entry', async ({ page }) => {
    await openDriving(page);
    await circuitReady(page);
    const navigateTo = (uri: string): Promise<void> =>
      page.evaluate((target) => (window as unknown as { Blazor: { navigateTo(uri: string): void } }).Blazor.navigateTo(target), uri);

    for (const id of ['prince', 'nobody']) {
      await navigateTo(`driving/${id}`);
      await expect(page, `driving/${id} leaves for driving`).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving`);
      await expect(page.getByTestId('stat-speeding'), `${id}: the report is on screen`).toBeVisible();
      await expect(page.getByTestId('detail-back'), `${id}: no driver week`).toHaveCount(0);
    }

    // The entry of driving/{id} was replaced: Back does not return to it.
    await page.goBack();
    await expect(page).toHaveURL((url) => url.pathname === `${INGRESS_PREFIX}/driving`);
    await expect(page.getByTestId('detail-back')).toHaveCount(0);
  });

  // ---- AC-38 ----------------------------------------------------------------------------------------------------------------------------------

  test(
    '[AC-38] each of the six items opens a dialog with the title, lore, summary, bars, footnote and a 52 px Got it',
    { tag: ['@phone', '@unfolded'] },
    async ({ page }) => {
      await openDriving(page);
      const width = page.viewportSize()?.width ?? 412;

      for (const spec of popupSpecs()) {
        const opener = page.getByTestId(spec.opener);
        const dialog = await openPopup(page, opener, spec.key);

        // The dialog is named by its title; the title, lore and the exact summary sentence of 6.7.
        await expect(page.getByRole('dialog', { name: spec.title, exact: true }), `${spec.key}: the dialog is named ${spec.title}`).toBeVisible();
        await expect(dialog.locator('.realm-popup__title'), `${spec.key}: title`).toHaveText(spec.title);
        await expect(dialog.locator('.realm-popup__lore'), `${spec.key}: lore`).toHaveText(spec.lore);
        await expect(dialog.locator('.realm-popup__summary'), `${spec.key}: summary`).toHaveText(spec.summary);

        // Bars for all four drivers, sorted as in 6.7.1.
        expect(await barIds(dialog), `${spec.key}: the bars, in order`).toEqual(spec.bars.map((id) => `bar-${id}`));

        // The footnote starts with the Realm's own records; the original source is never named, anywhere in the dialog.
        await expect(dialog.locator('.realm-popup__footnote'), `${spec.key}: footnote`).toHaveText(SOURCE_FOOTNOTE);
        await expect(dialog, `${spec.key}: Life360 is never named`).not.toContainText('Life360');

        // The dialog sits centred, 92 % of the width up to 420 (440 from 840), and Got it is a full-width 52 px button.
        const box = await boxOf(dialog, `${spec.key} dialog`);
        const expectedWidth = width >= 840 ? 440 : Math.min(0.92 * width, 420);
        expectApprox(box.width, expectedWidth, TOLERANCE_PX, `${spec.key} dialog width`);
        const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);
        expectApprox(box.x + box.width / 2, clientWidth / 2, TOLERANCE_PX, `${spec.key} dialog is centred`);
        const gotIt = await boxOf(dialog.getByTestId('popup-gotit'), `${spec.key} Got it`);
        expectApprox(gotIt.height, 52, TOLERANCE_PX, `${spec.key} Got it height`);
        expectApprox(gotIt.width, box.width - 48, TOLERANCE_PX, `${spec.key} Got it is the width of the content`);
        await expect(dialog.getByTestId('popup-gotit')).toHaveText('Got it');

        await closePopup(page, dialog, 'got-it');
        await expect(opener, `${spec.key}: the focus returns to the opener`).toBeFocused();
      }

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, 'no horizontal page scroll').toBeLessThanOrEqual(0);
    },
  );

  test('[AC-38] the speeding footnote says the count is sampled, and the phone footnote has the Android Auto information button', async ({ page }) => {
    await openDriving(page);

    const speeding = await openPopup(page, page.getByTestId('stat-speeding'), 'speeding');
    await expect(speeding.locator('.realm-popup__footnote')).toContainText('sampled every ~42 s, so brief bursts are missed');
    await expect(speeding.locator('.realm-popup__info'), 'only the phone popup has an information button').toHaveCount(0);
    await closePopup(page, speeding, 'got-it');

    const phone = await openPopup(page, page.getByTestId('stat-phone'), 'phone');
    const info = phone.getByRole('button', { name: 'About Android Auto' });
    await expect(info, 'the information button is in the phone footnote').toBeVisible();
    await expect(phone.locator('.realm-popup__footnote').locator('.realm-popup__info')).toHaveCount(1);
    const infoBox = await boxOf(info, 'the information button');
    expect.soft(Math.min(infoBox.width, infoBox.height), 'a tap target of at least 48 px').toBeGreaterThanOrEqual(48 - TOLERANCE_PX);
    expect(await tooltipOf(page, info, 'hover'), 'the tooltip on hover').toBe(ANDROID_AUTO_TIP);
    expect(await tooltipOf(page, info, 'focus'), 'the tooltip on keyboard focus').toBe(ANDROID_AUTO_TIP);
    await closePopup(page, phone, 'got-it');
  });

  test('[AC-38] Got it, the close button and Esc each close the popup, and focus returns to the opener', async ({ page }) => {
    await openDriving(page);
    const opener = page.getByTestId('stat-speeding');

    for (const how of ['got-it', 'close', 'escape'] as const) {
      const dialog = await openPopup(page, opener, 'speeding');
      await expect(dialog.getByTestId('popup-close'), 'the close button is named Close').toHaveAttribute('aria-label', 'Close');

      // Inside the dialog the focus is trapped: Tab goes from the title to Close, then to Got it (Speeding has no toggle and no information button).
      if (how === 'got-it') {
        await expect(dialog.locator('.realm-popup__title'), 'the title takes the focus when the popup opens').toBeFocused();
        await page.keyboard.press('Tab');
        await expect(dialog.getByTestId('popup-close'), 'Tab goes to Close first').toBeFocused();
        await page.keyboard.press('Tab');
        await expect(dialog.getByTestId('popup-gotit'), 'then to Got it').toBeFocused();
      }

      await closePopup(page, dialog, how);
      await expect(opener, `the focus returns to the Speeding chip after ${how}`).toBeFocused();
    }

    // The two cards close and give the focus back too (they are buttons of their own).
    for (const [testId, key] of [['card-topspeed', 'topspeed'], ['card-drives', 'drives']] as const) {
      const card = page.getByTestId(testId);
      const dialog = await openPopup(page, card, key);
      await closePopup(page, dialog, 'escape');
      await expect(card, `the focus returns to ${testId}`).toBeFocused();
    }
  });

  // The Back button of the device is the fourth way to close a popup (01 section 6.7, AC-38): a history layer of the overlay stack of D31, which HistorySync (S8a, S8c) owns. The layer is a
  // token entry (`#r<n>`) that realmShell.js pushes AFTER the circuit has opened the popup (a round trip): on Driving the page itself is depth 1 (the sentinel that stops Back from leaving
  // at once) and the open popup makes it 2. The test therefore waits for `#r2` before it presses Back, so that Back is pressed with the layer there and not on a guess (review R3-03).
  test('[AC-38] Back closes the popup, and focus returns to the opener', async ({ page }) => {
    await openDriving(page);
    const opener = page.getByTestId('stat-speeding');
    const dialog = await openPopup(page, opener, 'speeding');
    await expectHistoryDepth(page, 2, 'with the popup open over the Driving page');

    await page.goBack();

    await expect(dialog, 'Back closes the popup').toHaveCount(0);
    expect(new URL(page.url()).pathname, 'and stays on driving').toBe(`${INGRESS_PREFIX}/driving`);
    await expect(opener).toBeFocused();
  });

  // ---- AC-39 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-39] the Total Drives popup: bars 22, 18, 14, 10 in proportion, and the Miles toggle re-orders them', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openDriving(page);
    const dialog = await openPopup(page, page.getByTestId('card-drives'), 'drives');

    // Drives: Alden 22, Cass 18, Dara 14, Briar 10; the avatar at the right end of each fill.
    expect(await barIds(dialog)).toEqual(['bar-king', 'bar-jester', 'bar-cryptid', 'bar-queen']);
    expect(await pillTexts(dialog)).toEqual(['22', '18', '14', '10']);
    await expect(dialog.getByTestId('popup-toggle-drives'), 'Drives is the chosen view').toHaveAttribute('aria-pressed', 'true');
    await expectBars(dialog, [{ id: 'king', value: 22 }, { id: 'jester', value: 18 }, { id: 'cryptid', value: 14 }, { id: 'queen', value: 10 }], 56);

    // Miles: Dara 366.0 mi, Cass 202.6 mi, Briar 118.2 mi, Alden 94.4 mi, in the wider pills of 72 px.
    await dialog.getByTestId('popup-toggle-miles').click();
    await expect(dialog.getByTestId('popup-toggle-miles'), 'Miles is the chosen view').toHaveAttribute('aria-pressed', 'true');
    await expect.poll(() => barIds(dialog), 'the bars are re-ordered').toEqual(['bar-cryptid', 'bar-jester', 'bar-queen', 'bar-king']);
    expect(await pillTexts(dialog)).toEqual(['366.0 mi', '202.6 mi', '118.2 mi', '94.4 mi']);
    await expectBars(dialog, [{ id: 'cryptid', value: 366.0 }, { id: 'jester', value: 202.6 }, { id: 'queen', value: 118.2 }, { id: 'king', value: 94.4 }], 72);
    await expect(dialog.locator('.realm-popup__summary'), 'the sentence does not change with the toggle').toHaveText('64 drives, 781 miles on the road.');

    // And back to Drives.
    await dialog.getByTestId('popup-toggle-drives').click();
    await expect.poll(() => barIds(dialog)).toEqual(['bar-king', 'bar-jester', 'bar-cryptid', 'bar-queen']);
    expect(await pillTexts(dialog)).toEqual(['22', '18', '14', '10']);
  });

  test('[AC-39] the Phone use popup: 60 for Alden, then three "—" rows at the 56 px floor, last, with the info icon', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openDriving(page);
    const dialog = await openPopup(page, page.getByTestId('stat-phone'), 'phone');
    const cast = loadDemoCast();

    expect(await barIds(dialog), 'Alden first, then Cass, Dara and Briar in card order').toEqual(['bar-king', 'bar-jester', 'bar-cryptid', 'bar-queen']);
    expect(await pillTexts(dialog)).toEqual(['60', DASH, DASH, DASH]);

    const alden = await barGeometry(dialog, 'king');
    expect.soft(alden.pill.width, "Alden's pill is at least 56 px wide (min-width)").toBeGreaterThanOrEqual(56 - 0.5);
    expectApprox(alden.fill.width, alden.track.width - alden.pill.width - 8, TOLERANCE_PX, "Alden's bar takes all the room left of his pill (100 %)");
    for (const id of ['jester', 'cryptid', 'queen'] as const) {
      const row = dialog.getByTestId(`bar-${id}`);
      const geometry = await barGeometry(dialog, id);
      expectApprox(geometry.fill.width, 56, TOLERANCE_PX, `${id} sits at the 56 px floor`);
      await expect(row.locator('.realm-bar__info'), `${id} has the info icon`).toBeVisible();
      await expect(row.locator('.realm-sr-only'), `${id} accessible text`).toHaveText(`${castMember(cast, id).name}: not recorded`);
      await expect(row.locator('.realm-bar__value'), `${id} is a dash, never 0`).toHaveText(DASH);
    }
    await expect(dialog.locator('.realm-bar--unknown')).toHaveCount(3);
    await expect(dialog.getByTestId('bar-king').locator('.realm-sr-only')).toHaveText(`${castMember(cast, 'king').name}: 60 phone-use events`);
    await closePopup(page, dialog, 'got-it');
  });

  test('[AC-39] the Speeding and Top Speed bars are in value order with the avatar at the end of each fill', async ({ page }) => {
    await openDriving(page);

    const speeding = await openPopup(page, page.getByTestId('stat-speeding'), 'speeding');
    expect(await pillTexts(speeding)).toEqual(['38', '10', '6', '2']);
    await expectBars(speeding, [{ id: 'jester', value: 38 }, { id: 'cryptid', value: 10 }, { id: 'king', value: 6 }, { id: 'queen', value: 2 }], 56);
    await closePopup(page, speeding, 'got-it');

    const topSpeed = await openPopup(page, page.getByTestId('card-topspeed'), 'topspeed');
    expect(await pillTexts(topSpeed)).toEqual(['96 mph', '88 mph', '84 mph', '82 mph']);
    await expectBars(topSpeed, [{ id: 'king', value: 96 }, { id: 'jester', value: 88 }, { id: 'cryptid', value: 84 }, { id: 'queen', value: 82 }], 72);
    await closePopup(page, topSpeed, 'got-it');
  });

  // ---- AC-40 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-40] with variant phone-unavailable the Phone use chip, popup and Alden\'s pill read "—" and never 0', async ({ page }) => {
    await openDriving(page, { variant: 'phone-unavailable' });

    // The Phone use chip: a dash, no arrow, an information icon, and the tooltip on hover and on focus. Speeding is unchanged (56, up).
    const phone = page.getByTestId('stat-phone');
    await expect(phone).toHaveText(chipText(DASH, 'Phone use'));
    await expect(page.getByTestId('trend-phone'), 'no arrow on the Phone use chip').toHaveCount(0);
    await expect(phone.locator('.realm-stat-chip__info'), 'the information icon').toBeVisible();
    expect(await tooltipOf(page, phone, 'hover'), 'Phone use tooltip on hover').toBe(UNAVAILABLE_TIP);
    expect(await tooltipOf(page, phone, 'focus'), 'Phone use tooltip on focus').toBe(UNAVAILABLE_TIP);
    await expect(page.getByTestId('stat-speeding')).toHaveText(chipText('56', 'Speeding'));
    await expectArrow(page, 'speeding', 'up');

    // Alden's pill lists only speeding, with no asterisk.
    await expect(page.getByTestId('driver-pill-king')).toHaveText('6 speeding events');
    await expect(page.getByTestId('driver-pill-king')).not.toContainText('*');

    // The popup: the unavailable sentence and a dash on every row, none of them 0.
    const dialog = await openPopup(page, phone, 'phone');
    await expect(dialog.locator('.realm-popup__summary')).toHaveText(UNAVAILABLE_SENTENCE);
    await expect(dialog.locator('.realm-bar')).toHaveCount(4);
    expect(await pillTexts(dialog)).toEqual([DASH, DASH, DASH, DASH]);
    await expect(dialog.locator('.realm-bar--unknown')).toHaveCount(4);
    await expect(dialog.locator('.realm-bar__info')).toHaveCount(4);
    for (const id of CARD_ORDER) {
      await expect(dialog.getByTestId(`bar-${id}`).locator('.realm-sr-only')).toHaveText(`${castMember(loadDemoCast(), id).name}: not recorded`);
    }
    await closePopup(page, dialog, 'got-it');
  });

  // ---- AC-41 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-41] the driver week of Cass: 18 drives in day groups, newest first, chips only for non-zero counts, and the GPS caption', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await demo(page, { path: 'driving/jester', week: 0 });
    const cast = loadDemoCast();

    // The header: Back, the avatar, the name, the lore and the week; then the four tiles of 6.6.
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(castMember(cast, 'jester').name);
    await expect(page.locator('.realm-driver-week__lore')).toHaveText(castMember(cast, 'jester').lore);
    await expect(page.locator('.realm-driver-week__week')).toHaveText('This week · Sep 28 – Oct 4');
    await expect(page.getByTestId('detail-back')).toHaveAttribute('href', 'driving?week=0');
    await expect(page.getByTestId('detail-back')).toHaveAttribute('aria-label', 'Back to the driving report');
    const back = await boxOf(page.getByTestId('detail-back'), 'the Back link');
    expect.soft(Math.min(back.width, back.height), 'the Back link is at least 48 px').toBeGreaterThanOrEqual(48 - TOLERANCE_PX);
    await expect(page.locator('.realm-week-tile__label')).toHaveText(['Drives', 'Miles', 'Top speed', 'Events']);
    await expect(page.locator('.realm-week-tile__value')).toHaveText(['18', '202.6', '88 mph', '38 speeding events']);
    await expect(page.locator('.realm-week-event__count')).toHaveText(['38', DASH, DASH, DASH]);
    await expect(page.locator('.realm-week-event__caption'), 'speeding is sampled').toHaveText('sampled');

    // 18 drives in three day groups (Wed 6, Tue 4, Mon 8), newest first.
    await expect(page.locator('li.realm-drive-row')).toHaveCount(18);
    await expect(page.locator('.realm-drive-day__header')).toHaveText(['Wed, Sep 30', 'Tue, Sep 29', 'Mon, Sep 28']);
    const perDay = await page.locator('section.realm-drive-day').evaluateAll((days) => days.map((day) => day.querySelectorAll('li.realm-drive-row').length));
    expect(perDay, 'the drives in each day group').toEqual([6, 4, 8]);

    // The newest row: "9:01 – 9:06 pm, Hearth Haven → The Jester's Hall, 1.1 mi", with no chip (no non-zero count).
    const newest = page.getByTestId('drive-row-0');
    await expect(newest.locator('.realm-drive-row__time')).toHaveText('9:01 – 9:06 pm');
    await expect(newest.locator('.realm-drive-row__route')).toHaveText("Hearth Haven → The Jester's Hall");
    await expect(newest.locator('.realm-drive-row__detail')).toHaveText('1.1 mi · Top 38 mph');
    await expect(newest.locator('.realm-drive-event')).toHaveCount(0);
    const rowBox = await boxOf(newest, 'the newest row');
    expect.soft(rowBox.height, 'a drive row is at least 72 px high').toBeGreaterThanOrEqual(72 - TOLERANCE_PX);

    // Chips only for non-zero counts: the speeding chips are the week's 38.
    const counts = await page.locator('.realm-drive-event').evaluateAll((chips) => chips.map((chip) => Number(chip.querySelector('.realm-drive-event__count')?.textContent ?? '0')));
    expect(counts.length, 'some drives carry a chip, not all').toBeGreaterThan(0);
    expect(counts.length).toBeLessThan(18);
    expect(counts.every((count) => count > 0), 'no chip for a zero count').toBe(true);
    expect(counts.reduce((sum, count) => sum + count, 0), 'the chips add up to the 38 speeding events').toBe(38);
    await expect(page.getByTestId('drive-row-2').locator('.realm-drive-event')).toHaveAttribute('aria-label', '4 speeding events');

    // The page ends with the GPS caption of 8.8, and never scrolls sideways.
    await expect(page.locator('p.realm-driving__caption')).toHaveText('Distances are GPS-estimated (about 2–5 % low on winding roads).');
    const captionBox = await boxOf(page.locator('p.realm-driving__caption'), 'the caption');
    const lastRow = await boxOf(page.getByTestId('drive-row-17'), 'the last row');
    expect.soft(captionBox.y, 'the caption is below the last drive').toBeGreaterThanOrEqual(lastRow.y + lastRow.height - TOLERANCE_PX);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'no horizontal page scroll').toBeLessThanOrEqual(0);
    // R-001 / D26: the page never names Life360. One element, the page's own region (`main` and `body` together would be two, and body also holds the hidden error and reconnect texts).
    await expect(page.locator('#realm-main'), 'the driver week never names Life360').not.toContainText('Life360');
  });

  test('[AC-41] the driver week follows the week of the URL, and a week without a record says so', async ({ page }) => {
    await demo(page, { path: 'driving/jester', week: 1 });
    await expect(page.locator('.realm-driver-week__week')).toHaveText('Last week · Sep 21 – Sep 27');
    await expect(page.locator('.realm-week-tile__value')).toHaveText(['20', '215.2', '92 mph', '41 speeding events']);
    await expect(page.locator('li.realm-drive-row')).toHaveCount(20);
    await expect(page.getByTestId('detail-back')).toHaveAttribute('href', 'driving?week=1');

    await demo(page, { path: 'driving/jester', week: 2, variant: 'fresh-install' });
    await expect(page.locator('.realm-driver-week__week')).toHaveText('Sep 14 – Sep 20');
    await expect(page.locator('p.realm-driving__quiet')).toHaveText('The scribes have no record of this week.');
    await expect(page.locator('.realm-week-tile__value')).toHaveText([DASH, DASH, DASH, DASH]);
    await expect(page.locator('li.realm-drive-row')).toHaveCount(0);
    await expect(page.locator('p.realm-driving__caption')).toHaveCount(0);
  });
});
