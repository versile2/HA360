#!/usr/bin/env node
// make-summary.mjs: writes SUMMARY.md (and errors.log, build.tail.log, tests/*.trx) for one CI run.
// 03 section 7.4, 04 card S0a. Node built-ins only (no npm install runs in publish-ci, R2-024).
//
//   node tools/ci/make-summary.mjs --in ci-in --out ci-out --needs '<toJSON(needs)>'
//
// --in     folder holding the downloaded artifacts (one sub-folder per job); it is searched
//          recursively for errors.log, restore.log, build.log, guards.log and *.trx. May be missing or empty.
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

import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const MAX_ERRORS = 40;
export const MAX_WARNINGS = 20;
export const MAX_MESSAGE_LINES = 15;
export const RESTORE_TAIL_LINES = 40;
export const BUILD_TAIL_LINES = 300;
export const GUARDS_JOB = 'guards';
export const MAX_REPORT_EXAMPLES = 3;
export const MAX_OTHER_GUARD_LINES = 15;
export const MIN_RANGE = 3;

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

const DIAGNOSTIC_RE = (kind) => new RegExp(`^(.*?): (?:fatal )?${kind} ([A-Za-z]+\\d+): (.*)$`);
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
  const warningRe = DIAGNOSTIC_RE('warning');
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

export function collectInputs(inDir) {
  const files = walkFiles(inDir);
  const named = (base) => files.filter((f) => path.basename(f) === base);
  return {
    errorLogs: named('errors.log'),
    restoreLogs: named('restore.log'),
    buildLogs: named('build.log'),
    guardsLogs: named('guards.log'),
    trxFiles: files.filter((f) => f.toLowerCase().endsWith('.trx')),
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

  const trx = { passed: 0, failed: 0, skipped: 0, failures: [] };
  const trxMismatches = [];
  for (const file of inputs.trxFiles) {
    const parsed = parseTrx(readText(file));
    trx.passed += parsed.passed;
    trx.failed += parsed.failed;
    trx.skipped += parsed.skipped;
    trx.failures.push(...parsed.failures);
    if (parsed.countersFailed > parsed.failed) {
      trxMismatches.push(`${path.basename(file)} reports ${parsed.countersFailed} failed test(s) in its counters but ${parsed.failed} were found in its results`);
    }
  }

  // The verdict. A guards failure explains the rest: the jobs behind it are skipped, so they are not listed again.
  const guardNames = guards ? [...new Set(guards.fails.map((f) => f.name))] : [];
  const guardsJob = needs.jobs.find((job) => job.name === GUARDS_JOB);
  const guardsFailed = guardNames.length > 0 || (guardsJob !== undefined && guardsJob.result !== 'success');
  const guardsWhy = guardNames.length > 0
    ? `guards failed: ${guardNames.join(', ')}`
    : `guards failed: unknown (${guards ? 'guards.log holds no FAIL line' : 'no guards.log was found'})`;
  const hiddenByGuards = (job) => guardsFailed && (job.name === GUARDS_JOB || job.result === 'skipped');
  const why = [];
  if (guardsFailed) why.push(guardsWhy);
  for (const job of needs.jobs) if (job.result !== 'success' && !hiddenByGuards(job)) why.push(`job ${job.name}: ${job.result}`);
  if (needs.error) why.push(`the --needs JSON could not be parsed (${needs.error})`);
  if (errors.length > 0) why.push(`${errors.length} distinct compiler error(s)`);
  if (trx.failed > 0) why.push(`${trx.failed} failed test(s)`);
  for (const mismatch of trxMismatches) why.push(mismatch);
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

  const failedJobs = needs.jobs.filter((job) => job.result !== 'success' && !hiddenByGuards(job));
  if (failedJobs.length > 0 && errors.length === 0 && trx.failed === 0 && trxMismatches.length === 0) {
    const where = meta.url ? ` (${meta.url})` : '';
    sections.push(
      `## Notes\n\nJob ${failedJobs.map((j) => j.name).join(', ')} did not succeed, yet no compiler error and no failed test was found in the logs above. The cause is in the raw job log of the workflow run${where}: a crashed test host, a failed step or a missing artifact.`,
    );
  }

  const buildTail = buildTexts.length > 0 ? tailLines(buildTexts.join('\n'), BUILD_TAIL_LINES).join('\n') + '\n' : null;
  return {
    markdown: sections.join('\n\n') + '\n',
    result,
    errorsLog: errorsLogOut === '' ? '' : errorsLogOut + '\n',
    buildTail,
    trxFiles: inputs.trxFiles,
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
