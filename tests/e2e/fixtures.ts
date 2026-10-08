// Fixtures of the E2E suite (03 section 7.5, 04 card S6a). Specs import `test` and `expect` from here, never from '@playwright/test':
//   - `demo(page, opts)` opens the Demo app through the ingress proxy with the URL parameters of 01 Appendix B, waits until the circuit is interactive
//     (every path) and, on Location, until the map is settled;
//   - the GUARD fixture wraps every `page`: a test fails on a page error, on any console.error (which also catches the script's OnError reports,
//     realmMap.js logs them as `[realmMap] ...`), and on any request or websocket whose host is not the proxy's (so no fonts or tiles leave in Demo).
//     A test that expects an error says so with `test.use({ allowConsoleErrors: [{ pattern, reason }] })`; every entry needs a reason.
//
// Everything is relative to the baseURL, which is the proxy URL WITH a trailing slash (playwright.config.ts): demo(page) opens
// `<proxy>/api/hassio_ingress/<token>/?demo=1&...`, and a leading slash reaches the proxy's own origin (`/__proxy/health`).

import { expect, test as base, type APIRequestContext, type Page } from '@playwright/test';

import { APP_HOST, APP_PORT, CONTROL_PREFIX, INGRESS_PREFIX, PROXY_HOST, PROXY_PORT } from './harness/ingressProxy.mjs';
// Imports of the S6b additions at the end of this file (the Demo cast, mapReady, geometry helpers, saveShot).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { TestInfo } from '@playwright/test';

export { expect, INGRESS_PREFIX };

/** Where the browser talks to: HA's stand-in. */
export const PROXY_ORIGIN = `http://${PROXY_HOST}:${PROXY_PORT}`;
/** The app itself, behind the proxy. Only tests of what the proxy hides (a missing or invalid X-Ingress-Path) talk to it directly. */
export const APP_ORIGIN = `http://${APP_HOST}:${APP_PORT}`;

// ---- window.__realm (03 section 4.10, 01 Appendix B): the ten read-only hooks of a Demo session ----------------------------------------------

export interface Padding { top: number; right: number; bottom: number; left: number }
export interface CameraState { center: [number, number]; zoom: number; bounds: [[number, number], [number, number]]; animated: boolean; lastDurationMs: number }
export interface SheetInfo { state: 'peek' | '80' | 'panel'; heightPx: number; topPx: number; open: boolean }
export interface PinInfo { id: string; kind: 'member' | 'vehicle'; x: number; y: number; anchorX: number; anchorY: number; ring: string; dashed: boolean; badge: string | null; fanned: boolean; sizePx: number }
export interface BubbleInfo { id: string; ids: string[]; x: number; y: number; angleDeg: number; cluster: number }
export interface ZoneInfo { id: string; drawn: boolean; occupied: boolean; fillAlpha: number; dashed: boolean; radiusPx: number }
export interface HookStats { frames: number; setterCalls: number; callbacksSent: number }

export interface RealmHooks {
  mapPadding(): Padding;
  camera(): CameraState;
  sheet(): SheetInfo;
  pins(): PinInfo[];
  bubbles(): BubbleInfo[];
  zones(): ZoneInfo[];
  styleId(): string;
  layoutBubbles: (...args: unknown[]) => unknown;
  settled(): Promise<void>;
  stats(): HookStats;
}

/** The hooks that take no argument and return plain data (what `readHook` can serialise). */
export type DataHook = 'mapPadding' | 'camera' | 'sheet' | 'pins' | 'bubbles' | 'zones' | 'styleId' | 'stats';

type RealmWindow = Window & { __realm?: Record<string, (() => unknown) | undefined> };

/** Resolves when the style is loaded, no camera animation runs and the last payloads have rendered. Every geometry assertion and screenshot follows it; there are no sleeps. */
export async function settled(page: Page): Promise<void> {
  await page.evaluate(() => (window as RealmWindow).__realm?.settled?.());
}

/** One hook call, evaluated in the page: `await readHook(page, 'camera')`. */
export async function readHook<K extends DataHook>(page: Page, name: K): Promise<ReturnType<RealmHooks[K]>> {
  const value = await page.evaluate((hook) => (window as RealmWindow).__realm?.[hook]?.(), name);
  return value as ReturnType<RealmHooks[K]>;
}

// ---- demo(): the seven URL parameters of 01 Appendix B ---------------------------------------------------------------------------------------

export interface DemoOptions {
  /** Where to open, relative to the ingress prefix: '' is Location (the default), 'driving' and 'driving/jester' the other routes. Never a leading slash. */
  path?: string;
  /** `now`: an ISO 8601 instant WITH an offset or Z, overriding the frozen clock (the default is Wed 2026-09-30 21:25 CDT). */
  now?: string;
  /** `style`: a map style id; the hidden `demo-offline` unless given (no tile request can leave the page). */
  style?: string;
  /** `sheet`: the initial sheet state. */
  sheet?: 'peek' | '80';
  /** `layout`: the layout override. */
  layout?: 'auto' | 'sheet' | 'panel';
  /** `variant`: one variant name or several, applied left to right (02 section 9.5). */
  variant?: string | string[];
  /** `week`: 0..3, an ordinary route query. */
  week?: number;
  /**
   * Open the Demo with the roster it starts with (four people, the wagon, two entries under Not tracked) instead of the full cast. The acceptance suite describes the
   * seven roles of the cast on the map, so `demo()` adds the `full-cast` variant (02 section 9.5) unless this is true; only Settings, "Who's on the map" asks for the default.
   */
  defaultRoster?: boolean;
  /** Wait for `window.__realm` and `settled()`. The default is true on Location, the only page with a map (and so with hooks). */
  hooks?: boolean;
}

/**
 * The relative URL `demo()` opens: `<path>?demo=1&now=..&style=..&sheet=..&layout=..&variant=..&week=..`, the seven parameters in the order of
 * 03 Appendix A. Every value is percent-encoded (CR2-008): an unencoded `+05:30` offset arrives at the server as a space and the `now` override would
 * be ignored silently. Variant names are encoded one by one and joined with a literal comma.
 */
export function demoUrl(opts: DemoOptions = {}): string {
  const path = opts.path ?? '';
  if (path.startsWith('/')) throw new Error(`demo(): path '${path}' starts with a slash and would escape the ingress prefix; use a relative path`);
  const pairs: string[] = ['demo=1'];
  if (opts.now !== undefined) pairs.push(`now=${encodeURIComponent(opts.now)}`);
  pairs.push(`style=${encodeURIComponent(opts.style ?? 'demo-offline')}`);
  if (opts.sheet !== undefined) pairs.push(`sheet=${encodeURIComponent(opts.sheet)}`);
  if (opts.layout !== undefined) pairs.push(`layout=${encodeURIComponent(opts.layout)}`);
  if (opts.variant !== undefined) {
    const names = Array.isArray(opts.variant) ? opts.variant : [opts.variant];
    pairs.push(`variant=${names.map(encodeURIComponent).join(',')}`);
  }
  if (opts.week !== undefined) pairs.push(`week=${encodeURIComponent(String(opts.week))}`);
  return `${path}?${pairs.join('&')}`;
}

/**
 * The element that proves the circuit is interactive: `<script data-mudx-js>` in `<head>`. MudXProvider (MainLayout renders it on every page) creates it from
 * `OnAfterRenderAsync(firstRender)` through its `mudxProvider.js` (`injectJsFromFile`), and so does `ensureMudX` of realmShell.js on Location. Neither exists in the
 * prerendered HTML: `App.razor` only declares `link[data-mudx-css]`, and a static prerender never runs `OnAfterRender` or any JS interop. Blazor sends the first
 * render batch (which replaces the prerendered DOM) before the JS call that creates the element, over the one ordered circuit, so once the element is there the
 * prerendered page is gone and what a locator finds is the circuit's own DOM. (`data-layout`, set by realmShell.js, would not do: only the Location page starts that
 * script, so Driving never gets it.)
 */
export const INTERACTIVE_SELECTOR = 'script[data-mudx-js]';

/**
 * Waits until the page has been taken over by its circuit. A locator that matched the prerendered DOM can be detached a moment later, when the circuit replaces it,
 * and `boundingBox()` then returns null on an element that `toBeVisible()` had just seen (the AC-35 failure of CI run d091ad2).
 */
export async function waitForInteractive(page: Page, url: string): Promise<void> {
  await page
    .waitForFunction((selector) => document.querySelector(selector) !== null, INTERACTIVE_SELECTOR, { timeout: 30_000 })
    .catch((error: unknown) => {
      throw new Error(
        `the circuit did not become interactive within 30 s of opening ${url} (no ${INTERACTIVE_SELECTOR} in <head>: MudXProvider adds it from its first interactive render, ` +
          `so the page is still the prerendered one, or the websocket never connected): ${String(error)}`,
      );
    });
}

/** The options with the `full-cast` variant added, unless the test asked for the roster the Demo starts with. */
function withFullCast(opts: DemoOptions): DemoOptions {
  if (opts.defaultRoster === true) return opts;
  const names = opts.variant === undefined ? [] : Array.isArray(opts.variant) ? opts.variant : [opts.variant];
  return names.includes('full-cast') ? opts : { ...opts, variant: [...names, 'full-cast'] };
}

/**
 * Opens the Demo app through the proxy and waits until the page is interactive, on every path, and then (on Location) until the map is settled.
 *
 * The app prerenders (Interactive Server with prerendering): the HTML that `goto` returns is static, and when the circuit starts Blazor replaces it. A test that
 * touches the page before that moment can hold an element that is detached a moment later, so `demo()` returns only after the circuit has rendered once.
 *
 * Re-open with demo(), never with the navigation links, when a test needs a fresh circuit (CR2-024): the Demo-only parameters are read once per
 * circuit, the bottom-nav links drop the query string, and so a `page.reload()` after an in-app navigation comes back without `variant`, `now` and
 * `style` (and, in Live with `?demo=1`, as a Live session).
 */
export async function demo(page: Page, given: DemoOptions = {}): Promise<void> {
  const opts = withFullCast(given);
  await page.goto(demoUrl(opts));
  await waitForInteractive(page, demoUrl(opts));
  if (opts.hooks ?? (opts.path ?? '') === '') {
    await page
      .waitForFunction(() => (window as RealmWindow).__realm !== undefined, undefined, { timeout: 30_000 })
      .catch((error: unknown) => {
        throw new Error(`window.__realm did not appear within 30 s of opening ${demoUrl(opts)} (no Demo session, a map that failed to start, or WebGL2 missing): ${String(error)}`);
      });
    await settled(page);
  }
}

// ---- the proxy's control endpoints ---------------------------------------------------------------------------------------------------------

/**
 * `POST /__proxy/drop-websockets` closes every upgraded socket (to test the reconnect); `POST /__proxy/expire-session?upgrades=N` makes the next N
 * upgrades answer 401 (the 401 window of research ha-addon-ingress 2.5). Returns the proxy's JSON answer.
 */
export async function proxyControl(request: APIRequestContext, action: 'drop-websockets' | 'expire-session', query = ''): Promise<unknown> {
  const response = await request.post(`${CONTROL_PREFIX}${action}${query}`);
  expect(response.ok(), `the proxy answered ${response.status()} to ${action}`).toBe(true);
  return response.json();
}

/**
 * The query that limits `drop-websockets` to this page's own circuit. The proxy is shared by every test of every worker, so an unscoped drop also kills the circuits of tests that run
 * at the same time in the other worker (the AC-44b "WebSocket closed with status code: 1006" flake).
 */
export async function ownSessionQuery(page: Page): Promise<string> {
  const cookies = await page.context().cookies(page.url());
  const session = cookies.find((cookie) => cookie.name === 'ingress_session');
  if (session === undefined) throw new Error('the page has no ingress_session cookie, so its sockets cannot be told from the others');
  return `?session=${encodeURIComponent(session.value)}`;
}

// ---- the guard fixture -----------------------------------------------------------------------------------------------------------------------

export interface GuardAllow {
  /** Matched against the console message text. */
  pattern: RegExp;
  /** When given, also matched against the URL the message came from (a failed request logs the resource URL as its location). */
  url?: RegExp;
  /** Why this output is harmless. An entry without a reason is not accepted. */
  reason: string;
}

/**
 * Console errors that never fail a test. Keep this list short and every entry justified; a test that expects an error of its own adds it with
 * `test.use({ allowConsoleErrors: [...] })`, for that file or describe block only.
 */
export const ALLOWED_CONSOLE_ERRORS: readonly GuardAllow[] = [
  {
    pattern: /Failed to load resource/,
    url: /\/favicon\.ico$/,
    reason: 'The browser asks for /favicon.ico on its own; no page of ours links an icon and the ingress proxy answers 404 outside its prefix, like HA does.',
  },
];

function checkAllowList(entries: readonly GuardAllow[]): void {
  for (const entry of entries) {
    if (entry.reason.trim() === '') throw new Error(`allowConsoleErrors entry ${String(entry.pattern)} needs a reason`);
  }
}

interface Options {
  /** Extra console.error output this file or describe block expects, each with a reason. */
  allowConsoleErrors: GuardAllow[];
}

export const test = base.extend<Options>({
  allowConsoleErrors: [[], { option: true }],

  // Every test that opens a page is guarded: the page fixture is wrapped, so a test that only uses `request` never launches a browser.
  page: async ({ page, allowConsoleErrors }, use) => {
    const allowed = [...ALLOWED_CONSOLE_ERRORS, ...allowConsoleErrors];
    checkAllowList(allowed);
    const proxyHost = `${PROXY_HOST}:${PROXY_PORT}`;
    const problems: string[] = [];

    page.on('pageerror', (error) => {
      // An entry without a url filter also covers an uncaught page error with a matching message (a socket dropped on purpose makes SignalR throw one).
      if (allowed.some((entry) => entry.url === undefined && entry.pattern.test(error.message))) return;
      problems.push(`page error: ${error.message}`);
    });
    page.on('console', (message) => {
      if (message.type() !== 'error') return;
      const text = message.text();
      const url = message.location().url;
      if (allowed.some((entry) => entry.pattern.test(text) && (entry.url === undefined || entry.url.test(url)))) return;
      problems.push(`console.error: ${text}${url === '' ? '' : ` (${url})`}`);
    });
    page.on('request', (request) => {
      const url = new URL(request.url());
      if ((url.protocol === 'http:' || url.protocol === 'https:') && url.host !== proxyHost) problems.push(`request outside the proxy: ${request.method()} ${request.url()}`);
    });
    page.on('websocket', (socket) => {
      if (new URL(socket.url()).host !== proxyHost) problems.push(`websocket outside the proxy: ${socket.url()}`);
    });

    await use(page);

    expect(problems, 'guard fixture: the page logged an error or reached outside the ingress proxy').toEqual([]);
  },
});

// ==== S6b additions (additive; append-only as a block): the Demo cast, mapReady, geometry helpers, saveShot =======================================

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

// ---- the Demo cast (02 section 9.2, 03 section 8.1 rule 2) -----------------------------------------------------------------------------------

/** One member of `tests/e2e/fixtures/demo-cast.json`: the shape `DemoCastExporter.ToJson` writes (camelCase). */
export interface CastMember {
  id: string;
  name: string;
  lore: string;
  color: string;
  kind: 'live' | 'static';
  sortOrder: number;
  personUserId: string | null;
  phoneCapable: boolean;
  address: string | null;
  staticLabel: string | null;
}
export interface CastVehicle { id: string; name: string; lore: string; glyph: 'pickup' | 'car'; sortOrder: number }
/** One entry of the roster a Demo session starts with (D113): `group` is `people`, `vehicles` or `notTracked`. */
export interface CastRosterEntry { entityId: string; kind: 'person' | 'tracker'; group: 'people' | 'vehicles' | 'notTracked'; name: string; title: string | null; source: string; autoMoved: boolean }
/** `drawn` is false only for the arrival zone (radius above the 5 km maximum), which is never drawn or listed. */
export interface CastPlace { id: string; zoneName: string; name: string; subtitle: string; kind: string; lat: number; lon: number; radiusM: number; drawn: boolean }
export interface DemoCastFile { members: CastMember[]; vehicles: CastVehicle[]; roster: CastRosterEntry[]; places: CastPlace[] }

let demoCast: DemoCastFile | undefined;

/**
 * The fictional cast, read from `tests/e2e/fixtures/demo-cast.json`, which the e2e job writes with `export-demo-cast` before Playwright starts. Read
 * lazily (never at import time), so `playwright test --list` needs neither the file nor the app. Tests take every name and place from here and never
 * retype one (03 section 8.1 rule 2).
 */
export function loadDemoCast(): DemoCastFile {
  if (demoCast) return demoCast;
  const file = path.join(repositoryRoot, 'tests', 'e2e', 'fixtures', 'demo-cast.json');
  let text: string;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (error) {
    throw new Error(`${file} is missing (${String(error)}); the e2e job writes it with: dotnet publish/web/Realm.Web.dll export-demo-cast tests/e2e/fixtures/demo-cast.json`);
  }
  demoCast = JSON.parse(text) as DemoCastFile;
  return demoCast;
}

/** A member of the cast by role id (`king`, `queen`, `jester`, `cryptid`, `prince`); throws when the role id is not in the cast. */
export function castMember(cast: DemoCastFile, id: string): CastMember {
  const found = cast.members.find((member) => member.id === id);
  if (!found) throw new Error(`the demo cast has no member '${id}' (it has ${cast.members.map((member) => member.id).join(', ')})`);
  return found;
}

/** A place of the cast by zone id (`home`, `jester_hall`, `approach`, ...); throws when the id is not in the cast. */
export function castPlace(cast: DemoCastFile, id: string): CastPlace {
  const found = cast.places.find((place) => place.id === id);
  if (!found) throw new Error(`the demo cast has no place '${id}' (it has ${cast.places.map((place) => place.id).join(', ')})`);
  return found;
}

// ---- mapReady: wait for the payloads, not only for the style -----------------------------------------------------------------------------------

/** The pins on screen at the default view of the Demo fixture (01 Appendix A, AC-13), as `<kind>-<id>`. */
export const DEFAULT_VIEW_PINS: readonly string[] = ['member-king', 'member-queen', 'member-jester', 'vehicle-wagon'];

/**
 * Waits until the map holds what the Demo snapshot sends and has made its first default-view fit. `demo()` returns after `settled()`, and `settled()` can
 * resolve in the short gap between `window.__realm` appearing (during `init`) and the first payloads arriving (MapView sends zones, members, vehicles,
 * the default targets and last the first fit, one interop call each), because nothing is pending yet. A spec that reads pins, zones or the camera, or
 * takes a screenshot, calls `mapReady(page)` right after `demo(page)`. Only for the first load: it expects the camera at the default view (01 section 4.9).
 *
 * @param opts.pins the `<kind>-<id>` of the pins that must exist; the default is the four of the default view (a variant that moves people passes its own).
 */
export async function mapReady(page: Page, opts: { pins?: readonly string[] } = {}): Promise<void> {
  const wanted = opts.pins ?? DEFAULT_VIEW_PINS;
  const timeout = 20_000;
  await expect
    .poll(
      async () => {
        const have = new Set(((await readHook(page, 'pins')) ?? []).map((pin) => `${pin.kind}-${pin.id}`));
        return wanted.filter((key) => !have.has(key));
      },
      { message: 'pins the Demo snapshot sends that are not on the map yet', timeout },
    )
    .toEqual([]);
  await expect
    .poll(async () => ((await readHook(page, 'camera')) as { recenter?: string } | undefined)?.recenter, {
      message: "camera().recenter: the first default-view fit has not run ('default' expected)",
      timeout,
    })
    .toBe('default');
  await settled(page);
}

/**
 * The test ids (`pin-<kind>-<id>`, sorted) of the pins whose box meets the viewport: what a person sees. `pins()` lists every pin the map holds, on screen
 * or not, so an assertion about "the pins on screen" reads the DOM, where a pin that is off the viewport has a box outside it.
 */
export async function onScreenPinTestIds(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const width = window.innerWidth;
    const height = window.innerHeight;
    return [...document.querySelectorAll<HTMLElement>('button.realm-pin[data-testid^="pin-"]')]
      .filter((pin) => {
        const box = pin.getBoundingClientRect();
        return box.right > 0 && box.bottom > 0 && box.left < width && box.top < height;
      })
      .map((pin) => pin.getAttribute('data-testid') ?? '')
      .sort();
  });
}

// ---- geometry helpers (soft: one failing test lists every wrong number) --------------------------------------------------------------------------

export interface Rect { x: number; y: number; width: number; height: number }

/** `expect.soft(|actual - expected| <= tolerance)` with a message that carries both numbers. */
export function expectApprox(actual: number, expected: number, tolerance: number, label: string): void {
  expect.soft(Math.abs(actual - expected), `${label}: expected ${expected} ± ${tolerance}, got ${actual}`).toBeLessThanOrEqual(tolerance);
}

/** A bounding box against an expected one, every edge within `tolerance` px (01 section 11: ±2 px unless stated). A null box (not rendered) fails at once. */
export function expectRectApprox(actual: Rect | null, expected: Rect, tolerance: number, label: string): void {
  expect(actual, `${label} has no bounding box (not rendered, or display: none)`).not.toBeNull();
  if (actual === null) return;
  expectApprox(actual.x, expected.x, tolerance, `${label} x`);
  expectApprox(actual.y, expected.y, tolerance, `${label} y`);
  expectApprox(actual.width, expected.width, tolerance, `${label} width`);
  expectApprox(actual.height, expected.height, tolerance, `${label} height`);
}

// ---- the gallery's screenshot writer (03 section 7.5, 04 card S6b) -------------------------------------------------------------------------------

/**
 * `<repo>/ci-out/shots/<project>/<scene>.png`. The e2e job uploads `ci-out` whole, and `make-summary.mjs` copies every PNG under a folder named `shots`
 * to `shots/<project>/<scene>.png` of the run folder on `ci-artifacts` (and lists it with its sha256 in SUMMARY.md); nothing in CI has to change for a
 * new scene.
 */
export function shotPath(project: string, scene: string): string {
  return path.join(repositoryRoot, 'ci-out', 'shots', project, `${scene}.png`);
}

/**
 * Writes one gallery scene: waits for `settled()` (a no-op on a page without hooks, such as Driving) and the fonts, then takes a viewport screenshot with
 * animations disabled. The caller has already put the page in the scene's state and is responsible for `reducedMotion: 'reduce'` and the frozen clock.
 * Returns the file it wrote.
 */
export async function saveShot(page: Page, testInfo: TestInfo, scene: string): Promise<string> {
  await settled(page);
  await page.evaluate(() => document.fonts.ready.then(() => undefined));
  const file = shotPath(testInfo.project.name, scene);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  await page.screenshot({ path: file, animations: 'disabled', caret: 'hide', scale: 'css' });
  return file;
}

// ==== S8c additions (additive; append-only as a block): history depth, the Peek rectangle, the projection and the empty map ====================================

// ---- the history depth tokens (03 section 3.7): `#r<n>` ----------------------------------------------------------------------------------------

/**
 * The depth of the browser history as the page shows it: the `n` of the `#r<n>` fragment, 0 for none. The token entries are pushed by `realmShell.js` AFTER the server has
 * decided the depth (a round trip), so a test that is about to press Back first waits for the depth it expects, with `expectHistoryDepth`, and never presses it on a guess.
 */
export async function historyDepth(page: Page): Promise<number> {
  const hash = await page.evaluate(() => window.location.hash);
  const match = /^#r([1-9][0-9]?)$/.exec(hash);
  return match === null ? 0 : Number.parseInt(match[1] ?? '0', 10);
}

/** Waits (auto-retrying) until the history is `depth` entries deep. The message names what the depth stands for. */
export async function expectHistoryDepth(page: Page, depth: number, what: string): Promise<void> {
  await expect.poll(() => historyDepth(page), { message: `history depth (#r<n>) ${what}`, timeout: 10_000 }).toBe(depth);
}

// ---- the Peek rectangle (01 section 3.4.3) -------------------------------------------------------------------------------------------------------

/** The map padding at Peek, hence the rectangle a selection flight centres the pin in: x 16 to 340, y 72 to 725 at 412 x 915, centre (178, 399). */
export const PEEK_INSET = { top: 72, right: 72, bottom: 190, left: 16 };
/** AC-22: how far from the centre of the Peek rectangle the selected pin may be. */
export const PEEK_CENTRE_TOLERANCE_PX = 24;

/** The centre of the Peek visible rectangle in the page (01 section 3.4.3). */
export function peekCentre(viewport: { width: number; height: number }): { x: number; y: number } {
  return { x: (PEEK_INSET.left + viewport.width - PEEK_INSET.right) / 2, y: (PEEK_INSET.top + viewport.height - PEEK_INSET.bottom) / 2 };
}

/**
 * How far the true point of a pin is from the centre of the Peek rectangle, in px; Infinity while the map holds no such pin. A selection flight starts after the server round
 * trip, so a test polls this (`expect.poll(...).toBeLessThanOrEqual(24)`) and calls `settled()` before it trusts the number.
 */
export async function pinDistanceFromPeekCentre(page: Page, kind: 'member' | 'vehicle', id: string): Promise<number> {
  const viewport = page.viewportSize();
  if (viewport === null) throw new Error('the page has no viewport size');
  const pin = ((await readHook(page, 'pins')) ?? []).find((candidate) => candidate.kind === kind && candidate.id === id);
  if (pin === undefined) return Number.POSITIVE_INFINITY;
  const centre = peekCentre(viewport);
  return Math.hypot(pin.anchorX - centre.x, pin.anchorY - centre.y);
}

/**
 * How far the true point of a pin is from the centre of the visible map the map PADDING says it has now (`mapPadding()`: the Peek rectangle at Peek, the strip above the sheet at 80 %,
 * the part right of the panel in Expanded), in px; Infinity while the map holds no such pin. The padding follows the sheet after it has settled, so a test that is about a size change
 * first waits for the padding to say so, and only then polls this.
 */
export async function pinDistanceFromVisibleCentre(page: Page, kind: 'member' | 'vehicle', id: string): Promise<number> {
  const viewport = page.viewportSize();
  if (viewport === null) throw new Error('the page has no viewport size');
  const padding = await readHook(page, 'mapPadding');
  const pin = ((await readHook(page, 'pins')) ?? []).find((candidate) => candidate.kind === kind && candidate.id === id);
  if (pin === undefined) return Number.POSITIVE_INFINITY;
  return Math.hypot(pin.anchorX - (padding.left + viewport.width - padding.right) / 2, pin.anchorY - (padding.top + viewport.height - padding.bottom) / 2);
}

/**
 * Waits until the selected pin is in the middle of the visible map of the sheet size `tall` says (80 %: the strip above the sheet; otherwise the Peek rectangle or the panel's), and the map is
 * still. The map padding follows the sheet only after it has settled (03 section 4.3: 120 ms), so the padding is waited for first: until then the pin is still in the middle of the OLD rectangle
 * and would pass for centred. The re-centre then eases the pin over (D45), which is the second poll; `settled()` ends it.
 */
export async function expectSelectionCentred(page: Page, who: { kind: 'member' | 'vehicle'; id: string }, tall: boolean, label: string): Promise<void> {
  const viewport = page.viewportSize();
  if (viewport === null) throw new Error('the page has no viewport size');
  await expect
    .poll(async () => (await readHook(page, 'mapPadding')).bottom > viewport.height / 2, { message: `${label}: the map padding says the sheet is ${tall ? 'at 80 %' : 'low'}`, timeout: 10_000 })
    .toBe(tall);
  await expect
    .poll(() => pinDistanceFromVisibleCentre(page, who.kind, who.id), { message: `${label}: distance of pin-${who.kind}-${who.id} from the middle of the visible map`, timeout: 10_000 })
    .toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
  await settled(page);
}

// ---- where the map is, without a coordinate in a spec (D82) ------------------------------------------------------------------------------------------

export interface MapPoint { x: number; y: number }

/** Web Mercator y of a latitude in units of the world's width (0 at the top): linear in the screen's y at any camera with no bearing or pitch. */
function mercatorY(latitude: number): number {
  const sine = Math.sin((latitude * Math.PI) / 180);
  return 0.5 - Math.log((1 + sine) / (1 - sine)) / (4 * Math.PI);
}

/**
 * A function that turns a longitude and a latitude into a point of the page, for the camera as it is now. It is built from `camera().bounds` (the whole canvas, which is the
 * viewport) and not from the centre and the padding, so the arithmetic is independent of what the padding does to the centre. The map is north-up and flat, so x is linear in
 * the longitude and y in the Mercator y. The fixture's places and members come from the cast and the hooks; no spec has to type a coordinate (D82).
 */
export async function viewportProjector(page: Page): Promise<(longitude: number, latitude: number) => MapPoint> {
  const viewport = page.viewportSize();
  if (viewport === null) throw new Error('the page has no viewport size');
  const camera = await readHook(page, 'camera');
  const [[west, south], [east, north]] = camera.bounds;
  const top = mercatorY(north);
  const bottom = mercatorY(south);
  return (longitude, latitude) => ({
    x: ((longitude - west) / (east - west)) * viewport.width,
    y: ((mercatorY(latitude) - top) / (bottom - top)) * viewport.height,
  });
}

/**
 * A point of the page where a tap reaches the empty map and nothing else: no pin, bubble, chip, control, panel or sheet under it or within `MARGIN` px of it, and no zone circle
 * within `MARGIN` px of its edge (a tap on a zone selects its place). The DOM half is `elementFromPoint` on a cross of five points, so it is what the browser would hit; the zone
 * half is the cast's places projected with the camera and `zones()`'s radius in px. Of the free points the one nearest the middle of the visible map is returned. Call it after
 * `settled()`: it describes the camera of the moment. Throws, with the reason, when the map has no such point.
 */
export async function emptyMapPoint(page: Page): Promise<MapPoint> {
  const MARGIN = 28;
  const STEP = 8;
  const viewport = page.viewportSize();
  if (viewport === null) throw new Error('the page has no viewport size');
  const free = await page.evaluate(
    ({ margin, step, width, height }) => {
      const onlyTheMap = (x: number, y: number): boolean => {
        const hit = document.elementFromPoint(x, y);
        return hit !== null && hit.closest('[data-testid="map-canvas"]') !== null && hit.closest('button, a, [role="button"], [role="dialog"], .realm-pin, .realm-bubble, .realm-chip, .maplibregl-ctrl, [data-testid="map-attribution"]') === null;
      };
      const points: Array<{ x: number; y: number }> = [];
      for (let y = 16; y <= height - 16; y += step) {
        for (let x = 16; x <= width - 16; x += step) {
          if (onlyTheMap(x, y) && onlyTheMap(x - margin, y) && onlyTheMap(x + margin, y) && onlyTheMap(x, y - margin) && onlyTheMap(x, y + margin)) points.push({ x, y });
        }
      }
      return points;
    },
    { margin: MARGIN, step: STEP, width: viewport.width, height: viewport.height },
  );

  const project = await viewportProjector(page);
  const zones = await readHook(page, 'zones');
  const circles = loadDemoCast().places.flatMap((place) => {
    const zone = zones.find((candidate) => candidate.id === place.id && candidate.drawn);
    return zone === undefined ? [] : [{ ...project(place.lon, place.lat), radius: zone.radiusPx }];
  });
  const clear = free.filter((point) => circles.every((circle) => Math.hypot(point.x - circle.x, point.y - circle.y) > circle.radius + MARGIN));
  const padding = await readHook(page, 'mapPadding');
  const middle = { x: (padding.left + viewport.width - padding.right) / 2, y: (padding.top + viewport.height - padding.bottom) / 2 };
  clear.sort((a, b) => Math.hypot(a.x - middle.x, a.y - middle.y) - Math.hypot(b.x - middle.x, b.y - middle.y));
  const best = clear[0];
  if (best === undefined) {
    throw new Error(`the map has no empty point: ${free.length} points hit only the map, ${free.length - clear.length} of them near a zone circle (${circles.length} zones projected)`);
  }
  return best;
}

/** Taps the empty map the way a finger does (a click on the canvas), at a point `emptyMapPoint` found. Returns where it tapped. */
export async function tapEmptyMap(page: Page): Promise<MapPoint> {
  await settled(page);
  const point = await emptyMapPoint(page);
  await page.mouse.click(point.x, point.y);
  return point;
}
