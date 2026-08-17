# MSFS 2024 SAR Locator

A lightweight Windows overlay that helps you find the rescue target during Microsoft Flight Simulator 2024 Career **Search & Rescue** missions.

Career SAR missions drop you into a large search area with no precise marker for the person or vehicle you are looking for. This tool connects to the simulator through SimConnect, scans the SimObjects around your aircraft when you ask it to, picks the most likely rescue target, and then shows a live ADF-style needle, turn command, distance, and bearing so you can fly straight to it.

The locator is read-only with respect to your flight: it never moves the aircraft, never modifies mission or Career data, and never injects objects into the world. The only command it can send to the simulator is the pause menu, and only if you enable the optional auto-pause safety feature.

![Panel style](https://img.shields.io/badge/platform-Windows-blue) ![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)

---

## Download and install

No installer, no SDK, no compiler. Download, unzip, double-click.

### 1. Download

Go to the [latest release](https://github.com/chengchew0204/MSFS2024_SAR_Locator/releases/latest) and download:

```
MSFS-SAR-Locator-v1.0.0.zip
```

### 2. Extract

Extract the zip anywhere you like: Documents, Desktop, or a folder such as `D:\Tools\MSFS-SAR-Locator\`. There is no required location.

Two rules only:

- **Keep all three files in the same folder.** The locator loads the SimConnect libraries from its own folder, so do not move the `.exe` out on its own.
- **Do not put it in the MSFS Community folder.** This is a normal Windows application, not an add-on package. The Community folder will not run it.

### 3. Run

1. Double-click `MSFS2024SARLocator.exe`.
2. Start MSFS 2024 and load a Career **Search & Rescue** mission. The order does not matter; the locator waits and connects on its own.
3. Press `SCAN FOR TARGET` and follow the needle.

### First run: Windows may warn you

The executable is not code-signed, so Windows SmartScreen can show "Windows protected your PC" the first time. Choose **More info**, then **Run anyway**.

If the app does not start at all after extracting, Windows may have marked the download as blocked. Right-click the downloaded zip, choose **Properties**, tick **Unblock**, then extract again.

### What is in the zip

| File | What it is |
|---|---|
| `MSFS2024SARLocator.exe` | The locator itself |
| `Microsoft.FlightSimulator.SimConnect.dll` | Microsoft SimConnect client, managed .NET wrapper |
| `SimConnect.dll` | Microsoft SimConnect client, native x64 runtime |

The two DLLs are Microsoft components from the MSFS 2024 SDK. They let an external application talk to the simulator. They are shipped inside the zip so you never have to install the SDK yourself, and they remain under Microsoft's own license terms rather than this project's GPL license.

### Requirements

| Requirement | Notes |
|---|---|
| Windows 10 or 11 (x64) | The locator is a Windows Forms application |
| Microsoft Flight Simulator 2024 | Career mode, for Search & Rescue missions. The SimConnect server is built into the simulator |
| .NET Framework 4.8 | Already included with current Windows versions |

You do **not** need the MSFS 2024 SDK, Developer Mode, Git, or Visual Studio to use a release build. Those are only needed to build from source.

---

## Usage

1. Start MSFS 2024 and load a Career **Search & Rescue** mission. The locator can also be started first; it will wait and connect on its own.
2. Run `MSFS2024SARLocator.exe`. The header shows `CONNECTED` once SimConnect is live and aircraft data is flowing.
3. Press **`SCAN FOR TARGET`**. You can do this right at the mission start; there is no need to reach the search area first. The button becomes available as soon as aircraft position and heading arrive, and the default 15 NM radius often already covers the rescue target.
4. If a target is found, the header switches to `TARGET LOCKED` and live guidance starts:
   - follow the needle and the turn command,
   - watch the distance count down.
5. If the first scan finds nothing, the target is simply not loaded or not in range yet. Fly toward the mission objective and press the button again. Distance from the scan origin matters more than anything else, so scan again after each leg of your approach or search pattern.
6. Once you are overhead, use `Copy coordinates` in Settings if you want the exact position for a landing plan.

Scanning is entirely on demand. The locator never scans by itself, and pressing the button never depends on the search-area indicator in the header.

Recommended window setup: run MSFS in **windowed** or **borderless windowed** mode and enable `ON TOP`. In exclusive fullscreen, Windows can still draw the simulator above every other window.

### Header controls

| Control | Action |
|---|---|
| Header area | Drag to move the window |
| `SETTINGS` | Expand or collapse the settings panel below the HUD |
| `ON TOP` | Toggle always-on-top; the preference is remembered |
| `-` | Minimize to the taskbar |
| `X` | Exit the application |

### Settings

| Setting | Default | Purpose |
|---|---|---|
| Radius NM | 15 | SimObject scan radius, 1 to 100 NM. Increase only if a scan misses a target you know is nearby |
| Search gate NM | 0.50 | Distance to the navigation target at which the search area is considered active, 0.10 to 3.00 NM. Affects the `SEARCH ACTIVE` indicator only |
| Auto-pause on incident | On | Master switch for the auto-pause feature |
| Crash / Hard landing / Essential part broken | On | Individual incident triggers |
| `Scan target` | - | Same action as the main `SCAN FOR TARGET` button |
| `Reconnect` | - | Drop and re-establish the SimConnect connection |
| `Copy coordinates` | - | Copy the locked target's latitude and longitude to the clipboard |

---

## Features

### Target location

- **One-click target scan.** Press `SCAN FOR TARGET` at any point in the mission, including the moment you spawn. Each press runs one Ground-vehicle SimObject scan and, if nothing convincing is found, one automatic fallback scan across all SimObjects.
- **SAR-aware target scoring.** Object titles and categories are scored against search-and-rescue keywords (`sar`, `rescue`, `victim`, `survivor`, `missing`, `stranded`, `wreck`, `crash`, `hiker`, `bush passengers`). Explicit SAR objects lock immediately; generic vehicles must clear a much higher score before they are accepted.
- **Noise filtering.** Airport and service traffic is rejected outright: fuel, baggage, pushback, catering, belt loaders, de-icing, marshallers, jetways, stair trucks, ground power, tugs, forklifts, plus ambulances, fire trucks, police cars, crew cars, and other aircraft or helicopters.
- **Staging-zone protection.** Objects within 0.18 NM of your on-ground spawn point are ignored for the whole session, so parked vehicles at the departure airport never win the scan.
- **Sticky lock.** Once a target is locked it stays locked. Re-scanning refreshes the same target instead of jumping to a different object, and a long-distance travel skip (Alt+N) re-anchors the session while keeping the lock.

### Live guidance

- **ADF-style direction gauge.** The aircraft symbol stays fixed nose-up and the needle rotates to the target's relative bearing, so the correct turn direction is obvious at a glance.
- **Colour-coded state.** Green means the target is ahead, amber means it is behind you, blue means turn toward the needle.
- **Frame-rate updates.** Aircraft position and heading stream at simulator frame rate and are drained by a 30 ms dispatch pump, with short needle easing, so the gauge tracks your turns smoothly instead of stepping once per second.
- **Data column.** Turn command (`TURN LEFT` / `TURN RIGHT` / `AHEAD` / `TURN AROUND`), exact turn amount in degrees, distance in nautical miles, and the absolute target bearing.
- **Copy coordinates.** Copies the locked target's latitude and longitude to the clipboard.

### Connection

- **Automatic connect and reconnect.** Start the locator before or after MSFS 2024. It retries every three seconds and reconnects by itself when a session ends.
- **Search-area hint.** GPS waypoint and navigation-target telemetry is monitored so the header can switch to `SEARCH ACTIVE` when you cross into the search area. This is informational only; scanning always stays under your control.

### Window behaviour

- **Compact MSFS-style panel.** Frameless, semi-transparent, dark desaturated palette with a thin blue accent strip, matching the in-game panel look.
- **Fully manual window control.** The locator never minimizes, restores, moves, or resizes itself. Drag the header to move it, `ON TOP` toggles always-on-top, `-` minimizes, `X` exits.
- **Tray icon.** Double-click to bring the window back, or use the menu to open the locator, open settings, or exit.

### Optional auto-pause on incident

Enabled by default. When the locator detects an incident it opens the same pause menu as pressing Esc, giving you a chance to react before the mission is lost.

| Toggle | Detection |
|---|---|
| Crash | SimConnect `Crashed` system event |
| Hard landing | Touchdown with G force >= 2.0 or vertical speed <= -10 ft/s (about 600 fpm) |
| Essential part broken | `WEAR AND TEAR EXPOSED PARTS LOWEST LEVEL` dropping from healthy (>= 0.20) to broken (<= 0.05) |

The locator posts Escape to the MSFS window first so the pause UI matches a manual Esc. Because a posted key can be ignored, it waits for the simulator `Paused` event and falls back to the SimConnect pause-menu event after about 1.2 seconds. The status line reports every step, and the monitor re-arms once you unpause.

---

## Troubleshooting

| Symptom | What to check |
|---|---|
| App starts and immediately exits, or does not open at all | All three files must sit in the same folder. Copying only the `.exe` leaves it without the SimConnect libraries |
| Windows blocked the app on first run | SmartScreen: choose **More info**, then **Run anyway**. If the zip came down marked as blocked, right-click it, open **Properties**, tick **Unblock**, and extract again |
| Header stays on `AUTO CONNECT` | MSFS must be running and in a flight or mission, not at the main menu. The locator keeps retrying every three seconds |
| `SCAN FOR TARGET` is greyed out | The locator is waiting for valid aircraft position data. It enables itself as soon as position and heading arrive |
| Scan finds nothing | Fly closer to the mission objective and scan again. Career SAR targets only exist as SimObjects once the simulator has loaded them around your aircraft, so an early scan can come back empty even though a later one succeeds |
| Locator hidden behind the simulator | Enable `ON TOP` and switch MSFS to windowed or borderless windowed mode |
| Nothing happens after copying it into the Community folder | The locator is a standalone Windows application, not an add-on package. Run the `.exe` directly from any folder |
| Nearby rocks or debris block a landing | Landing-site obstacles in SAR missions are static scenery, not SimObjects, so they cannot be detected or removed through SimConnect |
| Crash auto-pause never fires | Crash damage must be enabled in the MSFS assistance settings; otherwise the simulator never raises the `Crashed` event |

---

## Build from source

This section is for developers. Players should use the [release zip](https://github.com/chengchew0204/MSFS2024_SAR_Locator/releases/latest) instead.

### Additional prerequisites

| Requirement | Notes |
|---|---|
| Microsoft Flight Simulator 2024 SDK | Provides the SimConnect libraries. Enable Developer Mode in MSFS, then `Help > SDK Installer` |
| .NET Framework 4.8 | The build script uses the bundled C# compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |

The SDK provides two files that the locator needs:

```
<SDK>\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll   (managed wrapper)
<SDK>\SimConnect SDK\lib\SimConnect.dll                                     (native x64 runtime)
```

With a default SDK installation these live under `C:\MSFS 2024 SDK\`. Neither DLL is committed to this repository, so you must install the SDK before building.

### Build with the included script

```powershell
git clone https://github.com/chengchew0204/MSFS2024_SAR_Locator.git
cd MSFS2024_SAR_Locator
.\RunBuild.bat
```

`RunBuild.bat` runs `Build.ps1`, which:

1. Locates `Microsoft.FlightSimulator.SimConnect.dll` automatically, checking `%MSFS_SDK%`, `%MSFS2024_SDK%`, the root of every fixed drive, and both Program Files folders. If nothing is found it prompts you for the full path.
2. Compiles `Program.cs` with the .NET Framework compiler as an optimized x64 `winexe`.
3. Copies both the managed wrapper and the native `SimConnect.dll` next to the produced executable.

The result is `MSFS2024SARLocator.exe` in the repository root. To point the script at a non-standard SDK location directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1 -SimConnectDll "D:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
```

### Build with MSBuild or Visual Studio

`MSFS2024SARLocator.csproj` resolves the SimConnect reference through the `MSFS_SDK` environment variable, which the SDK installer normally sets.

```powershell
msbuild MSFS2024SARLocator.csproj /p:Configuration=Release /p:Platform=x64
```

Output goes to `bin\Release\`, with the native `SimConnect.dll` copied alongside it.

### Package a release zip

```powershell
powershell -ExecutionPolicy Bypass -File .\Package-Release.ps1 -Version 1.0.0
```

`Package-Release.ps1` builds the project, stages the executable and both SimConnect libraries into a single top-level folder, and writes `dist\MSFS-SAR-Locator-v1.0.0.zip`. The `dist` folder is ignored by Git; upload the zip as a GitHub release asset.

### Deploying a build manually

`MSFS2024SARLocator.exe` needs both SimConnect DLLs in the same folder. To move the locator elsewhere, copy these three files together:

```
MSFS2024SARLocator.exe
Microsoft.FlightSimulator.SimConnect.dll
SimConnect.dll
```

No installer, registry entries, or configuration files are used.

---

## Project layout

| File | Purpose |
|---|---|
| `Program.cs` | Entire application: SimConnect integration, scan engine, target scoring, HUD, gauge, and settings |
| `Build.ps1` | SDK discovery and single-file compile script |
| `RunBuild.bat` | Double-click wrapper for `Build.ps1` |
| `Package-Release.ps1` | Builds and packages the distributable release zip |
| `MSFS2024SARLocator.csproj` | MSBuild / Visual Studio project |

## License

Released under the [GNU General Public License v3.0](LICENSE). The SimConnect libraries from the MSFS 2024 SDK are not included in this repository. They are redistributed inside the release zip for convenience and remain under Microsoft's own license terms.

## Notes

- Verified in live Career missions: `Car Bush Passengers` is a real SAR rescue target and is treated as a high-confidence match.
- SimConnect exposes only what the simulator chooses to publish. Some Career objective markers are not normal flight-plan waypoints, which is why the search-area indicator is a hint rather than a hard gate.
- This is a community project and is not affiliated with or endorsed by Microsoft or Asobo Studio.
