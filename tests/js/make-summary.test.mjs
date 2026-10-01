// Tests for tools/ci/make-summary.mjs: SUMMARY.md from errors.log, restore.log and .trx files.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { crashReport, distinctDiagnostics, normalizeDiagnostic, parseGuardsLog, parseSmoke, parseTrx, reportLine, smokeSection } from '../../tools/ci/make-summary.mjs';
import { cleanEnv, fixture, repoRoot, tempDir, tools, writeFile } from './helpers/ci-harness.mjs';

const WORKSPACE = '/home/runner/work/ha360/ha360';

// Lays out ci-in/<job>/... the way actions/download-artifact does and runs the tool.
function summarize({ files = {}, needs, extraArgs = [], env = {}, createIn = true } = {}) {
  const root = tempDir('make-summary');
  const inDir = path.join(root, 'ci-in');
  const outDir = path.join(root, 'ci-out');
  if (createIn) {
    fs.mkdirSync(inDir, { recursive: true });
    for (const [relative, text] of Object.entries(files)) writeFile(path.join(inDir, relative), text);
  }
  const args = [tools.makeSummary, '--in', inDir, '--out', outDir, '--root', WORKSPACE, ...extraArgs];
  if (needs !== undefined) args.push('--needs', needs);
  const result = spawnSync('node', args, {
    encoding: 'utf8',
    env: cleanEnv({ GITHUB_REF_NAME: 'slice/S0-skeleton', GITHUB_SHA: 'ABCDEF0123456789ABCDEF0123456789ABCDEF01', GITHUB_RUN_NUMBER: '7', ...env }),
  });
  const read = (name) => (fs.existsSync(path.join(outDir, name)) ? fs.readFileSync(path.join(outDir, name), 'utf8') : null);
  return { ...result, summary: read('SUMMARY.md'), errorsLog: read('errors.log'), outDir };
}

const NEEDS_FAILED = JSON.stringify({ dotnet: { result: 'failure', outputs: {} } });
const NEEDS_OK = JSON.stringify({ dotnet: { result: 'success', outputs: {} } });
const guardsNeeds = (guards, dotnet) => JSON.stringify({ guards: { result: guards, outputs: {} }, dotnet: { result: dotnet, outputs: {} } });
const section = (summary, heading) => new RegExp(`^## ${heading}\\n[\\s\\S]*?(?=^## |(?![\\s\\S]))`, 'm').exec(summary)?.[0] ?? null;
const AC_LINES = Array.from({ length: 50 }, (_, i) => `REPORT ac-coverage: AC-${String(i + 1).padStart(2, '0')} missing`);

test('compiler errors: deduplicated, workspace paths made relative, project suffix dropped', () => {
  const run = summarize({ files: { 'dotnet/errors.log': fixture('errors.sample.log') }, needs: NEEDS_FAILED });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^## Compiler errors \(4 distinct\)$/m);
  const lines = run.summary.split('\n');
  assert.ok(lines.includes('src/Realm.Web/Program.cs(3,1): CS1002 ; expected'));
  assert.ok(lines.includes("tests/Realm.Domain.Tests/SmokeTests.cs(12,9): CS0103 The name 'Foo' does not exist in the current context"));
  assert.ok(lines.includes("CSC: CS5001 Program does not contain a static 'Main' method suitable for an entry point"));
  assert.equal(lines.filter((l) => l.startsWith('src/Realm.Web/Program.cs(3,1)')).length, 1, 'three identical lines appear once');
  assert.equal(lines.filter((l) => l.includes('CS8618')).length, 1, 'two identical CS8618 lines appear once');
  assert.ok(!run.summary.includes(WORKSPACE), 'absolute runner paths are stripped');
  assert.ok(!run.summary.includes('.csproj]'), 'the trailing [project] is dropped');
  assert.equal(run.errorsLog.split('\n').filter(Boolean).length, 4, 'errors.log holds the same distinct lines');
});

test('compiler errors: the SUMMARY shows 40 distinct errors, errors.log keeps all of them', () => {
  const log = [];
  for (let i = 1; i <= 45; i += 1) {
    const line = `${WORKSPACE}/src/Realm.Domain/F${i}.cs(${i},1): error CS0103: The name 'N${i}' does not exist in the current context [${WORKSPACE}/src/Realm.Domain/Realm.Domain.csproj]`;
    log.push(line, line); // every error is reported twice
  }
  const run = summarize({ files: { 'dotnet/errors.log': log.join('\n') + '\n' }, needs: NEEDS_FAILED });
  assert.match(run.summary, /^## Compiler errors \(45 distinct\)$/m);
  const shown = run.summary.split('\n').filter((l) => /^src\/Realm\.Domain\/F\d+\.cs/.test(l));
  assert.equal(shown.length, 40);
  assert.equal(shown[0], "src/Realm.Domain/F1.cs(1,1): CS0103 The name 'N1' does not exist in the current context");
  assert.equal(shown[39], "src/Realm.Domain/F40.cs(40,1): CS0103 The name 'N40' does not exist in the current context");
  assert.match(run.summary, /\.\.\. and 5 more distinct errors/);
  assert.equal(run.errorsLog.split('\n').filter(Boolean).length, 45);
});

test('failed tests: found by regular expression in the .trx, name decoded, message cut to 15 lines', () => {
  const run = summarize({
    files: { 'dotnet/errors.log': '', 'dotnet/trx/results.trx': fixture('results.failed.trx') },
    needs: NEEDS_FAILED,
  });
  assert.match(run.summary, /^## Failed tests \(1 of 3\)$/m);
  assert.match(run.summary, /1 passed, 1 failed, 1 skipped \(1 \.trx file\)/);
  assert.match(run.summary, /^### Realm\.Domain\.Tests\.FusionTests\.Prefers <fresh> fix\("a"\)$/m);
  assert.match(run.summary, /Assert\.Equal\(\) Failure: Values differ\nExpected: 5\nActual: {3}4 & <done>/);
  assert.match(run.summary, /line 15\n/, 'line 15 of the message is shown');
  assert.ok(!run.summary.includes('line 16'), 'the message is cut after 15 lines');
  assert.match(run.summary, /\.\.\. \(2 more lines\)/);
  assert.ok(!run.summary.includes('TestProjectRuns'), 'passed tests are not listed');
  assert.ok(fs.existsSync(path.join(run.outDir, 'tests', 'results.trx')), 'the .trx is copied next to the summary');
});

test('failed tests: a .trx whose counters say "failed" but whose results the regex cannot read is a failure with a note', () => {
  const odd = fixture('results.failed.trx').replaceAll('UnitTestResult', 'TestOutcomeRecord');
  const run = summarize({ files: { 'dotnet/errors.log': '', 'dotnet/trx/odd.trx': odd }, needs: NEEDS_OK });
  assert.match(run.summary, /^- result: failure$/m, 'never green when the counters report a failure');
  assert.match(run.summary, /odd\.trx reports 1 failed test\(s\) in its counters but 0 were found in its results/);
  assert.match(run.summary, /layout of the \.trx differs/);
});

test('verdict: success only when every job succeeded and nothing failed', () => {
  const ok = summarize({ files: { 'dotnet/errors.log': '', 'dotnet/trx/r.trx': fixture('results.passed.trx') }, needs: NEEDS_OK });
  assert.match(ok.summary, /^- result: success$/m);
  assert.match(ok.summary, /^# CI summary: SUCCESS$/m);
  assert.match(ok.summary, /^## Compiler errors\n\nNone\.$/m);
  assert.match(ok.summary, /1 passed, 0 failed, 0 skipped/);
  assert.match(ok.summary, /\| dotnet \| success \|/);

  const failedJob = summarize({ files: { 'dotnet/errors.log': '' }, needs: NEEDS_FAILED });
  assert.match(failedJob.summary, /^- result: failure$/m);
  assert.match(failedJob.summary, /^- why: job dotnet: failure$/m);
  assert.match(failedJob.summary, /No compiler error and no failed test|no compiler error and no failed test/i, 'a failed job without findings points at the raw log');

  const failedTests = summarize({ files: { 'dotnet/trx/r.trx': fixture('results.failed.trx') }, needs: NEEDS_OK });
  assert.match(failedTests.summary, /^- result: failure$/m, 'a failed test fails the run even if the job says success');
});

test('header: branch, sha, run and url come from the GitHub variables', () => {
  const run = summarize({
    files: { 'dotnet/errors.log': '' },
    needs: NEEDS_OK,
    env: { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha360', GITHUB_RUN_ID: '555' },
  });
  assert.match(run.summary, /^- branch: slice\/S0-skeleton$/m);
  assert.match(run.summary, /^- sha: abcdef0123456789abcdef0123456789abcdef01$/m);
  assert.match(run.summary, /^- run: 7$/m);
  assert.match(run.summary, /^- url: https:\/\/github\.com\/Versile2\/ha360\/actions\/runs\/555$/m);
});

test('every input absent: still writes a SUMMARY.md (failure, nothing found), exit 0', () => {
  const missingFolder = summarize({ createIn: false });
  assert.equal(missingFolder.status, 0, missingFolder.stderr);
  assert.match(missingFolder.summary, /^- result: failure$/m);
  assert.match(missingFolder.summary, /^- why: no CI inputs were found$/m);
  assert.match(missingFolder.summary, /No errors\.log and no restore\.log were found\./);
  assert.match(missingFolder.summary, /No \.trx files were found\./);
  assert.equal(missingFolder.errorsLog, '');

  const emptyFolder = summarize({});
  assert.equal(emptyFolder.status, 0, emptyFolder.stderr);
  assert.match(emptyFolder.summary, /^- result: failure$/m);

  const needsOnly = summarize({ needs: NEEDS_FAILED, createIn: false });
  assert.match(needsOnly.summary, /\| dotnet \| failure \|/);
  assert.match(needsOnly.summary, /did not succeed/);
});

test('--needs that is not JSON is a failure with the reason written down', () => {
  const run = summarize({ files: { 'dotnet/errors.log': '' }, needs: '{ nope' });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /the --needs JSON could not be parsed/);
});

test('no errors.log: the tail of restore.log is shown and written to errors.log', () => {
  const lines = Array.from({ length: 60 }, (_, i) => `restore line ${i + 1}`);
  const run = summarize({ files: { 'dotnet/restore.log': lines.join('\n') + '\n' }, needs: NEEDS_FAILED });
  assert.match(run.summary, /There is no errors\.log/);
  assert.match(run.summary, /restore line 60/);
  assert.match(run.summary, /restore line 21/);
  assert.ok(!run.summary.includes('restore line 20\n'), 'only the last 40 lines are shown');
  assert.equal(run.errorsLog.trim().split('\n').length, 40);

  const real = summarize({ files: { 'dotnet/restore.log': fixture('restore.failed.log') }, needs: NEEDS_FAILED });
  assert.match(real.summary, /error NU1102: Unable to find package xunit/);
  assert.match(real.summary, /^## Warnings \(1 distinct\)$/m);
  assert.match(real.summary, /Realm\.slnx: NU1903 Package 'Newtonsoft\.Json' 9\.0\.1 has a known high severity vulnerability/);
});

test('warnings from restore.log are listed once and never fail a run', () => {
  const run = summarize({ files: { 'dotnet/errors.log': '', 'dotnet/restore.log': fixture('restore.ok.log') }, needs: NEEDS_OK });
  assert.match(run.summary, /^- result: success$/m);
  assert.match(run.summary, /^## Warnings \(1 distinct\)$/m);
});

test('build.log: its last 300 lines are kept as build.tail.log', () => {
  const lines = Array.from({ length: 400 }, (_, i) => `build line ${i + 1}`);
  const run = summarize({ files: { 'dotnet/errors.log': '', 'dotnet/build.log': lines.join('\n') + '\n' }, needs: NEEDS_OK });
  const tail = fs.readFileSync(path.join(run.outDir, 'build.tail.log'), 'utf8').trim().split('\n');
  assert.equal(tail.length, 300);
  assert.equal(tail[0], 'build line 101');
  assert.equal(tail[299], 'build line 400');
});

test('a crash inside make-summary is written as a failed run with the stack, in the same header format', () => {
  const meta = { branch: 'slice/S0-skeleton', sha: 'abcdef0123456789abcdef0123456789abcdef01', run: '9', url: 'https://ci.example.invalid/r/9' };
  const text = crashReport(meta, new Error('kaboom'));
  assert.match(text, /^- result: failure$/m);
  assert.match(text, /^- branch: slice\/S0-skeleton$/m);
  assert.match(text, /^- sha: abcdef0123456789abcdef0123456789abcdef01$/m);
  assert.match(text, /^- run: 9$/m);
  assert.match(text, /^- why: make-summary\.mjs itself failed/m);
  assert.match(text, /## make-summary\.mjs crashed[\s\S]*kaboom/);
});

test('command line: an unknown argument is a usage error (64)', () => {
  const result = spawnSync('node', [tools.makeSummary, '--bogus'], { encoding: 'utf8', env: cleanEnv() });
  assert.equal(result.status, 64);
  assert.match(result.stderr, /unknown argument '--bogus'/);
});

test('helpers: normalizeDiagnostic and parseTrx on small inputs', () => {
  assert.equal(normalizeDiagnostic('', 'error', ''), null);
  assert.equal(normalizeDiagnostic('Build FAILED.', 'error', ''), 'Build FAILED.', 'a line that is not a diagnostic is kept as it is');
  assert.equal(normalizeDiagnostic('/w/A.cs(1,2): error CS1: boom [/w/A.csproj]', 'error', '/w'), 'A.cs(1,2): CS1 boom');
  assert.equal(normalizeDiagnostic('   1:7>/w/A.cs(1,2): error CS1: boom [/w/A.csproj]', 'error', '/w'), 'A.cs(1,2): CS1 boom', 'the MSBuild node prefix "   1:7>" is dropped');
  assert.equal(normalizeDiagnostic('   2>/w/A.cs(1,2): error CS1: boom', 'error', '/w'), 'A.cs(1,2): CS1 boom');
  assert.deepEqual(distinctDiagnostics(['/w/A.cs(1,2): error CS1: boom\n/w/A.cs(1,2): error CS1: boom\n'], 'error', '/w'), ['A.cs(1,2): CS1 boom']);
  const none = parseTrx('<TestRun></TestRun>');
  assert.deepEqual(none, { passed: 0, failed: 0, skipped: 0, failures: [], countersFailed: 0 });
  const selfClosing = parseTrx('<UnitTestResult testName="X.Y" outcome="Failed" />');
  assert.equal(selfClosing.failed, 1);
  assert.equal(selfClosing.failures[0].name, 'X.Y');
});

test('guards: FAIL lines are shown verbatim, first; the why line and the Notes name the guards, not the compiler', () => {
  const log = [
    'PASS config-name',
    'FAIL no-wallclock: src/Realm.Web/Clock.cs:12 DateTime.UtcNow outside the clock adapter (use TimeProvider)',
    ...AC_LINES.slice(0, 3),
    'FAIL no-wallclock: src/Realm.Web/Other.cs:7 DateTime.Now outside the clock adapter (use TimeProvider)',
    'FAIL package-pins: Directory.Packages.props:9 package \'Foo.Bar\' is not in tools/ci/packages.allow.txt (a new package needs a decision)',
    'PASS no-static-files',
    '',
  ].join('\n');
  const run = summarize({ files: { 'guards/guards.log': log }, needs: guardsNeeds('failure', 'skipped') });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: guards failed: no-wallclock, package-pins$/m, 'one why entry, guard names once each, no "job dotnet: skipped"');
  const guards = section(run.summary, 'Guards');
  assert.ok(guards, 'a Guards section exists');
  const lines = guards.split('\n');
  for (const fail of log.split('\n').filter((l) => l.startsWith('FAIL '))) assert.ok(lines.includes(fail), `shown verbatim: ${fail}`);
  const indexOf = (prefix) => lines.findIndex((l) => l.startsWith(prefix));
  assert.ok(indexOf('FAIL package-pins') < indexOf('REPORT ac-coverage'), 'FAIL lines come before REPORT lines');
  assert.ok(lines.includes('REPORT ac-coverage: 3 AC ids missing (AC-01 \u2026 AC-03)'));
  assert.match(guards, /^PASS: 2 of 5 guards\.$/m, 'config-name and no-static-files passed; no-wallclock, package-pins and ac-coverage did not');
  assert.ok(run.summary.indexOf('## Jobs') < run.summary.indexOf('## Guards'), 'the section follows the job table');
  assert.match(run.summary, /\| guards \| failure \|/);
  assert.match(run.summary, /\| dotnet \| skipped \|/);
  const notes = section(run.summary, 'Notes');
  assert.match(notes, /guards failed: no-wallclock, package-pins\./);
  assert.match(notes, /jobs behind the guards did not run \(dotnet\)/);
  assert.doesNotMatch(run.summary, /test host|no compiler error and no failed test|job dotnet: skipped|job guards: failure/i);
});

test('guards: REPORT findings become one line per guard, with consecutive ids as ranges', () => {
  const run = summarize({ files: { 'guards/guards.log': ['PASS config-name', ...AC_LINES, ''].join('\n') }, needs: guardsNeeds('success', 'success') });
  const guards = section(run.summary, 'Guards');
  assert.equal(guards.split('\n').filter((l) => l.startsWith('REPORT ')).length, 1, '50 findings, one line');
  assert.ok(guards.includes('REPORT ac-coverage: 50 AC ids missing (AC-01 \u2026 AC-50)\n'));
  assert.match(guards, /^PASS: 1 of 2 guards\.$/m);
  assert.match(run.summary, /^- result: success$/m, 'REPORT never fails a run');
  assert.doesNotMatch(run.summary, /^- why:/m);
  assert.doesNotMatch(run.summary, /^## Notes/m);

  // Gaps, a pair (listed, not a range), a single id, two statuses, and findings that are not ids.
  const ids = (list, status) => list.map((n) => `REPORT ac-coverage: AC-${String(n).padStart(2, '0')} ${status}`);
  const mixed = [
    ...ids([1, 2, 3, 4, 7, 9, 10, 12, 13, 14, 50], 'missing'),
    ...ids([8], 'unmatched'),
    'REPORT testid-contract: data-testid \'a\' is not present in src/',
    'REPORT testid-contract: data-testid \'b\' is not present in src/',
    'REPORT testid-contract: data-testid \'c\' is not present in src/',
    'REPORT testid-contract: data-testid \'d\' is not present in src/',
    'REPORT testid-contract: data-testid \'a\' is not present in src/',
  ].join('\n');
  const out = summarize({ files: { 'guards/guards.log': mixed + '\n' }, needs: guardsNeeds('success', 'success') });
  const lines = section(out.summary, 'Guards').split('\n');
  assert.ok(lines.includes('REPORT ac-coverage: 11 AC ids missing (AC-01 \u2026 AC-04, AC-07, AC-09, AC-10, AC-12 \u2026 AC-14, AC-50); 1 AC id unmatched (AC-08)'));
  assert.ok(lines.includes('REPORT testid-contract: 4 findings: data-testid \'a\' is not present in src/; data-testid \'b\' is not present in src/; data-testid \'c\' is not present in src/; ... and 1 more'));
  assert.equal(lines.filter((l) => l.startsWith('REPORT ')).length, 2, 'one line per guard');
});

test('guards: the guards job failed without a FAIL line (crash, no log): the why line says so and the raw log is the pointer', () => {
  const crashed = summarize({
    files: { 'guards/guards.log': 'file:///w/tools/ci/guards.mjs:10\nTypeError: boom\n    at run (guards.mjs:10:3)\n' },
    needs: guardsNeeds('failure', 'skipped'),
    env: { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha360', GITHUB_RUN_ID: '9' },
  });
  assert.match(crashed.summary, /^- why: guards failed: unknown \(guards\.log holds no FAIL line\)$/m);
  assert.match(section(crashed.summary, 'Guards'), /TypeError: boom/, 'what the script printed is shown as it is');
  assert.match(section(crashed.summary, 'Guards'), /^PASS: 0 of 0 guards\.$/m);
  assert.match(section(crashed.summary, 'Notes'), /raw job log of the workflow run \(https:\/\/github\.com\/Versile2\/ha360\/actions\/runs\/9\)/);

  const noLog = summarize({ needs: guardsNeeds('failure', 'skipped') });
  assert.match(noLog.summary, /^- why: guards failed: unknown \(no guards\.log was found\)$/m);
  assert.equal(section(noLog.summary, 'Guards'), null, 'no log, no section');
  assert.doesNotMatch(noLog.summary, /test host|no compiler error and no failed test/i);
});

test('guards: no guards.log means no Guards section and no error; a passing log keeps the old verdict rules', () => {
  const none = summarize({ files: { 'dotnet/errors.log': '' }, needs: NEEDS_OK });
  assert.equal(none.status, 0, none.stderr);
  assert.doesNotMatch(none.summary, /^## Guards/m);
  assert.match(none.summary, /^- result: success$/m);

  const pass = summarize({
    files: { 'guards/guards.log': 'PASS config-name\nPASS addon-validate (nothing to check yet)\n', 'dotnet/errors.log': '' },
    needs: guardsNeeds('success', 'failure'),
  });
  assert.match(section(pass.summary, 'Guards'), /^## Guards\n\nPASS: 2 of 2 guards\.\n+$/, 'nothing but the count when every guard passed');
  assert.match(pass.summary, /^- why: job dotnet: failure$/m, 'a dotnet failure after passing guards keeps its own wording');
  assert.match(pass.summary, /no compiler error and no failed test/i);
});

test('guards: the real output of tools/ci/guards.mjs is understood', () => {
  const real = spawnSync('node', [path.join(repoRoot, 'tools', 'ci', 'guards.mjs')], { encoding: 'utf8', cwd: repoRoot });
  assert.ok(real.status === 0 || real.status === 1, real.stderr);
  const parsed = parseGuardsLog(real.stdout);
  assert.deepEqual(parsed.other, [], 'every line of guards.mjs is a PASS, FAIL or REPORT line');
  assert.equal(parsed.names.length, 12, 'twelve guards');
  const run = summarize({ files: { 'guards/guards.log': real.stdout }, needs: guardsNeeds(real.status === 0 ? 'success' : 'failure', 'success') });
  assert.match(section(run.summary, 'Guards'), /^PASS: \d+ of 12 guards\.$/m);
});

test('helpers: parseGuardsLog and reportLine on small inputs', () => {
  const parsed = parseGuardsLog('PASS a (note)\r\nFAIL b: x/y.cs:3 bad\r\nREPORT c: C-1 open\n\nstray line\nFAIL allow-list: tools/ci/guards.allow.json entry 0 has no reason\n');
  assert.deepEqual(parsed.passes, ['a']);
  assert.deepEqual(parsed.fails.map((f) => f.name), ['b', 'allow-list']);
  assert.equal(parsed.fails[0].line, 'FAIL b: x/y.cs:3 bad');
  assert.deepEqual([...parsed.reports], [['c', ['C-1 open']]]);
  assert.deepEqual(parsed.names, ['a', 'b', 'c', 'allow-list']);
  assert.deepEqual(parsed.other, ['stray line']);
  assert.equal(reportLine('c', ['AC-9 missing', 'AC-10 missing', 'AC-11 missing', 'AC-9 missing']), 'REPORT c: 3 AC ids missing (AC-9 \u2026 AC-11)', 'duplicates count once, numbers compare as numbers');
  assert.equal(reportLine('c', ['AC-01 missing', 'AC-02 missing']), 'REPORT c: 2 AC ids missing (AC-01, AC-02)', 'a pair is listed');
  assert.equal(reportLine('c', ['AC-07 missing']), 'REPORT c: 1 AC id missing (AC-07)');
  assert.equal(reportLine('c', ['src/a.cs [AC-99] matches no row']), 'REPORT c: src/a.cs [AC-99] matches no row');
});

// ---------------------------------------------------------------------------------------------
// Docker smoke: the "## Docker smoke" section from smoke.json (hand-made here; the real file is written by tools/ci/image-smoke.sh)
// ---------------------------------------------------------------------------------------------

const smokeData = (overrides = {}) => ({
  image: 'realm:smoke',
  image_size_bytes: 297400000,
  app_layer_bytes: 31200000,
  compressed_estimate_bytes: 112300000,
  time_to_healthy_s: 1.842,
  item1: 'PASS', item1_title: 'first /healthz 200', item1_detail: '1.842 s',
  item2: 'PASS', item2_title: 'base href follows X-Ingress-Path', item2_detail: "'/' without the header, '/api/hassio_ingress/TOKEN/' with it, an invalid value ignored",
  item3: 'PASS', item3_title: 'content types', item3_detail: '_framework/blazor.web.js (text/javascript)',
  item4: 'PASS', item4_title: 'no Set-Cookie on /', item4_detail: 'GET / answered 200 without Set-Cookie',
  item9: 'PASS', item9_title: 'image runs as root', item9_detail: 'id -u prints 0',
  item10: 'PASS', item10_title: 'no forbidden path in the image', item10_detail: '412 entries checked',
  ...overrides,
});
const smokeFile = (overrides) => ({ 'docker-smoke/smoke.json': JSON.stringify(smokeData(overrides)) });
const smokeNeeds = (smoke, dotnet = 'success') => JSON.stringify({ guards: { result: 'success' }, dotnet: { result: dotnet }, 'docker-smoke': { result: smoke } });

test('docker smoke: image size, time to healthy and every item are rendered; a passing smoke keeps the run green', () => {
  const run = summarize({
    files: { 'dotnet/errors.log': '', 'dotnet/restore.log': fixture('restore.ok.log'), ...smokeFile() },
    needs: smokeNeeds('success'),
  });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: success$/m);
  assert.doesNotMatch(run.summary, /^- why:/m);
  assert.match(run.summary, /\| docker-smoke \| success \|/);
  const smoke = section(run.summary, 'Docker smoke');
  assert.ok(smoke, 'a Docker smoke section exists');
  const lines = smoke.split('\n');
  assert.ok(lines.includes('- image: realm:smoke'));
  assert.ok(lines.includes('- image size (uncompressed): 297.4 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)'));
  assert.ok(lines.includes('- app layer (published output): 31.2 MB, within the target (6.5: target at most 35 MB, warn over 45 MB)'));
  assert.ok(lines.includes('- compressed size (estimate: gzip of docker save): 112.3 MB, within the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)'));
  assert.ok(lines.includes('- time to healthy: 1.84 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)'));
  assert.ok(lines.includes('| item | check | result | detail |'));
  assert.ok(lines.includes('| 1 | first /healthz 200 | PASS | 1.842 s |'));
  assert.ok(lines.includes("| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it, an invalid value ignored |"));
  const order = lines.filter((l) => /^\| \d+ \|/.test(l)).map((l) => Number(/^\| (\d+) \|/.exec(l)[1]));
  assert.deepEqual(order, [1, 2, 3, 4, 9, 10], 'items are listed by number, 10 after 9');
  assert.match(smoke, /^Items: 6 PASS, 0 WARN, 0 FAIL, 0 SKIP\./m);
  assert.doesNotMatch(smoke, /Container log/);
  assert.ok(run.summary.indexOf('## Tests') < run.summary.indexOf('## Docker smoke'), 'after the test results');
  assert.ok(run.summary.indexOf('## Docker smoke') < run.summary.indexOf('## Warnings'), 'before the warnings');
});

test('docker smoke: a FAIL item fails the run and is named in why; the job is not blamed on the raw log; the container log is shown', () => {
  const run = summarize({
    files: {
      'dotnet/errors.log': '',
      ...smokeFile({
        item3: 'FAIL',
        item3_detail: "_framework/blazor.web.js: status 200, type 'application/javascript' | want text/javascript",
        item9: 'FAIL',
        item9_detail: "id -u printed '1654'",
        container_log_tail: 'info: Now listening on: http://[::]:8099\nfail: Unhandled exception. System.InvalidOperationException: boom\n',
      }),
    },
    needs: smokeNeeds('failure'),
  });
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: docker smoke failed: items 3, 9$/m, 'one why entry, no "job docker-smoke: failure"');
  assert.doesNotMatch(run.summary, /job docker-smoke|did not succeed|no compiler error and no failed test/i);
  assert.match(run.summary, /\| docker-smoke \| failure \|/);
  const smoke = section(run.summary, 'Docker smoke');
  assert.ok(smoke.split('\n').includes("| 3 | content types | FAIL | _framework/blazor.web.js: status 200, type 'application/javascript' \\| want text/javascript |"), 'the pipe in a detail is escaped');
  assert.ok(smoke.split('\n').includes("| 9 | image runs as root | FAIL | id -u printed '1654' |"));
  assert.match(smoke, /^Items: 4 PASS, 0 WARN, 2 FAIL, 0 SKIP\./m);
  assert.match(smoke, /Container log \(last lines[^\n]*\n\n```text\ninfo: Now listening on: http:\/\/\[::\]:8099\nfail: Unhandled exception\. System\.InvalidOperationException: boom\n```/);

  const one = summarize({ files: { 'dotnet/errors.log': '', ...smokeFile({ item10: 'FAIL' }) }, needs: smokeNeeds('failure') });
  assert.match(one.summary, /^- why: docker smoke failed: item 10$/m, 'a single item is "item N"');
});

test('docker smoke: a slow start (WARN) and sizes over the 6.5 lines are reported but never fail the run', () => {
  const run = summarize({
    files: {
      'dotnet/errors.log': '',
      ...smokeFile({
        item1: 'WARN',
        item1_detail: '4.173 s is over the 3 s target',
        time_to_healthy_s: 4.173,
        image_size_bytes: 451000000,
        app_layer_bytes: 46000000,
        compressed_estimate_bytes: 125000000,
      }),
    },
    needs: smokeNeeds('success'),
  });
  assert.match(run.summary, /^- result: success$/m);
  const smoke = section(run.summary, 'Docker smoke');
  assert.ok(smoke.split('\n').includes('| 1 | first /healthz 200 | WARN | 4.173 s is over the 3 s target |'));
  assert.match(smoke, /^Items: 5 PASS, 1 WARN, 0 FAIL, 0 SKIP\./m);
  assert.match(smoke, /^- time to healthy: 4.17 s/m);
  assert.match(smoke, /^- image size \(uncompressed\): 451\.0 MB, over the fail line /m);
  assert.match(smoke, /^- app layer \(published output\): 46\.0 MB, over the warn line /m, 'the app layer row has no fail line');
  assert.match(smoke, /^- compressed size \(estimate: gzip of docker save\): 125\.0 MB, above the target /m);
});

test('docker smoke: items that were skipped because the container never started are shown; item 1 fails the run', () => {
  const run = summarize({
    files: {
      'dotnet/errors.log': '',
      'docker-smoke/smoke.json': JSON.stringify({
        image: 'realm:smoke',
        image_size_bytes: 300000000,
        item1: 'FAIL', item1_title: 'first /healthz 200', item1_detail: 'no 200 from /healthz within 30 s (last status 000)',
        item2: 'SKIP', item2_title: 'not run', item2_detail: 'the container never answered /healthz with 200 (item 1)',
        item9: 'PASS', item9_title: 'image runs as root', item9_detail: 'id -u prints 0',
      }),
    },
    needs: smokeNeeds('failure'),
  });
  assert.match(run.summary, /^- why: docker smoke failed: item 1$/m, 'SKIP is not a failure by itself');
  const smoke = section(run.summary, 'Docker smoke');
  assert.match(smoke, /^- time to healthy: not measured/m);
  assert.match(smoke, /^Items: 1 PASS, 0 WARN, 1 FAIL, 1 SKIP\./m);
});

test('docker smoke: no smoke.json. A failed or succeeded job is a failure with a pointer; a skipped job and a missing --needs give no section', () => {
  const env = { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha360', GITHUB_RUN_ID: '12' };
  const failed = summarize({ files: { 'dotnet/errors.log': '' }, needs: smokeNeeds('failure'), env });
  assert.match(failed.summary, /^- result: failure$/m);
  assert.match(failed.summary, /^- why: job docker-smoke: failure$/m);
  assert.match(section(failed.summary, 'Docker smoke'), /^No smoke\.json was found: the image build or the smoke script stopped before it wrote one\. The cause is in the raw job log of the workflow run \(https:\/\/github\.com\/Versile2\/ha360\/actions\/runs\/12\)\.$/m);

  const succeeded = summarize({ files: { 'dotnet/errors.log': '' }, needs: smokeNeeds('success') });
  assert.match(succeeded.summary, /^- why: job docker-smoke succeeded but left no smoke\.json$/m);
  assert.match(succeeded.summary, /^- result: failure$/m);

  const skipped = summarize({ files: { 'guards/guards.log': 'FAIL no-wallclock: src/A.cs:1 DateTime.Now\n' }, needs: JSON.stringify({ guards: { result: 'failure' }, dotnet: { result: 'skipped' }, 'docker-smoke': { result: 'skipped' } }) });
  assert.equal(section(skipped.summary, 'Docker smoke'), null, 'the guards failed, the job never ran: nothing to say about it');
  assert.match(skipped.summary, /^- why: guards failed: no-wallclock$/m);
  assert.doesNotMatch(skipped.summary, /docker-smoke:/);

  const noNeeds = summarize({ files: { 'dotnet/errors.log': '' } });
  assert.equal(section(noNeeds.summary, 'Docker smoke'), null);
});

test('docker smoke: an unreadable smoke.json, an empty one and a script error fail the run, never pass silently', () => {
  const broken = summarize({ files: { 'dotnet/errors.log': '', 'docker-smoke/smoke.json': '{ not json' }, needs: smokeNeeds('failure') });
  assert.match(broken.summary, /^- why: docker smoke: smoke\.json is not valid JSON$/m);
  assert.match(section(broken.summary, 'Docker smoke'), /^smoke\.json is not valid JSON \(/m);

  const list = summarize({ files: { 'dotnet/errors.log': '', 'docker-smoke/smoke.json': '[]' }, needs: smokeNeeds('success') });
  assert.match(list.summary, /^- why: docker smoke: smoke\.json is not a JSON object$/m);
  assert.match(list.summary, /^- result: failure$/m);

  const empty = summarize({ files: { 'dotnet/errors.log': '', 'docker-smoke/smoke.json': '{}' }, needs: smokeNeeds('success') });
  assert.match(empty.summary, /^- why: docker smoke: smoke\.json holds no item result/m);

  const early = summarize({
    files: { 'dotnet/errors.log': '', 'docker-smoke/smoke.json': JSON.stringify({ error: "image 'realm:smoke' does not exist locally" }) },
    needs: smokeNeeds('failure'),
  });
  assert.match(early.summary, /^- why: docker smoke: image 'realm:smoke' does not exist locally$/m);
  assert.match(section(early.summary, 'Docker smoke'), /^The smoke script reported: image 'realm:smoke' does not exist locally$/m);
  assert.doesNotMatch(early.summary, /did not succeed/);
});

test('helpers: parseSmoke reads items by number, tolerates case and gaps, and counts an unknown status as a failure', () => {
  const parsed = parseSmoke(JSON.stringify({ item10: 'PASS', item2: 'pass', item1: 'BROKEN', item1_title: 'first', other: 3 }));
  assert.deepEqual(parsed.items.map((i) => i.n), [1, 2, 10]);
  assert.deepEqual(parsed.items.map((i) => i.status), ['BROKEN', 'PASS', 'PASS']);
  assert.equal(parsed.items[1].title, '', 'a missing title or detail is empty');
  assert.deepEqual(parsed.failed.map((i) => i.n), [1]);
  assert.equal(parsed.problem, 'docker smoke failed: item 1');
  assert.equal(parseSmoke('{"item1":"PASS"}').problem, null);
  assert.match(parseSmoke('nope').unreadable, /not valid JSON/);
  assert.match(smokeSection(parseSmoke('nope')), /^## Docker smoke\n\nsmoke\.json is not valid JSON/);
  const long = smokeSection(parseSmoke(JSON.stringify({ item1: 'FAIL', item1_title: 't', item1_detail: `${'x'.repeat(600)}\nsecond line` })));
  assert.match(long, /\| 1 \| t \| FAIL \| x{400}\.\.\. \|/, 'a long detail is cut and kept on one line');
});
