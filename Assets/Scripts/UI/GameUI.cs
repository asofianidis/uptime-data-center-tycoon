using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    /// <summary>
    /// UI Toolkit front-end. Retained elements with live bindings (see UiKit). Split across partial files:
    /// GameUI (core, HUD, menus), GameUIInspector (selection), GameUIWindows (management windows),
    /// GameUIDialogs (pause menu, settings, saves, help, popups).
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        public enum Window { None, Market, Contracts, Research, Finance, Staff, Log, Menu, Help, Goals, Dashboard, Settings, SaveLoad, Achievements, Sites, Sandbox }

        public GameManager gm;
        public Window window;
        public bool showObjectives = true;
        public bool autoConnectNewRacks = true;
        public bool textFocus;
        public bool tutorialChoice = true;

        Simulation Sim => gm.sim;
        GameState S => gm.sim.S;

        UIDocument doc;
        PanelSettings panelSettings;
        VisualElement root, worldLayer, hudLayer, menuLayer, modalLayer, fxLayer, toastBox;
        Label tooltip;
        readonly List<Label> zoneLabels = new List<Label>();
        readonly List<Section> hudSections = new List<Section>();
        Section windowSection, modalSection, menuSection, inspectorSection, sideSection, bottomLeftSection, legendSection;
        bool built;
        float appliedScale = -1;

        bool buildMenuOpen, roomMenuOpen, showAllAssign, newGamePanel;
        int buildTab, contractsTab, staffTab;
        string rebinding, confirmText, scenarioId = "sandbox", difficulty = "Normal", templateName = "My rack", tutorialHighlight;
        string seedText = "";
        Action confirmAction;

        class ToastMsg { public Label label; public float time; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        // ================================================================ public API (used by GameManager)
        public bool ModalOpen => !gm.inGame || window == Window.Menu || window == Window.Help || window == Window.Settings || window == Window.SaveLoad || window == Window.Achievements ||
                                 (gm.sim != null && gm.sim.popups.Count > 0) || confirmAction != null;

        public bool IsPointerOverUI()
        {
            if (gm == null || !gm.inGame) return true;
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            return root.panel.Pick(p) != null;
        }

        public void Toast(string text, int sev)
        {
            if (toastBox == null) return;
            var l = K.Text(toastBox, text, "toast", sev == 3 ? "toast-bad" : sev == 2 ? "toast-warn" : sev == 1 ? "toast-good" : null);
            l.pickingMode = PickingMode.Ignore;
            toasts.Add(new ToastMsg { label = l, time = Time.unscaledTime });
            while (toasts.Count > 6) { toasts[0].label.RemoveFromHierarchy(); toasts.RemoveAt(0); }
        }

        public void OpenWindow(Window w)
        {
            if (w == Window.Settings && window != Window.Settings) ResetPendingDisplay();
            window = w;
            if (w != Window.None) { buildMenuOpen = false; roomMenuOpen = false; }
            windowSection?.Invalidate();
            modalSection?.Invalidate();
        }

        public void ToggleWindow(Window w) => OpenWindow(window == w ? Window.None : w);
        public void ToggleBuildMenu() { buildMenuOpen = !buildMenuOpen; roomMenuOpen = false; if (buildMenuOpen) window = Window.None; }

        public void ConfirmRemoveSelection()
        {
            var sel = gm.selection.ToList();
            if (sel.Count == 0) return;
            float refund = sel.Sum(o => Sim.RemovalRefund(o));
            Confirm($"Remove {sel.Count} object(s)? You will be refunded {K.Money(refund)} (resale value of the equipment and installed hardware).", () =>
            {
                foreach (var o in sel) if (Sim.objById.ContainsKey(o.id)) Sim.RemoveObject(o);
                gm.selection.Clear();
            });
        }

        void Confirm(string text, Action a) { confirmText = text; confirmAction = a; modalSection?.Invalidate(); }

        // ================================================================ setup
        void Start() => Build();

        void Build()
        {
            if (built) return;
            built = true;
            var go = new GameObject("UIDocument");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            doc = go.AddComponent<UIDocument>();
            var ps = Resources.Load<PanelSettings>("UI/UptimePanel");
            panelSettings = ps != null ? Instantiate(ps) : ScriptableObject.CreateInstance<PanelSettings>();
            if (panelSettings.themeStyleSheet == null) panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/UptimeTheme");
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 1f;
            ApplyScale();
            doc.panelSettings = panelSettings;
            go.SetActive(true);

            root = doc.rootVisualElement;
            var uss = Resources.Load<StyleSheet>("UI/Uptime");
            if (uss != null) root.styleSheets.Add(uss);
            root.pickingMode = PickingMode.Ignore;

            worldLayer = Layer("world-labels");
            hudLayer = Layer("hud");
            fxLayer = Layer("fx");
            menuLayer = Layer("menu");
            modalLayer = Layer("modal");

            BuildHud();
            BuildFx();
            menuSection = new Section(menuLayer, () => $"{gm.inGame}|{newGamePanel}|{window}", BuildMainMenu, "root-layer");
            menuSection.root.pickingMode = PickingMode.Ignore;
            modalSection = new Section(modalLayer, ModalSignature, BuildModal, "root-layer");
            modalSection.root.pickingMode = PickingMode.Ignore;
        }

        VisualElement Layer(string name)
        {
            var l = K.Div(root, "root-layer");
            l.name = name;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        void ApplyScale()
        {
            float s = Mathf.Clamp(Settings.UiScale, 0.6f, 2f);
            if (Mathf.Approximately(s, appliedScale) || panelSettings == null) return;
            appliedScale = s;
            panelSettings.referenceResolution = new Vector2Int(Mathf.RoundToInt(1600 / s), Mathf.RoundToInt(900 / s));
        }

        // ================================================================ frame loop
        void Update()
        {
            if (!built || root?.panel == null) return;
            ApplyScale();
            HandleRebind();
            TickDisplayRevert();
            var focused = root.panel.focusController?.focusedElement;
            textFocus = focused is TextField || rebinding != null;

            bool inGame = gm.inGame && gm.sim != null;
            hudLayer.style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None;
            fxLayer.style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None;
            worldLayer.style.display = inGame && !ModalOpen ? DisplayStyle.Flex : DisplayStyle.None;
            menuSection.Update();
            if (inGame)
            {
                foreach (var s in hudSections) s.Update();
                UpdateFx();
            }
            modalSection.Update();
            UpdateToasts();
        }

        // ================================================================ HUD
        float VW => root.layout.width > 0 ? root.layout.width : 1600;
        float VH => root.layout.height > 0 ? root.layout.height : 900;

        void BuildHud()
        {
            // Top bar
            var top = K.Panel(hudLayer, "topbar");
            K.Abs(top, left: 0, top: 0, right: 0, height: 46);
            top.style.borderTopWidth = 0; top.style.borderLeftWidth = 0; top.style.borderRightWidth = 0;
            hudSections.Add(new Section(top, () => $"{S.events.Count}|{string.Join(",", S.events.Select(e => e.type))}|{Sim.company.sims.Count}|{S.creative}|{S.TopFloor}", BuildTopBar, "row", "grow"));

            // Toolbar
            var tb = K.Panel(hudLayer);
            K.Abs(tb, left: 6, top: 52, width: 172, bottom: 6);
            hudSections.Add(new Section(tb, () => $"{S.creative}|{Settings.Keys.Count}|{KeySig()}", BuildToolbar, "grow"));

            // Side menu (build / rooms)
            var side = K.Div(hudLayer);
            K.Abs(side, left: 184, top: 52);
            side.pickingMode = PickingMode.Ignore;
            sideSection = new Section(side, () => $"{buildMenuOpen}|{roomMenuOpen}|{buildTab}|{S.Level}|{S.unlocked.Count}|{gm.tool}|{gm.roomType}|{(gm.placeDef != null ? gm.placeDef.id : "")}", BuildSideMenu);
            hudSections.Add(sideSection);

            // Management window
            var win = K.Div(hudLayer);
            K.Abs(win, left: 184, top: 52, bottom: 60);
            win.style.width = 880;
            win.pickingMode = PickingMode.Ignore;
            windowSection = new Section(win, WindowSignature, BuildWindow, "grow");
            windowSection.root.pickingMode = PickingMode.Ignore;
            hudSections.Add(windowSection);

            // Inspector
            var insp = K.Div(hudLayer);
            K.Abs(insp, right: 6, top: 52, bottom: 6, width: 410);
            insp.pickingMode = PickingMode.Ignore;
            inspectorSection = new Section(insp, InspectorSignature, BuildInspector, "grow");
            inspectorSection.root.pickingMode = PickingMode.Ignore;
            hudSections.Add(inspectorSection);

            // Bottom-left: tutorial or objectives
            var bl = K.Div(hudLayer);
            K.Abs(bl, left: 184, bottom: 8, width: 340);
            bl.pickingMode = PickingMode.Ignore;
            bottomLeftSection = new Section(bl, BottomLeftSignature, BuildBottomLeft);
            bottomLeftSection.root.pickingMode = PickingMode.Ignore;
            hudSections.Add(bottomLeftSection);

            // Overlay legend
            var lg = K.Div(hudLayer);
            K.Abs(lg, left: 0, right: 0, top: 52);
            lg.style.alignItems = Align.Center;
            lg.pickingMode = PickingMode.Ignore;
            legendSection = new Section(lg, () => gm.view.overlay.ToString(), (r, s) =>
            {
                if (gm.view.overlay == Overlay.None) return;
                var l = K.Text(r, $"Overlay: {gm.view.overlay}  -  {WorldView.OverlayLegend(gm.view.overlay)}", "tooltip");
                l.pickingMode = PickingMode.Ignore;
            });
            legendSection.root.pickingMode = PickingMode.Ignore;
            hudSections.Add(legendSection);
        }

        string KeySig() => string.Join(",", Settings.Keys.Values.Select(k => (int)k));

        void BuildTopBar(VisualElement r, Scope s)
        {
            var money = K.Text(s, r, () => K.Money(S.money), "money");
            money.style.width = 140;
            K.DynClass(s, money, () => S.money >= 0 ? "good" : "bad");
            var daily = K.Text(s, r, () => { float d = (Sim.hourlyIncome - Sim.hourlyCost) * 24f; return (d >= 0 ? "+" : "") + K.Money(d) + "/day"; }, "small");
            daily.style.width = 110;
            K.DynClass(s, daily, () => Sim.hourlyIncome >= Sim.hourlyCost ? "good" : "bad");
            K.Text(s, r, () => S.DateString(), "bold").style.width = 150;

            K.Btn(s, r, () => Sim.company.MultiSite ? $"{S.siteName} ({S.activeSite + 1}/{Sim.company.sims.Count})" : "Sites", () => ToggleWindow(Window.Sites), null, () => window == Window.Sites);
            K.Small(s, r, () => "▼", () => gm.ChangeFloor(-1), () => S.floor > 0);
            K.Text(s, r, () => S.floor == 0 ? "Ground" : $"Floor {S.floor + 1}", "bold").style.width = 58;
            K.Small(s, r, () => "▲", () => gm.ChangeFloor(+1), () => S.floor < Mathf.Min(GameState.MaxFloors - 1, S.TopFloor + 1));

            K.Space(r, 6).style.width = 6;
            string[] sp = { "II", "1x", "3x", "9x", "24x" };
            for (int i = 0; i < sp.Length; i++)
            {
                int idx = i;
                K.Btn(s, r, () => sp[idx], () => gm.speed = idx, null, () => gm.speed == idx).style.width = idx == 0 ? 32 : 40;
            }
            K.Text(s, r, () => $"Lv {S.Level}", "bold").style.marginLeft = 8;
            var xp = K.Div(r);
            xp.style.width = 110;
            K.Bar(s, xp, () => { int lv = S.Level; if (lv >= Catalog.MaxLevel) return 1f; int a = Catalog.XpForLevel(lv), b = Catalog.XpForLevel(lv + 1); return (S.xp - a) / (float)Mathf.Max(1, b - a); },
                () => new Color(0.6f, 0.4f, 1f), () => { int lv = S.Level; if (lv >= Catalog.MaxLevel) return "MAX"; int a = Catalog.XpForLevel(lv), b = Catalog.XpForLevel(lv + 1); return $"{S.xp - a}/{b - a} XP"; });
            var rep = K.Text(s, r, () => $"Rep {S.rating:0}");
            rep.style.marginLeft = 8;
            K.DynClass(s, rep, () => S.rating >= 60 ? "good" : S.rating >= 35 ? null : "bad");

            Stat(s, r, "Power", () => $"{Sim.loadKW:0}/{Sim.availableKW:0} kW", () => Sim.powerShort ? 2 : Sim.loadKW > Sim.availableKW * 0.85f ? 1 : 0);
            Stat(s, r, "Hot", () => Sim.zones.Count > 0 ? $"{Sim.zones.Max(z => z.temp):0.0}C" : "-", () => { float t = Sim.zones.Count > 0 ? Sim.zones.Max(z => z.temp) : 0; return t > 35 ? 2 : t > 29 ? 1 : 0; });
            Stat(s, r, "WAN", () => $"{Sim.bwDemand:0.#}/{Sim.ispGbps:0}G", () => Sim.netQuality < 1 ? 2 : Sim.bwDemand > Sim.ispGbps * 0.8f ? 1 : 0);
            Stat(s, r, "Ports", () => $"{Sim.corePortsUsed}/{Sim.corePorts}", () => Sim.corePorts > 0 && Sim.corePortsUsed >= Sim.corePorts ? 1 : 0);
            Stat(s, r, "Staff", () => $"{Sim.WorkingStaffCount}", () => 0);
            Stat(s, r, "", () => S.weather + (Sim.solarCapKW > 0 ? $" sun {Sim.solarKW:0}kW" : ""), () => S.weather == "Storm" ? 2 : 0);
            Stat(s, r, "$/kWh", () => $"{Sim.PowerPriceNow + S.city.carbonTax:0.000}", () => Sim.TimeOfDayPriceMul > 1f ? 1 : 0);
            foreach (var e in S.events.Take(3))
            {
                var ev = e;
                K.Text(s, r, () => $"{ev.label} {HoursToText(ev.endHour - S.hour)}", "chip");
            }
            if (S.creative) K.Text(r, "SANDBOX", "chip", "chip-info");
        }

        void Stat(Scope s, VisualElement r, string name, Func<string> value, Func<int> level)
        {
            if (!string.IsNullOrEmpty(name)) K.Text(r, name, "stat-name");
            else K.Space(r, 0).style.width = 10;
            var v = K.Text(s, r, value, "stat-val");
            K.DynClass(s, v, () => { int l = level(); return l == 2 ? "bad" : l == 1 ? "warn" : null; });
        }

        static string HoursToText(int h)
        {
            if (h < 0) h = 0;
            int d = h / 24, hh = h % 24;
            return d > 0 ? $"{d}d {hh}h" : $"{hh}h";
        }

        Button TB(Scope s, VisualElement parent, Func<string> text, Action click, Func<bool> on, string tutorialKey = null)
        {
            var b = K.Btn(s, parent, text, click, null, on);
            if (tutorialKey != null)
                s.Add(() => b.EnableInClassList("highlight", tutorialKey == tutorialHighlight && Mathf.PingPong(Time.unscaledTime * 3f, 1f) > 0.35f));
            return b;
        }

        void BuildToolbar(VisualElement r, Scope s)
        {
            var sv = K.Scroll(r);
            var c = sv.contentContainer;
            K.Text(c, "BUILD", "section-title");
            TB(s, c, () => "Select  [Esc]", () => { gm.SetTool(Tool.Select); buildMenuOpen = roomMenuOpen = false; }, () => gm.tool == Tool.Select);
            TB(s, c, () => $"Foundation  [{Settings.KeyName("Foundation")}]", () => { gm.SetTool(Tool.Foundation); buildMenuOpen = roomMenuOpen = false; }, () => gm.tool == Tool.Foundation, "Foundation");
            TB(s, c, () => "Rooms & Infra", () => { roomMenuOpen = !roomMenuOpen; buildMenuOpen = false; if (roomMenuOpen) { gm.SetTool(Tool.Room); window = Window.None; } },
                () => gm.tool == Tool.Room || gm.tool == Tool.Tray || gm.tool == Tool.Raised || roomMenuOpen, "Rooms");
            TB(s, c, () => $"Equipment  [{Settings.KeyName("Equipment")}]", ToggleBuildMenu, () => buildMenuOpen || gm.tool == Tool.Place, "Equipment");
            TB(s, c, () => $"Demolish  [{Settings.KeyName("Demolish")}]", () => { gm.SetTool(Tool.Demolish); buildMenuOpen = roomMenuOpen = false; }, () => gm.tool == Tool.Demolish);
            K.Btn(s, c, () => $"Undo ({gm.UndoCount})  [Ctrl+{Settings.KeyName("Undo")}]", gm.Undo, () => gm.UndoCount > 0);

            K.Text(c, "MANAGE", "section-title");
            TB(s, c, () => $"Market ({S.offers.Count})  [{Settings.KeyName("Market")}]", () => ToggleWindow(Window.Market), () => window == Window.Market, "Market");
            TB(s, c, () =>
            {
                int needs = S.contracts.Count(x => x.status == ContractStatus.Setup || (x.status == ContractStatus.Active && !x.meeting));
                return "Contracts" + (needs > 0 ? $" (!{needs})" : "") + (S.tickets.Count > 0 ? $" T{S.tickets.Count}" : "");
            }, () => ToggleWindow(Window.Contracts), () => window == Window.Contracts, "Contracts");
            TB(s, c, () => $"Research  [{Settings.KeyName("Research")}]", () => ToggleWindow(Window.Research), () => window == Window.Research);
            TB(s, c, () => $"Staff & Land  [{Settings.KeyName("Staff")}]", () => ToggleWindow(Window.Staff), () => window == Window.Staff, "Staff");
            TB(s, c, () => "Finance", () => ToggleWindow(Window.Finance), () => window == Window.Finance);
            TB(s, c, () => $"Dashboard  [{Settings.KeyName("Dashboard")}]", () => ToggleWindow(Window.Dashboard), () => window == Window.Dashboard);
            TB(s, c, () => $"Goals  [{Settings.KeyName("Goals")}]", () => ToggleWindow(Window.Goals), () => window == Window.Goals);
            TB(s, c, () => "Event Log", () => ToggleWindow(Window.Log), () => window == Window.Log);
            if (S.creative) TB(s, c, () => "Sandbox Tools", () => ToggleWindow(Window.Sandbox), () => window == Window.Sandbox);

            K.Text(c, "VIEW", "section-title");
            TB(s, c, () => $"Overlay: {gm.view.overlay}  [{Settings.KeyName("CycleOverlay")}]", gm.view.CycleOverlay, () => gm.view.overlay != Overlay.None);
            TB(s, c, () => "Network cables", () => gm.view.showCables = !gm.view.showCables, () => gm.view.showCables);
            TB(s, c, () => "Auto-cable new racks", () => autoConnectNewRacks = !autoConnectNewRacks, () => autoConnectNewRacks);
            TB(s, c, () => "Objectives", () => showObjectives = !showObjectives, () => showObjectives);
            K.Space(c, 8);
            K.Btn(c, "Menu", () => OpenWindow(Window.Menu));
        }

        // ---------------------------------------------------------------- side menus
        static readonly string[] BuildTabs = { "Racks", "Cooling", "Network", "Power", "Other" };

        static bool InTab(FacilityDef d, int tab)
        {
            switch (tab)
            {
                case 0: return d.kind == FacilityKind.Rack;
                case 1: return d.kind == FacilityKind.Cooling || d.kind == FacilityKind.Suppression;
                case 2: return d.kind == FacilityKind.CoreSwitch || d.kind == FacilityKind.Isp;
                case 3: return d.kind == FacilityKind.Transformer || d.kind == FacilityKind.Switchgear || d.kind == FacilityKind.Ups || d.kind == FacilityKind.Generator || d.kind == FacilityKind.Solar || d.kind == FacilityKind.Battery;
                default: return d.kind == FacilityKind.Desk || d.kind == FacilityKind.Elevator;
            }
        }

        void BuildSideMenu(VisualElement r, Scope s)
        {
            if (buildMenuOpen) BuildEquipmentMenu(r, s);
            else if (roomMenuOpen) BuildRoomMenu(r, s);
        }

        void BuildEquipmentMenu(VisualElement r, Scope s)
        {
            var p = K.Panel(r);
            p.style.width = 350;
            p.style.maxHeight = VH - 70;
            var tabs = K.Row(p);
            for (int i = 0; i < BuildTabs.Length; i++)
            {
                int idx = i;
                var b = K.Small(tabs, BuildTabs[i], () => { buildTab = idx; });
                b.EnableInClassList("on", buildTab == i);
            }
            var sv = K.Scroll(p);
            foreach (var d in Catalog.Facilities.Where(f => InTab(f, buildTab)))
            {
                var def = d;
                bool unlocked = Sim.IsUnlocked(d.id);
                var card = K.Card(sv.contentContainer);
                var head = K.Row(card);
                K.Text(head, d.name, "bold");
                K.Flex(head);
                var cost = K.Text(head, K.Money(d.cost));
                K.DynClass(s, cost, () => S.money >= def.cost ? "good" : "bad");
                K.Text(card, d.desc, "dim");
                K.Text(card, $"Place in: {Catalog.RoomName(d.room)}   Size {d.w}x{d.h}" + (d.monthly > 0 ? $"   {K.Money(d.monthly)}/mo" : ""), "dim");
                if (unlocked)
                {
                    bool placing = gm.tool == Tool.Place && gm.placeDef == d;
                    var b = K.Btn(card, placing ? "Placing... (right-click to stop)" : "Place", () => gm.BeginPlace(def));
                    b.EnableInClassList("on", placing);
                }
                else K.Text(card, S.Level >= d.level ? $"Research for {K.Money(d.research)} in the Research window" : $"Requires reputation level {d.level}", "warn");
            }
        }

        void BuildRoomMenu(VisualElement r, Scope s)
        {
            var p = K.Panel(r);
            p.style.width = 270;
            K.Text(p, "Paint Rooms", "header");
            K.Text(p, S.floor == 0 ? "Drag over building foundation." : $"Floor {S.floor + 1}: drag over the floor slab.", "dim");
            foreach (var t in new[] { RoomType.ServerHall, RoomType.Network, RoomType.Power, RoomType.Office, RoomType.Corridor })
            {
                var rt = t;
                var row = K.Row(p);
                K.Swatch(row, Catalog.RoomColor(t), 16);
                var b = K.Btn(s, row, () => Catalog.RoomName(rt), () => { gm.SetTool(Tool.Room); gm.roomType = rt; }, null, () => gm.tool == Tool.Room && gm.roomType == rt);
                b.style.flexGrow = 1;
            }
            if (gm.tool == Tool.Room)
            {
                string desc = gm.roomType switch
                {
                    RoomType.ServerHall => "Racks and cooling go here. Each connected hall on a floor is one cooling zone.",
                    RoomType.Network => "Core switches and ISP uplinks.",
                    RoomType.Power => "Switchgear, UPS and battery storage.",
                    RoomType.Office => "Technician desks.",
                    _ => "Corridor (free). Elevators go here.",
                };
                K.Text(p, desc, "dim");
            }
            K.Text(p, "INFRASTRUCTURE", "section-title");
            K.Btn(s, p, () => $"Cable Trays (${Simulation.TrayCost:0}/tile)", () => gm.SetTool(Tool.Tray), null, () => gm.tool == Tool.Tray);
            K.Btn(s, p, () => $"Raised Floor (${Simulation.RaisedCost:0}/tile)", () => gm.SetTool(Tool.Raised), null, () => gm.tool == Tool.Raised);
            if (gm.tool == Tool.Tray) K.Text(p, $"Overhead trays halve cabling cost, keep airflow clean, and are required for runs longer than {Simulation.MaxLooseCable} tiles. Cross-floor runs go through an Elevator & Cable Riser.", "dim");
            if (gm.tool == Tool.Raised) K.Text(p, "Raised floors in server halls protect against floods (ground floor) and add up to +10% cooling.", "dim");
            K.Text(p, "FLOORS", "section-title");
            K.Text(p, $"Viewing {(S.floor == 0 ? "the ground floor" : $"floor {S.floor + 1}")}. Use the arrows in the top bar or [{Settings.KeyName("FloorUp")}]/[{Settings.KeyName("FloorDown")}]. Upper floors need a slab ({K.Money(Catalog.UpperFloorCost)}/tile) built over the storey below.", "dim");
        }

        // ---------------------------------------------------------------- bottom-left: tutorial or objectives
        string BottomLeftSignature()
        {
            if (S.tutorial) return $"tut|{S.tutorialStep}";
            if (!showObjectives || S.activeSite != 0) return "none";
            return "obj|" + string.Join("", Objectives().Select(o => o.Item2 ? "1" : "0"));
        }

        List<(string, bool, string)> Objectives()
        {
            bool Has(FacilityKind k) => S.objects.Any(o => Catalog.Facility(o.def).kind == k);
            bool HasRoom(RoomType t) => S.rooms.Any(v => v == (int)t);
            var steps = new List<(string, bool, string)>
            {
                ("Build a foundation [F] (drag on clear land)", S.rooms.Any(v => v != 0), "Foundation"),
                ("Paint a Server Hall, Network Room and Power Room", HasRoom(RoomType.ServerHall) && HasRoom(RoomType.Network) && HasRoom(RoomType.Power), "Rooms"),
                ("Place a Utility Transformer outdoors and Switchgear in the Power Room", Has(FacilityKind.Transformer) && Has(FacilityKind.Switchgear), "Equipment"),
                ("Place a Core Switch and ISP Fiber in the Network Room", Has(FacilityKind.CoreSwitch) && Has(FacilityKind.Isp), "Equipment"),
                ("Place a Rack and a CRAC unit in the Server Hall", Has(FacilityKind.Rack) && Has(FacilityKind.Cooling), "Equipment"),
                ("Select the rack: add a ToR switch + servers, cable it to the core", Sim.racks.Values.Any(i => i.online), null),
                ("Accept a contract in the Market [M] and assign racks to it", S.contracts.Any() && S.objects.Any(o => o.contractId > 0), "Market"),
                ("Bring a contract online", S.contracts.Any(c => c.status == ContractStatus.Active || c.status == ContractStatus.Completed), "Contracts"),
                ("Build an Office with desks, then hire staff (Staff & Land)", S.staff.Count > 0, "Staff"),
            };
            return steps;
        }

        void BuildBottomLeft(VisualElement r, Scope s)
        {
            tutorialHighlight = null;
            if (S.tutorial) { BuildTutorial(r, s); return; }
            gm.view.HideHint();
            if (!showObjectives || S.activeSite != 0) return;
            var steps = Objectives();
            var p = K.Panel(r);
            var head = K.Row(p);
            K.Text(head, "Getting started", "bold");
            K.Flex(head);
            K.Small(head, "x", () => showObjectives = false);
            if (steps.All(x => x.Item2)) { K.Text(p, "All set! Grow your reputation and research new hardware.", "dim"); return; }
            bool first = true;
            foreach (var (text, done, key) in steps)
            {
                var l = K.Text(p, (done ? "[x] " : "[ ] ") + text, "small", done ? "dim" : first ? "warn" : null);
                if (!done && first) { tutorialHighlight = key; first = false; }
            }
        }

        // ---------------------------------------------------------------- effects layer: toasts, tooltip, world labels
        void BuildFx()
        {
            toastBox = K.Div(fxLayer);
            K.Abs(toastBox, bottom: 10);
            toastBox.style.left = Length.Percent(50);
            toastBox.style.width = 520;
            toastBox.style.translate = new Translate(Length.Percent(-50), 0);
            toastBox.pickingMode = PickingMode.Ignore;
            toastBox.style.justifyContent = Justify.FlexEnd;

            tooltip = K.Text(fxLayer, "", "tooltip");
            K.Abs(tooltip);
            tooltip.pickingMode = PickingMode.Ignore;
            tooltip.style.display = DisplayStyle.None;
        }

        void UpdateToasts()
        {
            float now = Time.unscaledTime;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                float age = now - toasts[i].time;
                if (age > 7f) { toasts[i].label.RemoveFromHierarchy(); toasts.RemoveAt(i); continue; }
                toasts[i].label.style.opacity = Mathf.Clamp01(7f - age);
            }
        }

        Vector2 MousePanel()
        {
            var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return root.panel != null ? RuntimePanelUtils.ScreenToPanel(root.panel, p) : p;
        }

        void UpdateFx()
        {
            // Tool hint / hover tooltip near the mouse.
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
            if (text == null || ModalOpen) tooltip.style.display = DisplayStyle.None;
            else
            {
                tooltip.style.display = DisplayStyle.Flex;
                if (tooltip.text != text) tooltip.text = text;
                var mp = MousePanel();
                float w = tooltip.resolvedStyle.width > 0 ? tooltip.resolvedStyle.width : 200, h = tooltip.resolvedStyle.height > 0 ? tooltip.resolvedStyle.height : 40;
                float x = mp.x + 18, y = mp.y + 18;
                if (x + w > VW) x = mp.x - w - 10;
                if (y + h > VH) y = mp.y - h - 10;
                tooltip.style.left = x; tooltip.style.top = y;
            }

            // Zone temperature labels on the viewed floor.
            var cam = Camera.main;
            int n = 0;
            if (cam != null && root.panel != null)
                foreach (var z in Sim.zones)
                {
                    if (z.tiles.Count < 2 || z.floor != S.floor) continue;
                    var wp = new Vector3(z.center.x + 0.5f, 0.1f + S.floor * GameState.FloorHeight, z.center.y + 0.5f);
                    var sp = cam.WorldToScreenPoint(wp);
                    if (sp.z < 0) continue;
                    while (zoneLabels.Count <= n)
                    {
                        var l = K.Text(worldLayer, "", "world-label");
                        K.Abs(l);
                        l.pickingMode = PickingMode.Ignore;
                        zoneLabels.Add(l);
                    }
                    var lab = zoneLabels[n++];
                    lab.style.display = DisplayStyle.Flex;
                    string t = $"{z.temp:0.0}C   {z.heatKW:0}/{z.coolKW:0} kW";
                    if (lab.text != t) lab.text = t;
                    lab.style.borderLeftColor = WorldView.HeatColor(z.temp);
                    var pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
                    lab.style.left = pp.x - 50; lab.style.top = pp.y - 10;
                }
            for (int i = n; i < zoneLabels.Count; i++) zoneLabels[i].style.display = DisplayStyle.None;
        }

        // ---------------------------------------------------------------- main menu
        void BuildMainMenu(VisualElement r, Scope s)
        {
            if (gm.inGame) return;
            var bg = K.Div(r, "root-layer", "menu-bg");
            bg.pickingMode = PickingMode.Position;
            var rnd = new System.Random(7);
            for (int i = 0; i < 160; i++)
            {
                var led = K.Div(bg, "blink-led");
                led.style.left = Length.Percent((float)rnd.NextDouble() * 100f);
                led.style.top = Length.Percent((float)rnd.NextDouble() * 100f);
                Color col = i % 5 == 0 ? new Color(1f, 0.7f, 0.2f) : i % 3 == 0 ? new Color(0.3f, 0.7f, 1f) : new Color(0.3f, 1f, 0.45f);
                float speed = 0.5f + (float)rnd.NextDouble() * 2f, ph = (float)rnd.NextDouble() * 10f;
                led.style.backgroundColor = col;
                s.Add(() => led.style.opacity = 0.15f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed + ph)));
            }
            var col0 = K.Div(bg, "root-layer");
            col0.pickingMode = PickingMode.Ignore;
            col0.style.alignItems = Align.Center;
            col0.style.paddingTop = Length.Percent(8);
            K.Text(col0, "UPTIME", "title");
            K.Text(col0, "DATA CENTER TYCOON", "subtitle");
            K.Space(col0, 40);
            if (window == Window.Settings || window == Window.SaveLoad || window == Window.Achievements) return;
            if (!newGamePanel)
            {
                var box = K.Div(col0);
                box.style.width = 320;
                K.Btn(box, "New Game", () => { newGamePanel = true; seedText = UnityEngine.Random.Range(100000, 999999).ToString(); }, "btn-big");
                var load = K.Btn(box, "Load Game", () => OpenWindow(Window.SaveLoad), "btn-big");
                load.SetEnabled(GameManager.HasSave(1) || GameManager.HasSave(2) || GameManager.HasSave(3) || GameManager.HasSave(0));
                K.Btn(box, "Achievements", () => OpenWindow(Window.Achievements), "btn-big");
                K.Btn(box, "Settings", () => OpenWindow(Window.Settings), "btn-big");
                K.Btn(box, "Quit", Application.Quit, "btn-big");
            }
            else BuildNewGamePanel(col0, s);
            var foot = K.Text(bg, "Every seed generates a different city, lot and stream of customers.", "dim");
            K.Abs(foot, left: 10, bottom: 8);
        }

        void BuildNewGamePanel(VisualElement parent, Scope s)
        {
            var p = K.Panel(parent);
            p.style.width = 640;
            K.Text(p, "New Data Center", "header");
            var row = K.Row(p);
            K.Text(row, "World seed:").style.width = 100;
            var seed = new TextField { value = seedText, maxLength = 9 };
            seed.AddToClassList("textfield");
            seed.style.width = 160;
            seed.RegisterValueChangedCallback(e => { seedText = new string(e.newValue.Where(char.IsDigit).ToArray()); if (seed.value != seedText) seed.SetValueWithoutNotify(seedText); });
            row.Add(seed);
            K.Btn(row, "Random", () => { seedText = UnityEngine.Random.Range(100000, 999999).ToString(); seed.SetValueWithoutNotify(seedText); });

            var drow = K.Row(p);
            K.Text(drow, "Difficulty:").style.width = 100;
            foreach (var d in new[] { "Easy", "Normal", "Hard" })
            {
                var dd = d;
                K.Btn(s, drow, () => dd, () => difficulty = dd, null, () => difficulty == dd).style.width = 100;
            }
            K.Text(s, p, () => difficulty == "Easy" ? "More starting cash, fewer hardware failures." : difficulty == "Hard" ? "Tight budget, low starting reputation, frequent failures." : "The intended experience.", "dim");

            var city = K.Card(p);
            CityProfile Preview() => ProcGen.GenerateCity(new System.Random(int.TryParse(seedText, out int sv) ? sv : 0));
            K.Text(s, city, () => $"Location: {Preview().name}", "bold");
            K.Text(s, city, () => { var c = Preview(); return $"Climate: {c.climate}  (cooling x{c.coolingFactor:0.00})"; });
            K.Text(s, city, () => { var c = Preview(); return $"Electricity: ${c.powerPrice:0.000}/kWh    Land price: x{c.landPrice:0.00}"; });
            K.Text(s, city, () => { var c = Preview(); return $"Disaster risk: x{c.disasterRate:0.00}    Market demand: x{c.demandFactor:0.00}"; });
            K.Text(s, city, () => Preview().blurb, "dim");

            K.Text(p, "Scenario:", "bold");
            var grid = K.Row(p);
            grid.style.flexWrap = Wrap.Wrap;
            foreach (var sc in Simulation.Scenarios)
            {
                var scen = sc;
                var b = K.Btn(s, grid, () => scen.name + (Simulation.UnlockedEver("scenario_" + scen.id) ? " *" : ""), () => scenarioId = scen.id, null, () => scenarioId == scen.id);
                b.style.width = 196;
            }
            K.Text(s, p, () => { var cur = Simulation.Scenario(scenarioId); return cur.desc + "  Goal: " + cur.goal; }, "dim");
            K.Btn(s, p, () => (tutorialChoice ? "[x] " : "[ ] ") + "Guided tutorial (recommended for your first game)", () => tutorialChoice = !tutorialChoice, null, () => tutorialChoice);

            var foot = K.Row(p);
            K.Btn(foot, "Back", () => newGamePanel = false).style.width = 120;
            K.Flex(foot);
            K.Btn(foot, "Start", () =>
            {
                newGamePanel = false;
                int seedV = int.TryParse(seedText, out int sv) ? sv : 0;
                gm.StartNewGame(seedV, difficulty, scenarioId);
                S.tutorial = tutorialChoice;
                S.tutorialStep = 0;
                tutSteps = null;
            }, "btn-big").style.width = 200;
        }

        // ---------------------------------------------------------------- key rebinding (polled with the legacy input manager)
        static readonly KeyCode[] AllKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode))).Where(k => k < KeyCode.Mouse0).ToArray();

        void HandleRebind()
        {
            if (rebinding == null) return;
            foreach (var k in AllKeys)
            {
                if (!Input.GetKeyDown(k)) continue;
                if (k != KeyCode.Escape) { Settings.Keys[rebinding] = k; Settings.Save(); }
                rebinding = null;
                modalSection?.Invalidate();
                break;
            }
        }
    }
}
