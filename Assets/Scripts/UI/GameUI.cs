using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    public partial class GameUI : MonoBehaviour
    {
        public enum Window { None, Market, Contracts, Research, Finance, Staff, Log, Menu, Help, Goals, Dashboard, Settings, SaveLoad, Achievements, Sites }

        public GameManager gm;
        public Window window;
        public bool showObjectives = true;
        public bool autoConnectNewRacks = true;
        public bool textFocus;

        Simulation Sim => gm.sim;
        GameState S => gm.sim.S;

        float scale = 1f;
        float VW => Screen.width / scale;
        float VH => Screen.height / scale;
        readonly List<Rect> curRects = new List<Rect>();
        List<Rect> lastRects = new List<Rect>();

        bool buildMenuOpen;
        bool showAllAssign;
        int buildTab;
        bool roomMenuOpen;
        int contractsTab;
        Vector2 scrollWin, scrollInspector, scrollDevices, scrollBuild;
        string seedText = "";
        string difficulty = "Normal";
        bool newGamePanel;
        string confirmText;
        System.Action confirmAction;

        class ToastMsg { public string text; public int sev; public float time; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        // ---------------------------------------------------------------- styles
        GUIStyle traitChip, panel, panelDark, btn, btnOn, btnSmall, lbl, lblSmall, lblDim, lblBold, header, title, lblGood, lblBad, lblWarn, lblCenter, chip, tooltipStyle, field, lblWrap;
        Texture2D white;
        bool stylesReady;

        static Texture2D Tex(Color c, Color border)
        {
            var t = new Texture2D(4, 4) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, (x == 0 || y == 0 || x == 3 || y == 3) ? border : c);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            white = Texture2D.whiteTexture;
            var font = GUI.skin.font;
            panel = new GUIStyle { normal = { background = Tex(new Color(0.08f, 0.1f, 0.13f, 0.95f), new Color(0.25f, 0.32f, 0.4f, 1f)) }, border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(10, 10, 8, 8) };
            panelDark = new GUIStyle(panel) { normal = { background = Tex(new Color(0.05f, 0.06f, 0.08f, 0.9f), new Color(0.18f, 0.22f, 0.28f, 1f)) }, padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(0, 0, 3, 3) };
            btn = new GUIStyle(GUI.skin.button)
            {
                normal = { background = Tex(new Color(0.17f, 0.21f, 0.27f), new Color(0.28f, 0.35f, 0.44f)), textColor = new Color(0.9f, 0.93f, 0.97f) },
                hover = { background = Tex(new Color(0.24f, 0.3f, 0.39f), new Color(0.4f, 0.5f, 0.62f)), textColor = Color.white },
                active = { background = Tex(new Color(0.12f, 0.45f, 0.75f), new Color(0.3f, 0.6f, 0.9f)), textColor = Color.white },
                border = new RectOffset(1, 1, 1, 1), fontSize = 13, padding = new RectOffset(8, 8, 5, 5), margin = new RectOffset(2, 2, 2, 2), alignment = TextAnchor.MiddleCenter,
            };
            btnOn = new GUIStyle(btn) { normal = { background = Tex(new Color(0.12f, 0.42f, 0.7f), new Color(0.35f, 0.65f, 0.95f)), textColor = Color.white }, hover = { background = Tex(new Color(0.15f, 0.5f, 0.82f), new Color(0.4f, 0.7f, 1f)), textColor = Color.white } };
            btnSmall = new GUIStyle(btn) { fontSize = 11, padding = new RectOffset(5, 5, 2, 2) };
            lbl = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.88f, 0.9f, 0.93f) }, wordWrap = false, margin = new RectOffset(2, 2, 1, 1), padding = new RectOffset(1, 1, 1, 1) };
            lblWrap = new GUIStyle(lbl) { wordWrap = true };
            lblSmall = new GUIStyle(lbl) { fontSize = 11 };
            lblDim = new GUIStyle(lblSmall) { normal = { textColor = new Color(0.58f, 0.63f, 0.7f) }, wordWrap = true };
            lblBold = new GUIStyle(lbl) { fontStyle = FontStyle.Bold };
            header = new GUIStyle(lbl) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            title = new GUIStyle(lbl) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            lblGood = new GUIStyle(lbl) { normal = { textColor = new Color(0.4f, 0.95f, 0.5f) } };
            lblBad = new GUIStyle(lbl) { normal = { textColor = new Color(1f, 0.42f, 0.38f) }, wordWrap = true };
            lblWarn = new GUIStyle(lbl) { normal = { textColor = new Color(1f, 0.78f, 0.3f) }, wordWrap = true };
            lblCenter = new GUIStyle(lbl) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            chip = new GUIStyle(lblSmall) { normal = { background = Tex(new Color(0.5f, 0.2f, 0.15f, 0.9f), new Color(0.9f, 0.4f, 0.3f)), textColor = Color.white }, border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(6, 6, 2, 2), margin = new RectOffset(3, 3, 6, 2) };
            traitChip = new GUIStyle(chip) { margin = new RectOffset(0, 4, 2, 2), fixedHeight = 18, alignment = TextAnchor.MiddleCenter };
            tooltipStyle = new GUIStyle(lblSmall) { normal = { background = Tex(new Color(0.03f, 0.04f, 0.06f, 0.92f), new Color(0.35f, 0.45f, 0.55f)), textColor = Color.white }, border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(8, 8, 5, 5), wordWrap = true, fontSize = 12 };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(6, 6, 4, 4) };
        }

        // ---------------------------------------------------------------- public API
        public bool ModalOpen => !gm.inGame || window == Window.Menu || window == Window.Help || window == Window.Settings || window == Window.SaveLoad || window == Window.Achievements || (gm.sim != null && gm.sim.popups.Count > 0) || confirmAction != null;

        public bool IsPointerOverUI()
        {
            if (!gm.inGame) return true;
            var mp = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
            foreach (var r in lastRects) if (r.Contains(mp)) return true;
            return false;
        }

        public void Toast(string text, int sev)
        {
            toasts.Add(new ToastMsg { text = text, sev = sev, time = Time.unscaledTime });
            if (toasts.Count > 6) toasts.RemoveAt(0);
        }

        public void OpenWindow(Window w) { window = w; scrollWin = Vector2.zero; if (w != Window.None) buildMenuOpen = false; }
        public void ToggleWindow(Window w) => OpenWindow(window == w ? Window.None : w);
        public void ToggleBuildMenu() { buildMenuOpen = !buildMenuOpen; roomMenuOpen = false; if (buildMenuOpen) window = Window.None; }

        public void ConfirmRemoveSelection()
        {
            var sel = gm.selection.ToList();
            if (sel.Count == 0) return;
            float refund = sel.Sum(o => Sim.RemovalRefund(o));
            Confirm($"Remove {sel.Count} object(s)? You will be refunded ${refund:N0} (50% of the value, including installed hardware).", () =>
            {
                foreach (var o in sel) if (Sim.objById.ContainsKey(o.id)) Sim.RemoveObject(o);
                gm.selection.Clear();
            });
        }

        void Confirm(string text, System.Action a) { confirmText = text; confirmAction = a; }

        Rect Reg(Rect r) { curRects.Add(r); return r; }

        // ---------------------------------------------------------------- OnGUI
        void OnGUI()
        {
            EnsureStyles();
            scale = Mathf.Max(0.6f, Screen.height / 900f * Settings.UiScale);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            if (Event.current.type == EventType.Layout) curRects.Clear();
            textFocus = GUIUtility.keyboardControl != 0 || rebinding != null;
            HandleRebind();

            if (!gm.inGame)
            {
                DrawMainMenu();
                DrawSharedWindows();
                DrawConfirm();
            }
            else
            {
                DrawWorldLabels();
                DrawTopBar();
                DrawToolbar2();
                DrawOverlayLegend();
                if (buildMenuOpen) DrawBuildMenu();
                if (roomMenuOpen) DrawRoomMenu();
                DrawInspector();
                if (S.tutorial) DrawTutorial();
                else { gm.view.HideHint(); if (showObjectives && S.activeSite == 0) DrawObjectives(); }
                DrawToasts();
                DrawWindow();
                DrawToolHint();
                DrawPopups();
                DrawConfirm();
            }
            if (Event.current.type == EventType.Repaint) lastRects = new List<Rect>(curRects);
        }

        // ---------------------------------------------------------------- helpers
        void Bar(Rect r, float frac, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(0.03f, 0.04f, 0.05f, 0.9f);
            GUI.DrawTexture(r, white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, Mathf.Max(0, (r.width - 2) * Mathf.Clamp01(frac)), r.height - 2), white);
            GUI.color = old;
        }

        void LayoutBar(float frac, Color c, string text, float height = 16)
        {
            var r = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
            Bar(r, frac, c);
            if (!string.IsNullOrEmpty(text)) GUI.Label(r, text, new GUIStyle(lblSmall) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
        }

        void Swatch(Color c, float size = 14)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            r.y += 3;
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        bool Button(string text, bool enabled = true, params GUILayoutOption[] opts)
        {
            bool prev = GUI.enabled;
            GUI.enabled = prev && enabled;
            bool r = GUILayout.Button(text, btn, opts);
            GUI.enabled = prev;
            return r;
        }

        bool SmallButton(string text, bool enabled = true, params GUILayoutOption[] opts)
        {
            bool prev = GUI.enabled;
            GUI.enabled = prev && enabled;
            bool r = GUILayout.Button(text, btnSmall, opts);
            GUI.enabled = prev;
            return r;
        }

        static string Money(float v) => (v < 0 ? "-$" : "$") + Mathf.Abs(v).ToString("N0");

        string HoursToText(int h)
        {
            if (h < 0) h = 0;
            int d = h / 24, hh = h % 24;
            return d > 0 ? $"{d}d {hh}h" : $"{hh}h";
        }

        // ---------------------------------------------------------------- main menu
        void DrawMainMenu()
        {
            var full = Reg(new Rect(0, 0, VW, VH));
            var old = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 1f);
            GUI.DrawTexture(full, white);
            // Decorative "server lights"
            var rnd = new System.Random(7);
            for (int i = 0; i < 160; i++)
            {
                float x = (float)rnd.NextDouble() * VW, y = (float)rnd.NextDouble() * VH;
                float ph = (float)rnd.NextDouble() * 10f;
                float a = 0.15f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * (0.5f + (float)rnd.NextDouble() * 2f) + ph));
                GUI.color = (i % 5 == 0 ? new Color(1f, 0.7f, 0.2f, a) : i % 3 == 0 ? new Color(0.3f, 0.7f, 1f, a) : new Color(0.3f, 1f, 0.45f, a));
                GUI.DrawTexture(new Rect(x, y, 4, 2), white);
            }
            GUI.color = old;

            GUI.Label(new Rect(0, VH * 0.12f, VW, 60), "UPTIME", title);
            GUI.Label(new Rect(0, VH * 0.12f + 58, VW, 30), "D A T A   C E N T E R   T Y C O O N", new GUIStyle(lblCenter) { fontSize = 18, normal = { textColor = new Color(0.5f, 0.8f, 1f) } });

            if (window == Window.Settings || window == Window.SaveLoad || window == Window.Achievements) { }
            else if (!newGamePanel)
            {
                float w = 320, x = (VW - w) / 2, y = VH * 0.42f;
                GUILayout.BeginArea(new Rect(x, y, w, 300));
                if (Button("New Game", true, GUILayout.Height(42))) { newGamePanel = true; seedText = Random.Range(100000, 999999).ToString(); }
                if (Button("Load Game", GameManager.HasSave(1) || GameManager.HasSave(2) || GameManager.HasSave(3) || GameManager.HasSave(0), GUILayout.Height(42))) OpenWindow(Window.SaveLoad);
                if (Button("Achievements", true, GUILayout.Height(42))) OpenWindow(Window.Achievements);
                if (Button("Settings", true, GUILayout.Height(42))) OpenWindow(Window.Settings);
                if (Button("Quit", true, GUILayout.Height(42))) Application.Quit();
                GUILayout.EndArea();
            }
            else
            {
                float w = 600, h = 560, x = (VW - w) / 2, y = Mathf.Max(10, VH * 0.24f);
                GUILayout.BeginArea(new Rect(x, y, w, h), panel);
                GUILayout.Label("New Data Center", header);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label("World seed:", lbl, GUILayout.Width(100));
                seedText = GUILayout.TextField(seedText, 9, field, GUILayout.Width(160));
                seedText = new string(seedText.Where(char.IsDigit).ToArray());
                if (Button("Random", true, GUILayout.Width(90))) { seedText = Random.Range(100000, 999999).ToString(); GUIUtility.keyboardControl = 0; }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Difficulty:", lbl, GUILayout.Width(100));
                foreach (var d in new[] { "Easy", "Normal", "Hard" })
                    if (GUILayout.Button(d, difficulty == d ? btnOn : btn, GUILayout.Width(100))) difficulty = d;
                GUILayout.EndHorizontal();
                GUILayout.Label(difficulty == "Easy" ? "More starting cash, fewer hardware failures." : difficulty == "Hard" ? "Tight budget, low starting reputation, frequent failures." : "The intended experience.", lblDim);

                int seed = int.TryParse(seedText, out int sv) ? sv : 0;
                var city = ProcGen.GenerateCity(new System.Random(seed));
                GUILayout.Space(8);
                GUILayout.BeginVertical(panelDark);
                GUILayout.Label($"Location: {city.name}", lblBold);
                GUILayout.Label($"Climate: {city.climate}  (cooling x{city.coolingFactor:0.00})", lbl);
                GUILayout.Label($"Electricity: ${city.powerPrice:0.000}/kWh    Land price: x{city.landPrice:0.00}", lbl);
                GUILayout.Label($"Disaster risk: x{city.disasterRate:0.00}    Market demand: x{city.demandFactor:0.00}", lbl);
                GUILayout.Label(city.blurb, lblDim);
                GUILayout.EndVertical();
                GUILayout.Space(6);
                ScenarioPicker();
                if (GUILayout.Button((tutorialChoice ? "[x] " : "[ ] ") + "Guided tutorial (recommended for your first game)", tutorialChoice ? btnOn : btnSmall)) tutorialChoice = !tutorialChoice;
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (Button("Back", true, GUILayout.Height(36), GUILayout.Width(120))) newGamePanel = false;
                GUILayout.FlexibleSpace();
                if (Button("Start", true, GUILayout.Height(36), GUILayout.Width(200))) { newGamePanel = false; GUIUtility.keyboardControl = 0; gm.StartNewGame(seed, difficulty, scenarioId); S.tutorial = tutorialChoice; S.tutorialStep = 0; tutSteps = null; }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
            GUI.Label(new Rect(10, VH - 26, VW - 20, 20), "Every seed generates a different city, lot and stream of customers.", lblDim);
        }

        // ---------------------------------------------------------------- top bar
        void DrawTopBar()
        {
            var r = Reg(new Rect(0, 0, VW, 46));
            GUI.Box(r, "", panel);
            GUILayout.BeginArea(new Rect(8, 6, VW - 16, 36));
            GUILayout.BeginHorizontal();

            float daily = (Sim.hourlyIncome - Sim.hourlyCost) * 24f;
            GUILayout.Label(Money(S.money), new GUIStyle(header) { normal = { textColor = S.money >= 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.4f, 0.35f) } }, GUILayout.Width(130));
            GUILayout.Label($"{(daily >= 0 ? "+" : "")}{Money(daily)}/day", daily >= 0 ? lblGood : lblBad, GUILayout.Width(110));
            GUILayout.Label(S.DateString(), lblBold, GUILayout.Width(150));
            SiteButton();

            string[] sp = { "II", "1x", "3x", "9x", "24x" };
            for (int i = 0; i < sp.Length; i++)
                if (GUILayout.Button(sp[i], gm.speed == i ? btnOn : btn, GUILayout.Width(i == 0 ? 34 : 40), GUILayout.Height(28))) gm.speed = i;
            GUILayout.Space(12);

            int lvl = S.Level;
            int a = Catalog.XpForLevel(lvl), b = Catalog.XpForLevel(lvl + 1);
            GUILayout.Label($"Lv {lvl}", lblBold, GUILayout.Width(40));
            GUILayout.BeginVertical(GUILayout.Width(110));
            GUILayout.Space(9);
            LayoutBar(lvl >= Catalog.MaxLevel ? 1 : (S.xp - a) / (float)Mathf.Max(1, b - a), new Color(0.6f, 0.4f, 1f), lvl >= Catalog.MaxLevel ? "MAX" : $"{S.xp - a}/{b - a} XP", 16);
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.Label($"Rep {S.rating:0}", S.rating >= 60 ? lblGood : S.rating >= 35 ? lbl : lblBad, GUILayout.Width(60));

            Stat("Power", $"{Sim.loadKW:0}/{Sim.availableKW:0} kW", Sim.powerShort ? 2 : Sim.loadKW > Sim.availableKW * 0.85f ? 1 : 0, 150);
            float maxT = Sim.zones.Count > 0 ? Sim.zones.Max(z => z.temp) : 0;
            Stat("Hot", Sim.zones.Count > 0 ? $"{maxT:0.0}C" : "-", maxT > 35 ? 2 : maxT > 29 ? 1 : 0, 80);
            Stat("WAN", $"{Sim.bwDemand:0.#}/{Sim.ispGbps:0} G", Sim.netQuality < 1 ? 2 : Sim.bwDemand > Sim.ispGbps * 0.8f ? 1 : 0, 120);
            Stat("Ports", $"{Sim.corePortsUsed}/{Sim.corePorts}", Sim.corePorts > 0 && Sim.corePortsUsed >= Sim.corePorts ? 1 : 0, 90);
            Stat("Staff", $"{Sim.WorkingStaffCount}", S.staff.Count > Sim.deskCount ? 1 : 0, 64);
            Stat(S.weather, Sim.solarCapKW > 0 ? $"sun {Sim.solarKW:0}kW" : "", S.weather == "Storm" ? 2 : 0, 110);
            Stat("$/kWh", $"{Sim.PowerPriceNow + S.city.carbonTax:0.000}", Sim.TimeOfDayPriceMul > 1f ? 1 : 0, 96);

            foreach (var e in S.events)
                GUILayout.Label($"{e.label} {HoursToText(e.endHour - S.hour)}", chip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Stat(string name, string value, int level, float width)
        {
            var st = level == 2 ? lblBad : level == 1 ? lblWarn : lbl;
            GUILayout.BeginHorizontal(GUILayout.Width(width));
            GUILayout.Label(name, lblDim, GUILayout.ExpandWidth(false));
            GUILayout.Label(value, new GUIStyle(st) { wordWrap = false });
            GUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- toolbar
        void DrawToolbar()
        {
            var r = Reg(new Rect(6, 52, 168, 560));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("BUILD", lblDim);
            if (GUILayout.Button("Select  [Esc]", gm.tool == Tool.Select ? btnOn : btn)) { gm.SetTool(Tool.Select); buildMenuOpen = roomMenuOpen = false; }
            if (GUILayout.Button("Foundation  [F]", gm.tool == Tool.Foundation ? btnOn : btn)) { gm.SetTool(Tool.Foundation); buildMenuOpen = roomMenuOpen = false; }
            if (GUILayout.Button("Rooms", gm.tool == Tool.Room || gm.tool == Tool.Tray || gm.tool == Tool.Raised || roomMenuOpen ? btnOn : btn)) { roomMenuOpen = !roomMenuOpen; buildMenuOpen = false; if (roomMenuOpen) { gm.SetTool(Tool.Room); window = Window.None; } }
            if (GUILayout.Button("Equipment  [B]", buildMenuOpen || gm.tool == Tool.Place ? btnOn : btn)) ToggleBuildMenu();
            if (GUILayout.Button("Demolish  [X]", gm.tool == Tool.Demolish ? btnOn : btn)) { gm.SetTool(Tool.Demolish); buildMenuOpen = roomMenuOpen = false; }
            GUILayout.Space(8);
            GUILayout.Label("MANAGE", lblDim);
            if (GUILayout.Button($"Market ({S.offers.Count})  [M]", window == Window.Market ? btnOn : btn)) ToggleWindow(Window.Market);
            int needs = S.contracts.Count(c => (c.status == ContractStatus.Setup) || (c.status == ContractStatus.Active && !c.meeting));
            if (GUILayout.Button(needs > 0 ? $"Contracts (!{needs})  [C]" : "Contracts  [C]", window == Window.Contracts ? btnOn : btn)) ToggleWindow(Window.Contracts);
            if (GUILayout.Button("Research  [T]", window == Window.Research ? btnOn : btn)) ToggleWindow(Window.Research);
            if (GUILayout.Button("Finance", window == Window.Finance ? btnOn : btn)) ToggleWindow(Window.Finance);
            if (GUILayout.Button("Staff & Land", window == Window.Staff ? btnOn : btn)) ToggleWindow(Window.Staff);
            if (GUILayout.Button("Event Log", window == Window.Log ? btnOn : btn)) ToggleWindow(Window.Log);
            GUILayout.Space(8);
            GUILayout.Label("VIEW", lblDim);
            if (GUILayout.Button("Heat map [H]", gm.view.heatOverlay ? btnOn : btn)) gm.view.heatOverlay = !gm.view.heatOverlay;
            if (GUILayout.Button("Network cables", gm.view.showCables ? btnOn : btn)) gm.view.showCables = !gm.view.showCables;
            if (GUILayout.Button("Auto-cable new racks", autoConnectNewRacks ? btnOn : btn)) autoConnectNewRacks = !autoConnectNewRacks;
            if (GUILayout.Button("Objectives", showObjectives ? btnOn : btn)) showObjectives = !showObjectives;
            GUILayout.FlexibleSpace();
            if (Button("Menu")) OpenWindow(Window.Menu);
            GUILayout.EndArea();
        }

        void DrawRoomMenu()
        {
            var r = Reg(new Rect(180, 52, 250, 420));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("Paint Rooms", header);
            GUILayout.Label("Drag over building foundation.", lblDim);
            foreach (var t in new[] { RoomType.ServerHall, RoomType.Network, RoomType.Power, RoomType.Office, RoomType.Corridor })
            {
                GUILayout.BeginHorizontal();
                Swatch(Catalog.RoomColor(t), 16);
                if (GUILayout.Button(Catalog.RoomName(t), gm.tool == Tool.Room && gm.roomType == t ? btnOn : btn)) { gm.SetTool(Tool.Room); gm.roomType = t; }
                GUILayout.EndHorizontal();
            }
            string desc = gm.roomType switch
            {
                RoomType.ServerHall => "Racks and cooling go here. Each connected hall is one cooling zone.",
                RoomType.Network => "Core switches and ISP uplinks.",
                RoomType.Power => "Switchgear and UPS batteries.",
                RoomType.Office => "Technician desks.",
                _ => "Plain corridor (free).",
            };
            if (gm.tool == Tool.Room) GUILayout.Label(desc, lblDim);
            InfraTools();
            GUILayout.EndArea();
        }

        static readonly string[] BuildTabs = { "Racks", "Cooling", "Network", "Power", "Office" };

        bool InTab(FacilityDef d, int tab)
        {
            switch (tab)
            {
                case 0: return d.kind == FacilityKind.Rack;
                case 1: return d.kind == FacilityKind.Cooling;
                case 2: return d.kind == FacilityKind.CoreSwitch || d.kind == FacilityKind.Isp;
                case 3: return d.kind == FacilityKind.Transformer || d.kind == FacilityKind.Switchgear || d.kind == FacilityKind.Ups || d.kind == FacilityKind.Generator;
                default: return d.kind == FacilityKind.Desk;
            }
        }

        void DrawBuildMenu()
        {
            var r = Reg(new Rect(180, 52, 340, 560));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < BuildTabs.Length; i++)
                if (GUILayout.Button(BuildTabs[i], buildTab == i ? btnOn : btnSmall)) buildTab = i;
            GUILayout.EndHorizontal();
            scrollBuild = GUILayout.BeginScrollView(scrollBuild);
            foreach (var d in Catalog.Facilities.Where(f => InTab(f, buildTab)))
            {
                bool unlocked = Sim.IsUnlocked(d.id);
                GUILayout.BeginVertical(panelDark);
                GUILayout.BeginHorizontal();
                GUILayout.Label(d.name, lblBold);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Money(d.cost), S.money >= d.cost ? lblGood : lblBad);
                GUILayout.EndHorizontal();
                GUILayout.Label(d.desc, lblDim);
                GUILayout.Label($"Place in: {Catalog.RoomName(d.room)}   Size {d.w}x{d.h}" + (d.monthly > 0 ? $"   {Money(d.monthly)}/mo" : ""), lblDim);
                if (unlocked)
                {
                    if (GUILayout.Button(gm.tool == Tool.Place && gm.placeDef == d ? "Placing... (RMB to stop)" : "Place", gm.tool == Tool.Place && gm.placeDef == d ? btnOn : btn))
                        gm.BeginPlace(d);
                }
                else
                {
                    GUILayout.Label(S.Level >= d.level ? $"Research for {Money(d.research)} in the Research tab" : $"Requires reputation level {d.level}", lblWarn);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- world labels & hints
        void DrawWorldLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var st = new GUIStyle(lblSmall) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            foreach (var z in Sim.zones)
            {
                if (z.tiles.Count < 2 || z.floor != S.floor) continue;
                var wp = new Vector3(z.center.x + 0.5f, 0.1f + S.floor * GameState.FloorHeight, z.center.y + 0.5f);
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0) continue;
                var p = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
                string txt = $"{z.temp:0.0}C   {z.heatKW:0}/{z.coolKW:0} kW";
                var size = st.CalcSize(new GUIContent(txt));
                var rr = new Rect(p.x - size.x / 2 - 6, p.y - 10, size.x + 12, 20);
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.55f);
                GUI.DrawTexture(rr, white);
                GUI.color = WorldView.HeatColor(z.temp);
                GUI.DrawTexture(new Rect(rr.x, rr.y, 3, rr.height), white);
                GUI.color = old;
                GUI.Label(rr, txt, st);
            }
        }

        void DrawToolHint()
        {
            string text = gm.toolHint;
            if (text == null && gm.tool == Tool.Select && gm.hoverObject != null && !IsPointerOverUI())
            {
                var o = gm.hoverObject;
                var d = Catalog.Facility(o.def);
                text = $"{d.name} #{o.id}";
                if (d.kind == FacilityKind.Rack && Sim.racks.TryGetValue(o.id, out var info))
                {
                    text += info.online ? "  - Online" : info.empty ? "  - Empty" : $"  - {info.issue}";
                    var c = o.contractId > 0 ? Sim.FindContract(o.contractId) : null;
                    if (c != null) text += $"\n{c.customer}";
                    text += $"\n{info.loadKW:0.0} kW, {info.usedU}/{d.rackU}U, {info.activeServers} servers";
                }
                else if (o.failed) text += "  - BROKEN";
            }
            if (text == null) return;
            var mp = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
            var size = tooltipStyle.CalcSize(new GUIContent(text));
            size.x = Mathf.Min(size.x + 16, 440);
            float h = tooltipStyle.CalcHeight(new GUIContent(text), size.x);
            var r = new Rect(mp.x + 18, mp.y + 18, size.x, h);
            if (r.xMax > VW) r.x = mp.x - size.x - 10;
            if (r.yMax > VH) r.y = mp.y - h - 10;
            GUI.Label(r, text, tooltipStyle);
        }

        void DrawToasts()
        {
            float now = Time.unscaledTime;
            toasts.RemoveAll(t => now - t.time > 7f);
            float w = 520, x = (VW - w) / 2, y = VH - 12;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                var content = new GUIContent(t.text);
                float h = tooltipStyle.CalcHeight(content, w);
                y -= h + 4;
                float a = Mathf.Clamp01(7f - (now - t.time));
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                var r = new Rect(x, y, w, h);
                GUI.Label(r, t.text, tooltipStyle);
                GUI.color = t.sev == 3 ? new Color(1f, 0.3f, 0.25f, a) : t.sev == 2 ? new Color(1f, 0.75f, 0.2f, a) : t.sev == 1 ? new Color(0.3f, 1f, 0.45f, a) : new Color(0.4f, 0.7f, 1f, a);
                GUI.DrawTexture(new Rect(x, y, 4, h), white);
                GUI.color = old;
            }
        }

        // ---------------------------------------------------------------- objectives
        void DrawObjectives()
        {
            var steps = new List<(string, bool)>();
            var keys = new List<string> { "Foundation", "Rooms", "Equipment", "Equipment", "Equipment", null, "Market", "Contracts", "Staff" };
            bool Has(FacilityKind k) => S.objects.Any(o => Catalog.Facility(o.def).kind == k);
            bool HasRoom(RoomType t) => S.rooms.Any(v => v == (int)t);
            steps.Add(("Build a foundation [F] (drag on clear land)", S.rooms.Any(v => v != 0)));
            steps.Add(("Paint a Server Hall, Network Room and Power Room", HasRoom(RoomType.ServerHall) && HasRoom(RoomType.Network) && HasRoom(RoomType.Power)));
            steps.Add(("Place a Utility Transformer outdoors and Switchgear in the Power Room", Has(FacilityKind.Transformer) && Has(FacilityKind.Switchgear)));
            steps.Add(("Place a Core Switch and ISP Fiber in the Network Room", Has(FacilityKind.CoreSwitch) && Has(FacilityKind.Isp)));
            steps.Add(("Place a Rack and a CRAC unit in the Server Hall", Has(FacilityKind.Rack) && Has(FacilityKind.Cooling)));
            steps.Add(("Select the rack: add a ToR switch + servers, cable it to the core", Sim.racks.Values.Any(i => i.online)));
            steps.Add(("Accept a contract in the Market [M] and assign racks to it", S.contracts.Any() && S.objects.Any(o => o.contractId > 0)));
            steps.Add(("Bring a contract online", S.contracts.Any(c => c.status == ContractStatus.Active || c.status == ContractStatus.Completed)));
            steps.Add(("Build an Office with desks, then hire staff (Staff & Land)", S.staff.Count > 0));
            if (steps.All(s => s.Item2)) { steps.Clear(); steps.Add(("All set! Grow your reputation and research new hardware.", true)); }

            float h = 34 + steps.Count * 21;
            var r = Reg(new Rect(6, 708, 330, h));
            if (r.yMax > VH - 6) r.y = VH - 6 - h;
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Getting started", lblBold);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", btnSmall)) showObjectives = false;
            GUILayout.EndHorizontal();
            bool firstOpen = true;
            tutorialHighlight = null;
            for (int si = 0; si < steps.Count; si++)
            {
                var (text, done) = steps[si];
                if (!done && firstOpen && si < keys.Count) tutorialHighlight = keys[si];
                var st = done ? lblDim : firstOpen ? lblWarn : lblSmall;
                if (!done) firstOpen = false;
                GUILayout.Label((done ? "[x] " : "[ ] ") + text, new GUIStyle(st) { wordWrap = false, fontSize = 11 });
            }
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- inspector
        void DrawInspector()
        {
            var sel = gm.selection;
            if (sel.Count == 0) return;
            float w = 400;
            var r = Reg(new Rect(VW - w - 6, 52, w, VH - 58));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            if (sel.Count == 1)
            {
                var d = Catalog.Facility(sel[0].def);
                GUILayout.Label($"{d.name}  #{sel[0].id}", header);
            }
            else GUILayout.Label($"{sel.Count} objects selected", header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", btnSmall)) { gm.selection.Clear(); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            GUILayout.EndHorizontal();

            scrollInspector = GUILayout.BeginScrollView(scrollInspector);
            if (sel.Count == 1)
            {
                var o = sel[0];
                var d = Catalog.Facility(o.def);
                if (d.kind == FacilityKind.Rack) RackInspector(o, d);
                else FacilityInspector(o, d);
            }
            else MultiInspector(sel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void FacilityInspector(PlacedObject o, FacilityDef d)
        {
            GUILayout.Label(d.desc, lblDim);
            GUILayout.Space(4);
            if (o.failed)
            {
                GUILayout.Label("BROKEN - " + (Sim.EffectiveTechs > 0 ? "staff will walk over and repair it" : "no staff available"), lblBad);
                float cost = Sim.ManualRepairCost(o, null);
                if (Button($"Emergency repair now ({Money(cost)})", S.money >= cost)) Sim.ManualRepair(o, null);
            }
            else GUILayout.Label("Operational", lblGood);
            GUILayout.Space(6);
            switch (d.kind)
            {
                case FacilityKind.Cooling:
                {
                    int zi = Sim.ZoneAt(o);
                    GUILayout.Label($"Cooling capacity: {d.coolingKW:0} kW (x{S.city.coolingFactor:0.00} climate)", lbl);
                    GUILayout.Label($"Power draw: {d.watts / 1000f:0.0} kW", lbl);
                    if (zi >= 0)
                    {
                        var z = Sim.zones[zi];
                        GUILayout.Label($"Hall temperature: {z.temp:0.0} C", z.temp > 32 ? lblBad : z.temp > 28 ? lblWarn : lblGood);
                        GUILayout.Label($"Hall heat load: {z.heatKW:0.0} / {z.coolKW:0.0} kW cooling", lbl);
                        LayoutBar(z.coolKW > 0 ? z.heatKW / z.coolKW : 1, z.heatKW > z.coolKW ? new Color(1, 0.3f, 0.2f) : new Color(0.3f, 0.7f, 1f), null, 10);
                    }
                    break;
                }
                case FacilityKind.CoreSwitch:
                {
                    int used = Sim.coreUsage.TryGetValue(o.id, out int u) ? u : 0;
                    GUILayout.Label($"Rack ports: {used} / {d.ports} used at {d.portGbps:0}G", lbl);
                    LayoutBar(used / (float)d.ports, new Color(0.3f, 0.6f, 1f), null, 10);
                    if (S.Room(o.x, o.y) != RoomType.Network) GUILayout.Label("Not in a Network Room - racks cabled here are offline!", lblBad);
                    if (Button("Auto-cable all uncabled racks here"))
                    {
                        foreach (var rack in Sim.Racks.Where(x => x.linkCore < 0).ToList())
                        {
                            if (Sim.FreePorts(o) <= 0) break;
                            string err = Sim.Connect(rack, o);
                            if (err != null) { Toast(err, 2); break; }
                        }
                    }
                    break;
                }
                case FacilityKind.Isp:
                    GUILayout.Label($"Bandwidth: {d.bandwidth:0} Gbps   Fee: {Money(d.monthly)}/month", lbl);
                    GUILayout.Label($"Total WAN: {Sim.ispGbps:0} Gbps, demand {Sim.bwDemand:0.0} Gbps", lbl);
                    break;
                case FacilityKind.Transformer:
                case FacilityKind.Switchgear:
                    GUILayout.Label($"Capacity: {d.supplyKW:0} kW", lbl);
                    GUILayout.Label($"Grid transformers: {Sim.transformerKW:0} kW   Switchgear: {Sim.switchgearKW:0} kW", lbl);
                    GUILayout.Label($"Usable supply: {Sim.supplyKW:0} kW   Load: {Sim.loadKW:0.0} kW", lbl);
                    break;
                case FacilityKind.Ups:
                case FacilityKind.Generator:
                    GUILayout.Label($"Backup capacity: {d.backupKW:0} kW", lbl);
                    GUILayout.Label($"Total UPS: {Sim.upsKW:0} kW   Generators: {Sim.genKW:0} kW   Load: {Sim.loadKW:0.0} kW", lbl);
                    GUILayout.Label("Short outages need UPS >= load. Long outages need both UPS and generators >= load.", lblDim);
                    break;
                case FacilityKind.Desk:
                    GUILayout.Label($"Desks: {Sim.deskCount}   Technicians: {S.technicians}", lbl);
                    if (Button("Open Staff & Land")) OpenWindow(Window.Staff);
                    break;
            }
            FacilityExtras(o, d);
            GUILayout.Space(10);
            if (Button($"Remove (refund {Money(Sim.RemovalRefund(o))})")) ConfirmRemoveSelection();
        }

        void RackInspector(PlacedObject o, FacilityDef d)
        {
            Sim.racks.TryGetValue(o.id, out var info);
            if (info == null) return;
            if (info.online) GUILayout.Label("ONLINE", lblGood);
            else GUILayout.Label(info.empty ? "Empty - install a ToR switch and servers" : "OFFLINE: " + info.issue, info.empty ? lblWarn : lblBad);
            if (info.online && info.servers > info.ports) GUILayout.Label($"Only {info.ports} switch ports for {info.servers} servers - add another ToR switch.", lblWarn);
            if (info.failedDevices > 0) GUILayout.Label($"{info.failedDevices} failed device(s)", lblWarn);

            GUILayout.Space(4);
            GUILayout.Label($"Power: {info.loadKW:0.00} / {d.rackKW:0} kW", lbl);
            LayoutBar(info.loadKW / d.rackKW, info.loadKW > d.rackKW ? new Color(1, 0.3f, 0.2f) : new Color(1f, 0.8f, 0.3f), null, 8);
            GUILayout.Label($"Space: {info.usedU} / {d.rackU} U", lbl);
            LayoutBar(info.usedU / (float)d.rackU, new Color(0.3f, 0.6f, 1f), null, 8);
            GUILayout.Label($"Servers: {info.activeServers} active / {info.servers}   Ports: {info.ports}", lbl);
            GUILayout.Label($"Cores: {info.cores}   Storage: {info.storage:0} TB   GPUs: {info.gpus}", lbl);
            if (info.zone >= 0) GUILayout.Label($"Hall temp: {Sim.zones[info.zone].temp:0.0} C", Sim.zones[info.zone].temp > 32 ? lblBad : lbl);
            RackExtras(o, info);

            // Network
            GUILayout.Space(8);
            GUILayout.Label("NETWORK", lblDim);
            PlacedObject core = o.linkCore > 0 && Sim.objById.TryGetValue(o.linkCore, out var cc) ? cc : null;
            if (core != null)
            {
                GUILayout.Label($"Cabled to {Catalog.Facility(core.def).name} #{core.id} @ {info.linkGbps:0} Gbps", lbl);
                GUILayout.BeginHorizontal();
                if (SmallButton("Re-cable...")) gm.BeginConnect(o);
                if (SmallButton("Disconnect")) Sim.Disconnect(o);
                GUILayout.EndHorizontal();
            }
            else
            {
                var best = Sim.BestCore(o);
                GUILayout.BeginHorizontal();
                if (SmallButton(best != null ? $"Auto-cable ({Money(Sim.CableCost(o, best))})" : "Auto-cable (no core)", best != null)) { var e = Sim.AutoConnect(o); if (e != null) Toast(e, 2); }
                if (SmallButton("Pick core switch...")) gm.BeginConnect(o);
                GUILayout.EndHorizontal();
            }

            // Contract
            GUILayout.Space(8);
            GUILayout.Label("CUSTOMER", lblDim);
            var con = o.contractId > 0 ? Sim.FindContract(o.contractId) : null;
            if (con != null)
            {
                GUILayout.BeginHorizontal();
                Swatch(Catalog.CustomerColors[con.colorIdx]);
                GUILayout.Label($"{con.customer} ({Catalog.TypeName(con.type)})", lblBold);
                GUILayout.EndHorizontal();
                if (con.type == ContractType.Colocation) GUILayout.Label($"Customer gear: {con.reqKWPerRack:0} kW in this rack (they bring their own servers)", lblDim);
                GUILayout.BeginHorizontal();
                if (SmallButton("Unassign")) Sim.Assign(o, null);
                if (SmallButton("View contract")) { OpenWindow(Window.Contracts); gm.view.highlightContract = con.id; }
                GUILayout.EndHorizontal();
            }
            else
            {
                AssignButtons(new List<PlacedObject> { o });
            }

            // Devices
            GUILayout.Space(8);
            GUILayout.Label("INSTALLED HARDWARE", lblDim);
            if (o.devices.Count == 0) GUILayout.Label("Nothing installed.", lblDim);
            foreach (var dev in o.devices.OrderByDescending(x => x.u).ToList())
            {
                var dd = Catalog.Device(dev.def);
                GUILayout.BeginHorizontal(panelDark);
                Swatch(dev.failed ? Color.red : dd.color, 10);
                GUILayout.Label($"U{dev.u + 1:00}  {dd.name}", dev.failed ? lblBad : lblSmall, GUILayout.Width(170));
                DeviceRowExtras(o, dev);
                GUILayout.FlexibleSpace();
                if (dev.failed)
                {
                    float rc = Sim.ManualRepairCost(o, dev);
                    if (SmallButton($"Fix {Money(rc)}", S.money >= rc)) Sim.ManualRepair(o, dev);
                }
                if (SmallButton("Sell")) Sim.RemoveDevice(o, dev);
                GUILayout.EndHorizontal();
            }

            TemplatesSection(new List<PlacedObject> { o });
            GUILayout.Space(8);
            GUILayout.Label("INSTALL HARDWARE", lblDim);
            DeviceShop(new List<PlacedObject> { o });

            GUILayout.Space(10);
            if (Button($"Remove rack (refund {Money(Sim.RemovalRefund(o))})")) ConfirmRemoveSelection();
        }

        void AssignButtons(List<PlacedObject> racks)
        {
            var open = S.contracts.Where(c => c.status == ContractStatus.Setup || c.status == ContractStatus.Active).ToList();
            if (open.Count == 0) { GUILayout.Label("No contracts yet - accept one in the Market [M].", lblDim); return; }
            GUILayout.Label("Assign to contract:", lblSmall);
            // Show contracts still being set up or short on capacity first; collapse the rest.
            open = open.OrderBy(c => c.status == ContractStatus.Setup ? 0 : !c.meeting ? 1 : 2).ToList();
            int shown = 0;
            foreach (var c in open)
            {
                if (!showAllAssign && shown >= 5) { if (SmallButton($"Show all {open.Count} contracts...")) showAllAssign = true; break; }
                shown++;
                GUILayout.BeginHorizontal();
                Swatch(Catalog.CustomerColors[c.colorIdx]);
                int count = Sim.Racks.Count(x => x.contractId == c.id);
                if (SmallButton($"{c.customer} - {Catalog.TypeName(c.type)} ({count} racks)"))
                {
                    foreach (var rck in racks)
                    {
                        string e = Sim.Assign(rck, c);
                        if (e != null) { Toast(e, 2); break; }
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        void DeviceShop(List<PlacedObject> racks)
        {
            bool multi = racks.Count > 1;
            foreach (var dd in Catalog.Devices)
            {
                if (!Sim.IsUnlocked(dd.id)) continue;
                GUILayout.BeginVertical(panelDark);
                GUILayout.BeginHorizontal();
                Swatch(dd.color, 10);
                GUILayout.Label(dd.name, lblBold);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Money(dd.cost), S.money >= dd.cost ? lblGood : lblBad);
                GUILayout.EndHorizontal();
                string spec = dd.kind == DeviceKind.Switch
                    ? $"{dd.heightU}U  {dd.ports} ports  {dd.uplinkGbps:0}G uplink  {dd.watts:0}W"
                    : $"{dd.heightU}U  {dd.cores} cores  {dd.storageTB:0}TB" + (dd.gpus > 0 ? $"  {dd.gpus} GPU" : "") + $"  {dd.watts:0}W";
                GUILayout.BeginHorizontal();
                GUILayout.Label(spec, lblDim);
                GUILayout.FlexibleSpace();
                string err = racks.Count > 0 ? Sim.CanAddDevice(racks[0], dd) : null;
                bool any = racks.Any(r => Sim.CanAddDevice(r, dd) == null);
                if (SmallButton(multi ? "+1 each" : "+1", any))
                    foreach (var r in racks) Sim.AddDevice(r, dd);
                if (dd.kind == DeviceKind.Server && SmallButton(multi ? "Fill all" : "Fill", any))
                {
                    int n = 0;
                    foreach (var r in racks)
                    {
                        // Leave room for power: stop before exceeding the PDU.
                        var rd = Catalog.Facility(r.def);
                        Sim.racks.TryGetValue(r.id, out var info);
                        float load = info?.loadKW ?? 0;
                        int byPower = Mathf.FloorToInt((rd.rackKW - load) / (dd.watts / 1000f));
                        n += Sim.FillRack(r, dd, Mathf.Max(0, byPower));
                    }
                    Toast($"Installed {n} x {dd.name}.", 1);
                }
                GUILayout.EndHorizontal();
                if (!any && err != null) GUILayout.Label(err, new GUIStyle(lblDim) { normal = { textColor = new Color(0.9f, 0.55f, 0.45f) } });
                GUILayout.EndVertical();
            }
        }

        void MultiInspector(List<PlacedObject> sel)
        {
            var racks = sel.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList();
            if (racks.Count == 0)
            {
                GUILayout.Label("Select racks to manage them in bulk.", lblDim);
                if (Button("Remove selected")) ConfirmRemoveSelection();
                return;
            }
            int online = racks.Count(r => Sim.racks.TryGetValue(r.id, out var i) && i.online);
            GUILayout.Label($"{racks.Count} racks, {online} online", online == racks.Count ? lblGood : lblWarn);
            GUILayout.Label($"Total load: {racks.Sum(r => Sim.racks.TryGetValue(r.id, out var i) ? i.loadKW : 0):0.0} kW", lbl);
            GUILayout.Label($"Cores: {racks.Sum(r => Sim.racks[r.id].cores)}   Storage: {racks.Sum(r => Sim.racks[r.id].storage):0} TB   GPUs: {racks.Sum(r => Sim.racks[r.id].gpus)}", lbl);
            var issues = racks.Select(r => Sim.racks[r.id].issue).Where(x => x != null).GroupBy(x => x).Select(g => $"{g.Count()}x {g.Key}");
            foreach (var i in issues) GUILayout.Label(i, lblWarn);

            GUILayout.Space(6);
            GUILayout.Label("NETWORK", lblDim);
            GUILayout.BeginHorizontal();
            if (SmallButton("Auto-cable uncabled"))
                foreach (var r in racks.Where(x => x.linkCore < 0)) { var e = Sim.AutoConnect(r); if (e != null) { Toast(e, 2); break; } }
            if (SmallButton("Cable all to core...")) gm.BeginConnect(racks[0]);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("CUSTOMER", lblDim);
            AssignButtons(racks);
            if (SmallButton("Unassign all")) foreach (var r in racks) Sim.Assign(r, null);

            GUILayout.Space(6);
            GUILayout.Label("CLONE", lblDim);
            var template = racks.FirstOrDefault(r => r.devices.Count > 0);
            if (template != null)
            {
                float cost = racks.Where(r => r.devices.Count == 0).Sum(r => template.devices.Sum(dv => Catalog.Device(dv.def).cost));
                int empties = racks.Count(r => r.devices.Count == 0);
                if (SmallButton($"Copy rack #{template.id}'s hardware into {empties} empty rack(s) ({Money(cost)})", empties > 0 && S.money >= cost))
                {
                    foreach (var r in racks.Where(x => x.devices.Count == 0))
                        foreach (var dv in template.devices.OrderBy(x => x.u))
                        {
                            var dd = Catalog.Device(dv.def);
                            if (Sim.CanAddDevice(r, dd) == null) Sim.AddDevice(r, dd);
                        }
                }
            }
            GUILayout.Space(6);
            TemplatesSection(racks);
            GUILayout.BeginHorizontal();
            if (SmallButton("Make all redundant")) foreach (var r in racks) { var e = Sim.MakeRedundant(r); if (e != null) { Toast(e, 2); break; } }
            if (SmallButton("Rotate all")) foreach (var r in racks) { Sim.RotateRack(r); gm.view.ForceRebuildObject(r.id); }
            GUILayout.EndHorizontal();
            GUILayout.Label("INSTALL IN EACH RACK", lblDim);
            DeviceShop(racks);
            GUILayout.Space(8);
            if (Button("Remove selected")) ConfirmRemoveSelection();
        }

        // ---------------------------------------------------------------- windows
        void DrawWindow()
        {
            if (window == Window.None) return;
            if (window == Window.Menu) { DrawMenu(); return; }
            if (window == Window.Help) { DrawHelp(); return; }
            if (DrawSharedWindows()) return;
            float w = Mathf.Min(860, VW - 600), h = Mathf.Min(640, VH - 80);
            w = Mathf.Max(w, 640);
            var r = Reg(new Rect(182, 52, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(window switch
            {
                Window.Market => "Contract Market",
                Window.Contracts => "Your Contracts",
                Window.Research => "Research & Unlocks",
                Window.Finance => "Finance",
                Window.Staff => "Staff & Land",
                Window.Goals => "Goals & Achievements",
                Window.Dashboard => "Dashboard",
                Window.Sites => "Sites",
                Window.Log => "Event Log",
                _ => ""
            }, header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", btnSmall)) { OpenWindow(Window.None); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            GUILayout.EndHorizontal();
            switch (window)
            {
                case Window.Market: MarketWindow(); break;
                case Window.Contracts: ContractsWindow(); break;
                case Window.Research: ResearchWindow(); break;
                case Window.Finance: FinanceWindow(); break;
                case Window.Staff: StaffWindow2(); break;
                case Window.Goals: GoalsWindow(); break;
                case Window.Dashboard: DashboardWindow(); break;
                case Window.Sites: SitesWindow(); break;
                case Window.Log: LogWindow(); break;
            }
            GUILayout.EndArea();
        }

        string Requirement(Contract c)
        {
            return ProcGen.Describe(c) + $"  +  {c.reqBandwidth:0.#} Gbps";
        }

        string Estimate(Contract c)
        {
            var devs = Catalog.Devices.Where(d => d.kind == DeviceKind.Server && Sim.IsUnlocked(d.id)).ToList();
            switch (c.type)
            {
                case ContractType.Colocation:
                {
                    var rack = Catalog.Facilities.Where(f => f.kind == FacilityKind.Rack && Sim.IsUnlocked(f.id) && f.rackKW >= c.reqKWPerRack).OrderBy(f => f.cost).FirstOrDefault();
                    if (rack == null) return $"!Needs racks rated for {c.reqKWPerRack:0} kW - research a denser rack first.";
                    return $"Needs {c.reqRacks} x {rack.name} with a ToR switch. Power: {c.reqRacks * c.reqKWPerRack:0} kW.";
                }
                case ContractType.Dedicated:
                {
                    var best = devs.Where(d => d.cores >= c.reqCoresPerServer).OrderBy(d => d.cost).FirstOrDefault();
                    if (best == null) return $"!No researched server has {c.reqCoresPerServer}+ cores.";
                    return $"~{c.reqServers} x {best.name} ({Money(best.cost * c.reqServers)}), {c.reqServers * best.watts / 1000f:0.0} kW";
                }
                case ContractType.Compute:
                {
                    var best = devs.Where(d => d.cores > 0).OrderBy(d => d.cost / d.cores).First();
                    int n = Mathf.CeilToInt(c.reqCores / (float)best.cores);
                    return $"~{n} x {best.name} ({Money(best.cost * n)}), {n * best.watts / 1000f:0.0} kW";
                }
                case ContractType.Storage:
                {
                    var best = devs.Where(d => d.storageTB > 0).OrderBy(d => d.cost / d.storageTB).First();
                    int n = Mathf.CeilToInt(c.reqStorageTB / best.storageTB);
                    return $"~{n} x {best.name} ({Money(best.cost * n)}), {n * best.watts / 1000f:0.0} kW";
                }
                case ContractType.GpuCloud:
                {
                    var best = devs.Where(d => d.gpus > 0).OrderBy(d => d.cost / d.gpus).FirstOrDefault();
                    if (best == null) return "!Research GPU servers first.";
                    int n = Mathf.CeilToInt(c.reqGpus / (float)best.gpus);
                    return $"~{n} x {best.name} ({Money(best.cost * n)}), {n * best.watts / 1000f:0.0} kW";
                }
            }
            return "";
        }

        void ContractHeader(Contract c)
        {
            GUILayout.BeginHorizontal();
            Swatch(Catalog.CustomerColors[c.colorIdx], 16);
            GUILayout.Label(c.customer, header);
            GUILayout.Label($"  {c.industry} - {Catalog.TypeName(c.type)}", lblDim);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Money(c.monthlyPay)}/mo", new GUIStyle(lblGood) { fontSize = 16, fontStyle = FontStyle.Bold });
            GUILayout.EndHorizontal();
        }

        void Traits(Contract c)
        {
            if (c.traits.Count == 0) return;
            foreach (var t in c.traits)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label(t, traitChip, GUILayout.Width(80));
                GUILayout.Label(ProcGen.TraitDesc(t), new GUIStyle(lblDim) { wordWrap = false, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(20));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        void MarketWindow()
        {
            GUILayout.Label($"Offers refresh over time. Better reputation brings more and better-paying customers. Reputation level {S.Level}, rating {S.rating:0}/100.", lblDim);
            RivalsStrip();
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            if (S.offers.Count == 0) GUILayout.Label("No offers right now. New customers will call soon.", lblDim);
            foreach (var c in S.offers.ToList())
            {
                GUILayout.BeginVertical(panelDark);
                ContractHeader(c);
                GUILayout.Label(c.flavor, lblDim);
                GUILayout.Label($"Requires: {Requirement(c)}", lblBold);
                GUILayout.Label($"SLA {c.sla}% uptime    Term: {c.months} months    Setup window: {c.setupDays} days    Signing bonus: {Money(c.signingBonus)}", lbl);
                Traits(c);
                string est = Estimate(c);
                GUILayout.Label(est.StartsWith("!") ? est.Substring(1) : "Estimate: " + est, est.StartsWith("!") ? lblBad : lblDim);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Offer expires in {HoursToText(c.expiresHour - S.hour)}", lblDim);
                GUILayout.FlexibleSpace();
                NegotiateButtons(c);
                if (Button("Decline", true, GUILayout.Width(90))) Sim.DeclineOffer(c);
                if (Button("Accept contract", true, GUILayout.Width(160))) Sim.AcceptOffer(c);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }

        void ReqBar(string label, float have, float need, string unit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, lblSmall, GUILayout.Width(80));
            LayoutBar(need <= 0 ? 1 : have / need, have >= need - 0.001f ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.55f, 0.2f), $"{have:0.#} / {need:0.#} {unit}", 16);
            GUILayout.EndHorizontal();
        }

        void ContractsWindow()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Active", contractsTab == 0 ? btnOn : btn, GUILayout.Width(120))) contractsTab = 0;
            if (GUILayout.Button("History", contractsTab == 1 ? btnOn : btn, GUILayout.Width(120))) contractsTab = 1;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Completed {S.contractsCompleted}   Failed {S.contractsFailed}", lblDim);
            GUILayout.EndHorizontal();
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            if (contractsTab == 1)
            {
                foreach (var c in S.contracts.Where(x => x.status == ContractStatus.Completed || x.status == ContractStatus.Cancelled).Reverse())
                {
                    GUILayout.BeginHorizontal(panelDark);
                    Swatch(Catalog.CustomerColors[c.colorIdx]);
                    GUILayout.Label($"{c.customer} - {Catalog.TypeName(c.type)}", lblBold, GUILayout.Width(330));
                    GUILayout.Label(c.status.ToString(), c.status == ContractStatus.Completed ? lblGood : lblBad, GUILayout.Width(90));
                    GUILayout.Label($"Earned {Money(c.earned)}   Uptime {Sim.Uptime(c, false):0.00}%", lblSmall);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                return;
            }
            var list = S.contracts.Where(x => x.status == ContractStatus.Setup || x.status == ContractStatus.Active).ToList();
            if (list.Count == 0) GUILayout.Label("No active contracts. Visit the Market [M] to sign customers.", lblDim);
            var selRacks = gm.selection.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList();
            foreach (var c in list)
            {
                GUILayout.BeginVertical(panelDark);
                ContractHeader(c);
                GUILayout.Label($"Requires: {Requirement(c)}    SLA {c.sla}%", lbl);
                Traits(c);
                Sim.provision.TryGetValue(c.id, out var p);
                p ??= new Provision();
                int racks = Sim.Racks.Count(o => o.contractId == c.id);
                switch (c.type)
                {
                    case ContractType.Colocation: ReqBar("Racks", p.racks, c.reqRacks, "online"); break;
                    case ContractType.Dedicated: ReqBar("Servers", p.qualServers, c.reqServers, $"({c.reqCoresPerServer}+ cores)"); break;
                    case ContractType.Compute: ReqBar("Cores", p.cores, c.reqCores, "cores"); break;
                    case ContractType.Storage: ReqBar("Storage", p.storage, c.reqStorageTB, "TB"); break;
                    case ContractType.GpuCloud: ReqBar("GPUs", p.gpus, c.reqGpus, "GPUs"); break;
                }
                ReqBar("Bandwidth", p.bandwidth, c.reqBandwidth, "Gbps");
                SatisfactionBar(c);

                if (c.status == ContractStatus.Setup)
                    GUILayout.Label($"SETTING UP - deadline in {HoursToText(c.deadlineHour - S.hour)}. Goes live automatically once requirements are met.", lblWarn);
                else
                {
                    float mu = Sim.Uptime(c, true);
                    GUILayout.Label($"Month {c.monthsServed + 1}/{c.months}   This month uptime {mu:0.000}% (SLA {c.sla}%)   Overall {Sim.Uptime(c, false):0.00}%   Breaches {c.breaches}/{(c.traits.Contains("Strict") ? 2 : 3)}   Earned {Money(c.earned)}",
                        mu < c.sla ? lblBad : lblSmall);
                }
                if (!string.IsNullOrEmpty(c.lastIssue) && !c.meeting) GUILayout.Label("Problem: " + c.lastIssue, lblBad);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{racks} rack(s) assigned", lblDim);
                GUILayout.FlexibleSpace();
                if (SmallButton($"Assign {selRacks.Count} selected rack(s)", selRacks.Count > 0))
                    foreach (var rk in selRacks) { var e = Sim.Assign(rk, c); if (e != null) { Toast(e, 2); break; } }
                if (SmallButton(gm.view.highlightContract == c.id ? "Stop highlight" : "Highlight racks")) gm.view.highlightContract = gm.view.highlightContract == c.id ? -1 : c.id;
                if (SmallButton("Select racks"))
                {
                    gm.selection.Clear();
                    gm.selection.AddRange(Sim.Racks.Where(o => o.contractId == c.id));
                }
                var cc = c;
                if (SmallButton($"Cancel ({Money(Sim.CancelPenalty(c))})")) Confirm($"Cancel the contract with {c.customer}? You'll pay a {Money(Sim.CancelPenalty(c))} early termination fee and lose reputation.", () => Sim.CancelContract(cc));
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }

        void ResearchWindow()
        {
            GUILayout.Label("Unlock new hardware with cash once your reputation level is high enough. Complete contracts reliably to gain reputation XP.", lblDim);
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            for (int lvl = 1; lvl <= Catalog.MaxLevel; lvl++)
            {
                var items = new List<(string id, string name, string kind, string desc, float cost)>();
                foreach (var d in Catalog.Devices.Where(x => x.level == lvl && x.research > 0))
                    items.Add((d.id, d.name, d.kind == DeviceKind.Switch ? "Switch" : "Server",
                        d.kind == DeviceKind.Switch ? $"{d.ports} ports, {d.uplinkGbps:0}G uplink" : $"{d.heightU}U, {d.cores} cores, {d.storageTB:0} TB{(d.gpus > 0 ? $", {d.gpus} GPU" : "")}, {d.watts:0} W", d.research));
                foreach (var f in Catalog.Facilities.Where(x => x.level == lvl && x.research > 0))
                    items.Add((f.id, f.name, Catalog.KindName(f.kind), f.desc, f.research));
                if (items.Count == 0) continue;
                bool reached = S.Level >= lvl;
                GUILayout.Label($"LEVEL {lvl}" + (reached ? "" : $"  (reach {Catalog.XpForLevel(lvl)} XP)"), reached ? lblBold : lblDim);
                foreach (var it in items)
                {
                    bool done = Sim.IsUnlocked(it.id);
                    GUILayout.BeginHorizontal(panelDark);
                    GUILayout.BeginVertical(GUILayout.Width(560));
                    GUILayout.Label($"{it.name}   <{it.kind}>", done ? lblGood : reached ? lblBold : lblDim);
                    GUILayout.Label(it.desc, lblDim);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    if (done) GUILayout.Label("Unlocked", lblGood);
                    else if (Button($"Research {Money(it.cost)}", reached && S.money >= it.cost, GUILayout.Width(170)))
                    {
                        var e = Sim.Research(it.id);
                        if (e != null) Toast(e, 2);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
        }

        void FinanceRow(string name, float v, bool expense)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(200));
            GUILayout.Label((expense ? "-" : "+") + Money(v), expense ? (v > 0 ? lblBad : lblDim) : lblGood);
            GUILayout.EndHorizontal();
        }

        void FinanceWindow()
        {
            var cur = S.current;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(panelDark, GUILayout.Width(330));
            GUILayout.Label("This month so far", lblBold);
            FinanceRow("Contract income", cur.income, false);
            FinanceRow("Bonuses & grants", cur.bonuses, false);
            FinanceRow("SLA credits & penalties", cur.penalties, true);
            FinanceRow("Electricity", cur.power, true);
            FinanceRow("ISP transit fees", cur.network, true);
            FinanceRow("Salaries", cur.salaries, true);
            FinanceRow("Repairs, research & hiring", cur.other, true);
            FinanceRow("Construction & hardware", cur.capex, true);
            GUILayout.Label($"Net: {Money(cur.Net)}", cur.Net >= 0 ? lblGood : lblBad);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(panelDark);
            GUILayout.Label("Operations", lblBold);
            float monthlyRev = S.contracts.Where(c => c.status == ContractStatus.Active).Sum(c => c.monthlyPay);
            float monthlyPower = Sim.loadKW * S.city.powerPrice * Catalog.HoursPerMonth;
            float monthlyNet = Sim.ObjectsOfKind(FacilityKind.Isp).Sum(o => Catalog.Facility(o.def).monthly);
            GUILayout.Label($"Recurring revenue: {Money(monthlyRev)}/mo", lblGood);
            GUILayout.Label($"Power at current load: {Money(monthlyPower)}/mo  ({Sim.loadKW:0.0} kW @ ${S.city.powerPrice:0.000}/kWh)", lbl);
            GUILayout.Label($"ISP fees: {Money(monthlyNet)}/mo", lbl);
            GUILayout.Label($"Salaries: {Money(S.technicians * Catalog.TechSalary)}/mo", lbl);
            float pue = Sim.itLoadKW > 0 ? Sim.loadKW / Sim.itLoadKW : 0;
            GUILayout.Label($"PUE: {(pue > 0 ? pue.ToString("0.00") : "-")}   IT load {Sim.itLoadKW:0.0} kW", lbl);
            GUILayout.Space(6);
            GUILayout.Label($"Lifetime revenue: {Money(S.lifetimeRevenue)}", lbl);
            GUILayout.Label($"City: {S.city.name} ({S.city.climate})   Seed {S.seed}   {S.difficulty}", lblDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Monthly net (last 12 months)", lblBold);
            var reps = S.reports.Skip(Mathf.Max(0, S.reports.Count - 12)).ToList();
            var area = GUILayoutUtility.GetRect(600, 180, GUILayout.ExpandWidth(true));
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.35f);
            GUI.DrawTexture(area, white);
            if (reps.Count > 0)
            {
                float max = Mathf.Max(1000, reps.Max(x => Mathf.Abs(x.Net)));
                float bw = area.width / 12f;
                float mid = area.y + area.height / 2;
                GUI.color = new Color(1, 1, 1, 0.2f);
                GUI.DrawTexture(new Rect(area.x, mid, area.width, 1), white);
                for (int i = 0; i < reps.Count; i++)
                {
                    float v = reps[i].Net;
                    float hgt = Mathf.Abs(v) / max * (area.height / 2 - 14);
                    GUI.color = v >= 0 ? new Color(0.3f, 0.85f, 0.45f) : new Color(1f, 0.4f, 0.3f);
                    var br = v >= 0 ? new Rect(area.x + i * bw + 6, mid - hgt, bw - 12, hgt) : new Rect(area.x + i * bw + 6, mid, bw - 12, hgt);
                    GUI.DrawTexture(br, white);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(area.x + i * bw, area.yMax - 16, bw, 16), $"M{reps[i].month + 1}", new GUIStyle(lblSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
                    GUI.Label(new Rect(area.x + i * bw, v >= 0 ? mid - hgt - 15 : mid + hgt, bw, 15), $"{v / 1000f:0.#}k", new GUIStyle(lblSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
                }
            }
            else
            {
                GUI.color = Color.white;
                GUI.Label(area, "No completed months yet.", lblCenter);
            }
            GUI.color = old;
        }

        void StaffWindow()
        {
            GUILayout.BeginVertical(panelDark);
            GUILayout.Label("Technicians", lblBold);
            GUILayout.Label("Technicians automatically repair failed servers, switches and facility equipment. Each needs a desk in an Office. Salary: " + Money(Catalog.TechSalary) + "/month each.", lblDim);
            int failed = S.objects.Count(o => o.failed) + S.objects.Sum(o => o.devices.Count(d => d.failed));
            GUILayout.Label($"Technicians: {S.technicians}   Desks: {Sim.deskCount}   Failed equipment waiting: {failed}", lbl);
            if (S.technicians > Sim.deskCount) GUILayout.Label("Some technicians have no desk and can't work!", lblWarn);
            GUILayout.BeginHorizontal();
            if (Button("Hire technician ($1,000 fee)", S.technicians < Sim.deskCount && S.money >= 1000, GUILayout.Width(240))) { var e = Sim.Hire(); if (e != null) Toast(e, 2); }
            if (Button("Let one go", S.technicians > 0, GUILayout.Width(120))) Sim.Fire();
            GUILayout.EndHorizontal();
            if (Sim.deskCount == 0) GUILayout.Label("Paint an Office room and place Technician Desks first.", lblWarn);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(panelDark);
            GUILayout.Label("Land", lblBold);
            GUILayout.Label($"You own a {S.ownX1 - S.ownX0} x {S.ownY1 - S.ownY0} lot. Buy 4-tile strips to expand. Land price multiplier in {S.city.name}: x{S.city.landPrice:0.00}.", lblDim);
            for (int d = 0; d < 4; d++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Simulation.DirNames[d], lbl, GUILayout.Width(80));
                if (Sim.CanExpand(d))
                {
                    float cost = Sim.LandCost(d);
                    if (Button($"Buy strip ({Money(cost)})", S.money >= cost, GUILayout.Width(220))) { var e = Sim.BuyLand(d); if (e != null) Toast(e, 2); }
                }
                else GUILayout.Label("Map edge", lblDim);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        void LogWindow()
        {
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            for (int i = S.log.Count - 1; i >= 0; i--)
            {
                var e = S.log[i];
                int d = e.hour / 24;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Y{d / 360 + 1} M{(d / 30) % 12 + 1:00} D{d % 30 + 1:00} {e.hour % 24:00}:00", lblDim, GUILayout.Width(130));
                GUILayout.Label(e.text, e.severity == 3 ? lblBad : e.severity == 2 ? lblWarn : e.severity == 1 ? new GUIStyle(lblGood) { wordWrap = true } : lblWrap);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void DrawMenu()
        {
            DimBackground();
            float w = 320, h = 440;
            var r = Reg(new Rect((VW - w) / 2, (VH - h) / 2, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("Paused", new GUIStyle(header) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(8);
            if (Button("Resume", true, GUILayout.Height(36))) OpenWindow(Window.None);
            if (Button("Save game  [F5]", true, GUILayout.Height(36))) { gm.Save(1); OpenWindow(Window.None); }
            if (Button("Save / Load slots", true, GUILayout.Height(36))) OpenWindow(Window.SaveLoad);
            if (Button("Settings", true, GUILayout.Height(36))) OpenWindow(Window.Settings);
            if (Button("How to play", true, GUILayout.Height(36))) OpenWindow(Window.Help);
            if (Button("Main menu", true, GUILayout.Height(36))) Confirm("Return to the main menu? Unsaved progress will be lost.", () => { gm.inGame = false; window = Window.None; gm.selection.Clear(); });
            if (Button("Quit", true, GUILayout.Height(36))) Application.Quit();
            GUILayout.EndArea();
        }

        void DimBackground()
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(Reg(new Rect(0, 0, VW, VH)), white);
            GUI.color = old;
        }

        void DrawHelp()
        {
            DimBackground();
            float w = 760, h = Mathf.Min(640, VH - 60);
            var r = Reg(new Rect((VW - w) / 2, (VH - h) / 2, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("How to play", header);
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            GUILayout.Label(HelpText, lblWrap);
            GUILayout.EndScrollView();
            if (Button("Close", true, GUILayout.Height(32))) OpenWindow(Window.None);
            GUILayout.EndArea();
        }

        const string HelpText =
@"GOAL
Run a profitable, reliable data center. Sign customer contracts, provision racks and servers for them, keep everything powered, cooled and connected, and grow your reputation to unlock better hardware. Every new game uses a seed that changes the city, the lot, and the stream of customers.

CAMERA
WASD / arrows: pan    Middle mouse drag: pan    Scroll: zoom    Q / E: rotate    PageUp / PageDown: tilt

BUILDING
1. Foundation [F]: drag on clear land you own. Trees and rocks must be cleared with Demolish.
2. Rooms: paint foundation as Server Hall, Network Room, Power Room or Office.
3. Equipment [B]: place gear in the right room. Transformers and generators go outdoors on owned land.
4. Demolish [X]: removes objects first, then rooms, then foundation, then trees/rocks.

POWER
Usable supply = min(Utility Transformers, Switchgear). If load exceeds supply, the newest racks shut down.
Grid outages happen. Short blips are covered by UPS >= load. Long outages need UPS AND generators >= load.

COOLING
Each connected Server Hall is a cooling zone. Heat = power of the racks inside. CRAC units remove heat.
Above ~30C hardware fails more often. At 45C racks shut down. Use the Heat map [H].

NETWORK
Each rack needs a ToR switch (top-of-rack), and a cable to a Core Switch in a Network Room.
Each server uses one ToR port. Rack bandwidth = min(ToR uplink, core port speed).
Total customer bandwidth must fit within your ISP uplinks or every customer is throttled.

CONTRACTS
Accept offers in the Market [M]. Then select racks and assign them to the contract (rack inspector or Contracts window).
Colocation: customers bring their own servers. You provide empty racks with a ToR switch, power and network.
Dedicated / Compute / Storage / GPU: you buy servers into the assigned racks to meet the requirement.
Once requirements are met the contract goes live and pays monthly. Miss the setup deadline and you pay a penalty.
Each month uptime is compared to the SLA. Breaches cost service credits and reputation, and customers leave after repeated breaches.
Over-provision a little: one failed server can drop you below the requirement.

STAFF
Build an Office with Technician Desks, then hire technicians in Staff & Land. They repair failures automatically.
Without technicians you must pay for emergency repairs from the inspector.

PROGRESSION
Completing contract months earns XP. Higher reputation levels unlock research for new servers, switches, racks, cooling and power.

HOT & COLD AISLES
A rack's intake is its front (the side with the hardware faceplates). Face rows front-to-front across a 1-tile cold aisle and back-to-back across hot aisles for a heat bonus. A rack breathing another rack's exhaust runs hotter. Use the Airflow overlay [O].

REDUNDANCY
Redundant contracts pay ~35% more but every rack needs a dual PDU, two ToR switches, and uplinks to two different core switches. Redundant racks keep running when one PDU trips or one core switch fails.

STAFF
Hire Technicians (servers, tickets), Electricians (power, cooling, PDUs), Network Engineers (network gear, tickets) and Security (stops DDoS attacks and break-ins). Staff physically walk to jobs, so keep aisles clear and the office close to the halls.

CUSTOMERS
Support tickets appear for active contracts. Resolve them (staff or paid contractor) to keep satisfaction up. Happy customers renew and refer new customers; unhappy ones get poached by rival data centers. In the Market you can negotiate once per offer.

ENERGY
Grid power is cheapest at night and most expensive 17:00-21:00. Solar arrays cut your bill and carbon tax; eco customers want 25%+ solar.

HARDWARE AGE
Hardware fails more often after ~1.5 years and resells for less. Refresh old devices from the rack inspector (Age overlay helps).

EVENTS
Fires (install Fire Suppression in every hall), coolant leaks, DDoS, chip shortages, price wars, bankruptcies, break-ins, storms and new carbon levies keep things interesting.

SITES
From reputation level 4 you can open data centers in other cities (Sites button in the top bar). Each site has its own climate, power prices, grid events, land and staff. Contracts can use racks in any site; geo-redundant customers need racks in at least two.

CABLE TRAYS & RAISED FLOORS
Rooms & Infra > Cable Trays: overhead trays halve cabling cost, avoid the loose-cable heat penalty and are required for runs over 14 tiles. Raised floors protect server halls from floods and boost cooling.

SHIFTS & BATTERIES
Staff work day (07-19) or night (19-07) shifts and share desks across shifts; tired staff slow down. Battery storage charges on cheap night power, discharges at the evening peak and backs up outages.

GOALS
Pick a scenario at New Game for a specific challenge, earn achievements, and take your company public once you're big enough.

TIME
Space: pause    1-4: game speed    F5: quick save    F9: quick load    Esc: cancel / menu
Shift+click or drag a box in Select mode to select multiple racks for bulk actions.
Ctrl+Z: undo the last build action. O: cycle overlays. All keys can be rebound in Settings.";

        // ---------------------------------------------------------------- popups
        void DrawPopups()
        {
            if (Sim.popups.Count == 0) return;
            var p = Sim.popups.Peek();
            DimBackground();
            float w = 520;
            float bodyH = lblWrap.CalcHeight(new GUIContent(p.body), w - 24);
            float h = bodyH + 120;
            var r = Reg(new Rect((VW - w) / 2, (VH - h) / 2, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label(p.title, header);
            GUILayout.Space(6);
            GUILayout.Label(p.body, lblWrap);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            foreach (var opt in p.options)
            {
                if (Button(opt.Key, true, GUILayout.Width(140), GUILayout.Height(34)))
                {
                    Sim.popups.Dequeue();
                    opt.Value?.Invoke();
                    Sim.company.BindActive();
                    Sim.Recompute();
                    break;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawConfirm()
        {
            if (confirmAction == null) return;
            DimBackground();
            float w = 460;
            float bodyH = lblWrap.CalcHeight(new GUIContent(confirmText), w - 24);
            float h = bodyH + 90;
            var r = Reg(new Rect((VW - w) / 2, (VH - h) / 2, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label(confirmText, lblWrap);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (Button("Cancel", true, GUILayout.Width(120), GUILayout.Height(32))) { confirmAction = null; }
            if (Button("Confirm", true, GUILayout.Width(120), GUILayout.Height(32))) { var a = confirmAction; confirmAction = null; a(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
