// Settings, "Who's on the map" (01 section 7.9, D113), in the Demo with the roster it starts with: four people, the wagon, and the prince and the hatchback under Not tracked. These tests
// carry no [AC-nn] tag: the fifty acceptance criteria are unchanged; this spec covers the section the 0.2.0 release adds. The names come from the cast file (03 section 8.1 rule 2).
// The drag itself (HTML5 drag and drop, wwwroot/js/roster.js) is exercised with Playwright's dragTo; the keyboard path is the ⋮ button.
import type { Locator, Page } from '@playwright/test';

import { castMember, demo, expect, loadDemoCast, mapReady, readHook, test } from '../fixtures.js';

const dialog = (page: Page): Locator => page.getByTestId('settings-dialog');
const section = (page: Page): Locator => page.getByTestId('roster-section');
const count = (page: Page, group: 'people' | 'vehicles' | 'not-tracked'): Locator => page.getByTestId(`roster-count-${group}`);
const more = (page: Page, slug: string): Locator => page.getByTestId(`roster-more-${slug}`);

async function openRoster(page: Page): Promise<void> {
  await demo(page, { defaultRoster: true });
  await mapReady(page);
  await page.getByTestId('btn-settings').click();
  await expect(dialog(page), 'the Settings dialog is open').toBeVisible();
}

// What the map draws for people and vehicles: the pins on screen and the edge bubbles of those that are far away (the prince is far from the default view).
const pinIds = async (page: Page): Promise<string[]> => [
  ...(await readHook(page, 'pins')).map((pin) => pin.id),
  ...(await readHook(page, 'bubbles')).flatMap((bubble) => bubble.ids),
].sort();

test.describe("Settings, Who's on the map", () => {
  test('[ROSTER] the section is first, with PEOPLE 4, TRACKERS 1 and NOT TRACKED 2, and every row reads kind · source', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openRoster(page);

    await expect(dialog(page).locator('.realm-settings__section h3').first(), 'the roster is the first section').toHaveText("Who's on the map");
    await expect(section(page).locator('.realm-roster__heading'), 'the three groups and their counts').toHaveText(['PEOPLE 4', 'TRACKERS 1', 'NOT TRACKED 2']);
    for (const entry of cast.roster) {
      const slug = entry.entityId.replace(/[._]/g, '-');
      const row = page.getByTestId(`roster-row-${slug}`);
      await expect(row, `${entry.entityId} is listed`).toBeVisible();
      await expect(row.locator('.realm-roster__name > span').first(), `${entry.entityId}: the name`).toHaveText(entry.name);
      const kind = entry.kind === 'person' ? 'Person' : 'Tracker';
      const expected = `${kind} · ${entry.source}${entry.autoMoved ? ' · moved automatically' : ''}`;
      await expect(page.getByTestId(`roster-meta-${slug}`), `${entry.entityId}: kind and source`).toHaveText(expected);
    }
    await expect(page.getByTestId('roster-group-not-tracked').getByTestId('roster-row-person-prince'), 'the prince starts under Not tracked').toBeVisible();
    await expect(dialog(page).locator('.realm-settings__connection-name'), 'Connections lists exactly two sources').toHaveText(['Home Assistant', 'Life360']);
  });

  test('[ROSTER] the Demo map shows only who is on the roster: four people and the wagon', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await demo(page, { defaultRoster: true });
    await mapReady(page);

    const ids = await pinIds(page);
    expect(ids, 'no pin for the prince or the hatchback').not.toContain('prince');
    expect(ids, 'no pin for the hatchback').not.toContain('chariot');
    expect(ids, 'the wagon is on the map').toContain('wagon');
  });

  test('[ROSTER] the ⋮ button opens Move to…, Esc closes it and the focus returns to the button', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openRoster(page);

    const button = more(page, 'person-king');
    await button.focus();
    await page.keyboard.press('Enter');
    await expect(button, 'the button says the menu is open').toHaveAttribute('aria-expanded', 'true');
    const items = page.getByTestId('roster-menu').getByRole('button');
    await expect(items, 'the other groups, then Move down (the king is first)').toHaveText(['Move to Trackers', 'Move to Not tracked', 'Move down']);
    await expect(page.getByTestId('roster-menu'), 'the menu has the focus').toBeFocused();

    await page.keyboard.press('Escape');
    await expect(page.getByTestId('roster-menu'), 'Esc closes the menu').toHaveCount(0);
    await expect(button, 'the focus is back on the button').toBeFocused();
    await expect(dialog(page), 'Esc closed the menu and not the dialog').toBeVisible();
  });

  test('[ROSTER] moving the prince to People puts him on the map; moving the king to Not tracked takes him off', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openRoster(page);

    await more(page, 'person-prince').click();
    await page.getByTestId('roster-move-people').click();
    await expect(count(page, 'people'), 'five people').toHaveText('5');
    await expect(count(page, 'not-tracked'), 'one not tracked').toHaveText('1');
    await expect(page.getByTestId('roster-status'), 'the move is announced politely').toContainText('moved to People');
    await expect(page.getByTestId('roster-meta-person-prince'), 'the automatic mark is cleared').not.toContainText('moved automatically');
    await expect.poll(() => pinIds(page), { message: 'the prince has a pin (or a bubble) now' }).toContain('prince');

    const king = castMember(loadDemoCast(), 'king');
    await more(page, 'person-king').click();
    await page.getByTestId('roster-move-not-tracked').click();
    await expect(count(page, 'people'), 'four people again').toHaveText('4');
    await expect(page.getByTestId('roster-status')).toHaveText(`${king.name} moved to Not tracked`);
    await expect.poll(() => pinIds(page), { message: 'the king has no pin any more' }).not.toContain('king');
  });

  test('[ROSTER] a row can be dragged to another group', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openRoster(page);

    await page.locator('[data-roster-entity="device_tracker.hatchback"]').dragTo(page.getByTestId('roster-group-vehicles'));

    await expect(count(page, 'vehicles'), 'two vehicles').toHaveText('2');
    await expect(count(page, 'not-tracked'), 'one not tracked').toHaveText('1');
  });

  test('[ROSTER] tapping a row edits the name, the title and the colour; Save keeps them on the list and on the map', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openRoster(page);

    await page.getByTestId('roster-open-person-king').click();
    await expect(page.getByTestId('roster-name'), 'the name field has the focus').toBeFocused();
    await page.getByTestId('roster-name').fill('Alden the Bold');
    await page.getByTestId('roster-title').fill('Keeper of the Keys');
    await page.getByTestId('roster-color-2').click();
    await expect(page.getByTestId('roster-color-2'), 'the swatch is selected').toHaveAttribute('aria-checked', 'true');
    await page.getByTestId('roster-save').click();

    await expect(page.getByTestId('roster-edit'), 'the editor closes').toHaveCount(0);
    await expect(page.getByTestId('roster-row-person-king').locator('.realm-roster__name'), 'the row has the new name and title').toContainText('Alden the Bold');
    await expect(page.getByTestId('roster-row-person-king').locator('.realm-roster__lore'), 'and the title').toHaveText('Keeper of the Keys');
  });
});
