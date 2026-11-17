# Uptime – Data Center Tycoon (Unity 6000.6.7f1)

A top-down data center management game. Build a facility, wire power, cooling and networking,
sign procedurally generated customer contracts, assign racks/servers to them, and keep them within SLA.

## Running
- Open this folder in Unity Hub (6000.6.7f1) and open `Assets/Scenes/Main.unity`, press Play.
  (The game also self-boots from any scene via `GameManager.AutoBoot`.)
- Or run the prebuilt player: `Builds/Windows/Uptime.exe`.
- Menu **Uptime/** has: Setup Main Scene, Build Windows Player, Run Simulation Test.

## Controls
WASD/arrows pan · middle-drag pan · scroll zoom · Q/E rotate · PgUp/PgDn tilt
F foundation · B equipment · X demolish · R rotate placement · RMB/Esc cancel
M market · C contracts · T research · H heat map · Space pause · 1-4 speed · F5/F9 save/load
Shift+click or drag-box to multi-select racks for bulk actions (assign, cable, fill, clone hardware).

## Code layout
- `Assets/Scripts/Data` – catalog of servers/switches/facilities (`Defs.cs`), serializable save state (`GameState.cs`).
- `Assets/Scripts/Sim` – `Simulation.cs` (power, cooling zones, network, contracts/SLA, failures, events, economy, all player actions),
  `ProcGen.cs` (cities, lots, customer names, contracts).
- `Assets/Scripts/View` – procedural meshes (`Models.cs`, `MeshBuilder.cs`), `WorldView.cs`, `CameraController.cs`.
- `Assets/Scripts/UI/GameUI.cs` – all UI (IMGUI).
- `Assets/Scripts/Core` – `GameManager.cs` (bootstrap, time, tools, save/load), `AutoManager.cs` (scripted player for tests/screenshots).
- `Assets/Editor/BuildTools.cs` – scene setup, Windows build, headless multi-seed balance test (writes `Logs/simtest.txt`).

Saves: `%USERPROFILE%/AppData/LocalLow/Uptime/Uptime - Data Center Tycoon/datacenter_slot1.json` (slot 0 = monthly autosave).
