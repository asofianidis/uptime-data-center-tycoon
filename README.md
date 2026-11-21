# Uptime – Data Center Tycoon (Unity 6000.6.9f1)

A top-down data center management game. Build a facility, wire power, cooling and networking,
sign procedurally generated customer contracts, assign racks/servers to them, and keep them within SLA.

## Running
- Open this folder in Unity Hub (6000.6.9f1), open `Assets/Scenes/Main.unity`, press Play.
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
- **Sandbox mode**: creative mode with unlimited money, all tech, max level, switches for failures/events/bankruptcy,
  and tools to add money, change level, spawn offers, trigger any event, change weather and skip days.
- **Multiple floors**: up to 4 floors per site (PageUp/PageDown). Upper floor slabs need structure below;
  an Elevator & Cable Riser column links floors for staff and network cables. Upper floors never flood.
- **Multiple sites**: from level 4, open data centers in other procedurally generated cities (own climate,
  power prices, grid events, land and staff). Contracts can use racks in any site; geo-redundant customers need two.
- **Infrastructure**: overhead cable trays (cheaper cabling, required for runs over 14 tiles, cables follow them),
  raised floors (flood protection, cooling bonus), battery storage that charges off-peak and covers outages.
- **Shifts & fatigue**: day and night shifts share desks; tired staff slow down.
- **Finance**: term loans and a credit line priced by a **credit score** (uptime, profit, debt, missed payments);
  **insurance** (basic or comprehensive) whose premium drops with suppression, raised floors and security, and which pays
  out on fires, floods, leaks and theft.
- **Maintenance**: equipment fails more the longer it goes unserviced. Auto-maintenance runs in a night window and skips
  units with no spare (N+1) capacity; servicing takes non-redundant racks and single units offline. Customers have
  **peak hours** when downtime counts 1.5x.
- **Staff growth**: XP and skill-ups, paid training courses, raise requests, **morale**, resignations and poaching.
- **Efficiency**: monthly **PUE** (cooling power scales with load), evaporative coolers that use **water** (humidity,
  droughts), **district-heat sales** from waste heat (liquid-cooled halls recover more, winter pays best), and
  **Efficient** customers with a PUE pledge.
- **Markets**: a **spot compute market** for idle capacity (shares WAN with SLA customers), competitive **tenders**
  (RFP bids against rivals), **rival acquisitions**, and rare **hyperscale anchor tenants** needing a dedicated hall
  that ramps 40% → 70% → 100%.
- **Compliance & cages**: site **audits** (access control, suppression, security on both shifts) for regulated
  customers; **private cages** painted inside server halls for caged colocation customers.
- **Supply chain**: standard hardware orders arrive by **truck** (a **loading dock** halves lead times; shortages add
  weeks) and are installed by technicians; express shipping installs instantly at a premium.
- **Neighbours**: houses on one side of each lot; generators, transformers and heat exchangers make **noise** that
  draws complaints and escalating fines. **Acoustic barriers** cut it.
- **After the IPO**: share price, market cap, quarterly guidance (6% growth, 99.5% uptime), board confidence, share
  issues and buybacks - miss too often and the board replaces you.
- **Incidents**: every stretch of customer downtime gets a **post-mortem** (cause chain and suggested fixes); a
  **customer portal** shows each customer's uptime vs SLA, peak hours and why their satisfaction moved.
- **Progression**: 10 reputation levels, research tree, **10 scenarios** (incl. Cold North, Turnaround, Edge Network),
  32 **achievements**, score, **IPO**.
- **Interactive tutorial**: 15 guided steps with world highlights (toggle on New Game).
- **Quality of life**: undo (Ctrl+Z), 6 overlays (heat, power, network, customer, age, airflow), dashboard
  charts, 3 save slots + autosave with thumbnails, settings (UI scale, audio, toggles, **key rebinding**), guided objectives.
- **Presentation**: Blender-made equipment models, walking staff, day/night, rain/snow, procedural audio.

## Controls (defaults, all rebindable)
WASD/arrows pan · middle-drag pan · scroll zoom · Q/E rotate · Home/End tilt · PgUp/PgDn floors · optional edge-pan
F foundation · B equipment · X demolish · R rotate placement · Ctrl+Z undo · RMB/Esc cancel
M market · C contracts · T research · K staff · G goals · J dashboard · H heat map · O cycle overlays
Space pause · 1-4 speed · F5/F9 quick save/load · Shift+click or box-drag to multi-select racks.

## Code layout
- `Assets/Scripts/Data` – catalog (`Defs.cs`) and serializable save state (`GameState.cs`).
- `Assets/Scripts/Sim` – `SimCompany.cs` (company layer: multi-site tick order, cross-site contract
  provisioning, site management), `Simulation.cs` (per-site power, cooling, network, contracts/SLA, failures, events, economy,
  player actions), `SimSystems.cs` (airflow, redundancy, energy, staff, tickets, rivals, negotiation, aging,
  weather, scenarios, achievements), `SimFinance.cs` (loans, credit score, insurance), `SimOps.cs` (maintenance,
  staff growth, PUE, water, heat sales, spot market, noise, deliveries), `SimBusiness.cs` (compliance, cages,
  tenders, anchor tenants, shareholders, acquisitions, incidents), `ProcGen.cs` (cities, lots, customers, contracts).
- `Assets/Scripts/View` – `WorldView*.cs` (world rendering, overlays, staff, environment), `Models.cs`
  (procedural fallback meshes), `CameraController.cs`.
- `Assets/Scripts/UI` – UI Toolkit front-end: `UiKit.cs` (binding scopes, rebuild-on-change sections, element
  builders, charts), `GameUI.cs` (HUD, toolbar, menus), `GameUIInspector.cs`, `GameUIWindows.cs`, `GameUIDialogs.cs`,
  `GameUIBusiness.cs` (loans, insurance, maintenance, tenders, spot, portal, incidents, corporate, compliance tabs).
  Styling lives in `Assets/Resources/UI/Uptime.uss`; the runtime theme and panel settings are generated by
  **Uptime → Setup UI Assets** (also run by Setup Main Scene).
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
