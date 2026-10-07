#!/usr/bin/env node
// validate-addon.mjs: the add-on contract checks of the `addon-validate` guard (03 section 7.3, 04 card S1a).
// It catches what the Supervisor would otherwise only show on the box (research/ha-addon-ingress.md section 1).
// Standard library plus `yaml`; no network. Also imported by guards.mjs, which prints the same lines.
//
//   node tools/ci/validate-addon.mjs [--root <repository root>]
//
// Prints `PASS addon-validate` or one `FAIL addon-validate: <file>[:line] <message>` per problem; exit 1 on any FAIL.
// The Dockerfile check (ingress_port against ASPNETCORE_HTTP_PORTS) is skipped while there is no Dockerfile (S2).

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import YAML from 'yaml';

export const IMAGE = 'ghcr.io/versile2/ha-cartographer';
export const REQUIRED_KEYS = ['name', 'slug', 'description', 'version', 'arch'];
export const WATCHDOG_RE = /^(?:https?|\[PROTO:\w+\]|tcp):\/\/\[HOST\]:(\[PORT:\d+\]|\d+).*$/;
export const SEMVER_RE = /^\d+\.\d+\.\d+$/;
export const MAX_NESTING = 2; // the Supervisor allows a list of dicts and no deeper
export const DEFAULT_INGRESS_PORT = 8099;
export const BUILD_FILES = ['build.yaml', 'build.yml', 'build.json'];
export const ICON_SIZE = { width: 128, height: 128 };
export const LOGO_RANGE = { width: [200, 300], height: [80, 120] }; // "about 250 x 100"

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

// ---------------------------------------------------------------------------------------------
// Helpers shared with guards.mjs
// ---------------------------------------------------------------------------------------------

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// 1-based line of `key:` in a YAML text; `section` (a top-level key) narrows the search to what follows it.
export function lineOfKey(text, key, section) {
  let start = 0;
  if (section) {
    const found = new RegExp(`^${escapeRegExp(section)}[ \\t]*:`, 'm').exec(text);
    if (found) start = found.index;
  }
  const match = new RegExp(`^[ \\t]*${escapeRegExp(key)}[ \\t]*:`, 'm').exec(text.slice(start));
  if (!match) return undefined;
  return text.slice(0, start + match.index).split('\n').length;
}

// { text, data } of a YAML file; { missing: true } when it does not exist; { error } when it does not parse.
export function readYaml(file) {
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (err) {
    if (err.code === 'ENOENT') return { missing: true };
    return { error: err.message };
  }
  try {
    return { text, data: YAML.parse(text) ?? {} };
  } catch (err) {
    return { text, error: String(err.message).split('\n')[0] };
  }
}

// { width, height } from the IHDR chunk of a PNG buffer, or null when it is not a PNG.
export function pngSize(buffer) {
  if (buffer.length < 24 || !buffer.subarray(0, 8).equals(PNG_SIGNATURE) || buffer.toString('latin1', 12, 16) !== 'IHDR') return null;
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

// How deep a schema or options value nests: a scalar is 0, a list or dict is one more than its deepest child.
export function nesting(value) {
  if (Array.isArray(value)) return 1 + Math.max(0, ...value.map(nesting));
  if (isObject(value)) return 1 + Math.max(0, ...Object.values(value).map(nesting));
  return 0;
}

// ---------------------------------------------------------------------------------------------
// The checks
// ---------------------------------------------------------------------------------------------

// Returns { problems: [{ file, line?, message }], notes: [string] } for the repository at `root`.
export function validateAddon(root) {
  const problems = [];
  const notes = [];
  const add = (file, line, message) => problems.push({ file, line, message });
  const configFile = 'realm/config.yaml';

  const config = readYaml(path.join(root, configFile));
  if (config.missing) {
    add(configFile, undefined, 'is missing: the Supervisor finds the add-on through it');
  } else if (config.error) {
    add(configFile, undefined, `is not valid YAML: ${config.error}`);
  } else if (!isObject(config.data)) {
    add(configFile, undefined, 'must be a YAML mapping');
  } else {
    checkConfig(root, configFile, config, add, notes);
  }

  checkAssets(root, add);
  checkRepositoryFile(root, add);
  return { problems, notes };
}

function checkConfig(root, file, { text, data }, add, notes) {
  const where = (key, section) => lineOfKey(text, key, section);

  for (const key of REQUIRED_KEYS) {
    const value = data[key];
    const empty = value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
    if (empty) add(file, undefined, `required key '${key}' is missing`);
  }

  if (data.image !== IMAGE) {
    add(file, where('image'), `image must be exactly '${IMAGE}' (lower case, no tag, no {arch}), found ${data.image === undefined ? 'none' : `'${data.image}'`}`);
  }

  if (data.version !== undefined && data.version !== null) {
    const version = String(data.version);
    if (!SEMVER_RE.test(version)) {
      add(file, where('version'), `version '${version}' is not plain MAJOR.MINOR.PATCH (quote it in YAML)`);
    } else {
      checkChangelog(root, version, add);
    }
  }

  checkIngressPort(root, file, data, where, add, notes);

  if (data.watchdog !== undefined && !(typeof data.watchdog === 'string' && WATCHDOG_RE.test(data.watchdog))) {
    add(file, where('watchdog'), `watchdog '${data.watchdog}' does not match the Supervisor pattern ${WATCHDOG_RE.source}`);
  }

  const options = isObject(data.options) ? data.options : {};
  const schema = isObject(data.schema) ? data.schema : {};
  for (const [section, block] of [['options', options], ['schema', schema]]) {
    for (const [key, value] of Object.entries(block)) {
      if (nesting(value) > MAX_NESTING) add(file, where(key, section), `${section}.${key} nests deeper than ${MAX_NESTING} levels`);
    }
  }
  for (const key of Object.keys(options)) {
    if (!(key in schema)) add(file, where(key, 'options'), `option '${key}' has no entry in schema`);
  }
  for (const [key, type] of Object.entries(schema)) {
    if (typeof type === 'string' && /^password\b/.test(type) && options[key] !== undefined && options[key] !== null) {
      add(file, where(key, 'options'), `option '${key}' is typed password and must have no default`);
    }
  }

  const mapped = data.map;
  if (mapped !== undefined && mapped !== null && !(Array.isArray(mapped) && mapped.length === 0)) {
    add(file, where('map'), "'map' is declared, but v1 mounts nothing from Home Assistant");
  }
  for (const name of BUILD_FILES) {
    if (fs.existsSync(path.join(root, 'realm', name))) add(`realm/${name}`, undefined, `${name} is no longer used by the Supervisor: delete it`);
  }
}

function checkChangelog(root, version, add) {
  const file = 'realm/CHANGELOG.md';
  let text;
  try {
    text = fs.readFileSync(path.join(root, file), 'utf8');
  } catch {
    add(file, undefined, `is missing: it needs a section for version ${version}`);
    return;
  }
  // The top section may be newer than `version` (release window, 03 section 6.6); a section for `version` must exist.
  if (!new RegExp(`^##\\s+\\[?v?${escapeRegExp(version)}\\]?(?:\\s|$)`, 'm').test(text)) {
    add(file, undefined, `has no '## ${version}' section for the version in config.yaml`);
  }
}

function checkIngressPort(root, file, data, where, add, notes) {
  let dockerfile;
  try {
    dockerfile = fs.readFileSync(path.join(root, 'Dockerfile'), 'utf8');
  } catch {
    notes.push('no Dockerfile yet, so ingress_port was not compared with ASPNETCORE_HTTP_PORTS');
    return;
  }
  const port = String(data.ingress_port ?? DEFAULT_INGRESS_PORT);
  const found = /\bASPNETCORE_HTTP_PORTS\s*[=\s]\s*"?([0-9;]+)"?/.exec(dockerfile);
  if (!found) {
    add('Dockerfile', undefined, `sets no ASPNETCORE_HTTP_PORTS, so Kestrel will not listen on ingress_port ${port}`);
  } else if (found[1] !== port) {
    add('Dockerfile', undefined, `ASPNETCORE_HTTP_PORTS=${found[1]} differs from ingress_port ${port} in ${file}`);
  }
}

function checkAssets(root, add) {
  const specs = [
    ['realm/icon.png', (s) => s.width === ICON_SIZE.width && s.height === ICON_SIZE.height, '128 x 128'],
    ['realm/logo.png', (s) => s.width >= LOGO_RANGE.width[0] && s.width <= LOGO_RANGE.width[1] && s.height >= LOGO_RANGE.height[0] && s.height <= LOGO_RANGE.height[1], 'about 250 x 100'],
  ];
  for (const [file, accepts, expected] of specs) {
    let buffer;
    try {
      buffer = fs.readFileSync(path.join(root, file));
    } catch {
      add(file, undefined, 'is missing');
      continue;
    }
    const size = pngSize(buffer);
    if (!size) add(file, undefined, 'is not a PNG file');
    else if (!accepts(size)) add(file, undefined, `is ${size.width} x ${size.height}, expected ${expected}`);
  }
}

function checkRepositoryFile(root, add) {
  const file = 'repository.yaml';
  const repo = readYaml(path.join(root, file));
  if (repo.missing) add(file, undefined, 'is missing: the store skips a repository without it');
  else if (repo.error) add(file, undefined, `is not valid YAML: ${repo.error}`);
  else if (!isObject(repo.data) || typeof repo.data.name !== 'string' || repo.data.name.trim() === '') add(file, undefined, "lacks the required key 'name'");
}

// ---------------------------------------------------------------------------------------------
// Command line
// ---------------------------------------------------------------------------------------------

export function formatProblem({ file, line, message }) {
  return `${file}${line ? `:${line}` : ''} ${message}`;
}

export function main(argv) {
  let root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--root' && i + 1 < argv.length) root = path.resolve(argv[(i += 1)]);
    else {
      process.stderr.write(`validate-addon: unknown argument '${argv[i]}'\nusage: validate-addon.mjs [--root <repository root>]\n`);
      return 64;
    }
  }
  const { problems, notes } = validateAddon(root);
  if (problems.length === 0) {
    process.stdout.write(`PASS addon-validate${notes.length > 0 ? ` (${notes.join('; ')})` : ''}\n`);
    return 0;
  }
  for (const problem of problems) process.stdout.write(`FAIL addon-validate: ${formatProblem(problem)}\n`);
  return 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}
