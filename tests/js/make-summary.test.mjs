// Tests for tools/ci/make-summary.mjs: SUMMARY.md from errors.log, restore.log and .trx files.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import {
  acStatus,
  acTokens,
  buildAcMatrix,
  crashReport,
  distinctDiagnostics,
  e2eReport,
  jsReport,
  keepTail,
  normalizeDiagnostic,
  parseGuardsLog,
  parsePlaywright,
  parseSmoke,
  parseStylesLog,
  parseTap,
  parseTrx,
  parseTrxAcTests,
  parseTsc,
  reportLine,
  screenshotCopies,
  smokeSection,
  stylesReport,
} from '../../tools/ci/make-summary.mjs';
import { cleanEnv, fixture, repoRoot, tempDir, tools, writeFile } from './helpers/ci-harness.mjs';

const WORKSPACE = '/home/runner/work/ha-cartographer/ha-cartographer';

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
    env: { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha-cartographer', GITHUB_RUN_ID: '555' },
  });
  assert.match(run.summary, /^- branch: slice\/S0-skeleton$/m);
  assert.match(run.summary, /^- sha: abcdef0123456789abcdef0123456789abcdef01$/m);
  assert.match(run.summary, /^- run: 7$/m);
  assert.match(run.summary, /^- url: https:\/\/github\.com\/Versile2\/ha-cartographer\/actions\/runs\/555$/m);
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
    env: { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha-cartographer', GITHUB_RUN_ID: '9' },
  });
  assert.match(crashed.summary, /^- why: guards failed: unknown \(guards\.log holds no FAIL line\)$/m);
  assert.match(section(crashed.summary, 'Guards'), /TypeError: boom/, 'what the script printed is shown as it is');
  assert.match(section(crashed.summary, 'Guards'), /^PASS: 0 of 0 guards\.$/m);
  assert.match(section(crashed.summary, 'Notes'), /raw job log of the workflow run \(https:\/\/github\.com\/Versile2\/ha-cartographer\/actions\/runs\/9\)/);

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
  item3: 'PASS', item3_title: 'content types', item3_detail: '_framework/blazor.web.k3j9x.js (text/javascript)',
  item4: 'PASS', item4_title: 'Set-Cookie on / (informational)', item4_detail: 'GET / answered 200 without Set-Cookie',
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
  assert.doesNotMatch(smoke, /Container log|Response headers/);
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
        failing_requests: 'item 3 content types\n  response headers of GET /_framework/blazor.web.js -> 404\n    HTTP/1.1 404 Not Found\n    Content-Length: 0\n  the image\'s static web assets (what MapStaticAssets reads)\n    ./Realm.Web.staticwebassets.endpoints.json: 4096 bytes, 40 routes\n',
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
  assert.match(smoke, /^Response headers of the failing requests[^\n]*\n\n```text\nitem 3 content types\n  response headers of GET \/_framework\/blazor\.web\.js -> 404\n    HTTP\/1\.1 404 Not Found\n    Content-Length: 0\n  the image's static web assets[^\n]*\n    \.\/Realm\.Web\.staticwebassets\.endpoints\.json: 4096 bytes, 40 routes\n```/m, 'the request evidence is fenced, indentation kept');
  assert.ok(smoke.indexOf('Response headers of the failing requests') < smoke.indexOf('Container log'), 'headers before the container log');
  assert.ok(smoke.indexOf('Items: 4 PASS') < smoke.indexOf('Response headers of the failing requests'), 'after the table');

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
        item4: 'WARN',
        item4_detail: 'GET / sets a cookie: .AspNetCore.Antiforgery.VyLW6ORzMgk (D61: informational; the Blazor antiforgery cookie is expected)',
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
  assert.ok(smoke.split('\n').includes('| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.VyLW6ORzMgk (D61: informational; the Blazor antiforgery cookie is expected) |'), 'the cookie warning (D61) is a row, not a failure');
  assert.match(smoke, /^Items: 4 PASS, 2 WARN, 0 FAIL, 0 SKIP\./m);
  assert.doesNotMatch(run.summary, /^- why:/m, 'WARN items never put a reason on the run');
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
  const env = { GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'Versile2/ha-cartographer', GITHUB_RUN_ID: '12' };
  const failed = summarize({ files: { 'dotnet/errors.log': '' }, needs: smokeNeeds('failure'), env });
  assert.match(failed.summary, /^- result: failure$/m);
  assert.match(failed.summary, /^- why: job docker-smoke: failure$/m);
  assert.match(section(failed.summary, 'Docker smoke'), /^No smoke\.json was found: the image build or the smoke script stopped before it wrote one\. The cause is in the raw job log of the workflow run \(https:\/\/github\.com\/Versile2\/ha-cartographer\/actions\/runs\/12\)\.$/m);

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
  assert.equal(parseSmoke('{"item1":"PASS"}').requests, null, 'no failing_requests, no block');
  assert.equal(parseSmoke('{"item1":"PASS","failing_requests":"  \\n"}').requests, null, 'a blank value is no block');
  assert.equal(parseSmoke('{"item1":"WARN","failing_requests":"item 4"}').requests, 'item 4');
  assert.equal(parseSmoke('{"item1":"WARN","item4":"WARN"}').problem, null, 'WARN is not a failure');
  assert.match(parseSmoke('nope').unreadable, /not valid JSON/);
  assert.match(smokeSection(parseSmoke('nope')), /^## Docker smoke\n\nsmoke\.json is not valid JSON/);
  const long = smokeSection(parseSmoke(JSON.stringify({ item1: 'FAIL', item1_title: 't', item1_detail: `${'x'.repeat(600)}\nsecond line` })));
  assert.match(long, /\| 1 \| t \| FAIL \| x{400}\.\.\. \|/, 'a long detail is cut and kept on one line');
});

// ---------------------------------------------------------------------------------------------
// S6a: the AC matrix ("## Acceptance criteria") and the e2e job (Playwright's results.json, the payload contract TAP, screenshots)
// ---------------------------------------------------------------------------------------------

// A .trx with one result per [name, outcome].
const trxOf = (results) =>
  `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>${results
    .map(([name, outcome]) => `<UnitTestResult testId="x" testName="${name}" outcome="${outcome}" />`)
    .join('')}</Results><ResultSummary outcome="Completed"><Counters total="${results.length}" executed="${results.length}" passed="${results.filter((r) => r[1] === 'Passed').length}" failed="${results.filter((r) => r[1] === 'Failed').length}" error="0" /></ResultSummary></TestRun>`;

// A Playwright JSON report (the shape of the `json` reporter: suites > suites > specs > tests > results) from [{ title, project, status, retries, error, attachments }].
// status is Playwright's own: expected | unexpected | flaky | skipped.
const playwrightOf = (tests, errors = []) => {
  const spec = (t, i) => {
    const attempts = t.attempts ?? (t.status === 'flaky' ? ['failed', 'passed'] : t.status === 'unexpected' ? ['failed', 'failed'] : t.status === 'skipped' ? ['skipped'] : ['passed']);
    return {
      title: t.title,
      ok: t.status === 'expected' || t.status === 'flaky',
      tags: [],
      id: `spec-${i}`,
      file: 'specs/demo.spec.ts',
      line: 10 + i,
      column: 3,
      tests: [
        {
          timeout: 45000,
          annotations: [],
          expectedStatus: t.expectedStatus ?? 'passed',
          projectId: t.project ?? 'phone',
          projectName: t.project ?? 'phone',
          results: attempts.map((status, retry) => ({
            workerIndex: 0,
            status,
            duration: 5,
            errors: [],
            error: status === 'failed' ? { message: t.error ?? 'Error: expect(received).toBe(expected)', location: { file: '/w/tests/e2e/specs/demo.spec.ts', line: 12, column: 5 } } : undefined,
            stdout: [],
            stderr: [],
            retry,
            attachments: status === 'failed' && retry === attempts.length - 1 ? (t.attachments ?? []) : [],
          })),
          status: t.status,
        },
      ],
    };
  };
  return JSON.stringify({
    config: {},
    suites: [{ title: 'demo.spec.ts', file: 'demo.spec.ts', column: 0, line: 0, specs: [], suites: [{ title: 'demo group', file: 'demo.spec.ts', column: 6, line: 3, specs: tests.map(spec), suites: [] }] }],
    errors,
    stats: {},
  });
};

const e2eNeeds = (e2e) => JSON.stringify({ dotnet: { result: 'success', outputs: {} }, e2e: { result: e2e, outputs: {} } });
const acRows = (summary) => new Map([...summary.matchAll(/^\| (AC-\d\d) \| (\w+) \| (.*) \|$/gm)].map((m) => [m[1], { status: m[2], found: m[3] }]));

test('helpers: acTokens reads [AC-nn] and [AC-nna], ignores ids outside AC-01..AC-50', () => {
  assert.deepEqual(acTokens('[AC-05] [AC-49a] two tokens'), [{ n: 5, part: '05' }, { n: 49, part: '49a' }]);
  assert.deepEqual(acTokens('Realm.Web.Tests.X.[AC-13a] [AC-16a] pins'), [{ n: 13, part: '13a' }, { n: 16, part: '16a' }]);
  assert.deepEqual(acTokens('[AC-00] [AC-51] [AC-5] [ac-05] AC-05 [X-01] [AC-050]'), [], 'out of range, one digit, lower case, no brackets, three digits');
});

test('helpers: parseTrxAcTests keeps the tests whose name carries a token, decodes the name and maps the outcome', () => {
  const tests = parseTrxAcTests(
    trxOf([
      ['Realm.Web.Tests.A.[AC-01] one', 'Passed'],
      ['Realm.Web.Tests.A.[AC-02] a &lt;b&gt; &amp; c', 'Failed'],
      ['Realm.Web.Tests.A.[AC-03] third', 'NotExecuted'],
      ['Realm.Web.Tests.A.[AC-04] fourth', 'Inconclusive'],
      ['Realm.Web.Tests.A.no token', 'Passed'],
      ['Realm.Web.Tests.A.[AC-99] outside', 'Passed'],
    ]),
  );
  assert.deepEqual(tests, [
    { title: 'Realm.Web.Tests.A.[AC-01] one', status: 'passed' },
    { title: 'Realm.Web.Tests.A.[AC-02] a <b> & c', status: 'failed' },
    { title: 'Realm.Web.Tests.A.[AC-03] third', status: 'skipped' },
    { title: 'Realm.Web.Tests.A.[AC-04] fourth', status: 'skipped' },
  ]);
  assert.deepEqual(parseTrxAcTests('<TestRun></TestRun>'), []);
});

test('helpers: parseTap reads ok, not ok and directives, the counters and the YAML block of a failure', () => {
  const tap = [
    'TAP version 13',
    '# Subtest: a suite',
    'ok 1 - passes [AC-01]',
    'not ok 2 - breaks [AC-02]',
    '  ---',
    '  duration_ms: 1.5',
    '  error: \'Expected values to be strictly equal:\'',
    '  expected: 1',
    '  actual: 2',
    '  ...',
    'ok 3 - later # SKIP not yet',
    'ok 4 - planned # TODO soon',
    'not ok 5 - failing todo # TODO soon',
    '1..5',
    '# tests 5',
    '# pass 2',
    '# fail 1',
    '',
  ].join('\n');
  const parsed = parseTap(tap);
  assert.deepEqual(parsed.tests, [
    { title: 'passes [AC-01]', status: 'passed' },
    { title: 'breaks [AC-02]', status: 'failed' },
    { title: 'later', status: 'skipped' },
    { title: 'planned', status: 'skipped' },
    { title: 'failing todo', status: 'skipped' },
  ]);
  assert.equal(parsed.pass, 2);
  assert.equal(parsed.fail, 1);
  assert.equal(parsed.failures.length, 1);
  assert.equal(parsed.failures[0].title, 'breaks [AC-02]');
  assert.match(parsed.failures[0].detail, /error: 'Expected values to be strictly equal:'\n\s+expected: 1\n\s+actual: 2/);
  assert.doesNotMatch(parsed.failures[0].detail, /^\s*\.\.\.\s*$/m, 'the YAML terminator is not part of the detail');
  assert.deepEqual(parseTap('nothing here'), { tests: [], pass: null, fail: null, failures: [] });
  // Nested subtests are indented in the TAP of node:test; they count like the top-level ones.
  assert.deepEqual(parseTap('    ok 1 - inner [AC-07]\nok 1 - outer').tests.map((t) => t.title), ['inner [AC-07]', 'outer']);
});

test('helpers: parsePlaywright maps passed, failed, flaky and skipped, joins the titles and keeps the first error and its screenshots', () => {
  const parsed = parsePlaywright(
    playwrightOf([
      { title: '[AC-01] passes', status: 'expected' },
      { title: '[AC-02] fails', status: 'unexpected', project: 'unfolded', error: '\u001b[31mError: boom\u001b[39m\nCall log', attachments: [{ name: 'screenshot', contentType: 'image/png', path: '/w/ci-out/e2e/artifacts/t-1/test-failed-1.png' }, { name: 'trace', contentType: 'application/zip', path: '/w/t.zip' }] },
      { title: '[AC-03] flaky', status: 'flaky' },
      { title: '[AC-04] skipped', status: 'skipped' },
      { title: '[AC-05] expected to fail', status: 'expected', expectedStatus: 'failed', attempts: ['failed'] },
    ]),
  );
  assert.deepEqual(parsed.tests.map((t) => [t.specTitle, t.status, t.project]), [
    ['[AC-01] passes', 'passed', 'phone'],
    ['[AC-02] fails', 'failed', 'unfolded'],
    ['[AC-03] flaky', 'flaky', 'phone'],
    ['[AC-04] skipped', 'skipped', 'phone'],
    ['[AC-05] expected to fail', 'skipped', 'phone'],
  ]);
  assert.equal(parsed.tests[0].title, 'demo group › [AC-01] passes', 'the file suite is left out, the describe title is kept');
  assert.equal(parsed.tests[1].message, 'Error: boom\nCall log', 'colour codes are removed');
  assert.equal(parsed.tests[1].location, 'demo.spec.ts:12');
  assert.deepEqual(parsed.tests[1].screenshots, ['/w/ci-out/e2e/artifacts/t-1/test-failed-1.png'], 'only PNG attachments');
  assert.deepEqual(parsed.errors, []);
  assert.deepEqual(parsePlaywright(playwrightOf([], [{ message: 'Error: no tests found' }])).errors, ['Error: no tests found']);
  assert.match(parsePlaywright('{ nope').unreadable, /not valid JSON/);
  assert.match(parsePlaywright('{"config":{}}').unreadable, /no "suites"/);
  assert.match(parsePlaywright('[]').unreadable, /no "suites"/);
});

test('helpers: buildAcMatrix gives every criterion one row; failed beats flaky beats partial beats passed; a suffix counts for its criterion', () => {
  const rows = buildAcMatrix([
    { title: '[AC-01] a', status: 'passed', source: 'dotnet' },
    { title: '[AC-02] a', status: 'passed', source: 'dotnet' },
    { title: '[AC-02] b', status: 'failed', source: 'e2e' },
    { title: '[AC-03] a', status: 'passed', source: 'e2e' },
    { title: '[AC-03] b', status: 'flaky', source: 'e2e' },
    { title: '[AC-04] a', status: 'skipped', source: 'e2e' },
    { title: '[AC-04] b', status: 'passed', source: 'node' },
    { title: '[AC-05] a', status: 'skipped', source: 'e2e' },
    { title: '[AC-49a] a', status: 'passed', source: 'dotnet' },
    { title: '[AC-49b] [AC-50] both', status: 'passed', source: 'dotnet' },
    { title: '[AC-77] outside', status: 'failed', source: 'dotnet' },
  ]);
  assert.equal(rows.length, 50);
  assert.deepEqual(rows.map((r) => r.id).slice(0, 2).concat(rows[49].id), ['AC-01', 'AC-02', 'AC-50']);
  const status = (id) => rows.find((r) => r.id === id).status;
  assert.equal(status('AC-01'), 'passed');
  assert.equal(status('AC-02'), 'failed');
  assert.equal(status('AC-03'), 'flaky');
  assert.equal(status('AC-04'), 'partial', 'a passed test next to a skipped one is partial, not passed (R3-05)');
  assert.equal(status('AC-05'), 'skipped', 'every test skipped: skipped');
  assert.equal(status('AC-06'), 'missing');
  assert.equal(status('AC-49'), 'passed');
  assert.equal(status('AC-50'), 'passed');
  assert.deepEqual(rows.find((r) => r.id === 'AC-04').skipped, ['e2e: [AC-04] a'], 'the skipped test is named');
  assert.deepEqual(rows.find((r) => r.id === 'AC-01').skipped, []);
  assert.equal(rows.find((r) => r.id === 'AC-02').found, 'dotnet 1, e2e 1');
  assert.deepEqual(rows.find((r) => r.id === 'AC-49').parts, ['49a', '49b']);
  assert.equal(rows.filter((r) => r.status === 'missing').length, 43);
  assert.deepEqual(buildAcMatrix([]).map((r) => r.status), Array(50).fill('missing'));
});

test('AC matrix: 50 rows built from the .trx, results.json and the js TAP; report-only, so the verdict does not move', () => {
  const run = summarize({
    files: {
      'dotnet/trx/results.trx': trxOf([
        ['Realm.Web.Tests.Zones.[AC-13a] zone fill', 'Passed'],
        ['Realm.Web.Tests.Zones.[AC-49a] pins', 'Passed'],
        ['Realm.Web.Tests.Zones.[AC-49b] pins again', 'Passed'],
        ['Realm.Web.Tests.Zones.[AC-30] cut', 'NotExecuted'],
        ['Realm.Web.Tests.Zones.untitled', 'Passed'],
      ]),
      'e2e/results.json': playwrightOf([
        { title: '[AC-01] opens', status: 'expected' },
        { title: '[AC-02] sheet', status: 'flaky' },
        { title: '[AC-13] zone fill, seen', status: 'expected' },
        { title: '[X-01] platform', status: 'expected' },
      ]),
      'js/js-tests.tap': 'ok 1 - [AC-40] the layout solver\nok 2 - plain test\n# pass 2\n# fail 0\n',
    },
    needs: e2eNeeds('success'),
  });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: success$/m, 'a flaky test and 44 missing criteria do not fail the run');
  const matrix = section(run.summary, 'Acceptance criteria');
  assert.ok(matrix, 'the section exists');
  assert.match(matrix, /Since S15 \(D50\)/);
  assert.match(matrix, /^50 criteria: 4 passed, 0 partial, 0 failed, 1 skipped, 1 flaky, 44 missing\.$/m);
  const rows = acRows(run.summary);
  assert.equal(rows.size, 50, 'one row per criterion');
  assert.deepEqual([...rows.keys()].slice(0, 3), ['AC-01', 'AC-02', 'AC-03']);
  assert.deepEqual(rows.get('AC-01'), { status: 'passed', found: 'e2e 1' });
  assert.deepEqual(rows.get('AC-02'), { status: 'flaky', found: 'e2e 1' });
  assert.deepEqual(rows.get('AC-13'), { status: 'passed', found: 'dotnet 1, e2e 1 (13a)' }, 'a dotnet [AC-13a] and an e2e [AC-13] meet in one row');
  assert.deepEqual(rows.get('AC-30'), { status: 'skipped', found: 'dotnet 1' });
  assert.deepEqual(rows.get('AC-40'), { status: 'passed', found: 'node 1' });
  assert.deepEqual(rows.get('AC-49'), { status: 'passed', found: 'dotnet 2 (49a, 49b)' });
  assert.deepEqual(rows.get('AC-50'), { status: 'missing', found: '—' });
  assert.ok(run.summary.indexOf('## Jobs') < run.summary.indexOf('## Acceptance criteria'), 'the matrix follows the job table');
});

test('AC matrix: a failed test turns its criterion red and nothing else; a run without any test source has no matrix', () => {
  const run = summarize({
    files: { 'dotnet/trx/r.trx': trxOf([['T.[AC-07] x', 'Failed'], ['T.[AC-08] y', 'Passed']]) },
    needs: NEEDS_OK,
  });
  const rows = acRows(run.summary);
  assert.equal(rows.get('AC-07').status, 'failed');
  assert.equal(rows.get('AC-08').status, 'passed');
  assert.match(run.summary, /^- result: failure$/m, 'the failed test fails the run through the existing rule, not through the matrix');
  assert.match(run.summary, /^50 criteria: 1 passed, 0 partial, 1 failed, 0 skipped, 0 flaky, 48 missing\.$/m);

  const none = summarize({ files: { 'dotnet/errors.log': '' }, needs: NEEDS_OK });
  assert.doesNotMatch(none.summary, /^## Acceptance criteria/m, 'no test source, no matrix');
  const noTests = summarize({ files: { 'dotnet/errors.log': '', 'dotnet/trx/r.trx': fixture('results.passed.trx') }, needs: NEEDS_OK });
  assert.equal(acRows(noTests.summary).size, 50, 'a .trx without tokens still gives the matrix, all missing');
  assert.match(noTests.summary, /^50 criteria: 0 passed, 0 partial, 0 failed, 0 skipped, 0 flaky, 50 missing\.$/m);
});

test('helpers: acStatus precedence is failed > flaky > partial > passed, a lone skipped is skipped, nothing is missing', () => {
  const status = (...list) => acStatus(new Set(list));
  assert.equal(status('failed', 'flaky', 'skipped', 'passed'), 'failed');
  assert.equal(status('flaky', 'skipped', 'passed'), 'flaky', 'flaky beats partial');
  assert.equal(status('flaky', 'skipped'), 'flaky');
  assert.equal(status('failed', 'skipped'), 'failed');
  assert.equal(status('skipped', 'passed'), 'partial');
  assert.equal(status('passed', 'skipped'), 'partial', 'the order of the tests does not matter');
  assert.equal(status('skipped'), 'skipped');
  assert.equal(status('passed'), 'passed');
  assert.equal(status(), 'missing');
});

test('AC matrix: a passing test next to a skipped or fixme\'d one is partial, in the table, in the totals and with the skipped tests named; [X-13] is not a criterion', () => {
  const run = summarize({
    files: {
      'dotnet/trx/r.trx': trxOf([
        ['Realm.Web.Tests.Driving.[AC-32] gear', 'Passed'],
        ['Realm.Web.Tests.Driving.[AC-32] settings opens', 'NotExecuted'],
        ['Realm.Web.Tests.Shell.[AC-03] gear', 'Passed'],
      ]),
      'e2e/results.json': playwrightOf([
        { title: '[AC-38] popup opens', status: 'expected' },
        { title: '[AC-38] Back closes the popup', status: 'skipped' },
        { title: '[AC-15] header of Dara', status: 'skipped' },
        { title: '[AC-15] header of the King', status: 'expected' },
        { title: '[AC-15] header of the King', status: 'expected', project: 'unfolded' },
        { title: '[AC-20] flaky next to skipped', status: 'flaky' },
        { title: '[AC-20] skipped', status: 'skipped' },
        { title: '[AC-21] failed next to skipped', status: 'unexpected' },
        { title: '[AC-21] skipped', status: 'skipped' },
        { title: '[AC-22] only skipped', status: 'skipped' },
        { title: '[X-13] the focus trap, a declared expected failure', status: 'expected', expectedStatus: 'failed', attempts: ['failed'] },
      ]),
    },
    needs: e2eNeeds('success'),
  });
  assert.equal(run.status, 0, run.stderr);
  const rows = acRows(run.summary);
  assert.equal(rows.get('AC-32').status, 'partial');
  assert.equal(rows.get('AC-38').status, 'partial');
  assert.equal(rows.get('AC-15').status, 'partial');
  assert.equal(rows.get('AC-03').status, 'passed', 'a criterion with no skipped test is still passed');
  assert.equal(rows.get('AC-20').status, 'flaky');
  assert.equal(rows.get('AC-21').status, 'failed');
  assert.equal(rows.get('AC-22').status, 'skipped');
  assert.deepEqual(rows.get('AC-38'), { status: 'partial', found: 'e2e 2' });
  const matrix = section(run.summary, 'Acceptance criteria');
  assert.match(matrix, /^50 criteria: 1 passed, 3 partial, 1 failed, 1 skipped, 1 flaky, 43 missing\.$/m);
  assert.match(matrix, /failed beats flaky beats partial beats passed/);
  assert.match(matrix, /`partial` means a test of the criterion passed and another was skipped, fixme'd or expected to fail/);
  assert.doesNotMatch(matrix, /beats passed beats skipped/, 'the old precedence is gone from the explanation');
  const listed = matrix.split('Skipped tests of the partial criteria (3):\n\n')[1];
  assert.ok(listed, 'the skipped tests of the partial criteria are listed');
  assert.deepEqual(listed.trimEnd().split('\n'), [
    '- AC-15, skipped: e2e: demo group › [AC-15] header of Dara',
    '- AC-32, skipped: dotnet: Realm.Web.Tests.Driving.[AC-32] settings opens',
    '- AC-38, skipped: e2e: demo group › [AC-38] Back closes the popup',
  ]);
  assert.ok(!matrix.includes('X-13'), '[X-13] is not a criterion and does not appear');
  assert.match(run.summary, /^- why: 1 failed E2E test\(s\)$/m, 'only the failed test fails the run (AC-21); the partial criteria add nothing to the verdict');
});

test('AC matrix: partial criteria alone do not fail the run (report-only until S15)', () => {
  const run = summarize({
    files: { 'e2e/results.json': playwrightOf([{ title: '[AC-38] opens', status: 'expected' }, { title: '[AC-38] Back closes', status: 'skipped' }]) },
    needs: e2eNeeds('success'),
  });
  assert.match(run.summary, /^- result: success$/m);
  assert.equal(acRows(run.summary).get('AC-38').status, 'partial');
  assert.match(run.summary, /^50 criteria: 0 passed, 1 partial, 0 failed, 0 skipped, 0 flaky, 49 missing\.$/m);
});

test('AC matrix: [X-13] (test.fail by design) is not an AC id, so it changes no row; a Playwright test.fail with an AC id would read as skipped', () => {
  const noAc = summarize({
    files: { 'e2e/results.json': playwrightOf([{ title: '[AC-01] fine', status: 'expected' }, { title: '[X-13] the focus trap', status: 'expected', expectedStatus: 'failed', attempts: ['failed'] }]) },
    needs: e2eNeeds('success'),
  });
  assert.match(noAc.summary, /^50 criteria: 1 passed, 0 partial, 0 failed, 0 skipped, 0 flaky, 49 missing\.$/m);
  assert.equal(section(noAc.summary, 'Acceptance criteria').includes('Skipped tests of the partial criteria'), false);
  const withAc = buildAcMatrix([
    { title: '[AC-01] a', status: 'passed', source: 'e2e' },
    { title: '[AC-01] b (test.fail)', status: 'skipped', source: 'e2e' },
  ]);
  assert.equal(withAc[0].status, 'partial');
});

test('AC matrix: the grouped ac-coverage guards line is kept as it was', () => {
  const run = summarize({
    files: { 'guards/guards.log': ['PASS config-name', ...AC_LINES, ''].join('\n'), 'dotnet/trx/r.trx': trxOf([['T.[AC-01] x', 'Passed']]) },
    needs: guardsNeeds('success', 'success'),
  });
  assert.ok(section(run.summary, 'Guards').includes('REPORT ac-coverage: 50 AC ids missing (AC-01 … AC-50)\n'));
  assert.equal(acRows(run.summary).get('AC-01').status, 'passed');
});

test('e2e: a clean run is summarised in one section, the matrix counts its tests, and results.json is published beside SUMMARY.md', () => {
  const results = playwrightOf([
    { title: '[X-01] platform', status: 'expected' },
    { title: '[X-01] platform', status: 'expected', project: 'unfolded' },
  ]);
  const run = summarize({
    files: { 'e2e/e2e/results.json': results, 'e2e/e2e/contract.tap': 'ok 1 - payload shape\n# pass 1\n# fail 0\n', 'e2e/e2e/app.log': 'info: started\nlistening\n' },
    needs: e2eNeeds('success'),
  });
  assert.match(run.summary, /^- result: success$/m);
  const e2e = section(run.summary, 'E2E');
  assert.match(e2e, /2 passed, 0 failed, 0 flaky, 0 skipped \(2 test runs in 2 projects\)\./);
  assert.match(e2e, /Payload contract \(node --test of tests\/contract\): 1 passed, 0 failed\./);
  assert.doesNotMatch(run.summary, /^## App log/m, 'the app log is shown only when something failed');
  assert.doesNotMatch(run.summary, /^## Flaky tests/m);
  assert.equal(fs.readFileSync(path.join(run.outDir, 'e2e', 'results.json'), 'utf8'), results);
  assert.equal(fs.readFileSync(path.join(run.outDir, 'e2e', 'contract.tap'), 'utf8'), 'ok 1 - payload shape\n# pass 1\n# fail 0\n');
  assert.equal(fs.readFileSync(path.join(run.outDir, 'e2e', 'app.log'), 'utf8'), 'info: started\nlistening\n');
});

test('e2e: a failed test fails the run, with its message, its criteria and the app log; the job is not listed a second time', () => {
  const run = summarize({
    files: {
      'e2e/e2e/results.json': playwrightOf([
        { title: '[AC-05] [AC-06] pins fan', status: 'unexpected', project: 'phone-short', error: 'Error: expect(received).toBe(expected)\n\nExpected: 3\nReceived: 2' },
        { title: '[X-01] fine', status: 'expected' },
      ]),
      'e2e/e2e/app.log': Array.from({ length: 50 }, (_, i) => `app line ${i + 1}`).join('\n') + '\n',
    },
    needs: e2eNeeds('failure'),
  });
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: 1 failed E2E test\(s\)$/m, 'the failure is named once; "job e2e: failure" is not added');
  const failed = section(run.summary, 'Failed E2E tests \\(1\\)');
  assert.ok(failed, 'a failed-tests section exists');
  assert.match(failed, /^### \[phone-short\] specs\/demo\.spec\.ts › demo group › \[AC-05\] \[AC-06\] pins fan$/m);
  assert.match(failed, /Acceptance criteria: AC-05, AC-06; at demo\.spec\.ts:12/);
  assert.match(failed, /Expected: 3\nReceived: 2/);
  const log = section(run.summary, 'App log \\(last 30 lines of e2e/app\\.log\\)');
  assert.ok(log.includes('app line 50') && log.includes('app line 21') && !log.includes('app line 20\n'), 'the last 30 lines');
  assert.doesNotMatch(run.summary, /^## Notes/m);
  const rows = acRows(run.summary);
  assert.equal(rows.get('AC-05').status, 'failed');
  assert.equal(rows.get('AC-06').status, 'failed');
});

test('e2e: a flaky test is listed, reported in the matrix, and does not fail the run', () => {
  const run = summarize({
    files: { 'e2e/e2e/results.json': playwrightOf([{ title: '[AC-12] sheet snaps', status: 'flaky', project: 'unfolded' }]) },
    needs: e2eNeeds('success'),
  });
  assert.match(run.summary, /^- result: success$/m);
  assert.match(section(run.summary, 'E2E'), /0 passed, 0 failed, 1 flaky, 0 skipped/);
  const flaky = section(run.summary, 'Flaky tests \\(1, passed on retry\\)');
  assert.ok(flaky, 'a flaky section exists');
  assert.match(flaky, /^\[unfolded\] demo group › \[AC-12\] sheet snaps$/m);
  assert.equal(acRows(run.summary).get('AC-12').status, 'flaky');
});

test('e2e: no results.json after a failed job is explained; after a successful job it is a failure; a skipped job says nothing', () => {
  const failedJob = summarize({ files: { 'e2e/e2e/app.log': 'Unhandled exception. boom\n' }, needs: e2eNeeds('failure') });
  assert.match(failedJob.summary, /^- result: failure$/m);
  assert.match(failedJob.summary, /^- why: job e2e: failure$/m);
  assert.match(section(failedJob.summary, 'E2E'), /No e2e\/results\.json was found: Playwright did not get as far as writing its report .* The e2e job result is failure\./);
  assert.match(section(failedJob.summary, 'App log \\(last 30 lines of e2e/app\\.log\\)'), /Unhandled exception\. boom/, 'the app log explains a start-up failure');

  const lying = summarize({ files: { 'dotnet/errors.log': '' }, needs: e2eNeeds('success') });
  assert.match(lying.summary, /^- why: job e2e succeeded but left no e2e\/results\.json$/m);
  assert.match(lying.summary, /^- result: failure$/m);

  const skipped = summarize({ files: { 'dotnet/errors.log': '' }, needs: JSON.stringify({ dotnet: { result: 'success', outputs: {} }, e2e: { result: 'skipped', outputs: {} } }) });
  assert.doesNotMatch(skipped.summary, /^## E2E/m);
  assert.doesNotMatch(skipped.summary, /e2e succeeded but left no/);
});

test('e2e: an unreadable results.json and a report without a test are failures, never silent', () => {
  const broken = summarize({ files: { 'e2e/e2e/results.json': '{ not json' }, needs: e2eNeeds('failure') });
  assert.match(broken.summary, /^- why: e2e: results\.json is not valid JSON/m);
  assert.match(section(broken.summary, 'E2E'), /results\.json is not valid JSON/);
  assert.equal(acRows(broken.summary).size, 50, 'the matrix is still written, every criterion missing');

  const empty = summarize({ files: { 'e2e/e2e/results.json': playwrightOf([]) }, needs: e2eNeeds('success') });
  assert.match(empty.summary, /^- why: e2e: results\.json holds no test$/m);

  const outside = summarize({ files: { 'e2e/e2e/results.json': playwrightOf([], [{ message: 'Error: Timed out waiting 120000ms from config.webServer.' }]) }, needs: e2eNeeds('failure') });
  assert.match(outside.summary, /^- why: Playwright reported an error outside the tests$/m);
  assert.match(section(outside.summary, 'Playwright errors outside any test'), /Timed out waiting 120000ms/);
});

test('e2e: a failing payload contract fails the run and its TAP detail is shown', () => {
  const tap = ['not ok 1 - members.json has the camelCase shape', '  ---', '  error: \'Expected values to be strictly equal\'', '  expected: true', '  actual: false', '  ...', 'ok 2 - zones.json', '# pass 1', '# fail 1', ''].join('\n');
  const run = summarize({
    files: { 'e2e/e2e/results.json': playwrightOf([{ title: '[X-01] fine', status: 'expected' }]), 'e2e/e2e/contract.tap': tap },
    needs: e2eNeeds('failure'),
  });
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: payload contract: 1 failed$/m);
  assert.match(section(run.summary, 'E2E'), /Payload contract \(node --test of tests\/contract\): 1 passed, 1 failed\./);
  const detail = section(run.summary, 'Payload contract failures');
  assert.match(detail, /### members\.json has the camelCase shape/);
  assert.match(detail, /expected: true\n\s*actual: false/);
});

test('screenshots: the gallery under shots/ and the first 20 failure screenshots are copied and indexed with their sha256', () => {
  const png = (seed) => `\u0089PNG-${seed}`;
  const failing = Array.from({ length: 22 }, (_, i) => ({
    title: `[X-${String(i + 1).padStart(2, '0')}] t${i + 1}`,
    status: 'unexpected',
    attachments: [{ name: 'screenshot', contentType: 'image/png', path: `/home/runner/work/ha-cartographer/ha-cartographer/ci-out/e2e/artifacts/t${i + 1}-phone/test-failed-1.png` }],
  }));
  const files = {
    'e2e/e2e/results.json': playwrightOf(failing),
    'e2e/shots/phone/location-sheet-peek.png': png('a'),
    'e2e/shots/unfolded/location-panel.png': png('b'),
    'e2e/e2e/readme.txt': 'not a png',
  };
  failing.forEach((_, i) => {
    files[`e2e/e2e/artifacts/t${i + 1}-phone/test-failed-1.png`] = png(`f${i + 1}`);
  });
  const run = summarize({ files, needs: e2eNeeds('failure') });
  assert.equal(fs.readFileSync(path.join(run.outDir, 'shots', 'phone', 'location-sheet-peek.png'), 'utf8'), png('a'));
  assert.equal(fs.readFileSync(path.join(run.outDir, 'shots', 'unfolded', 'location-panel.png'), 'utf8'), png('b'));
  const copied = fs.readdirSync(path.join(run.outDir, 'failures')).sort();
  assert.equal(copied.length, 20, 'at most 20 failing-test screenshots');
  assert.ok(copied.includes('x-01.phone.png') && copied.includes('x-20.phone.png') && !copied.includes('x-21.phone.png'));
  assert.equal(fs.readFileSync(path.join(run.outDir, 'failures', 'x-01.phone.png'), 'utf8'), png('f1'));
  assert.ok(!fs.existsSync(path.join(run.outDir, 'e2e', 'readme.txt')), 'only the named files are copied');
  const index = section(run.summary, 'Screenshots \\(22\\)');
  assert.ok(index, 'the index counts the gallery and the failure screenshots');
  const digest = (text) => crypto.createHash('sha256').update(text).digest('hex').slice(0, 12);
  assert.ok(index.includes(`shots/phone/location-sheet-peek.png  sha256:${digest(png('a'))}`));
  assert.ok(index.includes(`failures/x-02.phone.png  sha256:${digest(png('f2'))}`));
});

test('helpers: screenshotCopies ignores PNGs outside a shots folder and numbers two failures of one test and project', () => {
  const copies = screenshotCopies(['/a/b/other/x.png', '/a/e2e/shots/phone/a.png', '/a/shots'], null);
  assert.deepEqual(copies, [{ from: '/a/e2e/shots/phone/a.png', to: 'shots/phone/a.png' }]);
  const results = parsePlaywright(
    playwrightOf([
      { title: '[X-01] t', status: 'unexpected', attachments: [{ contentType: 'image/png', path: '/r/ci-out/e2e/artifacts/one/test-failed-1.png' }] },
      { title: '[X-01] t', status: 'unexpected', attachments: [{ contentType: 'image/png', path: '/r/ci-out/e2e/artifacts/two/test-failed-1.png' }] },
    ]),
  );
  const both = screenshotCopies(['/in/e2e/artifacts/one/test-failed-1.png', '/in/e2e/artifacts/two/test-failed-1.png'], results);
  assert.deepEqual(both.map((c) => c.to), ['failures/x-01.phone.png', 'failures/x-01.phone-2.png']);
  assert.equal(screenshotCopies(['/in/elsewhere/test-failed-1.png'], results).length, 0, 'a screenshot the report names but the artifact lacks is skipped');
});

test('helpers: e2eReport with nothing to report is empty', () => {
  assert.deepEqual(e2eReport({ results: null, contract: null, appLog: null, jobResult: undefined }), { sections: [], problems: [], flaky: [] });
  assert.deepEqual(e2eReport({ results: null, contract: null, appLog: null, jobResult: 'skipped' }), { sections: [], problems: [], flaky: [] });
});

// ---------------------------------------------------------------------------------------------
// FX4: the js job (js-tests.tap and tsc.log) in SUMMARY.md
// ---------------------------------------------------------------------------------------------

const jsNeeds = (js, dotnet = 'success') => JSON.stringify({ guards: { result: 'success', outputs: {} }, dotnet: { result: dotnet, outputs: {} }, js: { result: js, outputs: {} } });
const PASSING_TAP = 'TAP version 13\nok 1 - a\n1..1\n# tests 12\n# pass 12\n# fail 0\n';
const TSC_CLEAN = '\n> realm-dev-tools@ typecheck\n> tsc -p tsconfig.json --noEmit\n\n';

test('helpers: parseTap names a nested failure by its describe chain, lists the cause and not the parents that only roll it up, and keeps a crashed file with its crash text', () => {
  const parsed = parseTap(fixture('js-tests.failed.tap'), { detailLines: 40 });
  assert.deepEqual(
    parsed.failures.map((f) => f.title),
    [
      'tests/js/brokenImport.test.mjs',
      'layout solver › slides a bubble along the edge',
      'layout solver › clusters › merges close bubbles',
      'shell › closes the sheet',
    ],
  );
  assert.ok(parsed.failures.every((f) => f.cancelled === false));
  assert.equal(parsed.pass, 3);
  assert.equal(parsed.fail, 5, "node's counter includes the parents of failing subtests; the failures do not");
  // `tests` keeps every line, each with its own title: the AC matrix reads the titles from it.
  assert.deepEqual(
    parsed.tests.filter((t) => t.status === 'failed').map((t) => t.title),
    ['tests/js/brokenImport.test.mjs', 'slides a bubble along the edge', 'merges close bubbles', 'clusters', 'layout solver', 'closes the sheet', 'shell'],
  );
  const [file, slides, merges] = parsed.failures;
  assert.match(file.detail, /^node:internal\/modules\/esm\/resolve:275\n/, 'the lines node printed before the file entry come first');
  assert.match(file.detail, /Cannot find module '\/home\/runner\/work\/ha-cartographer\/ha-cartographer\/tests\/js\/helpers\/missing\.mjs'/);
  assert.match(file.detail, /\n {2}exitCode: 1\n/);
  assert.match(slides.detail, /^ {2}duration_ms: 1\.5\n {2}type: 'test'/, 'the YAML block of a nested test is shown at the indent of a top-level one');
  assert.match(slides.detail, / {4}\+ {3}y: 2\n {4}- {3}y: 3/);
  assert.doesNotMatch(slides.detail, /^\s*\.\.\.\s*$/m);
  assert.match(merges.detail, /error: 'cluster size was 1, expected 2'/);
  assert.doesNotMatch(slides.detail, /more lines/, 'under 40 lines: nothing is cut');
  assert.match(parseTap(fixture('js-tests.failed.tap')).failures[1].detail, /\n\.\.\. \(\d+ more lines\)$/, 'the default is 15 lines');
});

test('helpers: parseTap lists a suite whose hook threw although node counts "fail 0", and flags the tests cancelled behind it', () => {
  const tap = [
    'TAP version 13',
    '# Subtest: with hook',
    '    # Subtest: first',
    '    not ok 1 - first',
    '      ---',
    "      failureType: 'cancelledByParent'",
    "      error: 'test did not finish before its parent and was cancelled'",
    '      ...',
    '    1..1',
    'not ok 1 - with hook',
    '  ---',
    "  type: 'suite'",
    "  failureType: 'hookFailed'",
    "  error: 'hook exploded'",
    '  ...',
    '1..1',
    '# tests 1',
    '# suites 1',
    '# pass 0',
    '# fail 0',
    '# cancelled 1',
    '',
  ].join('\n');
  const parsed = parseTap(tap);
  assert.deepEqual(parsed.failures.map((f) => [f.title, f.cancelled]), [['with hook › first', true], ['with hook', false]]);
  assert.equal(parsed.fail, 0);
  assert.match(parsed.failures[1].detail, /error: 'hook exploded'/);
});

test('helpers: parseTap does not read the lines of a YAML block as tests, and ends a block that is not closed at the next entry', () => {
  const tap = [
    'not ok 1 - diff',
    '  ---',
    '  error: |-',
    '    ok 2 - a line of a diff that looks like a test',
    '    not ok 3 - another one',
    '  ...',
    'ok 2 - next',
    'not ok 3 - unclosed',
    '  ---',
    '  error: boom',
    'ok 4 - after',
    '# pass 2',
    '# fail 2',
    '',
  ].join('\n');
  const parsed = parseTap(tap);
  assert.deepEqual(parsed.tests, [
    { title: 'diff', status: 'failed' },
    { title: 'next', status: 'passed' },
    { title: 'unclosed', status: 'failed' },
    { title: 'after', status: 'passed' },
  ]);
  assert.match(parsed.failures[0].detail, /ok 2 - a line of a diff that looks like a test\n {4}not ok 3 - another one/);
  assert.equal(parsed.failures[1].detail, '  error: boom');
});

test('helpers: parseTsc keeps the distinct "error TS" lines, without continuation lines and without the runner path', () => {
  assert.deepEqual(parseTsc(fixture('tsc.failed.log'), WORKSPACE), [
    "src/Realm.Web/wwwroot/js/realmShell.js(306,16): error TS2304: Cannot find name 'document'.",
    "src/Realm.Web/wwwroot/js/layoutMath.js(12,5): error TS2322: Type 'string' is not assignable to type 'number'.",
    "src/Realm.Web/wwwroot/js/geo.js(40,9): error TS2339: Property 'lng' does not exist on type 'Point'.",
  ]);
  assert.deepEqual(parseTsc(TSC_CLEAN, WORKSPACE), []);
  assert.deepEqual(parseTsc('error TS18003: No inputs were found in config file.\nFound 1 error.\n', ''), ['error TS18003: No inputs were found in config file.']);
});

test('helpers: jsReport has nothing to say without a TAP, a tsc.log or a js job that ran', () => {
  assert.deepEqual(jsReport({ tap: null, tsc: null, jobResult: undefined }), { sections: [], problems: [] });
  assert.deepEqual(jsReport({ tap: null, tsc: null, jobResult: 'skipped' }), { sections: [], problems: [] });
  assert.deepEqual(jsReport({ tap: null, tsc: null, jobResult: 'success' }).sections, ['## JS\n\nNo js-tests.tap was found.']);
});

test('js: failing Node tests are named in SUMMARY.md: counts, one block per failure under its describe, the why line, and the order of the sections', () => {
  const run = summarize({
    files: {
      'dotnet/trx/r.trx': fixture('results.passed.trx'),
      'js/js-tests.tap': fixture('js-tests.failed.tap'),
      'js/tsc.log': TSC_CLEAN,
      'e2e/e2e/results.json': playwrightOf([{ title: '[X-01] fine', status: 'expected' }]),
    },
    needs: jsNeeds('failure'),
  });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: job js: failure; 4 failed JS test\(s\)$/m);
  const js = section(run.summary, 'JS');
  assert.match(js, /^Node tests \(node --test of tests\/js\): 3 passed, 4 failed\.$/m, 'the counts are those of the tests, not of the parents');
  assert.match(js, /^Type check \(tsc\.log\): no "error TS" line\.$/m);
  assert.doesNotMatch(js, /The js job failed/, 'a failing test explains the failed job');
  const failed = section(run.summary, 'Failed JS tests \\(4\\)');
  assert.ok(failed, 'the failures have their own section');
  assert.deepEqual(
    [...failed.matchAll(/^### (.*)$/gm)].map((m) => m[1]),
    [
      'tests/js/brokenImport.test.mjs',
      'layout solver › slides a bubble along the edge',
      'layout solver › clusters › merges close bubbles',
      'shell › closes the sheet',
    ],
  );
  assert.doesNotMatch(run.summary, /^### (layout solver|clusters|shell)$/m, 'the parents of a failing subtest are not listed');
  assert.match(failed, /Cannot find module 'tests\/js\/helpers\/missing\.mjs' imported from tests\/js\/brokenImport\.test\.mjs/, 'the crash of a test file is shown');
  assert.match(failed, /location: 'tests\/js\/layoutSolver\.test\.mjs:6:3'/);
  assert.match(failed, /error: 'sheet still open'/);
  assert.ok(!run.summary.includes(WORKSPACE), 'the runner path is stripped');
  assert.doesNotMatch(run.summary, /did not succeed, yet no compiler error/, 'the generic note is for jobs the report cannot explain');
  assert.equal(section(run.summary, 'Type errors'), null);
  const at = (heading) => run.summary.indexOf(`\n## ${heading}`);
  assert.ok(at('Tests') > 0 && at('Tests') < at('JS') && at('JS') < at('Failed JS tests (4)') && at('Failed JS tests (4)') < at('E2E') && at('E2E') < at('Acceptance criteria'), 'JS follows Tests and comes before E2E');
});

test('js: a job that failed next to a failing js job is still reported as unexplained, the js job is not named in the note', () => {
  const run = summarize({ files: { 'js/js-tests.tap': fixture('js-tests.failed.tap') }, needs: jsNeeds('failure', 'failure') });
  assert.match(run.summary, /^- why: job dotnet: failure; job js: failure; 4 failed JS test\(s\)$/m);
  assert.match(run.summary, /^Job dotnet did not succeed, yet no compiler error/m);
});

test('js: the detail of a failing test is its first 40 lines', () => {
  const tap = ['not ok 1 - long', '  ---', '  error: |-', ...Array.from({ length: 60 }, (_, i) => `    diff line ${i + 1}`), '  ...', '# pass 0', '# fail 1', ''].join('\n');
  const run = summarize({ files: { 'js/js-tests.tap': tap }, needs: jsNeeds('failure') });
  const lines = section(run.summary, 'Failed JS tests \\(1\\)').split('\n');
  assert.ok(lines.includes('    diff line 39'), 'line 40 is the 39th diff line (the first line is "error: |-")');
  assert.ok(!lines.includes('    diff line 40'));
  assert.ok(lines.includes('... (21 more lines)'));
});

test('js: more than 50 failing tests: the first 50 are shown, the count is the whole, causes come before the tests cancelled behind them', () => {
  const cancelled = Array.from({ length: 60 }, (_, i) => [`    not ok ${i + 1} - t${i + 1}`, '      ---', "      failureType: 'cancelledByParent'", '      ...']).flat();
  const tap = ['# Subtest: suite', ...cancelled, 'not ok 1 - suite', '  ---', "  failureType: 'hookFailed'", "  error: 'hook exploded'", '  ...', '# pass 0', '# fail 0', ''].join('\n');
  const run = summarize({ files: { 'js/js-tests.tap': tap }, needs: jsNeeds('failure') });
  assert.match(run.summary, /^- why: job js: failure; 61 failed JS test\(s\)$/m);
  const failed = section(run.summary, 'Failed JS tests \\(61\\)');
  const titles = [...failed.matchAll(/^### (.*)$/gm)].map((m) => m[1]);
  assert.equal(titles.length, 50);
  assert.equal(titles[0], 'suite', 'the hook that threw is the first block');
  assert.equal(titles[1], 'suite › t1');
  assert.match(failed, /^\.\.\. and 11 more failed JS tests \(all of them are in js\/js-tests\.tap\)$/m);
});

test('js: type errors of tsc.log are listed once each and counted in the why line; with them, a js job without a TAP is explained', () => {
  const run = summarize({ files: { 'js/tsc.log': fixture('tsc.failed.log') }, needs: jsNeeds('failure') });
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: job js: failure; 3 type error\(s\)$/m);
  const js = section(run.summary, 'JS');
  assert.match(js, /^Type check \(tsc\.log\): 3 distinct type errors\.$/m);
  assert.match(js, /^The js job failed and no js-tests\.tap was produced: the Node tests run after the type check, which failed/m);
  const errors = section(run.summary, 'Type errors');
  assert.ok(errors, 'the type errors have their own section');
  const lines = errors.split('\n');
  assert.deepEqual(lines.filter((l) => l.includes('error TS')), [
    "src/Realm.Web/wwwroot/js/realmShell.js(306,16): error TS2304: Cannot find name 'document'.",
    "src/Realm.Web/wwwroot/js/layoutMath.js(12,5): error TS2322: Type 'string' is not assignable to type 'number'.",
    "src/Realm.Web/wwwroot/js/geo.js(40,9): error TS2339: Property 'lng' does not exist on type 'Point'.",
  ]);
  assert.ok(!lines.includes("  Type 'string' is not assignable to type 'number'."), 'a continuation line is not an error of its own');
  assert.ok(!errors.includes(WORKSPACE), 'the runner path is stripped');
  assert.doesNotMatch(run.summary, /did not succeed, yet no compiler error/, 'the type errors explain the failed job');
});

test('js: at most 50 type errors are shown; the why line counts all of them', () => {
  const log = Array.from({ length: 60 }, (_, i) => `src/a.js(${i + 1},1): error TS2304: Cannot find name 'n${i + 1}'.`).join('\n') + '\n';
  const run = summarize({ files: { 'js/tsc.log': log }, needs: jsNeeds('failure') });
  assert.match(run.summary, /^- why: job js: failure; 60 type error\(s\)$/m);
  const errors = section(run.summary, 'Type errors');
  assert.equal(errors.split('\n').filter((l) => l.includes('error TS')).length, 50);
  assert.match(errors, /^\.\.\. and 10 more distinct type errors \(all of them are in js\/tsc\.log\)$/m);
});

test('js: a failed js job with no failing test and no type error says so, and the generic note stays', () => {
  const withTap = summarize({ files: { 'js/js-tests.tap': PASSING_TAP, 'js/tsc.log': TSC_CLEAN }, needs: jsNeeds('failure') });
  assert.match(withTap.summary, /^- why: job js: failure$/m);
  assert.match(section(withTap.summary, 'JS'), /^Node tests \(node --test of tests\/js\): 12 passed, 0 failed\.$/m);
  assert.match(section(withTap.summary, 'JS'), /^The js job failed but js-tests\.tap has no failing test; see tsc\.log\.$/m);
  assert.match(withTap.summary, /^Job js did not succeed, yet no compiler error/m);

  const noTap = summarize({ files: { 'dotnet/errors.log': '' }, needs: jsNeeds('failure') });
  assert.match(noTap.summary, /^- why: job js: failure$/m);
  assert.match(section(noTap.summary, 'JS'), /^The js job failed but no js-tests\.tap was produced \(the job stopped before the Node tests ran\); there is no tsc\.log either, so the cause is in the raw job log of the workflow run\.$/m);

  const tscCrash = summarize({ files: { 'js/tsc.log': '> realm-dev-tools@ typecheck\nnpm error Missing script: "typecheck"\n' }, needs: jsNeeds('failure') });
  assert.match(tscCrash.summary, /^- why: job js: failure$/m);
  assert.match(section(tscCrash.summary, 'JS'), /^The js job failed but no js-tests\.tap was produced \(the job stopped before the Node tests ran\); see tsc\.log\.$/m);
  assert.match(section(tscCrash.summary, 'JS'), /Last lines of tsc\.log:\n\n```text\n> realm-dev-tools@ typecheck\nnpm error Missing script: "typecheck"\n```/);

  const empty = summarize({ files: { 'js/js-tests.tap': '' }, needs: jsNeeds('failure') });
  assert.match(section(empty.summary, 'JS'), /^js-tests\.tap holds no test result/m);
  assert.match(section(empty.summary, 'JS'), /^The js job failed but js-tests\.tap has no failing test/m);
});

test('js: a clean js job: counts, no failure section, success', () => {
  const run = summarize({ files: { 'js/js-tests.tap': PASSING_TAP, 'js/tsc.log': TSC_CLEAN }, needs: jsNeeds('success') });
  assert.match(run.summary, /^- result: success$/m);
  assert.equal(section(run.summary, 'JS').trimEnd(), '## JS\n\nNode tests (node --test of tests/js): 12 passed, 0 failed.\n\nType check (tsc.log): no "error TS" line.');
  assert.equal(section(run.summary, 'Failed JS tests'), null);
  assert.equal(section(run.summary, 'Type errors'), null);
});

test('js: without --needs a failing test or a type error still fails the run; a TAP that counts failures but lists none is a failure with a note', () => {
  const tests = summarize({ files: { 'js/js-tests.tap': 'not ok 1 - x\n# pass 0\n# fail 1\n' } });
  assert.match(tests.summary, /^- result: failure$/m);
  assert.match(tests.summary, /^- why: 1 failed JS test\(s\)$/m);
  const types = summarize({ files: { 'js/tsc.log': 'src/a.js(1,1): error TS2304: Cannot find name \'n\'.\n' } });
  assert.match(types.summary, /^- why: 1 type error\(s\)$/m);
  const counted = summarize({ files: { 'js/js-tests.tap': '# pass 3\n# fail 2\n' }, needs: jsNeeds('success') });
  assert.match(counted.summary, /^- why: 2 failed JS test\(s\)$/m);
  assert.match(section(counted.summary, 'JS'), /^Node tests \(node --test of tests\/js\): 3 passed, 2 failed\.$/m);
  assert.match(section(counted.summary, 'JS'), /counts failed tests but lists none of them as "not ok"/);
  assert.equal(section(counted.summary, 'Failed JS tests'), null);
});

test('js: a section appears only when there is a TAP, a tsc.log or a js job that ran', () => {
  assert.equal(section(summarize({ files: { 'dotnet/errors.log': '' }, needs: NEEDS_OK }).summary, 'JS'), null, 'no js job, no files');
  assert.equal(section(summarize({ files: { 'dotnet/errors.log': '' }, needs: jsNeeds('skipped') }).summary, 'JS'), null, 'a skipped js job');
  const succeeded = summarize({ files: { 'dotnet/errors.log': '' }, needs: jsNeeds('success') });
  assert.equal(section(succeeded.summary, 'JS').trimEnd(), '## JS\n\nNo js-tests.tap was found.');
  assert.match(succeeded.summary, /^- result: success$/m, 'that alone does not fail the run');
});

test('js: js-tests.tap and tsc.log are published beside SUMMARY.md, and a file over 400 KB is cut to its last part', () => {
  const small = fixture('tsc.failed.log');
  const lines = Array.from({ length: 30000 }, (_, i) => `line ${i} ${'x'.repeat(20)}`);
  const big = lines.join('\n') + '\n';
  const run = summarize({ files: { 'js/js-tests.tap': big, 'js/tsc.log': small }, needs: jsNeeds('success') });
  assert.equal(fs.readFileSync(path.join(run.outDir, 'js', 'tsc.log'), 'utf8'), small, 'a small file is copied as it is');
  const kept = fs.readFileSync(path.join(run.outDir, 'js', 'js-tests.tap'));
  assert.ok(Buffer.byteLength(big) > 800 * 1024);
  assert.ok(kept.length <= 400 * 1024 && kept.length > 390 * 1024, `kept ${kept.length} bytes`);
  const text = kept.toString('utf8');
  const [note, first] = text.split('\n');
  assert.match(note, /^\.\.\. make-summary\.mjs kept the last part of this file \(\d+ bytes in the artifact, at most 409600 bytes here\)$/);
  assert.match(first, /^line \d+ x{20}$/, 'the kept part starts at a line');
  assert.ok(text.endsWith(`${lines[lines.length - 1]}\n`), 'the end of the file is kept');

  const none = summarize({ files: { 'dotnet/errors.log': '' }, needs: NEEDS_OK });
  assert.ok(!fs.existsSync(path.join(none.outDir, 'js')), 'nothing is published for a run without js files');
});

test('helpers: keepTail returns null for a file that fits and never cuts a character in two', () => {
  const dir = tempDir('keep-tail');
  const file = path.join(dir, 'a.log');
  writeFile(file, 'é'.repeat(200)); // 400 bytes, no line break
  assert.equal(keepTail(file, 400), null);
  for (const limit of [296, 297, 298, 299, 300, 301, 302, 303]) {
    const kept = keepTail(file, limit);
    assert.ok(!kept.includes('�'), `limit ${limit}: no broken character`);
    assert.ok(Buffer.byteLength(kept) <= limit, `limit ${limit}: ${Buffer.byteLength(kept)} bytes`);
  }
});

// ---------------------------------------------------------------------------------------------
// FX5 (R3-02): styles.log of the js job in SUMMARY.md
// ---------------------------------------------------------------------------------------------

test('helpers: parseStylesLog reads PASS, WARN and FAIL lines with their style id, and keeps a crash trace apart', () => {
  const parsed = parseStylesLog(fixture('styles.failed.log'));
  assert.deepEqual(parsed.fails.map((f) => [f.id, f.detail]), [
    ['demo-offline', 'base style: layers[2].paint.line-width: number expected, string found'],
    ['demo-offline', 'with overlay: layers[2].paint.line-width: number expected, string found'],
  ]);
  assert.deepEqual(parsed.warns.map((w) => w.id), ['night', 'day', 'streets']);
  assert.equal(parsed.warns[0].detail, 'https://tiles.openfreemap.org/styles/dark answered 403');
  assert.deepEqual(parsed.passes.map((p) => p.id), ['satellite']);
  assert.deepEqual(parsed.other, []);

  const crash = parseStylesLog('FAIL styles: validate-styles.mjs crashed: boom\nError: boom\n    at file:///w/tools/ci/validate-styles.mjs:95:32\n\n\u001b[31mPASS styles x\u001b[39m\n');
  assert.deepEqual(crash.fails.map((f) => [f.id, f.detail]), [[null, 'validate-styles.mjs crashed: boom']]);
  assert.deepEqual(crash.other, ['Error: boom', '    at file:///w/tools/ci/validate-styles.mjs:95:32']);
  assert.deepEqual(crash.passes.map((p) => p.id), ['x'], 'colour codes are removed');
  assert.deepEqual(parseStylesLog('PASS stylesheet ok\nFAILED styles x').other.length, 2, 'only "styles" as a whole word is a result line');
  assert.deepEqual(parseStylesLog('PASS styles openfreemap (skipped: --offline)').passes.map((p) => [p.id, p.detail]), [['openfreemap', '(skipped: --offline)']]);
});

test('helpers: stylesReport has nothing to say without a styles.log, lists WARN lines briefly and fails only on FAIL', () => {
  assert.deepEqual(stylesReport({ log: null }), { sections: [], problems: [], failed: false });

  const warned = stylesReport({ log: fixture('styles.warned.log') });
  assert.deepEqual(warned.problems, []);
  assert.equal(warned.failed, false);
  assert.match(warned.sections[0], /^## Map styles\n\nWARN: 3 lines about third-party styles .* a warning never fails the run\.\n\n```text\nWARN styles night: https:\/\/tiles\.openfreemap\.org\/styles\/dark answered 403\nWARN styles day: /);
  assert.match(warned.sections[0], /\n\nPASS: 2 \(satellite, demo-offline\)\.$/);
  assert.doesNotMatch(warned.sections[0], /FAIL/);

  const failed = stylesReport({ log: fixture('styles.failed.log') });
  assert.deepEqual(failed.problems, ['map styles failed: demo-offline'], 'one reason, the style named once');
  assert.equal(failed.failed, true);
  assert.match(failed.sections[0], /^## Map styles\n\nFAIL: 2 spec errors in a style we build or ship \(03 section 4\.9: this breaks the build\)\. Fix the style and push again\.\n\n```text\nFAIL styles demo-offline: base style: layers\[2\]\.paint\.line-width: number expected, string found\nFAIL styles demo-offline: with overlay: /);
  assert.match(failed.sections[0], /\n\nWARN: 3 lines about third-party styles/, 'the warnings stay visible next to a failure');

  const two = stylesReport({ log: 'FAIL styles satellite: a\nFAIL styles demo-offline: b\nFAIL styles satellite: c\n' });
  assert.deepEqual(two.problems, ['map styles failed: satellite, demo-offline']);
  assert.match(two.sections[0], /FAIL: 3 spec errors in a style/);
});

test('helpers: stylesReport cuts long lists (5 WARN lines, 40 FAIL lines) but counts all of them, and a log without a result line is a failure', () => {
  const warns = Array.from({ length: 8 }, (_, i) => `WARN styles night: published style: layers[${i}]: odd`).join('\n');
  const many = stylesReport({ log: `PASS styles satellite (x)\n${warns}\n` });
  assert.match(many.sections[0], /WARN: 8 lines about third-party styles/);
  assert.equal(many.sections[0].split('\n').filter((l) => l.startsWith('WARN styles night')).length, 5);
  assert.match(many.sections[0], /\n\.\.\. and 3 more WARN lines\n```/);
  assert.deepEqual(many.problems, []);

  const fails = Array.from({ length: 45 }, (_, i) => `FAIL styles satellite: layers[${i}]: bad`).join('\n');
  const lots = stylesReport({ log: fails });
  assert.match(lots.sections[0], /FAIL: 45 spec errors/);
  assert.equal(lots.sections[0].split('\n').filter((l) => l.startsWith('FAIL styles satellite')).length, 40);
  assert.match(lots.sections[0], /\n\.\.\. and 5 more FAIL lines \(all of them are in js\/styles\.log\)\n```/);

  for (const log of ['', '\n\n', 'node:internal/modules/esm/resolve:275\n    throw new ERR_MODULE_NOT_FOUND\n']) {
    const none = stylesReport({ log });
    assert.deepEqual(none.problems, ['map styles: styles.log holds no result line (validate-styles.mjs stopped before it printed one)'], JSON.stringify(log));
    assert.match(none.sections[0], /^FAIL: styles\.log holds no `PASS`, `WARN` or `FAIL` line, so the styles were not validated/m);
  }
  assert.match(stylesReport({ log: 'node:internal/modules/esm/resolve:275\n    throw new ERR_MODULE_NOT_FOUND\n' }).sections[0], /Other output of the script:\n\n```text\nnode:internal\/modules\/esm\/resolve:275\n {4}throw new ERR_MODULE_NOT_FOUND\n```/);
});

test('styles: a FAIL line fails the run, is named in the why line and in "## Map styles", and the failed js job is explained by it', () => {
  const run = summarize({
    files: { 'js/js-tests.tap': PASSING_TAP, 'js/tsc.log': TSC_CLEAN, 'js/styles.log': fixture('styles.failed.log') },
    needs: jsNeeds('failure'),
  });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.summary, /^- result: failure$/m);
  assert.match(run.summary, /^- why: job js: failure; map styles failed: demo-offline$/m);
  const styles = section(run.summary, 'Map styles');
  assert.ok(styles, 'the section exists');
  assert.match(styles, /^FAIL: 2 spec errors in a style we build or ship/m);
  assert.match(styles, /^FAIL styles demo-offline: base style: layers\[2\]\.paint\.line-width: number expected, string found$/m);
  assert.match(styles, /^WARN styles night: https:\/\/tiles\.openfreemap\.org\/styles\/dark answered 403$/m);
  assert.match(section(run.summary, 'JS'), /^The js job failed on the map styles step: see the Map styles section\.$/m);
  assert.doesNotMatch(run.summary, /^The js job failed but js-tests\.tap has no failing test/m, 'the styles explain the failed job');
  assert.doesNotMatch(run.summary, /Job js did not succeed, yet no compiler error/, 'the generic note is for jobs nothing explains');
  const at = (heading) => run.summary.indexOf(`\n## ${heading}`);
  assert.ok(at('JS') > 0 && at('JS') < at('Map styles'), 'Map styles follows the JS section');
  assert.equal(fs.readFileSync(path.join(run.outDir, 'js', 'styles.log'), 'utf8'), fixture('styles.failed.log'), 'styles.log is published beside the other js files');
});

test('styles: WARN lines alone (a third-party fetch that failed) are listed and never fail the run', () => {
  const run = summarize({
    files: { 'js/js-tests.tap': PASSING_TAP, 'js/tsc.log': TSC_CLEAN, 'js/styles.log': fixture('styles.warned.log') },
    needs: jsNeeds('success'),
  });
  assert.match(run.summary, /^- result: success$/m);
  assert.doesNotMatch(run.summary, /^- why:/m);
  const styles = section(run.summary, 'Map styles');
  assert.match(styles, /^WARN: 3 lines about third-party styles/m);
  assert.match(styles, /^WARN styles streets: https:\/\/tiles\.openfreemap\.org\/styles\/liberty answered 403$/m);
  assert.match(styles, /^PASS: 2 \(satellite, demo-offline\)\.$/m);
  assert.doesNotMatch(styles, /FAIL/);
  assert.ok(fs.existsSync(path.join(run.outDir, 'js', 'styles.log')));
});

test('styles: without --needs a FAIL line still fails the run; a crash or an empty styles.log is a failure with the output shown; no styles.log, no section', () => {
  const failed = summarize({ files: { 'js/styles.log': fixture('styles.failed.log') } });
  assert.match(failed.summary, /^- result: failure$/m);
  assert.match(failed.summary, /^- why: map styles failed: demo-offline$/m);

  const crash = summarize({ files: { 'js/styles.log': 'FAIL styles: validate-styles.mjs crashed: boom\nError: boom\n    at file:///w/validate-styles.mjs:95:32\n' }, needs: jsNeeds('failure') });
  assert.match(crash.summary, /^- why: job js: failure; map styles failed: validate-styles$/m);
  assert.match(section(crash.summary, 'Map styles'), /Other output of the script:\n\n```text\nError: boom\n {4}at file:\/\/\/w\/validate-styles\.mjs:95:32\n```/);

  const empty = summarize({ files: { 'js/styles.log': '' }, needs: jsNeeds('failure') });
  assert.match(empty.summary, /^- why: job js: failure; map styles: styles\.log holds no result line/m);

  const absent = summarize({ files: { 'js/js-tests.tap': PASSING_TAP, 'js/tsc.log': TSC_CLEAN }, needs: jsNeeds('success') });
  assert.equal(section(absent.summary, 'Map styles'), null);
  assert.match(absent.summary, /^- result: success$/m);
});

test('styles: what validate-styles.mjs really prints is understood (a spec error of ours fails the run, a refused fetch only warns)', async () => {
  const { buildStyle } = await import('../../src/Realm.Web/wwwroot/js/mapStyles.js');
  const { main: validateStyles } = await import('../../tools/ci/validate-styles.mjs');
  const refused = async () => new Response('', { status: 403 });
  const broken = (id, options) => {
    const style = buildStyle(id, options);
    if (id === 'satellite') style.layers[0].type = 'nonsense';
    return style;
  };
  const clean = [];
  assert.equal(await validateStyles([], (line) => clean.push(line), { fetchImpl: refused }), 0);
  const bad = [];
  assert.equal(await validateStyles([], (line) => bad.push(line), { built: { build: broken }, fetchImpl: refused }), 1);

  const ok = stylesReport({ log: clean.join('\n') });
  assert.deepEqual(ok.problems, []);
  assert.equal(parseStylesLog(clean.join('\n')).warns.length, 3);
  const ko = stylesReport({ log: bad.join('\n') });
  assert.equal(ko.failed, true);
  assert.deepEqual(ko.problems, ['map styles failed: satellite']);
  assert.equal(parseStylesLog(bad.join('\n')).fails.length, bad.filter((line) => line.startsWith('FAIL ')).length, 'every FAIL line the script prints is read');
  assert.equal(parseStylesLog(bad.join('\n')).other.length, 0, 'and nothing it prints is left unread');
});
