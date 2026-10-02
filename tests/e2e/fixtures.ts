// Fixtures of the E2E suite (03 section 7.5, 04 card S6a). Specs import `test` and `expect` from here, never from '@playwright/test':
//   - `demo(page, opts)` opens the Demo app through the ingress proxy with the URL parameters of 01 Appendix B and waits until the map is settled;
//   - the GUARD fixture wraps every `page`: a test fails on a page error, on any console.error (which also catches the script's OnError reports,
//     realmMap.js logs them as `[realmMap] ...`), and on any request or websocket whose host is not the proxy's (so no fonts or tiles leave in Demo).
//     A test that expects an error says so with `test.use({ allowConsoleErrors: [{ pattern, reason }] })`; every entry needs a reason.
//
// Everything is relative to the baseURL, which is the proxy URL WITH a trailing slash (playwright.config.ts): demo(page) opens
// `<proxy>/api/hassio_ingress/<token>/?demo=1&...`, and a leading slash reaches the proxy's own origin (`/__proxy/health`).

import { expect, test as base, type APIRequestContext, type Page } from '@playwright/test';

import { APP_HOST, APP_PORT, CONTROL_PREFIX, INGRESS_PREFIX, PROXY_HOST, PROXY_PORT } from './harness/ingressProxy.mjs';

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
 * Opens the Demo app through the proxy and waits until the map is settled.
 *
 * Re-open with demo(), never with the navigation links, when a test needs a fresh circuit (CR2-024): the Demo-only parameters are read once per
 * circuit, the bottom-nav links drop the query string, and so a `page.reload()` after an in-app navigation comes back without `variant`, `now` and
 * `style` (and, in Live with `?demo=1`, as a Live session).
 */
export async function demo(page: Page, opts: DemoOptions = {}): Promise<void> {
  await page.goto(demoUrl(opts));
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

    page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));
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
