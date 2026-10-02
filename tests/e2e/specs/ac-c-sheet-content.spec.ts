// The sheet's lists: AC-25 to AC-27 (Drivers), the list half of AC-28 (Vehicles), AC-29 (Places) and AC-50 (a UTC browser changes no string) of 01 section 11
// C, on the Demo app behind the ingress proxy. This is S7b's file; the sheet itself (handle, tabs, heights, layout) is S7a's `ac-a-sheet.spec.ts`. S8c appends the other half: the
// selection half of AC-28 (tap the pickup, the Peek header, the pin centred, the handle opens the detail), AC-30 (the member detail) and AC-31 (a place: the header, the detail, the
// Here-now row, the tab switch), in the block at the end of the file ("S8c: selecting from the lists").
//
// What the rows are made of is the markup of `MemberRow`, `VehicleRow` and `PlaceRow`: one `button[data-testid="row-<kind>-<id>"]` per entity, whose parts are found by
// class (`.realm-row-name`, `.realm-row-lore`, `.realm-row-status`, `.realm-row-detail-text`, `.realm-battery-pill`, ...). The strings are the ones of 01 section 8; every
// name, place, street and label is read from the Demo cast (`loadDemoCast()`), and only the times, the percentages and the spec's own sentences are written here
// (03 section 8.1 rule 2). The colours are never typed: `tokenColor()` asks the page what a `--realm-*` token resolves to.
//
// The list is only visible with the sheet at 80 % (at Peek the CSS hides it, AC-05), so on a Compact viewport every test opens with `?sheet=80`; in the unfolded
// projects the panel shows it at once, and the same assertions hold there ("rows are identical in the bottom sheet and the Expanded panel"). `demo()` waits for the
// map, the rows are then waited for by their own test ids and nothing sleeps.

import type { Locator, Page } from '@playwright/test';

import {
  PEEK_CENTRE_TOLERANCE_PX,
  castMember,
  castPlace,
  demo,
  expect,
  loadDemoCast,
  mapReady,
  pinDistanceFromPeekCentre,
  readHook,
  settled,
  test,
  type CastMember,
  type CastPlace,
  type CastVehicle,
  type DemoCastFile,
  type DemoOptions,
} from '../fixtures.js';

/** Below this width the layout is Compact (a bottom sheet); from it, with room in height, the Expanded panel (01 section 3.1). */
const EXPANDED_FROM = 840;
/** Row minimums of 01 sections 5.1 to 5.3, and the smallest touch target of 10.1. */
const MEMBER_ROW_MIN = 84;
const VEHICLE_ROW_MIN = 96;
const PLACE_ROW_MIN = 72;
const TARGET_MIN = 48;
/** Half a pixel of rounding slack on a height read from a layout box. */
const SLACK = 0.5;

/** 01 section 8.5 and 5.2: what the placeholder's info button explains. It is the spec's sentence, not a Demo value. */
const PLACEHOLDER_EXPLANATION = "This vehicle's maker has no official Home Assistant integration yet. When one exists, the Chariot will appear on the map.";

// ---- small helpers -------------------------------------------------------------------------------------------------------------------------

type Section = 'drivers' | 'vehicles' | 'places';

function viewportOf(page: Page): { width: number; height: number } {
  const size = page.viewportSize();
  if (size === null) throw new Error('the page has no viewport size');
  return size;
}

/** The Demo vehicle with this role id (the fixtures file has helpers for members and places only). */
function castVehicle(cast: DemoCastFile, id: string): CastVehicle {
  const found = cast.vehicles.find((vehicle) => vehicle.id === id);
  if (!found) throw new Error(`the demo cast has no vehicle '${id}' (it has ${cast.vehicles.map((vehicle) => vehicle.id).join(', ')})`);
  return found;
}

const row = (page: Page, kind: 'member' | 'vehicle' | 'place', id: string): Locator => page.getByTestId(`row-${kind}-${id}`);
const rowsOf = (page: Page, kind: 'member' | 'vehicle' | 'place'): Locator => page.locator(`[data-testid^="row-${kind}-"]`);

/** Compact when the window is under 840 wide, or when the layout is forced to the bottom sheet; the sheet then has to be raised to 80 % to show its list. */
function isCompact(page: Page, layout: DemoOptions['layout']): boolean {
  if (layout === 'sheet') return true;
  if (layout === 'panel') return false;
  return viewportOf(page).width < EXPANDED_FROM;
}

/** Opens Location with the Drivers list showing: `?sheet=80` for a bottom sheet, the panel as it is (it has no sizes). Waits for the first row, not for a delay. */
async function openList(page: Page, opts: Pick<DemoOptions, 'layout' | 'now'> = {}): Promise<void> {
  await demo(page, { ...opts, ...(isCompact(page, opts.layout) ? { sheet: '80' as const } : {}) });
  await expect(page.getByTestId('row-member-king'), 'the first Drivers row shows (the sheet at 80 %, or the panel)').toBeVisible();
}

/** Taps a section tab and waits for the tab to say it is selected. */
async function showSection(page: Page, section: Section): Promise<void> {
  const tab = page.getByTestId(`tab-${section}`);
  await tab.click();
  await expect(tab, `tab-${section} is selected`).toHaveAttribute('aria-selected', 'true');
}

/** A row's box against its minimum height and the 48 px target (01 section 10.1). */
async function expectTarget(target: Locator, minHeight: number, what: string): Promise<void> {
  const box = await target.boundingBox();
  expect(box, `${what} has no bounding box (not rendered, or display: none)`).not.toBeNull();
  if (box === null) return;
  expect(box.height, `${what} is at least ${minHeight} px high (got ${box.height.toFixed(2)})`).toBeGreaterThanOrEqual(minHeight - SLACK);
  expect(box.width, `${what} is at least ${TARGET_MIN} px wide (got ${box.width.toFixed(2)})`).toBeGreaterThanOrEqual(TARGET_MIN);
}

/** What a `--realm-*` token resolves to as a computed colour, so a test compares colours without typing one. */
async function tokenColor(page: Page, token: string): Promise<string> {
  return page.evaluate((name) => {
    const probe = document.createElement('span');
    probe.style.color = `var(${name})`;
    document.body.append(probe);
    const resolved = getComputedStyle(probe).color;
    probe.remove();
    return resolved;
  }, token);
}

/** The cast's people in list order (`SortOrder`): the king, queen, jester, cryptid, then the static prince. */
function membersInOrder(cast: DemoCastFile): CastMember[] {
  return [...cast.members].sort((a, b) => a.sortOrder - b.sortOrder);
}

/** The fixture stores a far-away address as "street, City, ST"; the row reads "street · City, ST" (01 section 5.1). */
function farAwayLine(member: CastMember): string {
  const [street, city, region] = (member.address ?? '').split(', ');
  return `${street} · ${city}, ${region}`;
}

/** Ordinal, case-insensitive: how the list orders the places that share a count (01 section 5.3). */
const byName = (a: CastPlace, b: CastPlace): number => {
  const left = a.name.toLowerCase();
  const right = b.name.toLowerCase();
  return left < right ? -1 : left > right ? 1 : 0;
};

// ---- AC-25 to AC-27: Drivers ---------------------------------------------------------------------------------------------------------------

test.describe('Drivers list', () => {
  test('[AC-25] Drivers lists five rows in order, each at least 84 px high and a 48 px target; Alden reads At Hearth Haven, Since 5:52 pm, 19% charging, with no distance', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);

    const ordered = membersInOrder(cast);
    expect(ordered, 'the Demo cast has five people (a check of the fixture, not of the app)').toHaveLength(5);

    // Five rows, in the cast order, each a button.
    const ids = await rowsOf(page, 'member').evaluateAll((rows) => rows.map((element) => element.getAttribute('data-testid')));
    expect(ids, 'the test ids of the Drivers rows, in list order').toEqual(ordered.map((member) => `row-member-${member.id}`));
    await expect(rowsOf(page, 'member').locator('.realm-row-name'), 'the names, in list order').toHaveText(ordered.map((member) => member.name));
    for (const member of ordered) {
      const target = row(page, 'member', member.id);
      expect(await target.evaluate((element) => element.tagName), `row-member-${member.id} is one button`).toBe('BUTTON');
      await expectTarget(target, MEMBER_ROW_MIN, `row-member-${member.id}`);
    }

    // Alden: the king, at home since 17:52, charging at 19 %. He is me, so no distance.
    const king = castMember(cast, 'king');
    const home = castPlace(cast, 'home');
    const alden = row(page, 'member', 'king');
    await expect(alden.locator('.realm-row-name'), 'L1 name').toHaveText(king.name);
    await expect(alden.locator('.realm-row-lore'), 'L1 lore title').toHaveText(king.lore);
    await expect(alden.locator('.realm-row-status'), 'L2').toHaveText(`At ${home.name}`);
    await expect(alden.locator('.realm-row-detail-text'), 'L3 (no "away": it is my own row)').toHaveText('Since 5:52 pm');
    const pill = alden.locator('.realm-battery-pill');
    await expect(pill.locator('.realm-battery-text'), 'the battery pill text').toHaveText('19%');
    await expect(pill, 'the pill says it is charging (the bolt)').toHaveAttribute('data-charging', 'true');
    await expect(pill, 'the pill is not in the low style').not.toHaveAttribute('data-low', 'true');
    await expect(pill, "the pill's accessible name says charging").toHaveAttribute('aria-label', /charging/);
    await expect(alden, 'a fresh row is drawn at full opacity').toHaveCSS('opacity', '1');
  });

  test('[AC-26] Briar reads Driving · 54 mph on her street with Since 9:12 pm and 62%; Cass reads her hall with Since 9:06 pm · 1.0 mi away and a low 12% pill', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);

    const queen = castMember(cast, 'queen');
    const briar = row(page, 'member', 'queen');
    await expect(briar.locator('.realm-row-name'), 'Briar L1 name').toHaveText(queen.name);
    await expect(briar.locator('.realm-row-status'), 'Briar L2').toHaveText(`Driving · 54 mph on ${queen.address ?? ''}`);
    await expect(briar.locator('.realm-row-detail-text'), 'Briar L3 (a driving row has no distance)').toHaveText('Since 9:12 pm');
    await expect(briar.locator('.realm-battery-text'), 'Briar pill').toHaveText('62%');
    await expect(briar.locator('.realm-battery-pill'), 'Briar is not low').not.toHaveAttribute('data-low', 'true');

    const jester = castMember(cast, 'jester');
    const hall = castPlace(cast, 'jester_hall');
    const cass = row(page, 'member', 'jester');
    await expect(cass.locator('.realm-row-name'), 'Cass L1 name').toHaveText(jester.name);
    await expect(cass.locator('.realm-row-status'), 'Cass L2').toHaveText(`At ${hall.name}`);
    await expect(cass.locator('.realm-row-detail-text'), 'Cass L3').toHaveText('Since 9:06 pm · 1.0 mi away');

    // The low pill: its text, its flag, its accessible name, and the red tint (the border is the error token, the text stays readable).
    const pill = cass.locator('.realm-battery-pill');
    await expect(pill.locator('.realm-battery-text'), 'Cass pill text').toHaveText('12%');
    await expect(pill, 'Cass pill is flagged low').toHaveAttribute('data-low', 'true');
    await expect(pill, 'Cass pill is not charging').not.toHaveAttribute('data-charging', 'true');
    await expect(pill, "Cass pill's accessible name contains low").toHaveAttribute('aria-label', /low/);
    await expect(pill, 'Cass pill is red-tinted (its border is --realm-error)').toHaveCSS('border-top-color', await tokenColor(page, '--realm-error'));
  });

  test('[AC-46a] a Drivers row is named as the map pin is, then Double tap to show on map', { tag: ['@phone'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);

    const jester = castMember(cast, 'jester');
    const hall = castPlace(cast, 'jester_hall');
    await expect(row(page, 'member', 'jester'), "Cass's row has the pin's accessible name of 01 section 10.3").toHaveAttribute(
      'aria-label',
      `${jester.name}, ${jester.lore}. At ${hall.name} since 9:06 pm. Battery 12 percent, low. 1.0 mile away. Double tap to show on map.`,
    );
    // What is inside the button is not read a second time: the avatar initial is hidden from assistive technology.
    await expect(row(page, 'member', 'jester').locator('.realm-row-initial'), 'the initial is hidden from assistive technology').toHaveAttribute('aria-hidden', 'true');
  });

  test('[AC-27] Dara is the stale far-away row (72 percent opacity, a warning line with a clock, a low 10% pill); Elio is the static prince with no pill; the summary counts four', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);

    // Dara: last seen 42 minutes ago, a long way off.
    const cryptid = castMember(cast, 'cryptid');
    const dara = row(page, 'member', 'cryptid');
    await expect(dara.locator('.realm-row-name'), 'Dara L1 name').toHaveText(cryptid.name);
    await expect(dara.locator('.realm-row-status'), 'Dara L2 (street, then city and state)').toHaveText(farAwayLine(cryptid));
    const warning = dara.locator('.realm-row-detail');
    await expect(warning.locator('.realm-row-detail-text'), 'Dara L3').toHaveText("The raven's late — last seen 42 min ago");
    await expect(warning, 'Dara L3 is in the warning colour (--realm-warning)').toHaveCSS('color', await tokenColor(page, '--realm-warning'));
    await expect(warning.locator('.realm-row-clock'), 'Dara L3 has the clock icon').toBeVisible();
    await expect(dara, 'a stale row is drawn at 0.72 opacity').toHaveCSS('opacity', '0.72');
    await expect(dara.locator('.realm-battery-text'), 'Dara pill').toHaveText('10%');
    await expect(dara.locator('.realm-battery-pill'), 'Dara pill is low').toHaveAttribute('data-low', 'true');
    await expect(dara.locator('.realm-battery-pill'), 'the stale warning is not in the pill').not.toHaveAttribute('data-charging', 'true');

    // Elio: the static pin. Nothing is shared, there is no battery, and the row is not dimmed.
    const prince = castMember(cast, 'prince');
    const elio = row(page, 'member', 'prince');
    await expect(elio.locator('.realm-row-name'), 'Elio L1 name').toHaveText(prince.name);
    await expect(elio.locator('.realm-row-lore'), 'Elio lore title').toHaveText(prince.lore);
    await expect(elio.locator('.realm-row-status'), 'Elio L2').toHaveText(prince.staticLabel ?? '');
    await expect(elio.locator('.realm-row-detail-text'), 'Elio L3').toHaveText("Location isn't shared");
    await expect(elio.locator('.realm-battery-pill'), 'Elio has no battery pill').toHaveCount(0);
    await expect(elio.locator('.realm-row-clock'), 'Elio has no warning clock').toHaveCount(0);
    await expect(elio, 'the static row is drawn at full opacity').toHaveCSS('opacity', '1');

    // The handle's (or the panel's) summary counts the four people who are live, not Elio.
    await expect(page.getByTestId('sheet-summary'), 'the summary counts four people, not five').toHaveText('4 in the Realm · 1 driving');
  });

  test('[AC-25] the rows are the same markup in the bottom sheet and in the Expanded panel, for all three lists', { tag: ['@unfolded'] }, async ({ page }) => {
    // The same strings, the same classes, the same icons: nothing in a row knows which of the two hosts draws it (01 section 3.4, 03 section 3.4).
    const listMarkup = async (): Promise<string[]> =>
      page.locator('.realm-sheet-list > li').evaluateAll((items) => items.map((item) => item.outerHTML.replace(/<!--[\s\S]*?-->/g, '')));

    const read = async (layout: 'sheet' | 'panel'): Promise<Record<Section, string[]>> => {
      await openList(page, { layout });
      const found: Record<Section, string[]> = { drivers: await listMarkup(), vehicles: [], places: [] };
      await showSection(page, 'vehicles');
      await expect(rowsOf(page, 'vehicle'), `the Vehicles list in the ${layout} layout`).toHaveCount(2);
      found.vehicles = await listMarkup();
      await showSection(page, 'places');
      await expect(rowsOf(page, 'place'), `the Places list in the ${layout} layout`).toHaveCount(14);
      found.places = await listMarkup();
      return found;
    };

    const inSheet = await read('sheet');
    const inPanel = await read('panel');
    for (const section of ['drivers', 'vehicles', 'places'] as const) {
      expect(inSheet[section].length, `the ${section} list has rows`).toBeGreaterThan(0);
      expect(inPanel[section], `the ${section} rows in the panel are the rows of the bottom sheet`).toEqual(inSheet[section]);
    }
  });
});

// ---- AC-28 (the list half): Vehicles -------------------------------------------------------------------------------------------------------

test.describe('Vehicles list', () => {
  test('[AC-28a] Vehicles lists the pickup on four lines and the hatchback placeholder with its note, no chevron and aria-disabled; the summary reads 2 vehicles · all parked', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);
    await showSection(page, 'vehicles');

    await expect(rowsOf(page, 'vehicle'), 'two vehicles are listed').toHaveCount(2);
    const ids = await rowsOf(page, 'vehicle').evaluateAll((rows) => rows.map((element) => element.getAttribute('data-testid')));
    expect(ids, 'the vehicles, in list order').toEqual(['row-vehicle-wagon', 'row-vehicle-chariot']);
    await expect(page.getByTestId('sheet-summary'), 'the Vehicles summary').toHaveText('2 vehicles · all parked');

    // The pickup (live): L1 name and lore, L2 where it is, L3 the engine and the fuel, L4 how old that is.
    const wagon = castVehicle(cast, 'wagon');
    const home = castPlace(cast, 'home');
    const pickup = row(page, 'vehicle', 'wagon');
    await expectTarget(pickup, VEHICLE_ROW_MIN, 'row-vehicle-wagon');
    await expect(pickup.locator('.realm-row-name'), 'pickup L1 name').toHaveText(wagon.name);
    await expect(pickup.locator('.realm-row-lore'), 'pickup L1 lore title').toHaveText(wagon.lore);
    const separator = await pickup.locator('.realm-row-lore').evaluate((element) => getComputedStyle(element, '::before').content);
    expect(separator, 'the lore follows the name after a middle dot ("Ford Pickup · The King\'s Wagon")').toContain('·');
    await expect(pickup.locator('.realm-row-status'), 'pickup L2').toHaveText(`At ${home.name}`);
    await expect(pickup.locator('.realm-row-part'), 'pickup L3: the engine and the fuel').toHaveText(['Engine off', 'Fuel 71%']);
    await expect(pickup.locator('.realm-row-detail-text'), 'pickup L4').toHaveText('Updated 20 min ago');
    await expect(pickup.locator('.realm-row-chevron'), 'a live row has its chevron').toHaveCount(1);
    await expect(pickup, 'the pickup is not disabled').not.toHaveAttribute('aria-disabled', 'true');
    await expect(pickup, 'the pickup is drawn at full opacity').toHaveCSS('opacity', '1');
    await expect(pickup.locator('.realm-row-glyph'), 'the pickup glyph is an svg').toHaveJSProperty('tagName', 'svg');

    // The hatchback (placeholder): its note is the Demo's, word for word, and the row is a disabled button with no chevron.
    const chariot = castVehicle(cast, 'chariot');
    const hatchback = row(page, 'vehicle', 'chariot');
    await expectTarget(hatchback, VEHICLE_ROW_MIN, 'row-vehicle-chariot');
    await expect(hatchback.locator('.realm-row-name'), 'placeholder L1 name').toHaveText(chariot.name);
    await expect(hatchback.locator('.realm-row-lore'), 'placeholder L1 lore title').toHaveText(chariot.lore);
    await expect(hatchback.locator('.realm-row-note'), 'placeholder L2 is exactly the chariot note of the Demo cast').toHaveText(cast.chariotNote);
    await expect(hatchback, 'the placeholder is aria-disabled').toHaveAttribute('aria-disabled', 'true');
    await expect(hatchback.locator('.realm-row-chevron'), 'the placeholder has no chevron').toHaveCount(0);
    await expect(hatchback.locator('.realm-row-part'), 'the placeholder has no engine or fuel line').toHaveCount(0);
    await expect(hatchback, 'the placeholder is drawn at 0.7 opacity').toHaveCSS('opacity', '0.7');
  });

  test('[AC-28a] tapping the placeholder does nothing; its info button, a 48 px sibling of the row, opens the explanation and Esc closes it', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    await openList(page);
    await showSection(page, 'vehicles');

    const hatchback = row(page, 'vehicle', 'chariot');
    const info = page.locator('.realm-row-info');
    const popover = page.locator('.realm-row-popover');
    await expect(info, 'one info button, for the one placeholder').toHaveCount(1);
    await expect(info, 'the info button is named for what it opens').toHaveAttribute('aria-label', 'About this vehicle');
    await expect(info, 'the popover is closed to begin with').toHaveAttribute('aria-expanded', 'false');
    await expect(popover, 'no popover yet').toHaveCount(0);
    expect(await info.evaluate((button) => button.parentElement?.closest('button') ?? null), 'the info button has no button around it (a button cannot hold one)').toBeNull();
    await expect(hatchback.locator('.realm-row-info'), 'the info button is a sibling of the row, not a child').toHaveCount(0);
    const target = await info.boundingBox();
    expect(target, 'the info button has a box').not.toBeNull();
    expect(Math.min(target?.width ?? 0, target?.height ?? 0), 'the info button is at least a 48 px target').toBeGreaterThanOrEqual(TARGET_MIN - SLACK);

    // A tap on the row is a no-op. The row is aria-disabled, which Playwright treats as not enabled, so the tap is forced. Nothing can be waited for after a
    // no-op, so the next step is the proof: events reach the circuit in order, and had the row tap opened the popover, the info tap below would close it again.
    await hatchback.click({ force: true, position: { x: 40, y: 40 } });

    // The info button opens the explanation, in the sheet and not in a layer of its own, and Esc on it closes it again.
    await info.click();
    await expect(info, 'aria-expanded after the tap on the info button (and after nothing from the row tap)').toHaveAttribute('aria-expanded', 'true');
    await expect(popover, 'the explanation is shown').toHaveText(PLACEHOLDER_EXPLANATION);
    await expect(popover, 'it is a note').toHaveAttribute('role', 'note');
    await expect(popover, 'it is the element the info button controls').toHaveAttribute('id', (await info.getAttribute('aria-controls')) ?? 'missing');
    await expect(hatchback, 'the placeholder is never the current row').not.toHaveAttribute('aria-current', 'true');
    await expect(page.getByTestId('sheet-selection-header'), 'a tap on the placeholder selects nothing').toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(popover, 'Esc closes the explanation').toHaveCount(0);
    await expect(info, 'aria-expanded after Esc').toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByTestId('sheet'), 'Esc never closes the sheet (01 section 3.4)').toBeVisible();
  });
});

// ---- AC-29: Places -------------------------------------------------------------------------------------------------------------------------

test.describe('Places list', () => {
  test('[AC-29] Places lists 14 rows: Hearth Haven then The Jester Hall with 1 here, then the empty places A to Z; the duplicates carry (2) and the arrival zone is absent', { tag: ['@phone', '@unfolded'] }, async ({ page }) => {
    const cast = loadDemoCast();
    await openList(page);
    await showSection(page, 'places');

    const drawn = cast.places.filter((place) => place.drawn);
    expect(drawn, 'the Demo cast draws and lists 14 of its 15 zones').toHaveLength(14);
    const occupied = ['home', 'jester_hall'].map((id) => castPlace(cast, id));
    const empty = drawn.filter((place) => !occupied.includes(place)).sort(byName);
    const expected = [...occupied, ...empty];

    // Fourteen rows, in order: the two occupied places (one person each, so by name), then the rest A to Z.
    await expect(rowsOf(page, 'place'), 'fourteen places are listed').toHaveCount(14);
    const ids = await rowsOf(page, 'place').evaluateAll((rows) => rows.map((element) => element.getAttribute('data-testid')));
    expect(ids, 'the test ids of the Places rows, in list order').toEqual(expected.map((place) => `row-place-${place.id}`));
    await expect(rowsOf(page, 'place').locator('.realm-row-name'), 'the display names, in list order').toHaveText(expected.map((place) => place.name));
    await expect(rowsOf(page, 'place').locator('.realm-row-subtitle'), 'the subtitles of 01 section 8.5, in list order').toHaveText(expected.map((place) => place.subtitle));

    // The duplicates, and the zone that is never listed.
    await expect(row(page, 'place', 'work_2').locator('.realm-row-name'), 'the second Work carries (2)').toHaveText('Work (2)');
    await expect(row(page, 'place', 'skate_two').locator('.realm-row-name'), 'the second Rollerdome carries (2)').toHaveText('Rollerdome (2)');
    await expect(row(page, 'place', 'approach'), 'the arrival zone (radius over 5 km) is not listed').toHaveCount(0);

    // Counts are people only (R2-009): Hearth Haven holds Alden and the parked pickup and still reads 1 here. The empty ones read Empty, dimmed.
    const first = row(page, 'place', occupied[0].id);
    const second = row(page, 'place', occupied[1].id);
    await expect(first.locator('.realm-row-count'), 'Hearth Haven count (the pickup adds nothing)').toHaveText('1 here');
    await expect(second.locator('.realm-row-count'), 'The Jester Hall count').toHaveText('1 here');
    await expect(first.locator('.realm-mini'), 'one mini avatar for the one person at Hearth Haven').toHaveCount(1);
    await expect(first.locator('.realm-mini-initial'), 'the mini avatar is the king initial').toHaveText(castMember(cast, 'king').name.charAt(0));
    await expect(second.locator('.realm-mini-initial'), 'the mini avatar is the jester initial').toHaveText(castMember(cast, 'jester').name.charAt(0));
    for (const place of empty) {
      const target = row(page, 'place', place.id);
      await expect(target.locator('.realm-row-count'), `${place.name} count`).toHaveText('Empty');
      await expect(target.locator('.realm-row-count'), `${place.name} count is the dimmed one`).toHaveClass(/realm-row-count--empty/);
      await expect(target.locator('.realm-mini'), `${place.name} has no mini avatars`).toHaveCount(0);
    }

    // Every row is a 72 px (or taller) button with no chevron, and the summary agrees with the list.
    for (const place of expected) {
      await expectTarget(row(page, 'place', place.id), PLACE_ROW_MIN, `row-place-${place.id}`);
    }
    await expect(page.getByTestId('sheet-summary'), 'the Places summary').toHaveText('14 places · 2 occupied');
  });
});

// ---- AC-50: the browser's zone changes nothing -----------------------------------------------------------------------------------------------

test.describe('a browser in UTC', () => {
  test.use({ timezoneId: 'UTC' });

  test('[AC-50a] with the browser time zone forced to UTC the rows read the same: Alden Since 5:52 pm and every other Since and relative string unchanged', { tag: ['@phone'] }, async ({ page }) => {
    // The Demo clock is 21:25 CDT, which is 02:25 the next day in UTC: a row that took the browser's zone would read yesterday and 10:52 pm.
    expect(await page.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone), 'the browser really is in UTC').toBe('UTC');
    await openList(page);

    await expect(row(page, 'member', 'king').locator('.realm-row-detail-text'), 'Alden L3 stays in the HA zone').toHaveText('Since 5:52 pm');
    await expect(row(page, 'member', 'queen').locator('.realm-row-detail-text'), 'Briar L3').toHaveText('Since 9:12 pm');
    await expect(row(page, 'member', 'jester').locator('.realm-row-detail-text'), 'Cass L3').toHaveText('Since 9:06 pm · 1.0 mi away');
    await expect(row(page, 'member', 'cryptid').locator('.realm-row-detail-text'), 'Dara L3 (a relative time, unchanged)').toHaveText("The raven's late — last seen 42 min ago");

    await showSection(page, 'vehicles');
    await expect(row(page, 'vehicle', 'wagon').locator('.realm-row-detail-text'), 'the pickup update age is relative, unchanged').toHaveText('Updated 20 min ago');
  });
});

// ==== S8c: selecting from the lists (01 section 11 AC-28 (D45 half), AC-30, AC-31; D45, D46) ===========================================================================================
// A row tap at 80 % collapses the sheet to Peek with the selection header (D45); the handle then opens the detail. These tests run at 412 x 915 (the phone project): the Peek numbers
// are its own. Every name, place and lore title comes from the cast; the times and the counts are the spec's. Nothing sleeps: after the tap the test waits for the header, then for the
// pin to arrive in the Peek rectangle (the flight starts after the server round trip), then for `settled()`.

const handle = (page: Page): Locator => page.getByTestId('sheet-handle');
const header = (page: Page): Locator => page.getByTestId('sheet-selection-header');

/** Opens the list at 80 % and waits until the map holds its pins, so that a row tap has something to fly to. */
async function openListWithMap(page: Page, section: Section): Promise<void> {
  await openList(page);
  await mapReady(page);
  if (section !== 'drivers') await showSection(page, section);
}

/** After a row tap: Peek with the header (no detail, no tabs) and, when a pin is named, the pin's true point within 24 px of the middle of the Peek rectangle (AC-22). */
async function expectSelectedAtPeek(page: Page, label: string, pin?: { kind: 'member' | 'vehicle'; id: string }): Promise<void> {
  await expect(header(page), `${label}: the selection header shows`).toBeVisible();
  await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: `${label}: sheet().state` }).toBe('peek');
  await expect(page.getByTestId('sheet-segments'), `${label}: the tab control is hidden`).toBeHidden();
  await expect(page.getByTestId('detail-back'), `${label}: no detail at Peek`).toHaveCount(0);
  if (pin === undefined) return;
  await expect
    .poll(() => pinDistanceFromPeekCentre(page, pin.kind, pin.id), { message: `${label}: distance of pin-${pin.kind}-${pin.id} from the centre of the Peek rectangle`, timeout: 10_000 })
    .toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
  await settled(page);
  expect(await pinDistanceFromPeekCentre(page, pin.kind, pin.id), `${label}: the pin's distance from the Peek centre after the flight`).toBeLessThanOrEqual(PEEK_CENTRE_TOLERANCE_PX);
}

/** The handle's tap: the detail of the selection at 80 %. */
async function openDetail(page: Page, label: string): Promise<void> {
  await handle(page).click();
  await expect(page.getByTestId('detail-back'), `${label}: the detail's back arrow shows`).toBeVisible();
  await expect.poll(async () => (await readHook(page, 'sheet')).state, { message: `${label}: sheet().state` }).toBe('80');
}

test.describe('S8c: selecting from the lists', () => {
  // [AC-28] The D45 half: the pickup row (sheet at 80 %) selects it. Peek, the header with no battery badge, the pin `pin-vehicle-wagon` centred as in AC-22 (the wagon is drawn beside the
  // King's pin, the true point is what is centred), then the handle opens the vehicle detail (01 section 5.5: Location, Engine, Fuel, Odometer, Last update; no Speed while it is parked).
  test('[AC-28] tapping the pickup row selects it at Peek with its header and no battery badge, the pin centred as in AC-22; the handle opens the vehicle detail', { tag: ['@phone'] }, async ({ page }) => {
    const cast = loadDemoCast();
    const wagon = castVehicle(cast, 'wagon');
    const home = castPlace(cast, 'home');
    await openListWithMap(page, 'vehicles');

    await row(page, 'vehicle', 'wagon').click();
    await expectSelectedAtPeek(page, 'the pickup selected from its row', { kind: 'vehicle', id: 'wagon' });
    await expect(header(page).locator('.realm-row-name'), 'the header names the pickup').toHaveText(wagon.name);
    await expect(header(page).locator('.realm-row-lore'), 'the header gives its lore title').toHaveText(wagon.lore);
    await expect(header(page).locator('.realm-sel-line'), 'the second line: where it is, the engine, how old that is').toHaveText(`At ${home.name} · Engine off · Updated 20 min ago`);
    await expect(page.getByTestId('sheet-selection-battery'), 'a vehicle has no battery badge').toHaveCount(0);

    await openDetail(page, 'after the handle tap');
    await expect(page.locator('.realm-detail-name'), 'the vehicle detail is the pickup\'s').toHaveText(wagon.name);
    await expect(page.locator('.realm-detail-heading .realm-detail-eyebrow'), 'its lore title').toHaveText(wagon.lore);
    await expect(page.locator('.realm-detail-rows dt'), 'the rows of a parked vehicle (no Speed)').toHaveText(['Location', 'Engine', 'Fuel', 'Odometer', 'Last update']);
    await expect(page.locator('.realm-detail-rows .realm-detail-row').nth(0).locator('dd'), 'Location').toHaveText(`At ${home.name}`);
    await expect(page.locator('.realm-detail-rows .realm-detail-row').nth(1).locator('dd'), 'Engine').toHaveText('Off');
    await expect(page.locator('.realm-detail-fuel-text'), 'Fuel').toHaveText('71%');
  });

  // [AC-30] Cass's detail at v1 scope, reached by selecting at Peek and tapping the handle: back, the 64 px avatar, the name, the eyebrow, "Updated 3 min ago", the status, the since line with
  // the distance, the battery chip, the address, the week's three tiles and the Full report link. No timeline and no trail button. The week loads from the session after the detail opens, so
  // the tiles are asserted with the retrying matchers.
  test('[AC-30] the member detail: back, a 64 px avatar, name, eyebrow, Updated 3 min ago, status, since and distance, the 12% Low battery chip, the address, the week tiles and Full report; no timeline and no Show trail', { tag: ['@phone'] }, async ({ page }) => {
    const cast = loadDemoCast();
    const jester = castMember(cast, 'jester');
    const hall = castPlace(cast, 'jester_hall');
    await openListWithMap(page, 'drivers');

    await row(page, 'member', 'jester').click();
    await expectSelectedAtPeek(page, 'Cass selected from his row', { kind: 'member', id: 'jester' });
    await openDetail(page, 'after the handle tap');

    const back = await page.getByTestId('detail-back').boundingBox();
    expect(back, 'the back arrow has a box').not.toBeNull();
    expect(Math.min(back?.width ?? 0, back?.height ?? 0), 'the back arrow is at least a 48 px target').toBeGreaterThanOrEqual(TARGET_MIN - SLACK);
    const avatar = await page.locator('.realm-detail-avatar').boundingBox();
    expect(avatar, 'the avatar has a box').not.toBeNull();
    expect(avatar?.width ?? 0, 'the avatar is 64 px wide').toBeCloseTo(64, 0);
    expect(avatar?.height ?? 0, 'the avatar is 64 px high').toBeCloseTo(64, 0);

    await expect(page.locator('.realm-detail-name'), 'the name').toHaveText(jester.name);
    await expect(page.locator('.realm-detail-heading .realm-detail-eyebrow'), 'the eyebrow is the lore title (drawn in capitals by the style)').toHaveText(jester.lore);
    await expect(page.locator('.realm-detail-updated'), 'the freshness line').toHaveText('Updated 3 min ago');
    await expect(page.locator('.realm-detail-status-line'), 'the status').toHaveText(`At ${hall.name}`);
    await expect(page.locator('.realm-detail-line'), 'since, and how far away').toHaveText('Since 9:06 pm · 1.0 mi away');
    await expect(page.locator('.realm-detail-chips .realm-detail-chip').first(), 'the battery chip').toHaveText('12% · Low battery');
    await expect(page.locator('.realm-detail-address'), 'the address of the cast').toHaveText(jester.address ?? '');

    await expect(page.locator('.realm-detail-week-title'), 'the week block is titled').toHaveText('This week');
    await expect(page.locator('.realm-detail-tile-value'), 'the three tiles: drives, miles, top speed').toHaveText(['18', '202.6', '88 mph']);
    await expect(page.locator('.realm-detail-tile-label'), 'their labels').toHaveText(['drives', 'mi', 'top']);
    await expect(page.locator('.realm-detail-link'), 'the link to the full report').toHaveText('Full report ›');
    await expect(page.locator('.realm-detail-link'), 'it goes to his week').toHaveAttribute('href', 'driving/jester?week=0');

    // v1 scope: the Today timeline and the trail button are v1.1 (01 section 13).
    await expect(page.getByTestId('btn-show-trail'), 'no Show trail button').toHaveCount(0);
    await expect(page.getByRole('button', { name: /trail/i }), 'nothing in the sheet is a trail control').toHaveCount(0);
    await expect(page.locator('.realm-detail').getByText(/timeline|today/i), 'no Today timeline').toHaveCount(0);
  });

  // [AC-30] The last sentence: Elio's detail shows no street address (`static_show_address` is off by default), and no week (he shares nothing); he is a static member.
  test('[AC-30] the static prince\'s detail shows no street address and no week', { tag: ['@phone'] }, async ({ page }) => {
    const prince = castMember(loadDemoCast(), 'prince');
    await openListWithMap(page, 'drivers');

    await row(page, 'member', 'prince').click();
    await expectSelectedAtPeek(page, 'Elio selected from his row');
    await openDetail(page, 'after the handle tap');

    await expect(page.locator('.realm-detail-name'), 'the name').toHaveText(prince.name);
    await expect(page.locator('.realm-detail-address'), 'no street address (static_show_address is off)').toHaveCount(0);
    await expect(page.locator('.realm-detail-week'), 'no week block for a static member').toHaveCount(0);
    await expect(page.locator('.realm-detail-sentence'), 'his own counsel, built from the lore title').toHaveText(`The ${prince.lore} keeps his own counsel.`);
  });

  // [AC-31] A place: its row selects it at Peek with the header "Hearth Haven" over "Here now (2)" (the person and the parked pickup); the handle opens its detail, which lists Alden and then the
  // pickup and has no comings and goings; a tap on Alden's row there selects him at Peek with his header; and a tab switch at 80 % keeps the size, clears the selection and shows the new section
  // with its summary. (The zone on the map is the other way in; it is a tap on a 14 px circle under the pins and is left to the unit tests of the map.)
  test('[AC-31] the Hearth Haven row selects it at Peek with Here now (2); the handle opens its detail with Alden then the pickup; his row there selects him at Peek; a tab switch at 80 % keeps the size and clears the selection', { tag: ['@phone'] }, async ({ page }) => {
    test.slow(); // three selections and a tab switch
    const cast = loadDemoCast();
    const home = castPlace(cast, 'home');
    const king = castMember(cast, 'king');
    const wagon = castVehicle(cast, 'wagon');
    await openListWithMap(page, 'places');

    await row(page, 'place', 'home').click();
    await expectSelectedAtPeek(page, 'Hearth Haven selected from its row');
    await expect(header(page).locator('.realm-row-name'), 'the header names the place').toHaveText(home.name);
    await expect(header(page).locator('.realm-sel-line'), 'the header counts the people and vehicles inside').toHaveText('Here now (2)');

    await openDetail(page, 'after the handle tap');
    await expect(page.locator('.realm-detail-name'), 'the place detail is its own').toHaveText(home.name);
    await expect(page.locator('.realm-detail-here-title'), 'the heading counts the same two').toHaveText('Here now (2)');
    await expect(page.locator('.realm-detail-here-item .realm-here-name'), 'Alden, then the pickup').toHaveText([king.name, wagon.name]);
    await expect(page.getByText(/comings and goings/i), 'no comings and goings (v1.1)').toHaveCount(0);

    // Alden's row selects him: the sheet goes to Peek with his header.
    await row(page, 'member', 'king').click();
    await expectSelectedAtPeek(page, 'Alden selected from the Here-now list');
    await expect(header(page).locator('.realm-row-name'), 'the header is Alden\'s').toHaveText(king.name);

    // At 80 % again (his detail), a tab switch keeps the size, clears the selection and shows the section with its summary.
    await openDetail(page, 'after the handle tap on Alden');
    await page.getByTestId('tab-vehicles').click();
    await expect(page.getByTestId('tab-vehicles'), 'the Vehicles tab is selected').toHaveAttribute('aria-selected', 'true');
    await expect(page.getByTestId('detail-back'), 'the selection is cleared: no detail').toHaveCount(0);
    await expect(header(page), 'the selection is cleared: no header').toHaveCount(0);
    expect((await readHook(page, 'sheet')).state, 'the tab switch kept the size').toBe('80');
    await expect(rowsOf(page, 'vehicle'), 'the Vehicles list shows').toHaveCount(2);
    await expect(page.getByTestId('sheet-summary'), 'the summary follows the section').toHaveText('2 vehicles · all parked');
  });
});
