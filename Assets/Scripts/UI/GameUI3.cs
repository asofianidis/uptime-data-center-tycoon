using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>UI for sites, infrastructure tools, battery storage, save thumbnails and the interactive tutorial.</summary>
    public partial class GameUI
    {
        public bool tutorialChoice = true;

        // ---------------------------------------------------------------- sites
        void SiteButton()
        {
            var co = Sim.company;
            string label = co.MultiSite ? $"{S.siteName} ({S.activeSite + 1}/{co.sims.Count})" : "Sites";
            if (GUILayout.Button(label, window == Window.Sites ? btnOn : btn, GUILayout.Width(co.MultiSite ? 150 : 70), GUILayout.Height(28))) ToggleWindow(Window.Sites);
        }

        void SitesWindow()
        {
            var co = Sim.company;
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            GUILayout.Label("YOUR SITES", lblDim);
            for (int i = 0; i < co.sims.Count; i++)
            {
                var s = co.sims[i];
                var d = S.sites[i];
                int racks = s.Racks.Count;
                int down = s.racks.Values.Count(x => !x.online && !x.empty);
                float maxT = s.zones.Count > 0 ? s.zones.Max(z => z.temp) : 0;
                int broken = d.objects.Count(o => o.failed || o.pduTripped) + d.objects.Sum(o => o.devices.Count(x => x.failed));
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.BeginVertical(GUILayout.Width(220));
                GUILayout.Label((i == S.activeSite ? "> " : "") + d.name, lblBold);
                GUILayout.Label($"{d.city.name} - {d.city.climate} - ${d.city.powerPrice:0.000}/kWh", lblDim);
                GUILayout.EndVertical();
                GUILayout.Label($"{racks} racks\n{s.loadKW:0}/{s.availableKW:0} kW", lblSmall, GUILayout.Width(110));
                GUILayout.Label($"Hot {maxT:0.0}C\n{d.staff.Count} staff", lblSmall, GUILayout.Width(90));
                GUILayout.Label(down > 0 || broken > 0 ? $"{down} racks down\n{broken} broken" : "All good", down > 0 || broken > 0 ? lblWarn : lblGood, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                int idx = i;
                if (i != S.activeSite && Button("Go to", true, GUILayout.Width(80))) { window = Window.None; gm.SwitchSite(idx); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); return; }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("EXPAND TO A NEW CITY", lblDim);
            GUILayout.Label($"Each site has its own climate, power prices, grid, land and staff. Contracts can use racks in any site, and geo-redundant customers require racks in at least two. Unlocks at reputation level {Company.MinLevelForSites}.", lblDim);
            foreach (var city in co.SiteCandidates())
            {
                float cost = co.SiteCost(city);
                string err = co.CanOpenSite(city);
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.BeginVertical();
                GUILayout.Label($"{city.name}  ({city.climate})", lblBold);
                GUILayout.Label($"Cooling x{city.coolingFactor:0.00}   Power ${city.powerPrice:0.000}/kWh{(city.carbonTax > 0 ? $" +${city.carbonTax:0.000} carbon" : "")}   Land x{city.landPrice:0.00}   Risk x{city.disasterRate:0.00}   Demand x{city.demandFactor:0.00}", lblSmall);
                GUILayout.Label(city.blurb, lblDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                var c = city;
                if (Button(err == null ? $"Open site {Money(cost)}" : err, err == null, GUILayout.Width(210), GUILayout.Height(40)))
                    Confirm($"Buy land and open a data center in {city.name} for {Money(cost)}?", () => { window = Window.None; gm.OpenSite(c); });
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- infrastructure tools in the room menu
        void InfraTools()
        {
            GUILayout.Space(6);
            GUILayout.Label("INFRASTRUCTURE", lblDim);
            if (GUILayout.Button($"Cable Trays (${Simulation.TrayCost:0}/tile)", gm.tool == Tool.Tray ? btnOn : btn)) gm.SetTool(Tool.Tray);
            if (GUILayout.Button($"Raised Floor (${Simulation.RaisedCost:0}/tile)", gm.tool == Tool.Raised ? btnOn : btn)) gm.SetTool(Tool.Raised);
            if (gm.tool == Tool.Tray) GUILayout.Label($"Overhead trays halve cabling cost, keep airflow clean, and are required for runs longer than {Simulation.MaxLooseCable} tiles. A rack is routed when a tray touches it and the same tray run reaches the core switch.", lblDim);
            if (gm.tool == Tool.Raised) GUILayout.Label("Raised floors in server halls protect hardware from floods and add up to +10% cooling.", lblDim);
        }

        // ---------------------------------------------------------------- staff shift & fatigue row
        void StaffShiftCells(StaffMember m)
        {
            bool on = Sim.OnShift(m);
            GUILayout.Label(m.shift == "Night" ? "Night" : "Day", on ? lblSmall : lblDim, GUILayout.Width(40));
            var r = GUILayoutUtility.GetRect(60, 12, GUILayout.Width(60));
            r.y += 4;
            Bar(r, m.fatigue / 100f, m.fatigue > 60 ? new Color(1f, 0.4f, 0.3f) : new Color(0.6f, 0.6f, 0.7f));
            var mm = m;
            if (SmallButton(m.shift == "Night" ? "To day" : "To night")) Sim.SetShift(mm, m.shift == "Night" ? "Day" : "Night");
        }

        string StaffState(StaffMember m)
        {
            if (!Sim.HasDesk(m)) return "No desk on this shift!";
            if (m.state == "off" || !Sim.OnShift(m)) return "Off shift";
            if (m.role == "Security") return "On patrol";
            string tired = m.fatigue > 60 ? " (tired)" : "";
            switch (m.state)
            {
                case "idle": return "Idle at desk" + tired;
                case "walking": return $"Walking to #{m.taskObj}" + tired;
                case "working": return (m.taskTicket >= 0 ? $"Ticket at #{m.taskObj}" : $"Repairing #{m.taskObj}") + tired;
                default: return "Returning" + tired;
            }
        }

        void HireButtons(Candidate c)
        {
            float fee = Sim.HireFee(c);
            bool canDay = Sim.ShiftCount("Day") < Sim.deskCount, canNight = Sim.ShiftCount("Night") < Sim.deskCount;
            if (SmallButton($"Hire day ({Money(fee)})", canDay && S.money >= fee)) { var e = Sim.HireCandidate(c, "Day"); if (e != null) Toast(e, 2); }
            if (SmallButton($"Hire night +15%", canNight && S.money >= fee)) { var e = Sim.HireCandidate(c, "Night"); if (e != null) Toast(e, 2); }
        }

        // ---------------------------------------------------------------- battery
        void BatteryInfo(PlacedObject o, FacilityDef d)
        {
            GUILayout.Label($"Charge: {o.charge:0}/{d.capacityKWh:0} kWh   Rate: {d.supplyKW:0} kW", lbl);
            LayoutBar(o.charge / Mathf.Max(1, d.capacityKWh), new Color(0.3f, 0.9f, 0.5f), null, 10);
            string mode = Sim.outageActive ? "Backing up the grid" : Sim.TimeOfDayPriceMul < 1f ? "Charging (cheap night power)" : Sim.TimeOfDayPriceMul > 1f ? "Discharging (evening peak)" : "Holding";
            GUILayout.Label(mode, lblDim);
            GUILayout.Label($"Site: charging {Sim.batteryChargeKW:0} kW, discharging {Sim.batteryDischargeKW:0} kW, {Sim.batteryChargeKWh:0}/{Sim.batteryCapKWh:0} kWh stored", lblDim);
        }

        // ---------------------------------------------------------------- save thumbnails
        readonly Dictionary<int, Texture2D> thumbs = new Dictionary<int, Texture2D>();

        public void InvalidateThumb(int slot)
        {
            if (thumbs.TryGetValue(slot, out var t) && t != null) Destroy(t);
            thumbs.Remove(slot);
        }

        void Thumb(int slot)
        {
            if (!thumbs.TryGetValue(slot, out var tex))
            {
                tex = null;
                try
                {
                    var p = GameManager.ThumbPath(slot);
                    if (File.Exists(p)) { tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(p)); }
                }
                catch { tex = null; }
                thumbs[slot] = tex;
            }
            var r = GUILayoutUtility.GetRect(112, 63, GUILayout.Width(112), GUILayout.Height(63));
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            else { var old = GUI.color; GUI.color = new Color(0, 0, 0, 0.4f); GUI.DrawTexture(r, white); GUI.color = old; }
        }

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

        List<TutStep> BuildTutorial()
        {
            return new List<TutStep>
            {
                new TutStep { title = "Welcome to Uptime", manual = true,
                    text = "You run a data center. Customers rent capacity, and you keep their racks powered, cooled and connected. This tutorial walks you through your first facility. Time is paused-friendly: press Space any time." },
                new TutStep { title = "Moving around", manual = true,
                    text = "Pan with WASD or the middle mouse button, zoom with the scroll wheel, rotate with Q/E. The yellow outlines in the world show where to build next." },
                new TutStep { title = "Lay a foundation", highlight = "Foundation", hint = () => Around(-6, -4, 13, 9),
                    text = "Press F (Foundation) and drag a rectangle over the yellow outline. This is your building.", done = () => S.rooms.Count(v => v != 0) >= 60 },
                new TutStep { title = "Paint a Server Hall", highlight = "Rooms", hint = () => Around(-6, -1, 8, 6),
                    text = "Open Rooms, choose Server Hall and drag over the outlined area. Racks and cooling go here.", done = () => CountRoom(RoomType.ServerHall) >= 24 },
                new TutStep { title = "Network and Power rooms", highlight = "Rooms", hint = () => Around(3, -4, 4, 9),
                    text = "Paint a Network Room (top part of the outline) and a Power Room (bottom part). Each needs at least 4 tiles.", done = () => CountRoom(RoomType.Network) >= 4 && CountRoom(RoomType.Power) >= 4 },
                new TutStep { title = "An office for your staff", highlight = "Rooms", hint = () => Around(-6, -4, 5, 2),
                    text = "Paint a small Office. Your technicians will work from here.", done = () => CountRoom(RoomType.Office) >= 2 },
                new TutStep { title = "Connect to the grid", highlight = "Equipment", hint = () => Around(7, -3, 1, 1),
                    text = "Open Equipment > Power. Place a Utility Transformer OUTSIDE the building (outlined), then a Switchgear Panel inside the Power Room.", done = () => HasKind(FacilityKind.Transformer) && HasKind(FacilityKind.Switchgear) },
                new TutStep { title = "Bring in the internet", highlight = "Equipment",
                    text = "Equipment > Network: place a Core Switch and an ISP Fiber line inside the Network Room.", done = () => HasKind(FacilityKind.CoreSwitch) && HasKind(FacilityKind.Isp) },
                new TutStep { title = "Racks and cooling", highlight = "Equipment",
                    text = "Equipment > Racks: place a 42U Rack in the Server Hall. Then Equipment > Cooling: place a CRAC unit in the same hall. Press R to rotate before placing.", done = () => HasKind(FacilityKind.Rack) && HasKind(FacilityKind.Cooling) },
                new TutStep { title = "Fill the rack", highlight = null,
                    text = "Click the rack. In the inspector, add a ToR Switch, then some Basic 1U Servers (+1 or Fill). Make sure it is cabled to the core switch (Auto-cable). The rack light turns blue when online.", done = () => Sim.racks.Values.Any(i => i.online) },
                new TutStep { title = "Sign a customer", highlight = "Market",
                    text = "Press M for the Market. Pick an offer your rack can satisfy (the estimate line helps) and Accept it.", done = () => S.contracts.Count > 0 },
                new TutStep { title = "Assign the rack", highlight = "Contracts",
                    text = "Select your rack and click the customer under 'Assign to contract' (or use the Contracts window). Add hardware until the requirement bars are full.", done = () => S.objects.Any(o => o.contractId > 0) },
                new TutStep { title = "Hire a technician", highlight = "Staff",
                    text = "Place a Technician Desk in the Office (Equipment > Office), then open Staff & Land > Hire. Hire a day-shift technician. Night shifts need their own staff!", done = () => S.staff.Count > 0 },
                new TutStep { title = "Go live", highlight = null,
                    text = "Speed up time (keys 1-4). The contract goes live once its requirements are met and starts paying monthly. Keep uptime above the SLA!", done = () => S.contracts.Any(c => c.status == ContractStatus.Active || c.status == ContractStatus.Completed) },
                new TutStep { title = "You're running a data center!", manual = true,
                    text = "Next: research better hardware (T), watch heat with the overlays (O), add a generator and UPS for outages, cable trays for long runs, and expand to new cities from the Sites button once you reach level 4. Good luck!" },
            };
        }

        void DrawTutorial()
        {
            tutSteps ??= BuildTutorial();
            if (S.tutorialStep >= tutSteps.Count) { S.tutorial = false; gm.view.HideHint(); return; }
            var step = tutSteps[S.tutorialStep];
            if (!step.manual && step.done != null && step.done())
            {
                S.tutorialStep++;
                gm.audio?.Play("good", 0.4f);
                return;
            }
            tutorialHighlight = step.highlight;
            var hint = step.hint?.Invoke();
            if (hint.HasValue) gm.view.ShowHint(hint.Value); else gm.view.HideHint();

            float w = 360;
            float h = lblWrap.CalcHeight(new GUIContent(step.text), w - 24) + 90;
            var r = Reg(new Rect(6, VH - h - 6, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tutorial {S.tutorialStep + 1}/{tutSteps.Count}", lblDim);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Skip tutorial", btnSmall)) { S.tutorial = false; gm.view.HideHint(); }
            GUILayout.EndHorizontal();
            GUILayout.Label(step.title, lblBold);
            GUILayout.Label(step.text, lblWrap);
            GUILayout.FlexibleSpace();
            if (step.manual)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (Button(S.tutorialStep == tutSteps.Count - 1 ? "Finish" : "Next", true, GUILayout.Width(100))) S.tutorialStep++;
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label("Waiting for you to complete this step...", new GUIStyle(lblDim) { fontStyle = FontStyle.Italic });
            GUILayout.EndArea();
        }
    }
}
