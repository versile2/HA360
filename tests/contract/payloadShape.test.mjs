// Tests for tests/contract/payloadShape.mjs: the checker for the 03 section 4.5 payloads, on hand-written JSON (the demo cast is
// fictional, 02 section 9.3). When tests/contract/payloads/ exists (the `e2e` job downloads the folder that PayloadContractTests
// writes, S5b and S6a) every golden file in it must also pass. Without golden files the golden test is skipped on a developer machine (no .NET
// here) and FAILS in CI (CI=true or GITHUB_ACTIONS=true): a lost dotnet artifact must not turn the contract step green (R1-16).
//
//   node --test "tests/contract/**/*.test.mjs"
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { PAYLOAD_SHAPES, SHAPES, checkShape, main, shapeNameForFile } from './payloadShape.mjs';

// ---- hand-written samples: one valid value per shape --------------------------------------------------------------------------

const ring = (over = {}) => ({ color: '#E8BC4E', dashed: false, widthPx: 4, ...over });

const member = (over = {}) => ({
  id: 'king',
  name: 'King',
  initial: 'K',
  color: '#E8BC4E',
  lat: 31.099,
  lon: -85.341,
  accuracyM: 12,
  poorAccuracy: false,
  status: 'atPlace',
  ring: ring(),
  badge: 'home',
  lowBattery: false,
  drivingFresh: false,
  far: false,
  isStatic: false,
  isMe: true,
  zClass: 1,
  avatarUrl: 'avatars/king',
  chip: 'Here for 3 hrs, 33 mins',
  chipMinute: 29_400_000,
  ariaLabel: 'King, at the Keep. Double tap for details.',
  tooltip: 'King',
  bubbleLabel: 'King, 2 miles east, off screen. Double tap to include on the map.',
  bubbleTooltip: 'King',
  glyph: null,
  ...over,
});

const vehicle = (over = {}) => ({
  id: 'wagon',
  name: "The King's Wagon",
  glyph: 'pickup',
  lat: 31.09907,
  lon: -85.341,
  ring: ring({ color: '#7FB3D5', widthPx: 3 }),
  stale: false,
  chip: null,
  ariaLabel: "The King's Wagon, parked at the Keep.",
  tooltip: "The King's Wagon",
  initial: 'T',
  color: '#A5B4FC',
  avatarUrl: null,
  showInitial: false,
  ...over,
});

const appearance = (over = {}) => ({ lineColor: '#E8BC4E', fillAlpha: 0.1, fillAlphaOccupied: 0.22, casing: false, ...over });

const SAMPLES = {
  Padding: () => ({ top: 72, right: 72, bottom: 190, left: 16 }),
  Bounds: () => [[-85.4647, 31.056], [-85.341, 31.099]],
  LayoutPayload: () => ({ mode: 'compact', panelLeftPx: 16, panelWidthPx: 0, panelHidden: false, stackVisible: true, safe: { top: 24, right: 0, bottom: 34, left: 0 }, navHeightPx: 64 }),
  Ring: () => ring(),
  MemberPayloadItem: () => member(),
  MembersPayload: () => ({
    version: 7,
    meId: 'king',
    members: [member(), member({ id: 'cryptid', name: 'Cryptid', initial: 'C', lat: null, lon: null, accuracyM: null, status: 'nofix', badge: null, avatarUrl: null, chip: null, isMe: false, zClass: 0 })],
  }),
  VehiclePayloadItem: () => vehicle(),
  VehiclesPayload: () => ({ version: 3, vehicles: [vehicle(), vehicle({ id: 'chariot', name: 'Chariot', lat: null, lon: null })] }),
  ZoneItem: () => ({ id: 'forge', name: 'The Forge', lat: 31.1, lon: -85.35, radiusM: 120, occupied: true }),
  ZoneAppearance: () => appearance(),
  ZonesPayload: () => ({
    version: 2,
    show: true,
    zones: [{ id: 'forge', name: 'The Forge', lat: 31.1, lon: -85.35, radiusM: 120, occupied: true }],
    appearances: { dark: appearance(), light: appearance({ fillAlpha: 0.14 }), imagery: appearance({ casing: true }) },
  }),
  SelectionPayload: () => ({ kind: 'member', id: 'king', follow: false }),
  DefaultTargets: () => ({ version: 1, default: { bounds: [[-85.4647, 31.056], [-85.341, 31.1]], maxZoom: 16 }, me: { center: [-85.341, 31.099], zoom: 16 } }),
  CameraState: () => ({ center: [-85.341, 31.099], zoom: 15.2, bounds: [[-85.36, 31.08], [-85.32, 31.12]], animated: false, lastDurationMs: 0, recenter: 'default', userInitiated: false }),
  StyleResult: () => ({ styleId: 'night', ok: true }),
};

const errorsOf = (shape, value) => checkShape(shape, value);
const mentions = (errors, text) => errors.some((error) => error.includes(text));

test('every shape has a hand-written sample and every sample is valid', () => {
  assert.deepEqual(Object.keys(SAMPLES).sort(), Object.keys(SHAPES).sort());
  for (const [name, make] of Object.entries(SAMPLES)) assert.deepEqual(errorsOf(name, make()), [], name);
});

test('samples survive a JSON round trip (the shape of the wire, not of a JavaScript object)', () => {
  for (const [name, make] of Object.entries(SAMPLES)) assert.deepEqual(errorsOf(name, JSON.parse(JSON.stringify(make()))), [], name);
});

test('unknown shape name throws', () => {
  assert.throws(() => checkShape('MemberPayload', {}), /unknown shape 'MemberPayload'/);
});

test('a renamed field fails twice: the old name is missing and the new one is unexpected', () => {
  const sample = SAMPLES.MemberPayloadItem();
  const { accuracyM, ...rest } = sample;
  const errors = errorsOf('MemberPayloadItem', { ...rest, accuracy: accuracyM });
  assert.ok(mentions(errors, '$.accuracyM: missing'), errors.join('\n'));
  assert.ok(mentions(errors, '$.accuracy: unexpected property'), errors.join('\n'));
});

test('a renamed field fails in a nested object', () => {
  const zones = SAMPLES.ZonesPayload();
  zones.zones[0].radius = zones.zones[0].radiusM;
  delete zones.zones[0].radiusM;
  const errors = errorsOf('ZonesPayload', zones);
  assert.ok(mentions(errors, '$.zones[0].radiusM: missing'), errors.join('\n'));
  assert.ok(mentions(errors, '$.zones[0].radius: unexpected property'), errors.join('\n'));
});

test('PascalCase keys (a serializer without the web defaults) fail', () => {
  const errors = errorsOf('LayoutPayload', { Mode: 'compact', PanelLeftPx: 16, PanelWidthPx: 0, PanelHidden: false, StackVisible: true, Safe: { Top: 0, Right: 0, Bottom: 0, Left: 0 }, NavHeightPx: 64 });
  assert.ok(mentions(errors, '$.mode: missing') && mentions(errors, '$.Mode: unexpected property'), errors.join('\n'));
});

test('a missing field fails with its path', () => {
  const sample = SAMPLES.MembersPayload();
  delete sample.members[1].chipMinute;
  assert.deepEqual(errorsOf('MembersPayload', sample), ['$.members[1].chipMinute: missing']);
  const noVersion = SAMPLES.VehiclesPayload();
  delete noVersion.version;
  assert.deepEqual(errorsOf('VehiclesPayload', noVersion), ['$.version: missing']);
});

test('an extra field fails', () => {
  const sample = SAMPLES.CameraState();
  sample.pitch = 0;
  assert.deepEqual(errorsOf('CameraState', sample), ['$.pitch: unexpected property (not in 03 section 4.5)']);
});

test('a wrong type fails: number as string, string as number, object as array, null where not allowed', () => {
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lat: '31.099' }), '$.lat: expected a finite number'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), name: 7 }), '$.name: expected a string'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lowBattery: 'false' }), '$.lowBattery: expected a boolean'));
  assert.ok(mentions(errorsOf('MembersPayload', { ...SAMPLES.MembersPayload(), members: {} }), '$.members: expected an array'));
  assert.ok(mentions(errorsOf('LayoutPayload', { ...SAMPLES.LayoutPayload(), safe: [] }), '$.safe: expected an object'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), ariaLabel: null }), '$.ariaLabel: expected a string, got null'));
  assert.ok(mentions(errorsOf('MembersPayload', null), '$: expected an object, got null'));
  assert.ok(mentions(errorsOf('MembersPayload', []), '$: expected an object, got array'));
});

test('a value outside an enum fails', () => {
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), status: 'atplace' }), '$.status: expected one of'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), badge: 'battery' }), '$.badge: expected one of'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), zClass: 5 }), '$.zClass: expected one of'));
  assert.ok(mentions(errorsOf('LayoutPayload', { ...SAMPLES.LayoutPayload(), mode: 'medium' }), '$.mode: expected one of'));
  assert.ok(mentions(errorsOf('VehiclePayloadItem', { ...SAMPLES.VehiclePayloadItem(), glyph: 'rocket' }), '$.glyph: expected one of'));
  assert.ok(mentions(errorsOf('SelectionPayload', { kind: 'zone', id: 'forge', follow: false }), '$.kind: expected one of'));
  assert.ok(mentions(errorsOf('CameraState', { ...SAMPLES.CameraState(), recenter: 'home' }), '$.recenter: expected one of'));
  assert.ok(mentions(errorsOf('StyleResult', { styleId: 'parchment', ok: true }), '$.styleId: expected one of'));
});

test('every documented status and style id is accepted', () => {
  for (const status of ['static', 'offline', 'stale', 'driving', 'atPlace', 'out', 'nofix']) assert.deepEqual(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), status }), [], status);
  for (const styleId of ['night', 'day', 'streets', 'satellite', 'demo-offline']) assert.deepEqual(errorsOf('StyleResult', { styleId, ok: false, error: 'timeout' }), [], styleId);
  for (const badge of ['driving', 'stale', 'offline', 'home', null]) assert.deepEqual(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), badge }), [], String(badge));
});

test('coordinates: out of range and half-null positions fail; a full null is "no fix"', () => {
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lat: 91 }), '$.lat: latitude 91 is outside'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lon: -181 }), '$.lon: longitude -181 is outside'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lon: null }), 'lat and lon must both be null'));
  assert.ok(mentions(errorsOf('VehiclePayloadItem', { ...SAMPLES.VehiclePayloadItem(), lat: null }), 'lat and lon must both be null'));
  assert.deepEqual(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), lat: null, lon: null }), []);
  assert.ok(mentions(errorsOf('ZoneItem', { ...SAMPLES.ZoneItem(), lat: 'x' }), '$.lat'));
});

test('avatarUrl is relative: a leading slash or a scheme fails', () => {
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), avatarUrl: '/avatars/king' }), 'must be relative'));
  assert.ok(mentions(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), avatarUrl: 'https://example.com/king.png' }), 'must be relative'));
  assert.deepEqual(errorsOf('MemberPayloadItem', { ...SAMPLES.MemberPayloadItem(), avatarUrl: null }), []);
});

test('ids are unique within a payload', () => {
  const members = SAMPLES.MembersPayload();
  members.members[1].id = 'king';
  assert.ok(mentions(errorsOf('MembersPayload', members), "duplicate member id 'king'"));
  const zones = SAMPLES.ZonesPayload();
  zones.zones.push({ ...zones.zones[0] });
  assert.ok(mentions(errorsOf('ZonesPayload', zones), "duplicate zone id 'forge'"));
});

test('zones: all three appearances are required, alphas stay in 0..1, the radius is positive', () => {
  const zones = SAMPLES.ZonesPayload();
  delete zones.appearances.imagery;
  assert.ok(mentions(errorsOf('ZonesPayload', zones), '$.appearances.imagery: missing'));
  assert.ok(mentions(errorsOf('ZoneAppearance', appearance({ fillAlpha: 1.5 })), 'alpha 1.5 is outside'));
  assert.ok(mentions(errorsOf('ZoneItem', { ...SAMPLES.ZoneItem(), radiusM: 0 }), '$.radiusM: expected a number above 0'));
  assert.deepEqual(errorsOf('ZonesPayload', { ...SAMPLES.ZonesPayload(), zones: [], show: false }), []);
});

test('DefaultTargets: maxZoom and the "me" zoom are the literal 16; "me" may be null; bounds are two [lon, lat] pairs', () => {
  const targets = SAMPLES.DefaultTargets();
  assert.ok(mentions(errorsOf('DefaultTargets', { ...targets, default: { ...targets.default, maxZoom: 15 } }), '$.default.maxZoom: expected one of 16'));
  assert.ok(mentions(errorsOf('DefaultTargets', { ...targets, me: { center: [0, 0], zoom: 15 } }), '$.me.zoom: expected one of 16'));
  assert.deepEqual(errorsOf('DefaultTargets', { ...targets, me: null }), []);
  assert.ok(mentions(errorsOf('DefaultTargets', { ...targets, default: { bounds: [[-85, 31]], maxZoom: 16 } }), '$.default.bounds: expected an array of 2 items'));
  assert.ok(mentions(errorsOf('Bounds', [[-85, 31], [-84, 31, 5]]), '$[1]: expected an array of 2 items'));
  assert.ok(mentions(errorsOf('Bounds', [[-85, 100], [-84, 31]]), 'latitude 100 is outside'));
});

test('StyleResult: error is optional (absent, a string or null)', () => {
  assert.deepEqual(errorsOf('StyleResult', { styleId: 'day', ok: true }), []);
  assert.deepEqual(errorsOf('StyleResult', { styleId: 'day', ok: false, error: 'timeout' }), []);
  assert.deepEqual(errorsOf('StyleResult', { styleId: 'day', ok: true, error: null }), []);
  assert.ok(mentions(errorsOf('StyleResult', { styleId: 'day', ok: false, error: 5 }), '$.error: expected a string'));
  assert.ok(mentions(errorsOf('StyleResult', { ok: true }), '$.styleId: missing'));
});

test('CameraState: numbers must be finite', () => {
  assert.ok(mentions(errorsOf('CameraState', { ...SAMPLES.CameraState(), zoom: null }), '$.zoom: expected a finite number'));
  assert.ok(mentions(errorsOf('CameraState', { ...SAMPLES.CameraState(), lastDurationMs: Number.NaN }), '$.lastDurationMs: expected a finite number'));
});

test('shapeNameForFile: the shape comes from the file name', () => {
  const cases = {
    'members.json': 'MembersPayload',
    'Members.demo.json': 'MembersPayload',
    'MembersPayload.json': 'MembersPayload',
    'vehicles-with-chariot.json': 'VehiclesPayload',
    'zones.json': 'ZonesPayload',
    'ZonesPayload-hidden.json': 'ZonesPayload',
    'layout.compact.json': 'LayoutPayload',
    'layout-expanded.json': 'LayoutPayload',
    'default-targets.json': 'DefaultTargets',
    'DefaultTargets.json': 'DefaultTargets',
    'camera.json': 'CameraState',
    'camera-state.json': 'CameraState',
    'CameraState.json': 'CameraState',
    'style_result.json': 'StyleResult',
    'selection.json': 'SelectionPayload',
    'tests/contract/payloads/members.json': 'MembersPayload',
    'readme.json': null,
  };
  for (const [file, shape] of Object.entries(cases)) assert.equal(shapeNameForFile(file), shape, file);
  for (const name of PAYLOAD_SHAPES) assert.equal(shapeNameForFile(`${name}.json`), name, name);
});

test('command line: PASS and FAIL lines, exit codes', () => {
  const dir = fs.mkdtempSync(path.join(process.env.TMPDIR ?? '/tmp', 'payload-shape-'));
  try {
    const good = path.join(dir, 'zones.json');
    const bad = path.join(dir, 'members.json');
    const odd = path.join(dir, 'mystery.json');
    fs.writeFileSync(good, JSON.stringify(SAMPLES.ZonesPayload()));
    fs.writeFileSync(bad, JSON.stringify({ version: 1, members: [] }));
    fs.writeFileSync(odd, '{}');
    const lines = [];
    assert.equal(main([good], (line) => lines.push(line)), 0);
    assert.deepEqual(lines, [`PASS ${good} (ZonesPayload)`]);
    lines.length = 0;
    assert.equal(main([good, bad], (line) => lines.push(line)), 1);
    assert.deepEqual(lines, [`PASS ${good} (ZonesPayload)`, `FAIL ${bad} (MembersPayload): $.meId: missing`]);
    lines.length = 0;
    assert.equal(main([odd], (line) => lines.push(line)), 1);
    assert.match(lines[0], /^FAIL .*mystery\.json: no shape for this file name/);
    lines.length = 0;
    assert.equal(main(['--shape', 'ZonesPayload', odd], (line) => lines.push(line)), 1);
    assert.match(lines[0], /^FAIL .*\(ZonesPayload\): \$\.version: missing/);
    assert.equal(main([], () => {}), 2);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

// ---- the golden payloads of PayloadContractTests (S5b), validated by the `e2e` job from S6a ------------------------------------

const GOLDEN = path.join(import.meta.dirname, 'payloads');
const golden = fs.existsSync(GOLDEN) ? fs.readdirSync(GOLDEN).filter((name) => name.endsWith('.json')).sort() : [];
// GitHub Actions sets both; the check is strict on purpose (a value such as 1 counts), so a CI that sets either can never skip.
const inCi = ['CI', 'GITHUB_ACTIONS'].some((name) => /^(true|1)$/i.test(process.env[name] ?? ''));
const noGolden = golden.length === 0;

const MISSING = 'no golden payload file in tests/contract/payloads (the folder is missing or holds no .json file)';
const WHY_IT_MATTERS = 'PayloadContractTests writes the files, the dotnet job uploads the folder and the e2e job copies it next to this test; without them the contract between C# and the script would pass on nothing';

// In CI a missing golden file is a failure with its cause; on a developer machine without .NET the test is skipped, saying so.
test(
  'golden payloads: every file of tests/contract/payloads matches its shape',
  { skip: noGolden && !inCi && `${MISSING}: skipped here, but this fails in CI (CI=true); run the dotnet tests first to write them` },
  async (t) => {
    assert.ok(!noGolden, `${MISSING}. ${WHY_IT_MATTERS}. A lost or empty dotnet artifact fails the contract step, it is not skipped.`);
    for (const file of golden) {
      await t.test(file, () => {
        const shape = shapeNameForFile(file);
        assert.ok(shape, `no payload shape matches the file name '${file}'; name golden files after the shape (members.json, zones.json, default-targets.json, ...)`);
        const errors = checkShape(shape, JSON.parse(fs.readFileSync(path.join(GOLDEN, file), 'utf8')));
        assert.deepEqual(errors, []);
      });
    }
  },
);
