// Tests for tools/ci/wait-for-ci.sh. A file:// bare repository stands in for "origin"; runs are published
// into it with the real publish-ci-artifacts.sh, so the two scripts are tested against each other.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  createBareRemote,
  createWorkRepo,
  git,
  hex,
  publish,
  runScript,
  startScript,
  tempDir,
  tools,
  writeCiOut,
} from './helpers/ci-harness.mjs';

const BRANCH = 'slice/S0-skeleton';
const sha = (n) => `${hex(n, 7)}${hex(n + 1000, 33)}`; // 7 hex digits, then 33 more

// A bare "origin", a working repository on BRANCH, and a function that publishes one run.
function scenario(branch = BRANCH) {
  const remote = createBareRemote();
  const work = createWorkRepo(remote.url, branch);
  const publishRun = ({ branch: runBranch = branch, sha: runSha, run, result = 'success', url = `https://ci.example.invalid/runs/${run}` }) => {
    const out = writeCiOut(tempDir('ci-out'), { branch: runBranch, sha: runSha, run, result, url });
    const published = publish(out, remote.url);
    assert.equal(published.status, 0, published.stderr);
  };
  return { remote, work, publishRun };
}

const wait = (work, args, env = {}) => runScript(tools.wait, args, { cwd: work, env: { WAIT_POLL_S: '1', WAIT_TIMEOUT_S: '30', ...env } });

test('exit 0: a successful run for the SHA; prints SUMMARY.md, RESULT=success and the run URL', () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(1), run: 5 });
  const result = wait(work, [sha(1)]); // the branch comes from `git rev-parse --abbrev-ref HEAD`
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /^# CI summary: SUCCESS$/m);
  assert.match(result.stdout, new RegExp(`^- sha: ${sha(1)}$`, 'm'));
  assert.match(result.stdout, /^RESULT=success$/m);
  assert.match(result.stdout, /^RUN_URL=https:\/\/ci\.example\.invalid\/runs\/5$/m);
  assert.ok(result.stdout.indexOf('RESULT=success') > result.stdout.indexOf('body line'), 'the verdict line follows the SUMMARY');
});

test('exit 1: a failed run; a 7-character SHA is enough', () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(2), run: 6, result: 'failure' });
  const result = wait(work, [sha(2).slice(0, 7)]);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout, /^# CI summary: FAILURE$/m);
  assert.match(result.stdout, /^RESULT=failure$/m);
});

test('exit 2: nothing is published for the SHA before WAIT_TIMEOUT_S (2 s) runs out', () => {
  const { work } = scenario(); // ci-artifacts does not even exist yet
  const started = Date.now();
  const result = wait(work, [sha(3)], { WAIT_TIMEOUT_S: '2', WAIT_POLL_S: '30' });
  const elapsed = Date.now() - started;
  assert.equal(result.status, 2, result.stderr);
  assert.match(result.stderr, /timed out after 2s/);
  assert.match(result.stderr, /does not exist on origin yet/);
  assert.ok(elapsed >= 1500 && elapsed < 15000, `waited about 2 s, took ${elapsed} ms (the poll interval never overshoots the timeout)`);
  assert.equal(result.stdout, '', 'nothing on stdout when there is no verdict');
});

test('exit 2 also when other runs exist but none is ours and none is newer', () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(4), run: 8 });
  const result = wait(work, [sha(5)], { WAIT_TIMEOUT_S: '2' });
  assert.equal(result.status, 2, result.stderr);
});

test('exit 3: a newer run of the same branch was published, so the run for our SHA will never appear', async () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(6), run: 20 }); // the run that is current when waiting starts: the baseline is 20
  const waiting = startScript(tools.wait, [sha(7)], { cwd: work, env: { WAIT_POLL_S: '1', WAIT_TIMEOUT_S: '60' } });
  await waiting.untilStderr(/baseline run number under runs\/slice-s0-skeleton\/: 20/);
  publishRun({ sha: sha(8), run: 22 }); // run 21 (ours) was cancelled; the newer commit's run 22 publishes
  const result = await waiting.done;
  assert.equal(result.status, 3, result.stderr);
  assert.match(result.stderr, /superseded: runs\/slice-s0-skeleton\/22-/);
  assert.equal(result.stdout, '');
});

test('exit 3 needs a run of the SAME branch: a newer run of another branch does not supersede ours', async () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(9), run: 30 });
  const waiting = startScript(tools.wait, [sha(10)], { cwd: work, env: { WAIT_POLL_S: '1', WAIT_TIMEOUT_S: '4' } });
  await waiting.untilStderr(/baseline run number/);
  publishRun({ branch: 'slice/S3-domain-core', sha: sha(11), run: 31 });
  const result = await waiting.done;
  assert.equal(result.status, 2, `${result.stderr}`);
});

test('match by directory (PR-1 rule 2): works when LATEST.json already names another branch\'s run', () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(14), run: 50, result: 'failure' });
  publishRun({ branch: 'slice/S3-domain-core', sha: sha(15), run: 51 }); // two branches finishing together: LATEST.json names this one
  const result = wait(work, [sha(14)]);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout, new RegExp(`^- sha: ${sha(14)}$`, 'm'), 'the output is the matched run, not LATEST.md');
  assert.ok(!result.stdout.includes(sha(15)));
});

test('match by LATEST.json (PR-1 rule 1): the same SHA was built on another branch', () => {
  const { work, publishRun } = scenario();
  publishRun({ branch: 'main', sha: sha(16), run: 60 });
  const result = wait(work, [sha(16), BRANCH]); // no runs/slice-s0-skeleton/ folder exists, LATEST.json names the SHA
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, new RegExp(`^- sha: ${sha(16)}$`, 'm'));
  assert.match(result.stdout, /^RESULT=success$/m);
});

test('the branch argument overrides HEAD and is slugged (case and slashes)', () => {
  const { work, publishRun } = scenario('some/other-branch');
  publishRun({ branch: 'slice/S5-map-1', sha: sha(17), run: 70 });
  const result = wait(work, [sha(17), 'Slice/S5-Map-1']);
  assert.equal(result.status, 0, result.stderr);
});

test('our run appears while waiting: the match comes before the exit-3 rule, although its number is above the baseline', async () => {
  const { work, publishRun } = scenario();
  publishRun({ sha: sha(19), run: 79 }); // an earlier run: the baseline is 79
  const waiting = startScript(tools.wait, [sha(18)], { cwd: work, env: { WAIT_POLL_S: '1', WAIT_TIMEOUT_S: '60' } });
  await waiting.untilStderr(/baseline run number under runs\/slice-s0-skeleton\/: 79/);
  publishRun({ sha: sha(18), run: 80 }); // above the baseline, and ours
  const result = await waiting.done;
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, new RegExp(`^- sha: ${sha(18)}$`, 'm'));
  assert.match(result.stdout, /^RESULT=success$/m);
});

test('exit 64: usage errors', () => {
  const { remote, work } = scenario();

  const none = runScript(tools.wait, [], { cwd: work });
  assert.equal(none.status, 64);
  assert.match(none.stderr, /usage: wait-for-ci\.sh/);

  const badSha = runScript(tools.wait, ['not-a-sha'], { cwd: work });
  assert.equal(badSha.status, 64);
  assert.match(badSha.stderr, /not a commit SHA/);
  assert.equal(runScript(tools.wait, ['abc123'], { cwd: work }).status, 64, 'fewer than 7 digits');

  assert.equal(runScript(tools.wait, [sha(1), 'a', 'b'], { cwd: work }).status, 64, 'too many arguments');

  // A detached HEAD has no branch: it is a usage error, never a guess.
  git(work, ['checkout', '-q', '--detach']);
  const detached = runScript(tools.wait, [sha(1)], { cwd: work });
  assert.equal(detached.status, 64);
  assert.match(detached.stderr, /cannot tell the branch/);
  // ...unless the branch is given.
  const detachedWithBranch = runScript(tools.wait, [sha(1), BRANCH], { cwd: work, env: { WAIT_TIMEOUT_S: '1' } });
  assert.equal(detachedWithBranch.status, 2);

  // A repository without any commit has no branch either.
  const unborn = tempDir('ci-unborn');
  git(unborn, ['init', '-q', '.']);
  git(unborn, ['remote', 'add', 'origin', remote.url]);
  assert.equal(runScript(tools.wait, [sha(1)], { cwd: unborn }).status, 64);

  const outside = runScript(tools.wait, [sha(1), BRANCH], { cwd: tempDir('not-a-repo') });
  assert.equal(outside.status, 64);
  assert.match(outside.stderr, /not inside a git repository/);

  const noOrigin = tempDir('ci-no-origin');
  git(noOrigin, ['init', '-q', '.']);
  assert.equal(runScript(tools.wait, [sha(1), BRANCH], { cwd: noOrigin }).status, 64);

  assert.equal(runScript(tools.wait, [sha(1)], { cwd: work, env: { WAIT_TIMEOUT_S: 'soon' } }).status, 64);
  assert.equal(runScript(tools.wait, [sha(1)], { cwd: work, env: { WAIT_POLL_S: '0' } }).status, 64);
});

test('-h prints the usage on stdout and exits 0', () => {
  const result = runScript(tools.wait, ['-h'], { cwd: tempDir('anywhere') });
  assert.equal(result.status, 0);
  assert.match(result.stdout, /exit: 0 success, 1 failure, 2 timeout, 3 superseded, 64 usage error/);
});
