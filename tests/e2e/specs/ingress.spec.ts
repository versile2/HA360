// Platform tests: what the Ingress hosting decisions of 03 section 5 promise, and no acceptance criterion covers (03 section 7.5).
// Every test runs through the ingress proxy (harness/ingressProxy.mjs). [X-02] and [X-06] are retired by D41 and their numbers are not reused;
// [X-04], [X-09] and [X-12] to [X-14] belong to later slices ([X-14] lives in appendix-c.spec.ts); [X-07] and [X-08] are S7a's; [X-11] is S9b's (G-6).
import { APP_ORIGIN, INGRESS_PREFIX, demo, expect, readHook, settled, test } from '../fixtures.js';

const BASE_HREF = `${INGRESS_PREFIX}/`;

const baseHref = (page: import('@playwright/test').Page) => page.evaluate(() => document.querySelector('base')?.getAttribute('href') ?? null);

test.describe('ingress platform tests', () => {
  test('[X-01] the page, its assets and the _blazor websocket all live under the ingress prefix', async ({ page, request }) => {
    // The upgrade is watched through the browser's own network log: Playwright's WebSocket object does not expose the 101.
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Network.enable');
    const handshakes: { status: number; url: string }[] = [];
    const urls = new Map<string, string>();
    cdp.on('Network.webSocketCreated', (event: { requestId: string; url: string }) => urls.set(event.requestId, event.url));
    cdp.on('Network.webSocketHandshakeResponseReceived', (event: { requestId: string; response: { status: number } }) =>
      handshakes.push({ status: event.response.status, url: urls.get(event.requestId) ?? '' }),
    );

    await demo(page);

    // <base href> is the prefix with one trailing slash, and so is the document's base URI.
    expect(await baseHref(page)).toBe(BASE_HREF);
    expect(new URL(await page.evaluate(() => document.baseURI)).pathname).toBe(BASE_HREF);

    // The assets answer 200 under the prefix. The Blazor script is requested under the name the page itself advertises: .NET 10 fingerprints it
    // (_framework/blazor.web.<hash>.js, smoke item 3), and a browser asks for exactly that name.
    const blazor = await page.evaluate(() => document.querySelector('script[src*="blazor.web"]')?.getAttribute('src') ?? null);
    expect(blazor, 'the page advertises a relative blazor.web script').toMatch(/^(?:\.\/)?_framework\/blazor\.web[\w.-]*\.js$/);
    const origin = new URL(page.url()).origin;
    for (const asset of [
      blazor as string,
      'css/app.css',
      'css/realm-map.css',
      'lib/maplibre-gl/maplibre-gl.css',
      'lib/maplibre-gl/maplibre-gl.mjs',
      '_content/MudX.MudBlazor.Extension/mudx.min.css',
    ]) {
      const response = await request.get(asset);
      expect(response.status(), `GET ${asset}`).toBe(200);
      expect(response.url(), `GET ${asset}`).toBe(new URL(asset, `${origin}${BASE_HREF}`).href);
    }

    // The page links the map's two stylesheets, in the order that lets realm-map.css win over MapLibre's own sheet, and the browser applied both (D74: until
    // S6b fix 1 neither was linked, so every marker was static and the attribution control spanned the screen, and nothing else caught it).
    const sheets = await page.evaluate(() => ({
      linked: [...document.querySelectorAll('link[rel="stylesheet"]')].map((link) => link.getAttribute('href') ?? ''),
      applied: [...document.styleSheets].map((sheet) => ({ href: sheet.href ?? '', rules: sheet.cssRules.length })),
    }));
    const order = ['lib/maplibre-gl/maplibre-gl.css', 'css/tokens.css', 'css/app.css', 'css/realm-map.css'].map((href) => sheets.linked.indexOf(href));
    expect(order, `the stylesheets MapLibre, tokens, app and map are linked (linked: ${sheets.linked.join(', ')})`).not.toContain(-1);
    expect(order, 'they are linked in that order').toEqual([...order].sort((a, b) => a - b));
    for (const href of ['lib/maplibre-gl/maplibre-gl.css', 'css/realm-map.css']) {
      const applied = sheets.applied.find((sheet) => new URL(sheet.href, 'http://x').pathname.endsWith(`/${href}`));
      expect(applied?.rules ?? 0, `${href} is applied and has rules`).toBeGreaterThan(0);
    }

    // The circuit's websocket upgraded with 101 and went through the prefix.
    await expect.poll(() => handshakes.some((h) => new URL(h.url).pathname === `${INGRESS_PREFIX}/_blazor`), { message: 'a _blazor websocket handshake under the prefix' }).toBe(true);
    for (const handshake of handshakes) {
      expect(handshake.status, handshake.url).toBe(101);
      expect(new URL(handshake.url).pathname.startsWith(`${INGRESS_PREFIX}/`), handshake.url).toBe(true);
    }

    // Location -> Driving -> Location keeps the prefix: no link or script escapes to the server root.
    await page.getByTestId('nav-driving').click();
    await expect(page).toHaveURL(`${origin}${INGRESS_PREFIX}/driving`);
    await expect(page.getByTestId('nav-driving')).toHaveAttribute('aria-current', 'page');
    await page.getByTestId('nav-location').click();
    await expect(page).toHaveURL(`${origin}${BASE_HREF}`);
    await expect(page.getByTestId('nav-location')).toHaveAttribute('aria-current', 'page');
    const escapes = await page.evaluate(() =>
      [...document.querySelectorAll('[href]:not(base), [src]')]
        .map((element) => element.getAttribute('href') ?? element.getAttribute('src') ?? '')
        .filter((value) => value.startsWith('/')),
    );
    expect(escapes, 'href or src attributes that start at the server root').toEqual([]);
  });

  test('[X-03] the module scripts are text/javascript and the MapLibre worker starts', async ({ page, request }) => {
    for (const script of ['lib/maplibre-gl/maplibre-gl.mjs', 'lib/maplibre-gl/maplibre-gl-worker.mjs', 'js/realmMap.js']) {
      const response = await request.get(script);
      expect(response.status(), `GET ${script}`).toBe(200);
      expect(response.headers()['content-type'], script).toMatch(/^text\/javascript(?:;|$)/);
    }

    // demo() returns after settled(): the style is loaded, so the worker answered. A worker error would be a console.error or a page error, which
    // the guard fixture turns into a failure of this test.
    await demo(page);
    const camera = await readHook(page, 'camera');
    expect(Array.isArray(camera.center)).toBe(true);
    expect(typeof camera.zoom).toBe('number');
  });

  // D68: informational. The Blazor antiforgery cookie is expected behind Ingress (every interactive page persists a token); what is asserted is that
  // nothing else is set and that the cookie is scoped to the ingress prefix, not that there is no Set-Cookie. The ingress_session cookie belongs to
  // HA's frontend (the proxy plays that part), so it is left out.
  test('[X-05] the only cookie / sets is the antiforgery cookie, scoped to the ingress prefix', async ({ page }) => {
    const response = await page.goto('');
    expect(response?.status()).toBe(200);
    const setCookies = (await response!.headersArray()).filter((header) => header.name.toLowerCase() === 'set-cookie').map((header) => header.value);
    const appCookies = setCookies.filter((cookie) => !cookie.startsWith('ingress_session='));
    test.info().annotations.push({ type: 'info', description: `Set-Cookie on /: ${appCookies.length === 0 ? '(none)' : appCookies.map((c) => c.split(';')[0].split('=')[0]).join(', ')}` });

    for (const cookie of appCookies) {
      const [pair, ...attributes] = cookie.split(';').map((part) => part.trim());
      const name = pair.slice(0, pair.indexOf('='));
      expect(name, `cookie ${name}`).toMatch(/^\.AspNetCore\.Antiforgery\./);
      const cookiePath = attributes.find((attribute) => attribute.toLowerCase().startsWith('path='))?.slice('path='.length);
      expect(cookiePath, `Path of ${name}`).toBe(INGRESS_PREFIX);
    }
  });

  // The proxy always sends a valid X-Ingress-Path, so this test talks to the app directly, as a client that sends none or a bad one would.
  test('[X-10] an invalid or missing X-Ingress-Path is ignored and <base href> stays "/"', async ({ playwright }) => {
    const baseTag = /<base href="([^"]*)"/;
    const invalid = ['no-leading-slash/path', 'x', '/with"quote', "/with'quote", '/with<angle', '/with>angle', '/with space'];
    for (const value of invalid) {
      const direct = await playwright.request.newContext({ baseURL: APP_ORIGIN, extraHTTPHeaders: { 'X-Ingress-Path': value } });
      try {
        const response = await direct.get('/');
        expect(response.status(), `X-Ingress-Path: ${value}`).toBe(200);
        expect((await response.text()).match(baseTag)?.[1], `X-Ingress-Path: ${value}`).toBe('/');
      } finally {
        await direct.dispose();
      }
    }

    // No header at all (PathBase stays empty), and a valid one as the control: the same request through the proxy carries the prefix.
    const bare = await playwright.request.newContext({ baseURL: APP_ORIGIN });
    try {
      const response = await bare.get('/');
      expect(response.status()).toBe(200);
      expect((await response.text()).match(baseTag)?.[1]).toBe('/');
      expect((await (await bare.get('/', { headers: { 'X-Ingress-Path': INGRESS_PREFIX } })).text()).match(baseTag)?.[1]).toBe(BASE_HREF);
    } finally {
      await bare.dispose();
    }
  });

  // 03 section 7.5, R-030 and 01 Appendix C item 10: the map does its per-frame work in the browser. A pan and a wheel zoom send a handful of
  // frames at most (the camera report when the gesture ends), whatever the number of pointer events. The MudXSheet handle drag, the one server-driven
  // gesture of D36 that used to be counted here, no longer exists (D73): the handle is a tap target.
  test('[X-07] a map pan and a zoom send fewer than 6 websocket frames', async ({ page }) => {
    const sent: number[] = [];
    page.on('websocket', (socket) => socket.on('framesent', () => sent.push(Date.now())));

    await demo(page);
    await expect.poll(async () => (await readHook(page, 'sheet')).open, { message: 'window.__realm.sheet().open before the traffic is measured' }).toBe(true);
    // Let the start-up traffic finish: the count below is the traffic of the gestures only.
    let quietSince = Date.now();
    let seen = sent.length;
    await expect
      .poll(
        () => {
          if (sent.length !== seen) {
            seen = sent.length;
            quietSince = Date.now();
          }
          return Date.now() - quietSince >= 800;
        },
        { message: 'the websocket never went quiet after the page loaded', timeout: 15_000 },
      )
      .toBe(true);

    const width = page.viewportSize()?.width ?? 412;
    const x = width / 2;
    const beforeMap = sent.length;
    await page.mouse.move(x, 300);
    await page.mouse.down();
    await page.mouse.move(x - 140, 380, { steps: 30 });
    await page.mouse.up();
    await settled(page);
    const zoomBeforeWheel = (await readHook(page, 'camera')).zoom;
    await page.mouse.move(x, 300);
    await page.mouse.wheel(0, -300);
    // The wheel zooms 40 ms after the event and eases for about 200 ms, and settled() does not see that timer: wait for the zoom itself, so that the frames of
    // the whole wheel gesture are inside the count.
    await expect
      .poll(async () => Math.abs((await readHook(page, 'camera')).zoom - zoomBeforeWheel), { message: `the wheel zoomed the map from ${zoomBeforeWheel}`, timeout: 8_000 })
      .toBeGreaterThan(0.01);
    await settled(page);
    const mapFrames = sent.length - beforeMap;
    test.info().annotations.push({ type: 'info', description: `[X-07] websocket frames sent by the browser during a map pan and zoom: ${mapFrames} (limit: fewer than 6)` });
    expect(mapFrames, 'websocket frames sent by the browser during a map pan (30 pointer moves) and a wheel zoom').toBeLessThan(6);
  });

  // 03 section 4.8, R-02: observeSheet follows `div[mudsheet], [data-testid="sheet"]`. A selector LIST matches every element of either kind, so the union
  // matches two elements by design (the popover and the contract element inside it): what the script needs, and what is asserted, is that each kind
  // exists exactly once, that the contract element is inside the popover, and that the list's first match in document order is the popover (the whole
  // sheet, handle included), which is the element the script measures.
  test('[X-08] exactly one popover div[mudsheet] and one [data-testid="sheet"], nested, and the selector list resolves to the popover', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await demo(page);
    await expect.poll(async () => (await readHook(page, 'sheet')).open, { message: 'window.__realm.sheet().open: MudX has not rendered the sheet' }).toBe(true);

    const found = await page.evaluate(() => {
      const list = 'div[mudsheet], [data-testid="sheet"]';
      const first = document.querySelector(list);
      return {
        popovers: document.querySelectorAll('div[mudsheet]').length,
        contracts: document.querySelectorAll('[data-testid="sheet"]').length,
        union: document.querySelectorAll(list).length,
        nested: document.querySelector('div[mudsheet] [data-testid="sheet"]') !== null,
        firstIsPopover: first !== null && first.matches('div[mudsheet]'),
        firstTag: first === null ? null : `${first.tagName.toLowerCase()}${first.id === '' ? '' : `#${first.id}`}`,
        state: document.querySelector('[data-testid="sheet"]')?.getAttribute('data-state') ?? null,
      };
    });
    const report = JSON.stringify(found);
    expect(found.popovers, `elements matching div[mudsheet]: ${report}`).toBe(1);
    expect(found.contracts, `elements matching [data-testid="sheet"]: ${report}`).toBe(1);
    expect(found.nested, `the contract element is inside the popover: ${report}`).toBe(true);
    expect(found.firstIsPopover, `the first match of the selector list is the popover: ${report}`).toBe(true);
    expect(found.state, `the contract element carries data-state: ${report}`).toMatch(/^(peek|80|panel)$/);
  });

  // [X-11] (03 section 7.5; threat T7 of the security table): in a Demo session `window.__realm` exists and holds the ten hooks of 03 section 4.10 and 01 Appendix B, and nothing
  // else (O-10). The object is frozen and the property is not writable, so a page script cannot swap a hook. This is the Demo half only: no test claims a Live-mode check (R2-028).
  test('[X-11] window.__realm exists in Demo and exposes exactly the ten hooks of 4.10', async ({ page }) => {
    await demo(page);

    const found = await page.evaluate(() => {
      const hooks = (window as Window & { __realm?: Record<string, unknown> }).__realm;
      if (hooks === undefined) return null;
      return {
        names: Object.keys(hooks).sort(),
        notFunctions: Object.entries(hooks).filter(([, value]) => typeof value !== 'function').map(([name]) => name),
        frozen: Object.isFrozen(hooks),
        writable: Object.getOwnPropertyDescriptor(window, '__realm')?.writable ?? null,
      };
    });
    expect(found, 'window.__realm exists in a Demo session').not.toBeNull();
    if (found === null) return;
    expect(found.names, 'the hooks, and no other name').toEqual(['bubbles', 'camera', 'layoutBubbles', 'mapPadding', 'pins', 'settled', 'sheet', 'stats', 'styleId', 'zones']);
    expect(found.notFunctions, 'every hook is a function').toEqual([]);
    expect(found.frozen, 'window.__realm is frozen').toBe(true);
    expect(found.writable, 'the window property is not writable').toBe(false);

    // Each observer answers with data (none throws or returns nothing), and `layoutBubbles` is the pure function of 4.6: an empty scene has no bubbles.
    expect(await readHook(page, 'mapPadding'), 'mapPadding()').toEqual({ top: expect.any(Number), right: expect.any(Number), bottom: expect.any(Number), left: expect.any(Number) });
    expect((await readHook(page, 'camera')).zoom, 'camera().zoom').toEqual(expect.any(Number));
    expect((await readHook(page, 'sheet')).state, 'sheet().state').toMatch(/^(peek|80|panel)$/);
    expect(Array.isArray(await readHook(page, 'pins')), 'pins() is a list').toBe(true);
    expect(Array.isArray(await readHook(page, 'bubbles')), 'bubbles() is a list').toBe(true);
    expect(Array.isArray(await readHook(page, 'zones')), 'zones() is a list').toBe(true);
    expect(await readHook(page, 'styleId'), 'styleId()').toBe('demo-offline');
    expect(await readHook(page, 'stats'), 'stats()').toEqual({ frames: expect.any(Number), setterCalls: expect.any(Number), callbacksSent: expect.any(Number) });
    const empty = await page.evaluate(() => (window as Window & { __realm?: { layoutBubbles?: (...args: unknown[]) => unknown } }).__realm?.layoutBubbles?.({ left: 8, top: 8, right: 404, bottom: 725 }, [], []));
    expect(empty, 'layoutBubbles(rect, [], []) of an empty scene').toEqual({ bubbles: [], onScreen: [], offScreen: [] });
  });
});
