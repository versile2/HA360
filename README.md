<div align="center">

<img src="realm/logo.png" alt="HA Cartographer" width="250">

# HA Cartographer

**A map-first family locator for Home Assistant. Everyone, on one map, on your own server.**

[![Status: experimental](https://img.shields.io/badge/status-experimental-E8BC4E?style=flat-square&labelColor=0B0E1F)](realm/CHANGELOG.md)
[![Home Assistant add-on](https://img.shields.io/badge/Home%20Assistant-add--on-5CC8FF?style=flat-square&labelColor=0B0E1F)](#install)
[![Privacy: no cloud](https://img.shields.io/badge/privacy-no%20cloud%2C%20no%20telemetry-4ADE80?style=flat-square&labelColor=0B0E1F)](#privacy)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-B794F6?style=flat-square&labelColor=0B0E1F)](LICENSE)

[**Install**](#install) &nbsp;·&nbsp; [**Features**](#what-it-does) &nbsp;·&nbsp; [**Gallery**](#gallery) &nbsp;·&nbsp; [**Privacy**](#privacy) &nbsp;·&nbsp; [**FAQ**](#faq) &nbsp;·&nbsp; [**Landing page**](docs/index.html)

<br>

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-map-peek.png" width="210" alt="The map with four people, a vehicle and the bottom sheet resting at its peek height"></td>
    <td align="center"><img src="assets/screenshots/phone-sheet-drivers.png" width="210" alt="The bottom sheet opened to 80 percent, listing five drivers with status and battery"></td>
    <td align="center"><img src="assets/screenshots/phone-driving-report.png" width="210" alt="The weekly Driving report with speeding, phone use, top speed, drives and miles"></td>
    <td align="center"><img src="assets/screenshots/phone-map-styles.png" width="210" alt="The map style picker offering Night, Day, Streets and Satellite"></td>
  </tr>
</table>

<sub>Every screenshot on this page is Demo mode: an invented court on a frozen clock. No real person, place or map data appears.</sub>

</div>

---

## What it does

HA Cartographer runs inside Home Assistant as an add-on (newer releases call them apps). It reads the people and trackers
Home Assistant already knows about, from the **Life360 integration** and the **companion app**, and draws them on a
full-screen map. It opens from the Home Assistant sidebar, so Home Assistant's own login is the only login.

Status: experimental. [realm/CHANGELOG.md](realm/CHANGELOG.md) lists what each version contains and what it does not do yet.

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>Map first</h3>
      A full-screen map with pins for people, vehicles and your saved places. Tap a pin to select it; the sheet follows.
      Those who are far away do not shrink the view: they wait at the edge as <b>edge bubbles</b> that point the way.
    </td>
    <td width="50%" valign="top">
      <h3>One sheet, two heights</h3>
      A bottom sheet rests at a <b>Peek</b> so the map stays yours, and opens to <b>80 percent</b> for the full
      <b>Drivers</b>, <b>Vehicles</b> and <b>Places</b> lists. On a wide screen it becomes a side panel.
    </td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-member-selected.png" width="230" alt="A selected member, Cass, shown in a compact card at the peek height"></td>
    <td align="center"><img src="assets/screenshots/phone-sheet-vehicles.png" width="230" alt="The Vehicles tab listing the vehicles that are on the map"></td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>Weekly driving report</h3>
      Trips are recorded as they close and kept on your server, so reports reach back further than Home Assistant's
      recorder does. Six statistics (speeding, phone use, rapid acceleration, hard braking, top speed, drives and miles),
      a card per driver and a week-by-week picker.
    </td>
    <td width="50%" valign="top">
      <h3>Every number explained</h3>
      Tap a statistic and a popup explains where it comes from, with a bar per driver. Drives and miles flip between the
      two views. Missing data shows a dash, not a guess.
    </td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-driver-week.png" width="230" alt="One driver's week: totals and a list of that week's trips"></td>
    <td align="center"><img src="assets/screenshots/phone-popup-speeding.png" width="230" alt="A popup explaining the Speeding statistic with a bar for each driver"></td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>Places that mean something</h3>
      Saved places are drawn as circles on the map and listed with who is there. Open one for its address, who is
      inside, and how far away it is.
    </td>
    <td width="50%" valign="top">
      <h3>Switchable map styles</h3>
      Night, Day, Streets or Satellite, from the layers button, and a toggle for the place circles. The demo uses its own
      offline map so nothing is fetched.
    </td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-place-detail.png" width="230" alt="Detail of a place with its address, people present and this week's totals"></td>
    <td align="center"><img src="assets/screenshots/phone-settings.png" width="230" alt="Settings: map style, show places, default view radius and layout"></td>
  </tr>
</table>

Also: a **default view radius** (you plus those within 6 to 155 miles, or everyone), **layout** choice (auto, bottom sheet or
side panel), battery and staleness cues ("last seen 42 min ago", "location isn't shared"), and a **Demo mode** with a
fictional court so you can check the install before any real data is involved.

## Gallery

Phone first, then unfolded. Each frame is a real capture of the running app in Demo mode.

### On a phone

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-map-peek.png" width="180" alt="Map at peek height"><br><sub>Map, Peek</sub></td>
    <td align="center"><img src="assets/screenshots/phone-map-far-members.png" width="180" alt="Map with saved places as circles and a far member at the edge"><br><sub>Edge bubble &amp; places</sub></td>
    <td align="center"><img src="assets/screenshots/phone-member-selected.png" width="180" alt="A selected member at the peek height"><br><sub>Selected member</sub></td>
    <td align="center"><img src="assets/screenshots/phone-map-styles.png" width="180" alt="Map style picker"><br><sub>Map styles</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-sheet-drivers.png" width="180" alt="Drivers list at 80 percent"><br><sub>Drivers, 80%</sub></td>
    <td align="center"><img src="assets/screenshots/phone-sheet-vehicles.png" width="180" alt="Vehicles list at 80 percent"><br><sub>Vehicles</sub></td>
    <td align="center"><img src="assets/screenshots/phone-sheet-places.png" width="180" alt="Places list at 80 percent"><br><sub>Places</sub></td>
    <td align="center"><img src="assets/screenshots/phone-place-detail.png" width="180" alt="Place detail"><br><sub>Place detail</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-driving-report.png" width="180" alt="Weekly Driving report"><br><sub>Driving report</sub></td>
    <td align="center"><img src="assets/screenshots/phone-popup-drives.png" width="180" alt="Total drives popup"><br><sub>Popup: drives</sub></td>
    <td align="center"><img src="assets/screenshots/phone-popup-miles.png" width="180" alt="Total drives popup showing miles"><br><sub>Popup: miles</sub></td>
    <td align="center"><img src="assets/screenshots/phone-popup-speeding.png" width="180" alt="Speeding popup"><br><sub>Popup: speeding</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-driver-week.png" width="180" alt="One driver's week"><br><sub>Driver's week</sub></td>
    <td align="center"><img src="assets/screenshots/phone-report-last-week.png" width="180" alt="The report for an earlier week"><br><sub>Earlier weeks</sub></td>
    <td align="center"><img src="assets/screenshots/phone-settings.png" width="180" alt="Settings"><br><sub>Settings</sub></td>
    <td align="center"><img src="assets/screenshots/phone-settings-about.png" width="180" alt="Settings: connections and about"><br><sub>Connections &amp; credits</sub></td>
  </tr>
</table>

### Unfolded and tablet

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/unfolded-map-and-panel.png" width="330" alt="Map with the drivers list as a side panel"><br><sub>Map and side panel</sub></td>
    <td align="center"><img src="assets/screenshots/unfolded-vehicles-panel.png" width="330" alt="Vehicles in the side panel"><br><sub>Vehicles</sub></td>
    <td align="center"><img src="assets/screenshots/unfolded-places-panel.png" width="330" alt="Places in the side panel"><br><sub>Places</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/unfolded-detail-panel.png" width="330" alt="Detail view in the side panel"><br><sub>Detail</sub></td>
    <td align="center"><img src="assets/screenshots/unfolded-map-styles.png" width="330" alt="Map style picker on a wide screen"><br><sub>Map styles</sub></td>
    <td align="center"><img src="assets/screenshots/unfolded-driving-report.png" width="330" alt="Driving report on a wide screen"><br><sub>Driving report</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/unfolded-popup-miles.png" width="330" alt="Statistic popup on a wide screen"><br><sub>Statistic popup</sub></td>
    <td align="center"><img src="assets/screenshots/unfolded-settings.png" width="330" alt="Settings on a wide screen"><br><sub>Settings</sub></td>
    <td></td>
  </tr>
</table>

<details>
<summary><b>Quiet states: nothing to show, nothing known, nothing reachable</b></summary>
<br>

The app says what it does not know instead of inventing it.

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/phone-report-empty.png" width="200" alt="A report week with no record yet: every figure is a dash"><br><sub>No record yet for a week</sub></td>
    <td align="center"><img src="assets/screenshots/phone-state-ha-unreachable.png" width="200" alt="The map with a banner saying Home Assistant cannot be reached and the app is retrying"><br><sub>Home Assistant unreachable: banner, retry, clears by itself</sub></td>
  </tr>
</table>

</details>

## Live in Home Assistant

These are captures of the add-on installed on a real Home Assistant instance, running in Demo mode and opened through Ingress with the Home Assistant frame cropped away. Every person, place and trip is fictional.

### Map styles (phone)

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-map-peek-night.png" width="180" alt="The map in the Night style with four people, a pickup and the bottom sheet at its peek height"><br><sub>Night (default)</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-map-day.jpg" width="180" alt="The map in the Day style, a pale greyscale base with the same pins"><br><sub>Day</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-map-streets.jpg" width="180" alt="The map in the Streets style, with coloured roads, water and parks"><br><sub>Streets</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-map-satellite.jpg" width="180" alt="The map in the Satellite style, USGS aerial imagery under the pins"><br><sub>Satellite</sub></td>
  </tr>
</table>

### Map and sheet

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-sheet-drivers.png" width="180" alt="The bottom sheet opened to 80 percent, listing five drivers with status and battery"><br><sub>Drivers, 80%</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-member-selected-edge-bubbles.png" width="180" alt="Alden selected: the map centred on him in a compact card, with the other members as edge bubbles"><br><sub>Selected member, edge bubbles</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-layers-popover.png" width="180" alt="The layers popover with Night, Day, Streets, Satellite and a Show places switch"><br><sub>Layers popover</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-sheet-vehicles.png" width="180" alt="The Vehicles tab listing the vehicles that are on the map"><br><sub>Vehicles</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-sheet-places.png" width="180" alt="The Places tab listing fourteen places and who is at each"><br><sub>Places</sub></td>
  </tr>
</table>

### Members

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-member-alden.png" width="180" alt="Alden at Hearth Haven, charging, with this week's drives, miles and top speed"><br><sub>Alden, at home</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-member-briar-driving.png" width="180" alt="Briar driving at 54 mph on I-65 with battery and the week's totals"><br><sub>Briar, driving</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-member-cass-low-battery.png" width="180" alt="Cass at The Jester's Hall with a low-battery badge and a street address"><br><sub>Cass, low battery</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-member-dara-stale.png" width="180" alt="Dara's last-seen warning, &quot;The raven's late&quot;, with the pin drawn dashed"><br><sub>Dara, stale</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-member-elio-static-pin.png" width="180" alt="Elio, who does not share his location, shown as a static pin at his home place"><br><sub>Elio, location not shared</sub></td>
  </tr>
</table>

### Details

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-vehicle-detail.png" width="180" alt="The pickup's detail: location and last update"><br><sub>Vehicle detail</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-place-detail.png" width="180" alt="Hearth Haven with its radius and the people and vehicles there now"><br><sub>Place detail</sub></td>
  </tr>
</table>

### Driving

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-driving-weekly-report.png" width="180" alt="The weekly Driving report with six statistics and a card per driver"><br><sub>Weekly report</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-last-week.png" width="180" alt="The report for the previous week"><br><sub>Earlier week</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-driver-week.png" width="180" alt="Alden's week: totals and the trips of each day"><br><sub>A driver's week</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-speeding-popup.png" width="180" alt="The Speeding popup with a bar per driver"><br><sub>Speeding popup</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-driving-phone-use-popup.png" width="180" alt="The Phone use popup, with dashes for drivers whose phone does not share it"><br><sub>Phone-use popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-top-speed-popup.png" width="180" alt="The Top speed popup naming the fastest drive"><br><sub>Top-speed popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-total-drives-popup.png" width="180" alt="The Total drives popup with a bar per driver"><br><sub>Drives popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-driving-unrecorded-stat-popup.png" width="180" alt="A statistic popup for something not recorded yet: every row is a dash"><br><sub>Not recorded yet</sub></td>
  </tr>
</table>

### Settings

Settings is the third item of the bottom bar, to the right of Location and Driving. At the bottom of the dialog, **Report an issue** and **Star this project** open GitHub in a new tab.

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-phone-settings.png" width="180" alt="Settings: map style, show places, default view and layout"><br><sub>Settings</sub></td>
    <td align="center"><img src="assets/screenshots/live-phone-settings-about.png" width="180" alt="Settings: connection status, version and map credits"><br><sub>Connections and credits</sub></td>
  </tr>
</table>

### On a wide screen

The unfolded two-pane layout, one capture per view.

<table>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-map-night.png" width="330" alt="The desktop two-pane layout in the Night style: drivers panel left, map right"><br><sub>Night</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-map-day.jpg" width="330" alt="The desktop layout in the Day style"><br><sub>Day</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-map-streets.jpg" width="330" alt="The desktop layout in the Streets style"><br><sub>Streets</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-map-satellite.jpg" width="330" alt="The desktop layout in the Satellite style"><br><sub>Satellite</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-layers-popover.jpg" width="330" alt="The layers popover on the desktop layout"><br><sub>Layers popover</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-attribution-credit.png" width="330" alt="The map credit pill expanded at the top right"><br><sub>Map credit</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-member-alden.png" width="330" alt="A selected member in the side panel with the map centred on him"><br><sub>Alden</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-member-briar-driving.png" width="330" alt="Briar driving, in the side panel"><br><sub>Briar driving</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-member-cass-low-battery.png" width="330" alt="Cass at home with a low battery"><br><sub>Cass</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-member-dara-stale.png" width="330" alt="Dara with the last-seen warning"><br><sub>Dara, stale</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-member-elio-static-pin.png" width="330" alt="Elio's static pin"><br><sub>Elio</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-panel-vehicles.png" width="330" alt="The Vehicles panel"><br><sub>Vehicles</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-vehicle-awaiting-integration.png" width="330" alt="The Vehicles panel on a wide screen"><br><sub>Vehicles</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-panel-places.png" width="330" alt="The Places panel"><br><sub>Places</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-place-detail.png" width="330" alt="Place detail in the side panel"><br><sub>Place detail</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-vehicle-detail.png" width="330" alt="Vehicle detail in the side panel"><br><sub>Vehicle detail</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-settings.png" width="330" alt="Settings dialog on the desktop layout"><br><sub>Settings</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-settings-about.png" width="330" alt="The Settings dialog scrolled to connections and about"><br><sub>Credits</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-weekly-report.png" width="330" alt="The weekly Driving report on a wide screen"><br><sub>Driving report</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-last-week.png" width="330" alt="The report for last week"><br><sub>Last week</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-earlier-week.png" width="330" alt="The report for the earliest week offered"><br><sub>Sep 7 to Sep 13</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-speeding-popup.png" width="330" alt="The Speeding popup on a wide screen"><br><sub>Speeding popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-phone-use-popup.png" width="330" alt="The Phone use popup on a wide screen"><br><sub>Phone-use popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-top-speed-popup.png" width="330" alt="The Top speed popup on a wide screen"><br><sub>Top-speed popup</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-total-drives-popup.png" width="330" alt="The Total drives popup on a wide screen"><br><sub>Drives popup</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-driver-week.png" width="330" alt="Alden's week on a wide screen"><br><sub>A driver's week</sub></td>
    <td align="center"><img src="assets/screenshots/live-desktop-driving-drive-list.png" width="330" alt="Alden's trips by day, with speeding and phone-use markers"><br><sub>Trip list</sub></td>
  </tr>
</table>

## Install

HA Cartographer is a Home Assistant add-on (newer releases of Home Assistant call add-ons apps), so it needs an installation
that has the Supervisor (Home Assistant OS or Supervised) on an amd64 machine.

1. **Add the repository.** In Home Assistant open **Settings -> Apps -> Install app**, open the menu (three dots),
   choose **Repositories**, paste `https://github.com/Versile2/ha-cartographer` exactly and press **Add**. Close the dialog and
   reload the page if the store does not list the app yet.
2. **Install.** Open **HA Cartographer** in the store and press **Install**. The Supervisor pulls the image
   `ghcr.io/versile2/ha-cartographer` (see the registry note below).
3. **Choose Demo mode first.** Before the first start open the app's **Configuration** tab, switch on **Demo mode**
   (`demo_mode: true` if you edit as YAML) and save. Nothing else needs to be filled in.
4. **Start and open it.** Press **Start**, switch on **Show in sidebar** (Home Assistant keeps this as a per-install
   setting, so the app cannot do it for you; **Watchdog** is optional) and open the sidebar entry called **The Realm** (the
   crown icon; the app is named HA Cartographer in the store). It also opens in the Home Assistant companion app. You should see the invented family on
   the map and a Driving report; that confirms the install works before any real data is involved.
5. **Switch to your own household.** Turn **Demo mode** off and restart the app. Everyone Home Assistant knows
   (its `person` entities and its GPS `device_tracker`s that have reported in the last 30 days) is put on the map
   at the first start, and you get one notification in Home Assistant saying so. Then open **Settings -> Who's on the
   map** inside the app to sort them: drag a row (or use its menu) between **People**, **Vehicles** and **Not tracked**,
   and tap a row to change its name, title and colour. Nothing is typed into the add-on options: they hold six switches
   and thresholds only (Demo mode, the demo address parameter, the log level, how long positions are kept, the week
   start and the speeding limit). Your names and entity ids stay in the app's database on your own Home Assistant.
   They are never part of this repository or the image.

The app's own **Documentation** tab ([realm/DOCS.md](realm/DOCS.md)) covers where the data comes from, what is stored
and where, the phone-use statistic and troubleshooting.

**Registry note.** The image is a package on the GitHub Container Registry, `ghcr.io/versile2/ha-cartographer:<version>`, and it
can be private until the owner of the repository makes it public. While it is private, Home Assistant cannot pull it and
the installation fails with a pull error (for example `unauthorized` or `denied` in the Supervisor log). In that case
either wait until the package is public, or give your Home Assistant a registry login for `ghcr.io` (a GitHub account
name and a token that may read packages).

## Privacy

- **Your data stays on your Home Assistant.** Positions, trips and the list of who is on the map live in a local SQLite file inside the
  add-on and are included in your backups. Positions are kept for 120 days by default.
- **No cloud, no analytics, no telemetry, no update checks, no geocoding.** Ingress is the only door in; the app opens no port.
- **No Life360 login.** It reads Home Assistant's entities and never calls Life360's API. The one exception is member
  pictures served from `life360.com`, fetched without credentials.
- **It writes one thing to Home Assistant:** persistent notifications ("HA Cartographer: ...") when someone joins the map or is moved off it. Nothing else is changed there, and Demo mode sends none.
- **Map tiles** are fetched by your browser from public servers (OpenFreeMap, and USGS for Satellite), which see your IP
  address and the area in view, not who is on the map. Demo mode uses an offline map and fetches nothing.

Details: [realm/DOCS.md](realm/DOCS.md#what-is-stored-and-where).

## FAQ

<details>
<summary><b>Why is the add-on called HA Cartographer but the sidebar says "The Realm"?</b></summary>
<br>
On purpose. The add-on store lists the project by its name, HA Cartographer. Inside Home Assistant the sidebar entry,
with the crown icon, is called <b>The Realm</b>, the in-app theme of kings, courts and royal scribes.
</details>

<details>
<summary><b>Do I need a Life360 account?</b></summary>
<br>
Not for the app itself. It reads what Home Assistant's Life360 integration and the companion app already publish; it holds
no Life360 login or token. Without them, only what Home Assistant knows is shown.
</details>

<details>
<summary><b>Does it work on Home Assistant Container or Core?</b></summary>
<br>
Not today. Add-ons need the Supervisor (Home Assistant OS or Supervised), and the image is built for amd64.
</details>

<details>
<summary><b>The phone-use figure is a dash.</b></summary>
<br>
Enable the disabled <code>binary_sensor.&lt;device&gt;_interactive</code> entity of the companion app. The app never changes
your Home Assistant configuration for you. See <a href="realm/DOCS.md#phone-use-stat">the docs</a>.
</details>

<details>
<summary><b>I cannot see the sidebar entry.</b></summary>
<br>
Switch on <b>Show in sidebar</b> on the app's page. Home Assistant keeps that as a per-install setting.
</details>

<details>
<summary><b>The install fails with a pull error.</b></summary>
<br>
The image may still be private. See the registry note under <a href="#install">Install</a>.
</details>

## Build

- .NET 10 SDK (see `global.json`): `dotnet build Realm.slnx` and `dotnet test Realm.slnx`.
- CI: `.github/workflows/ci.yml`. Every run publishes a summary to the `ci-artifacts` branch; see `tools/ci/README.md`.
  The screenshots above come from its gallery run.
- Releases: merging a PR to main releases automatically when `version` in `realm/config.yaml` is new (and `realm/CHANGELOG.md`
  has its `## MAJOR.MINOR.PATCH` section). The release workflow builds the image and pushes
  `ghcr.io/versile2/ha-cartographer:<version>` (never `latest`), and only then creates the tag `v<version>` and a GitHub release
  whose notes are the CHANGELOG section plus notes generated from the merged PRs. A merge whose version already has a tag
  releases nothing. To run it by hand: Actions -> release -> Run workflow, on main.

## Licence

HA Cartographer is released under the MIT licence ([LICENSE](LICENSE)). The components it bundles or fetches, and the
licences and credits they ask for, are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The image carries
both files in `/app`. Map data &copy; OpenStreetMap contributors; interface built with MudBlazor.

## Trademarks

HA Cartographer is an independent, unofficial project. It is not affiliated with, endorsed by or sponsored by Life360, Inc.,
the Open Home Foundation or Nabu Casa. Life360 is a trademark of Life360, Inc. Home Assistant names and logos belong
to their owners. These names are used only to say what the app works with.
