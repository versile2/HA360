// Playwright config for the E2E suite (03 section 7.5, 04 card S6a). Chromium only (the HA companion app is Chromium-based).
//
// Every test goes through the ingress proxy (harness/ingressProxy.mjs, a stand-in for HA Core plus the Supervisor), never straight to
// the app, so the base-path, forwarded-header and websocket behaviour that only exists behind Ingress is exercised on every push:
//
//   Chromium (SwiftShader WebGL2) -> http://127.0.0.1:8123/api/hassio_ingress/<token>/   proxy
//                                 -> http://127.0.0.1:8099/                              dotnet Realm.Web.dll, Demo mode
//
// Run:  npx playwright test --config tests/e2e/playwright.config.ts            (REALM_APP_DLL = the published Realm.Web.dll)
// List: npx playwright test --config tests/e2e/playwright.config.ts --list     (needs no browser and no app)
//
// Viewport projects: each test declares the projects it runs in with a tag, and a test with none runs in `phone` (03 section 7.5):
//   test('[AC-05] title', { tag: ['@phone', '@phone-short'] }, async ({ page }) => { ... });
// A tagged test runs in exactly the projects it names (so a test that is also wanted at the default size tags `@phone` too).

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { defineConfig } from '@playwright/test';

import { APP_HOST, APP_PORT, INGRESS_PREFIX, PROXY_HOST, PROXY_PORT } from './harness/ingressProxy.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const outDir = path.join(repoRoot, 'ci-out', 'e2e');
// The app's log is redirected into it before Playwright (and its reporters) create anything.
fs.mkdirSync(outDir, { recursive: true });

// The published app (`dotnet publish`, uploaded by the dotnet job as publish-web). Relative paths are from the repository root.
const appDll = path.resolve(repoRoot, process.env.REALM_APP_DLL ?? 'publish/web/Realm.Web.dll');

const VIEWPORT_TAGS = ['phone', 'phone-short', 'unfolded', 'unfolded-tall'];
const tagged = (name: string) => new RegExp(`@${name}(?![\\w-])`);
// The title the grep sees ends with the test's tags, so "carries none of the four viewport tags" is a negative lookahead over the whole title.
const untagged = new RegExp(`^(?!.*@(?:${VIEWPORT_TAGS.join('|')})(?![\\w-]))`);

// MapLibre 6 needs WebGL2, which a GPU-less runner only has through SwiftShader.
const SWIFTSHADER_ARGS = ['--use-angle=swiftshader', '--use-gl=angle', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'];

const project = (name: string, width: number, height: number, grep: RegExp | RegExp[]) => ({
  name,
  grep,
  use: { viewport: { width, height }, deviceScaleFactor: 1 },
});

export default defineConfig({
  testDir: path.join(here, 'specs'),
  testMatch: '**/*.spec.ts',
  outputDir: path.join(outDir, 'artifacts'),
  timeout: 45_000,
  expect: { timeout: 8_000 },
  workers: 2,
  // A test that passes on retry is reported as flaky in the summary, never silently green.
  retries: 1,
  forbidOnly: !!process.env.CI,
  reporter: [
    ['list'],
    ['json', { outputFile: path.join(outDir, 'results.json') }],
    ['html', { outputFolder: path.join(outDir, 'html'), open: 'never' }],
  ],
  projects: [
    project('phone', 412, 915, [tagged('phone'), untagged]),
    project('phone-short', 412, 800, tagged('phone-short')),
    project('unfolded', 884, 916, tagged('unfolded')),
    project('unfolded-tall', 884, 1104, tagged('unfolded-tall')),
  ],
  use: {
    // The proxy URL WITH a trailing slash, so that relative URLs ('', '?demo=1', 'driving', '_framework/x.js') resolve under the ingress
    // prefix and a leading slash ('/__proxy/health') reaches the proxy's own endpoints.
    baseURL: `http://${PROXY_HOST}:${PROXY_PORT}${INGRESS_PREFIX}/`,
    locale: 'en-US',
    timezoneId: 'America/Chicago', // the AC-50 test overrides it to UTC
    colorScheme: 'dark',
    launchOptions: { args: SWIFTSHADER_ARGS },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: [
    {
      // The Demo app, started the way the container starts it (cwd = the publish folder, which is the content root). Its log is kept in the artifact.
      command: `exec dotnet "${appDll}" > "${path.join(outDir, 'app.log')}" 2>&1`,
      cwd: path.dirname(appDll),
      url: `http://${APP_HOST}:${APP_PORT}/healthz`,
      env: {
        REALM_DATA_SOURCE: 'demo',
        ASPNETCORE_HTTP_PORTS: String(APP_PORT),
        DOTNET_NOLOGO: 'true',
        DOTNET_CLI_TELEMETRY_OPTOUT: 'true',
      },
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
    },
    {
      command: `"${process.execPath}" "${path.join(here, 'harness', 'ingressProxy.mjs')}"`,
      cwd: repoRoot,
      url: `http://${PROXY_HOST}:${PROXY_PORT}/__proxy/health`,
      env: { PROXY_PORT: String(PROXY_PORT), APP_PORT: String(APP_PORT) },
      timeout: 30_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
