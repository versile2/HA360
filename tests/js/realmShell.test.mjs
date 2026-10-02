// Tests for wwwroot/js/realmShell.js (03 section 4.8): the pure helpers, the sheet metrics (observeSheet, --realm-sheet-h, data-sheet-tall, the
// sheetMetrics subscription that feeds the map padding), the viewport report and the handle tap shim, run against a small fake DOM. The module touches
// no global when it is imported, so the fakes are installed per test.
import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';

import { DEFAULT_LAYOUT, computePadding } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';
import {
  CLICK_GRACE_MS,
  TAP_MAX_MS,
  TAP_SLOP_PX,
  describeSheet,
  dispose,
  findSheetElement,
  init,
  isSheetTall,
  isTap,
  observeSheet,
  readSheet,
  readViewport,
  setLayoutAttr,
  sheetHeightForPadding,
  sheetMetrics,
} from '../../src/Realm.Web/wwwroot/js/realmShell.js';

const SELECTOR = 'div[mudsheet], [data-testid="sheet"]';
const PHONE = { width: 412, height: 915 };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ---- a fake DOM: just what realmShell.js reads ----------------------------------------------------------------------------------------------

class FakeElement {
  constructor(attributes = {}, rect = { top: 0, height: 0 }) {
    this.attributes = { ...attributes };
    this.rect = rect;
    this.clicks = 0;
    this.children = [];
    this.parent = null;
  }
  getAttribute(name) {
    return name in this.attributes ? this.attributes[name] : null;
  }
  getBoundingClientRect() {
    return { top: this.rect.top, height: this.rect.height, bottom: this.rect.top + this.rect.height, left: 0, right: 0, width: 0 };
  }
  closest(selector) {
    for (let node = this; node; node = node.parent) if (node.matches(selector)) return node;
    return null;
  }
  matches(selector) {
    if (selector === '.mud-sheet-handle') return this.attributes.class === 'mud-sheet-handle';
    if (selector === '[data-testid="sheet-handle"]') return this.attributes['data-testid'] === 'sheet-handle';
    return false;
  }
  querySelector(selector) {
    for (const child of this.children) {
      if (child.matches(selector)) return child;
      const deeper = child.querySelector(selector);
      if (deeper) return deeper;
    }
    return null;
  }
  add(child) {
    child.parent = this;
    this.children.push(child);
    return child;
  }
  click() {
    this.clicks += 1;
  }
}

/** Installs window, document and the observers; returns handles to drive them. */
function installDom({ width = PHONE.width, height = PHONE.height } = {}) {
  const attributes = new Map();
  const properties = new Map();
  const html = {
    getAttribute: (name) => (attributes.has(name) ? attributes.get(name) : null),
    setAttribute: (name, value) => attributes.set(name, String(value)),
    removeAttribute: (name) => attributes.delete(name),
    style: { setProperty: (name, value) => properties.set(name, value), removeProperty: (name) => properties.delete(name) },
    appendChild: () => {},
  };
  const listeners = { document: new Map(), window: new Map() };
  const addTo = (bucket) => (type, handler) => {
    if (!bucket.has(type)) bucket.set(type, new Set());
    bucket.get(type).add(handler);
  };
  const removeFrom = (bucket) => (type, handler) => bucket.get(type)?.delete(handler);
  /** @type {Map<string, FakeElement>} selector -> the element a querySelector of exactly that selector finds */
  const found = new Map();
  const scripts = [];
  const mutationObservers = [];
  const resizeObservers = [];

  class FakeMutationObserver {
    constructor(callback) {
      this.callback = callback;
      this.connected = false;
      mutationObservers.push(this);
    }
    observe() {
      this.connected = true;
    }
    disconnect() {
      this.connected = false;
    }
  }
  class FakeResizeObserver {
    constructor(callback) {
      this.callback = callback;
      this.target = null;
      resizeObservers.push(this);
    }
    observe(target) {
      this.target = target;
    }
    disconnect() {
      this.target = null;
    }
  }

  const fakeWindow = {
    innerWidth: width,
    innerHeight: height,
    devicePixelRatio: 2,
    visualViewport: undefined,
    mudsheetHelper: {},
    matchMedia: () => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} }),
    addEventListener: addTo(listeners.window),
    removeEventListener: removeFrom(listeners.window),
    setTimeout: (...args) => setTimeout(...args),
  };
  const fakeDocument = {
    documentElement: html,
    body: new FakeElement(),
    head: { appendChild: (script) => scripts.push(script) },
    createElement: () => ({ setAttribute() {}, style: {}, remove() {}, appendChild() {} }),
    querySelector: (selector) => (selector === 'script[data-mudx-js]' ? (scripts[0] ?? null) : (found.get(selector) ?? null)),
    addEventListener: addTo(listeners.document),
    removeEventListener: removeFrom(listeners.document),
  };
  Object.assign(globalThis, {
    window: fakeWindow,
    document: fakeDocument,
    Element: FakeElement,
    HTMLElement: FakeElement,
    MutationObserver: FakeMutationObserver,
    ResizeObserver: FakeResizeObserver,
    getComputedStyle: () => ({ paddingTop: '0px', paddingRight: '0px', paddingBottom: '34px', paddingLeft: '0px' }),
  });

  const dispatch = (target, type, event) => {
    for (const handler of [...(listeners[target].get(type) ?? [])]) handler(event);
  };
  return {
    attributes,
    properties,
    found,
    scripts,
    window: fakeWindow,
    mutate: () => mutationObservers.filter((o) => o.connected).forEach((o) => o.callback([])),
    resize: () => resizeObservers.filter((o) => o.target).forEach((o) => o.callback([])),
    liveResizeObservers: () => resizeObservers.filter((o) => o.target).length,
    liveMutationObservers: () => mutationObservers.filter((o) => o.connected).length,
    listenerCount: () => [...listeners.document.values(), ...listeners.window.values()].reduce((n, set) => n + set.size, 0),
    fire: (type, event) => dispatch('document', type, event),
    fireWindow: (type, event) => dispatch('window', type, event),
  };
}

const KEYS = ['window', 'document', 'Element', 'HTMLElement', 'MutationObserver', 'ResizeObserver', 'getComputedStyle'];
afterEach(() => {
  dispose();
  for (const key of KEYS) delete globalThis[key];
});

// ---- pure helpers -----------------------------------------------------------------------------------------------------------------------------

test('isTap: a short, still press is a tap; a drag, a slow press and a bad sample are not', () => {
  const down = { x: 100, y: 800, t: 0 };
  assert.equal(isTap(down, { x: 100, y: 800, t: 80 }), true);
  assert.equal(isTap(down, { x: 103, y: 804, t: 120 }), true, 'a few pixels of finger jitter');
  assert.equal(isTap(down, { x: 100, y: 800 - TAP_SLOP_PX, t: 90 }), true, 'exactly at the slop');
  assert.equal(isTap(down, { x: 100, y: 800 - TAP_SLOP_PX - 1, t: 90 }), false, 'just past the slop');
  assert.equal(isTap(down, { x: 100, y: 700, t: 150 }), false, 'a 100 px drag');
  assert.equal(isTap(down, { x: 100, y: 800, t: TAP_MAX_MS }), true);
  assert.equal(isTap(down, { x: 100, y: 800, t: TAP_MAX_MS + 1 }), false, 'a long press');
  assert.equal(isTap(down, { x: 100, y: 800, t: -5 }), false, 'time running backwards');
  assert.equal(isTap(down, { x: Number.NaN, y: 800, t: 10 }), false);
});

test('isSheetTall: from half the viewport height up in Compact, never in the panel, never without a measurement', () => {
  assert.equal(isSheetTall(174, 915, 'compact'), false, 'Peek');
  assert.equal(isSheetTall(168, 800, 'compact'), false, 'Peek at the 168 floor');
  assert.equal(isSheetTall(732, 915, 'compact'), true, 'Tall');
  assert.equal(isSheetTall(457, 915, 'compact'), true, '49.9 percent counts as the half (the 0.5 px tolerance)');
  assert.equal(isSheetTall(456, 915, 'compact'), false);
  assert.equal(isSheetTall(640, 800, 'compact'), true);
  assert.equal(isSheetTall(732, 915, null), true, 'no layout attribute yet behaves as Compact');
  assert.equal(isSheetTall(824, 916, 'expanded'), false, 'the panel is as tall as the window and stays put');
  assert.equal(isSheetTall(0, 915, 'compact'), false);
  assert.equal(isSheetTall(Number.NaN, 915, 'compact'), false);
  assert.equal(isSheetTall(300, 0, 'compact'), false);
});

test('sheetHeightForPadding: the measured height, or null while there is no sheet', () => {
  assert.equal(sheetHeightForPadding(174), 174);
  assert.equal(sheetHeightForPadding(732.4), 732.4);
  assert.equal(sheetHeightForPadding(0), null);
  assert.equal(sheetHeightForPadding(-1), null);
  assert.equal(sheetHeightForPadding(Number.NaN), null);
  assert.equal(sheetHeightForPadding(Number.POSITIVE_INFINITY), null);
});

test('findSheetElement tries the selector list in the order written: the popover wins over the contract element', () => {
  const popover = new FakeElement({ mudsheet: '' });
  const contract = new FakeElement({ 'data-testid': 'sheet' });
  const root = { querySelector: (selector) => ({ 'div[mudsheet]': popover, '[data-testid="sheet"]': contract })[selector] ?? null };
  assert.equal(findSheetElement(root, SELECTOR), popover);
  assert.equal(findSheetElement(root, '[data-testid="sheet"], div[mudsheet]'), contract, 'the order is the caller\'s');
  const aside = { querySelector: (selector) => (selector === '[data-testid="sheet"]' ? contract : null) };
  assert.equal(findSheetElement(aside, SELECTOR), contract, 'with no popover (the aside host) the contract element is the sheet');
  assert.equal(findSheetElement({ querySelector: () => null }, SELECTOR), null);
  assert.equal(findSheetElement(root, ' , '), null);
});

test('describeSheet: state from data-state, geometry from the metrics, open only with an element and a height', () => {
  const metrics = { heightPx: 174, topPx: 741 };
  assert.deepEqual(describeSheet('peek', metrics, true), { state: 'peek', heightPx: 174, topPx: 741, open: true });
  assert.deepEqual(describeSheet('80', { heightPx: 732, topPx: 183 }, true), { state: '80', heightPx: 732, topPx: 183, open: true });
  assert.equal(describeSheet('panel', { heightPx: 824, topPx: 76 }, true).state, 'panel');
  assert.equal(describeSheet('512', metrics, true).state, null, 'a state outside the three is no state');
  assert.equal(describeSheet(null, metrics, false).state, null);
  assert.equal(describeSheet('peek', { heightPx: 0, topPx: 0 }, true).open, false);
  assert.equal(describeSheet('peek', metrics, false).open, false);
});

// ---- the sheet metrics against a fake DOM (03 section 4.8) ------------------------------------------------------------------------------------

test('observeSheet follows the popover: --realm-sheet-h, --realm-sheet-top, data-sheet-tall and the subscribers', () => {
  const dom = installDom();
  setLayoutAttr('compact');
  const seen = [];
  const unsubscribe = sheetMetrics.subscribe((m) => seen.push(m));

  observeSheet(SELECTOR);
  assert.deepEqual(sheetMetrics.current(), { heightPx: 0, topPx: 0 }, 'no element yet');
  assert.equal(dom.properties.has('--realm-sheet-h'), false, 'no variable without a sheet, so the CSS fallback (Peek) applies');
  assert.equal(dom.liveMutationObservers(), 1, 'a MutationObserver waits for MudX to mount the popover');

  // MudX mounts the popover at Peek: 174 px, top 741.
  const popover = new FakeElement({ mudsheet: '' }, { top: 741, height: 174 });
  dom.found.set('div[mudsheet]', popover);
  dom.mutate();
  assert.deepEqual(sheetMetrics.current(), { heightPx: 174, topPx: 741 });
  assert.equal(dom.properties.get('--realm-sheet-h'), '174px');
  assert.equal(dom.properties.get('--realm-sheet-top'), '741px');
  assert.equal(dom.attributes.has('data-sheet-tall'), false, 'Peek keeps the right stack');
  assert.equal(dom.liveResizeObservers(), 1, 'a ResizeObserver on the popover');
  assert.deepEqual(seen, [{ heightPx: 174, topPx: 741 }]);

  // The same mutation again changes nothing (no second observer, no second notification).
  dom.mutate();
  assert.equal(dom.liveResizeObservers(), 1);
  assert.equal(seen.length, 1);

  // A toggle to Tall: 732 px, top 183. The right stack goes.
  popover.rect = { top: 183, height: 732 };
  dom.resize();
  assert.deepEqual(sheetMetrics.current(), { heightPx: 732, topPx: 183 });
  assert.equal(dom.properties.get('--realm-sheet-h'), '732px');
  assert.equal(dom.attributes.has('data-sheet-tall'), true);
  assert.equal(seen.length, 2);

  // A mid-drag height under half (400 of 915): the stack is back and follows the sheet.
  popover.rect = { top: 515, height: 400 };
  dom.resize();
  assert.equal(dom.attributes.has('data-sheet-tall'), false);
  assert.equal(dom.properties.get('--realm-sheet-h'), '400px');

  // The same size again is not a change.
  const count = seen.length;
  dom.resize();
  assert.equal(seen.length, count);

  // Folding to the panel: it is as tall as the window and is never "tall". setLayoutAttr re-derives the flag.
  popover.rect = { top: 76, height: 824 };
  dom.resize();
  assert.equal(dom.attributes.has('data-sheet-tall'), true, 'in Compact an 824 px sheet is tall');
  setLayoutAttr('expanded');
  assert.equal(dom.attributes.get('data-layout'), 'expanded');
  assert.equal(dom.attributes.has('data-sheet-tall'), false, 'in Expanded it is not');
  setLayoutAttr(null);
  assert.equal(dom.attributes.has('data-layout'), false);

  // Stopping resets everything.
  observeSheet(null);
  assert.deepEqual(sheetMetrics.current(), { heightPx: 0, topPx: 0 });
  assert.equal(dom.properties.has('--realm-sheet-h'), false);
  assert.equal(dom.properties.has('--realm-sheet-top'), false);
  assert.equal(dom.liveResizeObservers(), 0);
  assert.equal(dom.liveMutationObservers(), 0);
  unsubscribe();
});

test('observeSheet re-attaches when MudX replaces the popover (Compact to Expanded makes a new one)', () => {
  const dom = installDom();
  observeSheet(SELECTOR);
  const first = new FakeElement({ mudsheet: '' }, { top: 741, height: 174 });
  dom.found.set('div[mudsheet]', first);
  dom.mutate();
  const second = new FakeElement({ mudsheet: '' }, { top: 76, height: 824 });
  dom.found.set('div[mudsheet]', second);
  dom.mutate();
  assert.deepEqual(sheetMetrics.current(), { heightPx: 824, topPx: 76 });
  assert.equal(dom.liveResizeObservers(), 1, 'the old element is no longer observed');

  // The popover is removed (the page is left): zero again.
  dom.found.delete('div[mudsheet]');
  dom.mutate();
  assert.deepEqual(sheetMetrics.current(), { heightPx: 0, topPx: 0 });
  assert.equal(dom.liveResizeObservers(), 0);
});

test('observeSheet with the aside host: the contract element is the sheet', () => {
  const dom = installDom();
  dom.found.set('[data-testid="sheet"]', new FakeElement({ 'data-testid': 'sheet', 'data-state': 'panel' }, { top: 76, height: 824 }));
  observeSheet(SELECTOR);
  assert.deepEqual(sheetMetrics.current(), { heightPx: 824, topPx: 76 });
  const hook = readSheet();
  assert.deepEqual(hook, { state: 'panel', heightPx: 824, topPx: 76, open: true });
});

test('readSheet reads the state from the contract element and reports a missing one as null state, closed', () => {
  const dom = installDom();
  assert.deepEqual(readSheet(), { state: null, heightPx: 0, topPx: 0, open: false });
  const popover = new FakeElement({ mudsheet: '' }, { top: 741, height: 174 });
  dom.found.set('div[mudsheet]', popover);
  dom.found.set('[data-testid="sheet"]', new FakeElement({ 'data-testid': 'sheet', 'data-state': 'peek' }));
  observeSheet(SELECTOR);
  assert.deepEqual(readSheet(), { state: 'peek', heightPx: 174, topPx: 741, open: true });
  dom.found.get('[data-testid="sheet"]').attributes['data-state'] = '80';
  popover.rect = { top: 183, height: 732 };
  dom.resize();
  assert.deepEqual(readSheet(), { state: '80', heightPx: 732, topPx: 183, open: true });
});

test('the metrics feed the map padding: Peek gives bottom 190, Tall gives 748 with the stack gone, no sheet falls back to Peek', () => {
  installDom();
  const layoutAt = (stackVisible) => ({ ...DEFAULT_LAYOUT, stackVisible });
  assert.deepEqual(computePadding(PHONE, layoutAt(true), sheetHeightForPadding(174)), { top: 72, right: 72, bottom: 190, left: 16 });
  assert.deepEqual(computePadding(PHONE, layoutAt(false), sheetHeightForPadding(732)), { top: 72, right: 16, bottom: 748, left: 16 });
  assert.deepEqual(computePadding(PHONE, layoutAt(true), sheetHeightForPadding(0)), { top: 72, right: 72, bottom: 190, left: 16 }, 'no sheet: the Peek padding of this viewport');
  assert.deepEqual(computePadding({ width: 412, height: 800 }, layoutAt(true), sheetHeightForPadding(168)), { top: 72, right: 72, bottom: 184, left: 16 }, 'the 168 px floor at 412 x 800');
});

test('a subscriber that throws does not stop the others', () => {
  const dom = installDom();
  const seen = [];
  const quiet = console.warn;
  console.warn = () => {};
  const stopA = sheetMetrics.subscribe(() => {
    throw new Error('boom');
  });
  const stopB = sheetMetrics.subscribe((m) => seen.push(m.heightPx));
  dom.found.set('div[mudsheet]', new FakeElement({ mudsheet: '' }, { top: 741, height: 174 }));
  observeSheet(SELECTOR);
  console.warn = quiet;
  assert.deepEqual(seen, [174]);
  stopA();
  stopB();
});

// ---- the viewport -----------------------------------------------------------------------------------------------------------------------------

test('readViewport reports CSS pixels, the orientation, the preferences and the safe area', () => {
  installDom({ width: 884, height: 916 });
  const viewport = readViewport();
  assert.deepEqual(viewport, {
    width: 884,
    height: 916,
    dpr: 2,
    safe: { top: 0, right: 0, bottom: 34, left: 0 },
    reducedMotion: false,
    prefersContrast: false,
    orientation: 'portrait',
  });
  globalThis.window.innerWidth = 915;
  globalThis.window.innerHeight = 412;
  assert.equal(readViewport().orientation, 'landscape');
});

test('init returns the window, loads nothing twice, and reports a resize once, debounced', async () => {
  const dom = installDom();
  const calls = [];
  const dotnet = { invokeMethodAsync: async (name, ...args) => calls.push([name, ...args]) };
  const info = await init(dotnet);
  assert.equal(info.viewport.width, 412);
  assert.equal(dom.scripts.length, 0, 'window.mudsheetHelper is there: no script is added');

  dom.window.innerWidth = 884;
  dom.window.innerHeight = 916;
  dom.fireWindow('resize', {});
  dom.fireWindow('resize', {});
  dom.fireWindow('resize', {});
  assert.equal(calls.length, 0, 'debounced');
  await sleep(160);
  assert.equal(calls.length, 1, 'one report for the burst');
  assert.equal(calls[0][0], 'OnViewport');
  assert.equal(calls[0][1].width, 884);

  // The same window again is not reported twice.
  dom.fireWindow('resize', {});
  await sleep(160);
  assert.equal(calls.length, 1);

  // dispose removes every listener.
  dispose();
  assert.equal(dom.listenerCount(), 0);
  dom.window.innerWidth = 700;
  dom.fireWindow('resize', {});
  await sleep(160);
  assert.equal(calls.length, 1);
});

test('init adds the MudX script when the helper is missing, and waits for it', async () => {
  const dom = installDom();
  delete dom.window.mudsheetHelper;
  let resolved = false;
  const pending = init({ invokeMethodAsync: async () => {} }).then(() => {
    resolved = true;
  });
  await sleep(60);
  assert.equal(dom.scripts.length, 1, 'the script element is added once');
  assert.equal(resolved, false, 'init waits for window.mudsheetHelper');
  dom.window.mudsheetHelper = {};
  await pending;
  assert.equal(resolved, true);
});

// ---- the handle tap shim ----------------------------------------------------------------------------------------------------------------------

function handleFixture() {
  const handle = new FakeElement({ class: 'mud-sheet-handle' });
  const button = handle.add(new FakeElement({ 'data-testid': 'sheet-handle' }));
  const pointer = (target, x, y, id = 1) => ({ target, clientX: x, clientY: y, pointerId: id });
  return { handle, button, pointer };
}

test('tap shim: a tap whose click never reached the button is clicked for it, once', async () => {
  const dom = installDom();
  await init({ invokeMethodAsync: async () => {} });
  const { handle, button, pointer } = handleFixture();
  dom.fire('pointerdown', pointer(button, 200, 880));
  dom.fire('pointerup', pointer(handle, 201, 881), 'MudX captured the pointer: the up event is retargeted to the wrapper');
  await sleep(CLICK_GRACE_MS + 60);
  assert.equal(button.clicks, 1);
});

test('tap shim: a tap that the browser delivered normally is left alone (no second toggle)', async () => {
  const dom = installDom();
  await init({ invokeMethodAsync: async () => {} });
  const { button, pointer } = handleFixture();
  dom.fire('pointerdown', pointer(button, 200, 880));
  dom.fire('pointerup', pointer(button, 200, 880));
  dom.fire('click', { target: button });
  await sleep(CLICK_GRACE_MS + 60);
  assert.equal(button.clicks, 0);
});

test('tap shim: a click that landed on the wrapper instead of the button does not count as delivered', async () => {
  const dom = installDom();
  await init({ invokeMethodAsync: async () => {} });
  const { handle, button, pointer } = handleFixture();
  dom.fire('pointerdown', pointer(button, 200, 880));
  dom.fire('pointerup', pointer(handle, 200, 880));
  dom.fire('click', { target: handle });
  await sleep(CLICK_GRACE_MS + 60);
  assert.equal(button.clicks, 1);
});

test('tap shim: a drag, another pointer, and a press elsewhere do nothing', async () => {
  const dom = installDom();
  await init({ invokeMethodAsync: async () => {} });
  const { handle, button, pointer } = handleFixture();
  dom.fire('pointerdown', pointer(button, 200, 880));
  dom.fire('pointerup', pointer(handle, 200, 600));
  dom.fire('pointerdown', pointer(button, 200, 880, 1));
  dom.fire('pointerup', pointer(handle, 200, 880, 2));
  const elsewhere = new FakeElement({ class: 'map' });
  dom.fire('pointerdown', pointer(elsewhere, 100, 300));
  dom.fire('pointerup', pointer(elsewhere, 100, 300));
  await sleep(CLICK_GRACE_MS + 60);
  assert.equal(button.clicks, 0);
});
