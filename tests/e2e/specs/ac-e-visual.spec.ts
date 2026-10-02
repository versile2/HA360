// Acceptance tests E of 01 section 11 (03 section 7.5): the visual system. S10b writes AC-42 (the computed colours of the dark palette of 01 section 7.2) and
// AC-43 (Cinzel for display text, Atkinson Hyperlegible for everything else, both self-hosted, no request to Google Fonts, under 150 KB). The axe sweep of AC-44 is
// S15's; its contrast half is the xUnit test ContrastTests ([AC-44a]).
//
// The Demo app behind the ingress proxy. A computed colour is compared as the browser writes it, `rgb(r, g, b)`, from the hex values of 01 section 7.2 typed below.
// The Driving page is prerendered, so a click that opens a popup can land before the circuit has connected and do nothing; it is repeated until the popup is there.
import type { Locator, Page } from '@playwright/test';

import { demo, expect, test } from '../fixtures.js';

// 01 section 7.2, "Dark value".
const BG = '#0B0E1F';
const SURFACE = '#141A33';
const TEXT = '#F3F0FA';
const PRIMARY = '#E8BC4E';
const ON_PRIMARY = '#1A1405';

/** The three files of 03 section 1.4, relative to the ingress prefix. */
const FONT_FILES = [
  'fonts/cinzel-latin-wght-normal.woff2',
  'fonts/atkinson-hyperlegible-latin-400-normal.woff2',
  'fonts/atkinson-hyperlegible-latin-700-normal.woff2',
] as const;

/** AC-43: "The font files are under 150 KB combined"; the font-budget guard allows 153,600 bytes, which is the same 150 KiB. */
const FONT_BUDGET_BYTES = 153_600;

/** `#0B0E1F` as the browser writes a computed colour: `rgb(11, 14, 31)`. */
function rgb(hex: string): string {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  if (match === null) throw new Error(`'${hex}' is not a #RRGGBB colour`);
  return `rgb(${match.slice(1, 4).map((part) => Number.parseInt(part, 16)).join(', ')})`;
}

/** What a CSS colour expression such as `var(--mud-palette-primary)` computes to, read through a probe element so the notation of the variable does not matter. */
async function computedColour(page: Page, expression: string): Promise<string> {
  return page.evaluate((value) => {
    const probe = document.createElement('span');
    probe.style.color = value;
    document.body.appendChild(probe);
    const colour = getComputedStyle(probe).color;
    probe.remove();
    return colour;
  }, expression);
}

/** The first family of a computed `font-family`, without quotes: `"Atkinson Hyperlegible", system-ui, ...` gives `Atkinson Hyperlegible`. */
function firstFamily(fontFamily: string): string {
  return (fontFamily.split(',')[0] ?? '').trim().replace(/^["']|["']$/g, '');
}

interface TextFace {
  tag: string;
  className: string;
  text: string;
  /** True for what 01 section 7.4 sets in the display face: H1 to H3, the wordmark and the popup titles (an H2). */
  display: boolean;
  family: string;
}

/**
 * Every visible element that carries text of its own (a non-blank text node among its children), with its computed first font family. The display ones are named by
 * `displaySelector`; the check is that those are all Cinzel and everything else is Atkinson Hyperlegible.
 */
async function textFaces(page: Page, displaySelector: string): Promise<TextFace[]> {
  return page.evaluate((display) => {
    const found: Array<{ tag: string; className: string; text: string; display: boolean; family: string }> = [];
    for (const element of document.body.querySelectorAll('*')) {
      if (['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE'].includes(element.tagName)) continue;
      const ownText = [...element.childNodes]
        .filter((node) => node.nodeType === Node.TEXT_NODE)
        .map((node) => node.textContent ?? '')
        .join('')
        .trim();
      if (ownText === '') continue;
      const style = getComputedStyle(element);
      if (style.display === 'none' || style.visibility === 'hidden' || element.getClientRects().length === 0) continue;
      found.push({
        tag: element.tagName.toLowerCase(),
        className: typeof element.className === 'string' ? element.className : '',
        text: ownText.slice(0, 40),
        display: element.matches(display),
        family: style.fontFamily,
      });
    }
    return found;
  }, displaySelector);
}

/** Splits the faces into the offenders of the rule "display text is Cinzel, all other text is Atkinson Hyperlegible", one readable line each. */
function faceOffenders(faces: readonly TextFace[]): string[] {
  const offenders: string[] = [];
  for (const face of faces) {
    const expected = face.display ? 'Cinzel' : 'Atkinson Hyperlegible';
    if (firstFamily(face.family) !== expected) {
      offenders.push(`<${face.tag} class="${face.className}"> "${face.text}" computes to ${face.family}, expected ${expected} first`);
    }
  }
  return offenders;
}

/** Opens the Speeding popup from the Driving page and returns the dialog; a tap before the circuit is up does nothing, so it is repeated until the popup is there. */
async function openSpeedingPopup(page: Page): Promise<Locator> {
  await demo(page, { path: 'driving' });
  await expect(page.getByTestId('stat-speeding'), 'the report has loaded').toBeVisible();
  const dialog = page.getByRole('dialog');
  await expect(async () => {
    if ((await dialog.count()) === 0) await page.getByTestId('stat-speeding').click({ timeout: 3_000 });
    await expect(dialog.getByTestId('popup-speeding'), 'the Speeding popup is open').toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 20_000 });
  return dialog;
}

test.describe('acceptance E: the visual system', () => {
  // The popups grow and fade in; with reduced motion they are instant, so what is measured is the final state.
  test.use({ reducedMotion: 'reduce' });

  // ---- AC-42 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-42] body is #0B0E1F, the 80 % sheet is #141A33, and the MudBlazor palette carries the same table', async ({ page }) => {
    await demo(page, { sheet: '80' });
    const sheet = page.getByTestId('sheet');
    await expect(sheet, 'the sheet starts at 80 %').toHaveAttribute('data-state', '80');

    expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor), 'body background').toBe(rgb(BG));
    await expect
      .poll(() => sheet.evaluate((element) => getComputedStyle(element).backgroundColor), { message: 'the surface of the sheet at 80 %' })
      .toBe(rgb(SURFACE));

    // RealmTheme.Create() reaches the page through MudThemeProvider: the variables MudBlazor writes carry the table of 01 section 7.2.
    const palette: ReadonlyArray<readonly [string, string]> = [
      ['--mud-palette-background', BG],
      ['--mud-palette-surface', SURFACE],
      ['--mud-palette-text-primary', TEXT],
      ['--mud-palette-primary', PRIMARY],
      ['--mud-palette-primary-text', ON_PRIMARY],
    ];
    for (const [variable, hex] of palette) {
      expect(await computedColour(page, `var(${variable})`), `${variable} computes to ${hex}`).toBe(rgb(hex));
    }
  });

  test('[AC-42] the primary button is #E8BC4E with #1A1405 text (the Got it button of a Driving popup)', async ({ page }) => {
    const dialog = await openSpeedingPopup(page);
    const gotIt = dialog.getByTestId('popup-gotit');

    await expect(gotIt, 'the primary button of the popup').toBeVisible();
    const style = await gotIt.evaluate((element) => {
      const computed = getComputedStyle(element);
      return { background: computed.backgroundColor, color: computed.color };
    });
    expect(style.background, 'primary button background').toBe(rgb(PRIMARY));
    expect(style.color, 'primary button text').toBe(rgb(ON_PRIMARY));
  });

  // ---- AC-43 ----------------------------------------------------------------------------------------------------------------------------------

  test('[AC-43] on Driving the H1 and the wordmark compute to Cinzel and every other text to Atkinson Hyperlegible', async ({ page }) => {
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('stat-speeding'), 'the report has loaded').toBeVisible();

    const faces = await textFaces(page, 'h1, h2, h3, .realm-driving__wordmark');
    const displayed = faces.filter((face) => face.display).map((face) => face.tag + (face.className === '' ? '' : `.${face.className.split(' ')[0]}`));
    expect(displayed, 'the display text on the page: the wordmark and the H1').toEqual(expect.arrayContaining(['span.realm-driving__wordmark', 'h1.realm-h1']));
    expect(faces.length, 'the sweep saw the text of the page').toBeGreaterThan(10);
    expect(faceOffenders(faces), 'text that is not set in its face').toEqual([]);
  });

  test('[AC-43] a popup title is Cinzel and the rest of the popup is Atkinson Hyperlegible', async ({ page }) => {
    const dialog = await openSpeedingPopup(page);

    const title = dialog.locator('.realm-popup__title');
    await expect(title, 'the popup title').toBeVisible();
    expect(firstFamily(await title.evaluate((element) => getComputedStyle(element).fontFamily)), 'popup title face').toBe('Cinzel');

    const faces = await textFaces(page, 'h1, h2, h3, .realm-driving__wordmark');
    expect(faces.some((face) => face.display && face.tag === 'h2'), 'the popup title (an H2) is among the display text').toBe(true);
    expect(faceOffenders(faces), 'text that is not set in its face').toEqual([]);
  });

  test('[AC-43] on Location the sheet and the navigation are set in Atkinson Hyperlegible', async ({ page }) => {
    await demo(page, { sheet: '80' });
    await expect(page.getByTestId('sheet'), 'the sheet is open at 80 %').toHaveAttribute('data-state', '80');
    await expect(page.getByTestId('sheet-list').locator('li').first(), 'the list has rows').toBeVisible();

    const faces = await textFaces(page, 'h1, h2, h3, .realm-driving__wordmark');
    expect(faces.length, 'the sweep saw the text of the page').toBeGreaterThan(5);
    expect(faceOffenders(faces), 'text that is not set in its face').toEqual([]);
  });

  test('[AC-43] both faces load from the app, nothing is requested from Google Fonts, and the font files are under 150 KB', async ({ page }) => {
    const google: string[] = [];
    page.on('request', (request) => {
      const host = new URL(request.url()).hostname;
      if (host === 'fonts.googleapis.com' || host === 'fonts.gstatic.com') google.push(request.url());
    });

    // Driving uses Cinzel (the H1) and Atkinson Hyperlegible (the rest); Location is loaded as well, because the check covers both page loads.
    await demo(page, { path: 'driving' });
    await expect(page.getByTestId('stat-speeding'), 'the report has loaded').toBeVisible();
    await demo(page);

    // The faces: each descriptor 01 section 7.4 uses matches a face of ours (the array is empty when no @font-face matches) and its file loads (the promise rejects when it
    // cannot be fetched or decoded); afterwards the faces report loaded and check() is true.
    const loaded = await page.evaluate(async () => {
      const descriptors = ['400 16px "Atkinson Hyperlegible"', '700 16px "Atkinson Hyperlegible"', '600 16px Cinzel', '700 16px Cinzel'];
      const matched: Record<string, number> = {};
      for (const descriptor of descriptors) matched[descriptor] = (await document.fonts.load(descriptor)).length;
      await document.fonts.ready;
      return {
        matched,
        checks: Object.fromEntries(descriptors.concat(['16px "Atkinson Hyperlegible"']).map((descriptor) => [descriptor, document.fonts.check(descriptor)])),
        faces: [...document.fonts].map((face) => ({ family: face.family.replace(/["']/g, ''), weight: face.weight, status: face.status })),
      };
    });
    for (const [descriptor, count] of Object.entries(loaded.matched)) expect(count, `a face matches ${descriptor}`).toBeGreaterThan(0);
    for (const [descriptor, ok] of Object.entries(loaded.checks)) expect(ok, `document.fonts.check(${descriptor})`).toBe(true);
    expect(loaded.faces.filter((face) => face.family === 'Cinzel' && face.status === 'loaded').map((face) => face.weight), 'Cinzel is loaded (variable, 600 to 700)').toEqual(['600 700']);
    expect(
      loaded.faces.filter((face) => face.family === 'Atkinson Hyperlegible' && face.status === 'loaded').map((face) => face.weight).sort(),
      'Atkinson Hyperlegible is loaded at 400 and 700',
    ).toEqual(['400', '700']);

    // The files: served by the app itself, and together under 150 KB.
    let total = 0;
    for (const file of FONT_FILES) {
      const response = await page.request.get(file);
      expect(response.ok(), `${file} is served (status ${response.status()})`).toBe(true);
      total += (await response.body()).length;
    }
    test.info().annotations.push({ type: 'info', description: `[AC-43] the three font files total ${total} bytes (budget ${FONT_BUDGET_BYTES})` });
    expect(total, 'the font files together').toBeLessThanOrEqual(FONT_BUDGET_BYTES);

    expect(google, 'requests to fonts.googleapis.com or fonts.gstatic.com').toEqual([]);
  });
});
