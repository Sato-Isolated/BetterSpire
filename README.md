# BetterSpire Guardian

A native C# / Godot mod for **Slay the Spire 2**, with incoming-damage forecasts, a compact teammate hand viewer, a combat journal, and a movable damage meter.

The mod ID and output filenames remain `BetterSpire2Lite`. The current manifest version is `3.6.0-v111` and requires Slay the Spire 2 v0.111.0 or newer.

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

Card details use a dedicated, click-through overlay instead of Godot's automatic tooltip popup. They follow the hovered card and disappear when you move away, drag the viewer, open settings, or leave the game window. This applies equally to the host and other players; multiplayer validation on both machines is still required.

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

Both scripts run seven C# behavior suites, compile the mod, and then verify the v0.111 metadata and IL call contracts. The game-boundary suite links the actual lifecycle, observer, victory resolver and turn-order source against test doubles; it does not run the game engine. Successful builds produce:

```text
dist/BetterSpire2Lite/
├── BetterSpire2Lite.dll
├── BetterSpire2Lite.json
└── BUILD-SUCCESS.txt
```

Build diagnostics are written to `build.log`. The scripts do not modify the game installation.

### Native API integration (v0.111)

- Combat setup and ready/undo notifications use native events. Guardian caches the event-provided combat state; a debug-state read is retained only for late initialization. Deferred native state notifications supplement immediate invalidation signals.
- The early victory patch targets `EndCombatInternal(CombatTurnState)`, not its parameterless test wrapper. The journal still drains before native history is cleared; final combat events provide additional overlay/speed cleanup.
- Forecast end-turn effects respect current extra-turn participants. Multi-target attacks are ordered by hit, then player. Unknown attack lifecycle hooks downgrade forecast confidence instead of executing gameplay callbacks.
- Card titles retain native upgrade suffixes, and display costs preserve the native negative/no-energy-cost sentinel.

In-game checks still required: normal victory and defeat, reset/retry, Instant-mode restoration, multiplayer ready/undo and extra turns, card/potion resolution, multi-hit attacks, and upgraded/negative-cost/X-cost card thumbnails. Passing the standalone tests is not an in-game certification.

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

The behavior suites, v0.111 metadata contract, and mod compilation pass locally. These checks do not validate Godot rendering or actual game input. In-game verification remains necessary, particularly for overlay interactions, scaling, and compatibility with other mods.

The obsolete Python audit runner and duplicate Python reference models have been removed. Python is **not required** to build or test the mod. The optional `tools/cli_reader.py` utility remains available for inspecting .NET assembly metadata and IL without executing the assembly:

```powershell
python tools/cli_reader.py references/sts2.dll CombatManager
python tools/cli_reader.py references/sts2.dll CombatManager --il
```

## Repository layout

- `BetterSpire2/` — mod implementation, feature modules, UI, and game patches.
- `tests/` — six C# behavior projects, the v0.111 API contract, and their JSON fixtures.
- `references/` — assemblies used for compilation.
- `tools/cli_reader.py` — optional assembly inspection utility.
- `build.cmd`, `build.ps1`, `build.sh` — test and packaging entry points.

## Credits

Manifest authors: **jdr** and **MindLated**. Fork origin: [original mod on Nexus Mods](https://www.nexusmods.com/slaythespire2/mods/2).
