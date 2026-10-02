// Platform tests: what the Ingress hosting decisions of 03 section 5 promise, and no acceptance criterion covers (03 section 7.5).
// Every test runs through the ingress proxy (harness/ingressProxy.mjs). [X-02] and [X-06] are retired by D41 and their numbers are not reused;
// [X-04], [X-07] to [X-09], [X-11] to [X-14] belong to later slices.
import { APP_ORIGIN, INGRESS_PREFIX, demo, expect, readHook, test } from '../fixtures.js';

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
    for (const asset of [blazor as string, 'css/app.css', 'lib/maplibre-gl/maplibre-gl.mjs', '_content/MudX.MudBlazor.Extension/mudx.min.css']) {
      const response = await request.get(asset);
      expect(response.status(), `GET ${asset}`).toBe(200);
      expect(response.url(), `GET ${asset}`).toBe(new URL(asset, `${origin}${BASE_HREF}`).href);
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
});
