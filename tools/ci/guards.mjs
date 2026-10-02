#!/usr/bin/env node
// guards.mjs: the CI guards of 03 section 7.3 (as revised after D41), run by the `guards` job and by hand.
// Standard library plus `yaml` (through validate-addon.mjs); no network; seconds.
//
//   node tools/ci/guards.mjs [--root <repository root>]
//
// One line per finding: `PASS name`, `FAIL name: file:line message`, `REPORT name: ...` (report-only checks).
// A guard whose target does not exist yet prints `PASS name (nothing to check yet)`, so later slices never edit
// this file to make a guard "start". Exit 1 if any line is a FAIL, otherwise 0 (REPORT never fails the run).
//
// The twelve guards, in the order of 03 section 7.3: config-name, addon-validate, option-bindings, no-leading-slash,
// no-static-files, no-wallclock, no-colour-literals, vendored-maplibre, font-budget, package-pins, testid-contract
// and ac-coverage. The last two only REPORT until tools/ci/ac-scope.json says { "mode": "enforce" } (S15).
//
// Exceptions live in tools/ci/guards.allow.json, one list per guard, every entry with a reason:
//   { "no-colour-literals": [ { "file": "src/.../mapStyles.js", "contains": "optional line text", "reason": "why" } ] }
// An entry silences findings in that file (only on lines containing `contains`, when given). A malformed entry or one
// without a reason is itself a FAIL (`FAIL allow-list: ...`).
//
// Source scans skip comments (heuristically: block comments, whole-line and trailing `//` comments) and read XML by
// regular expression, not a parser. Everything is read relative to --root, so tests can point it at a temp directory.

import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { lineOfKey, readYaml, validateAddon } from './validate-addon.mjs';

export const DEFAULT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const GUARD_NAMES = [
  'config-name',
  'addon-validate',
  'option-bindings',
  'no-leading-slash',
  'no-static-files',
  'no-wallclock',
  'no-colour-literals',
  'vendored-maplibre',
  'font-budget',
  'package-pins',
  'testid-contract',
  'ac-coverage',
];
// The guards that read guards.allow.json.
export const ALLOWABLE = ['no-leading-slash', 'no-static-files', 'no-wallclock', 'no-colour-literals', 'font-budget', 'package-pins'];

export const FONT_BUDGET_BYTES = 153_600;
export const MUDBLAZOR_VERSION = '9.5.0'; // D15
export const AC_COUNT = 50; // AC-01..AC-50 (01 section 11, numbers never change)
const MAX_FAIL_LINES = 30;
const ALLOWED_CONFIG = 'realm/config.yaml';
const MAPLIBRE_DIR = 'src/Realm.Web/wwwroot/lib/maplibre-gl';
const ALLOW_FILE = 'tools/ci/guards.allow.json';
const SCOPE_FILE = 'tools/ci/ac-scope.json';
const TESTIDS_FILE = 'tools/ci/testids.json';
const PACKAGES_FILE = 'tools/ci/packages.allow.txt';
const BINDINGS_FILE = 'tools/ci/option-bindings.json';

// ---------------------------------------------------------------------------------------------
// Files and text
// ---------------------------------------------------------------------------------------------

// What the Supervisor skips (dot directories, rootfs) plus build output and dependencies, which are never tracked.
const TREE_SKIP = new Set(['rootfs', 'node_modules', 'bin', 'obj', 'publish', 'ci-out', 'ci-in', 'ci-in-dotnet', 'TestResults', 'test-results', 'playwright-report']);
const treeSkip = (name) => name.startsWith('.') || TREE_SKIP.has(name);
const sourceSkip = (name) => name.startsWith('.') || name === 'node_modules' || name === 'bin' || name === 'obj';

// Relative posix paths (sorted) of the files under root/start, not entering a directory for which skipDir(name) is true.
function walk(root, start, skipDir) {
  const found = [];
  const visit = (rel) => {
    let entries;
    try {
      entries = fs.readdirSync(path.join(root, rel), { withFileTypes: true });
    } catch {
      return; // a missing directory is "nothing to check yet"
    }
    entries.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    for (const entry of entries) {
      const child = rel === '' ? entry.name : `${rel}/${entry.name}`;
      if (entry.isDirectory()) {
        if (!skipDir(entry.name)) visit(child);
      } else if (entry.isFile()) {
        found.push(child);
      }
    }
  };
  visit(start);
  return found;
}

function readText(root, rel) {
  return fs.readFileSync(path.join(root, rel), 'utf8');
}

function readJson(root, rel) {
  let text;
  try {
    text = fs.readFileSync(path.join(root, rel), 'utf8');
  } catch (err) {
    return err.code === 'ENOENT' ? { missing: true } : { error: err.message };
  }
  try {
    return { data: JSON.parse(text) };
  } catch (err) {
    return { error: err.message };
  }
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const blank = (match) => match.replace(/[^\r\n]/g, ' '); // keeps newlines, so line numbers survive

// 'css' (block comments only), 'markup' (razor, html: also @* *@ and <!-- -->) or 'code' (cs, js, mjs, ts).
function kindOf(rel) {
  if (/\.css$/i.test(rel)) return 'css';
  if (/\.(?:razor|cshtml|html?)$/i.test(rel)) return 'markup';
  return 'code';
}

function stripLineComment(line) {
  const comment = /(?:^|\s)\/\//g;
  let match;
  while ((match = comment.exec(line)) !== null) {
    const before = line.slice(0, match.index);
    const quotes = before.match(/(?<!\\)["'`]/g);
    if ((quotes?.length ?? 0) % 2 === 0) return before; // not inside a string
  }
  return line;
}

export function stripComments(text, kind) {
  let out = text.replace(/(?<![\w*"'/])\/\*[\s\S]*?\*\//g, blank);
  if (kind === 'markup') out = out.replace(/@\*[\s\S]*?\*@/g, blank).replace(/<!--[\s\S]*?-->/g, blank);
  if (kind === 'css') return out;
  return out.split('\n').map(stripLineComment).join('\n');
}

const lineAt = (text, index) => text.slice(0, index).split('\n').length;
const fmt = ({ file, line, message }) => `${file ? `${file}${line ? `:${line}` : ''} ` : ''}${message}`;

// ---------------------------------------------------------------------------------------------
// Context: the allow-list, the report/enforce switch and the test-id list
// ---------------------------------------------------------------------------------------------

function loadAllow(root) {
  const loaded = readJson(root, ALLOW_FILE);
  const allow = {};
  const errors = [];
  if (loaded.missing) return { allow, errors };
  if (loaded.error) return { allow, errors: [`is not valid JSON: ${loaded.error}`] };
  if (!isObject(loaded.data)) return { allow, errors: ['must be a JSON object: guard name -> list of entries'] };
  for (const [guard, entries] of Object.entries(loaded.data)) {
    if (!ALLOWABLE.includes(guard)) {
      errors.push(`'${guard}' is not a guard that takes exceptions (${ALLOWABLE.join(', ')})`);
    } else if (!Array.isArray(entries)) {
      errors.push(`${guard}: must be a list of entries`);
    } else {
      entries.forEach((entry, i) => {
        const label = `${guard}[${i}]`;
        if (!isObject(entry) || typeof entry.file !== 'string' || entry.file === '') errors.push(`${label}: needs a 'file'`);
        else if (typeof entry.reason !== 'string' || entry.reason.trim() === '') errors.push(`${label} (${entry.file}): needs a non-empty 'reason'`);
        else if (entry.contains !== undefined && (typeof entry.contains !== 'string' || entry.contains === '')) errors.push(`${label} (${entry.file}): 'contains' must be a non-empty string`);
        else (allow[guard] ??= []).push(entry);
      });
    }
  }
  return { allow, errors };
}

// { mode: 'report'|'enforce', enforced: ['AC-nn', ...], error? }
function loadScope(root) {
  const loaded = readJson(root, SCOPE_FILE);
  if (loaded.missing) return { mode: 'report', enforced: [] };
  if (loaded.error) return { mode: 'report', enforced: [], error: `${SCOPE_FILE} is not valid JSON: ${loaded.error}` };
  const { mode = 'report', enforced = [] } = isObject(loaded.data) ? loaded.data : {};
  if (mode !== 'report' && mode !== 'enforce') return { mode: 'report', enforced: [], error: `${SCOPE_FILE}: mode must be "report" or "enforce"` };
  const valid = Array.isArray(enforced) && enforced.every((id) => typeof id === 'string' && acNumber(id.replace(/^AC-/, '')) !== null);
  if (!valid) return { mode, enforced: [], error: `${SCOPE_FILE}: enforced must be a list of ids AC-01..AC-${AC_COUNT}` };
  return { mode, enforced };
}

// 'AC-nn' (nn = 2 digits, 01..50) -> number, otherwise null.
function acNumber(digits) {
  if (!/^\d{2}$/.test(digits)) return null;
  const n = Number(digits);
  return n >= 1 && n <= AC_COUNT ? n : null;
}
const acLabel = (n) => `AC-${String(n).padStart(2, '0')}`;

function makeContext(root) {
  const { allow, errors } = loadAllow(root);
  return {
    root,
    allowErrors: errors,
    scope: loadScope(root),
    allowed(guard, file, lineText) {
      return (allow[guard] ?? []).some((entry) => entry.file === file && (entry.contains === undefined || lineText.includes(entry.contains)));
    },
  };
}

// ---------------------------------------------------------------------------------------------
// Line scanners shared by the source guards
// ---------------------------------------------------------------------------------------------

// rules: [{ re, message, files? }]; returns the fails of one file (comments skipped, allow-list honoured). A rule with `files` (a RegExp over the
// relative path) only looks at the files it matches.
function scanFile(ctx, guard, rel, rules) {
  const text = readText(ctx.root, rel);
  const rawLines = text.split('\n');
  const fails = [];
  stripComments(text, kindOf(rel)).split('\n').forEach((line, i) => {
    for (const rule of rules) {
      if (rule.files && !rule.files.test(rel)) continue;
      if (rule.re.test(line) && !ctx.allowed(guard, rel, rawLines[i])) fails.push({ file: rel, line: i + 1, message: rule.message });
    }
  });
  return fails;
}

function scanSources(ctx, guard, accepts, rules) {
  const files = walk(ctx.root, 'src', sourceSkip).filter(accepts);
  return { fails: files.flatMap((rel) => scanFile(ctx, guard, rel, rules)), checked: files.length };
}

const SOURCE_TEXT = /\.(?:razor|cshtml|html?|cs|js|mjs|css)$/i;
// Where JavaScript lives (script files and inline <script> in markup) and where CSS does (stylesheets and inline <style> or style="" in markup).
const SCRIPT_SOURCE = /\.(?:js|mjs|razor|cshtml|html?)$/i;
const STYLE_SOURCE = /\.(?:css|razor|cshtml|html?)$/i;

// ---------------------------------------------------------------------------------------------
// The guards (03 section 7.3). Each returns { fails, reports?, checked, note? }.
// ---------------------------------------------------------------------------------------------

function configName(ctx) {
  const files = walk(ctx.root, '', treeSkip);
  const fails = files
    .filter((rel) => /^config\.(?:yaml|yml|json)$/.test(path.posix.basename(rel)) && rel !== ALLOWED_CONFIG)
    .map((rel) => ({ file: rel, message: 'would register a second app with the Supervisor; only realm/config.yaml may be named config.yaml, config.yml or config.json' }));
  return { fails, checked: files.length };
}

function addonValidate(ctx) {
  const { problems, notes } = validateAddon(ctx.root);
  return { fails: problems, checked: 1, note: notes.join('; ') };
}

function enumProblems(value, label, out) {
  if (typeof value === 'string') {
    const list = /^list\((.*)\)\??$/.exec(value);
    for (const alternative of list ? list[1].split('|') : []) {
      if (alternative !== alternative.toLowerCase()) out.push(`${label}: enumerated value '${alternative}' is not lower-case`);
    }
  } else if (Array.isArray(value)) {
    value.forEach((item) => enumProblems(item, `${label}[]`, out));
  } else if (isObject(value)) {
    for (const [key, item] of Object.entries(value)) enumProblems(item, `${label}.${key}`, out);
  }
}

function optionBindings(ctx) {
  const configFile = ALLOWED_CONFIG;
  const translationsFile = 'realm/translations/en.yaml';
  const config = readYaml(path.join(ctx.root, configFile));
  if (config.missing || config.error || !isObject(config.data)) return { fails: [], checked: 0 }; // addon-validate reports it
  const schema = isObject(config.data.schema) ? config.data.schema : {};
  const options = isObject(config.data.options) ? config.data.options : {};
  const schemaKeys = Object.keys(schema);
  const fails = [];

  const translations = readYaml(path.join(ctx.root, translationsFile));
  if (translations.missing) {
    fails.push({ file: translationsFile, message: 'is missing' });
  } else if (translations.error) {
    fails.push({ file: translationsFile, message: `is not valid YAML: ${translations.error}` });
  } else {
    const translated = Object.keys(isObject(translations.data.configuration) ? translations.data.configuration : {});
    for (const key of schemaKeys) {
      if (!translated.includes(key)) fails.push({ file: configFile, line: lineOfKey(config.text, key, 'schema'), message: `schema key '${key}' has no entry under configuration in translations/en.yaml` });
    }
    for (const key of translated) {
      if (!(key in schema)) fails.push({ file: translationsFile, line: lineOfKey(translations.text, key, 'configuration'), message: `translation '${key}' is not a schema key` });
    }
  }

  const bindings = readJson(ctx.root, BINDINGS_FILE);
  if (bindings.missing) {
    fails.push({ file: BINDINGS_FILE, message: 'is missing (the machine-readable copy of 02 section 3.4)' });
  } else if (bindings.error || !isObject(bindings.data) || !Array.isArray(bindings.data.keys)) {
    fails.push({ file: BINDINGS_FILE, message: `must be JSON with a "keys" list${bindings.error ? ` (${bindings.error})` : ''}` });
  } else {
    const bound = bindings.data.keys;
    for (const key of schemaKeys) if (!bound.includes(key)) fails.push({ file: BINDINGS_FILE, message: `schema key '${key}' is not in "keys"` });
    for (const key of bound) if (!(key in schema)) fails.push({ file: BINDINGS_FILE, message: `"keys" entry '${key}' is not a schema key` });
  }

  for (const [key, type] of Object.entries(schema)) {
    if (typeof type === 'string' && type.endsWith('?') && key in options) {
      fails.push({ file: configFile, line: lineOfKey(config.text, key, 'options'), message: `optional option '${key}' must not appear in options (the Supervisor would treat it as required, R-068)` });
    }
    const lowerCase = [];
    enumProblems(type, key, lowerCase);
    for (const message of lowerCase) fails.push({ file: configFile, line: lineOfKey(config.text, key, 'schema'), message: `${message} (R-069)` });
  }
  return { fails, checked: schemaKeys.length };
}

function noLeadingSlash(ctx) {
  return scanSources(ctx, 'no-leading-slash', (rel) => SOURCE_TEXT.test(rel), [
    // HTML attribute names are case-insensitive (CR2-005): HREF="/x" breaks behind Ingress like href="/x".
    { re: /\b(?:href|src)=\\?["']\//i, message: 'href="/ or src="/ starts at the server root and breaks behind Ingress; use a relative URL' },
    // The same mistake in scripts and styles (CR2-005): a leading slash in a module specifier, a fetch, a worker or a stylesheet url().
    { re: /\bimport\(\s*["'`]\//, files: SCRIPT_SOURCE, message: 'import("/...") starts at the server root and breaks behind Ingress; use a URL relative to the module (import.meta.url) or the page' },
    { re: /\bfetch\(\s*["'`]\//, files: SCRIPT_SOURCE, message: 'fetch("/...") starts at the server root and breaks behind Ingress; use a relative URL' },
    { re: /\bnew\s+(?:Shared)?Worker\(\s*["'`]\//, files: SCRIPT_SOURCE, message: 'new Worker("/...") starts at the server root and breaks behind Ingress; use a relative URL' },
    { re: /\burl\(\s*["']?\//i, files: STYLE_SOURCE, message: 'url(/...) starts at the server root and breaks behind Ingress; use a URL relative to the stylesheet' },
    { re: /\bNavigateTo\(\s*[$@]*"\//, message: 'NavigateTo("/...") starts at the server root; use a relative URL' },
    { re: /\b(?:Results|Response)\.Redirect\(/, message: 'Results.Redirect( and Response.Redirect( are banned; use Results.LocalRedirect with the PathBase' },
    { re: /\bnew\s+Uri\((?:[^()]|\([^()]*\))*,\s*[$@]*"\//, message: 'new Uri(..., "/...") starts at the server root; use a relative URL' },
  ]);
}

function noStaticFiles(ctx) {
  return scanSources(ctx, 'no-static-files', (rel) => SOURCE_TEXT.test(rel), [
    { re: /\bUse(?:Realm)?StaticFiles\b/, message: 'UseStaticFiles and UseRealmStaticFiles are not used; MapStaticAssets serves every file (5.3)' },
    { re: /\bAssets\[\s*[$@]*"(?:\.\/)?(?:js|lib|css|fonts|img)\//, message: '@Assets[...] fingerprints the name of our own file; use a plain relative URL (5.3)' },
  ]);
}

function noWallclock(ctx) {
  return scanSources(ctx, 'no-wallclock', (rel) => /\.(?:cs|razor)$/i.test(rel), [
    { re: /\b(?:DateTime|DateTimeOffset)\.(?:Now|UtcNow)\b|\bTimeZoneInfo\.Local\b|\bStopwatch\.GetTimestamp\b/, message: 'reads the wall clock; take a TimeProvider (2.12)' },
  ]);
}

function noColourLiterals(ctx) {
  const accepts = (rel) => /\.razor(?:\.css)?$/i.test(rel) || /(?:^|\/)wwwroot\/css\/.+\.css$/i.test(rel) || /(?:^|\/)wwwroot\/js\/.+\.js$/i.test(rel);
  return scanSources(ctx, 'no-colour-literals', accepts, [
    { re: /(?<![\w&])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])/, message: 'colour literal; use a var(--realm-*) token (3.8)' },
    { re: /\brgba?\s*\(/, message: 'rgb() colour literal; use a var(--realm-*) token (3.8)' },
  ]);
}

function vendoredMaplibre(ctx) {
  const dir = path.join(ctx.root, MAPLIBRE_DIR);
  if (!fs.existsSync(dir)) return { fails: [], checked: 0 };
  const fails = [];
  const sumsFile = `${MAPLIBRE_DIR}/SHA256SUMS`;
  let sums;
  try {
    sums = readText(ctx.root, sumsFile);
  } catch {
    return { fails: [{ file: sumsFile, message: 'is missing' }], checked: 1 };
  }
  let checked = 0;
  for (const line of sums.split(/\r?\n/)) {
    const entry = /^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$/.exec(line);
    if (!entry) continue;
    checked += 1;
    const file = `${MAPLIBRE_DIR}/${entry[2]}`;
    let bytes;
    try {
      bytes = fs.readFileSync(path.join(ctx.root, file));
    } catch {
      fails.push({ file, message: 'is listed in SHA256SUMS but missing' });
      continue;
    }
    const actual = crypto.createHash('sha256').update(bytes).digest('hex');
    if (actual !== entry[1].toLowerCase()) fails.push({ file, message: `sha256 ${actual} differs from SHA256SUMS (${entry[1].toLowerCase()}); vendored files are replaced as a unit` });
  }
  if (checked === 0) fails.push({ file: sumsFile, message: 'lists no files' });

  const versionFile = `${MAPLIBRE_DIR}/VERSION.txt`;
  let vendored;
  try {
    vendored = /\bmaplibre-gl\s+v?(\S+)/.exec(readText(ctx.root, versionFile))?.[1];
  } catch {
    fails.push({ file: versionFile, message: 'is missing' });
  }
  if (vendored !== undefined) {
    const packageJson = readJson(ctx.root, 'package.json');
    const declared = isObject(packageJson.data) ? (packageJson.data.devDependencies?.['maplibre-gl'] ?? packageJson.data.dependencies?.['maplibre-gl']) : undefined;
    const pinned = typeof declared === 'string' ? declared.replace(/^[\^~=v]+/, '') : undefined;
    if (pinned !== vendored) fails.push({ file: versionFile, message: `says maplibre-gl ${vendored} but package.json has ${pinned === undefined ? 'no maplibre-gl' : pinned}` });
  } else if (!fails.some((f) => f.file === versionFile)) {
    fails.push({ file: versionFile, message: 'does not say "maplibre-gl <version>"' });
  }
  return { fails, checked: Math.max(checked, 1) };
}

function fontBudget(ctx) {
  const files = walk(ctx.root, 'src', sourceSkip);
  const fails = [];
  const fonts = files.filter((rel) => /(?:^|\/)wwwroot\/fonts\/[^/]+\.woff2$/i.test(rel));
  const total = fonts.reduce((sum, rel) => sum + fs.statSync(path.join(ctx.root, rel)).size, 0);
  if (total > FONT_BUDGET_BYTES) fails.push({ file: 'src', message: `the .woff2 fonts total ${total} bytes, over the budget of ${FONT_BUDGET_BYTES} (AC-43)` });
  for (const dir of new Set(fonts.map((rel) => path.posix.dirname(rel)))) {
    if (!files.some((rel) => path.posix.dirname(rel) === dir && /ofl/i.test(path.posix.basename(rel)))) {
      fails.push({ file: dir, message: 'holds fonts but no OFL text (a file with OFL in its name); each font ships its licence' });
    }
  }
  const sources = files.filter((rel) => SOURCE_TEXT.test(rel));
  for (const rel of sources) {
    fails.push(...scanFile(ctx, 'font-budget', rel, [{ re: /fonts\.(?:googleapis|gstatic)\.com/i, message: 'a Google Fonts host; fonts are self-hosted (AC-43)' }]));
  }
  return { fails, checked: fonts.length + sources.length };
}

const EXACT_VERSION = /^\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/;

function packagePins(ctx) {
  let allowedIds = new Set();
  try {
    allowedIds = new Set(readText(ctx.root, PACKAGES_FILE).split(/\r?\n/).map((line) => line.replace(/#.*/, '').trim().toLowerCase()).filter(Boolean));
  } catch {
    // no list: every package is reported below as not allowed
  }
  const fails = [];
  let checked = 0;
  for (const rel of walk(ctx.root, '', treeSkip).filter((f) => /\.(?:csproj|props|targets)$/i.test(f))) {
    const original = readText(ctx.root, rel);
    const rawLines = original.split('\n');
    const text = original.replace(/<!--[\s\S]*?-->/g, blank);
    for (const element of text.matchAll(/<(PackageReference|PackageVersion)\b([^>]*?)\/?>/g)) {
      checked += 1;
      const line = lineAt(text, element.index);
      if (ctx.allowed('package-pins', rel, rawLines[line - 1] ?? '')) continue;
      const id = /\b(?:Include|Update)\s*=\s*"([^"]*)"/.exec(element[2])?.[1];
      const version = /\bVersion\s*=\s*"([^"]*)"/.exec(element[2])?.[1];
      const add = (message) => fails.push({ file: rel, line, message });
      if (id !== undefined && !allowedIds.has(id.toLowerCase())) add(`package '${id}' is not in ${PACKAGES_FILE} (a new package needs a decision)`);
      if (element[1] === 'PackageReference') {
        if (version !== undefined) add(`PackageReference '${id}' carries Version=; versions live in Directory.Packages.props`);
      } else if (version === undefined || !EXACT_VERSION.test(version)) {
        add(`PackageVersion '${id}' must be an exact version, found ${version === undefined ? 'none' : `'${version}'`} (no range, no wildcard)`);
      } else if (id?.toLowerCase() === 'mudblazor' && version !== MUDBLAZOR_VERSION) {
        add(`MudBlazor is pinned to ${MUDBLAZOR_VERSION} (D15), found ${version}; moving it needs a recorded decision`);
      }
    }
  }
  return { fails, checked };
}

function testidContract(ctx) {
  const loaded = readJson(ctx.root, TESTIDS_FILE);
  if (loaded.missing) return { fails: [], checked: 0 };
  const ids = isObject(loaded.data) ? loaded.data.testids : undefined;
  if (loaded.error || !Array.isArray(ids) || ids.some((id) => typeof id !== 'string' || id === '' || id.startsWith('{'))) {
    return { fails: [{ file: TESTIDS_FILE, message: `must be JSON { "testids": [ "id" | "prefix-{pattern}", ... ] }${loaded.error ? ` (${loaded.error})` : ''}` }], checked: 1 };
  }
  if (ctx.scope.error) return { fails: [{ message: ctx.scope.error }], checked: 1 };
  if (ids.length === 0) return { fails: [], checked: 0 };
  const sources = walk(ctx.root, 'src', sourceSkip).filter((rel) => /\.(?:razor|cshtml|html?|cs|js|mjs)$/i.test(rel));
  const text = sources.map((rel) => stripComments(readText(ctx.root, rel), kindOf(rel))).join('\n');
  const missing = ids.filter((id) => {
    const brace = id.indexOf('{');
    const literal = escapeRegExp(brace === -1 ? id : id.slice(0, brace));
    return !new RegExp(brace === -1 ? `["'\`]${literal}["'\`]` : `["'\`]${literal}`).test(text);
  });
  // Report-only until S15 flips tools/ci/ac-scope.json to "enforce".
  const findings = missing.map((id) => ({ message: `data-testid '${id}' is not present in src/` }));
  return ctx.scope.mode === 'enforce' ? { fails: findings, checked: ids.length } : { fails: [], reports: findings, checked: ids.length };
}

const CS_TITLE = /\bDisplayName\s*=\s*\$?@?"((?:[^"\\\n]|\\.|"")*)"/g;
const TS_TITLE = /\b(?:test|it)(?:\.fail)?\s*\(\s*(['"`])((?:\\.|(?!\1)[^\\\n])*)\1/g;

function acCoverage(ctx) {
  if (ctx.scope.error) return { fails: [{ message: ctx.scope.error }], checked: 1 };
  const files = walk(ctx.root, 'tests', sourceSkip).filter((rel) => /\.(?:cs|ts|mts|js|mjs)$/i.test(rel));
  if (files.length === 0) return { fails: [], checked: 0 };
  const found = new Set();
  const unknown = [];
  for (const rel of files) {
    const text = stripComments(readText(ctx.root, rel), 'code');
    const titles = [...text.matchAll(rel.endsWith('.cs') ? CS_TITLE : TS_TITLE)].map((match) => match[rel.endsWith('.cs') ? 1 : 2]);
    for (const title of titles) {
      for (const token of title.matchAll(/\[AC-(\d+)[a-z]?\]/g)) {
        const n = acNumber(token[1]);
        if (n === null) unknown.push({ rel, token: token[0] });
        else found.add(n);
      }
    }
  }
  const enforced = new Set(ctx.scope.enforced);
  const fails = [];
  const reports = [];
  const enforce = ctx.scope.mode === 'enforce';
  for (let n = 1; n <= AC_COUNT; n += 1) {
    if (found.has(n)) continue;
    const target = enforce || enforced.has(acLabel(n)) ? fails : reports;
    target.push({ message: `${acLabel(n)} missing` });
  }
  for (const { rel, token } of unknown) {
    (enforce ? fails : reports).push({ file: rel, message: `${token} matches no row of 01 section 11 (AC-01..AC-${AC_COUNT})` });
  }
  return { fails, reports, checked: files.length };
}

const GUARDS = {
  'config-name': configName,
  'addon-validate': addonValidate,
  'option-bindings': optionBindings,
  'no-leading-slash': noLeadingSlash,
  'no-static-files': noStaticFiles,
  'no-wallclock': noWallclock,
  'no-colour-literals': noColourLiterals,
  'vendored-maplibre': vendoredMaplibre,
  'font-budget': fontBudget,
  'package-pins': packagePins,
  'testid-contract': testidContract,
  'ac-coverage': acCoverage,
};

// ---------------------------------------------------------------------------------------------
// Running and printing
// ---------------------------------------------------------------------------------------------

function render(name, result) {
  const fails = result.fails ?? [];
  const reports = result.reports ?? [];
  const lines = fails.slice(0, MAX_FAIL_LINES).map((item) => `FAIL ${name}: ${fmt(item)}`);
  if (fails.length > MAX_FAIL_LINES) lines.push(`FAIL ${name}: ... and ${fails.length - MAX_FAIL_LINES} more`);
  for (const item of reports) lines.push(`REPORT ${name}: ${fmt(item)}`);
  if (fails.length === 0 && reports.length === 0) {
    lines.push(result.checked > 0 ? `PASS ${name}${result.note ? ` (${result.note})` : ''}` : `PASS ${name} (nothing to check yet)`);
  }
  return { lines, failed: fails.length > 0 };
}

// Runs the guards against the repository at `root`; returns { lines, failed }.
export function runGuards(root = DEFAULT_ROOT) {
  const ctx = makeContext(root);
  const lines = ctx.allowErrors.map((message) => `FAIL allow-list: ${ALLOW_FILE} ${message}`);
  let failed = ctx.allowErrors.length > 0;
  for (const name of GUARD_NAMES) {
    let result;
    try {
      result = GUARDS[name](ctx);
    } catch (err) {
      result = { fails: [{ message: `internal error: ${err instanceof Error ? err.message : err}` }] };
    }
    const rendered = render(name, result);
    lines.push(...rendered.lines);
    failed ||= rendered.failed;
  }
  return { lines, failed };
}

export function main(argv) {
  let root = DEFAULT_ROOT;
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--root' && i + 1 < argv.length) {
      root = path.resolve(argv[(i += 1)]);
    } else {
      process.stderr.write(`guards: unknown argument '${argv[i]}'\nusage: guards.mjs [--root <repository root>]\n`);
      return 64;
    }
  }
  const { lines, failed } = runGuards(root);
  process.stdout.write(`${lines.join('\n')}\n`);
  return failed ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}
