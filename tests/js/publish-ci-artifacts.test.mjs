// Tests for tools/ci/publish-ci-artifacts.sh against a file:// bare repository standing in for the remote.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import {
  cleanEnv,
  createBareRemote,
  createWorkRepo,
  git,
  hex,
  publish,
  runScriptAsync,
  showFile,
  tempDir,
  tools,
  treeOf,
  writeCiOut,
  writeFile,
} from './helpers/ci-harness.mjs';

const SHA_A = `abcdef0${hex(1, 33)}`;
const SHA_B = `1234567${hex(2, 33)}`;

function ciOut(options) {
  return writeCiOut(tempDir('ci-out'), options);
}

test('first publish creates ci-artifacts: README, LATEST.md, LATEST.json and runs/<slug>/<run>-<sha7>/', () => {
  const remote = createBareRemote();
  const out = ciOut({ branch: 'slice/S0-skeleton', sha: SHA_A, run: 7, result: 'failure', errors: 'src/X.cs(1,1): CS1 boom\n' });
  const result = publish(out, remote.url);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /published runs\/slice-s0-skeleton\/7-abcdef0 \(failure\)/);

  assert.deepEqual(treeOf(remote.dir), [
    'LATEST.json',
    'LATEST.md',
    'README.md',
    'runs/slice-s0-skeleton/7-abcdef0/SUMMARY.md',
    'runs/slice-s0-skeleton/7-abcdef0/errors.log',
  ]);
  const latest = JSON.parse(showFile(remote.dir, 'ci-artifacts:LATEST.json'));
  assert.deepEqual(latest, {
    run: 7,
    sha: SHA_A,
    branch: 'slice/S0-skeleton',
    slug: 'slice-s0-skeleton',
    result: 'failure',
    url: 'https://ci.example.invalid/run',
  });
  assert.equal(showFile(remote.dir, 'ci-artifacts:LATEST.md'), showFile(remote.dir, 'ci-artifacts:runs/slice-s0-skeleton/7-abcdef0/SUMMARY.md'));
  assert.equal(showFile(remote.dir, 'ci-artifacts:runs/slice-s0-skeleton/7-abcdef0/errors.log'), 'src/X.cs(1,1): CS1 boom');
});

test('the branch is one orphan commit, however many runs were published', () => {
  const remote = createBareRemote();
  publish(ciOut({ branch: 'a', sha: SHA_A, run: 1 }), remote.url);
  publish(ciOut({ branch: 'b', sha: SHA_B, run: 2 }), remote.url);
  publish(ciOut({ branch: 'a', sha: SHA_B, run: 3 }), remote.url);
  assert.equal(git(remote.dir, ['rev-list', '--count', 'ci-artifacts']), '1');
  assert.equal(git(remote.dir, ['rev-list', '--parents', '-n1', 'ci-artifacts']).split(' ').length, 1, 'the commit has no parent');
});

test('the runs/ folders of other branches are kept; the folder of the same branch is replaced', () => {
  const remote = createBareRemote();
  assert.equal(publish(ciOut({ branch: 'slice/S1-hosting-shell', sha: SHA_A, run: 10 }), remote.url).status, 0);
  assert.equal(publish(ciOut({ branch: 'slice/S3-domain-core', sha: SHA_B, run: 11 }), remote.url).status, 0);

  let tree = treeOf(remote.dir);
  assert.ok(tree.includes('runs/slice-s1-hosting-shell/10-abcdef0/SUMMARY.md'), 'the first branch survives the second publish');
  assert.ok(tree.includes('runs/slice-s3-domain-core/11-1234567/SUMMARY.md'));
  assert.equal(JSON.parse(showFile(remote.dir, 'ci-artifacts:LATEST.json')).slug, 'slice-s3-domain-core', 'LATEST names the newest run of any branch');

  assert.equal(publish(ciOut({ branch: 'slice/S1-hosting-shell', sha: SHA_B, run: 12 }), remote.url).status, 0);
  tree = treeOf(remote.dir);
  assert.ok(!tree.some((f) => f.startsWith('runs/slice-s1-hosting-shell/10-')), 'the older run of the same branch is replaced');
  assert.ok(tree.includes('runs/slice-s1-hosting-shell/12-1234567/SUMMARY.md'));
  assert.ok(tree.includes('runs/slice-s3-domain-core/11-1234567/SUMMARY.md'), 'the other branch is untouched');
});

test('everything in ci-out goes into the run folder; a missing errors.log becomes an empty file', () => {
  const remote = createBareRemote();
  const out = ciOut({ branch: 'main', sha: SHA_A, run: 4 });
  fs.rmSync(path.join(out, 'errors.log'));
  writeFile(path.join(out, 'build.tail.log'), 'tail\n');
  writeFile(path.join(out, 'tests', 'results.trx'), '<TestRun/>');
  writeFile(path.join(out, 'shots', 'phone', 'sc01.png'), 'png');
  assert.equal(publish(out, remote.url).status, 0);
  const tree = treeOf(remote.dir);
  for (const name of ['SUMMARY.md', 'errors.log', 'build.tail.log', 'tests/results.trx', 'shots/phone/sc01.png']) {
    assert.ok(tree.includes(`runs/main/4-abcdef0/${name}`), `${name} is published`);
  }
  assert.equal(showFile(remote.dir, 'ci-artifacts:runs/main/4-abcdef0/errors.log'), '');
});

test('the remote defaults to "origin" of the current repository', () => {
  const remote = createBareRemote();
  const work = createWorkRepo(remote.url, 'main');
  const result = publish(ciOut({ branch: 'main', sha: SHA_A, run: 1 }), undefined, { cwd: work });
  assert.equal(result.status, 0, result.stderr);
  assert.ok(treeOf(remote.dir).includes('runs/main/1-abcdef0/SUMMARY.md'));
});

test('two publishers racing: the loser retries on top of the winner and both runs end up published', async () => {
  const remote = createBareRemote();
  // A slow pre-receive hook keeps both pushes in flight at the same time, so the second ref update is rejected.
  const hook = path.join(remote.dir, 'hooks', 'pre-receive');
  writeFile(hook, '#!/bin/sh\nsleep 1\n');
  fs.chmodSync(hook, 0o755);

  const first = ciOut({ branch: 'slice/S1-hosting-shell', sha: SHA_A, run: 21 });
  const second = ciOut({ branch: 'slice/S3-domain-core', sha: SHA_B, run: 22 });
  const [one, two] = await Promise.all([
    runScriptAsync(tools.publish, [], { cwd: tempDir('cwd'), env: { CI_OUT: first, CI_REMOTE: remote.url } }),
    runScriptAsync(tools.publish, [], { cwd: tempDir('cwd'), env: { CI_OUT: second, CI_REMOTE: remote.url } }),
  ]);
  assert.equal(one.status, 0, one.stderr);
  assert.equal(two.status, 0, two.stderr);
  assert.match(one.stderr + two.stderr, /retrying, attempt 2 of 3/, 'one of them had to retry');

  const tree = treeOf(remote.dir);
  assert.ok(tree.includes('runs/slice-s1-hosting-shell/21-abcdef0/SUMMARY.md'), 'no run was lost');
  assert.ok(tree.includes('runs/slice-s3-domain-core/22-1234567/SUMMARY.md'), 'no run was lost');
  assert.equal(git(remote.dir, ['rev-list', '--count', 'ci-artifacts']), '1');
});

test('GH_TOKEN: an https://github.com remote is pushed through the x-access-token URL, and the token is never printed', () => {
  const remote = createBareRemote();
  const token = 'ghs_SENTINELtoken123';
  // git rewrites exactly the URL the script is expected to build onto the local bare repository.
  const env = {
    GH_TOKEN: token,
    GIT_CONFIG_COUNT: '1',
    GIT_CONFIG_KEY_0: `url.${remote.url}.insteadOf`,
    GIT_CONFIG_VALUE_0: `https://x-access-token:${token}@github.com/Versile2/ha360.git`,
  };
  for (const githubUrl of ['https://github.com/Versile2/ha360', 'https://github.com/Versile2/ha360.git', 'https://github.com/Versile2/ha360/']) {
    const out = ciOut({ branch: 'main', sha: SHA_A, run: 5 });
    const result = publish(out, githubUrl, { env });
    assert.equal(result.status, 0, `${githubUrl}: ${result.stderr}`);
    assert.ok(!(result.stdout + result.stderr).includes(token), 'the token never reaches stdout or stderr');
  }
  assert.ok(treeOf(remote.dir).includes('runs/main/5-abcdef0/SUMMARY.md'), 'the push went through the rewritten token URL');
});

test('GH_TOKEN: a failing push gives up after 3 attempts (exit 1) without printing the token', () => {
  const token = 'ghs_SENTINELfailing456';
  const result = publish(ciOut({ branch: 'main', sha: SHA_A, run: 1 }), 'https://github.com/Versile2/ha360', {
    env: { GH_TOKEN: token, GIT_CONFIG_COUNT: '1', GIT_CONFIG_KEY_0: 'http.proxy', GIT_CONFIG_VALUE_0: 'http://127.0.0.1:9' },
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /attempt 3 of 3/);
  assert.match(result.stderr, /failed after 3 attempts/);
  assert.ok(!(result.stdout + result.stderr).includes(token), 'the token never reaches stdout or stderr');
});

test('a token is not applied to a remote that is not https://github.com', () => {
  const remote = createBareRemote();
  const token = 'ghs_SENTINELfile789';
  const result = publish(ciOut({ branch: 'main', sha: SHA_A, run: 1 }), remote.url, { env: { GH_TOKEN: token } });
  assert.equal(result.status, 0, result.stderr);
  assert.ok(!(result.stdout + result.stderr).includes(token));
});

test('usage errors exit 64: no SUMMARY.md, and a header without a usable sha, run or branch', () => {
  const remote = createBareRemote();
  const empty = tempDir('ci-out-empty');
  const missing = publish(empty, remote.url);
  assert.equal(missing.status, 64);
  assert.match(missing.stderr, /SUMMARY\.md not found/);

  for (const [override, message] of [
    [{ sha: 'not-a-sha' }, /sha/],
    [{ run: 'x' }, /run/],
    [{ branch: 'unknown' }, /branch/],
  ]) {
    const out = ciOut({ branch: 'main', sha: SHA_A, run: 1, ...override });
    const result = publish(out, remote.url);
    assert.equal(result.status, 64, JSON.stringify(override));
    assert.match(result.stderr, message);
  }
  const noOrigin = publish(ciOut({ branch: 'main', sha: SHA_A, run: 1 }), undefined, { cwd: tempDir('not-a-repo') });
  assert.equal(noOrigin.status, 64);
});

test('branch_slug (shared by publish and wait): lower-case, everything outside [a-z0-9._-] becomes "-"', () => {
  const slug = (branch) => spawnSync('bash', ['-c', 'source "$1"; branch_slug "$2"', 'bash', tools.common, branch], { encoding: 'utf8', env: cleanEnv() }).stdout;
  assert.equal(slug('slice/S5-map-1'), 'slice-s5-map-1');
  assert.equal(slug('slice/S0-skeleton'), 'slice-s0-skeleton');
  assert.equal(slug('hotfix/H1_topic.v2'), 'hotfix-h1_topic.v2');
  assert.equal(slug('Fix/W3-1 A+B'), 'fix-w3-1-a-b');
  assert.equal(slug('main'), 'main');
  assert.equal(slug(slug('slice/S5-map-1')), 'slice-s5-map-1', 'idempotent');
});

test('both scripts use the one branch_slug of ci-common.sh and neither traces', () => {
  for (const script of [tools.publish, tools.wait]) {
    const text = fs.readFileSync(script, 'utf8');
    assert.match(text, /source "\$script_dir\/ci-common\.sh"/, `${path.basename(script)} sources ci-common.sh`);
    assert.match(text, /branch_slug "/, `${path.basename(script)} calls branch_slug`);
    assert.ok(!/branch_slug\(\)/.test(text), `${path.basename(script)} defines no slug function of its own`);
    assert.ok(!/\bset\s+-[a-zA-Z]*x/.test(text), `${path.basename(script)} never runs set -x`);
  }
});
