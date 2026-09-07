# BetterSpire Guardian

A native C# / Godot mod for **Slay the Spire 2**, with incoming-damage forecasts, a compact teammate hand viewer, a combat journal, and a movable damage meter.

The mod ID and output filenames remain `BetterSpire2Lite`. The current manifest version is `3.5.3-compact-hud-preview`.

## Features

- **Guardian forecasts:** projected remaining HP and HP loss above characters, with optional details, teammate and pet readouts, and adjustable placement.
- **HandViewer:** square card thumbnails, full card information on hover, teammate navigation, and an optional statistics view.
- **Combat journal:** observed combat events and turn results, with an export shortcut.
- **Damage meter:** current-fight damage during combat and run totals on the map. Enemy block can be included; overkill is excluded. Hiding the meter does not stop collection.
- **Conveniences:** combat speed options, splash-screen skipping, and a movable clock with 12-hour or 24-hour display.

Configure features and overlay placement through **F1**.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| F1 | Open or close settings |
| F2 | Toggle Guardian details when incoming-damage forecasts are enabled |
| F3 | Toggle the HandViewer |
| F4 | Toggle Guardian forecast visibility |
| F5 | Open or close the combat journal |
| F6 | Toggle the damage meter |
| F7 | Toggle performance recording |
| Page Up / Page Down | Switch players while the HandViewer is open and settings are closed |
| Alt + Page Up / Page Down | Change pages in Guardian details |
| Ctrl + F2 | Export the last forecast |
| Ctrl + F5 | Export the journal |
| Ctrl + F7 | Export performance diagnostics |
| Esc | Close an open mod panel, prioritizing settings, then HandViewer, journal, and Guardian details |

## Using the HandViewer

Press **F3** to open it, then drag the player name to move it. Its position is saved and its size follows the content.

Card thumbnails are **42 × 42 logical pixels at 100% scale**, adjustable from 50% to 200% in F1. Cards stay on one row; long hands scroll horizontally. Hover a card for its name, cost, and description, or hover the player name for combat statistics.

The viewer **stays open when you click in the game or on another HUD**. Close it with F3, Esc, or its close button. It also closes at combat end and temporarily hides behind blocking game screens. The former “Keep hand viewer open” setting is no longer needed; saved values for that option are ignored.

In **F1 → Multiplayer**, you can enable opening at combat start, show statistics instead of cards, or hide your own hand. If your own hand is hidden and no teammates are available, no empty panel is displayed.

## Build

### Requirements

- **.NET SDK 9**, as configured by `global.json`.
- These compilation references in `references/`: `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll`.

Compatibility depends on the game APIs in those reference assemblies. A different game build may require updated references and code changes.

From the repository root on Windows:

```powershell
.\build.cmd
```

Or from a Bash environment with the SDK installed:

```bash
bash build.sh
```

Both scripts run all six C# test projects and then compile the mod. Successful builds produce:

```text
dist/BetterSpire2Lite/
├── BetterSpire2Lite.dll
├── BetterSpire2Lite.json
└── BUILD-SUCCESS.txt
```

Build diagnostics are written to `build.log`. The scripts do not modify the game installation.

## Install or update

1. Close the game.
2. Build the mod using the instructions above.
3. Back up any previous BetterSpire installation outside the game's mods directory.
4. Place `BetterSpire2Lite.dll` and `BetterSpire2Lite.json` together in a `BetterSpire2Lite` folder inside the game's mods directory.
5. Launch the game and open F1 to configure the mod.

Keep only one active BetterSpire installation. **Do not copy the reference DLLs into the game or replace the game's own DLLs.** `BUILD-SUCCESS.txt` is a build record and does not need to be installed.

Settings are saved as `betterspire2_settings.json` in the game's Godot user-data directory.

## Development and testing

The build scripts are the main validation entry points. Tests execute the production C# core without starting the game and cover:

| Test project | Coverage |
| --- | --- |
| Guardian | Forecast simulation, overhead formatting, and placement |
| Journal | Combat observations and journal state |
| DamageMeter | Damage accounting, player totals, and combat/run behavior |
| Performance | Refresh throttling, caches, and checkpoints |
| Hud | Setting dependencies, navigation, geometry, and overlay visibility policies |
| Poison | Attribution, allocation, and asynchronous isolation |

To run only the HUD suite:

```powershell
dotnet run --project tests/Hud.Core.Tests.csproj --configuration Release
```

The six suites and mod compilation passed locally for the HandViewer fix. These checks do not validate Godot rendering or actual game input. In-game verification remains necessary, particularly for overlay interactions, scaling, and compatibility with other mods.

The obsolete Python audit runner and duplicate Python reference models have been removed. Python is **not required** to build or test the mod. The optional `tools/cli_reader.py` utility remains available for inspecting .NET assembly metadata and IL without executing the assembly:

```powershell
python tools/cli_reader.py references/sts2.dll CombatManager
python tools/cli_reader.py references/sts2.dll CombatManager --il
```

## Repository layout

- `BetterSpire2/` — mod implementation, feature modules, UI, and game patches.
- `tests/` — six C# test projects and their JSON fixtures.
- `references/` — assemblies used for compilation.
- `tools/cli_reader.py` — optional assembly inspection utility.
- `build.cmd`, `build.ps1`, `build.sh` — test and packaging entry points.

## Credits

Manifest authors: **jdr** and **MindLated**. Fork origin: [original mod on Nexus Mods](https://www.nexusmods.com/slaythespire2/mods/2).
