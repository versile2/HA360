// Tests for tools/ci/guards.mjs and tools/ci/validate-addon.mjs (03 section 7.3, 04 card S1a).
// Every case is built by the test itself in a temporary directory (R3-013): there are no committed fixture trees.
// A tree starts from baseline() (a small valid add-on) and each case overrides or deletes (null) files.
// An AC id is spelled through ac() so that this file never contains one in the [AC-nn] title form (ac-coverage
// scans tests/js/** on the real tree).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { ALLOWABLE, GUARD_NAMES, runGuards, stripComments } from '../../tools/ci/guards.mjs';
import { validateAddon } from '../../tools/ci/validate-addon.mjs';
import { repoRoot, tempDir, writeFile } from './helpers/ci-harness.mjs';

const ac = (nn) => `[AC-${nn}]`;
const pad = (n) => String(n).padStart(2, '0');

// ---------------------------------------------------------------------------------------------
// Trees
// ---------------------------------------------------------------------------------------------

function png(width, height) {
  const bytes = Buffer.alloc(33);
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).copy(bytes);
  bytes.writeUInt32BE(13, 8);
  bytes.write('IHDR', 12, 'latin1');
  bytes.writeUInt32BE(width, 16);
  bytes.writeUInt32BE(height, 20);
  return bytes;
}

const CONFIG = `name: "The Test Realm"
version: "0.1.0"
slug: realm
description: "A test add-on."
arch:
  - amd64
image: "ghcr.io/versile2/ha360"
ingress: true
ingress_port: 8099
watchdog: "http://[HOST]:[PORT:8099]/healthz"
options:
  log_level: information
  demo_mode: false
  members: []
schema:
  log_level: "list(trace|debug|information)"
  demo_mode: bool
  me_fallback_member: "str?"
  members:
    - id: "str"
      kind: "list(live|static)?"
`;

const TRANSLATIONS = `configuration:
  log_level:
    name: "Log level"
    description: "How much is logged."
  demo_mode:
    name: "Demo mode"
    description: "Shows a fictional family."
  me_fallback_member:
    name: "Fallback member"
    description: "Who counts as me when Home Assistant does not say."
  members:
    name: "Members"
    description: "The household."
network: {}
`;

const PACKAGES_PROPS = `<Project>
  <ItemGroup>
    <PackageVersion Include="MudBlazor" Version="9.5.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
  </ItemGroup>
</Project>
`;

function baseline() {
  return {
    'realm/config.yaml': CONFIG,
    'realm/translations/en.yaml': TRANSLATIONS,
    'realm/CHANGELOG.md': '# Changelog\n\n## 0.1.0\n\nFirst release.\n',
    'realm/icon.png': png(128, 128),
    'realm/logo.png': png(250, 100),
    'repository.yaml': 'name: "Test Apps"\nurl: "https://example.invalid/repo"\nmaintainer: "tester"\n',
    'Directory.Packages.props': PACKAGES_PROPS,
    'tools/ci/option-bindings.json': JSON.stringify({ keys: ['log_level', 'demo_mode', 'me_fallback_member', 'members'], bindings: {} }),
    'tools/ci/guards.allow.json': '{}\n',
    'tools/ci/packages.allow.txt': '# test list\nMudBlazor\nxunit\n',
    'tools/ci/ac-scope.json': JSON.stringify({ mode: 'report', enforced: [] }),
    'tools/ci/testids.json': JSON.stringify({ testids: [] }),
  };
}

function build(overrides = {}) {
  const root = tempDir('guards');
  for (const [rel, content] of Object.entries({ ...baseline(), ...overrides })) {
    if (content !== null) writeFile(path.join(root, rel), content);
  }
  return root;
}

const run = (overrides) => runGuards(build(overrides));
const web = (rel, content) => ({ [`src/Realm.Web/${rel}`]: content });
const allow = (guard, ...entries) => ({ 'tools/ci/guards.allow.json': JSON.stringify({ [guard]: entries }) });
const config = (edit) => ({ 'realm/config.yaml': edit(CONFIG) });

// ---------------------------------------------------------------------------------------------
// Reading the output
// ---------------------------------------------------------------------------------------------

function linesOf(result, name) {
  return result.lines.filter((line) => line.startsWith(`PASS ${name}`) || line.startsWith(`FAIL ${name}:`) || line.startsWith(`REPORT ${name}:`));
}

const failedGuards = (result) => [...new Set(result.lines.filter((l) => l.startsWith('FAIL ')).map((l) => /^FAIL ([a-z-]+)/.exec(l)[1]))];
const dump = (result) => `\n${result.lines.join('\n')}`;

// The guard `name` must be the only one that fails, with a FAIL line matching `pattern`.
function expectFail(overrides, name, pattern) {
  const result = run(overrides);
  assert.equal(result.failed, true, `expected a failure${dump(result)}`);
  assert.deepEqual(failedGuards(result), [name], `only ${name} should fail${dump(result)}`);
  assert.ok(linesOf(result, name).some((line) => line.startsWith('FAIL') && pattern.test(line)), `no FAIL ${name} line matches ${pattern}${dump(result)}`);
  return result;
}

// No guard fails, and the lines of `name` are all PASS (with `suffix` after the name, if given).
function expectPass(overrides, name, suffix = '') {
  const result = run(overrides);
  assert.equal(result.failed, false, `expected no failure${dump(result)}`);
  assert.deepEqual(linesOf(result, name), [`PASS ${name}${suffix}`], dump(result));
  return result;
}

// ---------------------------------------------------------------------------------------------
// The framework
// ---------------------------------------------------------------------------------------------

test('the baseline add-on passes the twelve guards of 03 section 7.3, one line each, in order', () => {
  assert.equal(GUARD_NAMES.length, 12);
  const result = run();
  assert.equal(result.failed, false, dump(result));
  assert.deepEqual(result.lines.map((line) => /^PASS ([a-z-]+)/.exec(line)?.[1]), GUARD_NAMES, dump(result));
});

test('a guard whose target does not exist yet prints PASS name (nothing to check yet)', () => {
  const result = run();
  for (const name of ['no-leading-slash', 'no-static-files', 'no-wallclock', 'no-colour-literals', 'vendored-maplibre', 'font-budget', 'testid-contract', 'ac-coverage']) {
    assert.deepEqual(linesOf(result, name), [`PASS ${name} (nothing to check yet)`]);
  }
  assert.match(linesOf(result, 'addon-validate')[0], /^PASS addon-validate \(no Dockerfile yet/, 'the Dockerfile check is skipped, not failed');
  assert.deepEqual(linesOf(result, 'package-pins'), ['PASS package-pins'], 'the props file is a target');
});

test('an unexpected exception inside a guard is a FAIL line, not a crash', () => {
  // realm/config.yaml as a directory makes the read throw something other than ENOENT.
  const root = build({ 'realm/config.yaml': null });
  fs.mkdirSync(path.join(root, 'realm', 'config.yaml'));
  const result = runGuards(root);
  assert.equal(result.failed, true);
  assert.ok(result.lines.some((l) => l.startsWith('FAIL addon-validate:')), dump(result));
});

test('stripComments keeps line numbers and does not mistake strings for comments', () => {
  const code = 'a(); // gone\nb("http://kept");\n/* two\nlines */ c();\nd("*/*");\ne("it // stays");';
  const out = stripComments(code, 'code');
  assert.equal(out.split('\n').length, code.split('\n').length);
  assert.ok(!out.includes('gone') && !out.includes('two'));
  assert.ok(out.includes('"http://kept"') && out.includes('"*/*"') && out.includes('"it // stays"') && out.includes('c();'));
  assert.ok(!stripComments('/* #fff */ x', 'css').includes('#fff'));
  assert.ok(stripComments('a // not a css comment', 'css').includes('// not a css comment'));
  assert.ok(!stripComments('<!-- #fff --> @* #eee *@ ok', 'markup').match(/#(?:fff|eee)/));
});

// ---------------------------------------------------------------------------------------------
// The allow-list
// ---------------------------------------------------------------------------------------------

const NAV = { 'src/Realm.Web/Nav.cs': 'class Nav { void Go(NavigationManager nav) { nav.NavigateTo("/driving"); } }\n' };

test('allow-list: an entry with a reason silences its file (and only the lines containing `contains`)', () => {
  const entry = { file: 'src/Realm.Web/Nav.cs', contains: 'NavigateTo', reason: 'a route name, not a URL' };
  expectPass({ ...NAV, ...allow('no-leading-slash', entry) }, 'no-leading-slash');
  expectFail({ ...NAV, ...allow('no-leading-slash', { ...entry, contains: 'Redirect' }) }, 'no-leading-slash', /Nav\.cs:1 NavigateTo/);
  expectFail({ ...NAV, ...allow('no-leading-slash', { ...entry, file: 'src/Realm.Web/Other.cs' }) }, 'no-leading-slash', /Nav\.cs:1/);
});

test('allow-list: an entry without a reason is a FAIL and silences nothing; so are an unknown guard and bad JSON', () => {
  for (const entry of [{ file: 'src/Realm.Web/Nav.cs' }, { file: 'src/Realm.Web/Nav.cs', reason: '  ' }, { reason: 'no file' }]) {
    const result = run({ ...NAV, ...allow('no-leading-slash', entry) });
    assert.ok(result.lines.some((l) => /^FAIL allow-list: .*(?:needs a non-empty 'reason'|needs a 'file')/.test(l)), dump(result));
    assert.ok(result.lines.some((l) => l.startsWith('FAIL no-leading-slash:')), 'the finding is not silenced');
  }
  const unknown = run({ 'tools/ci/guards.allow.json': JSON.stringify({ 'no-such-guard': [{ file: 'x', reason: 'y' }] }) });
  assert.ok(unknown.lines.some((l) => /^FAIL allow-list: .*'no-such-guard' is not a guard/.test(l)), dump(unknown));
  const broken = run({ 'tools/ci/guards.allow.json': '{ nope' });
  assert.ok(broken.lines.some((l) => /^FAIL allow-list: .*not valid JSON/.test(l)), dump(broken));
});

test('allow-list: every entry of the real tools/ci/guards.allow.json names an allowable guard, a file and a reason', () => {
  const real = JSON.parse(fs.readFileSync(path.join(repoRoot, 'tools', 'ci', 'guards.allow.json'), 'utf8'));
  for (const [guard, entries] of Object.entries(real)) {
    assert.ok(ALLOWABLE.includes(guard), `${guard} takes no exceptions`);
    assert.ok(Array.isArray(entries));
    for (const entry of entries) {
      assert.equal(typeof entry.file, 'string');
      assert.ok(entry.file.length > 0);
      assert.equal(typeof entry.reason, 'string');
      assert.ok(entry.reason.trim().length >= 10, `${entry.file} needs a real reason`);
    }
  }
});

// ---------------------------------------------------------------------------------------------
// config-name
// ---------------------------------------------------------------------------------------------

test('config-name: passes with realm/config.yaml alone, ignoring node_modules, dot directories and build output', () => {
  const ignored = {};
  for (const dir of ['node_modules/x', '.git', '.private', '.github', 'src/Realm.Web/bin/Release', 'src/Realm.Web/obj', 'publish']) ignored[`${dir}/config.json`] = '{}';
  expectPass(ignored, 'config-name');
});

test('config-name: fails on any other config.yaml, config.yml or config.json', () => {
  expectFail({ 'tests/e2e/fixtures/config.json': '{}' }, 'config-name', /tests\/e2e\/fixtures\/config\.json would register a second app/);
  expectFail({ 'config.yml': 'a: 1' }, 'config-name', /^FAIL config-name: config\.yml /);
  expectFail({ 'src/Realm.Web/wwwroot/config.yaml': 'a: 1' }, 'config-name', /wwwroot\/config\.yaml/);
});

// ---------------------------------------------------------------------------------------------
// addon-validate (validate-addon.mjs)
// ---------------------------------------------------------------------------------------------

const DOCKERFILE = (port) => `FROM scratch\nENV ASPNETCORE_HTTP_PORTS=${port} \\\n    DOTNET_gcServer=0\n`;

test('addon-validate: the baseline passes; a newer top CHANGELOG section, the Dockerfile and the watchdog variants are fine', () => {
  expectPass({}, 'addon-validate', ' (no Dockerfile yet, so ingress_port was not compared with ASPNETCORE_HTTP_PORTS)');
  expectPass({ 'realm/CHANGELOG.md': '# Changelog\n\n## 0.2.0\n\nNext.\n\n## 0.1.0\n\nFirst.\n' }, 'addon-validate', ' (no Dockerfile yet, so ingress_port was not compared with ASPNETCORE_HTTP_PORTS)');
  expectPass({ Dockerfile: DOCKERFILE(8099) }, 'addon-validate');
  for (const watchdog of ['tcp://[HOST]:[PORT:8099]', 'https://[HOST]:8099/healthz', '[PROTO:ssl]://[HOST]:[PORT:8099]/x']) {
    const result = run(config((c) => c.replace(/^watchdog: .*$/m, `watchdog: "${watchdog}"`)));
    assert.equal(result.failed, false, `${watchdog}${dump(result)}`);
  }
  const noWatchdog = run(config((c) => c.replace(/^watchdog: .*\n/m, '')));
  assert.equal(noWatchdog.failed, false, 'watchdog is optional');
});

const ADDON_FAILURES = [
  ['a required key is missing', config((c) => c.replace(/^description: .*\n/m, '')), /required key 'description' is missing/],
  ['arch is empty', config((c) => c.replace('arch:\n  - amd64\n', 'arch: []\n')), /required key 'arch' is missing/],
  ['image carries a tag', config((c) => c.replace('ha360"', 'ha360:0.1.0"')), /image must be exactly 'ghcr\.io\/versile2\/ha360'/],
  ['image uses {arch}', config((c) => c.replace('ha360"', 'ha360-{arch}"')), /image must be exactly/],
  ['image is not lower case', config((c) => c.replace('versile2', 'Versile2')), /image must be exactly/],
  ['image is absent', config((c) => c.replace(/^image: .*\n/m, '')), /found none/],
  ['version is not plain semver', config((c) => c.replace('"0.1.0"', '"0.1"')), /version '0\.1' is not plain MAJOR\.MINOR\.PATCH/],
  ['version has a build suffix', config((c) => c.replace('"0.1.0"', '"0.1.0+7"')), /not plain MAJOR\.MINOR\.PATCH/],
  ['CHANGELOG has no section for the version', { 'realm/CHANGELOG.md': '# Changelog\n\n## 0.0.9\n' }, /no '## 0\.1\.0' section/],
  ['CHANGELOG is missing', { 'realm/CHANGELOG.md': null }, /realm\/CHANGELOG\.md is missing/],
  ['ingress_port differs from the Dockerfile', { Dockerfile: DOCKERFILE(8080) }, /Dockerfile ASPNETCORE_HTTP_PORTS=8080 differs from ingress_port 8099/],
  ['the Dockerfile sets no port', { Dockerfile: 'FROM scratch\n' }, /sets no ASPNETCORE_HTTP_PORTS/],
  ['watchdog does not match the Supervisor pattern', config((c) => c.replace(/^watchdog: .*$/m, 'watchdog: "http://localhost:8099/healthz"')), /watchdog 'http:\/\/localhost:8099\/healthz' does not match/],
  ['options nest deeper than 2', config((c) => c.replace('      kind: "list(live|static)?"', '      kind: "list(live|static)?"\n      tags:\n        - [str]')), /schema\.members nests deeper than 2 levels/],
  ['an options key lacks a schema entry', config((c) => c.replace('  members: []\nschema', '  members: []\n  extra: 1\nschema')), /option 'extra' has no entry in schema/],
  ['a password option has a default', config((c) => c.replace('  members: []\nschema', '  members: []\n  secret: hunter2\nschema').replace('  demo_mode: bool\n', '  demo_mode: bool\n  secret: password\n')), /option 'secret' is typed password/],
  ['a map entry exists', config((c) => `${c}map:\n  - type: data\n`), /'map' is declared/],
  ['build.yaml exists', { 'realm/build.yaml': 'build_from: {}\n' }, /realm\/build\.yaml build\.yaml is no longer used/],
  ['icon.png is not 128 x 128', { 'realm/icon.png': png(64, 64) }, /realm\/icon\.png is 64 x 64, expected 128 x 128/],
  ['icon.png is missing', { 'realm/icon.png': null }, /realm\/icon\.png is missing/],
  ['logo.png is not about 250 x 100', { 'realm/logo.png': png(500, 500) }, /realm\/logo\.png is 500 x 500/],
  ['logo.png is not a PNG', { 'realm/logo.png': 'not a png at all, but long enough to read a header' }, /realm\/logo\.png is not a PNG file/],
  ['repository.yaml lacks name', { 'repository.yaml': 'url: "https://example.invalid"\n' }, /repository\.yaml lacks the required key 'name'/],
  ['repository.yaml is missing', { 'repository.yaml': null }, /repository\.yaml is missing/],
  ['config.yaml is missing', { 'realm/config.yaml': null }, /realm\/config\.yaml is missing/],
  ['config.yaml is not YAML', { 'realm/config.yaml': 'name: [unclosed\n' }, /realm\/config\.yaml is not valid YAML/],
];

for (const [label, overrides, pattern] of ADDON_FAILURES) {
  test(`addon-validate: fails when ${label}`, () => {
    const result = run(overrides);
    assert.equal(result.failed, true, dump(result));
    assert.ok(linesOf(result, 'addon-validate').some((l) => l.startsWith('FAIL addon-validate:') && pattern.test(l)), `${pattern}${dump(result)}`);
  });
}

test('addon-validate: a password option without a default is fine; an absent ingress_port means 8099', () => {
  const secret = config((c) => c.replace('  demo_mode: bool\n', '  demo_mode: bool\n  secret: "password?"\n'));
  // (the extra schema key has no translation, which option-bindings reports: only addon-validate is looked at here)
  assert.deepEqual(linesOf(run({ ...secret, Dockerfile: DOCKERFILE(8099) }), 'addon-validate'), ['PASS addon-validate']);
  const noPort = config((c) => c.replace(/^ingress_port: .*\n/m, ''));
  expectPass({ ...noPort, Dockerfile: DOCKERFILE(8099) }, 'addon-validate');
  expectFail({ ...noPort, Dockerfile: DOCKERFILE(8100) }, 'addon-validate', /ASPNETCORE_HTTP_PORTS=8100 differs from ingress_port 8099/);
});

test('validate-addon.mjs runs on its own: PASS exits 0, FAIL lines exit 1, a bad argument exits 64', () => {
  const script = path.join(repoRoot, 'tools', 'ci', 'validate-addon.mjs');
  const good = spawnSync('node', [script, '--root', build()], { encoding: 'utf8' });
  assert.equal(good.status, 0, good.stderr);
  assert.match(good.stdout, /^PASS addon-validate/);
  const bad = spawnSync('node', [script, '--root', build({ 'realm/icon.png': png(64, 64) })], { encoding: 'utf8' });
  assert.equal(bad.status, 1);
  assert.match(bad.stdout, /^FAIL addon-validate: realm\/icon\.png is 64 x 64/m);
  assert.equal(spawnSync('node', [script, '--nope'], { encoding: 'utf8' }).status, 64);
  assert.deepEqual(validateAddon(build()).problems, []);
});

// ---------------------------------------------------------------------------------------------
// option-bindings
// ---------------------------------------------------------------------------------------------

const bindingKeys = (keys) => ({ 'tools/ci/option-bindings.json': JSON.stringify({ keys, bindings: {} }) });

test('option-bindings: passes when schema, translations and option-bindings.json agree', () => {
  expectPass({}, 'option-bindings');
});

test('option-bindings: fails on every kind of disagreement', () => {
  const all = ['log_level', 'demo_mode', 'me_fallback_member', 'members'];
  expectFail({ 'realm/translations/en.yaml': TRANSLATIONS.replace(/  demo_mode:\n.*\n.*\n/, '') }, 'option-bindings', /schema key 'demo_mode' has no entry under configuration/);
  expectFail({ 'realm/translations/en.yaml': TRANSLATIONS.replace('network: {}', '  ghost:\n    name: x\n    description: y\nnetwork: {}') }, 'option-bindings', /translation 'ghost' is not a schema key/);
  expectFail({ 'realm/translations/en.yaml': null }, 'option-bindings', /translations\/en\.yaml is missing/);
  expectFail(bindingKeys(all.slice(1)), 'option-bindings', /schema key 'log_level' is not in "keys"/);
  expectFail(bindingKeys([...all, 'phantom']), 'option-bindings', /"keys" entry 'phantom' is not a schema key/);
  expectFail({ 'tools/ci/option-bindings.json': null }, 'option-bindings', /option-bindings\.json is missing/);
  expectFail(config((c) => c.replace('  members: []\nschema', '  members: []\n  me_fallback_member: x\nschema')), 'option-bindings', /optional option 'me_fallback_member' must not appear in options \(the Supervisor would treat it as required, R-068\)/);
  expectFail(config((c) => c.replace('trace|debug', 'trace|Debug')), 'option-bindings', /log_level: enumerated value 'Debug' is not lower-case \(R-069\)/);
  expectFail(config((c) => c.replace('list(live|static)?', 'list(Live|static)?')), 'option-bindings', /members\[\]\.kind: enumerated value 'Live' is not lower-case/);
});

// ---------------------------------------------------------------------------------------------
// no-leading-slash
// ---------------------------------------------------------------------------------------------

test('no-leading-slash: passes on relative links, LocalRedirect, comments and absolute URLs', () => {
  expectPass(
    {
      ...web('Pages/A.razor', '<a href="driving">Driving</a>\n<img src="./img/x.svg">\n@* <a href="/old"> *@\n<!-- <a href="/old"> -->\n<a href="https://example.invalid/">x</a>\n'),
      ...web('Nav.cs', '// NavigateTo("/x") is banned\nvar r = Results.LocalRedirect($"{ctx.Request.PathBase}/x");\nnav.NavigateTo("driving");\nvar u = new Uri(baseUri, "relative");\n'),
    },
    'no-leading-slash',
  );
});

const LEADING_SLASH = [
  ['href="/', web('Pages/A.razor', '<a href="/driving">x</a>\n'), /Pages\/A\.razor:1 href="\/ or src="\/ starts at the server root/],
  ['src="/ (single quotes)', web('Pages/A.razor', "<img src='/img/x.svg'>\n"), /Pages\/A\.razor:1/],
  ['href=\\"/ inside a C# string', web('Html.cs', 'var h = "<a href=\\"/x\\">";\n'), /Html\.cs:1/],
  ['NavigateTo("/', web('Nav.cs', 'nav.NavigateTo("/driving");\n'), /Nav\.cs:1 NavigateTo/],
  ['NavigateTo($"/', web('Nav.cs', 'nav.NavigateTo($"/driving/{id}");\n'), /Nav\.cs:1 NavigateTo/],
  ['Results.Redirect(', web('Ep.cs', 'return Results.Redirect("x");\n'), /Ep\.cs:1 Results\.Redirect\(/],
  ['Response.Redirect(', web('Ep.cs', 'ctx.Response.Redirect("x");\n'), /Ep\.cs:1/],
  ['new Uri(..., "/', web('Ep.cs', 'var u = new Uri(Base(a), "/x");\n'), /Ep\.cs:1 new Uri/],
  ['href="/ in a script file', web('wwwroot/js/a.js', 'el.innerHTML = \'<a href="/x">\';\n'), /a\.js:1/],
];

for (const [label, overrides, pattern] of LEADING_SLASH) {
  test(`no-leading-slash: fails on ${label}`, () => {
    expectFail(overrides, 'no-leading-slash', pattern);
  });
}

// ---------------------------------------------------------------------------------------------
// no-static-files
// ---------------------------------------------------------------------------------------------

test('no-static-files: passes on MapStaticAssets, framework @Assets and comments', () => {
  expectPass(
    { ...web('Program.cs', 'app.MapStaticAssets();\n// never app.UseStaticFiles()\n'), ...web('Components/App.razor', '<script src="@Assets["_framework/blazor.web.js"]"></script>\n') },
    'no-static-files',
  );
});

test('no-static-files: fails on UseStaticFiles, UseRealmStaticFiles and @Assets of our own js/lib/css/fonts/img files', () => {
  expectFail(web('Program.cs', 'app.UseStaticFiles();\n'), 'no-static-files', /Program\.cs:1 UseStaticFiles and UseRealmStaticFiles/);
  expectFail(web('Program.cs', 'app.UseRealmStaticFiles();\n'), 'no-static-files', /Program\.cs:1/);
  for (const dir of ['js', 'lib', 'css', 'fonts', 'img']) {
    expectFail(web('Components/App.razor', `<link href="@Assets["${dir}/x"]">\n`), 'no-static-files', /App\.razor:1 @Assets\[\.\.\.\] fingerprints/);
  }
});

// ---------------------------------------------------------------------------------------------
// no-wallclock
// ---------------------------------------------------------------------------------------------

test('no-wallclock: passes on TimeProvider and comments', () => {
  expectPass(web('Clock.cs', 'var now = time.GetUtcNow();\nvar t = TimeProvider.System.GetTimestamp();\n// DateTime.UtcNow is banned\nvar my = MyDateTime.Now;\n'), 'no-wallclock');
});

test('no-wallclock: fails on each wall-clock read, and an allow-listed clock adapter is exempt', () => {
  for (const call of ['DateTime.Now', 'DateTime.UtcNow', 'DateTimeOffset.Now', 'DateTimeOffset.UtcNow', 'TimeZoneInfo.Local', 'Stopwatch.GetTimestamp()', 'System.DateTime.UtcNow.Year']) {
    expectFail(web('Clock.cs', `var x = ${call};\n`), 'no-wallclock', /Clock\.cs:1 reads the wall clock/);
  }
  expectFail(web('Pages/P.razor', '@code { var x = DateTime.Now; }\n'), 'no-wallclock', /P\.razor:1/);
  expectPass({ ...web('Clock.cs', 'var x = DateTime.UtcNow;\n'), ...allow('no-wallclock', { file: 'src/Realm.Web/Clock.cs', reason: 'the clock adapter wraps the system clock' }) }, 'no-wallclock');
});

// ---------------------------------------------------------------------------------------------
// no-colour-literals
// ---------------------------------------------------------------------------------------------

test('no-colour-literals: passes on tokens, entities, anchors, comments, other file types and the vendored folder', () => {
  expectPass(
    {
      ...web('wwwroot/css/app.css', '/* #fff is banned */\n:root { color: var(--realm-fg); }\n#sheet { background: var(--realm-bg); }\n.x::after { content: "\\2192"; }\n'),
      ...web('Components/A.razor', '<p>&#8594; next</p>\n<a href="#main-content">skip</a>\n<div style="color: var(--realm-fg)"></div>\n'),
      ...web('Components/A.razor.css', '.a { color: var(--realm-fg); }\n'),
      ...web('wwwroot/js/realmMap.js', '// "#ff00ff" would be a literal\nconst id = "#map";\n'),
      ...web('Theme/RealmPalette.cs', 'public const string Bg = "#0B1020";\n'),
      ...web('wwwroot/lib/thirdparty/vendor.css', '.a { color: rgb(1, 2, 3); }\n'),
    },
    'no-colour-literals',
  );
});

test('no-colour-literals: fails on hex and rgb() literals in razor, scoped css, css and js', () => {
  expectFail(web('Components/A.razor', '<div style="color:#fff"></div>\n'), 'no-colour-literals', /A\.razor:1 colour literal/);
  expectFail(web('Components/A.razor.css', '.a { color: #1a2b3c; }\n'), 'no-colour-literals', /A\.razor\.css:1/);
  expectFail(web('wwwroot/css/app.css', '.a {\n  color: #1a2b3c80;\n}\n'), 'no-colour-literals', /app\.css:2/);
  expectFail(web('wwwroot/css/app.css', '.a { background: rgba(0, 0, 0, .5); }\n'), 'no-colour-literals', /app\.css:1 rgb\(\) colour literal/);
  expectFail(web('wwwroot/css/app.css', '.a { color: rgb(0 0 0); }\n'), 'no-colour-literals', /app\.css:1/);
  expectFail(web('wwwroot/js/realmMap.js', "const c = '#ABC';\n"), 'no-colour-literals', /realmMap\.js:1/);
});

test('no-colour-literals: the allow-listed demo style is exempt, another script is not', () => {
  const literal = "export const demo = { paint: { 'background-color': '#123456' } };\n";
  expectPass({ ...web('wwwroot/js/mapStyles.js', literal), ...allow('no-colour-literals', { file: 'src/Realm.Web/wwwroot/js/mapStyles.js', reason: 'the demo style is a MapLibre style object' }) }, 'no-colour-literals');
  expectFail({ ...web('wwwroot/js/other.js', literal), ...allow('no-colour-literals', { file: 'src/Realm.Web/wwwroot/js/mapStyles.js', reason: 'the demo style is a MapLibre style object' }) }, 'no-colour-literals', /other\.js:1/);
});

// ---------------------------------------------------------------------------------------------
// vendored-maplibre
// ---------------------------------------------------------------------------------------------

const VENDOR = 'src/Realm.Web/wwwroot/lib/maplibre-gl';
const PACKAGE_JSON = (version) => JSON.stringify({ name: 'x', devDependencies: { 'maplibre-gl': version } });

function vendored({ entry = 'export const a = 1;\n', worker = 'export const w = 1;\n', pin = '6.11.2', versionText = 'maplibre-gl 6.11.2\n' } = {}) {
  const sha = (text) => crypto.createHash('sha256').update(text).digest('hex');
  return {
    [`${VENDOR}/maplibre-gl.mjs`]: entry,
    [`${VENDOR}/maplibre-gl-worker.mjs`]: worker,
    [`${VENDOR}/SHA256SUMS`]: `${sha('export const a = 1;\n')}  maplibre-gl.mjs\n${sha('export const w = 1;\n')} *maplibre-gl-worker.mjs\n`,
    [`${VENDOR}/VERSION.txt`]: versionText,
    'package.json': PACKAGE_JSON(pin),
  };
}

test('vendored-maplibre: passes when the digests match and VERSION.txt agrees with package.json', () => {
  expectPass(vendored(), 'vendored-maplibre');
  expectPass(vendored({ pin: '^6.11.2' }), 'vendored-maplibre');
});

test('vendored-maplibre: fails on a changed file, a missing file or SHA256SUMS, and a version disagreement', () => {
  expectFail(vendored({ entry: 'export const a = 2;\n' }), 'vendored-maplibre', /maplibre-gl\.mjs sha256 [0-9a-f]{64} differs from SHA256SUMS/);
  expectFail({ ...vendored(), [`${VENDOR}/maplibre-gl-worker.mjs`]: null }, 'vendored-maplibre', /maplibre-gl-worker\.mjs is listed in SHA256SUMS but missing/);
  expectFail({ ...vendored(), [`${VENDOR}/SHA256SUMS`]: null }, 'vendored-maplibre', /SHA256SUMS is missing/);
  expectFail(vendored({ pin: '6.11.3' }), 'vendored-maplibre', /VERSION\.txt says maplibre-gl 6.11.2 but package\.json has 6.11.3/);
  expectFail({ ...vendored(), 'package.json': '{}' }, 'vendored-maplibre', /package\.json has no maplibre-gl/);
  expectFail(vendored({ versionText: 'something else\n' }), 'vendored-maplibre', /VERSION\.txt does not say "maplibre-gl <version>"/);
});

// ---------------------------------------------------------------------------------------------
// font-budget
// ---------------------------------------------------------------------------------------------

const FONTS = 'src/Realm.Web/wwwroot/fonts';
const fontFiles = (...sizes) => Object.fromEntries([...sizes.map((size, i) => [`${FONTS}/f${i}.woff2`, Buffer.alloc(size)]), [`${FONTS}/OFL-families.txt`, 'SIL Open Font License']]);

test('font-budget: passes at exactly 153,600 bytes with an OFL text, and on a comment that names a font host', () => {
  expectPass({ ...fontFiles(100_000, 53_600), ...web('wwwroot/css/fonts.css', '/* no fonts.googleapis.com here */\n@font-face { src: url(../fonts/f0.woff2); }\n') }, 'font-budget');
});

test('font-budget: fails over the budget, without an OFL text, and on a Google Fonts host', () => {
  expectFail(fontFiles(100_000, 53_601), 'font-budget', /the \.woff2 fonts total 153601 bytes, over the budget of 153600/);
  expectFail({ [`${FONTS}/f0.woff2`]: Buffer.alloc(10) }, 'font-budget', /wwwroot\/fonts holds fonts but no OFL text/);
  expectFail(web('wwwroot/css/fonts.css', '@import url("https://fonts.googleapis.com/css2?family=Cinzel");\n'), 'font-budget', /fonts\.css:1 a Google Fonts host/);
  expectFail(web('Components/App.razor', '<link rel="preconnect" href="https://fonts.gstatic.com">\n'), 'font-budget', /App\.razor:1 a Google Fonts host/);
});

// ---------------------------------------------------------------------------------------------
// package-pins
// ---------------------------------------------------------------------------------------------

const csproj = (reference) => ({ 'src/Realm.Web/Realm.Web.csproj': `<Project Sdk="Microsoft.NET.Sdk.Web">\n  <ItemGroup>\n    ${reference}\n  </ItemGroup>\n</Project>\n` });
const props = (version, id = 'MudBlazor') => ({ 'Directory.Packages.props': `<Project>\n  <ItemGroup>\n    <PackageVersion Include="${id}" Version="${version}" />\n  </ItemGroup>\n</Project>\n` });

test('package-pins: passes on central exact pins, versionless references, case differences and XML comments', () => {
  expectPass(csproj('<PackageReference Include="MudBlazor" />'), 'package-pins');
  expectPass(csproj('<PackageReference Include="MUDBLAZOR" />'), 'package-pins');
  expectPass({ ...csproj('<!-- <PackageReference Include="Evil" Version="1.0.0" /> -->'), ...props('9.5.0') }, 'package-pins');
  expectPass(props('10.0.12-rc.1', 'xunit'), 'package-pins');
});

test('package-pins: fails on Version=, ranges, wildcards, unknown packages and a moved MudBlazor', () => {
  expectFail(csproj('<PackageReference Include="MudBlazor" Version="9.5.0" />'), 'package-pins', /Realm\.Web\.csproj:3 PackageReference 'MudBlazor' carries Version=/);
  expectFail(csproj('<PackageReference\n      Include="xunit"\n      Version="2.9.3" />'), 'package-pins', /Realm\.Web\.csproj:3 PackageReference 'xunit' carries Version=/);
  expectFail(props('[9.0,10.0)'), 'package-pins', /Directory\.Packages\.props:3 PackageVersion 'MudBlazor' must be an exact version, found '\[9\.0,10\.0\)'/);
  expectFail(props('9.*'), 'package-pins', /found '9\.\*'/);
  expectFail(props('9.5.0-*'), 'package-pins', /found '9\.5\.0-\*'/);
  expectFail(csproj('<PackageReference Include="Newtonsoft.Json" />'), 'package-pins', /package 'Newtonsoft\.Json' is not in tools\/ci\/packages\.allow\.txt/);
  expectFail(props('9.11.0'), 'package-pins', /MudBlazor is pinned to 9\.5\.0 \(D15\), found 9\.11\.0/);
  expectFail({ 'tools/ci/packages.allow.txt': null }, 'package-pins', /package 'MudBlazor' is not in tools\/ci\/packages\.allow\.txt/);
});

test('package-pins: a recorded decision (an allow-list entry with a reason) lets MudBlazor move', () => {
  const entry = { file: 'Directory.Packages.props', contains: 'Include="MudBlazor"', reason: 'decision D99: MudX 9.11 is verified in CI' };
  expectPass({ ...props('9.11.0'), ...allow('package-pins', entry) }, 'package-pins');
});

// ---------------------------------------------------------------------------------------------
// testid-contract and ac-coverage (report-only until S15)
// ---------------------------------------------------------------------------------------------

const SCOPE = (mode, enforced = []) => ({ 'tools/ci/ac-scope.json': JSON.stringify({ mode, enforced }) });
const TESTIDS = (...ids) => ({ 'tools/ci/testids.json': JSON.stringify({ testids: ids }) });
const IDS = ['map-canvas', 'row-member-{id}', 'week-chip-{0..3}'];

test('testid-contract: REPORTs a missing id in report mode and never fails; passes when every id or prefix is a literal in src', () => {
  const partial = run({ ...TESTIDS(...IDS), ...web('Components/A.razor', '<div data-testid="map-canvas"></div>\n') });
  assert.equal(partial.failed, false, dump(partial));
  assert.deepEqual(linesOf(partial, 'testid-contract'), [
    "REPORT testid-contract: data-testid 'row-member-{id}' is not present in src/",
    "REPORT testid-contract: data-testid 'week-chip-{0..3}' is not present in src/",
  ]);
  const found = {
    ...TESTIDS(...IDS),
    ...web('Components/A.razor', '<div data-testid="map-canvas"></div>\n<li data-testid="row-member-@m.Id"></li>\n'),
    ...web('Components/B.cs', 'var id = $"week-chip-{n}";\n'),
  };
  expectPass(found, 'testid-contract');
});

test('testid-contract: a quoted literal is required, comments do not count, and enforce mode FAILs', () => {
  const sneaky = { ...TESTIDS('sheet'), ...web('Components/A.razor', '@* data-testid="sheet" *@\n<div class="sheet-body"></div>\n') };
  assert.match(linesOf(run(sneaky), 'testid-contract')[0], /^REPORT testid-contract: data-testid 'sheet' is not present/);
  expectFail({ ...TESTIDS('map-canvas'), ...SCOPE('enforce') }, 'testid-contract', /^FAIL testid-contract: data-testid 'map-canvas' is not present in src\//);
  expectFail({ 'tools/ci/testids.json': '{"testids": "map-canvas"}' }, 'testid-contract', /testids\.json must be JSON/);
});

const csTests = (...titles) => `public class T {\n${titles.map((t) => `  ${t}\n  public void M() {}`).join('\n')}\n}\n`;

test('ac-coverage: finds an AC id in a C# DisplayName ([Fact] and [Theory]) and in the title of a Playwright or node test', () => {
  const tests = {
    'tests/Realm.Web.Tests/A.cs': csTests(`[Fact(DisplayName = "${ac('01')} location opens")]`, `[Theory(DisplayName = "${ac('19a')} keep-out slide")]`),
    'tests/e2e/ac-a.spec.ts': `test('${ac('02')} nav', async () => {});\ntest.fail("${ac('47a')} focus", async () => {});\ntest(\`${ac('03')} gear\`, async () => {});\n`,
    'tests/js/b.test.mjs': `test('${ac('09')} padding', () => {});\n`,
  };
  const result = run(tests);
  assert.equal(result.failed, false, dump(result));
  const missing = linesOf(result, 'ac-coverage').map((l) => /AC-(\d\d) missing/.exec(l)?.[1]);
  for (const n of ['01', '02', '03', '09', '19', '47']) assert.ok(!missing.includes(n), `AC-${n} is covered`);
  assert.equal(missing.length, 50 - 6);
  assert.ok(missing.includes('04') && missing.includes('50'));
});

test('ac-coverage: report mode prints REPORT lines and never fails; text that is not a test title does not count', () => {
  const tests = {
    'tests/Realm.Web.Tests/A.cs': csTests(`// [Fact(DisplayName = "${ac('01')} commented out")]`, `[Fact(DisplayName = "no id here")] // ${ac('02')}`, `[Fact] public void ${'AC_03'}() {}`),
    'tests/e2e/a.spec.ts': `const note = '${ac('04')}';\n// test('${ac('05')} commented', () => {});\ntest.skip('${ac('06')} parked', () => {});\ntest('${ac('77')} no such row', () => {});\n`,
  };
  const result = run(tests);
  assert.equal(result.failed, false, dump(result));
  const lines = linesOf(result, 'ac-coverage');
  assert.ok(lines.every((l) => l.startsWith('REPORT ac-coverage: ')));
  assert.equal(lines.filter((l) => /AC-\d\d missing$/.test(l)).length, 50, 'none of the 50 is covered');
  assert.ok(lines.includes(`REPORT ac-coverage: tests/e2e/a.spec.ts ${ac('77')} matches no row of 01 section 11 (AC-01..AC-50)`));
});

const allFifty = (skip = []) => ({
  'tests/e2e/all.spec.ts': Array.from({ length: 50 }, (_, i) => i + 1)
    .filter((n) => !skip.includes(n))
    .map((n) => `test('${ac(pad(n))} title', async () => {});`)
    .join('\n'),
});

test('ac-coverage: passes with all fifty ids, REPORTs the missing ones, and FAILs them in enforce mode', () => {
  expectPass(allFifty(), 'ac-coverage');
  const partial = run(allFifty([33]));
  assert.equal(partial.failed, false);
  assert.deepEqual(linesOf(partial, 'ac-coverage'), ['REPORT ac-coverage: AC-33 missing']);
  expectFail({ ...allFifty([33]), ...SCOPE('enforce') }, 'ac-coverage', /^FAIL ac-coverage: AC-33 missing$/);
  expectPass({ ...allFifty(), ...SCOPE('enforce') }, 'ac-coverage');
  expectFail({ ...allFifty(), 'tests/e2e/x.spec.ts': `test('${ac('51')} nope', () => {});`, ...SCOPE('enforce') }, 'ac-coverage', /AC-51\] matches no row/);
});

test('ac-coverage: an id on the enforced list FAILs even in report mode; a bad ac-scope.json is a FAIL', () => {
  const result = run({ ...allFifty([7, 33]), ...SCOPE('report', ['AC-07']) });
  assert.equal(result.failed, true);
  assert.deepEqual(linesOf(result, 'ac-coverage'), ['FAIL ac-coverage: AC-07 missing', 'REPORT ac-coverage: AC-33 missing']);
  // the two report-only guards both need the switch, so both say so
  for (const [scope, pattern] of [['{"mode":"strict"}', /mode must be "report" or "enforce"/], [JSON.stringify({ mode: 'report', enforced: ['AC-99'] }), /enforced must be a list of ids/]]) {
    const broken = run({ ...allFifty(), ...TESTIDS('map-canvas'), 'tools/ci/ac-scope.json': scope });
    assert.deepEqual(failedGuards(broken), ['testid-contract', 'ac-coverage']);
    assert.ok(linesOf(broken, 'ac-coverage').every((l) => l.startsWith('FAIL') && pattern.test(l)), dump(broken));
  }
});

// ---------------------------------------------------------------------------------------------
// The real tree and the command line
// ---------------------------------------------------------------------------------------------

test('the real tree prints only PASS and REPORT lines and does not fail', () => {
  const result = runGuards(repoRoot);
  assert.equal(result.failed, false, dump(result));
  assert.ok(result.lines.every((l) => /^(?:PASS|REPORT) [a-z-]+/.test(l)), dump(result));
  for (const name of GUARD_NAMES) assert.ok(linesOf(result, name).length > 0, `${name} printed nothing`);
});

test('tools/ci/ac-scope.json and testids.json are created empty, in report mode (D50)', () => {
  const read = (name) => JSON.parse(fs.readFileSync(path.join(repoRoot, 'tools', 'ci', name), 'utf8'));
  assert.deepEqual(read('ac-scope.json'), { mode: 'report', enforced: [] });
  assert.deepEqual(read('testids.json'), { testids: [] });
});

test('this file never spells an AC id in the title form (ac-coverage scans tests/js on the real tree)', () => {
  const self = fs.readFileSync(fileURLToPath(import.meta.url), 'utf8');
  assert.ok(!/\[AC-\d/.test(self));
});

test('guards.mjs on the command line: exit 0 on PASS/REPORT, 1 on FAIL, 64 on a bad argument, under 30 s', () => {
  const script = path.join(repoRoot, 'tools', 'ci', 'guards.mjs');
  const started = Date.now();
  const real = spawnSync('node', [script], { encoding: 'utf8' });
  assert.ok(Date.now() - started < 30_000);
  assert.equal(real.status, 0, real.stdout + real.stderr);
  assert.ok(real.stdout.trimEnd().split('\n').every((l) => /^(?:PASS|REPORT) /.test(l)));

  const failing = spawnSync('node', [script, '--root', build({ 'tests/e2e/fixtures/config.json': '{}' })], { encoding: 'utf8' });
  assert.equal(failing.status, 1);
  assert.match(failing.stdout, /^FAIL config-name: tests\/e2e\/fixtures\/config\.json /m);
  assert.match(failing.stdout, /^PASS addon-validate/m, 'the other guards still run');

  const reportOnly = spawnSync('node', [script, '--root', build(allFifty([33]))], { encoding: 'utf8' });
  assert.equal(reportOnly.status, 0, 'REPORT lines never fail the run');
  assert.match(reportOnly.stdout, /^REPORT ac-coverage: AC-33 missing$/m);

  const usage = spawnSync('node', [script, '--bogus'], { encoding: 'utf8' });
  assert.equal(usage.status, 64);
  assert.match(usage.stderr, /usage: guards\.mjs/);
});
