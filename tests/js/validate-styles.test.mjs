// Tests for tools/ci/validate-styles.mjs (FX5, R3-02): a spec error in a style we build or ship is a FAIL (exit 1), a third-party fetch that
// fails, is refused or answers something that is not a style is a WARN (exit 0). Offline: the fetches are injected, nothing here uses the network.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import fs from 'node:fs';
import { test } from 'node:test';

import YAML from 'yaml';

import { main, validateBuilt, validateRemote } from '../../tools/ci/validate-styles.mjs';
import { STYLES, STYLE_IDS, buildStyle, transformStyle } from '../../src/Realm.Web/wwwroot/js/mapStyles.js';
import { cleanEnv, repoRoot } from './helpers/ci-harness.mjs';

const OPENFREEMAP_IDS = STYLE_IDS.filter((id) => STYLES[id].url);
const BUILT_IDS = STYLE_IDS.filter((id) => !STYLES[id].url);

/** A style that passes the validator, with or without our overlay. */
const goodStyle = () => ({ version: 8, sources: {}, layers: [{ id: 'bg', type: 'background', paint: { 'background-color': '#000000' } }] });
/** A style that fails the validator on its own (a layer of a type that does not exist). */
const brokenStyle = () => ({ version: 8, sources: {}, layers: [{ id: 'x', type: 'nonsense' }] });
/** A third-party style that is fine alone but breaks once our overlay is appended: it owns the id of our zone source with a raster definition. */
const clashingStyle = () => ({ ...goodStyle(), sources: { 'realm-zones': { type: 'raster', tiles: ['https://example.invalid/{z}/{x}/{y}.png'] } } });

const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
/** A fetch that answers every OpenFreeMap url with `answer(url)` (a Response, or a function that throws). */
const fetching = (answer) => async (url) => answer(String(url));

async function run(argv, options) {
  const lines = [];
  const code = await main(argv, (line) => lines.push(line), options);
  return { code, lines };
}

test('the styles built in JavaScript (satellite, demo-offline) validate, with and without the overlay', () => {
  const results = validateBuilt();
  assert.deepEqual(results.map((r) => r.id), BUILT_IDS);
  assert.deepEqual(BUILT_IDS, ['satellite', 'demo-offline'], 'the registry builds exactly the two styles 03 section 4.9 names');
  for (const { id, failures } of results) assert.deepEqual(failures, [], id);
});

test('validateBuilt reports every spec error of a built style, from the base style and from the style with the overlay', () => {
  const results = validateBuilt({ build: (id) => (id === 'satellite' ? brokenStyle() : buildStyle(id)) });
  const satellite = results.find((r) => r.id === 'satellite');
  assert.ok(satellite.failures.some((f) => /^base style: layers\[0\]\.type: expected one of/.test(f)), satellite.failures.join('\n'));
  assert.ok(satellite.failures.some((f) => /^with overlay: layers\[0\]\.type: expected one of/.test(f)), 'the overlay run reports it too');
  assert.deepEqual(results.find((r) => r.id === 'demo-offline').failures, [], 'the other style is not blamed');
});

test('validateBuilt: an overlay that breaks a built style, a builder that throws and one that returns a URL are all failures', () => {
  const overlayBreaks = validateBuilt({ overlay: (base) => transformStyle(undefined, { ...base, sources: { ...base.sources, 'realm-zones': { type: 'raster', tiles: ['https://example.invalid/{z}/{x}/{y}.png'] } } }) });
  for (const { id, failures } of overlayBreaks) {
    assert.ok(failures.length > 0 && failures.every((f) => f.startsWith('with overlay: ')), `${id}: ${failures.join(' | ')}`);
  }
  const throws = validateBuilt({ build: () => { throw new Error('graticule exploded'); } });
  assert.deepEqual(throws.map((r) => r.failures), [['buildStyle threw: graticule exploded'], ['buildStyle threw: graticule exploded']]);
  const url = validateBuilt({ build: () => 'https://example.invalid/style.json' });
  assert.deepEqual(url[0].failures, ['buildStyle returned a URL for a style that has none']);
  const validatorThrows = validateBuilt({ build: () => null });
  assert.match(validatorThrows[0].failures[0], /^the style could not be validated: /, 'a validator that throws on a built style fails it, it does not crash the run');
});

test('main: a spec error in a built style is a FAIL line and exit 1, also when every fetch worked', async () => {
  const fetchImpl = fetching(() => json(goodStyle()));
  const { code, lines } = await run([], { built: { build: (id) => (id === 'demo-offline' ? brokenStyle() : buildStyle(id)) }, fetchImpl });
  assert.equal(code, 1);
  assert.ok(lines.includes('PASS styles satellite (built in JavaScript, with the overlay)'));
  assert.ok(lines.some((l) => /^FAIL styles demo-offline: base style: layers\[0\]\.type: expected one of/.test(l)), lines.join('\n'));
  assert.equal(lines.filter((l) => l.startsWith('PASS styles') && l.includes('(fetched,')).length, OPENFREEMAP_IDS.length, 'the good third-party styles pass');
});

test('main: a spec error in a built style fails the run under --offline too', async () => {
  const { code, lines } = await run(['--offline'], { built: { build: () => brokenStyle() } });
  assert.equal(code, 1);
  assert.equal(lines.filter((l) => l.startsWith('FAIL styles ')).length >= 2, true, lines.join('\n'));
  assert.ok(lines.includes('PASS styles openfreemap (skipped: --offline)'));
});

test('main: a registry with no built style fails instead of passing on nothing', async () => {
  const { code, lines } = await run(['--offline'], { built: { ids: OPENFREEMAP_IDS } });
  assert.equal(code, 1);
  assert.match(lines[0], /^FAIL styles: no style built in JavaScript was found to validate/);
});

test('main: every third-party fetch refused (403, 500) is a WARN per style and exit 0', async () => {
  for (const status of [403, 500, 404]) {
    const { code, lines } = await run([], { fetchImpl: fetching(() => new Response('', { status })) });
    assert.equal(code, 0, `status ${status}`);
    assert.equal(lines.filter((l) => l.startsWith('FAIL')).length, 0);
    const warns = lines.filter((l) => l.startsWith('WARN styles '));
    assert.equal(warns.length, OPENFREEMAP_IDS.length);
    for (const id of OPENFREEMAP_IDS) assert.ok(warns.some((w) => w === `WARN styles ${id}: ${STYLES[id].url} answered ${status}`), `${id} ${status}`);
  }
});

test('main: a fetch that throws (no network, DNS, abort, timeout) is a WARN and exit 0', async () => {
  const refused = Object.assign(new TypeError('fetch failed'), { cause: { code: 'ECONNREFUSED' } });
  const { code, lines } = await run([], { fetchImpl: fetching(() => { throw refused; }) });
  assert.equal(code, 0);
  assert.equal(lines.filter((l) => /^WARN styles \w+: .* not fetched: ECONNREFUSED: fetch failed$/.test(l)).length, OPENFREEMAP_IDS.length, lines.join('\n'));
  const timeout = await run([], { fetchImpl: fetching(() => { throw Object.assign(new Error('The operation was aborted due to timeout'), { name: 'TimeoutError' }); }) });
  assert.equal(timeout.code, 0);
  assert.ok(timeout.lines.some((l) => /not fetched: TimeoutError: /.test(l)));
});

test('main: an answer that is not a style (HTML, JSON of another shape, null) is a WARN and exit 0, never a crash', async () => {
  const answers = {
    html: () => new Response('<html>blocked by the proxy</html>', { status: 200 }),
    emptyBody: () => new Response('', { status: 200 }),
    errorObject: () => json({ error: 'forbidden' }),
    array: () => json([]),
    nul: () => json(null),
    noLayers: () => json({ version: 8, sources: {} }),
    layersNotArray: () => json({ version: 8, sources: {}, layers: {} }),
  };
  for (const [name, answer] of Object.entries(answers)) {
    const { code, lines } = await run([], { fetchImpl: fetching(answer) });
    assert.equal(code, 0, name);
    assert.equal(lines.filter((l) => l.startsWith('FAIL')).length, 0, name);
    assert.equal(lines.filter((l) => l.startsWith('WARN styles ')).length, OPENFREEMAP_IDS.length, name);
  }
});

test('main: spec errors in the style the third party published are WARNs (not ours), exit 0', async () => {
  const { code, lines } = await run([], { fetchImpl: fetching(() => json(brokenStyle())) });
  assert.equal(code, 0);
  assert.equal(lines.filter((l) => l.startsWith('FAIL')).length, 0);
  assert.ok(lines.some((l) => /^WARN styles night: published style: layers\[0\]\.type: expected one of/.test(l)), lines.join('\n'));
});

test('main: an error that only appears once OUR overlay is appended to a fetched style is a FAIL and exit 1', async () => {
  const { code, lines } = await run([], { fetchImpl: fetching(() => json(clashingStyle())) });
  assert.equal(code, 1);
  const fails = lines.filter((l) => l.startsWith('FAIL styles '));
  assert.ok(fails.length >= OPENFREEMAP_IDS.length, lines.join('\n'));
  assert.ok(fails.some((l) => /^FAIL styles night: with overlay: layers\[3\]: layer "realm-zones-fill" requires a vector source$/.test(l)), fails.join('\n'));
  assert.equal(lines.filter((l) => l.startsWith('WARN')).length, 0, 'nothing was wrong with the published styles themselves');
});

test('main: a third-party WARN never hides a FAIL of ours, and a FAIL never hides the WARN lines', async () => {
  const fetchImpl = fetching((url) => (url.endsWith('/dark') ? new Response('', { status: 403 }) : json(clashingStyle())));
  const { code, lines } = await run([], { fetchImpl });
  assert.equal(code, 1);
  assert.ok(lines.some((l) => l.startsWith('WARN styles night: ')));
  assert.ok(lines.some((l) => l.startsWith('FAIL styles day: ')));
});

test('validateRemote: the fetched style is validated with the overlay appended, and the answers are summarised per style', async () => {
  const good = await validateRemote('night', { fetchImpl: fetching(() => json(goodStyle())) });
  assert.deepEqual(good, { id: 'night', warnings: [], failures: [], layers: 1 });
  const refused = await validateRemote('day', { fetchImpl: fetching(() => new Response('', { status: 403 })) });
  assert.deepEqual(refused, { id: 'day', warnings: [`${STYLES.day.url} answered 403`], failures: [], layers: 0 });
  const clash = await validateRemote('streets', { fetchImpl: fetching(() => json(clashingStyle())) });
  assert.equal(clash.warnings.length, 0);
  assert.equal(clash.failures.length, 4);
});

test('command line: --offline validates the real built styles with no network, prints PASS lines and exits 0', () => {
  const result = spawnSync('node', [path.join(repoRoot, 'tools', 'ci', 'validate-styles.mjs'), '--offline'], { encoding: 'utf8', env: cleanEnv(), cwd: repoRoot });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.equal(result.stdout, [
    'PASS styles satellite (built in JavaScript, with the overlay)',
    'PASS styles demo-offline (built in JavaScript, with the overlay)',
    'PASS styles openfreemap (skipped: --offline)',
    '',
  ].join('\n'));
});

test('ci.yml: the validate-styles step is an ordinary step (no continue-on-error), so its exit code fails the js job, and its output goes to styles.log', () => {
  const workflow = YAML.parse(fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'ci.yml'), 'utf8'));
  const steps = workflow.jobs.js.steps.filter((step) => typeof step.run === 'string' && step.run.includes('validate-styles.mjs'));
  assert.equal(steps.length, 1, 'exactly one step runs the script');
  assert.equal(steps[0]['continue-on-error'], undefined, 'R3-02: a style error of ours must not be swallowed');
  assert.equal(steps[0].if, undefined);
  assert.match(steps[0].run, /2>&1 \| tee ci-out\/styles\.log$/, 'the whole output is kept, and the step still fails (bash -eo pipefail)');
  assert.equal(workflow.defaults.run.shell, 'bash', 'an explicit bash shell is what makes "| tee" fail the step');
  const upload = workflow.jobs.js.steps.at(-1);
  assert.equal(upload.if, 'always()');
  assert.equal(upload.with.path, 'ci-out', 'styles.log is uploaded with the js artifact, also when the step failed');
});
