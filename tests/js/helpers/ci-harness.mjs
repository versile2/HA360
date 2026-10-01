// Shared helpers for the tests of tools/ci (make-summary, publish-ci-artifacts, wait-for-ci).
// Not a test file: only *.test.mjs files are picked up by `node --test "tests/js/**/*.test.mjs"`.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const repoRoot = path.resolve(here, '..', '..', '..');
export const fixturesDir = path.join(here, '..', 'fixtures', 'ci');
export const tools = {
  makeSummary: path.join(repoRoot, 'tools', 'ci', 'make-summary.mjs'),
  publish: path.join(repoRoot, 'tools', 'ci', 'publish-ci-artifacts.sh'),
  wait: path.join(repoRoot, 'tools', 'ci', 'wait-for-ci.sh'),
  common: path.join(repoRoot, 'tools', 'ci', 'ci-common.sh'),
};

const createdDirs = [];
process.on('exit', () => {
  for (const dir of createdDirs) fs.rmSync(dir, { recursive: true, force: true });
});

// A fresh temporary directory, removed when the test process exits.
export function tempDir(prefix) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `${prefix}-`));
  createdDirs.push(dir);
  return dir;
}

export function fixture(name) {
  return fs.readFileSync(path.join(fixturesDir, name), 'utf8');
}

export function writeFile(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
}

// An environment in which git and the scripts behave the same on every machine: no user or system git
// config, no GitHub variables, no token, no prompts.
export function cleanEnv(extra = {}) {
  const env = { ...process.env };
  for (const key of Object.keys(env)) {
    if (key.startsWith('GITHUB_') || key.startsWith('GIT_') || key === 'GH_TOKEN' || key === 'CI_OUT' || key === 'CI_REMOTE') delete env[key];
  }
  return {
    ...env,
    HOME: tempDir('ci-home'),
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_TERMINAL_PROMPT: '0',
    LC_ALL: 'C',
    PUBLISH_BACKOFF_S: '0',
    ...extra,
  };
}

export function git(cwd, args, extraEnv = {}) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', env: cleanEnv(extraEnv) });
  if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed in ${cwd}: ${result.stderr}`);
  return result.stdout.trim();
}

// A bare repository standing in for the GitHub remote, addressed as file:// (a plain path would ignore --depth).
export function createBareRemote() {
  const dir = tempDir('ci-bare');
  git(dir, ['init', '--bare', '-q', '.']);
  return { dir, url: `file://${dir}` };
}

// A working repository whose "origin" is the bare remote and whose branch is `branch` (one empty commit).
export function createWorkRepo(remoteUrl, branch) {
  const dir = tempDir('ci-work');
  git(dir, ['init', '-q', '.']);
  git(dir, ['checkout', '-q', '-b', branch]);
  git(dir, ['-c', 'user.name=t', '-c', 'user.email=t@example.invalid', 'commit', '-q', '--allow-empty', '-m', 'init']);
  git(dir, ['remote', 'add', 'origin', remoteUrl]);
  return dir;
}

export function hex(n, length = 40) {
  return n.toString(16).padStart(length, '0').slice(-length);
}

// A ci-out folder as make-summary.mjs would leave it, written by hand so the publish tests do not depend on it.
export function writeCiOut(dir, { branch, sha, run, result = 'success', url = 'https://ci.example.invalid/run', errors = '' }) {
  writeFile(
    path.join(dir, 'SUMMARY.md'),
    [`# CI summary: ${result.toUpperCase()}`, '', `- result: ${result}`, `- branch: ${branch}`, `- sha: ${sha}`, `- run: ${run}`, `- url: ${url}`, '', 'body line', ''].join('\n'),
  );
  writeFile(path.join(dir, 'errors.log'), errors);
  return dir;
}

export function runScript(script, args, { cwd, env = {} } = {}) {
  const result = spawnSync('bash', [script, ...args], { cwd, encoding: 'utf8', env: cleanEnv(env) });
  return { status: result.status, stdout: result.stdout, stderr: result.stderr };
}

// Like runScript but asynchronous, so that two processes can run at the same time.
export function runScriptAsync(script, args, { cwd, env = {} } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn('bash', [script, ...args], { cwd, env: cleanEnv(env) });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => (stdout += chunk));
    child.stderr.on('data', (chunk) => (stderr += chunk));
    child.on('error', reject);
    child.on('close', (status) => resolve({ status, stdout, stderr }));
  });
}

// A script that keeps running while the test does other things (publish a newer run, for example).
export function startScript(script, args, { cwd, env = {} } = {}) {
  const child = spawn('bash', [script, ...args], { cwd, env: cleanEnv(env) });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', (chunk) => (stdout += chunk));
  child.stderr.on('data', (chunk) => (stderr += chunk));
  const done = new Promise((resolve, reject) => {
    child.on('error', reject);
    child.on('close', (status) => resolve({ status, stdout, stderr }));
  });
  // Resolves when the script has written a line matching `pattern` to stderr.
  const untilStderr = (pattern, timeoutMs = 20000) =>
    new Promise((resolve, reject) => {
      const started = Date.now();
      const timer = setInterval(() => {
        if (pattern.test(stderr)) {
          clearInterval(timer);
          resolve();
        } else if (Date.now() - started > timeoutMs) {
          clearInterval(timer);
          reject(new Error(`timed out waiting for ${pattern} in stderr:\n${stderr}`));
        }
      }, 25);
    });
  return { child, done, untilStderr };
}

// Publishes `ciOutDir`. Without `remoteUrl` the script uses "origin" of `cwd`.
export function publish(ciOutDir, remoteUrl, { cwd = os.tmpdir(), env = {} } = {}) {
  const remote = remoteUrl === undefined ? {} : { CI_REMOTE: remoteUrl };
  return runScript(tools.publish, [], { cwd, env: { CI_OUT: ciOutDir, ...remote, ...env } });
}

export function treeOf(bareDir, ref = 'ci-artifacts') {
  return git(bareDir, ['ls-tree', '-r', '--name-only', ref]).split('\n').filter(Boolean);
}

export function showFile(bareDir, spec) {
  return git(bareDir, ['show', spec]);
}
