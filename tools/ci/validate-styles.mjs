#!/usr/bin/env node
// validate-styles.mjs: validates the map styles against the MapLibre style specification (03 section 7.2, 04 card S5a).
//
//   node tools/ci/validate-styles.mjs [--offline] [--timeout <ms>]
//
// Two kinds of style, two kinds of verdict (03 section 4.9: "a validation error on a style we build ourselves breaks the build"):
//   - the styles built in JavaScript (satellite, demo-offline), each with the zone/halo overlay of mapStyles.js appended exactly as
//     realmMap.js does: any validation error is a FAIL and the exit code is 1;
//   - the three OpenFreeMap styles, fetched from the network: an unreachable URL, a refused request, an answer that is not a style or a
//     problem in the published style is only a WARN (third-party and flaky, so it must never turn the build red). What is ours is the
//     overlay: errors that appear once the overlay is appended and were not in the published style are a FAIL.
// The js job of ci.yml runs this as an ordinary step (no continue-on-error): the exit code is the verdict, and make-summary.mjs reads the
// same lines from styles.log ("## Map styles"). A crash of this script is itself a FAIL line (exit 1), never a silent pass.
// --offline skips the fetches. Lines: `PASS styles <id> ...`, `WARN styles <id>: ...`, `FAIL styles <id>: ...`.

import { pathToFileURL } from 'node:url';

import { validateStyleMin } from '@maplibre/maplibre-gl-style-spec';

import { STYLES, STYLE_IDS, buildStyle, transformStyle } from '../../src/Realm.Web/wwwroot/js/mapStyles.js';

export const DEFAULT_TIMEOUT_MS = 8000;

// Messages of the style-spec validator, `layers[3].paint.fill-color: ...`, one per error.
const messages = (style) => validateStyleMin(style).map((error) => (error.identifier ? `${error.identifier}: ${error.message}` : error.message));

// What a style built here looks like to MapLibre: the base style with the overlay appended.
const withOverlay = (style) => transformStyle(undefined, style);

// Validates the styles that need no network. Returns [{ id, failures }]. The registry, the builder and the overlay are parameters only so
// that tests/js/validate-styles.test.mjs can hand in a broken style; the CLI always uses the real ones.
export function validateBuilt({ styles = STYLES, ids = STYLE_IDS, build = buildStyle, overlay = withOverlay } = {}) {
  const results = [];
  for (const id of ids) {
    if (styles[id].url) continue;
    const variants = id === 'demo-offline' ? [undefined, 'Demo map'] : [undefined];
    const failures = [];
    for (const demoAttribution of variants) {
      let base;
      try {
        base = build(id, { demoAttribution });
      } catch (err) {
        failures.push(`buildStyle threw: ${err?.message ?? err}`);
        continue;
      }
      if (typeof base === 'string') {
        failures.push('buildStyle returned a URL for a style that has none');
        continue;
      }
      try {
        failures.push(...messages(base).map((m) => `base style: ${m}`), ...messages(overlay(base)).map((m) => `with overlay: ${m}`));
      } catch (err) {
        failures.push(`the style could not be validated: ${err?.message ?? err}`);
      }
    }
    results.push({ id, failures: [...new Set(failures)] });
  }
  return results;
}

// Fetches one OpenFreeMap style and validates it. Returns { id, warnings, failures, layers }.
export async function validateRemote(id, { timeoutMs = DEFAULT_TIMEOUT_MS, fetchImpl = fetch } = {}) {
  const url = STYLES[id].url;
  let style;
  try {
    const response = await fetchImpl(url, { signal: AbortSignal.timeout(timeoutMs), headers: { accept: 'application/json' } });
    if (!response.ok) return { id, warnings: [`${url} answered ${response.status}`], failures: [], layers: 0 };
    style = await response.json();
  } catch (err) {
    return { id, warnings: [`${url} not fetched: ${err?.cause?.code ?? err?.name ?? 'error'}: ${err?.message ?? err}`], failures: [], layers: 0 };
  }
  // What the third party sent is not ours to fix: JSON that is not a style (an error page answered with 200, say) is a WARN, not a crash.
  if (style === null || typeof style !== 'object' || Array.isArray(style) || !Array.isArray(style.layers)) {
    return { id, warnings: [`${url} did not answer a style object (no "layers" array), not validated`], failures: [], layers: 0 };
  }
  try {
    const published = messages(style);
    const known = new Set(published);
    const failures = [...new Set(messages(withOverlay(style)).filter((m) => !known.has(m)))];
    return { id, warnings: published.map((m) => `published style: ${m}`), failures, layers: style.layers.length };
  } catch (err) {
    // The validator itself threw on content we do not control (the same code ran on our own styles in validateBuilt, which fails the build).
    return { id, warnings: [`${url} could not be validated: ${err?.message ?? err}`], failures: [], layers: style.layers.length };
  }
}

// Exit code: 1 when any FAIL line was printed (a style we build or ship has a spec error), otherwise 0, whatever the WARN lines say.
// `options.built` and `options.fetchImpl` are for tests (an offline run with fixed answers).
export async function main(argv = process.argv.slice(2), out = (line) => console.log(line), options = {}) {
  const offline = argv.includes('--offline');
  const at = argv.indexOf('--timeout');
  const timeoutMs = at >= 0 ? Number(argv[at + 1]) || DEFAULT_TIMEOUT_MS : DEFAULT_TIMEOUT_MS;
  let failed = false;

  const built = validateBuilt(options.built);
  if (built.length === 0) {
    // A registry without a built style would leave nothing that can fail: say so instead of passing.
    failed = true;
    out('FAIL styles: no style built in JavaScript was found to validate (the registry of mapStyles.js lists none without a url)');
  }
  for (const { id, failures } of built) {
    if (failures.length === 0) out(`PASS styles ${id} (built in JavaScript, with the overlay)`);
    for (const failure of failures) {
      failed = true;
      out(`FAIL styles ${id}: ${failure}`);
    }
  }

  if (offline) {
    out('PASS styles openfreemap (skipped: --offline)');
  } else {
    const remote = await Promise.all(STYLE_IDS.filter((id) => STYLES[id].url).map((id) => validateRemote(id, { timeoutMs, ...(options.fetchImpl ? { fetchImpl: options.fetchImpl } : {}) })));
    for (const { id, warnings, failures, layers } of remote) {
      if (warnings.length === 0 && failures.length === 0) out(`PASS styles ${id} (fetched, ${layers} layers, with the overlay)`);
      for (const warning of warnings) out(`WARN styles ${id}: ${warning}`);
      for (const failure of failures) {
        failed = true;
        out(`FAIL styles ${id}: with overlay: ${failure}`);
      }
    }
  }
  return failed ? 1 : 0;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  try {
    process.exitCode = await main();
  } catch (err) {
    // One line for make-summary.mjs (the stack goes to stderr, which the workflow merges into styles.log).
    console.log(`FAIL styles: validate-styles.mjs crashed: ${String(err?.message ?? err).split('\n')[0]}`);
    console.error(err?.stack ?? err);
    process.exitCode = 1;
  }
}
