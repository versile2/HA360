#!/usr/bin/env node
// make-summary.mjs: writes SUMMARY.md (and errors.log, build.tail.log, tests/*.trx) for one CI run.
// 03 section 7.4, 04 card S0a. Node built-ins only (no npm install runs in publish-ci, R2-024).
//
//   node tools/ci/make-summary.mjs --in ci-in --out ci-out --needs '<toJSON(needs)>'
//
// --in     folder holding the downloaded artifacts (one sub-folder per job); it is searched
//          recursively for errors.log, restore.log, build.log, guards.log, smoke.json, *.trx, results.json (Playwright), js-tests.tap,
//          tsc.log, styles.log, contract.tap, app.log, and the PNGs under a folder named shots. May be missing or empty.
// --out    folder that receives the files above (default ci-out).
// --needs  the JSON of the workflow's `needs` context: { "<job>": { "result": "success", ... } }.
//          Without it the verdict comes from the logs alone.
// --root   workspace path stripped from file names (default $GITHUB_WORKSPACE).
// --branch --sha --run --url   override GITHUB_REF_NAME, GITHUB_SHA, GITHUB_RUN_NUMBER and the run URL.
//
// The SUMMARY.md header ("- result:", "- branch:", "- sha:", "- run:", "- url:") is read by
// publish-ci-artifacts.sh and wait-for-ci.sh: keep those lines as they are.
// Exit: 0 (also when an input is missing or this script itself fails: the crash is written into
// SUMMARY.md as a failed run so the author can read it), 64 bad command line, 1 cannot write --out.

import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const MAX_ERRORS = 40;
export const MAX_WARNINGS = 20;
export const MAX_MESSAGE_LINES = 15;
export const RESTORE_TAIL_LINES = 40;
export const BUILD_TAIL_LINES = 300;
export const GUARDS_JOB = 'guards';
export const DOCKER_SMOKE_JOB = 'docker-smoke';
export const E2E_JOB = 'e2e';
export const AC_COUNT = 50; // AC-01..AC-50 (01 section 11, numbers never change)
export const MAX_FAILURE_SHOTS = 20; // 03 section 7.4: the first 20 failing-test screenshots
export const APP_LOG_TAIL_LINES = 30;
export const APP_LOG_KEPT_LINES = 300;
export const MAX_CELL_CHARS = 400;
export const MAX_REPORT_EXAMPLES = 3;
export const MAX_OTHER_GUARD_LINES = 15;
export const MIN_RANGE = 3;
export const JS_JOB = 'js';
export const MAX_JS_DETAIL_LINES = 40; // lines of the YAML block shown per failing JS test
export const MAX_JS_FAILURES = 50; // failing JS tests shown in SUMMARY.md (the rest are in js/js-tests.tap)
export const MAX_TYPE_ERRORS = 50;
export const TSC_TAIL_LINES = 20;
export const JS_KEPT_BYTES = 400 * 1024; // js/js-tests.tap, js/tsc.log and js/styles.log are published cut to their last 400 KB
export const MAX_STYLE_FAILS = 40; // FAIL lines of styles.log shown in SUMMARY.md (the rest are in js/styles.log)
export const MAX_STYLE_WARNINGS = 5; // WARN lines of styles.log shown in SUMMARY.md, "listed briefly"
export const MAX_PARTIAL_LISTED = 20; // skipped tests listed under the AC matrix

// ---------------------------------------------------------------------------------------------
// Pure helpers (exported for tests/js/make-summary.test.mjs)
// ---------------------------------------------------------------------------------------------

export function splitLines(text) {
  return text.split(/\r?\n/);
}

export function tailLines(text, count) {
  const lines = splitLines(text);
  while (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
  return lines.slice(-count);
}

function stripRoot(location, root) {
  if (!root) return location;
  const prefix = root.replace(/\/+$/, '') + '/';
  return location.startsWith(prefix) ? location.slice(prefix.length) : location;
}

// One regular expression per diagnostic kind, built once (CR1-015): a build log has thousands of lines.
const diagnosticRe = (kind) => new RegExp(`^(.*?): (?:fatal )?${kind} ([A-Za-z]+\\d+): (.*)$`);
const ERROR_RE = diagnosticRe('error');
const WARNING_RE = diagnosticRe('warning');
const DIAGNOSTIC_RE = (kind) => (kind === 'error' ? ERROR_RE : kind === 'warning' ? WARNING_RE : diagnosticRe(kind));
const NODE_PREFIX_RE = /^\s*\d+(?::\d+)?>/;
const PROJECT_SUFFIX_RE = /\s+\[[^[\]]*\.(?:csproj|slnx|sln|proj|props|targets)\]\s*$/;

// "/work/src/X.cs(3,1): error CS1002: ; expected [/work/src/X.csproj]" -> "src/X.cs(3,1): CS1002 ; expected"
// A line that is not an MSBuild diagnostic is returned unchanged (nothing is dropped); blank lines give null.
export function normalizeDiagnostic(rawLine, kind, root) {
  // MSBuild's file logger prefixes lines with the node that wrote them ("   1:7>", "   2>"): drop it, so the
  // same error reported by two nodes is one error.
  const line = rawLine.replace(NODE_PREFIX_RE, '');
  if (line.trim() === '') return null;
  const match = DIAGNOSTIC_RE(kind).exec(line);
  if (!match) return line.trimEnd();
  const location = stripRoot(match[1].trim(), root);
  const message = match[3].replace(PROJECT_SUFFIX_RE, '').trimEnd();
  return `${location}: ${match[2]} ${message}`;
}

// Distinct normalized diagnostics, in order of first appearance.
export function distinctDiagnostics(texts, kind, root) {
  const seen = new Set();
  const result = [];
  for (const text of texts) {
    for (const line of splitLines(text)) {
      const normalized = normalizeDiagnostic(line, kind, root);
      if (normalized === null || seen.has(normalized)) continue;
      seen.add(normalized);
      result.push(normalized);
    }
  }
  return result;
}

// Warnings only: lines that really are "warning CODE:" diagnostics (unlike errors, nothing else is kept).
export function distinctWarnings(texts, root) {
  const warningRe = WARNING_RE;
  const seen = new Set();
  const result = [];
  for (const text of texts) {
    for (const line of splitLines(text)) {
      if (!warningRe.test(line.replace(NODE_PREFIX_RE, ''))) continue;
      const normalized = normalizeDiagnostic(line, 'warning', root);
      if (normalized === null || seen.has(normalized)) continue;
      seen.add(normalized);
      result.push(normalized);
    }
  }
  return result;
}

export function decodeXml(text) {
  return text
    .replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g, '$1')
    .replace(/&(#x[0-9a-fA-F]+|#[0-9]+|lt|gt|amp|quot|apos);/g, (whole, entity) => {
      if (entity.startsWith('#x')) return String.fromCodePoint(parseInt(entity.slice(2), 16));
      if (entity.startsWith('#')) return String.fromCodePoint(parseInt(entity.slice(1), 10));
      return { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" }[entity];
    });
}

const FAILED_OUTCOMES = new Set(['Failed', 'Error', 'Timeout', 'Aborted']);
const PASSED_OUTCOMES = new Set(['Passed', 'PassedButRunAborted', 'Completed']);

// A regular expression over <UnitTestResult ... outcome="Failed"> and its <Message>, not an XML parser.
export function parseTrx(xml) {
  const resultRe = /<UnitTestResult\b([^>]*?)(?:\/>|>([\s\S]*?)<\/UnitTestResult>)/g;
  const counts = { passed: 0, failed: 0, skipped: 0 };
  const failures = [];
  let match;
  while ((match = resultRe.exec(xml)) !== null) {
    const attributes = match[1];
    const body = match[2] ?? '';
    const outcome = /\boutcome="([^"]*)"/.exec(attributes)?.[1] ?? '';
    if (FAILED_OUTCOMES.has(outcome)) {
      counts.failed += 1;
      const name = decodeXml(/\btestName="([^"]*)"/.exec(attributes)?.[1] ?? '(unnamed test)');
      const message = /<Message>([\s\S]*?)<\/Message>/.exec(body)?.[1];
      failures.push({ name, outcome, message: message === undefined ? '(no message in the .trx)' : decodeXml(message) });
    } else if (PASSED_OUTCOMES.has(outcome)) {
      counts.passed += 1;
    } else {
      counts.skipped += 1;
    }
  }
  // Cross-check against the run's own counters: if the regex above missed results (an unexpected layout), say so.
  const counters = /<Counters\b[^>]*>/.exec(xml)?.[0] ?? '';
  const countersFailed = ['failed', 'error', 'timeout', 'aborted'].reduce(
    (sum, name) => sum + Number(new RegExp(`\\b${name}="(\\d+)"`).exec(counters)?.[1] ?? 0),
    0,
  );
  return { ...counts, failures, countersFailed };
}

function firstLines(text, count) {
  const lines = splitLines(text.trimEnd());
  if (lines.length <= count) return lines.join('\n');
  return `${lines.slice(0, count).join('\n')}\n... (${lines.length - count} more lines)`;
}

function fenced(text) {
  return '```text\n' + text + '\n```';
}

// ---------------------------------------------------------------------------------------------
// guards.log: `PASS name [(note)]`, `FAIL name: file:line message`, `REPORT name: finding` (tools/ci/guards.mjs)
// ---------------------------------------------------------------------------------------------

const GUARD_LINE_RE = /^(PASS|FAIL|REPORT)\s+([^\s:]+)(.*)$/;
const REPORT_ID_RE = /^([A-Za-z][A-Za-z0-9]*)-(\d+)\s+(\S.*)$/;

// -> { fails: [{ name, line }], reports: Map(name -> [finding]), passes: [name], names: [every guard name, in order], other: [line] }
// `other` holds what is not a guard line (a stack trace when the script crashed, say); blank lines are dropped.
export function parseGuardsLog(text) {
  const parsed = { fails: [], reports: new Map(), passes: [], names: [], other: [] };
  for (const raw of splitLines(text)) {
    const line = raw.trimEnd();
    if (line.trim() === '') continue;
    const match = GUARD_LINE_RE.exec(line);
    if (!match) {
      parsed.other.push(line);
      continue;
    }
    const [, kind, name, rest] = match;
    if (!parsed.names.includes(name)) parsed.names.push(name);
    if (kind === 'FAIL') {
      parsed.fails.push({ name, line });
    } else if (kind === 'REPORT') {
      const finding = /^\s*:/.test(rest) ? rest.replace(/^\s*:\s*/, '') : rest.trim();
      if (!parsed.reports.has(name)) parsed.reports.set(name, []);
      parsed.reports.get(name).push(finding);
    } else if (!parsed.passes.includes(name)) {
      parsed.passes.push(name);
    }
  }
  return parsed;
}

// One line per guard: findings of the form "<PREFIX>-<number> <status>" are grouped per status and their consecutive
// numbers are written as ranges ("50 AC ids missing (AC-01 ... AC-50)"); any other finding is kept as written
// (the first few, when there are many).
export function reportLine(name, findings) {
  const groups = new Map();
  const loose = [];
  for (const finding of findings) {
    const match = REPORT_ID_RE.exec(finding);
    if (!match) {
      if (!loose.includes(finding)) loose.push(finding);
      continue;
    }
    const key = `${match[1]}\u0000${match[3]}`;
    if (!groups.has(key)) groups.set(key, { prefix: match[1], status: match[3], entries: new Map() });
    groups.get(key).entries.set(Number(match[2]), `${match[1]}-${match[2]}`);
  }
  const parts = [];
  for (const { prefix, status, entries } of groups.values()) {
    const numbers = [...entries.keys()].sort((a, b) => a - b);
    const ranges = [];
    for (let i = 0; i < numbers.length; ) {
      let end = i;
      while (end + 1 < numbers.length && numbers[end + 1] === numbers[end] + 1) end += 1;
      if (end - i + 1 >= MIN_RANGE) ranges.push(`${entries.get(numbers[i])} \u2026 ${entries.get(numbers[end])}`);
      else for (let k = i; k <= end; k += 1) ranges.push(entries.get(numbers[k]));
      i = end + 1;
    }
    parts.push(`${numbers.length} ${prefix} id${numbers.length === 1 ? '' : 's'} ${status} (${ranges.join(', ')})`);
  }
  if (loose.length === 1) {
    parts.push(loose[0]);
  } else if (loose.length > 1) {
    const shown = loose.slice(0, MAX_REPORT_EXAMPLES).join('; ');
    parts.push(`${loose.length} findings: ${shown}${loose.length > MAX_REPORT_EXAMPLES ? `; ... and ${loose.length - MAX_REPORT_EXAMPLES} more` : ''}`);
  }
  return `REPORT ${name}: ${parts.join('; ')}`;
}

// The "## Guards" section: every FAIL line as written, then one REPORT line per guard, then the PASS count.
export function guardsSection(parsed) {
  const lines = parsed.fails.map((f) => f.line);
  for (const [name, findings] of parsed.reports) lines.push(reportLine(name, findings));
  if (parsed.other.length > 0) lines.push(...firstLines(parsed.other.join('\n'), MAX_OTHER_GUARD_LINES).split('\n'));
  const count = `PASS: ${parsed.passes.length} of ${parsed.names.length} guards.`;
  return ['## Guards', ...(lines.length > 0 ? [fenced(lines.join('\n'))] : []), count].join('\n\n');
}

// ---------------------------------------------------------------------------------------------
// smoke.json: the flat { "name": number|string } map written by tools/ci/image-smoke.sh (03 section 7.8). Keys:
//   image, image_size_bytes, app_layer_bytes, compressed_estimate_bytes, time_to_healthy_s,
//   item<N>, item<N>_title, item<N>_detail (PASS, WARN, SKIP or FAIL), failing_requests, container_log_tail, error.
// failing_requests is free text: the response headers of the requests that failed an item, and what the image holds for static
// web assets when the Blazor script was not served.
// Items are read generically, so a slice that appends item 6 or 8 to the script needs no edit here.
// ---------------------------------------------------------------------------------------------

const SMOKE_NON_FAILING = new Set(['PASS', 'WARN', 'SKIP']);

// The 03 section 6.5 table, in MB (10^6 bytes, the unit `docker images` prints). Shown next to each size; nothing enforces
// them before item 8 (S16a), so a size over a line is reported, never a failure of the run.
export const SMOKE_SIZE_ROWS = [
  { key: 'image_size_bytes', label: 'image size (uncompressed)', target: 330, warn: 360, fail: 450 },
  { key: 'app_layer_bytes', label: 'app layer (published output)', target: 35, warn: 45 },
  { key: 'compressed_estimate_bytes', label: 'compressed size (estimate: gzip of docker save)', target: 120, warn: 130, fail: 200 },
];

function smokeNumber(data, key) {
  const value = data[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

// -> { data, items: [{ n, status, title, detail }], failed: [item], error, requests, logTail, problem } or { unreadable } when the text is not a JSON object.
// `problem` is the one-line reason the run fails (null when the smoke is fine).
export function parseSmoke(text) {
  let data;
  try {
    data = JSON.parse(text);
  } catch (err) {
    return { unreadable: `smoke.json is not valid JSON (${err.message})`, problem: 'docker smoke: smoke.json is not valid JSON' };
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) {
    return { unreadable: 'smoke.json is not a JSON object', problem: 'docker smoke: smoke.json is not a JSON object' };
  }
  const items = [];
  for (const key of Object.keys(data)) {
    const match = /^item(\d+)$/.exec(key);
    if (!match) continue;
    items.push({
      n: Number(match[1]),
      status: String(data[key]).trim().toUpperCase(),
      title: String(data[`item${match[1]}_title`] ?? ''),
      detail: String(data[`item${match[1]}_detail`] ?? ''),
    });
  }
  items.sort((a, b) => a.n - b.n);
  const failed = items.filter((item) => !SMOKE_NON_FAILING.has(item.status));
  const error = typeof data.error === 'string' && data.error !== '' ? data.error : null;
  const requests = typeof data.failing_requests === 'string' && data.failing_requests.trim() !== '' ? data.failing_requests : null;
  const logTail = typeof data.container_log_tail === 'string' && data.container_log_tail.trim() !== '' ? data.container_log_tail : null;
  let problem = null;
  if (error) problem = `docker smoke: ${error}`;
  else if (failed.length > 0) problem = `docker smoke failed: ${failed.length === 1 ? 'item' : 'items'} ${failed.map((item) => item.n).join(', ')}`;
  else if (items.length === 0) problem = 'docker smoke: smoke.json holds no item result (the script stopped before its first item)';
  return { data, items, failed, error, requests, logTail, problem };
}

function tableCell(text) {
  const flat = String(text).replace(/\r?\n/g, ' ').replace(/\|/g, '\\|').trim();
  return flat.length > MAX_CELL_CHARS ? `${flat.slice(0, MAX_CELL_CHARS)}...` : flat;
}

function megabytes(bytes) {
  return `${(bytes / 1e6).toFixed(1)} MB`;
}

function sizeLine(row, bytes) {
  const mb = bytes / 1e6;
  let verdict = 'within the target';
  if (row.fail !== undefined && mb > row.fail) verdict = 'over the fail line';
  else if (mb > row.warn) verdict = 'over the warn line';
  else if (mb > row.target) verdict = 'above the target';
  const lines = `target at most ${row.target} MB, warn over ${row.warn} MB${row.fail === undefined ? '' : `, fail over ${row.fail} MB`}`;
  return `- ${row.label}: ${megabytes(bytes)}, ${verdict} (6.5: ${lines})`;
}

// The "## Docker smoke" section for a parsed smoke.json: sizes against the 6.5 table, time to healthy, one row per item.
export function smokeSection(smoke) {
  if (smoke.unreadable) return `## Docker smoke\n\n${smoke.unreadable}.`;
  const { data } = smoke;
  const lines = [];
  if (typeof data.image === 'string') lines.push(`- image: ${data.image}`);
  for (const row of SMOKE_SIZE_ROWS) {
    const bytes = smokeNumber(data, row.key);
    if (bytes !== null) lines.push(sizeLine(row, bytes));
  }
  const seconds = smokeNumber(data, 'time_to_healthy_s');
  lines.push(seconds === null ? '- time to healthy: not measured (no 200 from /healthz)' : `- time to healthy: ${seconds.toFixed(2)} s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)`);
  const parts = ['## Docker smoke', lines.join('\n')];
  if (smoke.error) parts.push(`The smoke script reported: ${smoke.error}`);
  if (smoke.items.length > 0) {
    const rows = smoke.items.map((item) => `| ${item.n} | ${tableCell(item.title)} | ${tableCell(item.status)} | ${tableCell(item.detail)} |`);
    parts.push(['| item | check | result | detail |', '|---|---|---|---|', ...rows].join('\n'));
    const count = (status) => smoke.items.filter((item) => item.status === status).length;
    parts.push(`Items: ${count('PASS')} PASS, ${count('WARN')} WARN, ${smoke.failed.length} FAIL, ${count('SKIP')} SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).`);
  }
  if (smoke.requests) parts.push(`Response headers of the failing requests, with what the image holds for static web assets:\n\n${fenced(smoke.requests.trimEnd())}`);
  if (smoke.logTail) parts.push(`Container log (last lines, kept because an item failed):\n\n${fenced(smoke.logTail.trimEnd())}`);
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------------------------
// The AC matrix (D68, 04 card S6a): AC-01..AC-50, each passed / partial / failed / skipped / flaky / missing, from every test title that
// carries [AC-nn] (a suffix such as [AC-49a] counts for AC-49): the .trx files (dotnet), Playwright's results.json (e2e) and the node TAP
// of the js job. Report-only until S15 (D50): it never changes the verdict.
// ---------------------------------------------------------------------------------------------

const AC_TOKEN_RE = /\[AC-(\d{2})([a-z]?)\]/g;
export const AC_STATUSES = ['passed', 'partial', 'failed', 'skipped', 'flaky', 'missing'];
const acLabel = (n) => `AC-${String(n).padStart(2, '0')}`;

// "[AC-13a] [AC-16a] pins" -> [{ n: 13, part: '13a' }, { n: 16, part: '16a' }]; ids outside AC-01..AC-50 are not criteria and are ignored
// (the ac-coverage guard reports them).
export function acTokens(title) {
  const found = [];
  for (const match of title.matchAll(AC_TOKEN_RE)) {
    const n = Number(match[1]);
    if (n >= 1 && n <= AC_COUNT) found.push({ n, part: `${match[1]}${match[2]}` });
  }
  return found;
}

// The tests of a .trx whose display name carries an AC token: [{ title, status }], status passed | failed | skipped. A separate pass from
// parseTrx, so that function keeps the shape the rest of the report relies on.
export function parseTrxAcTests(xml) {
  const resultRe = /<UnitTestResult\b([^>]*?)(?:\/>|>)/g;
  const tests = [];
  let match;
  while ((match = resultRe.exec(xml)) !== null) {
    const name = /\btestName="([^"]*)"/.exec(match[1])?.[1];
    if (name === undefined) continue;
    const title = decodeXml(name);
    if (acTokens(title).length === 0) continue;
    const outcome = /\boutcome="([^"]*)"/.exec(match[1])?.[1] ?? '';
    tests.push({ title, status: FAILED_OUTCOMES.has(outcome) ? 'failed' : PASSED_OUTCOMES.has(outcome) ? 'passed' : 'skipped' });
  }
  return tests;
}

const TAP_LINE_RE = /^(\s*)(not ok|ok)\s+\d+\s*-?\s*(.*)$/;
const TAP_DIRECTIVE_RE = /\s+#\s*(?:SKIP|TODO)\b.*$/i;
const TAP_YAML_START_RE = /^\s*---\s*$/;
const TAP_YAML_END_RE = /^\s*\.\.\.\s*$/;
const TAP_FILE_TITLE_RE = /\.[cm]?[jt]s$/;
const TAP_PATH_SEPARATOR = ' \u203a '; // the separator Playwright titles use between a describe and its test

const leadingSpaces = (line) => /^ */.exec(line)[0].length;

// The YAML diagnostic block of a "not ok" line: { lines, last } (`last` is the index of the last line it covers). A block that is not
// closed by "..." ends where a line that cannot belong to it begins (an entry or a counter at the same or a lower indent).
function tapYamlBlock(lines, at, indent) {
  const block = [];
  let last = at;
  for (let j = at + 1; j < lines.length; j += 1) {
    if (TAP_YAML_END_RE.test(lines[j])) {
      last = j;
      break;
    }
    if (lines[j].trim() !== '' && leadingSpaces(lines[j]) < indent + 2) break;
    block.push(lines[j]);
    last = j;
  }
  return { lines: block, last };
}

// The "# ..." lines node prints right before the "# Subtest: <file>" header of a test FILE that failed as a whole (a syntax error, a
// missing import, process.exit, an uncaught error after the last test): the reason is only there, the "not ok" entry says "test failed".
function tapFileComments(lines, at) {
  if (at < 1 || !/^# Subtest:/.test(lines[at - 1])) return [];
  const comments = [];
  for (let j = at - 2; j >= 0 && /^# /.test(lines[j]) && !/^# Subtest:/.test(lines[j]) && !/^# [a-z_]+ \d/.test(lines[j]); j -= 1) comments.unshift(lines[j].slice(2));
  return comments;
}

// Node's TAP output (`node --test --test-reporter=tap`): { tests: [{ title, status }], pass, fail, failures: [{ title, detail, cancelled }] }.
// `tests` has one entry per "ok" / "not ok" line at any depth, with the title of that line alone (the AC matrix reads it).
// `pass` and `fail` are the top-level summary counters ("# pass 5"), null when the output has none. Node counts neither suites nor
// cancelled tests in "fail", so a suite whose hook threw has "# fail 0": count `failures`, not the counter.
// `failures` is what went wrong, with each cause once:
//  - subtests are printed indented BEFORE the line of their parent, so a "not ok" line is attributed to its parent chain: the title is
//    "describe › test" (the parents' titles, outermost first, joined by the separator Playwright titles use);
//  - a parent that fails only because a subtest failed ("1 subtest failed", failureType subtestsFailed) is not listed: its subtest is;
//  - a test file that failed as a whole is a top-level entry titled with the file name; its detail starts with the "# ..." lines that
//    came before it (the error text and stack of the crash);
//  - `cancelled` marks a test node cancelled because its parent finished first (failureType cancelledByParent): a consequence, not a cause.
// `detail` is the YAML block that follows the "not ok" line (error, expected, actual, stack), at the indent of a top-level test, cut to
// `detailLines` lines (default MAX_MESSAGE_LINES).
export function parseTap(text, { detailLines = MAX_MESSAGE_LINES } = {}) {
  const lines = splitLines(text);
  const entries = [];
  let pending = []; // entries whose parent line has not been read yet
  let pass = null;
  let fail = null;
  for (let i = 0; i < lines.length; i += 1) {
    const counter = /^# (pass|fail) (\d+)\s*$/.exec(lines[i]);
    if (counter) {
      if (counter[1] === 'pass') pass = Number(counter[2]);
      else fail = Number(counter[2]);
      continue;
    }
    const match = TAP_LINE_RE.exec(lines[i]);
    if (!match) continue;
    const indent = match[1].length;
    const skipped = TAP_DIRECTIVE_RE.test(match[3]);
    const title = match[3].replace(TAP_DIRECTIVE_RE, '').trim();
    const failed = match[2] === 'not ok' && !skipped;
    const entry = { title, status: failed ? 'failed' : skipped ? 'skipped' : 'passed', failed, indent, parent: null, children: pending.filter((p) => p.indent > indent), yaml: [], comments: [] };
    for (const child of entry.children) child.parent = entry;
    pending = [...pending.filter((p) => p.indent <= indent), entry];
    entries.push(entry);
    if (failed && indent === 0) entry.comments = tapFileComments(lines, i);
    if (TAP_YAML_START_RE.test(lines[i + 1] ?? '')) {
      // Whatever the block holds (a diff that looks like "ok 1 - x", say) is not a test line.
      const block = tapYamlBlock(lines, i + 1, indent);
      entry.yaml = failed ? block.lines : [];
      i = block.last;
    } else if (failed) {
      for (let j = i + 1; j < lines.length && !TAP_YAML_END_RE.test(lines[j]) && !TAP_LINE_RE.test(lines[j]); j += 1) entry.yaml.push(lines[j]);
    }
  }

  const failures = [];
  for (const entry of entries) {
    if (!entry.failed) continue;
    const yaml = entry.yaml.map((line) => line.slice(Math.min(entry.indent, leadingSpaces(line)))).join('\n');
    const failureType = /^\s*failureType:\s*'?(\w+)'?\s*$/m.exec(yaml)?.[1];
    if (failureType === 'subtestsFailed' && entry.children.some((child) => child.failed)) continue;
    const isFile = entry.indent === 0 && (TAP_FILE_TITLE_RE.test(entry.title) || /^\s*exitCode:/m.test(yaml));
    const parents = [];
    for (let parent = entry.parent; parent !== null; parent = parent.parent) parents.unshift(parent.title);
    const detail = [...(isFile ? entry.comments : []), ...(yaml === '' ? [] : [yaml])].join('\n');
    failures.push({ title: [...parents, entry.title].join(TAP_PATH_SEPARATOR), detail: firstLines(detail, detailLines), cancelled: failureType === 'cancelledByParent' });
  }
  return { tests: entries.map(({ title, status }) => ({ title, status })), pass, fail, failures };
}

const ANSI_RE = /\u001b\[[0-9;]*[A-Za-z]/g;
const stripAnsi = (text) => String(text).replace(ANSI_RE, '');

// Playwright's JSON report (the `json` reporter): { stats, tests: [...], errors: [message] } or { unreadable }.
// One entry per test and project: { project, file, title (describe titles and the test's own, joined), specTitle, status, message,
// location, screenshots }. status: passed | failed | flaky | skipped. A test that is EXPECTED to fail (test.fail) and does is "skipped": it
// proves nothing about the criterion. flaky is Playwright's own verdict (failed first, passed on a retry).
export function parsePlaywright(text) {
  let data;
  try {
    data = JSON.parse(text);
  } catch (err) {
    return { unreadable: `results.json is not valid JSON (${err.message})` };
  }
  if (data === null || typeof data !== 'object' || !Array.isArray(data.suites)) return { unreadable: 'results.json is not a Playwright JSON report (it has no "suites")' };
  const tests = [];
  const visit = (suite, parents, file) => {
    const titles = suite.title && suite.title !== suite.file ? [...parents, suite.title] : parents;
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        const results = test.results ?? [];
        const last = results[results.length - 1];
        let status;
        if (test.status === 'unexpected') status = 'failed';
        else if (test.status === 'flaky') status = 'flaky';
        else if (test.status === 'skipped') status = 'skipped';
        else if (test.status === 'expected') status = test.expectedStatus === 'failed' ? 'skipped' : 'passed';
        else status = last?.status === 'passed' ? 'passed' : last?.status === 'skipped' ? 'skipped' : 'failed';
        const error = last?.error ?? last?.errors?.[0];
        tests.push({
          project: test.projectName ?? '',
          file: spec.file ?? file ?? suite.file ?? '',
          title: [...titles, spec.title].join(' › '),
          specTitle: String(spec.title ?? ''),
          status,
          message: status === 'failed' && error?.message ? stripAnsi(error.message) : null,
          location: status === 'failed' && error?.location ? `${path.basename(error.location.file ?? '')}:${error.location.line ?? '?'}` : null,
          screenshots: status === 'failed' ? (last?.attachments ?? []).filter((a) => a?.contentType === 'image/png' && typeof a.path === 'string').map((a) => a.path) : [],
        });
      }
    }
    for (const child of suite.suites ?? []) visit(child, titles, suite.file ?? file);
  };
  for (const suite of data.suites) visit(suite, [], suite.file);
  const errors = (Array.isArray(data.errors) ? data.errors : []).map((e) => stripAnsi(e?.message ?? JSON.stringify(e)));
  return { tests, errors };
}

// The status of a criterion from the statuses of its tests (R3-05). Precedence: failed > flaky > partial > passed.
//  - a failed test fails the criterion, a flaky one makes it flaky, whatever else passed;
//  - a test that was skipped, fixme'd, TODO or expected to fail (`test.fail`) proves nothing: next to a passing test it makes the criterion
//    `partial` (never `passed`: one clause of it is not verified); with nothing passing beside it the criterion is `skipped`;
//  - no test at all is `missing`.
export function acStatus(statuses) {
  if (statuses.has('failed')) return 'failed';
  if (statuses.has('flaky')) return 'flaky';
  if (statuses.has('skipped')) return statuses.has('passed') ? 'partial' : 'skipped';
  if (statuses.has('passed')) return 'passed';
  return 'missing';
}

// tests: [{ title, status, source }] -> one row per criterion: { n, id, status, found, parts, skipped }. `skipped` lists the skipped tests of the
// criterion as "<source>: <title>" (each once, however many projects ran it), so a partial row can say what is not verified.
export function buildAcMatrix(tests) {
  const rows = Array.from({ length: AC_COUNT }, (_, i) => ({ n: i + 1, id: acLabel(i + 1), statuses: new Set(), sources: new Map(), parts: new Set(), skipped: new Set() }));
  for (const test of tests) {
    for (const { n, part } of acTokens(test.title)) {
      const row = rows[n - 1];
      row.statuses.add(test.status);
      row.sources.set(test.source, (row.sources.get(test.source) ?? 0) + 1);
      if (part.length > 2) row.parts.add(part);
      if (test.status === 'skipped') row.skipped.add(`${test.source}: ${test.title}`);
    }
  }
  return rows.map((row) => ({
    n: row.n,
    id: row.id,
    status: acStatus(row.statuses),
    found: [...row.sources].map(([source, count]) => `${source} ${count}`).join(', '),
    parts: [...row.parts].sort(),
    skipped: [...row.skipped],
  }));
}

// The "## Acceptance criteria" section. Report-only: no verdict is derived from it.
export function acSection(rows) {
  const counts = Object.fromEntries(AC_STATUSES.map((status) => [status, rows.filter((row) => row.status === status).length]));
  const lines = rows.map((row) => `| ${row.id} | ${row.status} | ${row.found === '' ? '—' : `${row.found}${row.parts.length > 0 ? ` (${row.parts.join(', ')})` : ''}`} |`);
  const parts = [
    '## Acceptance criteria',
    'Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright\'s `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats partial beats passed. `partial` means a test of the criterion passed and another was skipped, fixme\'d or expected to fail: part of the criterion is not verified, so it is never read as `passed`; when every test of it is skipped it is `skipped`.',
    `${rows.length} criteria: ${counts.passed} passed, ${counts.partial} partial, ${counts.failed} failed, ${counts.skipped} skipped, ${counts.flaky} flaky, ${counts.missing} missing.`,
    ['| AC | status | found in |', '|---|---|---|', ...lines].join('\n'),
  ];
  // What is not verified, so a partial row is not a mystery: the skipped tests of the partial criteria, each criterion once.
  const unverified = rows.filter((row) => row.status === 'partial').flatMap((row) => row.skipped.map((entry) => `- ${row.id}, skipped: ${tableCell(entry)}`));
  if (unverified.length > 0) {
    const shown = unverified.slice(0, MAX_PARTIAL_LISTED);
    const more = unverified.length > shown.length ? `\n... and ${unverified.length - shown.length} more skipped tests of partial criteria` : '';
    parts.push(`Skipped tests of the partial criteria (${unverified.length}):\n\n${shown.join('\n')}${more}`);
  }
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------------------------
// The e2e job: Playwright's report, the payload contract TAP, screenshots
// ---------------------------------------------------------------------------------------------

const slug = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'test';

// "[X-01] the page" -> "x-01", otherwise a slug of the title.
const testId = (test) => slug(/\[([A-Za-z]+-\d+[a-z]?)\]/.exec(test.specTitle)?.[1] ?? test.specTitle);

// The "## E2E" section and the sections that follow it. `results` is parsePlaywright's value (null: no results.json), `contract` parseTap's
// (null: no contract.tap), `appLog` the text of e2e/app.log (or null), `jobResult` the e2e job's result from --needs (or undefined).
// Returns { sections: [markdown], problems: [one-line reasons the run fails: empty when the e2e results are clean], flaky: [test names] }.
export function e2eReport({ results, contract, appLog, jobResult }) {
  const summary = [];
  const details = [];
  const problems = [];
  const flaky = [];
  let showAppLog = false;

  if (results === null) {
    if (jobResult !== undefined && jobResult !== 'skipped') {
      summary.push(`No e2e/results.json was found: Playwright did not get as far as writing its report (the app or the proxy did not start, or the job stopped before the tests). The e2e job result is ${jobResult}.`);
      showAppLog = true;
    }
  } else if (results.unreadable) {
    summary.push(`${results.unreadable}.`);
    problems.push(`e2e: ${results.unreadable}`);
    showAppLog = true;
  } else {
    const count = (status) => results.tests.filter((test) => test.status === status).length;
    const projects = new Set(results.tests.map((test) => test.project)).size;
    summary.push(`${count('passed')} passed, ${count('failed')} failed, ${count('flaky')} flaky, ${count('skipped')} skipped (${results.tests.length} test run${results.tests.length === 1 ? '' : 's'} in ${projects} project${projects === 1 ? '' : 's'}).`);
    const failed = results.tests.filter((test) => test.status === 'failed');
    flaky.push(...results.tests.filter((test) => test.status === 'flaky').map((test) => `[${test.project}] ${test.title}`));
    if (failed.length > 0) {
      problems.push(`${failed.length} failed E2E test(s)`);
      const blocks = failed.map((test) => {
        const ids = [...new Set(acTokens(test.specTitle).map((token) => acLabel(token.n)))];
        const meta = [ids.length > 0 ? `Acceptance criteria: ${ids.join(', ')}` : null, test.location ? `at ${test.location}` : null].filter(Boolean).join('; ');
        const message = fenced(firstLines(test.message ?? '(no error message in results.json)', MAX_MESSAGE_LINES));
        return [`### [${test.project}] ${test.file ? `${test.file} › ` : ''}${test.title}`, ...(meta === '' ? [] : [meta]), message].join('\n\n');
      });
      details.push([`## Failed E2E tests (${failed.length})`, ...blocks].join('\n\n'));
    }
    if (results.errors.length > 0) {
      problems.push('Playwright reported an error outside the tests');
      details.push(['## Playwright errors outside any test', ...results.errors.map((message) => fenced(firstLines(message, MAX_MESSAGE_LINES)))].join('\n\n'));
    }
    if (results.tests.length === 0 && results.errors.length === 0) problems.push('e2e: results.json holds no test');
    showAppLog = problems.length > 0;
  }

  if (contract !== null) {
    const failedCount = Math.max(contract.fail ?? 0, contract.failures.length);
    const passedCount = contract.pass ?? contract.tests.filter((test) => test.status === 'passed').length;
    summary.push(`Payload contract (node --test of tests/contract): ${passedCount} passed, ${failedCount} failed.`);
    if (failedCount > 0) {
      problems.push(`payload contract: ${failedCount} failed`);
      details.push(['## Payload contract failures', ...contract.failures.map((failure) => `### ${failure.title}\n\n${fenced(failure.detail)}`)].join('\n\n'));
    }
  }

  const sections = [];
  if (summary.length > 0) sections.push(['## E2E', ...summary].join('\n\n'));
  sections.push(...details);
  if (showAppLog && appLog !== null && appLog.trim() !== '') {
    sections.push(`## App log (last ${APP_LOG_TAIL_LINES} lines of e2e/app.log)\n\n${fenced(tailLines(appLog, APP_LOG_TAIL_LINES).join('\n'))}`);
  }
  return { sections, problems, flaky };
}

// ---------------------------------------------------------------------------------------------
// The js job: the type check (tsc.log) and the Node tests (js-tests.tap)
// ---------------------------------------------------------------------------------------------

// `text` with the workspace folder of the runner removed ("file:///home/runner/work/r/r/tests/js/a.test.mjs:3:1" -> "tests/js/a.test.mjs:3:1").
function stripRootText(text, root) {
  const base = (root ?? '').replace(/\/+$/, '');
  if (base === '') return text;
  return text.split(`file://${base}/`).join('').split(`${base}/`).join('');
}

const TSC_ERROR_RE = /\berror TS\d+\b/;

// The distinct "error TS<n>" lines of a tsc log (`tsc --noEmit`), in order of first appearance. The indented lines that continue a
// message are not kept; the whole log is published as js/tsc.log.
export function parseTsc(text, root) {
  const seen = new Set();
  const errors = [];
  for (const raw of splitLines(stripAnsi(text))) {
    const line = stripRootText(raw.trimEnd(), root);
    if (/^\s/.test(line) || !TSC_ERROR_RE.test(line) || seen.has(line)) continue;
    seen.add(line);
    errors.push(line);
  }
  return errors;
}

// The sections for the js job and what makes the run fail. `tap` is parseTap's value for js-tests.tap (null: none was found), `tsc` the text of
// tsc.log (null: none), `jobResult` the js job's result from --needs (or undefined). The js job result alone adds nothing to `problems` (the
// "job js: ..." reason is written by the caller); a js job that failed with nothing to show for it is said in the section, unless `stylesFailed`
// (stylesReport found a FAIL line) explains it.
// Returns { sections: [markdown], problems: [one-line reasons: empty when the js results are clean] }.
export function jsReport({ tap, tsc, jobResult, root = '', stylesFailed = false }) {
  const problems = [];
  const summary = [];
  const details = [];
  const failedJob = jobResult !== undefined && jobResult !== 'success' && jobResult !== 'skipped';
  if (tap === null && tsc === null && (jobResult === undefined || jobResult === 'skipped')) return { sections: [], problems };
  const verb = jobResult === 'failure' ? 'failed' : `ended as ${jobResult}`;

  const typeErrors = tsc === null ? [] : parseTsc(tsc, root);
  const emptyTap = tap !== null && tap.tests.length === 0 && tap.pass === null && tap.fail === null;
  let testFailures = [];
  if (tap !== null && emptyTap) {
    summary.push('js-tests.tap holds no test result: node did not get as far as running the tests.');
  } else if (tap !== null) {
    // Causes first: the tests cancelled behind a failing parent come last, so the cap never hides the cause.
    testFailures = [...tap.failures.filter((f) => !f.cancelled), ...tap.failures.filter((f) => f.cancelled)];
    const failedCount = testFailures.length > 0 ? testFailures.length : tap.fail ?? 0;
    const passedCount = tap.pass ?? tap.tests.filter((test) => test.status === 'passed').length;
    summary.push(`Node tests (node --test of tests/js): ${passedCount} passed, ${failedCount} failed.`);
    if (failedCount > 0) {
      problems.push(`${failedCount} failed JS test(s)`);
      if (testFailures.length === 0) {
        summary.push('js-tests.tap counts failed tests but lists none of them as "not ok" (the output may be cut short); the file is published as js/js-tests.tap.');
      } else {
        const shown = testFailures.slice(0, MAX_JS_FAILURES);
        const blocks = shown.map((failure) => `### ${stripRootText(failure.title, root)}\n\n${fenced(failure.detail === '' ? '(no detail in js-tests.tap)' : stripRootText(failure.detail, root))}`);
        const more = testFailures.length > shown.length ? [`... and ${testFailures.length - shown.length} more failed JS tests (all of them are in js/js-tests.tap)`] : [];
        details.push([`## Failed JS tests (${testFailures.length})`, ...blocks, ...more].join('\n\n'));
      }
    }
  }

  if (tsc !== null) {
    if (typeErrors.length === 0) {
      summary.push('Type check (tsc.log): no "error TS" line.');
    } else {
      problems.push(`${typeErrors.length} type error(s)`);
      summary.push(`Type check (tsc.log): ${typeErrors.length} distinct type error${typeErrors.length === 1 ? '' : 's'}.`);
      const shown = typeErrors.slice(0, MAX_TYPE_ERRORS);
      const more = typeErrors.length > shown.length ? `\n... and ${typeErrors.length - shown.length} more distinct type errors (all of them are in js/tsc.log)` : '';
      details.push(`## Type errors\n\n${fenced(shown.join('\n') + more)}`);
    }
  }

  // The js job failed: say what is, and what is not, explained by the above.
  const seeTsc = tsc !== null ? 'see tsc.log' : 'there is no tsc.log either, so the cause is in the raw job log of the workflow run';
  if (tap === null) {
    if (!failedJob) {
      summary.push('No js-tests.tap was found.');
    } else if (typeErrors.length > 0) {
      summary.push(`The js job ${verb} and no js-tests.tap was produced: the Node tests run after the type check, which failed (the Type errors section lists them).`);
    } else {
      summary.push(`The js job ${verb} but no js-tests.tap was produced (the job stopped before the Node tests ran); ${seeTsc}.`);
      if (tsc !== null && tsc.trim() !== '') summary.push(`Last lines of tsc.log:\n\n${fenced(stripRootText(tailLines(tsc, TSC_TAIL_LINES).join('\n'), root))}`);
    }
  } else if (failedJob && problems.length === 0) {
    summary.push(stylesFailed ? `The js job ${verb} on the map styles step: see the Map styles section.` : `The js job ${verb} but js-tests.tap has no failing test; ${seeTsc}.`);
  }

  return { sections: [['## JS', ...summary].join('\n\n'), ...details], problems };
}

// ---------------------------------------------------------------------------------------------
// The js job's styles.log (tools/ci/validate-styles.mjs, R3-02): `PASS styles <id> (...)`, `WARN styles <id>: ...`, `FAIL styles <id>: ...`.
// A FAIL is a spec error in a style we build or ship (our own satellite and demo-offline styles, or our overlay on a fetched one) and fails the
// run, like the exit code of the script fails the js job (03 section 4.9). A WARN is a third-party style that was not fetched, was refused or has
// its own problems: listed, never a failure.
// ---------------------------------------------------------------------------------------------

const STYLE_LINE_RE = /^(PASS|WARN|FAIL)\s+styles(?=\s|:|$)(?:\s+([^\s:]+))?(.*)$/;

// -> { fails: [{ id, detail, line }], warns: [...], passes: [...], other: [line] }. `id` is null for a line without one (the script crashed);
// `detail` is the text after "<id>:". `other` holds what is not a result line (a stack trace when the script crashed); blank lines are dropped.
export function parseStylesLog(text) {
  const parsed = { fails: [], warns: [], passes: [], other: [] };
  for (const raw of splitLines(stripAnsi(text))) {
    const line = raw.trimEnd();
    if (line.trim() === '') continue;
    const match = STYLE_LINE_RE.exec(line);
    if (!match) {
      parsed.other.push(line);
      continue;
    }
    const entry = { id: match[2] ?? null, detail: match[3].replace(/^\s*:\s*/, '').trim(), line };
    (match[1] === 'FAIL' ? parsed.fails : match[1] === 'WARN' ? parsed.warns : parsed.passes).push(entry);
  }
  return parsed;
}

// The "## Map styles" section and what makes the run fail. `log` is the text of styles.log (null: none was found).
// Returns { sections: [markdown], problems: [one-line reasons: empty when no style we build or ship failed], failed: boolean }.
export function stylesReport({ log }) {
  if (log === null) return { sections: [], problems: [], failed: false };
  const parsed = parseStylesLog(log);
  const problems = [];
  const parts = ['## Map styles'];
  const hasResult = parsed.fails.length + parsed.warns.length + parsed.passes.length > 0;
  if (parsed.fails.length > 0) {
    const names = [...new Set(parsed.fails.map((fail) => fail.id ?? 'validate-styles'))];
    problems.push(`map styles failed: ${names.join(', ')}`);
    const shown = parsed.fails.slice(0, MAX_STYLE_FAILS).map((fail) => fail.line);
    const more = parsed.fails.length > shown.length ? [`... and ${parsed.fails.length - shown.length} more FAIL lines (all of them are in js/styles.log)`] : [];
    parts.push(`FAIL: ${parsed.fails.length} spec error${parsed.fails.length === 1 ? '' : 's'} in a style we build or ship (03 section 4.9: this breaks the build). Fix the style and push again.`, fenced([...shown, ...more].join('\n')));
  } else if (!hasResult) {
    problems.push('map styles: styles.log holds no result line (validate-styles.mjs stopped before it printed one)');
    parts.push('FAIL: styles.log holds no `PASS`, `WARN` or `FAIL` line, so the styles were not validated: validate-styles.mjs stopped before it printed a result.');
  }
  if (parsed.other.length > 0 && (parsed.fails.length > 0 || !hasResult)) parts.push(`Other output of the script:\n\n${fenced(firstLines(parsed.other.join('\n'), MAX_OTHER_GUARD_LINES))}`);
  if (parsed.warns.length > 0) {
    const shown = parsed.warns.slice(0, MAX_STYLE_WARNINGS).map((warn) => warn.line);
    const more = parsed.warns.length > shown.length ? [`... and ${parsed.warns.length - shown.length} more WARN lines`] : [];
    parts.push(`WARN: ${parsed.warns.length} line${parsed.warns.length === 1 ? '' : 's'} about third-party styles (not fetched, refused, or a problem of the published style); a warning never fails the run.\n\n${fenced([...shown, ...more].join('\n'))}`);
  }
  if (parsed.passes.length > 0) parts.push(`PASS: ${parsed.passes.length} (${parsed.passes.map((pass) => pass.id ?? 'styles').join(', ')}).`);
  return { sections: [parts.join('\n\n')], problems, failed: parsed.fails.length > 0 };
}

// Screenshots: the gallery (PNGs under a folder named shots, S6b) is copied to shots/<project>/<scene>.png, and the first MAX_FAILURE_SHOTS
// screenshots of failing tests (Playwright's `screenshot: 'only-on-failure'`, found by the path its report records) to failures/.
// -> [{ from, to }]
export function screenshotCopies(pngs, results) {
  const posix = (file) => file.split(path.sep).join('/');
  const copies = [];
  for (const file of pngs) {
    const parts = posix(file).split('/');
    const at = parts.lastIndexOf('shots');
    if (at !== -1 && at < parts.length - 1) copies.push({ from: file, to: `shots/${parts.slice(at + 1).join('/')}` });
  }
  if (results !== null && !results.unreadable) {
    const used = new Set();
    let taken = 0;
    for (const test of results.tests) {
      if (taken >= MAX_FAILURE_SHOTS) break;
      for (const recorded of test.screenshots) {
        // The report records the path on the runner (".../ci-out/e2e/artifacts/<test folder>/test-failed-1.png"); the artifact keeps what follows "artifacts/".
        const tail = `/artifacts/${recorded.split('/artifacts/').pop()}`;
        const from = pngs.find((file) => posix(file).endsWith(tail));
        if (from === undefined) continue;
        let name = `failures/${testId(test)}.${slug(test.project)}.png`;
        for (let n = 2; used.has(name); n += 1) name = `failures/${testId(test)}.${slug(test.project)}-${n}.png`;
        used.add(name);
        copies.push({ from, to: name });
        taken += 1;
        break;
      }
    }
  }
  return copies;
}

// "## Screenshots": every copied PNG with the first 12 hex digits of its SHA-256, so an unchanged scene can be skipped (03 section 8.5, item 7).
export function screenshotIndex(copies) {
  const rows = copies.filter((copy) => copy.to.endsWith('.png')).map((copy) => {
    const digest = crypto.createHash('sha256').update(fs.readFileSync(copy.from)).digest('hex').slice(0, 12);
    return `${copy.to}  sha256:${digest}`;
  });
  return `## Screenshots (${rows.length})\n\n${fenced(rows.join('\n'))}`;
}

// ---------------------------------------------------------------------------------------------
// Inputs
// ---------------------------------------------------------------------------------------------

function walkFiles(dir) {
  const found = [];
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return found; // a missing or unreadable input folder is "no inputs", not an error
  }
  for (const entry of entries.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) found.push(...walkFiles(full));
    else if (entry.isFile()) found.push(full);
  }
  return found;
}

function readText(file) {
  return fs.readFileSync(file, 'utf8');
}

// The last part of a file that is bigger than `limit` bytes, starting at a line, behind a line that says it was cut (the whole is at most
// `limit` bytes); null when the file fits as it is.
export function keepTail(file, limit) {
  const size = fs.statSync(file).size;
  if (size <= limit) return null;
  const note = `... make-summary.mjs kept the last part of this file (${size} bytes in the artifact, at most ${limit} bytes here)\n`;
  const bytes = fs.readFileSync(file);
  let start = size - (limit - Buffer.byteLength(note));
  const newline = bytes.indexOf(0x0a, start);
  if (newline !== -1 && newline + 1 < size) start = newline + 1; // begin at a line
  while (start < size && (bytes[start] & 0xc0) === 0x80) start += 1; // never inside a multi-byte character
  return note + bytes.subarray(start).toString('utf8');
}

export function collectInputs(inDir) {
  const files = walkFiles(inDir);
  const named = (base) => files.filter((f) => path.basename(f) === base);
  return {
    errorLogs: named('errors.log'),
    restoreLogs: named('restore.log'),
    buildLogs: named('build.log'),
    guardsLogs: named('guards.log'),
    smokeJsons: named('smoke.json'),
    trxFiles: files.filter((f) => f.toLowerCase().endsWith('.trx')),
    resultsJsons: named('results.json'),
    jsTaps: named('js-tests.tap'),
    tscLogs: named('tsc.log'),
    stylesLogs: named('styles.log'),
    contractTaps: named('contract.tap'),
    appLogs: named('app.log'),
    pngs: files.filter((f) => f.toLowerCase().endsWith('.png')),
    fileCount: files.length,
  };
}

// ---------------------------------------------------------------------------------------------
// The verdict and the document
// ---------------------------------------------------------------------------------------------

export function parseNeeds(needsText) {
  if (needsText === undefined) return { given: false, jobs: [], error: null };
  try {
    const parsed = JSON.parse(needsText);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('not a JSON object');
    const jobs = Object.entries(parsed).map(([name, value]) => ({ name, result: String(value?.result ?? 'unknown') }));
    return { given: true, jobs, error: null };
  } catch (err) {
    return { given: true, jobs: [], error: err.message };
  }
}

function header(meta, result, why) {
  const lines = [
    `# CI summary: ${result.toUpperCase()}`,
    '',
    `- result: ${result}`,
    `- branch: ${meta.branch}`,
    `- sha: ${meta.sha}`,
    `- run: ${meta.run}`,
  ];
  if (meta.url) lines.push(`- url: ${meta.url}`);
  if (why.length > 0) lines.push(`- why: ${why.join('; ')}`);
  return lines.join('\n');
}

export function resolveMeta(options, env) {
  const server = env.GITHUB_SERVER_URL;
  const repository = env.GITHUB_REPOSITORY;
  const runId = env.GITHUB_RUN_ID;
  const derivedUrl = server && repository && runId ? `${server}/${repository}/actions/runs/${runId}` : '';
  return {
    branch: options.branch || env.GITHUB_REF_NAME || 'unknown',
    sha: (options.sha || env.GITHUB_SHA || 'unknown').toLowerCase(),
    run: options.run || env.GITHUB_RUN_NUMBER || '0',
    url: options.url || derivedUrl,
  };
}

// Reads the inputs and returns { markdown, result, files } without touching the disk beyond reading.
export function buildReport({ inDir, needsText, root, meta }) {
  const inputs = collectInputs(inDir);
  const needs = parseNeeds(needsText);

  const errorTexts = inputs.errorLogs.map(readText);
  const restoreTexts = inputs.restoreLogs.map(readText);
  const buildTexts = inputs.buildLogs.map(readText);
  const errors = distinctDiagnostics(errorTexts, 'error', root);
  const warnings = distinctWarnings([...restoreTexts, ...buildTexts], root);
  const guards = inputs.guardsLogs.length > 0 ? parseGuardsLog(inputs.guardsLogs.map(readText).join('\n')) : null;
  const smoke = inputs.smokeJsons.length > 0 ? parseSmoke(readText(inputs.smokeJsons[0])) : null;

  const trx = { passed: 0, failed: 0, skipped: 0, failures: [] };
  const trxMismatches = [];
  const acTests = [];
  for (const file of inputs.trxFiles) {
    const xml = readText(file);
    acTests.push(...parseTrxAcTests(xml).map((test) => ({ ...test, source: 'dotnet' })));
    const parsed = parseTrx(xml);
    trx.passed += parsed.passed;
    trx.failed += parsed.failed;
    trx.skipped += parsed.skipped;
    trx.failures.push(...parsed.failures);
    if (parsed.countersFailed > parsed.failed) {
      trxMismatches.push(`${path.basename(file)} reports ${parsed.countersFailed} failed test(s) in its counters but ${parsed.failed} were found in its results`);
    }
  }

  // The e2e job (S6a): Playwright's report, the payload contract TAP, the app log. The js job (FX4): the node TAP and tsc.log, reported in "## JS"; the TAP also feeds the AC matrix.
  const e2eJob = needs.jobs.find((job) => job.name === E2E_JOB);
  const e2eResults = inputs.resultsJsons.length > 0 ? parsePlaywright(readText(inputs.resultsJsons[0])) : null;
  const contract = inputs.contractTaps.length > 0 ? parseTap(inputs.contractTaps.map(readText).join('\n')) : null;
  const jsTap = inputs.jsTaps.length > 0 ? parseTap(inputs.jsTaps.map(readText).join('\n'), { detailLines: MAX_JS_DETAIL_LINES }) : null;
  const jsJob = needs.jobs.find((job) => job.name === JS_JOB);
  const styles = stylesReport({ log: inputs.stylesLogs.length > 0 ? inputs.stylesLogs.map(readText).join('\n') : null });
  const js = jsReport({ tap: jsTap, tsc: inputs.tscLogs.length > 0 ? inputs.tscLogs.map(readText).join('\n') : null, jobResult: jsJob?.result, root, stylesFailed: styles.failed });
  const appLog = inputs.appLogs.length > 0 ? readText(inputs.appLogs[0]) : null;
  const e2e = e2eReport({ results: e2eResults, contract, appLog, jobResult: e2eJob?.result });
  const e2eMissing = e2eResults === null && e2eJob !== undefined && e2eJob.result === 'success';
  if (e2eResults !== null && !e2eResults.unreadable) acTests.push(...e2eResults.tests.map((test) => ({ title: test.title, status: test.status, source: 'e2e' })));
  if (jsTap !== null) acTests.push(...jsTap.tests.map((test) => ({ ...test, source: 'node' })));
  const matrixWanted = inputs.trxFiles.length > 0 || e2eResults !== null || jsTap !== null || e2eJob !== undefined;

  // The verdict. A guards failure explains the rest: the jobs behind it are skipped, so they are not listed again.
  const guardNames = guards ? [...new Set(guards.fails.map((f) => f.name))] : [];
  const guardsJob = needs.jobs.find((job) => job.name === GUARDS_JOB);
  const guardsFailed = guardNames.length > 0 || (guardsJob !== undefined && guardsJob.result !== 'success');
  const guardsWhy = guardNames.length > 0
    ? `guards failed: ${guardNames.join(', ')}`
    : `guards failed: unknown (${guards ? 'guards.log holds no FAIL line' : 'no guards.log was found'})`;
  const hiddenByGuards = (job) => guardsFailed && (job.name === GUARDS_JOB || job.result === 'skipped');
  // The Docker smoke section names its own failures, so the docker-smoke job is not listed a second time (nor blamed on the raw log).
  const smokeJob = needs.jobs.find((job) => job.name === DOCKER_SMOKE_JOB);
  const smokeMissing = smoke === null && smokeJob !== undefined && smokeJob.result !== 'skipped';
  const explained = (job) =>
    hiddenByGuards(job) || (smoke !== null && smoke.problem !== null && job.name === DOCKER_SMOKE_JOB) || (e2e.problems.length > 0 && job.name === E2E_JOB);
  const why = [];
  if (guardsFailed) why.push(guardsWhy);
  for (const job of needs.jobs) if (job.result !== 'success' && !explained(job)) why.push(`job ${job.name}: ${job.result}`);
  if (needs.error) why.push(`the --needs JSON could not be parsed (${needs.error})`);
  if (errors.length > 0) why.push(`${errors.length} distinct compiler error(s)`);
  if (trx.failed > 0) why.push(`${trx.failed} failed test(s)`);
  for (const mismatch of trxMismatches) why.push(mismatch);
  why.push(...js.problems);
  why.push(...styles.problems);
  if (smoke !== null && smoke.problem !== null) why.push(smoke.problem);
  if (smokeMissing && smokeJob.result === 'success') why.push('job docker-smoke succeeded but left no smoke.json');
  why.push(...e2e.problems);
  if (e2eMissing) why.push('job e2e succeeded but left no e2e/results.json');
  if (!needs.given && inputs.fileCount === 0) why.push('no CI inputs were found');
  const result = why.length > 0 ? 'failure' : 'success';

  // The document.
  const sections = [header(meta, result, why)];

  if (needs.given) {
    const rows = needs.jobs.map((job) => `| ${job.name} | ${job.result} |`);
    sections.push(
      ['## Jobs', '', ...(needs.error ? [`The --needs JSON could not be parsed: ${needs.error}`, ''] : []), '| job | result |', '|---|---|', ...rows].join('\n'),
    );
  }

  if (guards) sections.push(guardsSection(guards));

  let errorsLogOut = '';
  let compileSection;
  if (inputs.errorLogs.length > 0) {
    errorsLogOut = errors.join('\n');
    if (errors.length === 0) {
      compileSection = '## Compiler errors\n\nNone.';
    } else {
      const shown = errors.slice(0, MAX_ERRORS);
      const more = errors.length > shown.length ? `\n... and ${errors.length - shown.length} more distinct errors (all of them are in errors.log)` : '';
      compileSection = `## Compiler errors (${errors.length} distinct)\n\n${fenced(shown.join('\n') + more)}`;
    }
  } else if (inputs.restoreLogs.length > 0) {
    const tail = tailLines(restoreTexts.join('\n'), RESTORE_TAIL_LINES).join('\n');
    errorsLogOut = tail;
    compileSection = `## Compiler errors\n\nThere is no errors.log (the build step did not run). Tail of restore.log (last ${RESTORE_TAIL_LINES} lines):\n\n${fenced(tail)}`;
  } else {
    compileSection = '## Compiler errors\n\nNo errors.log and no restore.log were found.';
  }
  sections.push(compileSection);

  if (inputs.trxFiles.length === 0) {
    sections.push('## Tests\n\nNo .trx files were found.');
  } else {
    const total = trx.passed + trx.failed + trx.skipped;
    const counts = `${trx.passed} passed, ${trx.failed} failed, ${trx.skipped} skipped (${inputs.trxFiles.length} .trx file${inputs.trxFiles.length === 1 ? '' : 's'})`;
    if (trx.failures.length === 0) {
      sections.push(`## Tests\n\n${counts}.`);
    } else {
      const blocks = trx.failures.map((f) => `### ${f.name}\n\n${fenced(firstLines(f.message, MAX_MESSAGE_LINES))}`);
      sections.push([`## Failed tests (${trx.failed} of ${total})`, `${counts}.`, ...blocks].join('\n\n'));
    }
  }

  sections.push(...js.sections);
  sections.push(...styles.sections);
  sections.push(...e2e.sections);
  if (e2e.flaky.length > 0) {
    sections.push(`## Flaky tests (${e2e.flaky.length}, passed on retry)\n\nA flaky test is investigated at once; two flaky runs in three consecutive runs block the merge (04 section 1.6).\n\n${fenced(e2e.flaky.join('\n'))}`);
  }
  if (matrixWanted) sections.push(acSection(buildAcMatrix(acTests)));

  if (smoke !== null) {
    sections.push(smokeSection(smoke));
  } else if (smokeMissing) {
    const where = meta.url ? ` (${meta.url})` : '';
    sections.push(`## Docker smoke\n\nNo smoke.json was found: the image build or the smoke script stopped before it wrote one. The cause is in the raw job log of the workflow run${where}.`);
  }

  if (warnings.length > 0) {
    const shown = warnings.slice(0, MAX_WARNINGS);
    const more = warnings.length > shown.length ? `\n... and ${warnings.length - shown.length} more` : '';
    sections.push(`## Warnings (${warnings.length} distinct)\n\n${fenced(shown.join('\n') + more)}`);
  }

  if (trxMismatches.length > 0) {
    sections.push(`## Notes\n\n${trxMismatches.join('\n')}. The layout of the .trx differs from what make-summary.mjs expects; the file itself is published under tests/.`);
  }

  if (guardsFailed) {
    const where = meta.url ? ` (${meta.url})` : '';
    const skipped = needs.jobs.filter((job) => job.result === 'skipped').map((job) => job.name);
    const cause = guardNames.length > 0
      ? 'The FAIL lines are in the Guards section above; fix them and push again.'
      : `The cause is in the raw job log of the workflow run${where}: a failed step before the guards ran, or a guards script that crashed.`;
    const behind = skipped.length > 0 ? ` The jobs behind the guards did not run (${skipped.join(', ')}), so this run has no build or test results.` : '';
    sections.push(`## Notes\n\n${guardsWhy}. ${cause}${behind}`);
  }

  // A js job that failed with failing JS tests, type errors or a style of ours that does not validate is explained by the sections that name them.
  const failedJobs = needs.jobs.filter((job) => job.result !== 'success' && !explained(job) && !(job.name === JS_JOB && (js.problems.length > 0 || styles.problems.length > 0)));
  if (failedJobs.length > 0 && errors.length === 0 && trx.failed === 0 && trxMismatches.length === 0 && e2e.problems.length === 0) {
    const where = meta.url ? ` (${meta.url})` : '';
    sections.push(
      `## Notes\n\nJob ${failedJobs.map((j) => j.name).join(', ')} did not succeed, yet no compiler error and no failed test was found in the logs above. The cause is in the raw job log of the workflow run${where}: a crashed test host, a failed step or a missing artifact.`,
    );
  }

  // Small files the orchestrator reads over git, published beside SUMMARY.md (03 section 7.4): the Playwright JSON, the contract TAP, the app
  // log, the js job's js-tests.tap, tsc.log and styles.log under js/, the gallery under shots/ and the first screenshots of failing tests under failures/.
  const copies = [];
  const writes = [];
  if (inputs.resultsJsons.length > 0) copies.push({ from: inputs.resultsJsons[0], to: 'e2e/results.json' });
  if (inputs.contractTaps.length > 0) copies.push({ from: inputs.contractTaps[0], to: 'e2e/contract.tap' });
  if (appLog !== null) writes.push({ to: 'e2e/app.log', text: tailLines(appLog, APP_LOG_KEPT_LINES).join('\n') + '\n' });
  // A file over JS_KEPT_BYTES is published as its last part (the report above was read from the whole file).
  for (const [files, to] of [[inputs.jsTaps, 'js/js-tests.tap'], [inputs.tscLogs, 'js/tsc.log'], [inputs.stylesLogs, 'js/styles.log']]) {
    if (files.length === 0) continue;
    const kept = keepTail(files[0], JS_KEPT_BYTES);
    if (kept === null) copies.push({ from: files[0], to });
    else writes.push({ to, text: kept });
  }
  const shots = screenshotCopies(inputs.pngs, e2eResults);
  copies.push(...shots);
  if (shots.length > 0) sections.push(screenshotIndex(shots));

  const buildTail = buildTexts.length > 0 ? tailLines(buildTexts.join('\n'), BUILD_TAIL_LINES).join('\n') + '\n' : null;
  return {
    markdown: sections.join('\n\n') + '\n',
    result,
    errorsLog: errorsLogOut === '' ? '' : errorsLogOut + '\n',
    buildTail,
    trxFiles: inputs.trxFiles,
    copies,
    writes,
  };
}

export function crashReport(meta, err) {
  const detail = err instanceof Error ? (err.stack ?? err.message) : String(err);
  const why = ['make-summary.mjs itself failed, so nothing below this line was analysed'];
  return `${header(meta, 'failure', why)}\n\n## make-summary.mjs crashed\n\n${fenced(detail)}\n\nThe raw logs are in the workflow artifacts of this run.\n`;
}

function writeOutputs(outDir, report) {
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, 'SUMMARY.md'), report.markdown);
  fs.writeFileSync(path.join(outDir, 'errors.log'), report.errorsLog);
  if (report.buildTail !== null) fs.writeFileSync(path.join(outDir, 'build.tail.log'), report.buildTail);
  if (report.trxFiles.length > 0) {
    const testsDir = path.join(outDir, 'tests');
    fs.mkdirSync(testsDir, { recursive: true });
    const used = new Set();
    for (const file of report.trxFiles) {
      let name = path.basename(file);
      for (let n = 2; used.has(name); n += 1) name = `${n}-${path.basename(file)}`;
      used.add(name);
      fs.copyFileSync(file, path.join(testsDir, name));
    }
  }
  for (const { from, to } of report.copies ?? []) {
    fs.mkdirSync(path.dirname(path.join(outDir, to)), { recursive: true });
    fs.copyFileSync(from, path.join(outDir, to));
  }
  for (const { to, text } of report.writes ?? []) {
    fs.mkdirSync(path.dirname(path.join(outDir, to)), { recursive: true });
    fs.writeFileSync(path.join(outDir, to), text);
  }
}

// ---------------------------------------------------------------------------------------------
// Command line
// ---------------------------------------------------------------------------------------------

class UsageError extends Error {}

const VALUE_OPTIONS = new Set(['in', 'out', 'needs', 'root', 'branch', 'sha', 'run', 'url']);

export function parseArgs(argv) {
  const options = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const name = arg.startsWith('--') ? arg.slice(2) : '';
    if (!VALUE_OPTIONS.has(name)) throw new UsageError(`unknown argument '${arg}'`);
    if (i + 1 >= argv.length) throw new UsageError(`${arg} needs a value`);
    options[name] = argv[(i += 1)];
  }
  return options;
}


export function main(argv, env) {
  let options;
  try {
    options = parseArgs(argv);
  } catch (err) {
    if (!(err instanceof UsageError)) throw err;
    process.stderr.write(`make-summary: ${err.message}\nusage: make-summary.mjs [--in ci-in] [--out ci-out] [--needs <json>] [--root <dir>] [--branch b] [--sha s] [--run n] [--url u]\n`);
    return 64;
  }
  const inDir = options.in ?? 'ci-in';
  const outDir = options.out ?? 'ci-out';
  const meta = resolveMeta(options, env);
  const root = options.root ?? env.GITHUB_WORKSPACE ?? '';

  let report;
  try {
    report = buildReport({ inDir, needsText: options.needs, root, meta });
  } catch (err) {
    process.stderr.write(`make-summary: ${err instanceof Error ? err.stack : err}\n`);
    report = { markdown: crashReport(meta, err), result: 'failure', errorsLog: '', buildTail: null, trxFiles: [] };
  }
  try {
    writeOutputs(outDir, report);
  } catch (err) {
    process.stderr.write(`make-summary: cannot write ${outDir}: ${err instanceof Error ? err.message : err}\n`);
    return 1;
  }
  process.stdout.write(`make-summary: result=${report.result}, wrote ${path.join(outDir, 'SUMMARY.md')}\n`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2), process.env);
}
