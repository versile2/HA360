// Location History (0.3.0, D123): opened from a person's detail, a day on the map and as a list, the day selector, a tapped drive highlighted on the map, the last seven days, and the
// print view. The Demo app frozen at Wed 2026-09-30 21:25 CDT; its generated history has a home-work-home day for Alden on every weekday. Not an acceptance criterion: no AC tags.
import type { Locator, Page } from '@playwright/test';

import { castMember, demo, demoUrl, expect, loadDemoCast, test, waitForInteractive } from '../fixtures.js';

const TODAY = '2026-09-30';
const YESTERDAY = '2026-09-29';

const dayOf = (page: Page): Promise<string | null> => page.getByTestId('history-page').getAttribute('data-day');

/** Opens a person's History through the app: the row, the handle (the detail), the History button. The prerendered page may swallow a click, so each step repeats until it has had its effect. */
async function openFromPerson(page: Page, id: string): Promise<void> {
  await demo(page, { sheet: '80', layout: 'sheet' });
  const row = page.getByTestId(`row-member-${id}`);
  await expect(async () => {
    await row.click();
    await expect(page.getByTestId('sheet-selection-header')).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  await expect(async () => {
    if ((await page.getByTestId('detail-history').count()) === 0) await page.getByTestId('sheet-handle').click();
    await expect(page.getByTestId('detail-history')).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  await page.getByTestId('detail-history').click();
  await expect(page.getByTestId('history-page')).toBeVisible();
  await expect(page.getByTestId('history-summary'), 'the day has loaded').toBeVisible();
}

/** Opens `history/{id}` with the Demo parameters first and the History query after them (`demo()` would put its own `?` after a path that carries a query). */
async function openHistory(page: Page, id: string, query = ''): Promise<void> {
  const url = demoUrl({ path: `history/${id}`, variant: 'full-cast' }) + query;
  await page.goto(url);
  await waitForInteractive(page, url);
  await expect(page.getByTestId('history-summary'), 'the day has loaded').toBeVisible();
}

const entries = (page: Page): Locator => page.locator('[data-testid^="history-entry-"]');

test.describe('Location History', () => {
  test('History opens from a person, shows today with the name, and Back returns to the map', async ({ page }) => {
    const cast = loadDemoCast();
    await openFromPerson(page, 'king');

    await expect(page.locator('h1.realm-history__name')).toHaveText(castMember(cast, 'king').name);
    expect(await dayOf(page)).toBe(TODAY);
    await expect(page.getByTestId('history-next'), 'there is no day after today').toBeDisabled();
    await expect(page.getByTestId('history-date')).toContainText('Sep 30');
    await expect(page.getByTestId('history-map')).toBeVisible();
    await expect(page.getByTestId('history-map')).toHaveAttribute('data-history-ready', 'true', { timeout: 30_000 });

    await page.getByTestId('history-back').click();
    await expect(page.getByTestId('history-page')).toHaveCount(0);
    await expect(page.getByTestId('btn-settings'), 'the Location screen is back').toBeVisible();
  });

  test('the day selector steps to yesterday and back, and a date in the URL opens that day', async ({ page }) => {
    await openHistory(page, 'king');
    expect(await dayOf(page)).toBe(TODAY);

    await page.getByTestId('history-prev').click();
    await expect.poll(() => dayOf(page), { message: 'one step back is yesterday' }).toBe(YESTERDAY);
    expect(new URL(page.url()).searchParams.get('date')).toBe(YESTERDAY);
    await expect(page.getByTestId('history-date')).toContainText('Sep 29');
    await expect(entries(page).first(), 'the day lists its visits and drives').toBeVisible();

    await page.getByTestId('history-today').click();
    await expect.poll(() => dayOf(page)).toBe(TODAY);

    await openHistory(page, 'king', `&date=${YESTERDAY}`);
    expect(await dayOf(page)).toBe(YESTERDAY);
  });

  test('a day reads home, work and home again, with the zone names and the drives between', async ({ page }) => {
    await openHistory(page, 'king', `&date=${YESTERDAY}`);

    const list = page.getByTestId('history-list');
    await expect(list).toContainText('At Hearth Haven');
    await expect(list).toContainText('At Work');
    await expect(list).toContainText(/Drive · [\d.]+ mi · /);
    await expect(list).toContainText('Hearth Haven → Work');
    await expect(page.getByText('Unknown place')).toHaveCount(0);
  });

  test('tapping a drive highlights its path on the map, and tapping it again clears it', async ({ page }) => {
    await openHistory(page, 'king', `&date=${YESTERDAY}`);
    const map = page.getByTestId('history-map');
    await expect(map).toHaveAttribute('data-history-ready', 'true', { timeout: 30_000 });
    await expect(map, 'the trail is drawn').not.toHaveAttribute('data-history-segments', '0');

    const drive = page.locator('[data-testid^="history-entry-"][data-kind="drive"]').first();
    await expect(drive).toBeVisible();
    const id = (await drive.getAttribute('data-testid'))!.replace('history-entry-', '');

    await expect(async () => {
      await drive.click();
      await expect(map).toHaveAttribute('data-history-active', id, { timeout: 1_500 });
    }).toPass({ timeout: 20_000 });
    await expect(drive).toHaveAttribute('aria-pressed', 'true');

    await drive.click();
    await expect(map).toHaveAttribute('data-history-active', '');
    await expect(drive).toHaveAttribute('aria-pressed', 'false');
  });

  test('the last seven days are a list of days, and a day opens its own', async ({ page }) => {
    await openHistory(page, 'king', '&range=7d');

    await expect(page.getByTestId('history-page')).toHaveAttribute('data-mode', 'range');
    await expect(page.getByTestId(`history-day-${TODAY}`)).toBeVisible();
    await expect(page.getByTestId('history-prev'), 'the day selector is for the day view').toHaveCount(0);

    await page.getByTestId(`history-day-${YESTERDAY}`).click();
    await expect.poll(() => dayOf(page)).toBe(YESTERDAY);
    await expect(page.getByTestId('history-page')).toHaveAttribute('data-mode', 'day');
  });

  test('the print stylesheet shows the timeline as a table with the header, and no map', async ({ page }) => {
    await openHistory(page, 'king', `&date=${YESTERDAY}`);
    await expect(page.getByTestId('btn-print')).toBeVisible();

    await page.emulateMedia({ media: 'print' });
    await expect(page.getByTestId('print-header')).toContainText('Location History · Alden · ');
    await expect(page.getByTestId('print-header')).toContainText('Sep 29');
    const table = page.getByTestId('print-history');
    await expect(table).toBeVisible();
    expect(await page.getByTestId('print-history-row').count()).toBeGreaterThan(2);
    await expect(table).toContainText('Hearth Haven');
    await expect(page.getByTestId('history-map'), 'the map is not printed').toBeHidden();
    await expect(page.getByTestId('history-panel'), 'the screen list is not printed').toBeHidden();
    await expect(page.getByTestId('print-footer')).toBeVisible();

    await page.emulateMedia({ media: 'screen' });
    await expect(page.getByTestId('print-header')).toBeHidden();
  });

  // 0.3.1, D125: a tracker keeps its history by default. The Demo wagon has five days: Tuesday it goes from home to the vet and back.
  test('a tracker that keeps its history shows a trail on the map and a timeline of its moves', async ({ page }) => {
    await openHistory(page, 'wagon', `&date=${YESTERDAY}`);

    await expect(page.getByTestId('history-none')).toHaveCount(0);
    await expect(page.locator('h1.realm-history__name')).toContainText('Pickup');
    const map = page.getByTestId('history-map');
    await expect(map).toHaveAttribute('data-history-ready', 'true', { timeout: 30_000 });
    await expect(map, 'the trail is drawn').not.toHaveAttribute('data-history-segments', '0');

    const list = page.getByTestId('history-list');
    await expect(list).toContainText('At Hearth Haven');
    await expect(list).toContainText('At Vet Clinic');
    await expect(list).toContainText(/Moved · [\d.]+ mi · /);
    await expect(list).toContainText('Hearth Haven → Vet Clinic');
    await expect(list).not.toContainText('Drive ·');
    expect(await entries(page).count()).toBeGreaterThan(3);
  });

  test('someone who is not on the map has no history: a sentence and the way back, no map', async ({ page }) => {
    // The hatchback has no demo history; the Demo roster starts with it under Not tracked, so it has none either way.
    await demo(page, { path: 'history/hatchback', hooks: false });
    await expect(page.getByTestId('history-none')).toBeVisible();
    await expect(page.getByTestId('history-map')).toHaveCount(0);
  });
});
