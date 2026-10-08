// "+ Add driver", "+ Add tracker" and "+ Add place" (0.2.2, D119, D120, D121), in the Demo: the rows end the lists, the pickers list every candidate and grey out the ones on the map, a pick moves
// the entry and selects it, and a place is placed with a pin, a radius and a name (the creation is simulated in the Demo, so no Home Assistant is needed). These tests carry no [AC-nn] tag: the
// fifty acceptance criteria are unchanged.
import type { Locator, Page } from '@playwright/test';

import { demo, expect, mapReady, readHook, test } from '../fixtures.js';

const dialog = (page: Page): Locator => page.getByTestId('add-picker');

async function openList(page: Page, tab: 'tab-drivers' | 'tab-vehicles' | 'tab-places'): Promise<void> {
  await demo(page, { defaultRoster: true });
  await mapReady(page);
  await page.getByTestId('sheet-handle').click();
  await page.getByTestId(tab).click();
  await expect(page.getByTestId(tab), 'the tab is selected').toHaveAttribute('aria-selected', 'true');
}

const pinIds = async (page: Page): Promise<string[]> => [
  ...(await readHook(page, 'pins')).map((pin) => pin.id),
  ...(await readHook(page, 'bubbles')).flatMap((bubble) => bubble.ids),
].sort();

test.describe('Adding drivers, trackers and places', () => {
  test('[ADD] each list ends with its Add row, a 48 px target that is a button, after the rows and not among them', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page, 'tab-drivers');

    for (const [tab, testId, label] of [['tab-drivers', 'add-driver', '+ Add driver'], ['tab-vehicles', 'add-tracker', '+ Add tracker'], ['tab-places', 'add-place', '+ Add place']] as const) {
      await page.getByTestId(tab).click();
      const row = page.getByTestId(testId);
      await row.scrollIntoViewIfNeeded();
      await expect(row, `${testId} is shown`).toBeVisible();
      await expect(row, `${testId}: the label`).toHaveText(label);
      await expect(page.getByRole('button', { name: label }), `${testId}: an accessible button`).toHaveCount(1);
      const box = await row.boundingBox();
      expect(box?.height ?? 0, `${testId}: at least 48 px tall`).toBeGreaterThanOrEqual(48);
      await expect(page.locator('[data-testid="sheet-list"] [data-testid^="add-"]'), `${testId}: not inside the list`).toHaveCount(0);
    }
  });

  test('[ADD] Add tracker lists every candidate, greys out those on the map, searches, and a pick moves the hatchback into Trackers and selects it', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page, 'tab-vehicles');
    expect(await pinIds(page), 'the hatchback starts off the map').not.toContain('chariot');

    await page.getByTestId('add-tracker').click();
    await expect(dialog(page), 'the picker is open').toBeVisible();
    await expect(page.locator('.realm-popup__title'), 'the title').toHaveText('Add tracker');
    const wagon = page.getByTestId('add-pick-device-tracker-wagon');
    await expect(wagon, 'the wagon is on the map, so it is disabled').toBeDisabled();
    await expect(wagon, 'and says why').toContainText('Already on the map');
    await expect(wagon.locator('.realm-roster__avatar'), 'with the avatar the map draws').toBeVisible();
    await expect(wagon.locator('code'), 'and its entity id').toHaveText('device_tracker.wagon');
    const hatchback = page.getByTestId('add-pick-device-tracker-hatchback');
    await expect(hatchback, 'the hatchback is Not tracked, so it can be chosen').toBeEnabled();

    await page.getByTestId('add-search').fill('hatch');
    await expect(page.locator('[data-testid^="add-pick-"]'), 'the search narrows the list').toHaveCount(1);
    await page.getByTestId('add-search').fill('zzzz');
    await expect(page.getByTestId('add-none'), 'nothing matches').toBeVisible();
    await page.getByTestId('add-search').fill('');

    await hatchback.click();
    await expect(dialog(page), 'a pick closes the picker').toHaveCount(0);
    await expect.poll(() => pinIds(page), { message: 'the hatchback is on the map' }).toContain('chariot');
    await expect(page.getByTestId('sheet-selection-header'), 'and selected').toBeVisible();
  });

  test('[ADD] Add driver puts the prince on the map the same way, and Back closes the picker', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page, 'tab-drivers');

    await page.getByTestId('add-driver').click();
    await expect(page.locator('.realm-popup__title'), 'the title').toHaveText('Add driver');
    await expect(page.getByTestId('add-pick-person-king'), 'the king is on the map').toBeDisabled();
    await page.keyboard.press('Escape');
    await expect(dialog(page), 'Esc closes the picker').toHaveCount(0);

    await page.getByTestId('add-driver').click();
    await page.getByTestId('add-pick-person-prince').click();
    await expect(dialog(page)).toHaveCount(0);
    await expect.poll(() => pinIds(page), { message: 'the prince is on the map' }).toContain('prince');
  });

  test('[ADD] Add place shows a pin and a circle, a tap moves the pin, the slider sets the radius, and Save adds the place and selects it', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page, 'tab-places');

    await page.getByTestId('add-place').click();
    const panel = page.getByTestId('placement-panel');
    await expect(panel, 'the panel is open').toBeVisible();
    await expect(page.getByTestId('placement-pin'), 'the pin is on the map').toBeVisible();
    await expect(page.getByTestId('sheet'), 'the sheet gives way').toBeHidden();
    await expect(page.getByTestId('placement-radius-text'), 'the radius starts at 100 m').toHaveText('100 m · 328 ft');

    const before = await page.getByTestId('placement-pin').boundingBox();
    await page.getByTestId('map-canvas').click({ position: { x: 90, y: 160 } });
    await expect.poll(async () => (await page.getByTestId('placement-pin').boundingBox())?.x, { message: 'a tap on the map moves the pin' }).not.toBe(before?.x);

    await page.getByTestId('placement-radius').fill('300');
    await expect(page.getByTestId('placement-radius-text'), 'the slider sets the radius').toHaveText('300 m · 984 ft');

    await page.getByTestId('placement-save').click();
    await expect(page.getByTestId('placement-error'), 'a name is required').toHaveText('Give the place a name.');

    await page.getByTestId('placement-name').fill('Dog Park');
    await page.getByTestId('placement-kind-park').click();
    await page.getByTestId('placement-save').click();
    await expect(panel, 'saving closes the panel').toHaveCount(0);
    await expect(page.getByText('Dog Park was added to Places.'), 'a toast says so').toBeVisible();
    await expect(page.getByTestId('sheet-selection-header'), 'the new place is selected').toContainText('Dog Park');
  });

  test('[ADD] Esc and Cancel leave placement without adding anything', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page, 'tab-places');

    await page.getByTestId('add-place').click();
    await expect(page.getByTestId('placement-panel')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('placement-panel'), 'Esc cancels').toHaveCount(0);
    await expect(page.getByTestId('placement-pin'), 'the pin is gone').toHaveCount(0);

    await page.getByTestId('add-place').click();
    await page.getByTestId('placement-name').fill('Nowhere');
    await page.getByTestId('placement-cancel').click();
    await expect(page.getByTestId('placement-panel'), 'Cancel cancels').toHaveCount(0);
    await expect(page.getByText('Nowhere'), 'nothing was added').toHaveCount(0);
  });
});
