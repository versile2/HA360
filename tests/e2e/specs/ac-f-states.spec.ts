// Acceptance tests F of 01 section 11 (03 section 7.5): states and accessibility. S8c writes [AC-47a], the focus flow of D45 (01 section 10.2): where the keyboard focus goes when a selection
// is made, the detail is opened, Back is taken and the selection is cleared, and what the sheet's one polite live region says about it (01 section 10.3). The Tab order half of AC-47 and
// the axe scans (AC-44) belong to S15; the Tab trap of the sheet is [X-13] in appendix-c.spec.ts, an expected failure.
//
// What the focus does, in the order of 01 section 10.2:
//   - a row tap collapses the sheet to Peek and hides the row, so the focus goes to the sheet handle ("Showing Cass" is announced);
//   - the handle (or a tap on the selection header, A-1) opens the detail, and the focus goes to the detail's Back button ("Details opened");
//   - Back (the arrow, Esc or the Android Back) returns the focus to the handle ("List collapsed");
//   - clearing the selection (the X, Esc, Back or a tap on the empty map) returns it to the active section tab, and the live region is emptied.
// The focus is moved by the page after the render that shows the new body, so every assertion is `toBeFocused()`, which retries; nothing sleeps. The tests run at 412 x 915 (the phone project):
// the Expanded panel has no handle and no Peek, and its focus rules are the detail's alone.
import type { Locator, Page } from '@playwright/test';

import { castMember, demo, expect, expectHistoryDepth, loadDemoCast, mapReady, proxyControl, readHook, saveShot, tapEmptyMap, test } from '../fixtures.js';

const handle = (page: Page): Locator => page.getByTestId('sheet-handle');
const back = (page: Page): Locator => page.getByTestId('detail-back');
const header = (page: Page): Locator => page.getByTestId('sheet-selection-header');
const announce = (page: Page): Locator => page.getByTestId('sheet-announce');
const activeTab = (page: Page): Locator => page.getByTestId('tab-drivers');

/** What the live region says while the Drivers section shows and nothing has changed since: it starts empty and is emptied again when a selection is cleared. */
const NOTHING_SAID = '';

/** Opens Location with the Drivers list at 80 % (so a row is there to tap) and waits for the map's pins. */
async function openList(page: Page): Promise<void> {
  await demo(page, { sheet: '80' });
  await mapReady(page);
  await expect(page.getByTestId('row-member-jester'), 'the Drivers list shows at 80 %').toBeVisible();
}

/** Selects Cass from the map pin with the sheet at Peek: nothing moves the focus (a pin selects "from where the focus is"). */
async function selectByPin(page: Page): Promise<void> {
  await demo(page);
  await mapReady(page);
  await page.getByTestId('pin-member-jester').click();
  await expect(header(page), 'the selection header shows').toBeVisible();
}

test.describe('[AC-47a] the focus flow of D45', () => {
  test('[AC-47a] a row tap moves the focus to the handle, the handle opens the detail and the focus goes to Back, Back returns it to the handle, clearing returns it to the section tab', async ({ page }) => {
    const jester = castMember(loadDemoCast(), 'jester');
    await openList(page);
    await expect(announce(page), 'the live region starts empty').toHaveText(NOTHING_SAID);

    // A row tap: the sheet collapses to Peek, the row is gone, the focus is on the handle, and the one thing announced is the selection.
    await page.getByTestId('row-member-jester').click();
    await expect(header(page), 'the selection header shows').toBeVisible();
    await expect(handle(page), 'a row tap moves the focus to the sheet handle').toBeFocused();
    await expect(announce(page), 'a selection that collapses the sheet announces only the selection').toHaveText(`Showing ${jester.name}`);

    // The handle, from the keyboard: the detail opens and the focus goes to its Back button.
    await page.keyboard.press('Enter');
    await expect(back(page), "the detail's back arrow shows").toBeVisible();
    await expect(back(page), 'opening the detail with the handle moves the focus to Back').toBeFocused();
    await expect(announce(page), 'the detail is announced').toHaveText('Details opened');

    // Back (the arrow): Peek with the selection kept, the focus on the handle.
    await back(page).click();
    await expect(header(page), 'Back returns to the selection header').toBeVisible();
    await expect(back(page), 'the detail is gone').toHaveCount(0);
    await expect(handle(page), 'Back returns the focus to the handle').toBeFocused();
    await expect(announce(page), 'the sheet coming down is announced').toHaveText('List collapsed');

    // The X clears the selection: the focus goes to the section tab that shows, and the live region is emptied.
    await page.getByTestId('sheet-selection-clear').click();
    await expect(header(page), 'the selection is cleared').toHaveCount(0);
    await expect(activeTab(page), 'the Drivers tab is the selected one').toHaveAttribute('aria-selected', 'true');
    await expect(activeTab(page), 'clearing the selection returns the focus to the active section tab').toBeFocused();
    await expect(announce(page), 'the live region is emptied').toHaveText(NOTHING_SAID);
  });

  // A-1 (D46): a tap on the selection header is the handle's tap, so the detail it opens takes the focus to Back as well. Esc is a Back step: first the detail (the focus goes to the handle), then the
  // selection (the focus goes to the section tab).
  test('[AC-47a] a tap on the selection header moves the focus to Back like the handle, and Esc then returns it to the handle and to the section tab', async ({ page }) => {
    await selectByPin(page);

    await header(page).locator('.realm-row-name').click();
    await expect(back(page), "the detail's back arrow shows").toBeVisible();
    await expect(back(page), 'a tap on the header moves the focus to Back, like the handle').toBeFocused();

    await page.keyboard.press('Escape');
    await expect(header(page), 'Esc from the detail returns to the selection header').toBeVisible();
    await expect(back(page), 'the detail is gone').toHaveCount(0);
    await expect(handle(page), 'Esc from the detail returns the focus to the handle').toBeFocused();

    await page.keyboard.press('Escape');
    await expect(header(page), 'Esc at Peek clears the selection').toHaveCount(0);
    await expect(activeTab(page), 'Esc that clears the selection returns the focus to the section tab').toBeFocused();
  });

  // Clearing is the same for every way of doing it: a tap on the empty map and the Android Back both end with the focus on the section tab.
  test('[AC-47a] clearing the selection with a tap on the empty map or with Back returns the focus to the active section tab', async ({ page }) => {
    await selectByPin(page);
    await expectHistoryDepth(page, 1, 'with a selection at Peek');

    await tapEmptyMap(page);
    await expect(header(page), 'the empty map cleared the selection').toHaveCount(0);
    await expect(activeTab(page), 'a tap on the empty map returns the focus to the section tab').toBeFocused();

    await page.getByTestId('pin-member-jester').click();
    await expect(header(page), 'Cass is selected again').toBeVisible();
    await expectHistoryDepth(page, 1, 'with a selection at Peek again');
    await page.goBack();
    await expect(header(page), 'Back cleared the selection').toHaveCount(0);
    await expect(activeTab(page), 'Back that clears the selection returns the focus to the section tab').toBeFocused();
  });

  // The way out of the sheet by keyboard, as 01 section 10.2 documents it (R-032): MudXSheet traps Tab inside the sheet, so a keyboard user leaves it with Esc and then Shift+Tab from the handle.
  // Whether the handle itself is inside the trap is an inference from MudX's markup that nobody could run (03 section 3.5), so this test records where Shift+Tab lands instead of asserting the
  // destination: the sheet is still there after Esc (Esc never closes it), and Shift+Tab from the handle moves the focus off the handle. [X-13] is the test of the trap itself.
  test('[AC-47a] the documented exit from the sheet (Esc, then Shift+Tab from the handle): Esc never closes the sheet, and where the focus lands is recorded', async ({ page }) => {
    await demo(page);
    await mapReady(page);
    await handle(page).focus();
    await expect(handle(page), 'the handle has the focus').toBeFocused();

    await page.keyboard.press('Escape');
    expect((await readHook(page, 'sheet')).open, 'Esc never closes the sheet itself').toBe(true);
    await expect(handle(page), 'Esc at Peek with nothing selected leaves the focus on the handle').toBeFocused();

    await page.keyboard.press('Shift+Tab');
    await expect(handle(page), 'Shift+Tab from the handle moves the focus away from it').not.toBeFocused();
    const landed = await page.evaluate(() => {
      const element = document.activeElement;
      if (element === null) return 'nothing';
      const testId = element.getAttribute('data-testid');
      const insideSheet = element.closest('[mudsheet], [data-testid="sheet"]') !== null;
      return `${element.tagName.toLowerCase()}${testId === null ? '' : `[data-testid=${testId}]`} (${insideSheet ? 'inside' : 'outside'} the sheet)`;
    });
    test.info().annotations.push({ type: 'info', description: `[AC-47a] Shift+Tab from the handle landed on ${landed}` });
  });
});

// ==== S15b: the connection states ([AC-49a] E2E half, SC16, [X-04]) =====================================================================================
// [AC-49b] (skeleton rows, "Still summoning the court…" after 8 s, the error and Retry after 20 s) cannot be reached in Demo, which has its data at once; it is covered by
// FirstDataWatchTests and the component tests (dotnet). Here: the Demo `ha-down` banner, its gallery scene, and the circuit reconnect banner.

const HA_DOWN_TEXT = "The royal messengers can't reach Home Assistant. Retrying…";

test.describe('[AC-49a] the Home Assistant banner under ?variant=ha-down', () => {
  test('[AC-49a] the banner shows its copy as a polite status within 5 s, stays clear of the gear, and clears with a toast when Home Assistant is restored', async ({ page }) => {
    await demo(page, { variant: 'ha-down' });
    const banner = page.getByTestId('banner-ha');
    await expect(banner, 'the banner shows').toBeVisible({ timeout: 5000 });
    await expect(banner).toContainText(HA_DOWN_TEXT);
    await expect(banner, 'a polite status region').toHaveAttribute('role', 'status');

    const gear = await page.getByTestId('btn-settings').boundingBox();
    const box = await banner.boundingBox();
    expect(gear, 'the gear has a box').not.toBeNull();
    expect(box, 'the banner has a box').not.toBeNull();
    const overlaps = box!.x < gear!.x + gear!.width && box!.x + box!.width > gear!.x && box!.y < gear!.y + gear!.height && box!.y + box!.height > gear!.y;
    expect(overlaps, 'the banner does not cover the gear').toBe(false);

    await banner.click();
    await expect(banner, 'the banner clears when Home Assistant is back').toHaveCount(0);
  });

  test('[GAL] SC16-variant-ha-down', { tag: ['@phone', '@unfolded'] }, async ({ page }, testInfo) => {
    await demo(page, { variant: 'ha-down' });
    await expect(page.getByTestId('banner-ha')).toBeVisible({ timeout: 5000 });
    await saveShot(page, testInfo, 'SC16-variant-ha-down');
  });
});

test.describe('[X-04] the circuit reconnect banner', () => {
  test('[X-04] after the sockets drop the static banner "Reconnecting to the court…" shows and clears, and the selection is unchanged', async ({ page, request }) => {
    await demo(page);
    await mapReady(page);
    await page.getByTestId('pin-member-jester').click();
    await expect(header(page), 'the selection header shows').toBeVisible();

    await proxyControl(request, 'drop-websockets');
    const reconnect = page.getByTestId('banner-reconnect');
    await expect(reconnect, 'the reconnect banner shows').toBeVisible({ timeout: 15000 });
    await expect(reconnect).toContainText('Reconnecting to the court…');
    await expect(reconnect, 'the banner clears when the circuit is back').toBeHidden({ timeout: 60000 });
    await expect(header(page), 'the selection survived the reconnect').toBeVisible();
  });
});
