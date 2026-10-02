// Platform tests for the open questions of 01 Appendix C that nobody could run before MudX was in the page (03 section 7.5).
// S7a creates this file with [X-14]; [X-12] (z-index order and the non-modal overlay) belongs to S10b and [X-13] (the focus trap, a declared expected
// failure) to S8c, which append to it.
import { demo, expect, readHook, settled, test } from '../fixtures.js';

test.describe('appendix C platform tests', () => {
  // 01 Appendix C item 7. MudXSheet renders a MudOverlay with LockScroll=true even when it is non-modal, and MudBlazor's scroll lock adds the class
  // scroll-locked to <body> and, when the page has a scrollbar, a padding-right that makes up for it. Location has no scrollbar (html and body are
  // overflow:hidden), so any padding would shift the whole screen sideways for nothing: css/realm-sheet.css sets it back to 0 on the Location page.
  // The map must also keep receiving drags and the wheel while the sheet is open. MudBlazor's lock is only that class on <body> (overflow:hidden); neither
  // MudBlazor's scroll manager nor MudX 9.5.0 registers a wheel or touch listener, and the non-modal overlay is pointer-events:none, so nothing is expected
  // to swallow a gesture; the pan and the wheel below prove it.
  test('[X-14] the page scroll lock of the open sheet adds no padding to <body>, and the map still pans and zooms', async ({ page }) => {
    await demo(page);
    await expect.poll(async () => (await readHook(page, 'sheet')).open, { message: 'window.__realm.sheet().open: the sheet must be open before the scroll lock can be judged' }).toBe(true);

    const lock = await page.evaluate(() => {
      const body = getComputedStyle(document.body);
      const html = getComputedStyle(document.documentElement);
      return {
        bodyClass: document.body.className,
        bodyPaddingRight: body.paddingRight,
        bodyInlineStyle: document.body.getAttribute('style'),
        bodyOverflow: body.overflow,
        htmlOverflow: html.overflow,
        innerWidth: window.innerWidth,
        clientWidth: document.documentElement.clientWidth,
        overlays: [...document.querySelectorAll('.mud-overlay')].map((overlay) => {
          const style = getComputedStyle(overlay);
          return { className: overlay.className, pointerEvents: style.pointerEvents, background: style.backgroundColor };
        }),
      };
    });
    test.info().annotations.push({ type: 'info', description: `[X-14] body class "${lock.bodyClass}", padding-right ${lock.bodyPaddingRight}, overlays ${JSON.stringify(lock.overlays)}` });

    expect(lock.bodyPaddingRight, `body padding-right under the scroll lock (body class "${lock.bodyClass}", inline style ${String(lock.bodyInlineStyle)})`).toBe('0px');
    expect(lock.bodyInlineStyle ?? '', 'no inline padding-right was written on <body>').not.toMatch(/padding-right\s*:\s*[1-9]/);
    expect(lock.clientWidth, 'the layout width equals the window width: no scrollbar and no shift').toBe(lock.innerWidth);
    expect(lock.htmlOverflow, 'html is overflow:hidden on Location (the lock is harmless)').toBe('hidden');

    // A drag on the map, above the sheet, moves the camera; the wheel zooms it.
    const { width } = page.viewportSize() ?? { width: 412 };
    const x = width / 2;
    const before = await readHook(page, 'camera');
    await page.mouse.move(x, 300);
    await page.mouse.down();
    await page.mouse.move(x - 140, 380, { steps: 20 });
    await page.mouse.up();
    await settled(page);
    const panned = await readHook(page, 'camera');
    const moved = Math.hypot(panned.center[0] - before.center[0], panned.center[1] - before.center[1]);
    expect(moved, `a 140 px drag on the map moved the camera centre by ${moved} degrees (before ${JSON.stringify(before.center)}, after ${JSON.stringify(panned.center)}): the scroll lock or an overlay swallowed the drag`).toBeGreaterThan(1e-4);

    // MapLibre does not zoom at the wheel event: its scroll-zoom handler waits 40 ms to tell a wheel from a trackpad, then eases for about 200 ms, and
    // map.isMoving() (so settled()) is still false until that timer has fired. The zoom is therefore polled for, not read once after settled(); the
    // assertion is the same, a zoom change above 0.01, and a lock that swallowed the wheel still fails it after the timeout.
    await page.mouse.move(x, 300);
    await page.mouse.wheel(0, -300);
    let zoomed = panned;
    await expect
      .poll(
        async () => {
          zoomed = await readHook(page, 'camera');
          return Math.abs(zoomed.zoom - panned.zoom);
        },
        { message: `the wheel (deltaY -300 at x ${x}, y 300) did not change the zoom from ${panned.zoom} within the timeout: the scroll lock swallowed the wheel`, timeout: 8_000 },
      )
      .toBeGreaterThan(0.01);
    expect(zoomed.zoom, `a wheel turned forward (deltaY -300) zooms in: from ${panned.zoom} to ${zoomed.zoom}`).toBeGreaterThan(panned.zoom);
    await settled(page);
  });

  // 01 Appendix C item 4, 03 section 3.5, R-035. The z-index order, low to high: the floating controls (10), the MudX sheet (a MudPopover: MudBlazor's popover layer is
  // --mud-zindex-popover, 1200, plus one for the popover itself, so 1201), the bottom navigation (1210, css/app.css), MudBlazor's dialogs (--mud-zindex-dialog, 1400). The
  // numbers are read from the page, and the order is also proved by what a tap would reach: the navigation bar hides the lower 64 px of the sheet, and a dialog hides the bar.
  test('[X-12] the sheet is below the navigation bar, which is below the dialog layer', async ({ page }) => {
    await demo(page);
    await expect.poll(async () => (await readHook(page, 'sheet')).open, { message: 'window.__realm.sheet().open: the sheet must be open before its layer can be judged' }).toBe(true);

    const layers = await page.evaluate(() => {
      const zIndexOf = (selector: string): number => {
        const element = document.querySelector(selector);
        return element === null ? Number.NaN : Number.parseInt(getComputedStyle(element).zIndex, 10);
      };
      const rootStyle = getComputedStyle(document.documentElement);
      const nav = document.querySelector('.realm-nav');
      const bar = nav === null ? null : nav.getBoundingClientRect();
      const hit = bar === null ? null : document.elementFromPoint(bar.left + bar.width / 2, bar.top + bar.height / 2);
      return {
        sheet: zIndexOf('div[mudsheet]'),
        nav: zIndexOf('.realm-nav'),
        popoverLayer: Number.parseInt(rootStyle.getPropertyValue('--mud-zindex-popover'), 10),
        dialogLayer: Number.parseInt(rootStyle.getPropertyValue('--mud-zindex-dialog'), 10),
        navigationReceivesTheTap: hit !== null && hit.closest('.realm-nav') !== null,
      };
    });
    test.info().annotations.push({ type: 'info', description: `[X-12] z-index of the sheet ${layers.sheet}, the navigation ${layers.nav}; MudBlazor layers: popover ${layers.popoverLayer}, dialog ${layers.dialogLayer}` });

    expect(layers.nav, 'the navigation bar is 1210 (css/app.css)').toBe(1210);
    expect(layers.sheet, `the sheet is a popover layer, at least --mud-zindex-popover (${layers.popoverLayer})`).toBeGreaterThanOrEqual(layers.popoverLayer);
    expect(layers.sheet, 'the sheet is below the navigation bar').toBeLessThan(layers.nav);
    expect(layers.dialogLayer, 'MudBlazor dialogs are above the navigation bar').toBeGreaterThan(layers.nav);
    expect(layers.navigationReceivesTheTap, 'the centre of the navigation bar hits the bar, not the sheet that extends under it').toBe(true);
  });

  test('[X-12] a dialog covers the navigation bar: its layer is above the bar and the bar does not receive the tap', async ({ page }) => {
    // The popups grow and fade in; with reduced motion they are instant.
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('stat-speeding'), 'the report has loaded').toBeVisible();
    const dialog = page.getByRole('dialog');
    // The page is prerendered, so a tap before the circuit is up does nothing; it is repeated until the popup is there, and never once it is open (the scrim would be in the way).
    await expect(async () => {
      if ((await dialog.count()) === 0) await page.getByTestId('stat-speeding').click({ timeout: 3_000 });
      await expect(dialog.getByTestId('popup-speeding'), 'the Speeding popup is open').toBeVisible({ timeout: 1_500 });
    }).toPass({ timeout: 20_000 });

    const layers = await page.evaluate(() => {
      const zIndexOf = (selector: string): number => {
        const element = document.querySelector(selector);
        return element === null ? Number.NaN : Number.parseInt(getComputedStyle(element).zIndex, 10);
      };
      const nav = document.querySelector('.realm-nav');
      const bar = nav === null ? null : nav.getBoundingClientRect();
      const hit = bar === null ? null : document.elementFromPoint(bar.left + bar.width / 2, bar.top + bar.height / 2);
      return {
        nav: zIndexOf('.realm-nav'),
        dialog: zIndexOf('.mud-dialog-container'),
        navigationReceivesTheTap: hit !== null && hit.closest('.realm-nav') !== null,
      };
    });
    test.info().annotations.push({ type: 'info', description: `[X-12] z-index of the navigation ${layers.nav}, of the dialog container ${layers.dialog}` });

    expect(layers.dialog, 'the dialog container has a z-index (.mud-dialog-container)').not.toBeNaN();
    expect(layers.dialog, 'the dialog layer is above the navigation bar').toBeGreaterThan(layers.nav);
    expect(layers.navigationReceivesTheTap, 'the centre of the navigation bar hits the dialog layer, not the bar').toBe(false);
  });

  // R-035: MudXSheet always renders a non-modal MudOverlay (LockScroll is what it is for). Non-modal means pointer-events: none on the overlay, and it must never be given a
  // background (neither a scrim child nor a colour), or it would grey the map and swallow every gesture.
  test('[X-12] the sheet\'s non-modal overlay computes pointer-events: none and has no background', async ({ page }) => {
    await demo(page);
    await expect.poll(async () => (await readHook(page, 'sheet')).open, { message: 'window.__realm.sheet().open: the sheet must be open before its overlay can be judged' }).toBe(true);

    const overlays = await page.evaluate(() => {
      // A point on the map, above the sheet at Peek: nothing of the overlay may be the element that receives it.
      const hit = document.elementFromPoint(window.innerWidth / 2, 120);
      return {
        list: [...document.querySelectorAll('.mud-overlay')].map((overlay) => {
          const style = getComputedStyle(overlay);
          const scrim = overlay.querySelector('.mud-overlay-scrim');
          return {
            className: overlay.className,
            pointerEvents: style.pointerEvents,
            background: style.backgroundColor,
            backgroundImage: style.backgroundImage,
            scrim: scrim === null ? null : { className: scrim.className, background: getComputedStyle(scrim).backgroundColor },
          };
        }),
        mapPointHitsAnOverlay: hit !== null && hit.closest('.mud-overlay') !== null,
      };
    });
    test.info().annotations.push({ type: 'info', description: `[X-12] overlays ${JSON.stringify(overlays.list)}` });

    expect(overlays.list.length, 'the open sheet renders its non-modal MudOverlay (.mud-overlay)').toBeGreaterThan(0);
    for (const overlay of overlays.list) {
      expect(overlay.pointerEvents, `overlay "${overlay.className}" computes pointer-events`).toBe('none');
      expect(overlay.background, `overlay "${overlay.className}" has a transparent background colour`).toBe('rgba(0, 0, 0, 0)');
      expect(overlay.backgroundImage, `overlay "${overlay.className}" has no background image`).toBe('none');
      expect(overlay.scrim, `overlay "${overlay.className}" has no scrim child (no dark or light background)`).toBeNull();
    }
    expect(overlays.mapPointHitsAnOverlay, 'a point on the map above the sheet is not caught by an overlay').toBe(false);
  });
});
