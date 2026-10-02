// Tests for the history depth tokens and the global Escape handler of wwwroot/js/realmShell.js (03 sections 3.7 and 4.8, R-086, D47): the pure helpers, the controller against a
// fake browser history (push, suppressed go, user Back and Forward, the kill switch, serialisation, the settle timeout), and attach and detach against fake globals. The module touches
// no global when it is imported; the fakes are installed per test.
import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';

import {
  DIALOG_LAYER_SELECTOR,
  FOREIGN_POPOVER_SELECTOR,
  HISTORY_SETTLE_MS,
  MAX_HISTORY_DEPTH,
  OWN_POPOVER_SELECTOR,
  attachHistory,
  createHistoryController,
  depthOfEntry,
  detachHistory,
  foreignLayerOpen,
  history,
  shouldHandleEscape,
  tokenUrl,
} from '../../src/Realm.Web/wwwroot/js/realmShell.js';

// ---- a fake browser history -------------------------------------------------------------------------------------------------------------------

/**
 * A session history as the browser has it: entries, a current index, popstate listeners, and an asynchronous `go` (the popstate arrives on a later turn, as in a browser).
 * `swallowGo` models a `go` that never fires popstate (a target outside the joint history).
 */
function fakeBrowser({ state = null, hash = '', swallowGo = false } = {}) {
  const entries = [{ state, hash }];
  let index = 0;
  const listeners = new Set();
  const calls = { pushes: [], goes: [] };
  const timers = [];
  const firePopState = () => {
    for (const listener of [...listeners]) listener();
  };
  const env = {
    getState: () => entries[index].state,
    getHash: () => entries[index].hash,
    push: (pushed, depth) => {
      entries.splice(index + 1);
      entries.push({ state: pushed, hash: `#r${depth}` });
      index += 1;
      calls.pushes.push(depth);
    },
    go: (delta) => {
      calls.goes.push(delta);
      if (swallowGo) return;
      queueMicrotask(() => {
        const to = index + delta;
        if (to < 0 || to >= entries.length) return;
        index = to;
        firePopState();
      });
    },
    onPopState: (handler) => {
      listeners.add(handler);
      return () => listeners.delete(handler);
    },
    setTimer: (callback, ms) => {
      const timer = { callback, ms, live: true };
      timers.push(timer);
      return timer;
    },
    clearTimer: (timer) => {
      timer.live = false;
    },
  };
  return {
    env,
    calls,
    timers,
    entries,
    index: () => index,
    listenerCount: () => listeners.size,
    /** The person's Back (or Forward) gesture: moves the index and fires popstate, which nobody asked for. */
    user: (delta) => {
      index += delta;
      firePopState();
    },
  };
}

function controllerOver(browser, { tokens = true } = {}) {
  const reports = [];
  const controller = createHistoryController(browser.env, { tokens, onUserBack: (depth) => reports.push(depth) });
  return { controller, reports };
}

// ---- the pure helpers -------------------------------------------------------------------------------------------------------------------------

test('depthOfEntry reads the token of the state, else of the URL fragment, else 0', () => {
  assert.equal(depthOfEntry({ realmDepth: 2 }, ''), 2);
  assert.equal(depthOfEntry(null, '#r3'), 3);
  assert.equal(depthOfEntry(undefined, '#r1'), 1);
  assert.equal(depthOfEntry({ realmDepth: 2 }, '#r1'), 2, 'the state wins');
  assert.equal(depthOfEntry({ _index: 4, userState: null }, '#r1'), 1, 'Blazor replaced the state and kept the URL: the fragment still says');
  assert.equal(depthOfEntry({ _index: 4 }, ''), 0);
  assert.equal(depthOfEntry(null, ''), 0);
  assert.equal(depthOfEntry(null, '#section'), 0);
  assert.equal(depthOfEntry(null, '#r0'), 0);
  assert.equal(depthOfEntry(null, '#r-1'), 0);
  assert.equal(depthOfEntry(null, '#r1x'), 0);
  assert.equal(depthOfEntry({ realmDepth: 0 }, ''), 0);
  assert.equal(depthOfEntry({ realmDepth: 1.5 }, ''), 0);
  assert.equal(depthOfEntry({ realmDepth: '2' }, ''), 0);
  assert.equal(depthOfEntry({ realmDepth: MAX_HISTORY_DEPTH + 1 }, ''), 0);
  assert.equal(depthOfEntry('r2', 7), 0);
});

test('tokenUrl keeps the page path and query and changes only the fragment (a bare #r1 would resolve against <base href>)', () => {
  assert.equal(tokenUrl('driving', '?week=2', 1), 'driving?week=2#r1');
  assert.equal(tokenUrl('api/hassio_ingress/token/', '', 2), 'api/hassio_ingress/token/#r2');
  assert.notEqual(tokenUrl('driving', '', 1), '#r1');
});

// ---- setDepth, tokens on ----------------------------------------------------------------------------------------------------------------------

test('setDepth pushes one #r<n> entry per missing level, with the token in the state', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);

  await controller.setDepth(2);

  assert.deepEqual(browser.calls.pushes, [1, 2]);
  assert.deepEqual(browser.entries.map((entry) => entry.hash), ['', '#r1', '#r2']);
  assert.deepEqual(browser.entries.map((entry) => entry.state), [null, { realmDepth: 1 }, { realmDepth: 2 }]);
  assert.equal(controller.getDepth(), 2);
  assert.deepEqual(browser.calls.goes, []);
  assert.deepEqual(reports, []);
});

test('setDepth is idempotent: the depth it already has pushes and pops nothing', async () => {
  const browser = fakeBrowser();
  const { controller } = controllerOver(browser);
  await controller.setDepth(2);

  await controller.setDepth(2);
  await controller.setDepth(0 + 2);

  assert.deepEqual(browser.calls.pushes, [1, 2]);
  assert.deepEqual(browser.calls.goes, []);
});

test('setDepth goes back by the surplus in one history.go, resolves when the popstate fired, and does not report that popstate as a user Back', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(3);

  await controller.setDepth(1);

  assert.deepEqual(browser.calls.goes, [-2]);
  assert.equal(browser.index(), 1);
  assert.equal(controller.getDepth(), 1);
  assert.deepEqual(reports, [], 'a popstate that setDepth caused is not a Back the person made');
  assert.ok(browser.timers.every((timer) => !timer.live), 'the settle timer is cleared once the popstate arrived');
});

test('a push after a pop replaces the forward entries', async () => {
  const browser = fakeBrowser();
  const { controller } = controllerOver(browser);
  await controller.setDepth(2);
  await controller.setDepth(0);

  await controller.setDepth(1);

  assert.deepEqual(browser.entries.map((entry) => entry.hash), ['', '#r1']);
  assert.equal(controller.getDepth(), 1);
});

test('calls run one after the other, in the order made, even though history.go is asynchronous', async () => {
  const browser = fakeBrowser();
  const { controller } = controllerOver(browser);

  const all = [controller.setDepth(2), controller.setDepth(0), controller.setDepth(1)];
  await Promise.all(all);

  assert.deepEqual(browser.calls.pushes, [1, 2, 1]);
  assert.deepEqual(browser.calls.goes, [-2]);
  assert.deepEqual(browser.entries.map((entry) => entry.hash), ['', '#r1']);
  assert.equal(controller.getDepth(), 1);
});

test('setDepth clamps what C# sends: negative and non-finite mean 0, huge means the maximum', async () => {
  const browser = fakeBrowser();
  const { controller } = controllerOver(browser);

  await controller.setDepth(Number.NaN);
  assert.equal(controller.getDepth(), 0);
  await controller.setDepth(-3);
  assert.equal(controller.getDepth(), 0);
  await controller.setDepth(2.9);
  assert.equal(controller.getDepth(), 2);
  await controller.setDepth(5000);
  assert.equal(controller.getDepth(), MAX_HISTORY_DEPTH);
  assert.equal(browser.calls.pushes.length, MAX_HISTORY_DEPTH);
});

test('the controller starts from the depth of the entry it finds (a reload on #r2, or Blazor having replaced the state)', async () => {
  const reloaded = fakeBrowser({ state: { realmDepth: 2 }, hash: '#r2' });
  assert.equal(controllerOver(reloaded).controller.getDepth(), 2);

  const replaced = fakeBrowser({ state: { _index: 3 }, hash: '#r1' });
  assert.equal(controllerOver(replaced).controller.getDepth(), 1);

  const base = fakeBrowser();
  assert.equal(controllerOver(base).controller.getDepth(), 0);
});

// ---- a Back or a Forward the person made ------------------------------------------------------------------------------------------------------

test('a user Back is reported with the depth the browser is at', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(2);

  browser.user(-1);

  assert.deepEqual(reports, [1]);
  assert.equal(controller.getDepth(), 1);
});

test('a Back by several entries is reported once, with the depth it landed on', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(3);

  browser.user(-3);

  assert.deepEqual(reports, [0]);
  assert.equal(controller.getDepth(), 0);
});

test('a Forward is reported too, with the higher depth, so that C# can undo it', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(2);
  browser.user(-2);
  reports.length = 0;

  browser.user(+1);

  assert.deepEqual(reports, [1]);
  assert.equal(controller.getDepth(), 1);

  await controller.setDepth(0);   // C# undoes it: back to where its state is
  assert.equal(browser.index(), 0);
  assert.deepEqual(reports, [1], 'undoing it is our own go and is not reported again');
});

test('converging after a multi-step Back pushes the missing entries again', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(3);
  browser.user(-3);   // depth 0; C# reduces once, so its state is at 2

  assert.deepEqual(reports, [0]);
  await controller.setDepth(2);

  assert.deepEqual(browser.entries.map((entry) => entry.hash), ['', '#r1', '#r2']);
  assert.equal(controller.getDepth(), 2);
});

// ---- the kill switch (D47) --------------------------------------------------------------------------------------------------------------------

test('with the tokens off setDepth records the depth and touches nothing', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser, { tokens: false });

  await controller.setDepth(2);
  assert.equal(controller.getDepth(), 2);
  await controller.setDepth(1);
  assert.equal(controller.getDepth(), 1);
  await controller.setDepth(0);

  assert.deepEqual(browser.calls.pushes, []);
  assert.deepEqual(browser.calls.goes, []);
  assert.equal(browser.entries.length, 1);
  assert.deepEqual(reports, []);
});

test('with the tokens off a popstate is nobody\'s business and the recorded depth stands', async () => {
  const browser = fakeBrowser();
  const { controller, reports } = controllerOver(browser, { tokens: false });
  await controller.setDepth(2);

  browser.entries.push({ state: null, hash: '' });
  browser.user(+1);

  assert.deepEqual(reports, []);
  assert.equal(controller.getDepth(), 2);
});

// ---- failure and teardown ---------------------------------------------------------------------------------------------------------------------

test('a go whose popstate never comes resolves after the settle timeout, reading where the browser is', async () => {
  const browser = fakeBrowser({ swallowGo: true });
  const { controller } = controllerOver(browser);
  await controller.setDepth(2);

  const settled = controller.setDepth(0);
  await new Promise((resolve) => setTimeout(resolve, 0));
  const timer = browser.timers.find((candidate) => candidate.live);
  assert.ok(timer, 'a settle timer is waiting');
  assert.equal(timer.ms, HISTORY_SETTLE_MS);
  timer.callback();
  await settled;

  assert.deepEqual(browser.calls.goes, [-2]);
  assert.equal(controller.getDepth(), 2, 'the browser never moved, and the controller says where it really is');
});

test('a push that throws is logged and setDepth still resolves, and the queue goes on', async () => {
  const browser = fakeBrowser();
  const warnings = [];
  const original = console.warn;
  console.warn = (...args) => warnings.push(args);
  try {
    const { controller } = controllerOver(browser);
    const push = browser.env.push;
    let fail = true;
    browser.env.push = (...args) => {
      if (fail) throw new Error('SecurityError');
      push(...args);
    };

    await controller.setDepth(1);
    fail = false;
    await controller.setDepth(1);

    assert.equal(warnings.length, 1);
    assert.equal(controller.getDepth(), 1);
  } finally {
    console.warn = original;
  }
});

test('dispose removes the listener, ends a pending wait, and makes setDepth a no-op', async () => {
  const browser = fakeBrowser({ swallowGo: true });
  const { controller, reports } = controllerOver(browser);
  await controller.setDepth(2);
  const pending = controller.setDepth(0);
  await new Promise((resolve) => setTimeout(resolve, 0));

  controller.dispose();
  await pending;

  assert.equal(browser.listenerCount(), 0);
  browser.user(-1);
  assert.deepEqual(reports, []);
  await controller.setDepth(5);
  assert.deepEqual(browser.calls.pushes, [1, 2]);
});

// ---- Escape -----------------------------------------------------------------------------------------------------------------------------------

/** A root that finds only the selectors it was told about. */
function rootWith(...open) {
  return { querySelector: (selector) => (open.includes(selector) ? {} : null) };
}

const ESCAPE = { key: 'Escape', defaultPrevented: false, isComposing: false, target: { tagName: 'DIV' } };

test('Escape is ours when nothing else owns it', () => {
  assert.equal(shouldHandleEscape(ESCAPE, rootWith()), true);
  assert.equal(shouldHandleEscape({ ...ESCAPE, key: 'Esc' }, rootWith()), true);
});

test('only the Escape key is ours', () => {
  assert.equal(shouldHandleEscape({ ...ESCAPE, key: 'Enter' }, rootWith()), false);
  assert.equal(shouldHandleEscape({ ...ESCAPE, key: 'e' }, rootWith()), false);
  assert.equal(shouldHandleEscape({}, rootWith()), false);
});

test('a default-prevented or composing keydown is not ours', () => {
  assert.equal(shouldHandleEscape({ ...ESCAPE, defaultPrevented: true }, rootWith()), false);
  assert.equal(shouldHandleEscape({ ...ESCAPE, isComposing: true }, rootWith()), false);
});

test('Escape typed into an input, a textarea, a select or contenteditable is the field\'s own', () => {
  for (const target of [{ tagName: 'INPUT' }, { tagName: 'textarea' }, { tagName: 'SELECT' }, { tagName: 'DIV', isContentEditable: true }]) {
    assert.equal(shouldHandleEscape({ ...ESCAPE, target }, rootWith()), false, JSON.stringify(target));
  }
  assert.equal(shouldHandleEscape({ ...ESCAPE, target: { tagName: 'BUTTON' } }, rootWith()), true);
  assert.equal(shouldHandleEscape({ ...ESCAPE, target: null }, rootWith()), true, 'the document itself');
});

test('an open dialog owns Escape: MudBlazor closes it, and the sheet must not step as well', () => {
  assert.equal(shouldHandleEscape(ESCAPE, rootWith(DIALOG_LAYER_SELECTOR)), false);
});

test('an open popover that is not ours owns Escape', () => {
  assert.equal(shouldHandleEscape(ESCAPE, rootWith(FOREIGN_POPOVER_SELECTOR)), false);
});

test('the foreign popover selector leaves out the sheet (it is itself an open MudPopover), our style popover and tooltips', () => {
  assert.ok(FOREIGN_POPOVER_SELECTOR.startsWith('.mud-popover-open'));
  for (const own of ['.mud-sheet-popover', '.realm-style-popover', '.mud-tooltip']) {
    assert.ok(FOREIGN_POPOVER_SELECTOR.includes(`:not(${own})`), `${own} is excluded`);
  }
  // With only the sheet's popover open the selector matches nothing, which is what the fake root says for it.
  assert.equal(foreignLayerOpen(rootWith('.mud-popover-open')), false);
});

test('Escape pressed inside our own style popover is the popover\'s own: it closes itself', () => {
  const inside = { tagName: 'BUTTON', closest: (selector) => (selector === OWN_POPOVER_SELECTOR ? {} : null) };
  const outside = { tagName: 'BUTTON', closest: () => null };
  assert.equal(shouldHandleEscape({ ...ESCAPE, target: inside }, rootWith()), false);
  assert.equal(shouldHandleEscape({ ...ESCAPE, target: outside }, rootWith()), true);
});

// ---- attach and detach against fake globals ---------------------------------------------------------------------------------------------------

const GLOBAL_KEYS = ['window', 'document'];

afterEach(() => {
  detachHistory();
  for (const key of GLOBAL_KEYS) delete globalThis[key];
});

function installGlobals({ pathname = 'driving', search = '?week=1', hash = '', state = null } = {}) {
  const windowListeners = new Map();
  const documentListeners = new Map();
  const bucketOf = (map, type) => {
    if (!map.has(type)) map.set(type, new Set());
    return map.get(type);
  };
  const location = { pathname, search, hash };
  const pushed = [];
  const fakeHistory = {
    state,
    pushState: (nextState, _title, url) => {
      pushed.push({ state: nextState, url });
      fakeHistory.state = nextState;
      location.hash = url.slice(url.indexOf('#'));
    },
    go: () => {},
  };
  globalThis.window = {
    history: fakeHistory,
    location,
    addEventListener: (type, handler) => bucketOf(windowListeners, type).add(handler),
    removeEventListener: (type, handler) => bucketOf(windowListeners, type).delete(handler),
    setTimeout: (callback, ms) => setTimeout(callback, ms),
    clearTimeout: (handle) => clearTimeout(handle),
  };
  globalThis.document = {
    querySelector: () => null,
    addEventListener: (type, handler) => bucketOf(documentListeners, type).add(handler),
    removeEventListener: (type, handler) => bucketOf(documentListeners, type).delete(handler),
  };
  return {
    pushed,
    location,
    keydownListeners: () => bucketOf(documentListeners, 'keydown').size,
    popstateListeners: () => bucketOf(windowListeners, 'popstate').size,
    keydown: (event) => [...bucketOf(documentListeners, 'keydown')].forEach((handler) => handler(event)),
    popstate: () => [...bucketOf(windowListeners, 'popstate')].forEach((handler) => handler({})),
  };
}

function fakeDotNet() {
  const calls = [];
  return { calls, invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } };
}

test('attachHistory starts the popstate and keydown listeners, history.setDepth pushes the page\'s own URL with a fragment, and Escape reaches .NET', async () => {
  const dom = installGlobals();
  const dotnet = fakeDotNet();

  const token = attachHistory(dotnet, { historyTokens: true });

  assert.ok(token > 0);
  assert.equal(dom.keydownListeners(), 1);
  assert.equal(dom.popstateListeners(), 1);
  await history.setDepth(2);
  assert.deepEqual(dom.pushed.map((entry) => entry.url), ['driving?week=1#r1', 'driving?week=1#r2']);
  assert.equal(history.getDepth(), 2);

  dom.keydown({ key: 'Escape', defaultPrevented: false, target: { tagName: 'DIV' } });
  dom.keydown({ key: 'a', target: { tagName: 'DIV' } });
  assert.deepEqual(dotnet.calls, [['OnEscape']]);
});

test('a popstate the script did not cause reaches .NET as OnHistoryBack(depth)', async () => {
  const dom = installGlobals();
  const dotnet = fakeDotNet();
  attachHistory(dotnet);
  await history.setDepth(2);

  globalThis.window.history.state = { realmDepth: 1 };
  dom.location.hash = '#r1';
  dom.popstate();

  assert.deepEqual(dotnet.calls, [['OnHistoryBack', 1]]);
});

test('historyTokens false: attachHistory records and pushes nothing, and a popstate is not reported', async () => {
  const dom = installGlobals();
  const dotnet = fakeDotNet();
  attachHistory(dotnet, { historyTokens: false });

  await history.setDepth(2);
  assert.equal(history.getDepth(), 2);
  dom.popstate();

  assert.deepEqual(dom.pushed, []);
  assert.deepEqual(dotnet.calls, []);
});

test('Escape and Back still reach .NET after a failed .NET call: a rejected invokeMethodAsync is swallowed', async () => {
  const dom = installGlobals();
  const dotnet = { invokeMethodAsync: () => Promise.reject(new Error('There is no tracked object with id 1')) };
  attachHistory(dotnet);

  dom.keydown({ key: 'Escape', target: { tagName: 'DIV' } });
  await new Promise((resolve) => setTimeout(resolve, 0));

  const throwing = { invokeMethodAsync: () => { throw new Error('disposed'); } };
  attachHistory(throwing);
  dom.keydown({ key: 'Escape', target: { tagName: 'DIV' } });
});

test('a second attach replaces the first, and the first one\'s detach (the page that was replaced) leaves the second alone', () => {
  const dom = installGlobals();
  const first = attachHistory(fakeDotNet());
  const second = attachHistory(fakeDotNet());

  assert.notEqual(first, second);
  assert.equal(dom.keydownListeners(), 1);
  assert.equal(dom.popstateListeners(), 1);

  detachHistory(first);
  assert.equal(dom.keydownListeners(), 1, 'a stale token does nothing');

  detachHistory(second);
  assert.equal(dom.keydownListeners(), 0);
  assert.equal(dom.popstateListeners(), 0);
});

test('detachHistory without a token detaches the current attachment, and twice is safe', () => {
  const dom = installGlobals();
  attachHistory(fakeDotNet());

  detachHistory();
  detachHistory();

  assert.equal(dom.keydownListeners(), 0);
  assert.equal(dom.popstateListeners(), 0);
});

test('history.setDepth and getDepth do nothing while no page is attached', async () => {
  installGlobals();

  await history.setDepth(3);

  assert.equal(history.getDepth(), 0);
});

test('attachHistory never throws: without a document it logs and returns 0', () => {
  const original = console.warn;
  const warnings = [];
  console.warn = (...args) => warnings.push(args);
  try {
    assert.equal(attachHistory(fakeDotNet()), 0);
    assert.equal(warnings.length, 1);
  } finally {
    console.warn = original;
  }
});
