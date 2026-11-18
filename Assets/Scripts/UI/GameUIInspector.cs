using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    public partial class GameUI
    {
        string InspectorSignature()
        {
            if (gm.selection.Count == 0) return "none";
            var sb = new StringBuilder();
            foreach (var o in gm.selection)
            {
                sb.Append(o.id).Append(':').Append(o.contractId).Append(':').Append(o.linkCore).Append(':').Append(o.linkCore2)
                  .Append(o.dualPdu ? 'P' : 'p').Append(o.pduTripped ? 'T' : 't').Append(o.failed ? 'F' : 'f').Append(o.rot)
                  .Append(o.maintQueued ? 'Q' : 'q').Append(o.inMaint ? 'M' : 'm').Append(o.servicedHour).Append('o').Append(o.orders.Count).Append(o.orders.Count(x => x.arrived)).Append('[');
                foreach (var d in o.devices) sb.Append(d.id).Append(d.failed ? 'x' : '-').Append((int)(Simulation.AgeYears(d) * 2)).Append(',');
                sb.Append(']');
            }
            sb.Append('|').Append(string.Join(",", S.contracts.Where(c => c.status == ContractStatus.Setup || c.status == ContractStatus.Active).Select(c => c.id)));
            sb.Append('|').Append(S.unlocked.Count).Append('|').Append(Templates.All.Count).Append('|').Append(showAllAssign).Append('|').Append(Sim.objectsVersion).Append('|').Append(S.shipExpress);
            return sb.ToString();
        }

        void BuildInspector(VisualElement r, Scope s)
        {
            var sel = gm.selection.ToList();
            if (sel.Count == 0) return;
            var p = K.Panel(r, "grow");
            var head = K.Row(p);
            if (sel.Count == 1) K.Text(head, $"{Catalog.Facility(sel[0].def).name}  #{sel[0].id}", "header");
            else K.Text(head, $"{sel.Count} objects selected", "header");
            K.Flex(head);
            K.Small(head, "x", () => gm.selection.Clear());
            // The inspector is rebuilt whenever its signature changes (buying a device, assigning, cabling...).
            // Keep the scroll position across rebuilds of the same selection so the panel doesn't jump to the top.
            string selKey = string.Join(",", sel.Select(o => o.id)) + "|" + Tab(sel.Count == 1 ? "insp" : "inspMulti");
            float keepY = inspectorScroll != null && selKey == inspectorScrollKey ? inspectorScroll.scrollOffset.y : 0f;
            if (sel.Count == 1 && Catalog.Facility(sel[0].def).kind == FacilityKind.Rack)
            {
                var o0 = sel[0];
                Tabs(p, inspectorSection, "insp", "Overview", "Connections", $"Hardware ({o0.devices.Count}{(o0.orders.Count > 0 ? $"+{o0.orders.Count}" : "")})", "Install");
            }
            else if (sel.Count > 1 && sel.Any(o => Catalog.Facility(o.def).kind == FacilityKind.Rack))
                Tabs(p, inspectorSection, "inspMulti", "Overview", "Bulk", "Install");
            var sv = K.Scroll(p);
            inspectorScroll = sv;
            inspectorScrollKey = selKey;
            if (keepY > 0f)
            {
                EventCallback<GeometryChangedEvent> restore = null;
                // Layout runs after the build, so the offset is clamped to 0 until the content has a size.
                // Re-apply it as the content lays out, then stop (shortly after) so user scrolling isn't overridden.
                restore = e =>
                {
                    sv.scrollOffset = new Vector2(0, keepY);
                    if (sv.scrollOffset.y >= keepY - 0.5f) sv.contentContainer.UnregisterCallback(restore);
                };
                sv.contentContainer.RegisterCallback(restore);
                sv.schedule.Execute(() => sv.contentContainer.UnregisterCallback(restore)).ExecuteLater(250);
            }
            var c = sv.contentContainer;
            if (sel.Count == 1)
            {
                var o = sel[0];
                var d = Catalog.Facility(o.def);
                if (d.kind == FacilityKind.Rack) RackInspector(c, s, o, d);
                else FacilityInspector(c, s, o, d);
            }
            else MultiInspector(c, s, sel);
        }

        ScrollView inspectorScroll;
        string inspectorScrollKey;

        RackInfo Info(PlacedObject o) => Sim.racks.TryGetValue(o.id, out var i) ? i : null;

        void RackInspector(VisualElement c, Scope s, PlacedObject o, FacilityDef d)
        {
            switch (Tab("insp"))
            {
                case 1: RackConnections(c, s, o); return;
                case 2: RackHardware(c, s, o); return;
                case 3:
                    K.Text(c, "INSTALL HARDWARE", "section-title");
                    DeviceShop(c, s, new List<PlacedObject> { o });
                    TemplatesSection(c, s, new List<PlacedObject> { o });
                    return;
            }
            var status = K.Text(s, c, () =>
            {
                var info = Info(o);
                if (info == null) return "";
                return info.online ? "ONLINE" : info.empty ? "Empty - install a ToR switch and servers" : "OFFLINE: " + info.issue;
            }, "wrap");
            K.DynClass(s, status, () => { var i = Info(o); return i == null ? null : i.online ? "good" : i.empty ? "warn" : "bad"; });
            var ports = K.Text(s, c, () => { var i = Info(o); return i != null && i.online && i.servers > i.ports ? $"Only {i.ports} switch ports for {i.servers} servers - add another ToR switch." : ""; }, "warn");
            K.Show(s, ports, () => ports.text.Length > 0);

            K.Text(s, c, () => $"Power: {Info(o)?.loadKW:0.00} / {d.rackKW:0} kW");
            K.Bar(s, c, () => (Info(o)?.loadKW ?? 0) / d.rackKW, () => (Info(o)?.loadKW ?? 0) > d.rackKW ? new Color(1, 0.3f, 0.2f) : new Color(1f, 0.8f, 0.3f), null, 8);
            K.Text(s, c, () => $"Space: {Info(o)?.usedU} / {d.rackU} U");
            K.Bar(s, c, () => (Info(o)?.usedU ?? 0) / (float)d.rackU, () => new Color(0.3f, 0.6f, 1f), null, 8);
            K.Text(s, c, () => { var i = Info(o); return i == null ? "" : $"Servers: {i.activeServers} active / {i.servers}   Ports: {i.ports}"; });
            K.Text(s, c, () => { var i = Info(o); return i == null ? "" : $"Cores: {i.cores}   Storage: {i.storage:0} TB   GPUs: {i.gpus}"; });
            K.Text(s, c, () => { var i = Info(o); return i != null && i.zone >= 0 && i.zone < Sim.zones.Count ? $"Hall temp: {Sim.zones[i.zone].temp:0.0} C" : ""; });
            if (o.floor > 0) K.Text(c, $"On floor {o.floor + 1}", "dim");
            if (o.orders.Count > 0) K.Text(c, $"{o.orders.Count(x => !x.arrived)} item(s) on order, {o.orders.Count(x => x.arrived)} delivered awaiting install (Hardware tab)", "small", "warn");
            if (Sim.CageOf(o) > 0) K.Text(c, $"Inside private cage #{Sim.CageOf(o)}", "small", "dim");
            MaintenanceBlock(c, s, o);

            // Customer
            K.Text(c, "CUSTOMER", "section-title");
            var con = o.contractId > 0 ? Sim.FindContract(o.contractId) : null;
            if (con != null)
            {
                var row = K.Row(c);
                K.Swatch(row, Catalog.CustomerColors[con.colorIdx]);
                K.Text(row, $"{con.customer} ({Catalog.TypeName(con.type)})", "bold");
                if (con.type == ContractType.Colocation) K.Text(c, $"Customer gear: {con.reqKWPerRack:0} kW in this rack (they bring their own servers)", "dim");
                K.Text(c, $"Customer peak hours: {Simulation.PeakText(con)}", "small", "dim");
                var b = K.Row(c);
                K.Small(b, "Unassign", () => Sim.Assign(o, null));
                K.Small(b, "View contract", () => { OpenWindow(Window.Contracts); gm.view.highlightContract = con.id; });
            }
            else AssignButtons(c, s, new List<PlacedObject> { o });

            // Quick health summary of the other tabs, so problems are visible without switching.
            K.Text(c, "CHECKS", "section-title");
            var chk = K.Text(s, c, () =>
            {
                var lines = new List<string>();
                if (o.linkCore <= 0) lines.Add("Not cabled to a core switch (Connections tab)");
                var gap = Sim.RedundancyGap(o);
                if (gap != null) lines.Add("Redundancy: " + gap);
                var i = Info(o);
                if (i != null && i.airflow > 1.001f) lines.Add($"Airflow: {i.airflowNote}");
                if (o.devices.Any(x => x.failed)) lines.Add($"{o.devices.Count(x => x.failed)} failed device(s) (Hardware tab)");
                return lines.Count == 0 ? "All good" : string.Join("\n", lines);
            }, "wrap");
            K.DynClass(s, chk, () => chk.text == "All good" ? "good" : "dim");
            K.Space(c, 8);
            K.Btn(c, $"Remove rack (refund {K.Money(Sim.RemovalRefund(o))})", ConfirmRemoveSelection);
        }

        void RackConnections(VisualElement c, Scope s, PlacedObject o)
        {
            // Airflow
            K.Text(c, "AIRFLOW", "section-title");
            var air = K.Text(s, c, () => { var i = Info(o); return i == null ? "" : $"{i.airflowNote}  (heat x{i.airflow:0.00})"; }, "wrap");
            K.DynClass(s, air, () => { var i = Info(o); return i == null ? null : i.airflow < 1f ? "good" : i.airflow > 1.001f ? "warn" : null; });
            K.Text(c, $"Face rows front-to-front across a 1-tile cold aisle and back-to-back across hot aisles. Rotate with [{Settings.KeyName("Rotate")}] when placing.", "dim");
            K.Small(c, "Rotate rack 90 deg", () => { Sim.RotateRack(o); gm.view.ForceRebuildObject(o.id); });

            // Redundancy
            K.Text(c, "REDUNDANCY", "section-title");
            var red = K.Text(s, c, () => Sim.RedundancyGap(o) ?? "Fully redundant (A+B power, dual ToR, dual uplink)", "wrap");
            K.DynClass(s, red, () => Sim.RedundancyGap(o) == null ? "good" : "dim");
            var deg = K.Text(s, c, () => (Info(o)?.degraded ?? false) ? "Running degraded on one path - repair soon!" : "", "warn");
            K.Show(s, deg, () => deg.text.Length > 0);
            if (o.pduTripped) K.Text(c, o.dualPdu ? "PDU A tripped, running on feed B" : "PDU tripped!", o.dualPdu ? "warn" : "bad");
            var rrow = K.Row(c);
            if (!o.dualPdu) K.Small(s, rrow, () => $"Dual PDU ({K.Money(Simulation.DualPduCost)})", () => Report(Sim.BuyDualPdu(o)), () => S.money >= Simulation.DualPduCost);
            if (Sim.RedundancyGap(o) != null) K.Small(rrow, "Make redundant", () => Report(Sim.MakeRedundant(o)));
            var brow = K.Row(c);
            PlacedObject core2 = o.linkCore2 > 0 && Sim.objById.TryGetValue(o.linkCore2, out var c2) ? c2 : null;
            if (core2 != null)
            {
                K.Text(brow, $"Uplink B: Core #{core2.id}", "small");
                K.Small(brow, "Disconnect B", () => Sim.DisconnectSecondary(o));
            }
            else if (o.linkCore > 0)
            {
                K.Small(brow, "Auto-cable uplink B", () => Report(Sim.AutoConnectSecondary(o)));
                K.Small(brow, "Pick core for B...", () => gm.BeginConnect(o, true));
            }
            if (!Sim.IsReachable(o)) K.Text(c, "No clear walking access: technicians must squeeze past equipment (repairs take 50% longer).", "warn");

            // Network
            K.Text(c, "NETWORK", "section-title");
            PlacedObject core = o.linkCore > 0 && Sim.objById.TryGetValue(o.linkCore, out var cc) ? cc : null;
            if (core != null)
            {
                var route = Sim.CableRouteFor(o, core);
                K.Text(s, c, () => $"Cabled to {Catalog.Facility(core.def).name} #{core.id} @ {Info(o)?.linkGbps:0} Gbps" + (core.floor != o.floor ? $" (floor {core.floor + 1}, via riser)" : ""), "wrap");
                K.Text(c, route.routed ? "Routed through cable trays" : "Loose cabling (+8% heat) - add cable trays", route.routed ? "good" : "dim");
                var row = K.Row(c);
                K.Small(row, "Re-cable...", () => gm.BeginConnect(o));
                K.Small(row, "Disconnect", () => Sim.Disconnect(o));
            }
            else
            {
                var best = Sim.BestCore(o);
                var row = K.Row(c);
                K.Small(row, best != null ? $"Auto-cable ({K.Money(Sim.CableCost(o, best))})" : "Auto-cable (no reachable core)", () => Report(Sim.AutoConnect(o))).SetEnabled(best != null);
                K.Small(row, "Pick core switch...", () => gm.BeginConnect(o));
            }

        }

        void RackHardware(VisualElement c, Scope s, PlacedObject o)
        {
            if (o.orders.Count > 0)
            {
                K.Text(c, "ON ORDER", "section-title");
                foreach (var ord in o.orders.OrderBy(x => x.arriveHour).ToList())
                {
                    var od = ord;
                    var row = K.Row(c, "card");
                    K.Text(row, $"U{ord.u + 1:00}  {Catalog.Device(ord.def)?.name}", "small").style.width = 170;
                    K.Text(s, row, () => od.arrived ? "Delivered - waiting for a technician" : $"Arrives in {HoursToText(od.arriveHour - S.hour)}", "small", od.arrived ? "good" : "dim");
                    K.Flex(row);
                    if (!ord.arrived) K.Small(row, "Cancel (90% refund)", () => Report(Sim.CancelOrder(o, od)));
                }
            }
            K.Text(c, "INSTALLED HARDWARE", "section-title");
            if (o.devices.Count == 0) K.Text(c, "Nothing installed - see the Install tab.", "dim");
            foreach (var dev in o.devices.OrderByDescending(x => x.u).ToList())
            {
                var dd = Catalog.Device(dev.def);
                var dv = dev;
                var row = K.Row(c, "card");
                K.Swatch(row, dev.failed ? Color.red : dd.color, 10);
                var nm = K.Text(row, $"U{dev.u + 1:00}  {dd.name}", "small", dev.failed ? "bad" : null);
                nm.style.width = 170;
                float age = Simulation.AgeYears(dev);
                if (age >= 0.1f) K.Text(row, $"{age:0.0}y", "small", age > 3f ? "bad" : age > 1.5f ? "warn" : "dim").style.width = 34;
                K.Flex(row);
                if (age > 1.5f)
                {
                    float cost = Sim.DeviceCost(dd) - Sim.ResaleValue(dev);
                    K.Small(s, row, () => $"Refresh {K.Money(cost)}", () => Report(Sim.RefreshDevice(o, dv)), () => S.money >= cost);
                }
                if (dev.failed)
                {
                    float rc = Sim.ManualRepairCost(o, dev);
                    K.Small(s, row, () => $"Fix {K.Money(rc)}", () => Sim.ManualRepair(o, dv), () => S.money >= rc);
                }
                K.Small(row, "Sell", () => Sim.RemoveDevice(o, dv));
            }
        }

        void Report(string err) { if (err != null) Toast(err, 2); }

        void AssignButtons(VisualElement c, Scope s, List<PlacedObject> racks)
        {
            var open = S.contracts.Where(x => x.status == ContractStatus.Setup || x.status == ContractStatus.Active).ToList();
            if (open.Count == 0) { K.Text(c, "No contracts yet - accept one in the Market [M].", "dim"); return; }
            K.Text(c, "Assign to contract:", "small");
            open = open.OrderBy(x => x.status == ContractStatus.Setup ? 0 : !x.meeting ? 1 : 2).ToList();
            int shown = 0;
            foreach (var con in open)
            {
                if (!showAllAssign && shown >= 5) { K.Small(c, $"Show all {open.Count} contracts...", () => showAllAssign = true); break; }
                shown++;
                var ct = con;
                var row = K.Row(c);
                K.Swatch(row, Catalog.CustomerColors[con.colorIdx]);
                int count = Sim.company.AllRacks().Count(x => x.rack.contractId == con.id);
                var b = K.Small(row, $"{con.customer} - {Catalog.TypeName(con.type)} ({count} racks)", () =>
                {
                    foreach (var rk in racks) { var e = Sim.Assign(rk, ct); if (e != null) { Toast(e, 2); break; } }
                });
                b.style.flexGrow = 1;
            }
        }

        void DeviceShop(VisualElement c, Scope s, List<PlacedObject> racks)
        {
            bool multi = racks.Count > 1;
            var ship = K.Row(c, "card");
            K.Text(ship, "Shipping:", "small").style.width = 70;
            K.Btn(s, ship, () => $"Standard ({Sim.LeadHours(Catalog.Devices[0]) / 24}d+)", () => S.shipExpress = false, null, () => !S.shipExpress).style.flexGrow = 1;
            K.Btn(s, ship, () => $"Express +{(Sim.ExpressMul - 1f) * 100:0}% (instant)", () => S.shipExpress = true, null, () => S.shipExpress).style.flexGrow = 1;
            K.Text(s, c, () => S.shipExpress ? "Express: installed immediately at a premium." : $"Standard: a truck delivers in {Sim.LeadHours(Catalog.Devices[0]) / 24}+ days{(Sim.HasDock ? "" : " (build a Loading Dock to halve that)")}, then a technician installs it. Shortages add weeks.", "small", "dim", "wrap");
            foreach (var dd in Catalog.Devices)
            {
                if (!Sim.IsUnlocked(dd.id)) continue;
                var def = dd;
                var card = K.Card(c);
                var head = K.Row(card);
                K.Swatch(head, dd.color, 10);
                K.Text(head, dd.name, "bold");
                K.Flex(head);
                var price = K.Text(s, head, () => K.Money(Sim.OrderPrice(def, S.shipExpress)) + (S.shipExpress ? "" : $"  ({Sim.LeadHours(def) / 24}d)"));
                K.DynClass(s, price, () => S.money >= Sim.OrderPrice(def, S.shipExpress) ? "good" : "bad");
                string spec = dd.kind == DeviceKind.Switch
                    ? $"{dd.heightU}U  {dd.ports} ports  {dd.uplinkGbps:0}G uplink  {dd.watts:0}W"
                    : $"{dd.heightU}U  {dd.cores} cores  {dd.storageTB:0}TB" + (dd.gpus > 0 ? $"  {dd.gpus} GPU" : "") + $"  {dd.watts:0}W";
                var row = K.Row(card);
                K.Text(row, spec, "dim");
                K.Flex(row);
                Func<bool> any = () => racks.Any(r => Sim.CanAddDevice(r, def) == null);
                K.Small(s, row, () => multi ? "+1 each" : "+1", () => { foreach (var r in racks) Sim.AddDevice(r, def); }, any);
                if (dd.kind == DeviceKind.Server)
                    K.Small(s, row, () => multi ? "Fill all" : "Fill", () =>
                    {
                        int n = 0;
                        foreach (var r in racks)
                        {
                            var rd = Catalog.Facility(r.def);
                            float load = Info(r)?.loadKW ?? 0;
                            float pending = r.orders.Sum(x => (Catalog.Device(x.def)?.watts ?? 0) / 1000f);
                            int byPower = Mathf.FloorToInt((rd.rackKW - load - pending) / (def.watts / 1000f));
                            n += Sim.FillRack(r, def, Mathf.Max(0, byPower));
                        }
                        Toast(S.shipExpress ? $"Installed {n} x {def.name}." : $"Ordered {n} x {def.name}.", 1);
                    }, any);
                var why = K.Text(s, card, () => any() || racks.Count == 0 ? "" : Sim.CanAddDevice(racks[0], def) ?? "", "dim");
                K.Show(s, why, () => why.text.Length > 0);
            }
        }

        void TemplatesSection(VisualElement c, Scope s, List<PlacedObject> racks)
        {
            K.Text(c, "RACK TEMPLATES", "section-title");
            var src = racks.FirstOrDefault(r => r.devices.Count > 0);
            if (src != null)
            {
                var row = K.Row(c);
                var tf = new TextField { value = templateName, maxLength = 24 };
                tf.AddToClassList("textfield");
                tf.style.width = 170;
                tf.RegisterValueChangedCallback(e => templateName = e.newValue);
                row.Add(tf);
                K.Small(row, $"Save #{src.id} as template", () => { Templates.All.Add(Templates.FromRack(src, templateName)); Templates.Save(); Toast($"Template \"{templateName}\" saved.", 1); inspectorSection.Invalidate(); });
            }
            var empties = racks.Where(r => r.devices.Count == 0 && r.orders.Count == 0).ToList();
            foreach (var t in Templates.All.ToList())
            {
                var tt = t;
                float cost = Templates.Cost(t, Sim) * Mathf.Max(1, empties.Count);
                var row = K.Row(c, "card");
                K.Text(row, $"{t.name}  ({t.devices.Count} devices{(t.dualPdu ? ", dual PDU" : "")})", "small").style.width = 220;
                K.Flex(row);
                K.Small(s, row, () => $"Apply {K.Money(cost)}", () =>
                {
                    int n = 0; string err = null;
                    foreach (var r in empties) n += Templates.Apply(tt, r, Sim, out err);
                    Toast(err != null ? $"Installed {n} devices. Stopped: {err}" : $"Installed {n} devices from \"{tt.name}\".", err != null ? 2 : 1);
                }, () => empties.Count > 0 && S.money >= cost);
                K.Small(row, "x", () => { Templates.All.Remove(tt); Templates.Save(); inspectorSection.Invalidate(); });
            }
            if (empties.Count == 0) K.Text(c, "Templates apply to empty racks in the selection.", "dim");
        }

        void FacilityInspector(VisualElement c, Scope s, PlacedObject o, FacilityDef d)
        {
            K.Text(c, d.desc, "dim");
            var st = K.Text(s, c, () => o.failed ? "BROKEN - " + (Sim.EffectiveTechs > 0 ? "staff will walk over and repair it" : "no staff on shift") : "Operational", "wrap");
            K.DynClass(s, st, () => o.failed ? "bad" : "good");
            float cost = Sim.ManualRepairCost(o, null);
            var fix = K.Btn(s, c, () => $"Emergency repair now ({K.Money(cost)})", () => Sim.ManualRepair(o, null), () => S.money >= cost);
            K.Show(s, fix, () => o.failed);
            if (o.floor > 0) K.Text(c, $"On floor {o.floor + 1}", "dim");
            MaintenanceBlock(c, s, o);
            if (d.noise > 0)
            {
                var nz = K.Text(s, c, () =>
                {
                    float l = Sim.NoiseAtNeighbours(o, true);
                    return $"Noise at the houses to the {Simulation.SideNames[Sim.ResidentialSide]}: {l:0} dB{(d.kind == FacilityKind.Generator ? " when running" : "")} (limit {S.city.noiseLimit:0} dB, {S.city.noiseLimit - 5:0} at night)" + (Sim.HasSoundWallNear(o) ? " - behind a sound wall" : "");
                }, "small", "wrap");
                K.DynClass(s, nz, () => Sim.NoiseAtNeighbours(o, true) > S.city.noiseLimit - 5 ? "warn" : "dim");
            }
            K.Space(c, 4);
            switch (d.kind)
            {
                case FacilityKind.Cooling:
                    K.Text(c, $"Cooling capacity: {d.coolingKW:0} kW (x{S.city.coolingFactor:0.00} climate)");
                    K.Text(c, $"Power draw: {d.watts / 1000f:0.0} kW");
                    if (d.water > 0)
                    {
                        K.Text(s, c, () => $"Evaporative: x{Sim.EvapFactor:0.00} capacity at {S.city.humidity * 100:0}% humidity{(S.events.Any(e => e.type == "drought") ? " (drought!)" : "")}", "small", "wrap");
                        K.Text(s, c, () => $"Site water use: {Sim.waterM3Hour:0.00} m3/h at ${Sim.WaterPriceNow:0.00}/m3 ({K.Money(Sim.waterM3Hour * Sim.WaterPriceNow * Catalog.HoursPerMonth)}/mo)", "small", "dim");
                    }
                    if (Simulation.IsLiquid(d)) K.Text(c, "Liquid-cooled halls give up far more heat to a District Heat Exchanger.", "small", "dim", "wrap");
                    K.Text(s, c, () => { int zi = Sim.ZoneAt(o); return zi >= 0 ? $"Hall temperature: {Sim.zones[zi].temp:0.0} C   Heat {Sim.zones[zi].heatKW:0.0} / {Sim.zones[zi].coolKW:0.0} kW cooling" : "Not in a server hall!"; }, "wrap");
                    K.Bar(s, c, () => { int zi = Sim.ZoneAt(o); return zi >= 0 && Sim.zones[zi].coolKW > 0 ? Sim.zones[zi].heatKW / Sim.zones[zi].coolKW : 1; },
                        () => { int zi = Sim.ZoneAt(o); return zi >= 0 && Sim.zones[zi].heatKW > Sim.zones[zi].coolKW ? new Color(1, 0.3f, 0.2f) : new Color(0.3f, 0.7f, 1f); }, null, 10);
                    break;
                case FacilityKind.CoreSwitch:
                    K.Text(s, c, () => $"Rack ports: {(Sim.coreUsage.TryGetValue(o.id, out int u) ? u : 0)} / {d.ports} used at {d.portGbps:0}G");
                    K.Bar(s, c, () => (Sim.coreUsage.TryGetValue(o.id, out int u) ? u : 0) / (float)d.ports, () => new Color(0.3f, 0.6f, 1f), null, 10);
                    if (S.RoomAt(o.floor, o.x, o.y) != RoomType.Network) K.Text(c, "Not in a Network Room - racks cabled here are offline!", "bad");
                    K.Btn(c, "Auto-cable all uncabled racks here", () =>
                    {
                        foreach (var rack in Sim.Racks.Where(x => x.linkCore < 0).ToList())
                        {
                            if (Sim.FreePorts(o) <= 0) break;
                            var err = Sim.Connect(rack, o);
                            if (err != null) { Toast(err, 2); break; }
                        }
                    });
                    break;
                case FacilityKind.Isp:
                    K.Text(c, $"Bandwidth: {d.bandwidth:0} Gbps   Fee: {K.Money(d.monthly)}/month");
                    K.Text(s, c, () => $"Site WAN: {Sim.ispGbps:0} Gbps, demand {Sim.bwDemand:0.0} Gbps");
                    break;
                case FacilityKind.Transformer:
                case FacilityKind.Switchgear:
                    K.Text(c, $"Capacity: {d.supplyKW:0} kW");
                    K.Text(s, c, () => $"Grid transformers: {Sim.transformerKW:0} kW   Switchgear: {Sim.switchgearKW:0} kW");
                    K.Text(s, c, () => $"Usable supply: {Sim.supplyKW:0} kW   Load: {Sim.loadKW:0.0} kW");
                    break;
                case FacilityKind.Ups:
                case FacilityKind.Generator:
                    K.Text(c, $"Backup capacity: {d.backupKW:0} kW");
                    K.Text(s, c, () => $"Total UPS: {Sim.upsKW:0} kW   Generators: {Sim.genKW:0} kW   Batteries: {Sim.batteryKW:0} kW   Load: {Sim.loadKW:0.0} kW", "wrap");
                    K.Text(c, "Short outages need UPS >= load. Long outages need both UPS and generators >= load.", "dim");
                    break;
                case FacilityKind.Desk:
                    K.Text(s, c, () => $"Desks: {Sim.deskCount}   Staff on day/night shift: {Sim.ShiftCount("Day")}/{Sim.ShiftCount("Night")}");
                    K.Btn(c, "Open Staff & Land", () => OpenWindow(Window.Staff));
                    break;
                case FacilityKind.Solar:
                    K.Text(s, c, () => $"Peak output: {d.supplyKW:0} kW   Now: {(Sim.solarCapKW > 0 ? Sim.solarKW * d.supplyKW / Sim.solarCapKW : 0):0.0} kW");
                    K.Text(s, c, () => $"Weather: {S.weather}   Sun factor x{S.city.solarFactor:0.00}   Last month solar share: {S.lastSolarShare * 100:0}%", "dim");
                    break;
                case FacilityKind.Suppression:
                    K.Text(c, Sim.ZoneAt(o) >= 0 ? "Protecting this server hall from fire." : "Not in a server hall!", Sim.ZoneAt(o) >= 0 ? "good" : "bad");
                    K.Text(s, c, () => $"Fires extinguished so far: {S.firesFought}", "dim");
                    break;
                case FacilityKind.Battery:
                    K.Text(s, c, () => $"Charge: {o.charge:0}/{d.capacityKWh:0} kWh   Rate: {d.supplyKW:0} kW");
                    K.Bar(s, c, () => o.charge / Mathf.Max(1, d.capacityKWh), () => new Color(0.3f, 0.9f, 0.5f), null, 10);
                    K.Text(s, c, () => Sim.outageActive ? "Backing up the grid" : Sim.TimeOfDayPriceMul < 1f ? "Charging (cheap night power)" : Sim.TimeOfDayPriceMul > 1f ? "Discharging (evening peak)" : "Holding", "dim");
                    break;
                case FacilityKind.HeatExchanger:
                    K.Text(s, c, () => $"Exporting {Sim.heatExportKW:0} / {Sim.heatCapKW:0} kW at ${Sim.HeatPriceNow:0.000}/kWh (season x{Sim.HeatSeason:0.00})");
                    K.Text(s, c, () => $"Earning {K.Money(Sim.heatRevenueHour * 24)}/day   Heat sold so far {K.Money(S.heatSoldTotal)}", "small");
                    K.Text(c, S.city.heatPrice <= 0 ? "Nobody in this city buys heat - it's too warm." : "Air-cooled halls recover 35% of their heat, liquid-cooled up to 90%. Winter pays best.", "small", "dim", "wrap");
                    break;
                case FacilityKind.AccessControl:
                    K.Text(c, "Counts towards the site's compliance certification and stops half of break-in attempts.", "small", "dim", "wrap");
                    K.Btn(c, "Open Sites > Compliance", () => { OpenWindow(Window.Sites); SetTab("sites", 2, windowSection); });
                    break;
                case FacilityKind.LoadingDock:
                    K.Text(s, c, () => $"Orders in transit to this site: {Sim.PendingOrders}", "small");
                    K.Text(c, "Standard hardware deliveries take half as long with a dock.", "small", "dim");
                    break;
                case FacilityKind.SoundWall:
                    K.Text(s, c, () => $"Noise at the houses to the {Simulation.SideNames[Sim.ResidentialSide]} right now: {Sim.noiseNow:0} dB (limit {Sim.NoiseLimitNow:0})", "small", "wrap");
                    K.Text(c, "Cuts the noise of outdoor equipment within 2 tiles by 12 dB.", "small", "dim");
                    break;
                case FacilityKind.Elevator:
                {
                    var floors = Enumerable.Range(0, GameState.MaxFloors).Where(f => { var e = Sim.ObjectAtF(f, o.x, o.y); return e != null && Catalog.Facility(e.def).kind == FacilityKind.Elevator; }).Select(f => f + 1).ToList();
                    K.Text(c, $"Shaft serves floors: {string.Join(", ", floors)}", "bold");
                    K.Text(c, floors.Count < 2 ? "Place another elevator at the same spot on the floor above or below to link floors." : "Staff ride between linked floors; cross-floor cables run up this riser.", "dim");
                    break;
                }
            }
            if (!Sim.IsReachable(o)) K.Text(c, "No clear walking access: repairs take 50% longer.", "warn");
            K.Space(c, 8);
            K.Btn(c, $"Remove (refund {K.Money(Sim.RemovalRefund(o))})", ConfirmRemoveSelection);
        }

        void MaintenanceBlock(VisualElement c, Scope s, PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            if (!Simulation.Serviceable(d) || (d.kind == FacilityKind.Rack && o.devices.Count == 0)) return;
            K.Text(c, "MAINTENANCE", "section-title");
            var t = K.Text(s, c, () =>
            {
                float m = Sim.ServiceAgeMonths(o);
                string state = o.inMaint ? "Being serviced now" + (Sim.MaintSafe(o) ? " (stays online)" : " - offline!") :
                               o.maintQueued ? (o.maintNow || !S.maintNightOnly ? "Queued for the next free technician" : "Queued for the night window") : Sim.MaintDue(o) ? "Overdue" : "OK";
                return $"Last serviced {m:0.0} months ago - failure rate x{Simulation.ServiceFactorFor(m):0.00}.   {state}";
            }, "small", "wrap");
            K.DynClass(s, t, () => o.inMaint ? "warn" : Sim.MaintDue(o) ? "warn" : "dim");
            var row = K.Row(c);
            if (o.maintQueued) K.Small(row, "Cancel service", () => Sim.CancelMaintenance(o));
            else K.Small(row, $"Service now ({K.Money(Sim.MaintCost(o))})", () => Report(Sim.QueueMaintenance(o, true)));
            if (!o.maintQueued) K.Small(row, "Service tonight", () => Report(Sim.QueueMaintenance(o, false)));
            if (d.kind == FacilityKind.Rack && !Sim.MaintSafe(o)) K.Text(c, "Goes offline while serviced - fit dual PDUs and two ToR switches to keep it running.", "small", "dim", "wrap");
            var risk = K.Text(s, c, () => { var r = Sim.MaintRisk(o); return r != null ? r + ". Auto-maintenance skips it." : ""; }, "small", "warn", "wrap");
            K.Show(s, risk, () => risk.text.Length > 0);
        }

        void MultiInspector(VisualElement c, Scope s, List<PlacedObject> sel)
        {
            var racks = sel.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList();
            if (racks.Count == 0)
            {
                K.Text(c, "Select racks to manage them in bulk.", "dim");
                K.Btn(c, "Remove selected", ConfirmRemoveSelection);
                return;
            }
            int tab = Tab("inspMulti");
            if (tab == 2)
            {
                K.Text(c, "INSTALL IN EACH RACK", "section-title");
                DeviceShop(c, s, racks);
                return;
            }
            if (tab == 1) { MultiBulk(c, s, racks); return; }
            var st = K.Text(s, c, () => $"{racks.Count} racks, {racks.Count(r => Info(r)?.online ?? false)} online");
            K.DynClass(s, st, () => racks.All(r => Info(r)?.online ?? false) ? "good" : "warn");
            K.Text(s, c, () => $"Total load: {racks.Sum(r => Info(r)?.loadKW ?? 0):0.0} kW");
            K.Text(s, c, () => $"Cores: {racks.Sum(r => Info(r)?.cores ?? 0)}   Storage: {racks.Sum(r => Info(r)?.storage ?? 0):0} TB   GPUs: {racks.Sum(r => Info(r)?.gpus ?? 0)}");
            K.Text(s, c, () => string.Join("\n", racks.Select(r => Info(r)?.issue).Where(x => x != null).GroupBy(x => x).Select(g => $"{g.Count()}x {g.Key}")), "warn");

            K.Text(c, "NETWORK", "section-title");
            var nrow = K.Row(c);
            K.Small(nrow, "Auto-cable uncabled", () => { foreach (var r in racks.Where(x => x.linkCore < 0)) { var e = Sim.AutoConnect(r); if (e != null) { Toast(e, 2); break; } } });
            K.Small(nrow, "Cable all to core...", () => gm.BeginConnect(racks[0]));

            K.Text(c, "CUSTOMER", "section-title");
            AssignButtons(c, s, racks);
            K.Small(c, "Unassign all", () => { foreach (var r in racks) Sim.Assign(r, null); });
            K.Space(c, 8);
            K.Btn(c, "Remove selected", ConfirmRemoveSelection);
        }

        void MultiBulk(VisualElement c, Scope s, List<PlacedObject> racks)
        {
            K.Text(c, "BULK", "section-title");
            var brow = K.Row(c);
            K.Small(brow, "Make all redundant", () => { foreach (var r in racks) { var e = Sim.MakeRedundant(r); if (e != null) { Toast(e, 2); break; } } });
            K.Small(brow, "Rotate all", () => { foreach (var r in racks) { Sim.RotateRack(r); gm.view.ForceRebuildObject(r.id); } });
            var template = racks.FirstOrDefault(r => r.devices.Count > 0);
            if (template != null)
            {
                int empties = racks.Count(r => r.devices.Count == 0);
                float cost = racks.Where(r => r.devices.Count == 0).Sum(r => template.devices.Sum(dv => Sim.DeviceCost(Catalog.Device(dv.def))));
                K.Small(s, c, () => $"Copy rack #{template.id}'s hardware into {empties} empty rack(s) ({K.Money(cost)})", () =>
                {
                    foreach (var r in racks.Where(x => x.devices.Count == 0))
                        foreach (var dv in template.devices.OrderBy(x => x.u))
                        {
                            var dd = Catalog.Device(dv.def);
                            if (Sim.CanAddDevice(r, dd) == null) Sim.AddDevice(r, dd);
                        }
                }, () => empties > 0 && S.money >= cost);
            }
            TemplatesSection(c, s, racks);
        }
    }
}
