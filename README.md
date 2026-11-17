# Uptime – Data Center Tycoon (Unity 6000.6.7f1)

A top-down data center management game. Build a facility, wire power, cooling and networking,
sign procedurally generated customer contracts, assign racks/servers to them, and keep them within SLA.

## Running
- Open this folder in Unity Hub (6000.6.7f1), open `Assets/Scenes/Main.unity`, press Play.
  (The game also self-boots from any scene via `GameManager.AutoBoot`.)
- Or build the player: menu **Uptime → Build Windows Player** (outputs `Builds/Windows/Uptime.exe`).
- Menu **Uptime/** also has: Setup Main Scene, Run Simulation Test, Export Catalog JSON, Check Models.

## Features
- **Building**: foundation, rooms (server hall, network, power, office), outdoor equipment, land expansion.
- **Racks & hardware**: 15 servers, 4 ToR switches, 3 rack types; templates, bulk actions, cloning.
- **Power**: transformers, switchgear, UPS, generators, solar (time-of-day pricing, carbon tax).
- **Cooling**: per-hall zones, CRAC/in-row/liquid cooling, **hot/cold aisle airflow** from rack orientation.
- **Networking**: ToR → core switch cabling, port limits, ISP capacity, **redundant A/B uplinks**.
- **Contracts**: 5 types, SLA tiers, traits, **redundancy and eco requirements**, negotiation, renewals,
  referrals, support **tickets** and customer **satisfaction**; **rival** data centers poach unhappy customers.
- **Staff**: technicians, electricians, network engineers, security — hired from candidates, with skills,
  salaries and desks. They **walk** to failures and tickets, so layout and access matter.
- **Events**: outages, storms, heat waves, fires (fire suppression), coolant leaks, DDoS, chip/GPU shortages,
  price wars, bankruptcies, break-ins, carbon levies, grants.
- **Hardware aging**: failure rates rise after ~1.5 years; refresh old gear.
- **Progression**: 10 reputation levels, research tree, **6 scenarios**, 18 **achievements**, score, **IPO**.
- **Quality of life**: undo (Ctrl+Z), 6 overlays (heat, power, network, customer, age, airflow), dashboard
  charts, 3 save slots + autosave, settings (UI scale, audio, toggles, **key rebinding**), guided objectives.
- **Presentation**: Blender-made equipment models, walking staff, day/night, rain/snow, procedural audio.

## Controls (defaults, all rebindable)
WASD/arrows pan · middle-drag pan · scroll zoom · Q/E rotate · PgUp/PgDn tilt · optional edge-pan
F foundation · B equipment · X demolish · R rotate placement · Ctrl+Z undo · RMB/Esc cancel
M market · C contracts · T research · K staff · G goals · J dashboard · H heat map · O cycle overlays
Space pause · 1-4 speed · F5/F9 quick save/load · Shift+click or box-drag to multi-select racks.

## Code layout
- `Assets/Scripts/Data` – catalog (`Defs.cs`) and serializable save state (`GameState.cs`).
- `Assets/Scripts/Sim` – `Simulation.cs` (power, cooling, network, contracts/SLA, failures, events, economy,
  player actions), `SimSystems.cs` (airflow, redundancy, energy, staff, tickets, rivals, negotiation, aging,
  weather, scenarios, achievements), `ProcGen.cs` (cities, lots, customers, contracts).
- `Assets/Scripts/View` – `WorldView*.cs` (world rendering, overlays, staff, environment), `Models.cs`
  (procedural fallback meshes), `CameraController.cs`.
- `Assets/Scripts/UI` – `GameUI.cs`, `GameUI2.cs` (IMGUI).
- `Assets/Scripts/Core` – `GameManager.cs` (bootstrap, time, tools, undo, save/load), `Settings.cs`
  (preferences, key bindings, rack templates), `AudioFx.cs` (procedural audio), `AutoManager.cs` (scripted
  player used by tests and screenshots).
- `Assets/Editor/BuildTools.cs` – editor menu commands and the multi-seed balance test (`Logs/simtest.txt`).
- `Assets/Tests/Editor` – NUnit edit-mode tests (run from Window → General → Test Runner).
- `Tools/blender/make_models.py` – generates the FBX models in `Assets/Resources/Models`.

## Modding the equipment catalog
`Assets/Resources/Data/catalog.json` overrides the equipment defined in `Defs.cs` (costs, power, cooling,
ports, research levels, etc.). Edit it and restart the game. `kind` and `room` are enum numbers
(see `Defs.cs`). Delete the file to fall back to the in-code catalog; re-export with **Uptime → Export Catalog JSON**.

## Regenerating the 3D models
```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P Tools/blender/make_models.py -- "<project>/Assets/Resources/Models"
```
Any facility without a model (or with the model file deleted) falls back to a procedural mesh.

## Continuous integration
`.github/workflows/tests.yml` runs the edit-mode tests with [GameCI](https://game.ci) on every push.
It needs Unity license secrets in the repo settings (Settings → Secrets and variables → Actions):
`UNITY_LICENSE` (contents of a `.ulf` license file) and/or `UNITY_EMAIL` + `UNITY_PASSWORD`.
See https://game.ci/docs/github/activation. Without them the workflow skips the tests with a notice.

Saves live in `%USERPROFILE%/AppData/LocalLow/Uptime/Uptime - Data Center Tycoon/` (slot 0 = monthly autosave).
