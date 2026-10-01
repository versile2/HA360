// payloadShape.mjs: the hand-written checker for the JSON that crosses the .NET <-> JavaScript boundary of the map (03 section 4.5).
//
// The shapes below are the field lists of 03 section 4.5 (and of the JSDoc typedefs at the top of wwwroot/js/realmMap.js, which carry
// the same lists). The checker is strict: a missing field, a field of the wrong type, a value outside an enum and a field that 4.5
// does not list are all errors, so a renamed C# property fails CI instead of silently reaching the browser. Keys are camelCase, as
// System.Text.Json writes them with the web defaults.
//
// Used three ways:
//   - as a module:  import { checkShape } from './payloadShape.mjs';  checkShape('MembersPayload', json) -> string[] (empty = valid)
//   - from the contract test (payloadShape.test.mjs): hand-written samples, and every golden file of tests/contract/payloads/ that
//     PayloadContractTests (S5b) wrote, when that folder exists (the `e2e` job downloads it; the sandbox has none);
//   - from a shell, on any JSON file:  node tests/contract/payloadShape.mjs [--shape <Name>] <file.json>...
//     The shape comes from --shape or from the file name (see shapeNameForFile); output is one PASS or FAIL line per file.

import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// ---- the checking vocabulary: a check is (value, at, errors) => void and pushes `at: message` for each problem ---------------------

const kindOf = (value) => (value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value);
const problem = (errors, at, message) => errors.push(`${at}: ${message}`);
const withOptional = (check) => Object.assign((value, at, errors) => check(value, at, errors), { optional: true });

export const str = (value, at, errors) => {
  if (typeof value !== 'string') problem(errors, at, `expected a string, got ${kindOf(value)}`);
};
export const bool = (value, at, errors) => {
  if (typeof value !== 'boolean') problem(errors, at, `expected a boolean, got ${kindOf(value)}`);
};
export const num = (value, at, errors) => {
  if (typeof value !== 'number' || !Number.isFinite(value)) problem(errors, at, `expected a finite number, got ${kindOf(value)}${typeof value === 'number' ? ` ${value}` : ''}`);
};
export const int = (value, at, errors) => {
  if (!Number.isInteger(value)) problem(errors, at, `expected an integer, got ${kindOf(value)}${typeof value === 'number' ? ` ${value}` : ''}`);
};
const ranged = (check, min, max, what) => (value, at, errors) => {
  const before = errors.length;
  check(value, at, errors);
  if (errors.length === before && (value < min || value > max)) problem(errors, at, `${what} ${value} is outside [${min}, ${max}]`);
};
export const lat = ranged(num, -90, 90, 'latitude');
export const lon = ranged(num, -180, 180, 'longitude');
export const alpha = ranged(num, 0, 1, 'alpha');
export const positive = (value, at, errors) => {
  const before = errors.length;
  num(value, at, errors);
  if (errors.length === before && !(value > 0)) problem(errors, at, `expected a number above 0, got ${value}`);
};
export const nonNegativeInt = (value, at, errors) => {
  const before = errors.length;
  int(value, at, errors);
  if (errors.length === before && value < 0) problem(errors, at, `expected an integer of 0 or more, got ${value}`);
};
/** A colour as a non-empty string (RealmPalette writes #RRGGBB or #RRGGBBAA; the checker does not parse it). */
export const colour = (value, at, errors) => {
  if (typeof value !== 'string' || value.trim() === '') problem(errors, at, `expected a colour string, got ${kindOf(value)}`);
};
/** A path relative to the page base: never absolute and never starting with a slash (03 section 5.3, the no-leading-slash rule). */
export const relativeUrl = (value, at, errors) => {
  if (typeof value !== 'string' || value === '') problem(errors, at, `expected a relative URL string, got ${kindOf(value)}`);
  else if (value.startsWith('/') || /^[a-z][a-z0-9+.-]*:/i.test(value)) problem(errors, at, `'${value}' must be relative, without a leading slash or a scheme`);
};
export const oneOf = (...allowed) => (value, at, errors) => {
  if (!allowed.includes(value)) problem(errors, at, `expected one of ${allowed.map((a) => JSON.stringify(a)).join(', ')}, got ${JSON.stringify(value)}`);
};
export const literal = (expected) => oneOf(expected);
export const nullable = (check) => (value, at, errors) => {
  if (value !== null) check(value, at, errors);
};
export const optional = (check) => withOptional(nullable(check));
export const arrayOf = (check) => (value, at, errors) => {
  if (!Array.isArray(value)) return problem(errors, at, `expected an array, got ${kindOf(value)}`);
  value.forEach((item, index) => check(item, `${at}[${index}]`, errors));
};
export const tuple = (...checks) => (value, at, errors) => {
  if (!Array.isArray(value) || value.length !== checks.length) return problem(errors, at, `expected an array of ${checks.length} items, got ${Array.isArray(value) ? `${value.length} items` : kindOf(value)}`);
  checks.forEach((check, index) => check(value[index], `${at}[${index}]`, errors));
};
/** An object with exactly these fields (optional ones may be absent); `refine` runs on a structurally valid object for cross-field rules. */
export const object = (fields, refine) => (value, at, errors) => {
  if (kindOf(value) !== 'object') return problem(errors, at, `expected an object, got ${kindOf(value)}`);
  const before = errors.length;
  for (const [name, check] of Object.entries(fields)) {
    if (!Object.prototype.hasOwnProperty.call(value, name)) {
      if (!check.optional) problem(errors, `${at}.${name}`, 'missing');
      continue;
    }
    check(value[name], `${at}.${name}`, errors);
  }
  for (const name of Object.keys(value)) {
    if (!Object.prototype.hasOwnProperty.call(fields, name)) problem(errors, `${at}.${name}`, 'unexpected property (not in 03 section 4.5)');
  }
  if (refine && errors.length === before) refine(value, at, errors);
};

// ---- 03 section 4.5 ---------------------------------------------------------------------------------------------------------

export const STYLE_IDS = ['night', 'day', 'streets', 'satellite', 'demo-offline'];
export const MEMBER_STATUSES = ['static', 'offline', 'stale', 'driving', 'atPlace', 'out', 'nofix'];

const padding = object({ top: num, right: num, bottom: num, left: num });
const lngLat = tuple(lon, lat);
const bounds = tuple(lngLat, lngLat); // [[w, s], [e, n]]
const ring = object({ color: colour, dashed: bool, widthPx: positive });
const badge = nullable(oneOf('driving', 'stale', 'offline', 'home'));

// "null = no fix": a position is both coordinates or neither.
const bothOrNeither = (value, at, errors) => {
  if ((value.lat === null) !== (value.lon === null)) problem(errors, at, `lat and lon must both be null (no fix) or both be numbers, got lat ${value.lat} and lon ${value.lon}`);
};
const uniqueIds = (items, at, errors, what) => {
  const seen = new Set();
  items.forEach((item, index) => {
    if (seen.has(item.id)) problem(errors, `${at}[${index}].id`, `duplicate ${what} id '${item.id}'`);
    seen.add(item.id);
  });
};

export const LayoutPayload = object({
  mode: oneOf('compact', 'expanded'),
  panelLeftPx: num,
  panelWidthPx: num,
  panelHidden: bool,
  stackVisible: bool,
  safe: padding,
  navHeightPx: num,
});

export const MemberPayloadItem = object(
  {
    id: str,
    name: str,
    initial: str,
    color: colour,
    lat: nullable(lat),
    lon: nullable(lon),
    accuracyM: nullable(num),
    poorAccuracy: bool,
    status: oneOf(...MEMBER_STATUSES),
    ring,
    badge,
    lowBattery: bool,
    drivingFresh: bool,
    far: bool,
    isStatic: bool,
    isMe: bool,
    zClass: oneOf(0, 1, 2, 3, 4),
    avatarUrl: nullable(relativeUrl),
    chip: nullable(str),
    chipMinute: int,
    ariaLabel: str,
    tooltip: str,
    bubbleLabel: str,
    bubbleTooltip: str,
  },
  bothOrNeither,
);

export const MembersPayload = object({ version: nonNegativeInt, meId: str, members: arrayOf(MemberPayloadItem) }, (value, at, errors) => uniqueIds(value.members, `${at}.members`, errors, 'member'));

export const VehiclePayloadItem = object(
  { id: str, name: str, glyph: oneOf('pickup', 'car'), lat: nullable(lat), lon: nullable(lon), ring, stale: bool, chip: nullable(str), ariaLabel: str, tooltip: str },
  bothOrNeither,
);

export const VehiclesPayload = object({ version: nonNegativeInt, vehicles: arrayOf(VehiclePayloadItem) }, (value, at, errors) => uniqueIds(value.vehicles, `${at}.vehicles`, errors, 'vehicle'));

export const ZoneItem = object({ id: str, name: str, lat, lon, radiusM: positive, occupied: bool });
export const ZoneAppearance = object({ lineColor: colour, fillAlpha: alpha, fillAlphaOccupied: alpha, casing: bool });
export const ZonesPayload = object(
  { version: nonNegativeInt, show: bool, zones: arrayOf(ZoneItem), appearances: object({ dark: ZoneAppearance, light: ZoneAppearance, imagery: ZoneAppearance }) },
  (value, at, errors) => uniqueIds(value.zones, `${at}.zones`, errors, 'zone'),
);

/** Sent by `setSelection`, which arrives with S8a; listed here because 4.5 pins it and the checker is one file. */
export const SelectionPayload = object({ kind: oneOf('member', 'vehicle', 'place'), id: str, follow: bool });

export const DefaultTargets = object({
  version: nonNegativeInt,
  default: object({ bounds, maxZoom: literal(16) }),
  me: nullable(object({ center: lngLat, zoom: literal(16) })),
});

export const CameraState = object({
  center: lngLat,
  zoom: num,
  bounds,
  animated: bool,
  lastDurationMs: num,
  recenter: oneOf('away', 'default', 'me'),
  userInitiated: bool,
});

/** `error` is optional in 4.5; a null (a C# record without WhenWritingNull) is accepted too. */
export const StyleResult = object({ styleId: oneOf(...STYLE_IDS), ok: bool, error: optional(str) });

export const Padding = padding;
export const Bounds = bounds;
export const Ring = ring;

/** Every shape by its 03 section 4.5 name. */
export const SHAPES = Object.freeze({
  Padding,
  Bounds,
  LayoutPayload,
  Ring,
  MemberPayloadItem,
  MembersPayload,
  VehiclePayloadItem,
  VehiclesPayload,
  ZoneItem,
  ZoneAppearance,
  ZonesPayload,
  SelectionPayload,
  DefaultTargets,
  CameraState,
  StyleResult,
});

/** The shapes that are whole interop payloads (the golden files of PayloadContractTests are one of these). */
export const PAYLOAD_SHAPES = Object.freeze(['LayoutPayload', 'MembersPayload', 'VehiclesPayload', 'ZonesPayload', 'SelectionPayload', 'DefaultTargets', 'CameraState', 'StyleResult']);

/**
 * Checks a value against a named shape.
 * @param {string} shapeName a key of {@link SHAPES}
 * @param {unknown} value parsed JSON
 * @returns {string[]} one `path: message` per problem; empty when the value is valid. Paths start at `$`.
 */
export function checkShape(shapeName, value) {
  const check = SHAPES[shapeName];
  if (!check) throw new Error(`unknown shape '${shapeName}'; known: ${Object.keys(SHAPES).join(', ')}`);
  const errors = [];
  check(value, '$', errors);
  return errors;
}

const normalise = (text) => text.toLowerCase().replace(/[^a-z0-9]/g, '');

// Other names a golden file may carry besides the shape name and the shape name minus "Payload".
const FILE_ALIASES = Object.freeze({ CameraState: ['camera'] });

/**
 * The payload shape a golden file holds, from its name: the file name without `.json`, case and punctuation ignored, starts with
 * the shape name or with the name minus a trailing "Payload" (`members.json`, `Members.demo.json`, `ZonesPayload-off.json`,
 * `default-targets.json`, `camera_state.json`, `camera.json`). The longest match wins. Null when no payload shape matches.
 * @param {string} fileName
 * @returns {string | null}
 */
export function shapeNameForFile(fileName) {
  const stem = normalise(path.basename(fileName).replace(/\.json$/i, ''));
  let best = null;
  let bestLength = 0;
  for (const name of PAYLOAD_SHAPES) {
    for (const key of new Set([name, name.replace(/Payload$/, ''), ...(FILE_ALIASES[name] ?? [])].map(normalise))) {
      if (stem.startsWith(key) && key.length > bestLength) {
        best = name;
        bestLength = key.length;
      }
    }
  }
  return best;
}

// ---- command line ------------------------------------------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @param {(line: string) => void} [out]
 * @returns {number} the exit code
 */
export function main(argv, out = (line) => console.log(line)) {
  let shape = null;
  const files = [];
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--shape') shape = argv[(i += 1)];
    else files.push(argv[i]);
  }
  if (files.length === 0) {
    out('usage: node tests/contract/payloadShape.mjs [--shape <Name>] <file.json>...');
    out(`shapes: ${Object.keys(SHAPES).join(', ')}`);
    return 2;
  }
  let failed = false;
  for (const file of files) {
    const name = shape ?? shapeNameForFile(file);
    if (!name || !SHAPES[name]) {
      failed = true;
      out(`FAIL ${file}: no shape for this file name; pass --shape <${Object.keys(SHAPES).join('|')}>`);
      continue;
    }
    let json;
    try {
      json = JSON.parse(fs.readFileSync(file, 'utf8'));
    } catch (err) {
      failed = true;
      out(`FAIL ${file}: ${err.message}`);
      continue;
    }
    const errors = checkShape(name, json);
    if (errors.length === 0) out(`PASS ${file} (${name})`);
    for (const error of errors) {
      failed = true;
      out(`FAIL ${file} (${name}): ${error}`);
    }
  }
  return failed ? 1 : 0;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) process.exitCode = main(process.argv.slice(2));
