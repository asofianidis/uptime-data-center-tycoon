using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    public partial class GameUI
    {
        string ModalSignature()
        {
            string popup = gm.sim != null && gm.sim.popups.Count > 0 ? gm.sim.popups.Peek().GetHashCode().ToString() : "";
            return $"{popup}|{confirmText}|{confirmAction != null}|{window}|{rebinding}|{gm.inGame}";
        }

        VisualElement Dimmer(VisualElement r)
        {
            var d = K.Div(r, "dimmer");
            d.pickingMode = PickingMode.Position;
            return d;
        }

        void BuildModal(VisualElement r, Scope s)
        {
            if (confirmAction != null) { BuildConfirm(r); return; }
            if (gm.inGame && gm.sim != null && gm.sim.popups.Count > 0) { BuildPopup(r, gm.sim.popups.Peek()); return; }
            switch (window)
            {
                case Window.Menu: if (gm.inGame) BuildPauseMenu(r); break;
                case Window.Help: BuildHelp(r); break;
                case Window.Settings: BuildSettings(r, s); break;
                case Window.SaveLoad: BuildSaveLoad(r, s); break;
                case Window.Achievements: BuildAchievements(r); break;
            }
        }

        VisualElement ModalPanel(VisualElement r, string title, float width, float maxHeight, bool closeButton = true)
        {
            var dim = Dimmer(r);
            var p = K.Panel(dim, "modal");
            p.style.width = width;
            p.style.maxHeight = Mathf.Min(maxHeight, VH - 40);
            if (title != null)
            {
                var head = K.Row(p);
                K.Text(head, title, "header");
                K.Flex(head);
                if (closeButton) K.Small(head, "Close", () => { rebinding = null; OpenWindow(gm.inGame ? Window.Menu : Window.None); });
            }
            return p;
        }

        void BuildPopup(VisualElement r, Popup pop)
        {
            var p = ModalPanel(r, pop.title, 540, 600, false);
            K.Text(p, pop.body, "wrap");
            K.Space(p, 10);
            var row = K.Row(p);
            K.Flex(row);
            foreach (var opt in pop.options)
            {
                var o = opt;
                K.Btn(row, o.Key, () =>
                {
                    if (gm.sim.popups.Count > 0 && gm.sim.popups.Peek() == pop) gm.sim.popups.Dequeue();
                    o.Value?.Invoke();
                    gm.sim.company.BindActive();
                    gm.sim.Recompute();
                    modalSection.Invalidate();
                }).style.width = 140;
            }
        }

        void BuildConfirm(VisualElement r)
        {
            var p = ModalPanel(r, null, 480, 400, false);
            K.Text(p, confirmText, "wrap");
            K.Space(p, 10);
            var row = K.Row(p);
            K.Flex(row);
            K.Btn(row, "Cancel", () => { confirmAction = null; confirmText = null; }).style.width = 120;
            K.Btn(row, "Confirm", () => { var a = confirmAction; confirmAction = null; confirmText = null; a?.Invoke(); }, "on").style.width = 120;
        }

        void BuildPauseMenu(VisualElement r)
        {
            var dim = Dimmer(r);
            var p = K.Panel(dim);
            p.style.width = 320;
            K.Text(p, "Paused", "header", "center");
            K.Space(p, 6);
            K.Btn(p, "Resume", () => OpenWindow(Window.None), "btn-big");
            K.Btn(p, $"Quick save  [{Settings.KeyName("QuickSave")}]", () => { gm.Save(1); OpenWindow(Window.None); }, "btn-big");
            K.Btn(p, "Save / Load slots", () => OpenWindow(Window.SaveLoad), "btn-big");
            K.Btn(p, "Settings", () => OpenWindow(Window.Settings), "btn-big");
            K.Btn(p, "How to play", () => OpenWindow(Window.Help), "btn-big");
            K.Btn(p, "Main menu", () => Confirm("Return to the main menu? Unsaved progress will be lost.", () => { gm.inGame = false; window = Window.None; gm.selection.Clear(); }), "btn-big");
            K.Btn(p, "Quit", Application.Quit, "btn-big");
        }

        // ---------------------------------------------------------------- settings
        void BuildSettings(VisualElement r, Scope s)
        {
            var p = ModalPanel(r, "Settings", 640, 680);
            var sv = K.Scroll(p);
            var c = sv.contentContainer;
            BuildVideo(c, s);
            K.Text(c, "DISPLAY", "section-title");
            SliderRow(c, "UI scale", Settings.UiScale, 0.7f, 1.6f, v => Settings.UiScale = v, v => $"x{v:0.00}");
            ToggleRow(c, s, "Day/night cycle", () => Settings.DayNight, v => Settings.DayNight = v);
            ToggleRow(c, s, "Weather effects", () => Settings.WeatherFx, v => Settings.WeatherFx = v);
            ToggleRow(c, s, "Show walking staff", () => Settings.ShowStaff, v => Settings.ShowStaff = v);
            K.Text(c, "AUDIO", "section-title");
            SliderRow(c, "Master volume", Settings.Master, 0, 1, v => Settings.Master = v, v => $"{v:P0}");
            SliderRow(c, "Music", Settings.Music, 0, 1, v => Settings.Music = v, v => $"{v:P0}");
            SliderRow(c, "Effects", Settings.Sfx, 0, 1, v => Settings.Sfx = v, v => $"{v:P0}");
            K.Text(c, "GAMEPLAY", "section-title");
            ToggleRow(c, s, "Edge-of-screen panning", () => Settings.EdgePan, v => Settings.EdgePan = v);
            ToggleRow(c, s, "Monthly autosave", () => Settings.Autosave, v => Settings.Autosave = v);
            K.Text(c, "KEY BINDINGS (click, then press a key; Esc cancels)", "section-title");
            foreach (var kv in Settings.Defaults)
            {
                var action = kv.Key;
                var row = K.Row(c);
                K.Text(row, action).style.width = 170;
                K.Btn(s, row, () => rebinding == action ? "press a key..." : Settings.KeyName(action), () => { rebinding = action; }, null, () => rebinding == action, "btn-small").style.width = 150;
            }
            K.Btn(c, "Reset keys to defaults", () => { Settings.ResetKeys(); rebinding = null; modalSection.Invalidate(); });
        }

        // ---------------------------------------------------------------- video
        // Display mode / resolution are staged and only applied with "Apply", followed by a
        // 15 second "keep these settings?" countdown that reverts automatically if not confirmed.
        const float DisplayRevertSeconds = 15f;
        int pendMode, pendW, pendH;
        int revMode, revW, revH;
        float revertAt = -1f;

        void ResetPendingDisplay()
        {
            // A window may have been resized by dragging its edges (or sized by command-line args): adopt its real size.
            if (Settings.DisplayMode == 2 && Settings.ModeIndex(Screen.fullScreenMode) == 2 && !Application.isEditor)
            {
                Settings.ResW = Screen.width;
                Settings.ResH = Screen.height;
            }
            pendMode = Settings.DisplayMode;
            pendW = Settings.ResW;
            pendH = Settings.ResH;
        }

        /// <summary>Called after the display changed outside the settings window (F11 / Alt+Enter).</summary>
        public void OnDisplayChanged()
        {
            revertAt = -1f;
            ResetPendingDisplay();
        }

        bool DisplayDirty => pendMode != Settings.DisplayMode || Settings.EffectiveResolution(pendW, pendH) != Settings.EffectiveResolution(Settings.ResW, Settings.ResH);

        void ApplyPendingDisplay()
        {
            revMode = Settings.DisplayMode; revW = Settings.ResW; revH = Settings.ResH;
            Settings.DisplayMode = pendMode; Settings.ResW = pendW; Settings.ResH = pendH;
            Settings.ApplyDisplay();
            Settings.Save();
            revertAt = Time.unscaledTime + DisplayRevertSeconds;
        }

        void RevertDisplay()
        {
            Settings.DisplayMode = revMode; Settings.ResW = revW; Settings.ResH = revH;
            Settings.ApplyDisplay();
            Settings.Save();
            revertAt = -1f;
            ResetPendingDisplay();
        }

        void TickDisplayRevert()
        {
            if (revertAt < 0) return;
            // Leaving the settings window counts as accepting the new mode.
            if (window != Window.Settings) { revertAt = -1f; return; }
            if (Time.unscaledTime >= revertAt) { RevertDisplay(); Toast("Display settings reverted.", 2); }
        }

        void CycleResolution(int dir)
        {
            var list = Settings.AvailableResolutions();
            int i = list.IndexOf(Settings.EffectiveResolution(pendW, pendH));
            i = i < 0 ? list.Count - 1 : (i + dir + list.Count) % list.Count;
            var r = list[i];
            if (r == Settings.NativeResolution) { pendW = pendH = 0; }
            else { pendW = r.x; pendH = r.y; }
        }

        int QualityLevel => Settings.Quality >= 0 ? Settings.Quality : QualitySettings.GetQualityLevel();

        void BuildVideo(VisualElement c, Scope s)
        {
            K.Text(c, "VIDEO", "section-title");
            ChoiceRow(c, s, "Display mode", () => Settings.DisplayModeNames[pendMode], d => pendMode = (pendMode + d + 3) % 3);
            ChoiceRow(c, s, "Resolution", () => Settings.ResName(pendW, pendH), CycleResolution);
            var row = K.Row(c);
            var status = K.Text(s, row, () => revertAt >= 0
                ? $"Keep these settings? Reverting in {Mathf.CeilToInt(revertAt - Time.unscaledTime)}s"
                : $"Current: {Screen.width} x {Screen.height}, {Settings.DisplayModeNames[Settings.ModeIndex(Screen.fullScreenMode)].ToLowerInvariant()}", "small");
            status.style.width = 330;
            status.style.alignSelf = Align.Center;
            var apply = K.Btn(s, row, () => "Apply", ApplyPendingDisplay, () => DisplayDirty && revertAt < 0, null, "btn-small");
            apply.style.width = 80;
            K.Show(s, apply, () => revertAt < 0);
            var keep = K.Btn(row, "Keep", () => { revertAt = -1f; }, "btn-small", "on");
            keep.style.width = 70;
            K.Show(s, keep, () => revertAt >= 0);
            var revert = K.Btn(row, "Revert", RevertDisplay, "btn-small");
            revert.style.width = 70;
            K.Show(s, revert, () => revertAt >= 0);
            K.Text(c, $"Tip: [{Settings.KeyName("Fullscreen")}] or Alt+Enter toggles fullscreen at any time.", "small", "dim");

            ChoiceRow(c, s, "Graphics quality", () => QualitySettings.names.Length > 0 ? QualitySettings.names[Mathf.Clamp(QualityLevel, 0, QualitySettings.names.Length - 1)] : "Default",
                d => Settings.SetQualityPreset(Mathf.Clamp(QualityLevel + d, 0, QualitySettings.names.Length - 1)));
            ChoiceRow(c, s, "Anti-aliasing", () => Settings.Msaa <= 1 ? "Off" : $"MSAA {Settings.Msaa}x", d =>
            {
                int i = Array.IndexOf(Settings.MsaaLevels, Settings.Msaa);
                Settings.Msaa = Settings.MsaaLevels[Mathf.Clamp((i < 0 ? 0 : i) + d, 0, Settings.MsaaLevels.Length - 1)];
                Settings.ApplyGraphics(); Settings.Save();
            });
            ChoiceRow(c, s, "Shadows", () => Settings.ShadowNames[Settings.Shadows], d =>
            {
                Settings.Shadows = Mathf.Clamp(Settings.Shadows + d, 0, 2);
                Settings.ApplyGraphics(); Settings.Save();
            });
            ToggleRow(c, s, "VSync", () => Settings.VSync, v => { Settings.VSync = v; Settings.ApplyGraphics(); });
            ChoiceRow(c, s, "Frame rate limit", () => Settings.VSync ? "(VSync on)" : Settings.FpsCap <= 0 ? "Unlimited" : $"{Settings.FpsCap} fps", d =>
            {
                int i = Array.IndexOf(Settings.FpsCaps, Settings.FpsCap);
                if (i < 0) i = Array.IndexOf(Settings.FpsCaps, 120);
                Settings.FpsCap = Settings.FpsCaps[Mathf.Clamp(i + d, 0, Settings.FpsCaps.Length - 1)];
                Settings.ApplyGraphics(); Settings.Save();
            }, () => !Settings.VSync);
        }

        void ChoiceRow(VisualElement c, Scope s, string name, Func<string> value, Action<int> step, Func<bool> enabled = null)
        {
            var row = K.Row(c);
            K.Text(row, name).style.width = 170;
            K.Btn(s, row, () => "<", () => step(-1), enabled, null, "btn-small").style.width = 36;
            var l = K.Text(s, row, value);
            l.style.width = 220;
            l.style.alignSelf = Align.Center;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            K.Btn(s, row, () => ">", () => step(+1), enabled, null, "btn-small").style.width = 36;
        }

        void SliderRow(VisualElement c, string name, float value, float min, float max, Action<float> set, Func<float, string> fmt)
        {
            var row = K.Row(c);
            K.Text(row, name).style.width = 170;
            var sl = new Slider(min, max) { value = value };
            sl.style.width = 240;
            sl.style.alignSelf = Align.Center;
            sl.style.marginTop = 10; sl.style.marginBottom = 0;
            row.Add(sl);
            var val = K.Text(row, fmt(value), "small");
            sl.RegisterValueChangedCallback(e => { set(e.newValue); val.text = fmt(e.newValue); Settings.Save(); });
        }

        void ToggleRow(VisualElement c, Scope s, string name, Func<bool> get, Action<bool> set)
        {
            var b = K.Btn(s, c, () => (get() ? "[x] " : "[ ] ") + name, () => { set(!get()); Settings.Save(); }, null, get);
            b.style.width = 400;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        // ---------------------------------------------------------------- saves
        readonly Dictionary<int, Texture2D> thumbs = new Dictionary<int, Texture2D>();

        public void InvalidateThumb(int slot)
        {
            if (thumbs.TryGetValue(slot, out var t) && t != null) Destroy(t);
            thumbs.Remove(slot);
            modalSection?.Invalidate();
        }

        Texture2D Thumb(int slot)
        {
            if (thumbs.TryGetValue(slot, out var tex)) return tex;
            tex = null;
            try
            {
                var p = GameManager.ThumbPath(slot);
                if (File.Exists(p)) { tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(p)); }
            }
            catch { tex = null; }
            thumbs[slot] = tex;
            return tex;
        }

        void BuildSaveLoad(VisualElement r, Scope s)
        {
            var p = ModalPanel(r, gm.inGame ? "Save / Load" : "Load Game", 680, 560);
            foreach (int slot in new[] { 1, 2, 3, 0 })
            {
                int sl = slot;
                var row = K.Row(p, "card");
                K.Text(row, slot == 0 ? "Auto" : $"Slot {slot}", "bold").style.width = 52;
                var img = new Image { image = Thumb(slot), scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 128; img.style.height = 72;
                img.style.backgroundColor = new Color(0, 0, 0, 0.4f);
                img.style.marginRight = 8;
                row.Add(img);
                string meta = GameManager.SlotMeta(slot);
                var ml = K.Text(row, string.IsNullOrEmpty(meta) ? (slot == 0 ? "(no autosave yet - saved every month)" : "(empty)") : meta, "small", "wrap");
                ml.style.flexShrink = 1;
                ml.style.width = 280;
                K.Flex(row);
                if (gm.inGame && slot != 0)
                    K.Small(row, "Save", () => { if (GameManager.HasSave(sl)) Confirm($"Overwrite slot {sl}?", () => gm.Save(sl)); else gm.Save(sl); });
                var load = K.Small(row, "Load", () => { gm.Load(sl); OpenWindow(Window.None); });
                load.SetEnabled(GameManager.HasSave(slot));
            }
        }

        void BuildAchievements(VisualElement r)
        {
            var p = ModalPanel(r, "Achievements (all games)", 640, 620);
            var sv = K.Scroll(p);
            foreach (var a in Simulation.Achievements)
            {
                bool ever = Simulation.UnlockedEver(a.id);
                var row = K.Row(sv.contentContainer, "card");
                K.Text(row, ever ? "[*]" : "[ ]", ever ? "good" : "dim").style.width = 30;
                K.Text(row, a.name, ever ? "bold" : null).style.width = 170;
                K.Text(row, a.desc, "dim");
            }
            K.Text(sv.contentContainer, "SCENARIOS COMPLETED", "section-title");
            foreach (var sc in Simulation.Scenarios.Where(x => x.days > 0))
            {
                bool done = Simulation.UnlockedEver("scenario_" + sc.id);
                K.Text(sv.contentContainer, (done ? "[*] " : "[ ] ") + sc.name, done ? "good" : "dim");
            }
        }

        // ---------------------------------------------------------------- help
        void BuildHelp(VisualElement r)
        {
            var p = ModalPanel(r, "How to play", 780, 680);
            var sv = K.Scroll(p);
            foreach (var para in HelpText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var lines = para.Split('\n');
                K.Text(sv.contentContainer, lines[0], "section-title");
                K.Text(sv.contentContainer, string.Join("\n", lines.Skip(1)), "wrap");
            }
        }

        const string HelpText =
@"GOAL
Run a profitable, reliable data center. Sign customer contracts, provision racks and servers for them, keep everything powered, cooled and connected, and grow your reputation to unlock better hardware. Every new game uses a seed that changes the city, the lot and the stream of customers. Pick Sandbox for creative mode with unlimited money.

CAMERA
WASD / arrows: pan. Middle mouse drag: pan. Scroll: zoom. Q / E: rotate. Home / End: tilt. PageUp / PageDown: change floor.

BUILDING
Foundation [F]: drag on clear land you own. Rooms & Infra: paint Server Hall, Network Room, Power Room, Office, Corridor, plus cable trays and raised floors. Equipment [B]: place gear in the right room (transformers, generators and solar go outdoors). Demolish [X]: removes objects first, then trays/raised floor, rooms, foundation, then trees/rocks.

FLOORS
Up to 4 floors. Go up a floor and build floor slabs over the storey below. Place an Elevator & Cable Riser at the same spot on each floor to link them: staff ride it, and cross-floor network cables run up it. Upper floors never flood.

POWER
Usable supply = min(transformers, switchgear). If load exceeds supply, the newest racks shut down. Short outages need UPS >= load; long outages need UPS and generators. Battery storage charges on cheap night power and discharges at the evening peak.

COOLING & AIRFLOW
Each connected Server Hall on a floor is a cooling zone. Face racks front-to-front across a cold aisle and back-to-back across hot aisles. Above ~30C hardware fails more often; at 45C racks shut down. Use the Heat and Airflow overlays.

NETWORK
Each rack needs a ToR switch and a cable to a Core Switch in a Network Room. Runs longer than 14 tiles need cable trays. Total customer bandwidth must fit within your ISP uplinks.

CONTRACTS
Accept offers in the Market, assign racks, and provision hardware until the requirement bars fill. Uptime is checked monthly against the SLA. Redundant contracts need dual PDU, two ToR switches and two core uplinks; geo-redundant contracts need racks in two sites. Resolve support tickets to keep customers happy.

STAFF
Hire technicians, electricians, network engineers and security on day or night shifts. Each desk seats one person per shift. Staff walk to jobs, so keep aisles clear.

SITES & PROGRESSION
From reputation level 4 you can open data centers in other cities. Research better hardware, earn achievements and take the company public.

KEYS
Space: pause. 1-4: speed. Ctrl+Z: undo. O: overlays. F5/F9: quick save/load. Esc: cancel/menu. All keys can be rebound in Settings.";

        // ---------------------------------------------------------------- interactive tutorial
        class TutStep
        {
            public string title, text, highlight;
            public Func<bool> done;
            public Func<RectInt?> hint;
            public bool manual;
        }

        List<TutStep> tutSteps;

        RectInt Around(int dx, int dy, int w, int h)
        {
            int cx = (S.ownX0 + S.ownX1) / 2, cy = (S.ownY0 + S.ownY1) / 2;
            return new RectInt(cx + dx, cy + dy, w, h);
        }

        int CountRoom(RoomType t) => S.rooms.Count(v => v == (int)t);
        bool HasKind(FacilityKind k) => S.objects.Any(o => Catalog.Facility(o.def).kind == k);

        List<TutStep> BuildTutorialSteps() => new List<TutStep>
        {
            new TutStep { title = "Welcome to Uptime", manual = true, text = "You run a data center. Customers rent capacity, and you keep their racks powered, cooled and connected. This tutorial walks you through your first facility. Press Space any time to pause." },
            new TutStep { title = "Moving around", manual = true, text = "Pan with WASD or the middle mouse button, zoom with the scroll wheel, rotate with Q/E. Yellow outlines in the world show where to build next." },
            new TutStep { title = "Lay a foundation", highlight = "Foundation", hint = () => Around(-6, -4, 13, 9), text = "Press F (Foundation) and drag a rectangle over the yellow outline. This is your building.", done = () => S.rooms.Count(v => v != 0) >= 60 },
            new TutStep { title = "Paint a Server Hall", highlight = "Rooms", hint = () => Around(-6, -1, 8, 6), text = "Open Rooms & Infra, choose Server Hall and drag over the outlined area. Racks and cooling go here.", done = () => CountRoom(RoomType.ServerHall) >= 24 },
            new TutStep { title = "Network and Power rooms", highlight = "Rooms", hint = () => Around(3, -4, 4, 9), text = "Paint a Network Room and a Power Room inside the outline. Each needs at least 4 tiles.", done = () => CountRoom(RoomType.Network) >= 4 && CountRoom(RoomType.Power) >= 4 },
            new TutStep { title = "An office for your staff", highlight = "Rooms", hint = () => Around(-6, -4, 5, 2), text = "Paint a small Office. Your technicians will work from here.", done = () => CountRoom(RoomType.Office) >= 2 },
            new TutStep { title = "Connect to the grid", highlight = "Equipment", hint = () => Around(7, -3, 1, 1), text = "Open Equipment > Power. Place a Utility Transformer OUTSIDE the building (outlined), then a Switchgear Panel inside the Power Room.", done = () => HasKind(FacilityKind.Transformer) && HasKind(FacilityKind.Switchgear) },
            new TutStep { title = "Bring in the internet", highlight = "Equipment", text = "Equipment > Network: place a Core Switch and an ISP Fiber line inside the Network Room.", done = () => HasKind(FacilityKind.CoreSwitch) && HasKind(FacilityKind.Isp) },
            new TutStep { title = "Racks and cooling", highlight = "Equipment", text = "Equipment > Racks: place a 42U Rack in the Server Hall. Then Equipment > Cooling: place a CRAC unit in the same hall. Press R to rotate before placing.", done = () => HasKind(FacilityKind.Rack) && HasKind(FacilityKind.Cooling) },
            new TutStep { title = "Fill the rack", text = "Click the rack. In the inspector, add a ToR Switch, then some Basic 1U Servers (+1 or Fill). Make sure it is cabled to the core switch (Auto-cable). The rack light turns blue when online.", done = () => Sim.racks.Values.Any(i => i.online) },
            new TutStep { title = "Sign a customer", highlight = "Market", text = "Press M for the Market. Pick an offer your rack can satisfy (the estimate line helps) and Accept it.", done = () => S.contracts.Count > 0 },
            new TutStep { title = "Assign the rack", highlight = "Contracts", text = "Select your rack and click the customer under 'Assign to contract' (or use the Contracts window). Add hardware until the requirement bars are full.", done = () => S.objects.Any(o => o.contractId > 0) },
            new TutStep { title = "Hire a technician", highlight = "Staff", text = "Place a Technician Desk in the Office (Equipment > Other), then open Staff & Land > Hire and hire a day-shift technician. Night shifts need their own staff!", done = () => S.staff.Count > 0 },
            new TutStep { title = "Go live", text = "Speed up time (keys 1-4). The contract goes live once its requirements are met and starts paying monthly. Keep uptime above the SLA!", done = () => S.contracts.Any(c => c.status == ContractStatus.Active || c.status == ContractStatus.Completed) },
            new TutStep { title = "You're running a data center!", manual = true, text = "Next: research better hardware (T), watch heat with the overlays (O), add a generator and UPS for outages, cable trays for long runs, build upward with floors (PageUp), and expand to new cities from the Sites button at level 4. Good luck!" },
        };

        void BuildTutorial(VisualElement r, Scope s)
        {
            tutSteps ??= BuildTutorialSteps();
            if (S.tutorialStep >= tutSteps.Count) { S.tutorial = false; gm.view.HideHint(); return; }
            var step = tutSteps[S.tutorialStep];
            tutorialHighlight = step.highlight;
            var hint = step.hint?.Invoke();
            if (hint.HasValue) gm.view.ShowHint(hint.Value); else gm.view.HideHint();

            var p = K.Panel(r);
            var head = K.Row(p);
            K.Text(head, $"Tutorial {S.tutorialStep + 1}/{tutSteps.Count}", "dim");
            K.Flex(head);
            K.Small(head, "Skip tutorial", () => { S.tutorial = false; gm.view.HideHint(); });
            K.Text(p, step.title, "bold");
            K.Text(p, step.text, "wrap");
            if (step.manual)
            {
                var row = K.Row(p);
                K.Flex(row);
                K.Btn(row, S.tutorialStep == tutSteps.Count - 1 ? "Finish" : "Next", () => S.tutorialStep++, "on").style.width = 100;
            }
            else
            {
                K.Text(p, "Waiting for you to complete this step...", "dim");
                s.Add(() =>
                {
                    if (step.done != null && step.done() && S.tutorialStep < tutSteps.Count && tutSteps[S.tutorialStep] == step)
                    {
                        S.tutorialStep++;
                        gm.audio?.Play("good", 0.4f);
                    }
                });
            }
        }
    }
}
