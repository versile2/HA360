// The golden-payload check of tests/contract/payloadShape.test.mjs must FAIL in CI when its input files are missing and may only skip on a
// developer machine (R1-16, 03 section 4.5). The contract test is copied next to a payloads folder that this test controls and run as a child
// process, so the top-level wiring (skip or fail) is what is tested, not a re-implementation of it. Offline, no .NET.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { cleanEnv, repoRoot, tempDir, writeFile } from './helpers/ci-harness.mjs';

const GOLDEN_TEST = 'golden payloads: every file of tests/contract/payloads matches its shape';
const DEFAULT_TARGETS = { version: 1, default: { bounds: [[-85.4647, 31.056], [-85.341, 31.1]], maxZoom: 16 }, me: { center: [-85.341, 31.099], zoom: 16 } };

/**
 * Runs a copy of the contract test with `payloads` (file name -> text) as the golden folder; `null` means no folder at all.
 * `env` is added to an environment from which CI and GITHUB_ACTIONS are removed.
 */
function runContract(payloads, env = {}) {
  const dir = tempDir('payload-golden');
  for (const name of ['payloadShape.mjs', 'payloadShape.test.mjs']) fs.copyFileSync(path.join(repoRoot, 'tests', 'contract', name), path.join(dir, name));
  if (payloads !== null) {
    fs.mkdirSync(path.join(dir, 'payloads'));
    for (const [name, text] of Object.entries(payloads)) writeFile(path.join(dir, 'payloads', name), text);
  }
  const clean = cleanEnv(env);
  delete clean.CI;
  delete clean.GITHUB_ACTIONS;
  delete clean.NODE_TEST_CONTEXT; // set by the outer `node --test`: a child that inherits it would send events to its parent instead of printing TAP
  Object.assign(clean, env);
  const result = spawnSync('node', ['--test', '--test-reporter=tap', 'payloadShape.test.mjs'], { cwd: dir, encoding: 'utf8', env: clean });
  const tap = result.stdout;
  const entry = new RegExp(`^(not ok|ok) \\d+ - ${GOLDEN_TEST.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(.*)$`, 'm').exec(tap);
  return { status: result.status, tap, stderr: result.stderr, golden: entry === null ? null : { ok: entry[1] === 'ok', directive: entry[2].trim() } };
}

test('no golden folder on a developer machine: the golden test is skipped with a message, the run passes', () => {
  const run = runContract(null);
  assert.equal(run.status, 0, run.tap + run.stderr);
  assert.equal(run.golden.ok, true);
  assert.match(run.golden.directive, /^# SKIP no golden payload file in tests\/contract\/payloads .*: skipped here, but this fails in CI \(CI=true\)/);
});

test('no golden folder in CI (CI=true or GITHUB_ACTIONS=true): the golden test FAILS with the cause, it is not skipped', () => {
  for (const env of [{ CI: 'true' }, { GITHUB_ACTIONS: 'true' }, { CI: 'true', GITHUB_ACTIONS: 'true' }, { CI: '1' }]) {
    const run = runContract(null, env);
    assert.equal(run.status, 1, `${JSON.stringify(env)}\n${run.tap}`);
    assert.equal(run.golden.ok, false, JSON.stringify(env));
    assert.doesNotMatch(run.golden.directive, /SKIP/, JSON.stringify(env));
    assert.match(run.tap, /no golden payload file in tests\/contract\/payloads \(the folder is missing or holds no \.json file\)\. PayloadContractTests writes the files, the dotnet job uploads the folder/);
    assert.match(run.tap, /A lost or empty dotnet artifact fails the contract step, it is not skipped\./);
  }
});

test('an empty golden folder, or one without a .json file, fails in CI and skips locally', () => {
  for (const payloads of [{}, { 'README.txt': 'not a payload' }]) {
    const ci = runContract(payloads, { CI: 'true' });
    assert.equal(ci.status, 1, ci.tap);
    assert.match(ci.tap, /no golden payload file in tests\/contract\/payloads/);
    const local = runContract(payloads);
    assert.equal(local.status, 0, local.tap);
    assert.match(local.golden.directive, /^# SKIP /);
  }
});

test('CI that is not "true" (CI=false, CI=0, empty) is a developer machine: the golden test is skipped', () => {
  for (const value of ['false', '0', '']) {
    const run = runContract(null, { CI: value });
    assert.equal(run.status, 0, `CI=${value}\n${run.tap}`);
    assert.match(run.golden.directive, /^# SKIP /, `CI=${value}`);
  }
});

test('golden files that are present are still validated, in CI and locally: a valid one passes, an invalid one fails', () => {
  for (const env of [{ CI: 'true' }, {}]) {
    const good = runContract({ 'default-targets.json': JSON.stringify(DEFAULT_TARGETS) }, env);
    assert.equal(good.status, 0, `${JSON.stringify(env)}\n${good.tap}`);
    assert.equal(good.golden.ok, true);
    assert.doesNotMatch(good.golden.directive, /SKIP/);
    assert.match(good.tap, /ok \d+ - default-targets\.json/, 'one subtest per golden file');

    const bad = runContract({ 'default-targets.json': JSON.stringify({ ...DEFAULT_TARGETS, default: { ...DEFAULT_TARGETS.default, maxZoom: 15 } }) }, env);
    assert.equal(bad.status, 1, `${JSON.stringify(env)}\n${bad.tap}`);
    assert.match(bad.tap, /\$\.default\.maxZoom: expected one of 16/);

    const unknown = runContract({ 'mystery.json': '{}' }, env);
    assert.equal(unknown.status, 1, unknown.tap);
    assert.match(unknown.tap, /no payload shape matches the file name 'mystery\.json'/);
  }
});
