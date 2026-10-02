// Tests for the camera report of wwwroot/js/realmMap.js ([X-07], 03 section 4.7, 01 Appendix C item 10, R1-12): one gesture sends at most one `OnCameraChanged`, and a gesture that leaves the
// recentre state as .NET holds it sends none, because every report re-renders the Location page on the server and costs the circuit frames. realmMap.js cannot run `init` without MapLibre, and
// its exports are a fixed list, so the function under test is cut out of the file as text and run here against a fake clock, a fake camera and a fake sessionStorage. The numbers it uses
// (the debounce, the storage key) are read from the file too, so this test cannot drift from it. Positions are synthetic.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import { haversine } from '../../src/Realm.Web/wwwroot/js/layoutMath.js';

const SOURCE = readFileSync(new URL('../../src/Realm.Web/wwwroot/js/realmMap.js', import.meta.url), 'utf8');

/** @param {RegExp} pattern @param {string} what */
function cut(pattern, what) {
  const match = pattern.exec(SOURCE);
  assert.ok(match, `realmMap.js no longer has ${what}; this test cuts it out as text`);
  return match;
}

const FUNCTION_SOURCE = cut(/^function scheduleCameraReport\(r\) \{\n[\s\S]*?\n\}$/m, 'function scheduleCameraReport(r)')[0];
const DEBOUNCE_MS = Number(cut(/^const CAMERA_DEBOUNCE_MS = (\d+);/m, 'const CAMERA_DEBOUNCE_MS')[1]);
const STORAGE_KEY = cut(/^const CAMERA_STORAGE_KEY = '([^']+)';/m, 'const CAMERA_STORAGE_KEY')[1];

const HERE = [10, 50];
/** About 11 m north of HERE. */
const NEAR = [10, 50.0001];
/** About 5.5 km north of HERE. */
const FAR = [10, 50.05];

/**
 * The function under test with a fake world around it. `camera` is what the map reports when the timer fires; `reports` is every `notify('OnCameraChanged', ...)`; `stored` is the
 * sessionStorage mirror. `settle(camera)` plays one settled camera: the move ends, and the debounce runs out.
 * @param {{ storageThrows?: boolean }} [options]
 */
function world(options = {}) {
  let now = 0;
  let nextId = 1;
  /** @type {Map<number, { at: number, fn: () => void }>} */
  const timers = new Map();
  const clock = {
    setTimeout(fn, ms) {
      const id = nextId++;
      timers.set(id, { at: now + ms, fn });
      return id;
    },
    clearTimeout(id) {
      timers.delete(id);
    },
  };
  /** @param {number} ms */
  const advance = (ms) => {
    const until = now + ms;
    for (;;) {
      const due = [...timers.entries()].filter(([, t]) => t.at <= until).sort((a, b) => a[1].at - b[1].at)[0];
      if (!due) break;
      timers.delete(due[0]);
      now = due[1].at;
      due[1].fn();
    }
    now = until;
  };

  const w = {
    camera: { center: HERE, zoom: 15, recenter: 'default' },
    /** @type {{ name: string, state: any }[]} */
    notifications: [],
    /** @type {Record<string, string>} */
    stored: {},
    storageWrites: 0,
    pending: () => timers.size,
    advance,
  };
  const env = {
    cameraState: () => ({ ...w.camera, bounds: [[0, 0], [1, 1]], animated: false, lastDurationMs: 0, userInitiated: true }),
    haversine,
    notify: (/** @type {string} */ name, /** @type {any} */ state) => w.notifications.push({ name, state }),
    sessionStorage: {
      setItem(/** @type {string} */ key, /** @type {string} */ value) {
        if (options.storageThrows) throw new Error('blocked');
        w.storageWrites++;
        w.stored[key] = value;
      },
    },
    setTimeout: clock.setTimeout,
    clearTimeout: clock.clearTimeout,
    CAMERA_DEBOUNCE_MS: DEBOUNCE_MS,
    CAMERA_STORAGE_KEY: STORAGE_KEY,
  };
  const names = Object.keys(env);
  // `rt` is the module's current runtime: the function does nothing for a runtime that has been replaced.
  const factory = new Function(...names, `let rt = null;\n${FUNCTION_SOURCE}\nreturn { schedule: scheduleCameraReport, setRuntime(value) { rt = value; } };`);
  const loaded = factory(...names.map((name) => env[name]));

  const runtime = { cameraTimer: null, lastReported: null };
  loaded.setRuntime(runtime);
  w.runtime = runtime;
  w.setRuntime = loaded.setRuntime;
  /** One move ends. */
  w.moveEnd = () => loaded.schedule(runtime);
  /** A settled camera: it is where `camera` says, the move ended, and the debounce ran out. */
  w.settle = (/** @type {{ center?: number[], zoom?: number, recenter?: string }} */ camera) => {
    w.camera = { ...w.camera, ...camera };
    w.moveEnd();
    advance(DEBOUNCE_MS);
  };
  w.reports = () => w.notifications.filter((n) => n.name === 'OnCameraChanged');
  return w;
}

test('[X-07] the first pan, which takes the camera away from the default view, is one report and one storage write', () => {
  const w = world();
  w.settle({ center: FAR, recenter: 'away' });
  assert.equal(w.reports().length, 1);
  assert.equal(w.reports()[0].state.recenter, 'away');
  assert.deepEqual(w.reports()[0].state.center, FAR);
  assert.equal(w.storageWrites, 1);
  assert.equal(JSON.parse(w.stored[STORAGE_KEY]).recenter, 'away');
});

test('[X-07] a zoom after that, with the camera still away, sends nothing to .NET and still mirrors the camera to sessionStorage (R1-12)', () => {
  const w = world();
  w.settle({ center: FAR, recenter: 'away' });
  w.settle({ zoom: 16.5 });
  assert.equal(w.reports().length, 1, 'only the pan reported');
  assert.equal(w.storageWrites, 2);
  assert.equal(JSON.parse(w.stored[STORAGE_KEY]).zoom, 16.5);
});

test('[X-07] a pan with many move ends and a wheel zoom send at most one report between them', () => {
  const w = world();
  // a pointer pan: a move end on every step, each well inside the debounce of the one before
  w.camera = { ...w.camera, center: FAR, recenter: 'away' };
  for (let step = 0; step < 30; step++) {
    w.moveEnd();
    w.advance(DEBOUNCE_MS / 4);
  }
  assert.equal(w.pending(), 1, 'one timer waits, whatever the number of move ends');
  w.advance(DEBOUNCE_MS);
  assert.equal(w.pending(), 0);
  // then a wheel zoom, which is a few move ends of its own
  w.camera = { ...w.camera, zoom: 17 };
  for (let step = 0; step < 5; step++) {
    w.moveEnd();
    w.advance(DEBOUNCE_MS / 4);
  }
  w.advance(DEBOUNCE_MS);
  assert.ok(w.reports().length <= 1, `the pan and the zoom reported ${w.reports().length} times`);
  assert.equal(w.reports().length, 1);
});

test('[X-07] a pan or a zoom that leaves the recentre state as .NET holds it sends nothing, whether the camera is at the default view or away from it', () => {
  const atDefault = world();
  atDefault.settle({ center: NEAR, recenter: 'default' });
  atDefault.settle({ zoom: 15.5, recenter: 'default' });
  assert.equal(atDefault.reports().length, 0, 'a pan that ends at the default view is not news: .NET starts from the default view');
  assert.equal(atDefault.storageWrites, 2, 'the camera is still mirrored');

  const away = world();
  away.settle({ center: FAR, recenter: 'away' });
  const before = away.reports().length;
  away.settle({ center: [10.2, 50.2] });
  away.settle({ zoom: 13 });
  away.settle({ center: HERE, zoom: 18 });
  assert.equal(away.reports().length, before, 'once .NET knows the camera is away, further moves say nothing');
});

test('[AC-20] a change of the recentre state is reported once, in every direction: away, back to the default view, onto me alone, away again', () => {
  const w = world();
  w.settle({ center: FAR, recenter: 'away' });
  assert.deepEqual(w.reports().map((n) => n.state.recenter), ['away']);
  w.settle({ center: HERE, recenter: 'default' });
  assert.deepEqual(w.reports().map((n) => n.state.recenter), ['away', 'default']);
  w.settle({ center: NEAR, zoom: 18, recenter: 'me' });
  assert.deepEqual(w.reports().map((n) => n.state.recenter), ['away', 'default', 'me']);
  w.settle({ center: FAR, recenter: 'away' });
  assert.deepEqual(w.reports().map((n) => n.state.recenter), ['away', 'default', 'me', 'away']);
  // and the same state again, even at a new place, is silent
  w.settle({ center: [11, 51], zoom: 9 });
  assert.equal(w.reports().length, 4);
});

test('[AC-20] a recentre state that changes with the camera not having moved (the targets moved, not the camera) is reported, and the storage is left alone', () => {
  const w = world();
  w.settle({ recenter: 'me' }); // the first settle: the camera is where it was, the state is not the default one
  assert.equal(w.reports().length, 1);
  assert.equal(w.storageWrites, 1, 'the first settle always mirrors the camera');
  w.settle({ center: [10, 50.0000001], recenter: 'away' }); // under a metre: not a move
  assert.equal(w.reports().length, 2);
  assert.equal(w.reports()[1].state.recenter, 'away');
  assert.equal(w.storageWrites, 1, 'not moved: nothing to mirror');
});

test('a settle that neither moved the camera nor changed the state does nothing', () => {
  const w = world();
  w.settle({ center: FAR, recenter: 'away' });
  const reports = w.reports().length;
  const writes = w.storageWrites;
  w.settle({ center: [FAR[0], FAR[1] + 0.000001] }); // about 0.1 m
  w.settle({ zoom: 15.004 });
  assert.equal(w.reports().length, reports);
  assert.equal(w.storageWrites, writes);
});

test('the camera is mirrored from 1 m and from 0.01 zoom, not below', () => {
  const w = world();
  w.settle({ center: FAR, recenter: 'away' });
  assert.equal(w.storageWrites, 1);
  w.settle({ center: [FAR[0], FAR[1] + 0.00001] }); // about 1.1 m
  assert.equal(w.storageWrites, 2);
  w.settle({ zoom: 15.02 });
  assert.equal(w.storageWrites, 3);
});

test('blocked storage does not stop the report: the camera is then simply not restored after a reload', () => {
  const w = world({ storageThrows: true });
  w.settle({ center: FAR, recenter: 'away' });
  assert.equal(w.reports().length, 1);
  assert.equal(w.storageWrites, 0);
});

test('a runtime that has been replaced reports nothing and mirrors nothing', () => {
  const w = world();
  w.camera = { ...w.camera, center: FAR, recenter: 'away' };
  w.moveEnd();
  w.setRuntime({}); // the map was disposed and another one started
  w.advance(DEBOUNCE_MS);
  assert.equal(w.reports().length, 0);
  assert.equal(w.storageWrites, 0);
  assert.equal(w.runtime.cameraTimer, null);
});

test('[X-07] OnCameraChanged is sent from one place in realmMap.js, the gate of the camera report, so one gesture cannot send two', () => {
  const sends = SOURCE.match(/notify\('OnCameraChanged'/g) ?? [];
  assert.equal(sends.length, 1);
  assert.ok(FUNCTION_SOURCE.includes("notify('OnCameraChanged', state)"), 'and it is the one in scheduleCameraReport');
  const callers = SOURCE.match(/scheduleCameraReport\(/g) ?? [];
  assert.ok(callers.length >= 2, 'the function is called by the move end handler and by the default targets');
});
