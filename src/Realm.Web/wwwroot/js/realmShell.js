// @ts-check
// realmShell.js: the entry module of ShellInterop (03 section 4.8), everything in the browser that is not the map. S7a owns the viewport
// reports, the layout attribute, the sheet metrics and the handle tap shim; the preferences, history tokens, Escape handling, HA bridge and
// clipboard exports of 4.8 arrive with the slices that own them, in the commented region at the end of this file.
//
// Contract in brief: `init` is idempotent (a repeated call tears the previous state down first) and never rejects; every export catches its own
// errors and never rethrows into Blazor; nothing touches `window` or `document` when the module is imported (the node tests import it).
// Module specifiers and URLs are plain relative ones, resolved against <base href> (D62): no leading slash, no @Assets.

/** @typedef {'peek' | '80' | 'panel'} SheetState the three values of window.__realm.sheet().state (01 Appendix B) */
/** @typedef {{ heightPx: number, topPx: number }} SheetMetrics */
/** @typedef {{ top: number, right: number, bottom: number, left: number }} Padding */
/** @typedef {{ width: number, height: number, dpr: number, safe: Padding, reducedMotion: boolean, prefersContrast: boolean, orientation: 'portrait' | 'landscape' }} ViewportInfo */
/** @typedef {{ invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown> }} DotNetRef */
/** @typedef {{ x: number, y: number, t: number }} PointerSample */

/** The MudX script, by the URL MudXProvider itself builds (a Release package ships only the minified file). It defines window.mudsheetHelper. */
const MUDX_SCRIPT = './_content/MudX.MudBlazor.Extension/mudx.min.js';
/** How long init waits for window.mudsheetHelper before it gives up (the sheet then reports its own JS error, and the guard fixture shows it). */
const MUDX_WAIT_MS = 5000;
const MUDX_POLL_MS = 25;

/** A resize burst becomes one report (03 section 4.8). */
export const VIEWPORT_DEBOUNCE_MS = 100;
/** A press that moves at most this far and lasts at most {@link TAP_MAX_MS} is a tap, not a drag. */
export const TAP_SLOP_PX = 10;
export const TAP_MAX_MS = 600;
/** How long a tap waits for the browser's own click on the handle button before the shim sends one. */
export const CLICK_GRACE_MS = 80;
/** The right stack hides once the sheet covers this fraction of the viewport height (01 section 3.5, AC-09). */
export const TALL_FRACTION = 0.5;

const HANDLE_SELECTOR = '.mud-sheet-handle';
const HANDLE_BUTTON_SELECTOR = '[data-testid="sheet-handle"]';
const SHEET_STATE_SELECTOR = '[data-testid="sheet"]';
const MEDIA_QUERIES = ['(prefers-reduced-motion: reduce)', '(prefers-contrast: more)'];

// ---- pure helpers (unit-tested in tests/js/realmShell.test.mjs) -------------------------------------------------------------------------------

/**
 * Whether a press that went down at `down` and up at `up` was a tap on the handle: it stayed within the slop and was released quickly.
 * Anything else was a drag (or a long press), which MudX handles itself.
 * @param {PointerSample} down
 * @param {PointerSample} up
 * @param {number} [slopPx]
 * @param {number} [maxMs]
 */
export function isTap(down, up, slopPx = TAP_SLOP_PX, maxMs = TAP_MAX_MS) {
  const moved = Math.hypot(up.x - down.x, up.y - down.y);
  const elapsed = up.t - down.t;
  return Number.isFinite(moved) && Number.isFinite(elapsed) && moved <= slopPx && elapsed >= 0 && elapsed <= maxMs;
}

/**
 * Whether the sheet is at least half the viewport height tall, which hides the right button stack. A panel (Expanded) is never "tall": its
 * height is the window minus its insets, and the stack stays where it is.
 * @param {number} heightPx
 * @param {number} viewportHeightPx
 * @param {string | null | undefined} layout the value of <html data-layout>
 */
export function isSheetTall(heightPx, viewportHeightPx, layout) {
  if (layout === 'expanded') return false;
  if (!Number.isFinite(heightPx) || !Number.isFinite(viewportHeightPx) || heightPx <= 0 || viewportHeightPx <= 0) return false;
  return heightPx >= viewportHeightPx * TALL_FRACTION - 0.5;
}

/**
 * The sheet height the map pads for: the measured height, or null while there is no sheet (zero, not finite), which computePadding reads as "the
 * Peek height of this viewport". Padding by zero would put the map under the navigation bar.
 * @param {number} heightPx
 * @returns {number | null}
 */
export function sheetHeightForPadding(heightPx) {
  return Number.isFinite(heightPx) && heightPx > 0 ? heightPx : null;
}

/**
 * The first element that matches the selector list, trying the entries in the order written: `div[mudsheet], [data-testid="sheet"]` finds the
 * MudX popover (the whole sheet, handle row included) and falls back to the contract element only when there is no popover. A plain
 * querySelector would return whichever comes first in the document.
 * @param {ParentNode} root
 * @param {string} selector
 * @returns {Element | null}
 */
export function findSheetElement(root, selector) {
  for (const part of selector.split(',')) {
    const trimmed = part.trim();
    if (trimmed === '') continue;
    const found = root.querySelector(trimmed);
    if (found) return found;
  }
  return null;
}

/**
 * The `sheet()` hook's value (01 Appendix B): the state comes from the contract element's data-state (R2-027), the geometry from the metrics.
 * `state` is null while no sheet element exists (the E2E reads that as "the sheet is not there").
 * @param {string | null | undefined} dataState
 * @param {SheetMetrics} metrics
 * @param {boolean} present
 * @returns {{ state: SheetState | null, heightPx: number, topPx: number, open: boolean }}
 */
export function describeSheet(dataState, metrics, present) {
  const state = dataState === 'peek' || dataState === '80' || dataState === 'panel' ? dataState : null;
  return { state, heightPx: metrics.heightPx, topPx: metrics.topPx, open: present && metrics.heightPx > 0 };
}

// ---- module state -----------------------------------------------------------------------------------------------------------------------------

/** @type {SheetMetrics} */
const EMPTY_METRICS = { heightPx: 0, topPx: 0 };

/** @type {SheetMetrics} */
let metrics = EMPTY_METRICS;
/** @type {Set<(m: SheetMetrics) => void>} */
const subscribers = new Set();

/**
 * @typedef {object} Active
 * @property {DotNetRef} dotnet
 * @property {number | null} timer the debounce timer of the next viewport report
 * @property {string} lastReport JSON of the last viewport sent, to drop identical reports
 * @property {Array<() => void>} cleanups
 */
/** @type {Active | null} */
let active = null;

/**
 * @typedef {object} SheetWatch
 * @property {string} selector
 * @property {Element | null} element
 * @property {ResizeObserver | null} resize
 * @property {MutationObserver} mutation
 */
/** @type {SheetWatch | null} */
let watch = null;

// ---- the viewport -----------------------------------------------------------------------------------------------------------------------------

/**
 * The safe-area insets in CSS pixels, read through a throwaway zero-size element so that env() is resolved by the browser (it is not a number
 * in script). 0 everywhere without a notch or an HA frame that reports insets.
 * @returns {Padding}
 */
function readSafeArea() {
  const zero = { top: 0, right: 0, bottom: 0, left: 0 };
  try {
    const probe = document.createElement('div');
    probe.setAttribute('aria-hidden', 'true');
    probe.style.cssText =
      'position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;' +
      'padding:env(safe-area-inset-top,0px) env(safe-area-inset-right,0px) env(safe-area-inset-bottom,0px) env(safe-area-inset-left,0px)';
    document.documentElement.appendChild(probe);
    const style = getComputedStyle(probe);
    const read = (/** @type {string} */ value) => (Number.isFinite(parseFloat(value)) ? Math.max(0, parseFloat(value)) : 0);
    const safe = { top: read(style.paddingTop), right: read(style.paddingRight), bottom: read(style.paddingBottom), left: read(style.paddingLeft) };
    probe.remove();
    return safe;
  } catch {
    return zero;
  }
}

/** @param {string} query */
function matches(query) {
  try {
    return window.matchMedia(query).matches;
  } catch {
    return false;
  }
}

/** The window as ViewportInfo (03 section 4.8): CSS pixels of the layout viewport. @returns {ViewportInfo} */
export function readViewport() {
  const width = window.innerWidth;
  const height = window.innerHeight;
  return {
    width,
    height,
    dpr: window.devicePixelRatio || 1,
    safe: readSafeArea(),
    reducedMotion: matches(MEDIA_QUERIES[0]),
    prefersContrast: matches(MEDIA_QUERIES[1]),
    orientation: width >= height ? 'landscape' : 'portrait',
  };
}

function scheduleViewportReport() {
  const state = active;
  if (!state) return;
  if (state.timer !== null) clearTimeout(state.timer);
  state.timer = window.setTimeout(() => {
    state.timer = null;
    void reportViewport(state);
  }, VIEWPORT_DEBOUNCE_MS);
}

/** @param {Active} state */
async function reportViewport(state) {
  if (active !== state) return;
  const viewport = readViewport();
  const key = JSON.stringify(viewport);
  if (key === state.lastReport) return;
  state.lastReport = key;
  try {
    await state.dotnet.invokeMethodAsync('OnViewport', viewport);
  } catch (error) {
    // The circuit dropped or the component is gone: the next report (after a reconnect and a new init) starts clean.
    console.warn('[realmShell] OnViewport failed', error);
  }
}

// ---- the MudX script --------------------------------------------------------------------------------------------------------------------------

/**
 * Makes sure window.mudsheetHelper exists. MudXProvider injects mudx.min.js from its own first render, with no ordering against the sheet's
 * first render, and MudXSheet calls the helper from OnAfterRenderAsync: a sheet that wins the race throws in the circuit. init therefore loads
 * the script itself (as the very same `script[data-mudx-js]` element, so the provider finds it and adds nothing) and waits for it.
 * @returns {Promise<boolean>} whether the helper is there
 */
export function ensureMudX() {
  const win = /** @type {any} */ (window);
  if (win.mudsheetHelper) return Promise.resolve(true);
  try {
    if (!document.querySelector('script[data-mudx-js]')) {
      const script = document.createElement('script');
      script.setAttribute('data-mudx-js', 'true');
      script.type = 'text/javascript';
      script.src = MUDX_SCRIPT;
      document.head.appendChild(script);
    }
  } catch (error) {
    console.warn('[realmShell] could not add the MudX script', error);
  }
  return new Promise((resolve) => {
    const startedAt = Date.now();
    const poll = () => {
      if (win.mudsheetHelper) return resolve(true);
      if (Date.now() - startedAt >= MUDX_WAIT_MS) {
        console.warn('[realmShell] window.mudsheetHelper did not appear; the sheet will fail');
        return resolve(false);
      }
      setTimeout(poll, MUDX_POLL_MS);
    };
    poll();
  });
}

// ---- the handle tap shim ----------------------------------------------------------------------------------------------------------------------

/**
 * MudX sets pointer capture on the handle wrapper from its own pointerdown handler, after a circuit round trip. A tap that is released after
 * that has its click retargeted to the wrapper, so the handle button never sees it (R-033's premise, that a click always follows the pointerup,
 * does not hold). The shim watches the pointer events on the handle: for a tap it waits CLICK_GRACE_MS for the button's own click and, when none
 * came, clicks the button itself. A tap that the browser delivered normally is left alone, so nothing toggles twice.
 * @returns {() => void} the remover
 */
function installTapShim() {
  /** @type {(PointerSample & { id: number }) | null} */
  let down = null;
  let lastButtonClickAt = Number.NEGATIVE_INFINITY;

  /** @param {EventTarget | null} target */
  const handleOf = (target) => (target instanceof Element ? target.closest(HANDLE_SELECTOR) : null);

  /** @param {PointerEvent} event */
  const onDown = (event) => {
    down = handleOf(event.target) ? { x: event.clientX, y: event.clientY, t: performance.now(), id: event.pointerId } : null;
  };

  /** @param {PointerEvent} event */
  const onUp = (event) => {
    const start = down;
    down = null;
    if (!start || start.id !== event.pointerId) return;
    const handle = handleOf(event.target);
    if (!handle || !isTap(start, { x: event.clientX, y: event.clientY, t: performance.now() })) return;
    const releasedAt = performance.now();
    window.setTimeout(() => {
      if (lastButtonClickAt >= releasedAt) return;
      const button = handle.querySelector(HANDLE_BUTTON_SELECTOR);
      if (button instanceof HTMLElement) button.click();
    }, CLICK_GRACE_MS);
  };

  /** @param {MouseEvent} event */
  const onClick = (event) => {
    if (event.target instanceof Element && event.target.closest(HANDLE_BUTTON_SELECTOR)) lastButtonClickAt = performance.now();
  };

  const options = { capture: true };
  document.addEventListener('pointerdown', onDown, options);
  document.addEventListener('pointerup', onUp, options);
  document.addEventListener('click', onClick, options);
  return () => {
    document.removeEventListener('pointerdown', onDown, options);
    document.removeEventListener('pointerup', onUp, options);
    document.removeEventListener('click', onClick, options);
  };
}

// ---- init and dispose -------------------------------------------------------------------------------------------------------------------------

/**
 * Starts the viewport reports, the handle shim and the MudX script, and returns the window as it is now (so the first layout needs no callback).
 * Never rejects: a failed part is logged and the rest stands.
 * @param {DotNetRef} dotnet the DotNetObjectReference of ShellCallbacks
 * @returns {Promise<{ viewport: ViewportInfo }>}
 */
export async function init(dotnet) {
  try {
    dispose();
    /** @type {Active} */
    const state = { dotnet, timer: null, lastReport: '', cleanups: [] };
    active = state;

    const schedule = () => scheduleViewportReport();
    window.addEventListener('resize', schedule);
    window.addEventListener('orientationchange', schedule);
    state.cleanups.push(() => window.removeEventListener('resize', schedule), () => window.removeEventListener('orientationchange', schedule));
    const visual = window.visualViewport;
    if (visual) {
      visual.addEventListener('resize', schedule);
      state.cleanups.push(() => visual.removeEventListener('resize', schedule));
    }
    for (const query of MEDIA_QUERIES) {
      try {
        const list = window.matchMedia(query);
        list.addEventListener('change', schedule);
        state.cleanups.push(() => list.removeEventListener('change', schedule));
      } catch {
        // an old browser without MediaQueryList events: the preference is read again on the next resize
      }
    }
    state.cleanups.push(installTapShim());

    const viewport = readViewport();
    state.lastReport = JSON.stringify(viewport);
    await ensureMudX();
    return { viewport };
  } catch (error) {
    console.error('[realmShell] init failed', error);
    return { viewport: readViewport() };
  }
}

/** Removes every listener and observer and clears what init set on <html>. Safe to call twice and before init. */
export function dispose() {
  try {
    const state = active;
    active = null;
    if (state) {
      if (state.timer !== null) clearTimeout(state.timer);
      for (const cleanup of state.cleanups) cleanup();
    }
    observeSheet(null);
    document.documentElement.removeAttribute('data-layout');
  } catch (error) {
    console.warn('[realmShell] dispose failed', error);
  }
}

/**
 * Sets <html data-layout> (`compact` or `expanded`), which the CSS reads; null removes it. Re-derives the "sheet is tall" flag, which depends on it.
 * @param {'compact' | 'expanded' | null} mode
 */
export function setLayoutAttr(mode) {
  try {
    const root = document.documentElement;
    if (mode === 'compact' || mode === 'expanded') root.setAttribute('data-layout', mode);
    else root.removeAttribute('data-layout');
    applyMetricsToDocument();
  } catch (error) {
    console.warn('[realmShell] setLayoutAttr failed', error);
  }
}

// ---- the sheet metrics ------------------------------------------------------------------------------------------------------------------------

/** Publishes the metrics on <html>: `--realm-sheet-h`, `--realm-sheet-top` (px) and the `data-sheet-tall` flag the CSS hides the right stack with. */
function applyMetricsToDocument() {
  const root = document.documentElement;
  if (metrics.heightPx > 0) {
    root.style.setProperty('--realm-sheet-h', `${metrics.heightPx}px`);
    root.style.setProperty('--realm-sheet-top', `${metrics.topPx}px`);
  } else {
    // No sheet: the variables go, so the CSS fallback (the Peek height) applies instead of a stack glued to the viewport bottom.
    root.style.removeProperty('--realm-sheet-h');
    root.style.removeProperty('--realm-sheet-top');
  }
  if (isSheetTall(metrics.heightPx, window.innerHeight, root.getAttribute('data-layout'))) root.setAttribute('data-sheet-tall', '');
  else root.removeAttribute('data-sheet-tall');
}

/** @param {SheetMetrics} next */
function setMetrics(next) {
  const changed = next.heightPx !== metrics.heightPx || next.topPx !== metrics.topPx;
  metrics = next;
  applyMetricsToDocument();
  if (!changed) return;
  for (const callback of [...subscribers]) {
    try {
      callback({ ...metrics });
    } catch (error) {
      console.warn('[realmShell] a sheetMetrics subscriber threw', error);
    }
  }
}

/** @param {SheetWatch} state */
function measureSheet(state) {
  const element = state.element;
  if (!element) return setMetrics(EMPTY_METRICS);
  const box = element.getBoundingClientRect();
  setMetrics({ heightPx: Math.round(box.height * 100) / 100, topPx: Math.round(box.top * 100) / 100 });
}

/** Re-resolves the element: MudX mounts its popover after the first render and replaces it when the host switches position. @param {SheetWatch} state */
function attachSheet(state) {
  const element = findSheetElement(document, state.selector);
  if (element === state.element) return;
  state.resize?.disconnect();
  state.resize = null;
  state.element = element;
  if (element && typeof ResizeObserver === 'function') {
    state.resize = new ResizeObserver(() => measureSheet(state));
    state.resize.observe(element);
  }
  measureSheet(state);
}

/**
 * Follows the sheet element (03 section 4.8): a ResizeObserver on it, a MutationObserver on the body that re-attaches when it appears or is
 * replaced, and on every observed size `--realm-sheet-h` and `--realm-sheet-top` on <html> and the `sheetMetrics` subscribers. It adds no server
 * traffic. The selector is a list tried in order. null stops watching and resets the metrics to zero.
 * @param {string | null} selector
 */
export function observeSheet(selector) {
  try {
    if (watch) {
      watch.resize?.disconnect();
      watch.mutation.disconnect();
      watch = null;
    }
    if (!selector) {
      setMetrics(EMPTY_METRICS);
      return;
    }
    /** @type {SheetWatch} */
    const state = { selector, element: null, resize: null, mutation: new MutationObserver(() => attachSheet(state)) };
    watch = state;
    state.mutation.observe(document.body, { childList: true, subtree: true });
    attachSheet(state);
    if (!state.element) setMetrics(EMPTY_METRICS);
  } catch (error) {
    console.warn('[realmShell] observeSheet failed', error);
  }
}

/** The sheet's height and top in CSS pixels, and who is told when they change (the map's computePadding input). */
export const sheetMetrics = {
  /** @returns {SheetMetrics} */
  current() {
    return { ...metrics };
  },
  /**
   * @param {(m: SheetMetrics) => void} callback
   * @returns {() => void} the unsubscribe
   */
  subscribe(callback) {
    subscribers.add(callback);
    return () => {
      subscribers.delete(callback);
    };
  },
};

/** The value of `window.__realm.sheet()` (realmMap.js installs it in the probe). @returns {ReturnType<typeof describeSheet>} */
export function readSheet() {
  const element = document.querySelector(SHEET_STATE_SELECTOR);
  return describeSheet(element?.getAttribute('data-state'), metrics, element !== null);
}

// ---- S7b (selection header focus), S8a (history and Escape), S15 (preferences, HA bridge) add their exports and state below this line ----------
