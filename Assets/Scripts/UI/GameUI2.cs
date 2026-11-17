using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>UI for v2 systems: staff, goals, dashboard, settings, save slots, templates, redundancy, tickets, negotiation.</summary>
    public partial class GameUI
    {
        string tutorialHighlight;
        string rebinding;
        string templateName = "My rack";
        int staffTab;
        string scenarioId = "sandbox";

        // ---------------------------------------------------------------- toolbar (with tutorial highlights)
        bool TB(string label, bool on, string key = null)
        {
            bool r = GUILayout.Button(label, on ? btnOn : btn);
            if (key != null && key == tutorialHighlight && Event.current.type == EventType.Repaint)
            {
                var rect = GUILayoutUtility.GetLastRect();
                float a = 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
                var old = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.2f, a);
                GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, 2), white);
                GUI.DrawTexture(new Rect(rect.x - 2, rect.yMax, rect.width + 4, 2), white);
                GUI.DrawTexture(new Rect(rect.x - 2, rect.y, 2, rect.height), white);
                GUI.DrawTexture(new Rect(rect.xMax, rect.y, 2, rect.height), white);
                GUI.color = old;
            }
            return r;
        }

        void DrawToolbar2()
        {
            var r = Reg(new Rect(6, 52, 168, 650));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("BUILD", lblDim);
            if (TB("Select  [Esc]", gm.tool == Tool.Select)) { gm.SetTool(Tool.Select); buildMenuOpen = roomMenuOpen = false; }
            if (TB($"Foundation  [{Settings.KeyName("Foundation")}]", gm.tool == Tool.Foundation, "Foundation")) { gm.SetTool(Tool.Foundation); buildMenuOpen = roomMenuOpen = false; }
            if (TB("Rooms", gm.tool == Tool.Room || roomMenuOpen, "Rooms")) { roomMenuOpen = !roomMenuOpen; buildMenuOpen = false; if (roomMenuOpen) { gm.SetTool(Tool.Room); window = Window.None; } }
            if (TB($"Equipment  [{Settings.KeyName("Equipment")}]", buildMenuOpen || gm.tool == Tool.Place, "Equipment")) ToggleBuildMenu();
            if (TB($"Demolish  [{Settings.KeyName("Demolish")}]", gm.tool == Tool.Demolish)) { gm.SetTool(Tool.Demolish); buildMenuOpen = roomMenuOpen = false; }
            bool prev = GUI.enabled;
            GUI.enabled = gm.UndoCount > 0;
            if (TB($"Undo ({gm.UndoCount})  [Ctrl+{Settings.KeyName("Undo")}]", false)) gm.Undo();
            GUI.enabled = prev;
            GUILayout.Space(6);
            GUILayout.Label("MANAGE", lblDim);
            if (TB($"Market ({S.offers.Count})  [{Settings.KeyName("Market")}]", window == Window.Market, "Market")) ToggleWindow(Window.Market);
            int needs = S.contracts.Count(c => (c.status == ContractStatus.Setup) || (c.status == ContractStatus.Active && !c.meeting));
            int tix = S.tickets.Count;
            string cl = "Contracts" + (needs > 0 ? $" (!{needs})" : "") + (tix > 0 ? $" T{tix}" : "");
            if (TB(cl, window == Window.Contracts, "Contracts")) ToggleWindow(Window.Contracts);
            if (TB($"Research  [{Settings.KeyName("Research")}]", window == Window.Research)) ToggleWindow(Window.Research);
            if (TB($"Staff & Land  [{Settings.KeyName("Staff")}]", window == Window.Staff, "Staff")) ToggleWindow(Window.Staff);
            if (TB("Finance", window == Window.Finance)) ToggleWindow(Window.Finance);
            if (TB($"Dashboard  [{Settings.KeyName("Dashboard")}]", window == Window.Dashboard)) ToggleWindow(Window.Dashboard);
            if (TB($"Goals  [{Settings.KeyName("Goals")}]", window == Window.Goals)) ToggleWindow(Window.Goals);
            if (TB("Event Log", window == Window.Log)) ToggleWindow(Window.Log);
            GUILayout.Space(6);
            GUILayout.Label("VIEW", lblDim);
            if (TB($"Overlay: {gm.view.overlay}  [{Settings.KeyName("CycleOverlay")}]", gm.view.overlay != Overlay.None)) gm.view.CycleOverlay();
            if (TB("Network cables", gm.view.showCables)) gm.view.showCables = !gm.view.showCables;
            if (TB("Auto-cable new racks", autoConnectNewRacks)) autoConnectNewRacks = !autoConnectNewRacks;
            if (TB("Objectives", showObjectives)) showObjectives = !showObjectives;
            GUILayout.FlexibleSpace();
            if (Button("Menu")) OpenWindow(Window.Menu);
            GUILayout.EndArea();
        }

        void DrawOverlayLegend()
        {
            if (gm.view.overlay == Overlay.None) return;
            string text = $"Overlay: {gm.view.overlay}  -  {WorldView.OverlayLegend(gm.view.overlay)}";
            var size = tooltipStyle.CalcSize(new GUIContent(text));
            float w = Mathf.Min(size.x + 16, VW - 600);
            var r = Reg(new Rect((VW - w) / 2, 52, w, size.y));
            GUI.Label(r, text, tooltipStyle);
        }

        // ---------------------------------------------------------------- inspector additions
        void RackExtras(PlacedObject o, RackInfo info)
        {
            GUILayout.Space(6);
            GUILayout.Label("AIRFLOW", lblDim);
            var st = info.airflow < 1f ? lblGood : info.airflow > 1.001f ? lblWarn : lbl;
            GUILayout.Label($"{info.airflowNote}  (heat x{info.airflow:0.00})", st);
            GUILayout.Label("Tip: face rows front-to-front across a 1-tile cold aisle, back-to-back across hot aisles. Rotate with [" + Settings.KeyName("Rotate") + "] when placing.", lblDim);
            if (GUILayout.Button("Rotate rack 90 deg", btnSmall)) RotateRack(o);

            GUILayout.Space(6);
            GUILayout.Label("REDUNDANCY", lblDim);
            string gap = Sim.RedundancyGap(o);
            if (gap == null) GUILayout.Label("Fully redundant (A+B power, dual ToR, dual uplink)", lblGood);
            else GUILayout.Label(gap, lblDim);
            if (info.degraded) GUILayout.Label("Running degraded on one path - repair soon!", lblWarn);
            if (o.pduTripped) GUILayout.Label(o.dualPdu ? "PDU A tripped, running on B" : "PDU tripped!", o.dualPdu ? lblWarn : lblBad);
            GUILayout.BeginHorizontal();
            if (!o.dualPdu && SmallButton($"Dual PDU ({Money(Simulation.DualPduCost)})", S.money >= Simulation.DualPduCost)) { var e = Sim.BuyDualPdu(o); if (e != null) Toast(e, 2); }
            if (gap != null && SmallButton("Make redundant")) { var e = Sim.MakeRedundant(o); if (e != null) Toast(e, 2); }
            GUILayout.EndHorizontal();
            PlacedObject core2 = o.linkCore2 > 0 && Sim.objById.TryGetValue(o.linkCore2, out var c2) ? c2 : null;
            GUILayout.BeginHorizontal();
            if (core2 != null)
            {
                GUILayout.Label($"Uplink B: Core #{core2.id}", lblSmall);
                if (SmallButton("Disconnect B")) Sim.DisconnectSecondary(o);
            }
            else
            {
                if (SmallButton("Auto-cable uplink B", o.linkCore > 0)) { var e = Sim.AutoConnectSecondary(o); if (e != null) Toast(e, 2); }
                if (SmallButton("Pick core for B...", o.linkCore > 0)) gm.BeginConnect(o, true);
            }
            GUILayout.EndHorizontal();
            if (!Sim.IsReachable(o)) GUILayout.Label("No clear walking access: technicians must squeeze past equipment (repairs take 50% longer).", lblWarn);
        }

        void RotateRack(PlacedObject o)
        {
            Sim.RotateRack(o);
            gm.view.ForceRebuildObject(o.id);
        }

        void DeviceRowExtras(PlacedObject o, RackDevice dev)
        {
            float age = Simulation.AgeYears(dev);
            if (age >= 0.1f)
                GUILayout.Label($"{age:0.0}y", age > 3f ? new GUIStyle(lblSmall) { normal = { textColor = new Color(1f, 0.45f, 0.4f) } } : age > 1.5f ? new GUIStyle(lblSmall) { normal = { textColor = new Color(1f, 0.8f, 0.35f) } } : lblDim, GUILayout.Width(34));
            if (age > 1.5f)
            {
                float cost = Sim.DeviceCost(Catalog.Device(dev.def)) - Sim.ResaleValue(dev);
                if (SmallButton($"Refresh {Money(cost)}", S.money >= cost)) { var e = Sim.RefreshDevice(o, dev); if (e != null) Toast(e, 2); }
            }
        }

        void TemplatesSection(List<PlacedObject> racks)
        {
            GUILayout.Space(6);
            GUILayout.Label("RACK TEMPLATES", lblDim);
            var src = racks.FirstOrDefault(r => r.devices.Count > 0);
            if (src != null)
            {
                GUILayout.BeginHorizontal();
                templateName = GUILayout.TextField(templateName, 24, GUILayout.Width(170));
                if (SmallButton($"Save #{src.id} as template")) { Templates.All.Add(Templates.FromRack(src, templateName)); Templates.Save(); Toast($"Template \"{templateName}\" saved.", 1); }
                GUILayout.EndHorizontal();
            }
            var empties = racks.Where(r => r.devices.Count == 0).ToList();
            foreach (var t in Templates.All.ToList())
            {
                float cost = Templates.Cost(t, Sim) * Mathf.Max(1, empties.Count);
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.Label($"{t.name}  ({t.devices.Count} devices{(t.dualPdu ? ", dual PDU" : "")})", lblSmall, GUILayout.Width(210));
                GUILayout.FlexibleSpace();
                if (SmallButton($"Apply {Money(cost)}", empties.Count > 0 && S.money >= cost))
                {
                    int n = 0; string err = null;
                    foreach (var r in empties) n += Templates.Apply(t, r, Sim, out err);
                    Toast(err != null ? $"Installed {n} devices. Stopped: {err}" : $"Installed {n} devices from \"{t.name}\".", err != null ? 2 : 1);
                }
                if (SmallButton("x")) { Templates.All.Remove(t); Templates.Save(); }
                GUILayout.EndHorizontal();
            }
            if (empties.Count == 0) GUILayout.Label("Templates apply to empty racks in the selection.", lblDim);
        }

        void FacilityExtras(PlacedObject o, FacilityDef d)
        {
            switch (d.kind)
            {
                case FacilityKind.Solar:
                    GUILayout.Label($"Peak output: {d.supplyKW:0} kW   Now: {(Sim.solarCapKW > 0 ? Sim.solarKW * d.supplyKW / Sim.solarCapKW : 0):0.0} kW", lbl);
                    GUILayout.Label($"Weather: {S.weather}   Sun factor x{S.city.solarFactor:0.00}   Last month solar share: {S.lastSolarShare * 100:0}%", lblDim);
                    break;
                case FacilityKind.Suppression:
                {
                    int zi = Sim.ZoneAt(o);
                    GUILayout.Label(zi >= 0 ? "Protecting this server hall from fire." : "Not in a server hall!", zi >= 0 ? lblGood : lblBad);
                    GUILayout.Label($"Fires extinguished so far: {S.firesFought}", lblDim);
                    break;
                }
            }
            if (d.kind != FacilityKind.Rack && !Sim.IsReachable(o)) GUILayout.Label("No clear walking access: repairs take 50% longer.", lblWarn);
        }

        // ---------------------------------------------------------------- contracts additions
        void SatisfactionBar(Contract c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Satisfaction", lblSmall, GUILayout.Width(80));
            Color col = c.satisfaction >= 70 ? new Color(0.3f, 0.85f, 0.4f) : c.satisfaction >= 40 ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
            LayoutBar(c.satisfaction / 100f, col, $"{c.satisfaction:0}/100" + (c.satisfaction < 35 ? "  - at risk of leaving for a rival!" : c.satisfaction > 75 ? "  - may refer new customers" : ""), 16);
            GUILayout.EndHorizontal();
            if (c.redundant) GUILayout.Label("REDUNDANT: every rack needs dual PDU, 2 ToR switches and 2 core uplinks.", new GUIStyle(lblSmall) { normal = { textColor = new Color(0.4f, 0.95f, 1f) } });
            foreach (var t in S.tickets.Where(x => x.contractId == c.id).ToList())
            {
                GUILayout.BeginHorizontal(panelDark);
                var who = t.assigned >= 0 ? S.staff.FirstOrDefault(m => m.id == t.assigned) : null;
                GUILayout.Label($"TICKET: {t.text} (rack #{t.rackId}) - due in {HoursToText(t.deadlineHour - S.hour)} - fee {Money(t.fee)}" + (who != null ? $" - {who.name} on it" : " - waiting for staff"), lblSmall);
                GUILayout.FlexibleSpace();
                float ec = Sim.ExpediteCost(t);
                if (SmallButton($"Contractor {Money(ec)}", S.money >= ec)) { var e = Sim.ExpediteTicket(t); if (e != null) Toast(e, 2); }
                GUILayout.EndHorizontal();
            }
        }

        void NegotiateButtons(Contract c)
        {
            if (c.negotiated) { GUILayout.Label("Negotiated", lblDim, GUILayout.Width(80)); return; }
            if (SmallButton("Ask +15% pay")) Toast(Sim.Negotiate(c, "pay"), 0);
            if (SmallButton("Ask +7 days")) Toast(Sim.Negotiate(c, "time"), 0);
            if (c.sla > 99f && SmallButton("Ask lower SLA")) Toast(Sim.Negotiate(c, "sla"), 0);
        }

        void RivalsStrip()
        {
            GUILayout.BeginHorizontal(panelDark);
            GUILayout.Label("Competitors:", lblDim, GUILayout.Width(90));
            foreach (var rv in S.rivals)
            {
                Swatch(Catalog.CustomerColors[rv.colorIdx], 10);
                GUILayout.Label($"{rv.name}  rep {rv.reputation:0}  {rv.customers} customers", lblSmall);
                GUILayout.Space(8);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"You: {Sim.YourCustomers} customers", lblBold);
            GUILayout.EndHorizontal();
            if (S.events.Any(e => e.type == "pricewar")) GUILayout.Label("A price war is on: new offers pay 15% less.", lblWarn);
        }

        // ---------------------------------------------------------------- staff & land
        void StaffWindow2()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Team", staffTab == 0 ? btnOn : btn, GUILayout.Width(110))) staffTab = 0;
            if (GUILayout.Button($"Hire ({S.candidates.Count})", staffTab == 1 ? btnOn : btn, GUILayout.Width(110))) staffTab = 1;
            if (GUILayout.Button("Land", staffTab == 2 ? btnOn : btn, GUILayout.Width(110))) staffTab = 2;
            GUILayout.FlexibleSpace();
            float payroll = S.staff.Sum(m => m.salary);
            GUILayout.Label($"Staff {S.staff.Count}/{Sim.deskCount} desks   Payroll {Money(payroll)}/mo", lblDim);
            GUILayout.EndHorizontal();
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            if (staffTab == 0)
            {
                if (Sim.deskCount == 0) GUILayout.Label("Paint an Office room and place desks. Each desk seats one employee.", lblWarn);
                int failed = S.objects.Count(o => o.failed || o.pduTripped) + S.objects.Sum(o => o.devices.Count(d => d.failed));
                GUILayout.Label($"Open work: {failed} broken item(s), {S.tickets.Count} ticket(s).   Tickets resolved {S.ticketsDone}, missed {S.ticketsMissed}.   Security level {Sim.SecurityLevel}.", lbl);
                if (S.staff.Count == 0) GUILayout.Label("No employees yet. Open the Hire tab.", lblDim);
                for (int i = 0; i < S.staff.Count; i++)
                {
                    var m = S.staff[i];
                    GUILayout.BeginHorizontal(panelDark);
                    Swatch(WorldView.RoleColor(m.role), 12);
                    GUILayout.Label($"{m.name}", lblBold, GUILayout.Width(150));
                    GUILayout.Label($"{Simulation.RoleName(m.role)}  skill {new string('*', m.skill)}", lblSmall, GUILayout.Width(190));
                    string state = i >= Sim.deskCount ? "No desk!" : m.role == "Security" ? "On patrol" : m.state == "idle" ? "Idle at desk" : m.state == "walking" ? $"Walking to #{m.taskObj}" : m.state == "working" ? (m.taskTicket >= 0 ? $"Handling ticket at #{m.taskObj}" : $"Repairing #{m.taskObj}") : "Returning";
                    GUILayout.Label(state, i >= Sim.deskCount ? lblWarn : lblSmall, GUILayout.Width(170));
                    GUILayout.Label($"{Money(m.salary)}/mo", lblSmall);
                    GUILayout.FlexibleSpace();
                    var mm = m;
                    if (SmallButton("Let go")) Confirm($"Let {m.name} go?", () => Sim.FireStaff(mm));
                    GUILayout.EndHorizontal();
                }
            }
            else if (staffTab == 1)
            {
                GUILayout.Label("New candidates arrive every week. Matching a job to the right role makes it 50% faster; skill speeds up all work.", lblDim);
                foreach (var role in Simulation.Roles) GUILayout.Label($"{Simulation.RoleName(role)}: {Simulation.RoleDesc(role)}", lblDim);
                GUILayout.Space(4);
                foreach (var c in S.candidates.ToList())
                {
                    GUILayout.BeginHorizontal(panelDark);
                    Swatch(WorldView.RoleColor(c.role), 12);
                    GUILayout.Label(c.name, lblBold, GUILayout.Width(150));
                    GUILayout.Label($"{Simulation.RoleName(c.role)}  skill {new string('*', c.skill)}", lblSmall, GUILayout.Width(190));
                    GUILayout.Label($"{Money(c.salary)}/mo", lblSmall, GUILayout.Width(90));
                    GUILayout.FlexibleSpace();
                    float fee = Sim.HireFee(c);
                    if (SmallButton($"Hire (fee {Money(fee)})", S.staff.Count < Sim.deskCount && S.money >= fee)) { var e = Sim.HireCandidate(c); if (e != null) Toast(e, 2); }
                    GUILayout.EndHorizontal();
                }
                if (S.staff.Count >= Sim.deskCount) GUILayout.Label("All desks are taken - place more Technician Desks in an Office.", lblWarn);
            }
            else
            {
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
            }
            GUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- goals
        void GoalsWindow()
        {
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            var sc = Simulation.Scenario(S.scenario);
            GUILayout.BeginVertical(panelDark);
            GUILayout.Label($"Scenario: {sc.name}", lblBold);
            GUILayout.Label(sc.goal, lbl);
            if (sc.id != "sandbox")
            {
                float p = Sim.ScenarioProgress(out string txt);
                LayoutBar(p, S.scenarioDone ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.4f, 0.6f, 1f), txt, 18);
                GUILayout.Label(S.scenarioDone ? "Scenario finished - endless mode." : $"Days left: {Mathf.Max(0, sc.days - S.Day)}", lblDim);
            }
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(panelDark, GUILayout.Width(330));
            GUILayout.Label($"Score: {Sim.Score:N0}", header);
            GUILayout.Label($"Lifetime revenue {Money(S.lifetimeRevenue)}", lblSmall);
            GUILayout.Label($"Contracts completed {S.contractsCompleted}, failed {S.contractsFailed}", lblSmall);
            GUILayout.Label($"Achievements {S.achievements.Count}/{Simulation.Achievements.Count}", lblSmall);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(panelDark);
            GUILayout.Label("Go public (IPO)", lblBold);
            bool can = Sim.CanIpo(out string why);
            GUILayout.Label(S.ipo ? "Your company is publicly traded." : can ? "You qualify! An IPO raises a year of revenue in cash." : why, S.ipo || can ? lblGood : lblDim);
            if (!S.ipo && Button("Ring the bell (IPO)", can)) Sim.DoIpo();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Label("ACHIEVEMENTS", lblDim);
            foreach (var a in Simulation.Achievements)
            {
                bool got = S.achievements.Contains(a.id);
                bool ever = Simulation.UnlockedEver(a.id);
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.Label(got ? "[*]" : ever ? "[o]" : "[ ]", got ? lblGood : lblDim, GUILayout.Width(30));
                GUILayout.Label(a.name, got ? lblBold : lbl, GUILayout.Width(170));
                GUILayout.Label(a.desc + (ever && !got ? "  (earned in another game)" : ""), lblDim);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- dashboard
        void LineChart(string title, System.Func<DayStat, float> f, Color c, string fmt)
        {
            var data = S.dayStats;
            GUILayout.Label(title, lblBold);
            var area = GUILayoutUtility.GetRect(300, 110, GUILayout.ExpandWidth(true));
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.35f);
            GUI.DrawTexture(area, white);
            GUI.color = old;
            if (data.Count < 2) { GUI.Label(area, "Not enough data yet - check back after a few days.", lblCenter); return; }
            float min = data.Min(f), max = data.Max(f);
            if (Mathf.Approximately(min, max)) { max += 1; min -= 1; }
            float pad = 6;
            Vector2 P(int i) => new Vector2(area.x + pad + (area.width - 2 * pad) * i / (data.Count - 1f), area.yMax - pad - (area.height - 2 * pad) * (f(data[i]) - min) / (max - min));
            GUI.color = c;
            for (int i = 1; i < data.Count; i++)
            {
                var a = P(i - 1); var b = P(i);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                for (int k = 0; k <= steps; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)steps);
                    GUI.DrawTexture(new Rect(p.x - 1, p.y - 1, 2.5f, 2.5f), white);
                }
            }
            GUI.color = old;
            GUI.Label(new Rect(area.x + 4, area.y + 2, 200, 16), string.Format(fmt, max), lblSmall);
            GUI.Label(new Rect(area.x + 4, area.yMax - 18, 200, 16), string.Format(fmt, min), lblSmall);
            GUI.Label(new Rect(area.xMax - 180, area.y + 2, 176, 16), "now " + string.Format(fmt, f(data[data.Count - 1])), new GUIStyle(lblSmall) { alignment = TextAnchor.UpperRight });
        }

        void DashboardWindow()
        {
            GUILayout.Label($"Last {S.dayStats.Count} days.   PUE {(Sim.itLoadKW > 0 ? (Sim.loadKW / Sim.itLoadKW).ToString("0.00") : "-")}   Solar now {Sim.solarKW:0} kW / {Sim.solarCapKW:0} kW   Grid price now ${Sim.PowerPriceNow:0.000}/kWh (+${S.city.carbonTax:0.000} carbon)", lblDim);
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            LineChart("Cash", d => d.money, new Color(0.4f, 1f, 0.5f), "${0:N0}");
            LineChart("Monthly recurring revenue", d => d.revenue, new Color(0.4f, 0.7f, 1f), "${0:N0}");
            LineChart("Avg customer service level", d => d.uptime, new Color(1f, 0.85f, 0.3f), "{0:0.0}%");
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            LineChart("Facility load", d => d.loadKW, new Color(1f, 0.6f, 0.3f), "{0:0} kW");
            LineChart("Daily operating cost", d => d.cost, new Color(1f, 0.4f, 0.4f), "${0:N0}");
            LineChart("Hottest hall", d => d.temp, new Color(1f, 0.5f, 0.2f), "{0:0.0} C");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- settings
        void SettingsWindowBody()
        {
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            bool changed = false;
            GUILayout.Label("DISPLAY", lblDim);
            changed |= Slider("UI scale", ref Settings.UiScale, 0.7f, 1.6f, "x{0:0.00}");
            changed |= Toggle("Day/night cycle", ref Settings.DayNight);
            changed |= Toggle("Weather effects", ref Settings.WeatherFx);
            changed |= Toggle("Show walking staff", ref Settings.ShowStaff);
            GUILayout.Label("AUDIO", lblDim);
            changed |= Slider("Master volume", ref Settings.Master, 0, 1, "{0:P0}");
            changed |= Slider("Music", ref Settings.Music, 0, 1, "{0:P0}");
            changed |= Slider("Effects", ref Settings.Sfx, 0, 1, "{0:P0}");
            GUILayout.Label("GAMEPLAY", lblDim);
            changed |= Toggle("Edge-of-screen panning", ref Settings.EdgePan);
            changed |= Toggle("Monthly autosave", ref Settings.Autosave);
            GUILayout.Label("KEY BINDINGS (click, then press a key)", lblDim);
            foreach (var kv in Settings.Defaults)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key, lbl, GUILayout.Width(160));
                string label = rebinding == kv.Key ? "press a key..." : Settings.KeyName(kv.Key);
                if (GUILayout.Button(label, rebinding == kv.Key ? btnOn : btnSmall, GUILayout.Width(140))) rebinding = kv.Key;
                GUILayout.EndHorizontal();
            }
            if (Button("Reset keys to defaults")) { Settings.ResetKeys(); rebinding = null; }
            GUILayout.EndScrollView();
            if (changed) Settings.Save();
        }

        void HandleRebind()
        {
            if (rebinding == null) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode != KeyCode.Escape) { Settings.Keys[rebinding] = e.keyCode; Settings.Save(); }
                rebinding = null;
                e.Use();
            }
        }

        bool Slider(string name, ref float v, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(160));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(220));
            GUILayout.Label(string.Format(fmt, nv), lblSmall, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            bool ch = !Mathf.Approximately(nv, v);
            v = nv;
            return ch;
        }

        bool Toggle(string name, ref bool v)
        {
            bool nv = v;
            if (GUILayout.Button((v ? "[x] " : "[ ] ") + name, v ? btnOn : btn, GUILayout.Width(390))) nv = !v;
            bool ch = nv != v;
            v = nv;
            return ch;
        }

        // ---------------------------------------------------------------- save / load slots
        void SaveLoadBody(bool allowSave)
        {
            for (int slot = 1; slot <= 3; slot++)
            {
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.Label($"Slot {slot}", lblBold, GUILayout.Width(60));
                string meta = GameManager.SlotMeta(slot);
                GUILayout.Label(string.IsNullOrEmpty(meta) ? "(empty)" : meta, lblSmall, GUILayout.Width(330));
                GUILayout.FlexibleSpace();
                int s = slot;
                if (allowSave && SmallButton("Save")) { if (GameManager.HasSave(s)) Confirm($"Overwrite slot {s}?", () => gm.Save(s)); else gm.Save(s); }
                if (SmallButton("Load", GameManager.HasSave(s))) { gm.Load(s); window = Window.None; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal(panelDark);
            GUILayout.Label("Auto", lblBold, GUILayout.Width(60));
            string am = GameManager.SlotMeta(0);
            GUILayout.Label(string.IsNullOrEmpty(am) ? "(no autosave yet - saved every month)" : am, lblSmall, GUILayout.Width(330));
            GUILayout.FlexibleSpace();
            if (SmallButton("Load", GameManager.HasSave(0))) { gm.Load(0); window = Window.None; }
            GUILayout.EndHorizontal();
        }

        void DrawModalPanel(string title, System.Action body, float w = 560, float h = 520)
        {
            DimBackground();
            h = Mathf.Min(h, VH - 40);
            var r = Reg(new Rect((VW - w) / 2, (VH - h) / 2, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, header);
            GUILayout.FlexibleSpace();
            bool close = GUILayout.Button("Close", btnSmall);
            GUILayout.EndHorizontal();
            body();
            GUILayout.EndArea();
            if (close) { window = gm.inGame ? Window.Menu : Window.None; rebinding = null; }
        }

        /// <summary>Windows shared between the main menu and the pause menu.</summary>
        bool DrawSharedWindows()
        {
            switch (window)
            {
                case Window.Settings: DrawModalPanel("Settings", SettingsWindowBody, 620, 640); return true;
                case Window.SaveLoad: DrawModalPanel(gm.inGame ? "Save / Load" : "Load Game", () => SaveLoadBody(gm.inGame), 620, 300); return true;
                case Window.Achievements: DrawModalPanel("Achievements (all games)", AchievementsBody, 620, 600); return true;
            }
            return false;
        }

        void AchievementsBody()
        {
            scrollWin = GUILayout.BeginScrollView(scrollWin);
            foreach (var a in Simulation.Achievements)
            {
                bool ever = Simulation.UnlockedEver(a.id);
                GUILayout.BeginHorizontal(panelDark);
                GUILayout.Label(ever ? "[*]" : "[ ]", ever ? lblGood : lblDim, GUILayout.Width(30));
                GUILayout.Label(a.name, ever ? lblBold : lbl, GUILayout.Width(170));
                GUILayout.Label(a.desc, lblDim);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("SCENARIOS COMPLETED", lblDim);
            foreach (var sc in Simulation.Scenarios.Skip(1))
                GUILayout.Label((Simulation.UnlockedEver("scenario_" + sc.id) ? "[*] " : "[ ] ") + sc.name, Simulation.UnlockedEver("scenario_" + sc.id) ? lblGood : lblDim);
            GUILayout.EndScrollView();
        }

        void ScenarioPicker()
        {
            GUILayout.Label("Scenario:", lbl);
            GUILayout.BeginHorizontal();
            int i = 0;
            foreach (var sc in Simulation.Scenarios)
            {
                if (i++ == 3) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                string label = sc.name + (Simulation.UnlockedEver("scenario_" + sc.id) ? " *" : "");
                if (GUILayout.Button(label, scenarioId == sc.id ? btnOn : btnSmall, GUILayout.Width(170))) scenarioId = sc.id;
            }
            GUILayout.EndHorizontal();
            var cur = Simulation.Scenario(scenarioId);
            GUILayout.Label(cur.desc + "  Goal: " + cur.goal, lblDim);
        }
    }
}
