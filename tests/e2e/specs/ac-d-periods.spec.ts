// 0.2.0 Driving report: the long periods (split button), the custom range, the drive-list pager and the print stylesheet, in the Demo app frozen at Wed 2026-09-30 21:25 CDT
// (retention 400 days, so every period is offered). The address query is the state of the period: `?period=last-month|3m|6m|1y|custom&from=..&to=..`.
// The page is prerendered, so a click that needs the circuit is repeated until it has had its effect (toPass), as in ac-d-driving.spec.ts.
import type { Page } from '@playwright/test';

import { demo, expect, test } from '../fixtures.js';

async function openDriving(page: Page, path = 'driving'): Promise<void> {
  await demo(page, { path });
  await expect(page.getByTestId('period-bar'), 'the period bar has loaded').toBeVisible();
}

function periodOf(page: Page): string | null {
  return new URL(page.url()).searchParams.get('period');
}

async function chooseFromMenu(page: Page, value: string): Promise<void> {
  await expect(async () => {
    await page.getByTestId('period-menu').locator('button').first().click();
    await page.getByTestId(`period-item-${value}`).click({ timeout: 1_500 });
    await expect.poll(() => periodOf(page), { timeout: 1_500 }).toBe(value);
  }).toPass({ timeout: 20_000 });
}

/** The team report in the 1-year period, then Cass's page: the period travels in the link of the card. */
async function openDriverYear(page: Page): Promise<void> {
  await openDriving(page);
  await chooseFromMenu(page, '1y');
  await expect(async () => {
    await page.getByTestId('driver-card-jester').click();
    await expect(page.getByTestId('drive-list')).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
}

/** Types a date into an editable picker, commits it with Enter and closes the picker's popover so that it cannot cover Apply. */
async function typeDate(page: Page, testId: string, value: string): Promise<void> {
  const input = page.getByTestId(testId).locator('input');
  await input.fill(value, { timeout: 5_000 });
  await input.press('Enter');
  await input.press('Escape');
  await expect(page.locator('.mud-popover-open.mud-picker-popover-paper'), 'the picker is closed').toHaveCount(0, { timeout: 5_000 });
}

test.describe('Driving periods', () => {
  test('the menu offers every period at 400 days of history and choosing one puts it in the URL', async ({ page }) => {
    await openDriving(page);
    await expect(page.getByTestId('period-main'), 'the main part defaults to Last month').toContainText('Last month');

    await expect(async () => {
      await page.getByTestId('period-menu').locator('button').first().click();
      await expect(page.getByTestId('period-item-3m'), 'the menu is open').toBeVisible({ timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    for (const value of ['last-month', '3m', '6m', '1y', 'custom']) await expect(page.getByTestId(`period-item-${value}`)).toBeVisible();
    await page.keyboard.press('Escape');

    await chooseFromMenu(page, '3m');
    await expect(page.getByTestId('period-main'), 'the main part now re-applies Last 3 months').toContainText('Last 3 months');
    await expect(page.getByTestId('week-chip-0'), 'no week chip is selected').toHaveAttribute('aria-checked', 'false');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Driving Report');

    // A week chip leaves the long period; the main part keeps the choice.
    await expect(async () => {
      await page.getByTestId('week-chip-1').click();
      await expect(page.getByTestId('week-chip-1')).toHaveAttribute('aria-checked', 'true', { timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    await expect.poll(() => periodOf(page)).toBeNull();
    await page.getByTestId('period-main').click();
    await expect.poll(() => periodOf(page)).toBe('3m');
  });

  test('a custom range is validated and then applied', async ({ page }) => {
    await openDriving(page);
    await expect(async () => {
      await page.getByTestId('period-menu').locator('button').first().click();
      await page.getByTestId('period-item-custom').click({ timeout: 1_500 });
      await expect(page.getByTestId('custom-range')).toBeVisible({ timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    await expect(page.getByTestId('custom-range'), 'the custom range panel is open').toBeVisible();

    // Start after end is refused with a message.
    await typeDate(page, 'custom-from', '2026-09-10');
    await typeDate(page, 'custom-to', '2026-09-01');
    await page.getByTestId('custom-apply').click();
    await expect(page.getByTestId('custom-message')).toBeVisible();
    expect(periodOf(page)).not.toBe('custom');

    // A valid range is applied and lands in the address.
    await typeDate(page, 'custom-from', '2026-09-01');
    await typeDate(page, 'custom-to', '2026-09-10');
    await page.getByTestId('custom-apply').click();
    await expect.poll(() => periodOf(page)).toBe('custom');
    const query = new URL(page.url()).searchParams;
    expect(query.get('from')).toBe('2026-09-01');
    expect(query.get('to')).toBe('2026-09-10');
    await expect(page.getByText('Sep 1 – Sep 10', { exact: false }).first()).toBeVisible();
  });
});

test.describe('Driver events pager', () => {
  test('25 rows by default, 50 and 100 on request, and a single page shows only the bottom pager', async ({ page }) => {
    await openDriverYear(page);

    const rows = page.getByTestId('drive-list').locator('li');
    await expect(page.getByTestId('events-pager-bottom')).toBeVisible();
    await expect(page.getByTestId('events-pager-size-bottom')).toHaveValue('25');
    const total = await page.getByTestId('print-event-row').count();

    if (total > 25) {
      await expect(page.getByTestId('events-pager-top'), 'many pages show the top pager as well').toBeVisible();
      await expect(rows).toHaveCount(25);
      await page.getByTestId('events-pager-size-bottom').selectOption('50');
      await expect(rows).toHaveCount(Math.min(50, total));
      await page.getByTestId('events-pager-size-top').selectOption('100');
      await expect(rows).toHaveCount(Math.min(100, total));
    } else {
      await expect(page.getByTestId('events-pager-top'), 'one page hides the top pager').toHaveCount(0);
      await expect(rows).toHaveCount(total);
    }
  });

  test('a short week fits one page: no top pager', async ({ page }) => {
    await openDriving(page);
    await expect(async () => {
      await page.getByTestId('driver-card-jester').click();
      await expect(page.getByTestId('drive-list')).toBeVisible({ timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    await expect(page.getByTestId('events-pager-top')).toHaveCount(0);
    await expect(page.getByTestId('events-pager-bottom')).toBeVisible();
  });
});

test.describe('Printing', () => {
  test('the print stylesheet hides the chrome and lists every event', async ({ page }) => {
    await openDriverYear(page);
    const total = await page.getByTestId('print-event-row').count();
    expect(total).toBeGreaterThan(0);

    await page.emulateMedia({ media: 'print' });
    await expect(page.getByTestId('nav-driving'), 'the navigation is not printed').toBeHidden();
    await expect(page.getByTestId('period-bar')).toBeHidden();
    await expect(page.getByTestId('events-pager-bottom')).toBeHidden();
    await expect(page.getByTestId('print-header')).toContainText('Driving Report');
    await expect(page.getByTestId('print-header')).toContainText('printed');
    await expect(page.getByTestId('print-footer')).toBeVisible();
    const printed = page.getByTestId('print-event-row');
    await expect(printed).toHaveCount(total);
    await expect(printed.first()).toBeVisible();
    await expect(printed.last()).toBeVisible();
    await expect(page.getByTestId('drive-list'), 'the paged screen list is not printed').toBeHidden();
    await expect(page.getByText('Unknown place')).toHaveCount(0);

    await page.emulateMedia({ media: 'screen' });
    await expect(page.getByTestId('print-header')).toBeHidden();
  });

  test('the team report prints its totals and drivers tables', async ({ page }) => {
    await openDriving(page);
    await expect(page.getByTestId('btn-print')).toBeVisible();
    await page.emulateMedia({ media: 'print' });
    await expect(page.getByTestId('print-totals')).toBeVisible();
    await expect(page.getByTestId('print-drivers')).toBeVisible();
    await expect(page.getByTestId('btn-print')).toBeHidden();
  });
});
