using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DCT
{
    public enum Tool { Select, Foundation, Room, Demolish, Place, Connect, Tray, Raised }

    public class GameManager : MonoBehaviour
    {
        public static GameManager I;

        public Simulation sim;
        public WorldView view;
        public CameraController cam;
        public GameUI ui;
        public Light sun;

        public bool inGame;
        public int speed = 1;
        public static readonly float[] SpeedHoursPerSec = { 0f, 4f, 12f, 36f, 96f };
        float hourAccum;

        public Tool tool = Tool.Select;
        public RoomType roomType = RoomType.ServerHall;
        public FacilityDef placeDef;
        public int placeRot;
        public readonly List<PlacedObject> selection = new List<PlacedObject>();
        public Vector2Int hoverTile = new Vector2Int(-1, -1);
        public PlacedObject hoverObject;
        public bool dragging;
        public Vector2Int dragStart;
        public string toolHint;
        public PlacedObject connectFrom;
        public bool connectSecondary;
        public AudioFx audio;

        // ---- undo ----
        readonly List<string> undoStack = new List<string>();
        float lastSnapshot = -10f;
        public int UndoCount => undoStack.Count;

        /// <summary>Fraction of the current in-game hour that has elapsed (for smooth animation).</summary>
        public float HourFraction => speed == 0 ? 1f : Mathf.Clamp01(hourAccum);

        public static string SavePath(int slot) => Path.Combine(Application.persistentDataPath, $"datacenter_slot{slot}.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindAnyObjectByType<GameManager>() == null) new GameObject("GameManager").AddComponent<GameManager>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Settings.Load();
            Settings.ApplyGraphics();
            // Re-assert the saved display mode/resolution, unless a capture run sized the window itself.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-shotdir") < 0 && PlayerPrefs.HasKey("set_dispmode"))
                Settings.ApplyDisplay();
            Catalog.EnsureLoaded();
            SetupScene();
        }

        System.Collections.IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-shotdir");
            if (i < 0 || i + 1 >= args.Length) yield break;
            string dir = args[i + 1];
            Directory.CreateDirectory(dir);
            int days = 60;
            int di = System.Array.IndexOf(args, "-shotdays");
            if (di >= 0 && di + 1 < args.Length) int.TryParse(args[di + 1], out days);

            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "0_menu.png"));
            yield return null; yield return null;

            StartNewGame(20261117, "Normal");
            sim.popups.Clear();
            AutoManager.BuildStarter(sim);
            for (int d = 0; d < days; d++)
            {
                AutoManager.DailyManage(sim);
                for (int h = 0; h < 24; h++) { sim.TickHour(); while (sim.popups.Count > 0) { var p = sim.popups.Dequeue(); p.options[0].Value?.Invoke(); } }
            }
            // Advance to early afternoon, break a few servers so staff are out walking.
            while (sim.S.hour % 24 != 13) sim.TickHour();
            var broken = sim.Racks.Where(r => r.devices.Count > 0).Take(3).ToList();
            foreach (var r in broken) { r.devices[0].failed = true; sim.MarkDirty(r); }
            sim.TickHour();
            sim.popups.Clear();
            speed = 0;
            var S = sim.S;
            S.weather = "Clear";
            var center = new Vector3((S.ownX0 + S.ownX1) * 0.5f, 0, (S.ownY0 + S.ownY1) * 0.5f);
            System.Collections.IEnumerator Shot(string name)
            {
                for (int k = 0; k < 12; k++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null; yield return null;
            }

            cam.SnapTo(center - Vector3.forward, 24f);
            yield return Shot("01_overview");

            var rack = sim.Racks.FirstOrDefault(r => r.devices.Count > 0);
            if (rack != null) { selection.Clear(); selection.Add(rack); cam.SnapTo(new Vector3(rack.x + 0.5f, 0, rack.y + 0.5f), 10f); }
            yield return Shot("02_rack_inspector");
            selection.Clear();

            var staffer = S.staff.FirstOrDefault(m => m.state != "idle") ?? S.staff.FirstOrDefault();
            if (staffer != null) cam.SnapTo(new Vector3(staffer.x, 0, staffer.y), 7f);
            yield return Shot("03_staff_closeup");

            cam.SnapTo(center, 24f);
            ui.OpenWindow(GameUI.Window.Market);
            yield return Shot("04_market");
            ui.OpenWindow(GameUI.Window.Contracts);
            yield return Shot("05_contracts");
            ui.OpenWindow(GameUI.Window.Staff);
            yield return Shot("06_staff");
            ui.OpenWindow(GameUI.Window.Goals);
            yield return Shot("07_goals");
            ui.OpenWindow(GameUI.Window.Dashboard);
            yield return Shot("08_dashboard");
            ui.OpenWindow(GameUI.Window.Research);
            yield return Shot("08b_research");
            ui.OpenWindow(GameUI.Window.Finance);
            yield return Shot("08c_finance");
            ui.OpenWindow(GameUI.Window.None);

            view.SetOverlay(Overlay.Airflow);
            cam.SnapTo(center, 18f);
            yield return Shot("09_overlay_airflow");
            view.SetOverlay(Overlay.Network);
            yield return Shot("10_overlay_network");
            view.SetOverlay(Overlay.None);

            S.hour = S.Day * 24 + 22;
            S.weather = "Rain";
            cam.SnapTo(center, 22f);
            yield return Shot("11_night_rain");
            S.weather = "Clear";
            S.hour = S.Day * 24 + 13;

            // Cable trays along the rack rows, cables re-routed over them.
            foreach (var rk in sim.Racks.Take(8).ToList()) { sim.Disconnect(rk); AutoManager.LayTrayToCore(sim, rk); }
            cam.SnapTo(center, 16f);
            yield return Shot("11b_cable_trays");

            // Second floor: slab over the building, a server hall upstairs and an elevator/riser column.
            {
                S.money += 1_000_000;
                int bw = S.ownX1 - S.ownX0, bh = S.ownY1 - S.ownY0;
                var bld = new RectInt(S.ownX0 + bw / 2 - 7, S.ownY0 + bh / 2 - 5, 15, 11);
                sim.SetFloor(1);
                sim.BuildFoundation(bld, true, out _);
                sim.PaintRoom(new RectInt(bld.x, bld.y + 3, 10, 8), RoomType.ServerHall, true, out _);
                var core0 = sim.ObjectsOfKind(FacilityKind.CoreSwitch).First();
                int ex = bld.x + 10, ey = bld.y + 2;
                foreach (int fl in new[] { 0, 1 })
                {
                    sim.SetFloor(fl);
                    var blocker = sim.ObjectAt(ex, ey);
                    if (blocker != null) sim.RemoveObject(blocker);
                    sim.PaintRoom(new RectInt(ex, ey, 1, 1), RoomType.Corridor, true, out _);
                    sim.Place(Catalog.Facility("elevator"), ex, ey, 0);
                }
                sim.SetFloor(1);
                var shaft = sim.ObjectAt(ex, ey);
                for (int ri = 0; ri < 6; ri++)
                {
                    var rk = sim.Place(Catalog.Facility("rack_std"), bld.x + 1 + ri, bld.y + 8, 0);
                    if (rk == null) continue;
                    sim.AddDevice(rk, Catalog.Device("tor_1g"));
                    sim.FillRack(rk, Catalog.Device("srv_basic"), 12);
                    if (shaft != null) AutoManager.LayTray(sim, rk, shaft);
                    sim.Connect(rk, core0);
                }
                sim.Place(Catalog.Facility("crac"), bld.x + 8, bld.y + 9, 0);
                for (int h = 0; h < 6; h++) sim.TickHour();
                sim.popups.Clear();
                cam.SnapTo(center, 20f);
                for (int k = 0; k < 20; k++) yield return null;
                yield return Shot("11g_floor2");
                sim.SetFloor(0);
                yield return Shot("11h_floor1_again");
            }

            // Second site in another city.
            S.money += 2_000_000;
            S.xp = Mathf.Max(S.xp, Catalog.XpForLevel(Company.MinLevelForSites));
            ui.OpenWindow(GameUI.Window.Sites);
            yield return Shot("11c_sites_window");
            ui.OpenWindow(GameUI.Window.None);
            OpenSite(sim.company.SiteCandidates()[1]);
            AutoManager.BuildStarter(sim);
            for (int h = 0; h < 30; h++) sim.TickHour();
            sim.popups.Clear();
            var S2 = sim.S;
            cam.SnapTo(new Vector3((S2.ownX0 + S2.ownX1) * 0.5f, 0, (S2.ownY0 + S2.ownY1) * 0.5f), 24f);
            yield return Shot("11d_second_site");
            ui.OpenWindow(GameUI.Window.Sites);
            yield return Shot("11e_sites_list");
            ui.OpenWindow(GameUI.Window.Staff);
            yield return Shot("11f_staff_shifts");
            ui.OpenWindow(GameUI.Window.None);
            SwitchSite(0);

            ui.OpenWindow(GameUI.Window.Settings);
            yield return Shot("12_settings");
            ui.OpenWindow(GameUI.Window.None);
            inGame = false;
            yield return Shot("13_mainmenu");
            StartNewGame(777, "Normal", "sandbox");
            sim.S.tutorial = true; sim.S.tutorialStep = 2;
            sim.popups.Clear();
            yield return Shot("14_tutorial");
            StartNewGame(4242, "Normal", "creative");
            sim.popups.Clear();
            AutoManager.BuildStarter(sim);
            ui.OpenWindow(GameUI.Window.Sandbox);
            yield return Shot("15_sandbox");
            ui.OpenWindow(GameUI.Window.Settings);
            yield return Shot("16_settings_new");
            Application.Quit();
        }

        void SetupScene()
        {
            // Camera
            var camGO = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            camGO.tag = "MainCamera";
            var c = camGO.GetComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.12f, 0.16f, 0.2f);
            c.fieldOfView = 40f;
            c.nearClipPlane = 0.3f;
            c.farClipPlane = 400f;
            c.allowMSAA = true;
            cam = camGO.GetComponent<CameraController>();
            if (cam == null) cam = camGO.AddComponent<CameraController>();

            // Lighting
            foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) Destroy(l.gameObject);
            var sunGO = new GameObject("Sun");
            sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = Settings.SunShadows;
            sun.shadowStrength = 0.55f;
            sunGO.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.25f);
            RenderSettings.fog = false;

            view = new GameObject("World").AddComponent<WorldView>();
            view.gm = this;
            ui = gameObject.AddComponent<GameUI>();
            ui.gm = this;
            audio = gameObject.AddComponent<AudioFx>();
            audio.gm = this;
        }

        // ================================================================ game lifecycle
        public void StartNewGame(int seed, string difficulty) => StartNewGame(seed, difficulty, "sandbox");

        public void StartNewGame(int seed, string difficulty, string scenario)
        {
            undoStack.Clear();
            LoadState(Simulation.NewGame(seed, difficulty, scenario));
            var sc = Simulation.Scenario(scenario);
            if (sc.id != "sandbox")
                sim.popups.Enqueue(new Popup { title = "Scenario: " + sc.name, body = sc.desc + "\n\nGoal: " + sc.goal + "\n\nTrack your progress in the Goals window [" + Settings.KeyName("Goals") + "]." }.Add("Let's go", null));
            var S = sim.S;
            cam.SnapTo(new Vector3((S.ownX0 + S.ownX1) * 0.5f, 0, (S.ownY0 + S.ownY1) * 0.5f), 26f);
            ui.showObjectives = true;
            ui.OpenWindow(GameUI.Window.None);
            speed = 1;
        }

        void LoadState(GameState s, bool keepView = false)
        {
            if (s.rooms == null || s.rooms.Length < s.mapW * s.mapH) s.rooms = new int[s.mapW * s.mapH * GameState.MaxFloors];
            if (s.obstacles == null || s.obstacles.Length != s.mapW * s.mapH) s.obstacles = new int[s.mapW * s.mapH];
            // Sanitize: racks pointing to finished contracts.
            foreach (var o in s.objects)
                if (o.contractId > 0 && !s.contracts.Any(c => c.id == o.contractId && (c.status == ContractStatus.Setup || c.status == ContractStatus.Active)))
                    o.contractId = -1;
            var oldSel = selection.Select(o => o.id).ToList();
            sim = new Simulation(s);
            sim.OnLog += (t, sev) => { ui.Toast(t, sev); audio?.OnLog(sev); };
            sim.BeforeChange += OnBeforeChange;
            if (view.sim == null) view.Init(sim); else view.ResetSim(sim);
            cam.boundsMax = new Vector2(s.mapW, s.mapH);
            selection.Clear();
            if (keepView) foreach (var id in oldSel) if (sim.objById.TryGetValue(id, out var o)) selection.Add(o);
            if (!keepView) SetTool(Tool.Select);
            inGame = true;
            hourAccum = 0;
        }

        // ================================================================ undo
        void OnBeforeChange()
        {
            if (Time.unscaledTime - lastSnapshot < 0.5f && undoStack.Count > 0) return;
            lastSnapshot = Time.unscaledTime;
            undoStack.Add(sim.S.Serialize());
            if (undoStack.Count > 30) undoStack.RemoveAt(0);
        }

        public void Undo()
        {
            if (undoStack.Count == 0) { ui.Toast("Nothing to undo.", 0); return; }
            string json = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            var s = JsonUtility.FromJson<GameState>(json);
            LoadState(s, true);
            lastSnapshot = -10f;
            ui.Toast("Undone.", 0);
        }

        public static string ThumbPath(int slot) => SavePath(slot) + ".png";

        /// <summary>Renders the world camera (without UI) into a small PNG next to the save file.</summary>
        void CaptureThumb(int slot)
        {
            try
            {
                var c = Camera.main;
                var rt = RenderTexture.GetTemporary(320, 180, 24);
                var prev = c.targetTexture;
                c.targetTexture = rt;
                c.Render();
                c.targetTexture = prev;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(320, 180, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(ThumbPath(slot), tex.EncodeToPNG());
                Destroy(tex);
                ui.InvalidateThumb(slot);
            }
            catch (System.Exception e) { Debug.LogWarning("Thumbnail failed: " + e.Message); }
        }

        // ================================================================ sites
        public void SwitchSite(int i)
        {
            if (sim == null || i == sim.S.activeSite || i < 0 || i >= sim.company.sims.Count) return;
            sim.S.floor = 0;
            sim = sim.company.SetActive(i);
            view.ResetSim(sim);
            selection.Clear();
            SetTool(Tool.Select);
            var S = sim.S;
            cam.boundsMax = new Vector2(S.mapW, S.mapH);
            cam.SnapTo(new Vector3((S.ownX0 + S.ownX1) * 0.5f, 0, (S.ownY0 + S.ownY1) * 0.5f), 26f);
            ui.Toast($"Now viewing {S.siteName} ({S.city.name}, {S.city.climate}).", 0);
        }

        public void OpenSite(CityProfile city)
        {
            var created = sim.company.OpenSite(city);
            if (created == null) { ui.Toast(sim.company.CanOpenSite(city) ?? "Could not open site", 2); return; }
            SwitchSite(sim.company.sims.IndexOf(created));
        }

        public static string SlotMeta(int slot)
        {
            try { return PlayerPrefs.GetString("slotmeta_" + slot, File.Exists(SavePath(slot)) ? "Saved game" : ""); } catch { return ""; }
        }

        void WriteMeta(int slot)
        {
            var S = sim.S;
            string meta = $"{S.city.name} - {Simulation.Scenario(S.scenario).name} - {S.DateString().Substring(0, 11)} - ${S.money:N0} - Lv {S.Level}";
            try { PlayerPrefs.SetString("slotmeta_" + slot, meta); PlayerPrefs.Save(); } catch { }
        }

        public bool Save(int slot = 1)
        {
            if (sim == null) return false;
            try
            {
                File.WriteAllText(SavePath(slot), sim.S.Serialize());
                WriteMeta(slot);
                CaptureThumb(slot);
                ui.Toast($"Game saved (slot {slot}).", 1);
                return true;
            }
            catch (System.Exception e) { ui.Toast("Save failed: " + e.Message, 3); return false; }
        }

        public bool Load(int slot = 1)
        {
            string p = SavePath(slot);
            if (!File.Exists(p)) { ui.Toast("No save found.", 2); return false; }
            try
            {
                var s = JsonUtility.FromJson<GameState>(File.ReadAllText(p));
                undoStack.Clear();
                LoadState(s);
                cam.SnapTo(new Vector3((s.ownX0 + s.ownX1) * 0.5f, 0, (s.ownY0 + s.ownY1) * 0.5f), 26f);
                ui.OpenWindow(GameUI.Window.None);
                ui.Toast("Game loaded.", 1);
                speed = 1;
                return true;
            }
            catch (System.Exception e) { ui.Toast("Load failed: " + e.Message, 3); return false; }
        }

        public static bool HasSave(int slot = 1) => File.Exists(SavePath(slot));

        // ================================================================ loop
        void Update()
        {
            cam.inputEnabled = inGame && !ui.ModalOpen && !ui.textFocus;
            cam.blockScroll = ui.IsPointerOverUI();
            if (!ui.textFocus)
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (Settings.Down("Fullscreen") || (alt && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))))
                {
                    Settings.ToggleFullscreen();
                    ui.OnDisplayChanged();
                }
            }
            if (!inGame || sim == null) return;

            bool paused = ui.ModalOpen || sim.popups.Count > 0 || sim.S.gameOver;
            if (!paused && speed > 0)
            {
                hourAccum += Time.unscaledDeltaTime * SpeedHoursPerSec[speed];
                int ticks = 0;
                while (hourAccum >= 1f && ticks < 8)
                {
                    hourAccum -= 1f;
                    sim.TickHour();
                    ticks++;
                    if (sim.popups.Count > 0) { hourAccum = 0; break; }
                }
                if (hourAccum > 8f) hourAccum = 0f;
                if (sim.AutoSaveRequested) { sim.AutoSaveRequested = false; if (Settings.Autosave) { try { File.WriteAllText(SavePath(0), sim.S.Serialize()); WriteMeta(0); CaptureThumb(0); } catch { } } }
            }

            HandleHotkeys();
            cam.targetY = sim.S.floor * GameState.FloorHeight;
            HandleWorldInput();
            selection.RemoveAll(o => !sim.objById.ContainsKey(o.id));
            view.ShowSelection(selection, tool == Tool.Connect ? new Color(1f, 0.8f, 0.2f) : new Color(0.3f, 0.85f, 1f));
            view.showGrid = tool != Tool.Select;
        }

        void HandleHotkeys()
        {
            if (ui.textFocus) return;
            if (Settings.Down("Pause")) speed = speed == 0 ? 1 : 0;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Settings.Down("Undo")) Undo();
            if (Settings.Down("Staff")) ui.ToggleWindow(GameUI.Window.Staff);
            if (Settings.Down("Goals")) ui.ToggleWindow(GameUI.Window.Goals);
            if (Settings.Down("Dashboard")) ui.ToggleWindow(GameUI.Window.Dashboard);
            if (Settings.Down("CycleOverlay")) view.CycleOverlay();
            if (Settings.Down("FloorUp")) ChangeFloor(+1);
            if (Settings.Down("FloorDown")) ChangeFloor(-1);
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) speed = 1;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) speed = 2;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) speed = 3;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) speed = 4;
            if (Settings.Down("Rotate") && tool == Tool.Place) placeRot = (placeRot + 1) % 4;
            if (Settings.Down("Equipment")) ui.ToggleBuildMenu();
            if (Settings.Down("Foundation")) SetTool(Tool.Foundation);
            if (!ctrl && (Settings.Down("Demolish") || Input.GetKeyDown(KeyCode.Delete))) { if (selection.Count > 0 && tool == Tool.Select) ui.ConfirmRemoveSelection(); else SetTool(Tool.Demolish); }
            if (!ctrl && Settings.Down("Contracts")) ui.ToggleWindow(GameUI.Window.Contracts);
            if (Settings.Down("Market")) ui.ToggleWindow(GameUI.Window.Market);
            if (Settings.Down("Research")) ui.ToggleWindow(GameUI.Window.Research);
            if (Settings.Down("HeatMap")) view.SetOverlay(view.overlay == Overlay.Heat ? Overlay.None : Overlay.Heat);
            if (Settings.Down("QuickSave")) Save(1);
            if (Settings.Down("QuickLoad")) Load(1);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (tool != Tool.Select) SetTool(Tool.Select);
                else if (ui.window != GameUI.Window.None) ui.OpenWindow(GameUI.Window.None);
                else if (selection.Count > 0) selection.Clear();
                else ui.OpenWindow(GameUI.Window.Menu);
            }
        }

        public void ChangeFloor(int delta)
        {
            int before = sim.S.floor;
            sim.SetFloor(before + delta);
            if (sim.S.floor != before)
            {
                selection.Clear();
                dragging = false;
                ui.Toast(sim.S.floor == 0 ? "Ground floor" : $"Floor {sim.S.floor + 1}" + (sim.S.floor > sim.S.TopFloor ? " (empty - build floor slabs over the storey below)" : ""), 0);
            }
            else if (delta > 0) ui.Toast("Build on the top floor first to go higher (max 4 floors).", 2);
        }

        public void SetTool(Tool t)
        {
            tool = t;
            dragging = false;
            view?.HideGhost();
            view?.HideRect();
            if (t != Tool.Place) placeDef = null;
            if (t != Tool.Connect) connectFrom = null;
        }

        public void BeginPlace(FacilityDef d)
        {
            SetTool(Tool.Place);
            placeDef = d;
        }

        public void BeginConnect(PlacedObject rack, bool secondary = false)
        {
            SetTool(Tool.Connect);
            connectFrom = rack;
            connectSecondary = secondary;
        }

        PlacedObject PickObject()
        {
            Ray r = Camera.main.ScreenPointToRay(Input.mousePosition);
            PlacedObject best = null; float bestT = float.MaxValue;
            float fy = sim.S.floor * GameState.FloorHeight;
            foreach (var o in sim.S.objects)
            {
                if (o.floor != sim.S.floor) continue;
                var d = Catalog.Facility(o.def);
                Simulation.Footprint(d, o.rot, out int w, out int h);
                float height = d.kind == FacilityKind.Rack ? Models.RackHeight(d) : d.kind == FacilityKind.Desk ? 1.1f : 1.8f;
                var b = new Bounds(new Vector3(o.x + w * 0.5f, fy + height * 0.5f, o.y + h * 0.5f), new Vector3(w * 0.92f, height, h * 0.92f));
                if (b.IntersectRay(r, out float t) && t < bestT) { bestT = t; best = o; }
            }
            return best;
        }

        void HandleWorldInput()
        {
            toolHint = null;
            bool overUI = ui.IsPointerOverUI() || ui.ModalOpen;
            hoverObject = null;
            if (overUI)
            {
                if (!dragging) { view.HideGhost(); }
                return;
            }
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) { GUIUtility.keyboardControl = 0; ui.ClearFocus(); }
            if (!cam.MouseGround(out var gp)) return;
            var tile = new Vector2Int(Mathf.FloorToInt(gp.x), Mathf.FloorToInt(gp.z));
            hoverTile = tile;
            hoverObject = PickObject();

            if (Input.GetMouseButtonDown(1))
            {
                if (tool != Tool.Select) { SetTool(Tool.Select); return; }
                selection.Clear();
            }

            switch (tool)
            {
                case Tool.Select:
                    if (Input.GetMouseButtonDown(0))
                    {
                        var o = hoverObject ?? sim.ObjectAt(tile.x, tile.y);
                        bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftControl);
                        if (o == null) { if (!add) selection.Clear(); dragging = true; dragStart = tile; }
                        else if (add) { if (selection.Contains(o)) selection.Remove(o); else selection.Add(o); }
                        else { selection.Clear(); selection.Add(o); }
                    }
                    if (dragging)
                    {
                        var rect = Simulation.Normalize(dragStart, tile);
                        if (rect.width * rect.height > 1) view.ShowRect(rect, new Color(0.3f, 0.8f, 1f, 0.15f));
                        if (Input.GetMouseButtonUp(0))
                        {
                            dragging = false;
                            view.HideRect();
                            if (rect.width * rect.height > 1)
                            {
                                // Box-select racks (or everything if no racks in the box).
                                var inBox = sim.S.objects.Where(o => o.floor == sim.S.floor && rect.Contains(new Vector2Int(o.x, o.y))).ToList();
                                var rackSel = inBox.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList();
                                foreach (var o in (rackSel.Count > 0 ? rackSel : inBox)) if (!selection.Contains(o)) selection.Add(o);
                            }
                        }
                    }
                    break;

                case Tool.Place:
                {
                    if (placeDef == null) { SetTool(Tool.Select); break; }
                    Simulation.Footprint(placeDef, placeRot, out int w, out int h);
                    int ax = tile.x - (w - 1) / 2, ay = tile.y - (h - 1) / 2;
                    string err = sim.CanPlace(placeDef, ax, ay, placeRot);
                    view.ShowGhost(placeDef, ax, ay, placeRot, err == null);
                    toolHint = err == null ? $"{placeDef.name}  ${placeDef.cost:N0}   [R] rotate  [RMB] cancel" : err;
                    if (Input.GetMouseButtonDown(0) && err == null)
                    {
                        var o = sim.Place(placeDef, ax, ay, placeRot);
                        if (o != null && placeDef.kind == FacilityKind.Rack && ui.autoConnectNewRacks) sim.AutoConnect(o);
                    }
                    break;
                }

                case Tool.Foundation:
                case Tool.Room:
                case Tool.Demolish:
                case Tool.Tray:
                case Tool.Raised:
                {
                    if (Input.GetMouseButtonDown(0)) { dragging = true; dragStart = tile; }
                    var rect = dragging ? Simulation.Normalize(dragStart, tile) : new RectInt(tile.x, tile.y, 1, 1);
                    float cost; int n; string what = null;
                    Color col;
                    if (tool == Tool.Foundation) { n = sim.BuildFoundation(rect, false, out cost); col = new Color(0.9f, 0.9f, 0.9f, 0.3f); what = $"Build {n} foundation tile(s)"; }
                    else if (tool == Tool.Room) { n = sim.PaintRoom(rect, roomType, false, out cost); col = Catalog.RoomColor(roomType); col.a = 0.45f; what = $"Paint {n} tile(s) as {Catalog.RoomName(roomType)}"; }
                    else if (tool == Tool.Tray) { n = sim.PaintFlag(rect, GameState.FlagTray, false, out cost); col = new Color(1f, 0.8f, 0.2f, 0.35f); what = $"Install {n} tile(s) of overhead cable tray"; }
                    else if (tool == Tool.Raised) { n = sim.PaintFlag(rect, GameState.FlagRaised, false, out cost); col = new Color(0.6f, 0.8f, 1f, 0.35f); what = $"Install {n} tile(s) of raised floor"; }
                    else { n = sim.Demolish(rect, false, out cost, out what); col = new Color(1f, 0.25f, 0.2f, 0.3f); }
                    view.ShowRect(rect, col);
                    toolHint = n > 0 ? $"{what}   {(cost >= 0 ? "Cost" : "Refund")}: ${Mathf.Abs(cost):N0}" : (tool == Tool.Room ? "Paint over existing building foundation" : tool == Tool.Foundation ? "Drag on owned, clear land" : tool == Tool.Tray ? "Drag over building tiles to run overhead cable trays" : tool == Tool.Raised ? "Drag over Server Hall tiles" : "Nothing to demolish here");
                    if (dragging && Input.GetMouseButtonUp(0))
                    {
                        dragging = false;
                        if (tool == Tool.Foundation) sim.BuildFoundation(rect, true, out _);
                        else if (tool == Tool.Room) sim.PaintRoom(rect, roomType, true, out _);
                        else if (tool == Tool.Tray) sim.PaintFlag(rect, GameState.FlagTray, true, out _);
                        else if (tool == Tool.Raised) sim.PaintFlag(rect, GameState.FlagRaised, true, out _);
                        else sim.Demolish(rect, true, out _, out _);
                    }
                    break;
                }

                case Tool.Connect:
                {
                    if (connectFrom == null || !sim.objById.ContainsKey(connectFrom.id)) { SetTool(Tool.Select); break; }
                    var target = hoverObject;
                    bool isCore = target != null && Catalog.Facility(target.def).kind == FacilityKind.CoreSwitch;
                    toolHint = isCore ? $"Cable to Core #{target.id}: ${sim.CableCost(connectFrom, target):N0}  ({sim.FreePorts(target)} free ports)" : "Click a core switch to run a cable";
                    if (Input.GetMouseButtonDown(0) && isCore)
                    {
                        var targets = selection.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList();
                        if (!targets.Contains(connectFrom)) targets.Add(connectFrom);
                        foreach (var rack in targets)
                        {
                            if (rack.linkCore == target.id || (connectSecondary && rack.linkCore2 == target.id)) continue;
                            string err = connectSecondary ? sim.ConnectSecondary(rack, target) : sim.Connect(rack, target);
                            if (err != null) { ui.Toast(err, 2); break; }
                        }
                        SetTool(Tool.Select);
                    }
                    break;
                }
            }
        }
    }
}
