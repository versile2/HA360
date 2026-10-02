// Tests for the credits policy of wwwroot/js/mapStyles.js: which styles open with the map credits expanded and how the credits fold (D81, the
// OSMF Attribution Guidelines; licence audit 1, action 4). The map itself needs WebGL, so only the pure decision and the timer logic run here.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  ATTRIBUTION_FOLD_MS,
  STYLES,
  STYLE_IDS,
  attributionStartsExpanded,
  buildStyle,
  createAttributionFold,
} from '../../src/Realm.Web/wwwroot/js/mapStyles.js';

/** A timer under the test's control: nothing runs until `advance` says so, and a cancelled callback never runs. */
function fakeTimer() {
  let nextId = 1;
  const pending = new Map();
  let now = 0;
  return {
    scheduled: [],
    cancelled: [],
    schedule(callback, delayMs) {
      const id = nextId++;
      pending.set(id, { callback, due: now + delayMs });
      this.scheduled.push({ id, delayMs });
      return id;
    },
    cancel(id) {
      this.cancelled.push(id);
      pending.delete(id);
    },
    advance(ms) {
      now += ms;
      for (const [id, entry] of [...pending]) {
        if (entry.due <= now) {
          pending.delete(id);
          entry.callback();
        }
      }
    },
    /** Runs a callback even though it was cancelled: a timer that had already fired into the queue. */
    fireAnyway(id, callback) {
      pending.delete(id);
      callback();
    },
    get pendingCount() {
      return pending.size;
    },
  };
}

function foldWith(timer, options = {}) {
  const collapsed = [];
  const fold = createAttributionFold({
    schedule: (callback, delayMs) => timer.schedule(callback, delayMs),
    cancel: (handle) => timer.cancel(handle),
    collapse: () => collapsed.push('collapse'),
    ...options,
  });
  return { fold, collapsed };
}

// ---- attributionStartsExpanded -------------------------------------------------------------------------------------------

test('attributionStartsExpanded: every style but demo-offline starts with the credits open', () => {
  assert.equal(attributionStartsExpanded('demo-offline'), false);
  for (const id of ['night', 'day', 'streets', 'satellite']) assert.equal(attributionStartsExpanded(id), true, id);
  assert.deepEqual(
    STYLE_IDS.filter((id) => attributionStartsExpanded(id)),
    ['night', 'day', 'streets', 'satellite'],
  );
});

test('attributionStartsExpanded: an unknown or missing id counts as third-party data (the safe side)', () => {
  for (const value of ['', 'osm bright', 'Night', 'demo', undefined, null]) assert.equal(attributionStartsExpanded(value), true, String(value));
});

test('attributionStartsExpanded: it is true exactly for the styles that load third-party data', () => {
  // A style draws third-party data when MapLibre fetches its document from a URL or one of its sources has tiles or a url. A style added later
  // that does, and is left out of the policy, fails here.
  const drawsThirdPartyData = (id) => {
    const style = buildStyle(id, { demoAttribution: 'Demo map' });
    if (typeof style === 'string') return true;
    return Object.values(style.sources).some((source) => 'tiles' in source || 'url' in source);
  };
  for (const id of STYLE_IDS) {
    assert.equal(attributionStartsExpanded(id), drawsThirdPartyData(id), `${id} (${STYLES[id].url ?? 'built in JavaScript'})`);
  }
});

// ---- createAttributionFold ----------------------------------------------------------------------------------------------

test('the fold delay is five seconds', () => {
  assert.equal(ATTRIBUTION_FOLD_MS, 5000);
});

test('arm: nothing happens before five seconds, then the credits collapse once', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.arm();
  assert.deepEqual(timer.scheduled.map((entry) => entry.delayMs), [5000]);
  timer.advance(4999);
  assert.deepEqual(collapsed, [], 'still open at 4999 ms');
  assert.equal(fold.isDone(), false);
  timer.advance(1);
  assert.deepEqual(collapsed, ['collapse'], 'folded at 5000 ms');
  assert.equal(fold.isDone(), true);
  timer.advance(60_000);
  assert.deepEqual(collapsed, ['collapse'], 'and only once');
  assert.deepEqual(timer.cancelled, [], 'a timer that fired has nothing to cancel');
});

test('arm twice starts one countdown, and nothing is scheduled until arm is called', () => {
  const timer = fakeTimer();
  const { fold } = foldWith(timer);
  assert.equal(timer.pendingCount, 0, 'creating the fold schedules nothing');
  fold.arm();
  fold.arm();
  assert.equal(timer.scheduled.length, 1);
});

test('fold (the first interaction) collapses at once and cancels the countdown', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.arm();
  timer.advance(1200);
  fold.fold();
  assert.deepEqual(collapsed, ['collapse']);
  assert.deepEqual(timer.cancelled, [timer.scheduled[0].id], 'the countdown is cancelled');
  timer.advance(60_000);
  assert.deepEqual(collapsed, ['collapse'], 'the cancelled countdown never fires');
});

test('fold before the map has loaded collapses at once, and a later arm starts nothing', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.fold();
  assert.deepEqual(collapsed, ['collapse']);
  fold.arm();
  assert.equal(timer.scheduled.length, 0, 'the first interaction ended it');
  timer.advance(60_000);
  assert.deepEqual(collapsed, ['collapse']);
});

test('only the first interaction folds: later ones leave credits the person reopened alone', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.arm();
  fold.fold();
  fold.fold();
  fold.fold();
  assert.deepEqual(collapsed, ['collapse']);
});

test('release (the person used the (i) button) cancels the countdown and nothing collapses afterwards', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.arm();
  fold.release();
  assert.deepEqual(timer.cancelled, [timer.scheduled[0].id]);
  timer.advance(60_000);
  fold.fold();
  assert.deepEqual(collapsed, [], 'the credits stay as the person left them');
  assert.equal(fold.isDone(), true);
});

test('release before arm: the countdown never starts', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.release();
  fold.arm();
  assert.equal(timer.scheduled.length, 0);
  timer.advance(60_000);
  assert.deepEqual(collapsed, []);
});

test('dispose cancels the countdown without collapsing', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer);
  fold.arm();
  fold.dispose();
  assert.deepEqual(timer.cancelled, [timer.scheduled[0].id]);
  timer.advance(60_000);
  assert.deepEqual(collapsed, []);
});

test('a countdown callback that was already queued when the fold ended does nothing', () => {
  const timer = fakeTimer();
  let queued = null;
  const collapsed = [];
  const fold = createAttributionFold({
    schedule: (callback) => {
      queued = callback;
      return 1;
    },
    cancel: (handle) => timer.cancel(handle),
    collapse: () => collapsed.push('collapse'),
  });
  fold.arm();
  fold.release();
  timer.fireAnyway(1, queued);
  assert.deepEqual(collapsed, [], 'the person had taken over');
});

test('a different delay is honoured', () => {
  const timer = fakeTimer();
  const { fold, collapsed } = foldWith(timer, { delayMs: 250 });
  fold.arm();
  assert.deepEqual(timer.scheduled.map((entry) => entry.delayMs), [250]);
  timer.advance(250);
  assert.deepEqual(collapsed, ['collapse']);
});
