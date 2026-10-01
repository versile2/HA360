// Tests for tools/ci/make-summary.mjs: SUMMARY.md from errors.log, restore.log and .trx files.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { crashReport, distinctDiagnostics, normalizeDiagnostic, parseTrx } from '../../tools/ci/make-summary.mjs';
import { cleanEnv, fixture, tempDir, tools, writeFile } from './helpers/ci-harness.mjs';

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
