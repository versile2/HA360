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
});
