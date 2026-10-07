// @ts-check
// realmShell.js: the entry module of ShellInterop (03 section 4.8), everything in the browser that is not the map. S7a owns the viewport
// reports, the layout attribute and the sheet metrics; S10a the preferences; S8c the history depth tokens and the global Escape handler (below);
// the HA bridge and clipboard exports of 4.8 arrive with the slices that own them, in the commented region before the preferences.
//
// Contract in brief: `init` is idempotent (a repeated call tears the previous state down first) and never rejects; every export catches its own
// errors and never rethrows into Blazor; nothing touches `window` or `document` when the module is imported (the node tests import it).
// Module specifiers and URLs are plain relative ones, resolved against <base href> (D62): no leading slash, no @Assets.

/** @typedef {'peek' | '80' | 'panel'} SheetState the three values of window.__realm.sheet().state (01 Appendix B) */
/** @typedef {{ heightPx: number, topPx: number }} SheetMetrics */
/** @typedef {{ top: number, right: number, bottom: number, left: number }} Padding */
/** @typedef {{ width: number, height: number, dpr: number, safe: Padding, reducedMotion: boolean, prefersContrast: boolean, orientation: 'portrait' | 'landscape' }} ViewportInfo */
/** @typedef {{ invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown> }} DotNetRef */

/** The MudX script, by the URL MudXProvider itself builds (a Release package ships only the minified file). It defines window.mudsheetHelper. */
const MUDX_SCRIPT = './_content/MudX.MudBlazor.Extension/mudx.min.js';
/** How long init waits for window.mudsheetHelper before it gives up (the sheet then reports its own JS error, and the guard fixture shows it). */
const MUDX_WAIT_MS = 5000;
const MUDX_POLL_MS = 25;

/** A resize burst becomes one report (03 section 4.8). */
export const VIEWPORT_DEBOUNCE_MS = 100;
/** The right stack hides once the sheet covers this fraction of the viewport height (01 section 3.5, AC-09). */
export const TALL_FRACTION = 0.5;

const SHEET_STATE_SELECTOR = '[data-testid="sheet"]';
const MEDIA_QUERIES = ['(prefers-reduced-motion: reduce)', '(prefers-contrast: more)'];

// ---- pure helpers (unit-tested in tests/js/realmShell.test.mjs) -------------------------------------------------------------------------------

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

// ---- init and dispose -------------------------------------------------------------------------------------------------------------------------

/**
 * Starts the viewport reports and the MudX script, and returns the window as it is now (so the first layout needs no callback).
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
 * Scrolls a week chip into the centre of its row (Driving, R3-12). The row scrolls, the page does not.
 * @param {Element | null} chip
 */
export function scrollChipIntoView(chip) {
  try {
    chip?.scrollIntoView({ block: 'nearest', inline: 'center' });
  } catch (error) {
    console.warn('[realmShell] scrollChipIntoView failed', error);
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

// ---- S7b (selection header focus), S15 (HA bridge, clipboard) add their exports and state below this line ---------------------------------------

// ---- S8c: history depth tokens and Escape (03 sections 3.7 and 4.8, 01 sections 2.2 and 5.7) -----------------------------------------------------
// One history entry per level of depth (`#r1`, `#r2`), so the Android Back gesture steps through the sheet and the overlays instead of leaving the app. C# decides the
// depth and calls `history.setDepth(n)`; the script pushes the missing entries or goes back by the surplus, and tells C# about a Back (or Forward) it did not cause itself.
// The controller below knows nothing of the browser: it works on a small environment, so the node tests run it against a fake history. `attachHistory` is its own entry,
// apart from `init`: `init` tears the viewport state down on every call and belongs to the Location page, while the Driving page needs the history too.
// This module declares `history` as an export (the contract of 4.8), so the browser's own is always written `window.history` here.

/** The deepest history the script will build; far above any real depth (a selection, the Tall size and a few overlays). */
export const MAX_HISTORY_DEPTH = 99;
/** How long `setDepth` waits for the popstate of its own `history.go` before it gives up and reads where the browser is. */
export const HISTORY_SETTLE_MS = 1500;
/** The dialog layer MudBlazor draws and closes by itself on Escape (`CloseOnEscapeKey`). */
export const DIALOG_LAYER_SELECTOR = '.mud-dialog-container';
/** An open popover that is not ours: the sheet itself is a MudPopover, our own style popover handles its Escape in its own markup, a tooltip is not a layer. */
export const FOREIGN_POPOVER_SELECTOR = '.mud-popover-open:not(.mud-sheet-popover):not(.realm-style-popover):not(.mud-tooltip)';
/** The style popover: an Escape pressed inside it is its own (it closes itself), so the global handler stays out of the way. */
export const OWN_POPOVER_SELECTOR = '.realm-style-popover';

/**
 * @typedef {object} HistoryEnv the browser, as far as the depth tokens need it
 * @property {() => unknown} getState `history.state` of the current entry
 * @property {() => string} getHash `location.hash` of the current entry
 * @property {(state: { realmDepth: number }, depth: number) => void} push pushes the entry of `depth`
 * @property {(delta: number) => void} go `history.go`
 * @property {(handler: () => void) => (() => void)} onPopState adds a popstate listener and returns the remover
 * @property {(callback: () => void, ms: number) => unknown} setTimer
 * @property {(handle: unknown) => void} clearTimer
 */

/**
 * @typedef {object} HistoryController
 * @property {(depth: number) => Promise<void>} setDepth
 * @property {() => number} getDepth
 * @property {() => void} dispose
 */

/**
 * The depth an entry stands for: the `realmDepth` of its state, else the `#r<n>` of its URL. The state alone is not enough: Blazor's own `replaceState` (a week chip, a
 * replacing navigation) swaps the state object for its own and keeps the URL, fragment included. Anything else (the base entry, a foreign fragment) is 0.
 * @param {unknown} state
 * @param {unknown} hash
 * @returns {number}
 */
export function depthOfEntry(state, hash) {
  if (state !== null && typeof state === 'object') {
    const depth = /** @type {{ realmDepth?: unknown }} */ (state).realmDepth;
    if (typeof depth === 'number' && Number.isInteger(depth) && depth > 0 && depth <= MAX_HISTORY_DEPTH) return depth;
  }
  const match = typeof hash === 'string' ? /^#r([1-9][0-9]?)$/.exec(hash) : null;
  return match ? Number(match[1]) : 0;
}

/**
 * The URL a token entry is pushed with. It is the page's own path and query with only the fragment changed, built from the location and not from a bare `#r1`: a relative
 * reference resolves against `<base href>`, which is the app's root (D62), so `#r1` alone would move the address bar from `driving` to the root.
 * @param {string} pathname
 * @param {string} search
 * @param {number} depth
 * @returns {string}
 */
export function tokenUrl(pathname, search, depth) {
  return `${pathname}${search}#r${depth}`;
}

/**
 * The depth tokens of 03 section 3.7 over an environment. `setDepth(n)` pushes `#r<k>` entries for each missing level, or goes back by the surplus with the resulting popstate
 * swallowed (it is ours), and resolves once the browser has settled; calls run one after the other, because `history.go` is asynchronous. A popstate that is not ours is a
 * Back or a Forward the person made: it is reported through `onUserBack` with the depth the browser is at. With `tokens` false, `setDepth` only records the depth: nothing is
 * pushed, nothing is popped, nothing is reported (the Android Back then simply leaves the app).
 * @param {HistoryEnv} env
 * @param {{ tokens?: boolean, onUserBack: (depth: number) => void }} options
 * @returns {HistoryController}
 */
export function createHistoryController(env, options) {
  const tokens = options.tokens !== false;
  let depth = depthOfEntry(env.getState(), env.getHash());
  /** @type {{ resolve: () => void, timer: unknown } | null} */
  let waiting = null;
  let queue = Promise.resolve();
  let disposed = false;

  // The popstate of our own history.go (or the timer that gave up waiting for it): reads where the browser landed and lets setDepth return.
  const settle = () => {
    const pending = waiting;
    if (!pending) return false;
    waiting = null;
    env.clearTimer(pending.timer);
    depth = depthOfEntry(env.getState(), env.getHash());
    pending.resolve();
    return true;
  };

  const removeListener = env.onPopState(() => {
    if (disposed || settle() || !tokens) return;
    depth = depthOfEntry(env.getState(), env.getHash());
    options.onUserBack(depth);
  });

  /** @param {number} requested */
  const apply = async (requested) => {
    const target = Number.isFinite(requested) ? Math.max(0, Math.min(MAX_HISTORY_DEPTH, Math.trunc(requested))) : 0;
    if (!tokens) {
      depth = target;
      return;
    }
    if (target > depth) {
      for (let level = depth + 1; level <= target; level++) env.push({ realmDepth: level }, level);
      depth = target;
      return;
    }
    if (target < depth) {
      /** @type {Promise<void>} */
      const settled = new Promise((resolve) => {
        const timer = env.setTimer(() => {
          if (waiting?.resolve === resolve) settle();
        }, HISTORY_SETTLE_MS);
        waiting = { resolve, timer };
      });
      env.go(target - depth);
      await settled;
    }
  };

  return {
    setDepth(requested) {
      if (disposed) return Promise.resolve();
      const run = queue.then(() => apply(requested)).catch((error) => {
        console.warn('[realmShell] history.setDepth failed', error);
      });
      queue = run;
      return run;
    },
    getDepth() {
      return depth;
    },
    dispose() {
      disposed = true;
      removeListener();
      const pending = waiting;
      waiting = null;
      if (pending) {
        env.clearTimer(pending.timer);
        pending.resolve();
      }
    },
  };
}

/**
 * @param {unknown} target the element the key was pressed in
 * @returns {boolean} whether it is a text field, a select or contenteditable: the keys are the field's own
 */
function isTextEntry(target) {
  if (target === null || typeof target !== 'object') return false;
  const element = /** @type {{ tagName?: unknown, isContentEditable?: unknown }} */ (target);
  const tag = typeof element.tagName === 'string' ? element.tagName.toUpperCase() : '';
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || element.isContentEditable === true;
}

/**
 * @param {unknown} target
 * @returns {boolean} whether the key was pressed inside our own style popover, which closes itself on Escape
 */
function insideOwnPopover(target) {
  if (target === null || typeof target !== 'object') return false;
  const element = /** @type {{ closest?: unknown }} */ (target);
  return typeof element.closest === 'function' && element.closest(OWN_POPOVER_SELECTOR) !== null;
}

/**
 * Whether MudBlazor has a layer open that closes itself on Escape: a dialog (it takes the key first, through Blazor's own delegated keydown, and its container is still there when
 * this handler runs), or a popover that is neither the sheet nor our own.
 * @param {{ querySelector: (selector: string) => unknown }} root
 * @returns {boolean}
 */
export function foreignLayerOpen(root) {
  return root.querySelector(DIALOG_LAYER_SELECTOR) !== null || root.querySelector(FOREIGN_POPOVER_SELECTOR) !== null;
}

/**
 * Whether an Escape keydown is ours (03 section 4.8): it is the Escape key, nothing default-prevented it, it was not typed into a field or a composition, it was not pressed inside
 * our own popover, and MudBlazor has no layer of its own open that already closes on it. C# then closes our topmost overlay, or runs Back steps 2 and 3.
 * @param {{ key?: string, defaultPrevented?: boolean, isComposing?: boolean, target?: unknown }} event
 * @param {{ querySelector: (selector: string) => unknown }} root
 * @returns {boolean}
 */
export function shouldHandleEscape(event, root) {
  if (event.key !== 'Escape' && event.key !== 'Esc') return false;
  if (event.defaultPrevented === true || event.isComposing === true) return false;
  if (isTextEntry(event.target) || insideOwnPopover(event.target)) return false;
  return !foreignLayerOpen(root);
}

/** The real history as a HistoryEnv. @returns {HistoryEnv} */
function browserHistoryEnv() {
  return {
    getState: () => window.history.state,
    getHash: () => window.location.hash,
    push: (state, depth) => window.history.pushState(state, '', tokenUrl(window.location.pathname, window.location.search, depth)),
    go: (delta) => window.history.go(delta),
    onPopState: (handler) => {
      window.addEventListener('popstate', handler);
      return () => window.removeEventListener('popstate', handler);
    },
    setTimer: (callback, ms) => window.setTimeout(callback, ms),
    clearTimer: (handle) => window.clearTimeout(/** @type {number} */ (handle)),
  };
}

/**
 * @typedef {object} HistoryAttachment
 * @property {number} token tells this attachment from a later one
 * @property {HistoryController} controller
 * @property {Array<() => void>} cleanups
 */
/** @type {HistoryAttachment | null} */
let attachment = null;
let attachments = 0;

/**
 * One call into .NET for the history or Escape. A disposed reference or a dropped circuit is not an error here (03 section 4.7): a late event must never throw.
 * @param {DotNetRef} dotnet
 * @param {string} name
 * @param {...unknown} args
 */
function notifyHistory(dotnet, name, ...args) {
  try {
    void Promise.resolve(dotnet.invokeMethodAsync(name, ...args)).catch(() => {});
  } catch {
    // the reference was disposed
  }
}

function teardownHistory() {
  const current = attachment;
  attachment = null;
  if (!current) return;
  current.controller.dispose();
  for (const cleanup of current.cleanups) cleanup();
}

/**
 * Starts the depth tokens and the Escape handler for the page on screen (one attachment per document: a later call replaces the earlier one). Never throws.
 * @param {DotNetRef} dotnet the DotNetObjectReference of HistoryCallbacks: `OnHistoryBack(int)` and `OnEscape()`
 * @param {{ historyTokens?: boolean } | null} [options] `historyTokens` false is the kill switch `Realm:Ui:HistoryTokens` (D47): the depth is recorded and the history is never touched
 * @returns {number} the token `detachHistory` takes; 0 when the attach failed
 */
export function attachHistory(dotnet, options = null) {
  try {
    teardownHistory();
    const controller = createHistoryController(browserHistoryEnv(), {
      tokens: options?.historyTokens !== false,
      onUserBack: (depth) => notifyHistory(dotnet, 'OnHistoryBack', depth),
    });
    /** @param {KeyboardEvent} event */
    const onKeyDown = (event) => {
      if (shouldHandleEscape(event, document)) notifyHistory(dotnet, 'OnEscape');
    };
    document.addEventListener('keydown', onKeyDown);
    attachments += 1;
    attachment = { token: attachments, controller, cleanups: [() => document.removeEventListener('keydown', onKeyDown)] };
    return attachment.token;
  } catch (error) {
    console.warn('[realmShell] attachHistory failed', error);
    return 0;
  }
}

/**
 * Stops the listeners of an attachment. A token that is not the current one (the page that attached was replaced by the next page) does nothing; no token detaches the current one.
 * The entries already pushed stay: the tab navigation takes them back with `history.setDepth(0)` before it leaves.
 * @param {number} [token]
 */
export function detachHistory(token) {
  try {
    if (attachment && (token === undefined || token === attachment.token)) teardownHistory();
  } catch (error) {
    console.warn('[realmShell] detachHistory failed', error);
  }
}

/** The history helpers of 4.8: `setDepth(n)` (resolves when the browser has settled) and `getDepth()`; both do nothing while no page is attached. */
export const history = {
  /**
   * @param {number} depth
   * @returns {Promise<void>}
   */
  setDepth(depth) {
    return attachment ? attachment.controller.setDepth(depth) : Promise.resolve();
  },
  /** @returns {number} */
  getDepth() {
    return attachment ? attachment.controller.getDepth() : 0;
  },
};


// ---- S10a: device preferences (01 section 7.9, 03 section 4.8) -----------------------------------------------------------------------------
// localStorage is read and written only from here, and only after the first render (the circuit exists then; prerendering has no browser). Every access is in
// a try/catch and every failure reads as "no stored value", so a private window or blocked site data leaves the defaults in force. Only keys that start with
// `realm.` are ever touched: `realm.mapStyle`, `realm.showZones`, `realm.viewRadiusKm` and `realm.layout` (DevicePrefs.cs holds the list and the defaults).

/** Every key of ours starts with this; anything else is refused. */
const PREFS_PREFIX = 'realm.';

/**
 * localStorage, or null when the browser refuses it (the getter itself can throw in a private window or with blocked site data).
 * @returns {Storage | null}
 */
function deviceStorage() {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

/**
 * Whether `key` is one of ours.
 * @param {unknown} key
 * @returns {key is string}
 */
export function isPrefKey(key) {
  return typeof key === 'string' && key.length > PREFS_PREFIX.length && key.startsWith(PREFS_PREFIX);
}

/** The stored device preferences. None of these throws; `set` says whether the value was stored. */
export const prefs = {
  /**
   * Every stored `realm.*` value, by key. Empty when storage is unavailable.
   * @returns {Record<string, string>}
   */
  getAll() {
    /** @type {Record<string, string>} */
    const all = {};
    try {
      const store = deviceStorage();
      if (!store) return all;
      for (let index = 0; index < store.length; index++) {
        const key = store.key(index);
        if (!isPrefKey(key)) continue;
        const value = store.getItem(key);
        if (value !== null) all[key] = value;
      }
    } catch {
      // Unreadable storage is an empty one.
    }
    return all;
  },

  /**
   * @param {string} key a `realm.*` key
   * @param {string} value
   * @returns {boolean} true when the value was stored (false for a foreign key, a full or blocked storage)
   */
  set(key, value) {
    if (!isPrefKey(key)) return false;
    try {
      const store = deviceStorage();
      if (!store) return false;
      store.setItem(key, String(value));
      return true;
    } catch {
      return false;
    }
  },

  /** @param {string} key a `realm.*` key */
  remove(key) {
    if (!isPrefKey(key)) return;
    try {
      deviceStorage()?.removeItem(key);
    } catch {
      // Nothing to remove from storage that cannot be reached.
    }
  },

  /** Removes every `realm.*` value (the C# side has no caller in v1: "Reset device settings" is not in v1). */
  reset() {
    try {
      const store = deviceStorage();
      if (!store) return;
      const keys = [];
      for (let index = 0; index < store.length; index++) {
        const key = store.key(index);
        if (isPrefKey(key)) keys.push(key);
      }
      for (const key of keys) store.removeItem(key);
    } catch {
      // As above.
    }
  },
};
