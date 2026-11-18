using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    public partial class GameUI
    {
        string WindowSignature()
        {
            switch (window)
            {
                case Window.Market: return $"m|{string.Join(",", S.offers.Select(o => o.id + (o.negotiated ? "n" : "") + o.monthlyPay))}|{S.rivals.Count}";
                case Window.Contracts: return $"c|{string.Join(",", S.contracts.Select(c => c.id + c.status.ToString() + (c.meeting ? "m" : "") + (c.satisfaction < 40 ? "u" : "")))}|{string.Join(",", S.tickets.Select(t => t.id))}";
                case Window.Research: return $"r|{S.unlocked.Count}|{S.Level}";
                case Window.Finance: return $"f|{S.reports.Count}";
                case Window.Staff: return $"s|{S.activeSite}|{string.Join(",", S.staff.Select(m => m.id + m.shift))}|{string.Join(",", S.candidates.Select(c => c.name))}|{S.ownX0},{S.ownY0},{S.ownX1},{S.ownY1}";
                case Window.Log: return $"l|{S.log.Count}|{(S.log.Count > 0 ? S.log[S.log.Count - 1].hour : 0)}";
                case Window.Goals: return $"g|{S.achievements.Count}|{S.ipo}|{S.scenarioDone}";
                case Window.Dashboard: return "d";
                case Window.Sites: return $"st|{Sim.company.sims.Count}|{S.activeSite}|{S.Level}";
                case Window.Sandbox: return $"sb|{S.cheatInfiniteMoney}{S.cheatNoFailures}{S.cheatNoEvents}{S.cheatNoBankruptcy}";
                default: return window.ToString();
            }
        }

        static readonly HashSet<Window> PanelWindows = new HashSet<Window> { Window.Market, Window.Contracts, Window.Research, Window.Finance, Window.Staff, Window.Log, Window.Goals, Window.Dashboard, Window.Sites, Window.Sandbox };

        void BuildWindow(VisualElement r, Scope s)
        {
            if (!PanelWindows.Contains(window)) return;
            var p = K.Panel(r);
            var head = K.Row(p);
            K.Text(head, window switch
            {
                Window.Market => "Contract Market",
                Window.Contracts => "Your Contracts",
                Window.Research => "Research & Unlocks",
                Window.Finance => "Finance",
                Window.Staff => "Staff & Land" + (Sim.company.MultiSite ? $" - {S.siteName}" : ""),
                Window.Log => "Event Log",
                Window.Goals => "Goals & Achievements",
                Window.Dashboard => "Dashboard",
                Window.Sites => "Sites",
                Window.Sandbox => "Sandbox Tools",
                _ => ""
            }, "header");
            K.Flex(head);
            K.Small(head, "Close", () => OpenWindow(Window.None));
            switch (window)
            {
                case Window.Market: MarketWindow(p, s); break;
                case Window.Contracts: ContractsWindow(p, s); break;
                case Window.Research: ResearchWindow(p, s); break;
                case Window.Finance: FinanceWindow(p, s); break;
                case Window.Staff: StaffWindow(p, s); break;
                case Window.Log: LogWindow(p, s); break;
                case Window.Goals: GoalsWindow(p, s); break;
                case Window.Dashboard: DashboardWindow(p, s); break;
                case Window.Sites: SitesWindow(p, s); break;
                case Window.Sandbox: SandboxWindow(p, s); break;
            }
        }

        // ---------------------------------------------------------------- contracts helpers
        static string Requirement(Contract c) => ProcGen.Describe(c) + $"  +  {c.reqBandwidth:0.#} Gbps";

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
                    return $"~{c.reqServers} x {best.name} ({K.Money(Sim.DeviceCost(best) * c.reqServers)}), {c.reqServers * best.watts / 1000f:0.0} kW";
                }
                case ContractType.Compute:
                {
                    var best = devs.Where(d => d.cores > 0).OrderBy(d => d.cost / d.cores).First();
                    int n = Mathf.CeilToInt(c.reqCores / (float)best.cores);
                    return $"~{n} x {best.name} ({K.Money(Sim.DeviceCost(best) * n)}), {n * best.watts / 1000f:0.0} kW";
                }
                case ContractType.Storage:
                {
                    var best = devs.Where(d => d.storageTB > 0).OrderBy(d => d.cost / d.storageTB).First();
                    int n = Mathf.CeilToInt(c.reqStorageTB / best.storageTB);
                    return $"~{n} x {best.name} ({K.Money(Sim.DeviceCost(best) * n)}), {n * best.watts / 1000f:0.0} kW";
                }
                case ContractType.GpuCloud:
                {
                    var best = devs.Where(d => d.gpus > 0).OrderBy(d => d.cost / d.gpus).FirstOrDefault();
                    if (best == null) return "!Research GPU servers first.";
                    int n = Mathf.CeilToInt(c.reqGpus / (float)best.gpus);
                    return $"~{n} x {best.name} ({K.Money(Sim.DeviceCost(best) * n)}), {n * best.watts / 1000f:0.0} kW";
                }
            }
            return "";
        }

        /// <summary>One line: customer, industry, trait chips (hover for what they mean), optional status text and the monthly pay.</summary>
        void ContractHeader(VisualElement p, Scope s, Contract c, Func<string> extra = null)
        {
            var row = K.Row(p);
            K.Swatch(row, Catalog.CustomerColors[c.colorIdx], 14);
            var name = K.Text(row, c.customer, "card-title");
            if (!string.IsNullOrEmpty(c.flavor)) Tip(name, c.flavor);
            K.Text(row, $"{c.industry} - {Catalog.TypeName(c.type)}", "dim").style.marginRight = 6;
            foreach (var t in c.traits) Tip(K.Text(row, t, "chip"), ProcGen.TraitDesc(t));
            K.Flex(row);
            if (extra != null) K.Text(s, row, extra, "dim").style.marginRight = 8;
            K.Text(s, row, () => $"{K.Money(c.monthlyPay)}/mo", "big-pay");
        }

        /// <summary>A labelled slot in a row of bars; the slots share the row's width equally.</summary>
        static VisualElement BarCell(VisualElement row, string label)
        {
            var cell = K.Row(row);
            cell.style.flexGrow = 1;
            cell.style.flexBasis = 0;
            cell.style.marginRight = 8;
            K.Text(cell, label, "small", "dim").style.marginRight = 4;
            return cell;
        }

        void ReqBar(VisualElement row, Scope s, string label, Func<float> have, float need, string unit)
        {
            K.Bar(s, BarCell(row, label), () => need <= 0 ? 1 : have() / need, () => have() >= need - 0.001f ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.55f, 0.2f), () => $"{have():0.#} / {need:0.#} {unit}");
        }

        // ---------------------------------------------------------------- market
        void MarketWindow(VisualElement p, Scope s)
        {
            K.Text(s, p, () => $"Offers refresh over time. Better reputation brings more and better-paying customers. Reputation level {S.Level}, rating {S.rating:0}/100.", "dim");
            var rv = K.Row(p, "card");
            K.Text(rv, "Competitors:", "dim").style.width = 90;
            foreach (var rival in S.rivals)
            {
                K.Swatch(rv, Catalog.CustomerColors[rival.colorIdx], 10);
                K.Text(rv, $"{rival.name}  rep {rival.reputation:0}  {rival.customers} customers", "small").style.marginRight = 10;
            }
            K.Flex(rv);
            K.Text(s, rv, () => $"You: {Sim.YourCustomers} customers", "bold");
            if (S.events.Any(e => e.type == "pricewar")) K.Text(p, "A price war is on: new offers pay 15% less.", "warn");

            var types = (ContractType[])Enum.GetValues(typeof(ContractType));
            var labels = new List<string> { $"All ({S.offers.Count})" };
            labels.AddRange(types.Select(t => $"{Catalog.TypeName(t)} ({S.offers.Count(o => o.type == t)})"));
            int tab = Tabs(p, windowSection, "market", labels.ToArray());
            var shown = S.offers.Where(o => tab == 0 || o.type == types[tab - 1]).ToList();

            var sv = K.Scroll(p, "fit");
            if (shown.Count == 0) K.Text(sv.contentContainer, S.offers.Count == 0 ? "No offers right now. New customers will call soon." : "No offers of this type right now.", "dim");
            foreach (var c in shown)
            {
                var con = c;
                var card = K.Card(sv.contentContainer);
                ContractHeader(card, s, c, () => $"expires in {HoursToText(con.expiresHour - S.hour)}");
                var terms = K.Row(card);
                terms.style.flexWrap = Wrap.Wrap;
                K.Text(terms, "Requires: " + Requirement(c), "bold").style.marginRight = 14;
                K.Text(terms, $"SLA {c.sla}%   Term {c.months} months   Setup {c.setupDays} days   Signing bonus {K.Money(c.signingBonus)}");
                var row = K.Row(card);
                string est = Estimate(c);
                var el = K.Text(row, est.StartsWith("!") ? est.Substring(1) : "Estimate: " + est, est.StartsWith("!") ? "bad" : "dim");
                el.style.flexGrow = 1; el.style.flexShrink = 1; el.style.flexBasis = 0;
                if (!c.negotiated)
                {
                    K.Small(row, "Ask +15% pay", () => Toast(Sim.Negotiate(con, "pay"), 0));
                    K.Small(row, "Ask +7 days", () => Toast(Sim.Negotiate(con, "time"), 0));
                    if (c.sla > 99f) K.Small(row, "Ask lower SLA", () => Toast(Sim.Negotiate(con, "sla"), 0));
                }
                else K.Text(row, "Negotiated", "dim");
                K.Small(row, "Decline", () => Sim.DeclineOffer(con));
                K.Small(row, "Accept contract", () => Sim.AcceptOffer(con), "on");
            }
        }

        // ---------------------------------------------------------------- contracts
        void ContractsWindow(VisualElement p, Scope s)
        {
            var setup = S.contracts.Where(x => x.status == ContractStatus.Setup).ToList();
            var live = S.contracts.Where(x => x.status == ContractStatus.Active).ToList();
            var past = S.contracts.Where(x => x.status == ContractStatus.Completed || x.status == ContractStatus.Cancelled).ToList();
            // Live contracts that are short of requirements, have open tickets or an unhappy customer.
            var attention = live.Where(x => !x.meeting || x.satisfaction < 40 || S.tickets.Any(t => t.contractId == x.id)).ToList();
            int tab = Tabs(p, windowSection, "contracts", live.Count == 0 && setup.Count > 0 ? 2 : 0, out var bar,
                $"Live ({live.Count})", $"Needs attention ({attention.Count})", $"Setting up ({setup.Count})", $"History ({past.Count})");
            K.Text(s, bar, () => $"Completed {S.contractsCompleted}   Failed {S.contractsFailed}", "dim");
            var sv = K.Scroll(p, "fit");
            var c0 = sv.contentContainer;
            if (tab == 3)
            {
                if (past.Count == 0) K.Text(c0, "No finished contracts yet.", "dim");
                foreach (var c in Enumerable.Reverse(past))
                {
                    var row = K.Row(c0, "card");
                    K.Swatch(row, Catalog.CustomerColors[c.colorIdx]);
                    K.Text(row, $"{c.customer} - {Catalog.TypeName(c.type)}", "bold").style.width = 330;
                    K.Text(row, c.status.ToString(), c.status == ContractStatus.Completed ? "good" : "bad").style.width = 90;
                    K.Text(row, $"Earned {K.Money(c.earned)}   Uptime {Sim.Uptime(c, false):0.00}%", "small");
                }
                return;
            }
            var list = tab == 0 ? live : tab == 1 ? attention : setup;
            if (list.Count == 0) K.Text(c0, tab == 0 ? (setup.Count > 0 ? "No live contracts yet - see Setting up." : "No active contracts. Visit the Market [M] to sign customers.") : tab == 1 ? "Every live contract is running fine." : "No contracts waiting to go live.", "dim");
            foreach (var c in list)
            {
                var con = c;
                var card = K.Card(c0);
                ContractHeader(card, s, c);
                // Requirements on the left, the contract's actions (added further down) on the right.
                var act = K.Row(card);
                var req = K.Text(act, $"Requires: {Requirement(c)}    SLA {c.sla}%", "small", "wrap");
                req.style.flexGrow = 1; req.style.flexShrink = 1; req.style.flexBasis = 0;
                var bars = K.Row(card);
                Provision P() => Sim.provision.TryGetValue(con.id, out var pv) ? pv : new Provision();
                switch (c.type)
                {
                    case ContractType.Colocation: ReqBar(bars, s, "Racks", () => P().racks, c.reqRacks, "online"); break;
                    case ContractType.Dedicated: ReqBar(bars, s, "Servers", () => P().qualServers, c.reqServers, $"({c.reqCoresPerServer}+ cores)"); break;
                    case ContractType.Compute: ReqBar(bars, s, "Cores", () => P().cores, c.reqCores, "cores"); break;
                    case ContractType.Storage: ReqBar(bars, s, "Storage", () => P().storage, c.reqStorageTB, "TB"); break;
                    case ContractType.GpuCloud: ReqBar(bars, s, "GPUs", () => P().gpus, c.reqGpus, "GPUs"); break;
                }
                ReqBar(bars, s, "Bandwidth", () => P().bandwidth, c.reqBandwidth, "Gbps");
                var sat = BarCell(bars, "Satisfaction");
                sat.style.marginRight = 0;
                K.Bar(s, sat, () => con.satisfaction / 100f,
                    () => con.satisfaction >= 70 ? new Color(0.3f, 0.85f, 0.4f) : con.satisfaction >= 40 ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f),
                    () => $"{con.satisfaction:0}/100" + (con.satisfaction < 35 ? "  - at risk of leaving for a rival!" : con.satisfaction > 75 ? "  - may refer new customers" : ""));
                if (c.redundant) K.Text(card, "REDUNDANT: every rack needs dual PDU, 2 ToR switches and 2 core uplinks.", "small", "good");
                if (c.geoRedundant) K.Text(card, "GEO-REDUNDANT: needs online racks in at least two of your sites.", "small", "good");
                foreach (var t in S.tickets.Where(x => x.contractId == c.id).ToList())
                {
                    var tk = t;
                    var row = K.Row(card, "card");
                    K.Text(s, row, () =>
                    {
                        StaffMember who = null;
                        if (tk.assigned >= 0) foreach (var site in S.sites) { who = site.staff.FirstOrDefault(m => m.id == tk.assigned); if (who != null) break; }
                        return $"TICKET: {tk.text} (rack #{tk.rackId}) - due in {HoursToText(tk.deadlineHour - S.hour)} - fee {K.Money(tk.fee)}" + (who != null ? $" - {who.name} on it" : " - waiting for staff");
                    }, "small", "wrap").style.flexShrink = 1;
                    K.Flex(row);
                    float ec = Sim.ExpediteCost(t);
                    K.Small(s, row, () => $"Contractor {K.Money(ec)}", () => Report(Sim.ExpediteTicket(tk)), () => S.money >= ec);
                }
                var st = K.Text(s, card, () =>
                {
                    int racks = Sim.company.AllRacks().Count(x => x.rack.contractId == con.id);
                    if (con.status == ContractStatus.Setup) return $"SETTING UP - deadline in {HoursToText(con.deadlineHour - S.hour)}. Goes live automatically once requirements are met.   {racks} rack(s) assigned";
                    float mu = Sim.Uptime(con, true);
                    return $"Month {con.monthsServed + 1}/{con.months}   This month uptime {mu:0.000}% (SLA {con.sla}%)   Overall {Sim.Uptime(con, false):0.00}%   Breaches {con.breaches}/{(con.traits.Contains("Strict") ? 2 : 3)}   Earned {K.Money(con.earned)}   {racks} rack(s) assigned";
                }, "small", "wrap");
                K.DynClass(s, st, () => con.status == ContractStatus.Setup ? "warn" : Sim.Uptime(con, true) < con.sla ? "bad" : null);
                var prob = K.Text(s, card, () => !string.IsNullOrEmpty(con.lastIssue) && !con.meeting ? "Problem: " + con.lastIssue : "", "small", "bad");
                K.Show(s, prob, () => prob.text.Length > 0);
                K.Small(s, act, () => $"Assign {gm.selection.Count(o => Catalog.Facility(o.def).kind == FacilityKind.Rack)} selected rack(s)", () =>
                {
                    foreach (var rk in gm.selection.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Rack).ToList()) { var e = Sim.Assign(rk, con); if (e != null) { Toast(e, 2); break; } }
                }, () => gm.selection.Any(o => Catalog.Facility(o.def).kind == FacilityKind.Rack));
                K.Small(s, act, () => gm.view.highlightContract == con.id ? "Stop highlight" : "Highlight racks", () => gm.view.highlightContract = gm.view.highlightContract == con.id ? -1 : con.id);
                K.Small(act, "Select racks", () => { gm.selection.Clear(); gm.selection.AddRange(Sim.Racks.Where(o => o.contractId == con.id)); });
                K.Small(act, $"Cancel ({K.Money(Sim.CancelPenalty(c))})", () => Confirm($"Cancel the contract with {con.customer}? You'll pay a {K.Money(Sim.CancelPenalty(con))} early termination fee and lose reputation.", () => Sim.CancelContract(con)));
            }
        }

        // ---------------------------------------------------------------- research
        void ResearchWindow(VisualElement p, Scope s)
        {
            K.Text(p, "Unlock new hardware with cash once your reputation level is high enough. Complete contracts reliably to gain reputation XP.", "dim");
            // Tab 0 is rack hardware (servers & switches); the rest follow the equipment menu's categories.
            // Each label counts the items that can be researched right now.
            string Ready(int t) { int n = ResearchItems(t).Count(it => !Sim.IsUnlocked(it.id) && S.Level >= it.level); return n > 0 ? $" ({n})" : ""; }
            var labels = new[] { "Servers & Switches" }.Concat(BuildTabs).Select((name, i) => name + Ready(i)).ToArray();
            int tab = Tabs(p, windowSection, "research", labels);
            var sv = K.Scroll(p, "fit");
            var all = ResearchItems(tab);
            if (all.Count == 0) K.Text(sv.contentContainer, "Nothing to research in this category.", "dim");
            for (int lvl = 1; lvl <= Catalog.MaxLevel; lvl++)
            {
                var items = all.Where(x => x.level == lvl).ToList();
                if (items.Count == 0) continue;
                bool reached = S.Level >= lvl;
                K.Text(sv.contentContainer, $"LEVEL {lvl}" + (reached ? "" : $"  (reach {Catalog.XpForLevel(lvl)} XP)"), "section-title");
                foreach (var it in items)
                {
                    var item = it;
                    bool done = Sim.IsUnlocked(it.id);
                    var row = K.Row(sv.contentContainer, "card");
                    K.Text(row, it.name, "wrap", done ? "good" : reached ? "bold" : "dim").style.width = 210;
                    K.Text(row, it.kind, "small", "dim").style.width = 100;
                    var desc = K.Text(row, it.desc, "dim");
                    desc.style.flexGrow = 1; desc.style.flexShrink = 1; desc.style.flexBasis = 0;
                    if (done) K.Text(row, "Unlocked", "small", "good");
                    else K.Small(s, row, () => $"Research {K.Money(item.cost)}", () => Report(Sim.Research(item.id)), () => S.Level >= lvl && S.money >= item.cost).style.width = 150;
                }
            }
        }

        List<(string id, string name, string kind, string desc, float cost, int level)> ResearchItems(int tab)
        {
            var items = new List<(string id, string name, string kind, string desc, float cost, int level)>();
            if (tab == 0)
                foreach (var d in Catalog.Devices.Where(x => x.research > 0))
                    items.Add((d.id, d.name, d.kind == DeviceKind.Switch ? "Switch" : "Server",
                        d.kind == DeviceKind.Switch ? $"{d.ports} ports, {d.uplinkGbps:0}G uplink" : $"{d.heightU}U, {d.cores} cores, {d.storageTB:0} TB{(d.gpus > 0 ? $", {d.gpus} GPU" : "")}, {d.watts:0} W", d.research, d.level));
            else
                foreach (var f in Catalog.Facilities.Where(x => x.research > 0 && InTab(x, tab - 1)))
                    items.Add((f.id, f.name, Catalog.KindName(f.kind), f.desc, f.research, f.level));
            return items;
        }

        // ---------------------------------------------------------------- finance
        void FinanceWindow(VisualElement p, Scope s)
        {
            if (Tabs(p, windowSection, "finance", "This month", "History") == 1) { FinanceHistory(p, s); return; }
            var top = K.Row(p);
            top.style.alignItems = Align.FlexStart;
            var left = K.Card(top);
            left.style.width = 330;
            K.Text(left, "This month so far", "bold");
            void Row(string name, Func<float> v, bool expense)
            {
                var r = K.Row(left);
                K.Text(r, name).style.width = 200;
                var l = K.Text(s, r, () => (expense ? "-" : "+") + K.Money(v()));
                K.DynClass(s, l, () => expense ? (v() > 0 ? "bad" : "dim") : "good");
            }
            Row("Contract income", () => S.current.income, false);
            Row("Bonuses & grants", () => S.current.bonuses, false);
            Row("SLA credits & penalties", () => S.current.penalties, true);
            Row("Electricity", () => S.current.power, true);
            Row("ISP transit fees", () => S.current.network, true);
            Row("Salaries", () => S.current.salaries, true);
            Row("Repairs, research & hiring", () => S.current.other, true);
            var op = K.Text(s, left, () => $"Operating profit: {K.Money(S.current.Operating)}", "bold");
            K.DynClass(s, op, () => S.current.Operating >= 0 ? "good" : "bad");
            Row("Construction & hardware", () => S.current.capex, true);
            var net = K.Text(s, left, () => $"Cash flow after investment: {K.Money(S.current.Net)}", "small");
            K.DynClass(s, net, () => S.current.Net >= 0 ? "good" : "dim");

            var right = K.Card(top);
            right.style.flexGrow = 1;
            K.Text(right, "Operations", "bold");
            K.Text(s, right, () => $"Recurring revenue: {K.Money(Sim.MonthlyRevenue)}/mo", "good");
            K.Text(s, right, () => $"Power at current load: {K.Money(Sim.company.sims.Sum(x => x.gridKW) * (S.city.powerPrice + S.city.carbonTax) * Catalog.HoursPerMonth)}/mo ({Sim.company.TotalLoadKW:0.0} kW across sites)", "wrap");
            K.Text(s, right, () => $"ISP fees: {K.Money(S.sites.Sum(site => site.objects.Where(o => Catalog.Facility(o.def).kind == FacilityKind.Isp).Sum(o => Catalog.Facility(o.def).monthly)))}/mo");
            K.Text(s, right, () => $"Salaries: {K.Money(S.sites.Sum(site => site.staff.Sum(m => m.salary)))}/mo");
            K.Text(s, right, () => { float pue = Sim.itLoadKW > 0 ? Sim.loadKW / Sim.itLoadKW : 0; return $"PUE: {(pue > 0 ? pue.ToString("0.00") : "-")}   IT load {Sim.itLoadKW:0.0} kW (this site)"; });
            K.Text(s, right, () => $"Lifetime revenue: {K.Money(S.lifetimeRevenue)}");
            K.Text(right, $"Seed {S.seed}   {S.difficulty}   {Simulation.Scenario(S.scenario).name}", "dim");
        }

        void FinanceHistory(VisualElement p, Scope s)
        {
            K.Text(p, "Monthly operating profit, before construction & hardware (last 12 months)", "bold");
            var chart = new BarChart();
            chart.style.height = 190;
            chart.data = () => S.reports.Skip(Mathf.Max(0, S.reports.Count - 12)).Select(x => x.Operating).ToList();
            chart.labels = () => S.reports.Skip(Mathf.Max(0, S.reports.Count - 12)).Select(x => $"M{x.month + 1}").ToList();
            p.Add(chart);
            s.Add(chart.Refresh);
            if (S.reports.Count == 0) { K.Text(p, "No completed months yet.", "dim"); return; }
            var sv = K.Scroll(p, "fit");
            var hr = K.Row(sv.contentContainer);
            foreach (var h in new[] { "Month", "Income", "Running costs", "Operating", "Investment", "Cash flow" }) K.Text(hr, h, "bold", "small").style.width = 130;
            foreach (var rp in Enumerable.Reverse(S.reports.Skip(Mathf.Max(0, S.reports.Count - 24)).ToList()))
            {
                var row = K.Row(sv.contentContainer);
                float income = rp.income + rp.bonuses;
                K.Text(row, $"Y{rp.month / 12 + 1} M{rp.month % 12 + 1:00}", "small").style.width = 130;
                K.Text(row, K.Money(income), "small", "good").style.width = 130;
                K.Text(row, K.Money(income - rp.Operating), "small", "dim").style.width = 130;
                K.Text(row, K.Money(rp.Operating), "small", rp.Operating >= 0 ? "good" : "bad").style.width = 130;
                K.Text(row, K.Money(rp.capex), "small", "dim").style.width = 130;
                K.Text(row, K.Money(rp.Net), "small", rp.Net >= 0 ? "good" : "dim").style.width = 130;
            }
        }

        // ---------------------------------------------------------------- staff & land
        void StaffWindow(VisualElement p, Scope s)
        {
            int staffTab = Tabs(p, windowSection, "staff", out var tabs, $"Team ({S.staff.Count})", $"Hire ({S.candidates.Count})", "Roles", "Land");
            K.Text(s, tabs, () => $"Desks {Sim.deskCount}   Day {Sim.ShiftCount("Day")}  Night {Sim.ShiftCount("Night")}   Payroll {K.Money(S.staff.Sum(m => m.salary))}/mo", "dim");
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            if (staffTab == 0)
            {
                if (Sim.deskCount == 0) K.Text(c, "Paint an Office room and place desks. Each desk seats one employee per shift.", "warn");
                K.Text(s, c, () =>
                {
                    int failed = S.objects.Count(o => o.failed || o.pduTripped) + S.objects.Sum(o => o.devices.Count(d => d.failed));
                    return $"Open work: {failed} broken item(s), {S.tickets.Count} ticket(s).   Tickets resolved {S.ticketsDone}, missed {S.ticketsMissed}.   Security level {Sim.SecurityLevel}.";
                }, "wrap");
                if (S.staff.Count == 0) K.Text(c, "No employees at this site yet. Open the Hire tab.", "dim");
                foreach (var m in S.staff.ToList())
                {
                    var mm = m;
                    var row = K.Row(c, "card");
                    K.Swatch(row, WorldView.RoleColor(m.role), 12);
                    K.Text(row, m.name, "bold").style.width = 140;
                    K.Text(row, $"{Simulation.RoleName(m.role)}  {new string('*', m.skill)}", "small").style.width = 150;
                    K.Text(s, row, () => Sim.OnShift(mm) ? mm.shift : mm.shift + " (off)", "small").style.width = 72;
                    var fat = K.Div(row);
                    fat.style.width = 60;
                    K.Bar(s, fat, () => mm.fatigue / 100f, () => mm.fatigue > 60 ? new Color(1f, 0.4f, 0.3f) : new Color(0.6f, 0.6f, 0.7f), null, 10);
                    var stl = K.Text(s, row, () => StaffState(mm), "small");
                    stl.style.width = 160;
                    stl.style.marginLeft = 6;
                    K.DynClass(s, stl, () => Sim.HasDesk(mm) ? null : "warn");
                    K.Text(row, $"{K.Money(m.salary)}/mo", "small");
                    K.Flex(row);
                    K.Small(row, m.shift == "Night" ? "To day" : "To night", () => { Sim.SetShift(mm, mm.shift == "Night" ? "Day" : "Night"); windowSection.Invalidate(); });
                    K.Small(row, "Let go", () => Confirm($"Let {mm.name} go?", () => Sim.FireStaff(mm)));
                }
            }
            else if (staffTab == 1)
            {
                K.Text(c, "New candidates arrive every week. The Roles tab explains what each role does.", "dim");
                if (S.candidates.Count == 0) K.Text(c, "No candidates right now.", "dim");
                foreach (var cand in S.candidates.ToList())
                {
                    var cd = cand;
                    var row = K.Row(c, "card");
                    K.Swatch(row, WorldView.RoleColor(cand.role), 12);
                    K.Text(row, cand.name, "bold").style.width = 150;
                    K.Text(row, $"{Simulation.RoleName(cand.role)}  {new string('*', cand.skill)}", "small").style.width = 170;
                    K.Text(row, $"{K.Money(cand.salary)}/mo", "small").style.width = 90;
                    K.Flex(row);
                    float fee = Sim.HireFee(cand);
                    K.Small(s, row, () => $"Hire day ({K.Money(fee)})", () => Report(Sim.HireCandidate(cd, "Day")), () => Sim.ShiftCount("Day") < Sim.deskCount && S.money >= fee);
                    K.Small(s, row, () => "Hire night +15%", () => Report(Sim.HireCandidate(cd, "Night")), () => Sim.ShiftCount("Night") < Sim.deskCount && S.money >= fee);
                }
            }
            else if (staffTab == 2)
            {
                K.Text(c, "Matching a job to the right role makes it 50% faster; skill speeds up all work. Desks are shared between the day (07-19) and night (19-07) shifts.", "dim");
                foreach (var role in Simulation.Roles)
                {
                    var row = K.Row(c, "card");
                    K.Swatch(row, WorldView.RoleColor(role), 12);
                    K.Text(row, Simulation.RoleName(role), "bold").style.width = 160;
                    K.Text(row, Simulation.RoleDesc(role), "dim").style.flexShrink = 1;
                }
            }
            else
            {
                K.Text(c, $"You own a {S.ownX1 - S.ownX0} x {S.ownY1 - S.ownY0} lot in {S.city.name}. Buy 4-tile strips to expand. Land price multiplier: x{S.city.landPrice:0.00}.", "dim");
                for (int d = 0; d < 4; d++)
                {
                    int dir = d;
                    var row = K.Row(c);
                    K.Text(row, Simulation.DirNames[d]).style.width = 80;
                    if (Sim.CanExpand(d))
                    {
                        float cost = Sim.LandCost(d);
                        K.Btn(s, row, () => $"Buy strip ({K.Money(cost)})", () => { Report(Sim.BuyLand(dir)); windowSection.Invalidate(); }, () => S.money >= cost).style.width = 220;
                    }
                    else K.Text(row, "Map edge", "dim");
                }
            }
        }

        string StaffState(StaffMember m)
        {
            if (!Sim.HasDesk(m)) return "No desk on this shift!";
            if (m.state == "off" || !Sim.OnShift(m)) return "Off shift";
            if (m.role == "Security") return "On patrol";
            string tired = m.fatigue > 60 ? " (tired)" : "";
            string where = m.floor > 0 ? $" [F{m.floor + 1}]" : "";
            switch (m.state)
            {
                case "idle": return "Idle at desk" + tired;
                case "walking": return $"Walking to #{m.taskObj}{where}" + tired;
                case "working": return (m.taskTicket >= 0 ? $"Ticket at #{m.taskObj}" : $"Repairing #{m.taskObj}") + where + tired;
                default: return "Returning" + tired;
            }
        }

        // ---------------------------------------------------------------- log
        void LogWindow(VisualElement p, Scope s)
        {
            int tab = Tabs(p, windowSection, "log", "All", "Problems", "Warnings", "Good news");
            var sv = K.Scroll(p, "fit");
            int shown = 0;
            for (int i = S.log.Count - 1; i >= 0 && shown < 200; i--)
            {
                var e = S.log[i];
                if (tab == 1 && e.severity != 3 || tab == 2 && e.severity != 2 || tab == 3 && e.severity != 1) continue;
                shown++;
                int d = e.hour / 24;
                var row = K.Row(sv.contentContainer);
                row.style.alignItems = Align.FlexStart;
                K.Text(row, $"Y{d / 360 + 1} M{(d / 30) % 12 + 1:00} D{d % 30 + 1:00} {e.hour % 24:00}:00", "dim").style.width = 130;
                var l = K.Text(row, e.text, "wrap", e.severity == 3 ? "bad" : e.severity == 2 ? "warn" : e.severity == 1 ? "good" : null);
                l.style.flexShrink = 1;
            }
            if (shown == 0) K.Text(sv.contentContainer, "Nothing here yet.", "dim");
        }

        // ---------------------------------------------------------------- goals
        void GoalsWindow(VisualElement p, Scope s)
        {
            int tab = Tabs(p, windowSection, "goals", "Overview", $"Achievements ({S.achievements.Count}/{Simulation.Achievements.Count})");
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            if (tab == 1)
            {
                foreach (var a in Simulation.Achievements)
                {
                    bool got = S.achievements.Contains(a.id), ever = Simulation.UnlockedEver(a.id);
                    var r = K.Row(c, "card");
                    K.Text(r, got ? "[*]" : ever ? "[o]" : "[ ]", got ? "good" : "dim").style.width = 30;
                    K.Text(r, a.name, got ? "bold" : null).style.width = 170;
                    K.Text(r, a.desc + (ever && !got ? "  (earned in another game)" : ""), "dim");
                }
                return;
            }
            var sc = Simulation.Scenario(S.scenario);
            var card = K.Card(c);
            K.Text(card, $"Scenario: {sc.name}", "bold");
            K.Text(card, sc.goal, "wrap");
            if (sc.id != "sandbox" && sc.id != "creative")
            {
                K.Bar(s, card, () => Sim.ScenarioProgress(out _), () => S.scenarioDone ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.4f, 0.6f, 1f), () => { Sim.ScenarioProgress(out string t); return t; }, 18);
                K.Text(s, card, () => S.scenarioDone ? "Scenario finished - endless mode." : $"Days left: {Mathf.Max(0, sc.days - S.Day)}", "dim");
            }
            var row = K.Row(c);
            row.style.alignItems = Align.FlexStart;
            var sc1 = K.Card(row);
            sc1.style.width = 330;
            K.Text(s, sc1, () => $"Score: {Sim.Score:N0}", "header");
            K.Text(s, sc1, () => $"Lifetime revenue {K.Money(S.lifetimeRevenue)}", "small");
            K.Text(s, sc1, () => $"Contracts completed {S.contractsCompleted}, failed {S.contractsFailed}", "small");
            K.Text(s, sc1, () => $"Achievements {S.achievements.Count}/{Simulation.Achievements.Count}" + (S.creative ? " (disabled in Sandbox)" : ""), "small");
            var ipo = K.Card(row);
            ipo.style.flexGrow = 1;
            K.Text(ipo, "Go public (IPO)", "bold");
            K.Text(s, ipo, () => S.ipo ? "Your company is publicly traded." : Sim.CanIpo(out string why) ? "You qualify! An IPO raises a year of revenue in cash." : why, "wrap");
            if (!S.ipo) K.Btn(s, ipo, () => "Ring the bell (IPO)", Sim.DoIpo, () => Sim.CanIpo(out _));
        }

        // ---------------------------------------------------------------- dashboard
        void DashboardWindow(VisualElement p, Scope s)
        {
            K.Text(s, p, () => $"Last {S.dayStats.Count} days.   PUE {(Sim.itLoadKW > 0 ? (Sim.loadKW / Sim.itLoadKW).ToString("0.00") : "-")}   Solar now {Sim.solarKW:0}/{Sim.solarCapKW:0} kW   Grid price now ${Sim.PowerPriceNow:0.000}/kWh (+${S.city.carbonTax:0.000} carbon)", "dim");
            // All six charts at once, three to a row.
            var grid = K.Row(p);
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginRight = -6;
            void Chart(string title, Func<DayStat, float> f, Color color, string fmt)
            {
                var cell = K.Div(grid);
                cell.style.width = Length.Percent(33.33f);
                cell.style.paddingRight = 6;
                K.Text(cell, title, "small", "bold");
                var ch = new LineChart { color = color, format = fmt, data = () => S.dayStats.Select(f).ToList() };
                ch.style.height = 120;
                cell.Add(ch);
                s.Add(ch.Refresh);
            }
            Chart("Cash", d => d.money, new Color(0.4f, 1f, 0.5f), "${0:N0}");
            Chart("Monthly recurring revenue", d => d.revenue, new Color(0.4f, 0.7f, 1f), "${0:N0}");
            Chart("Daily operating cost", d => d.cost, new Color(1f, 0.4f, 0.4f), "${0:N0}");
            Chart("Facility load (all sites)", d => d.loadKW, new Color(1f, 0.6f, 0.3f), "{0:0} kW");
            Chart("Avg customer service level", d => d.uptime, new Color(1f, 0.85f, 0.3f), "{0:0.0}%");
            Chart("Hottest hall", d => d.temp, new Color(1f, 0.5f, 0.2f), "{0:0.0} C");
        }

        // ---------------------------------------------------------------- sites
        void SitesWindow(VisualElement p, Scope s)
        {
            var co = Sim.company;
            var cands = co.SiteCandidates().ToList();
            int tab = Tabs(p, windowSection, "sites", $"Your sites ({co.sims.Count})", $"Expand ({cands.Count})");
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            for (int i = 0; tab == 0 && i < co.sims.Count; i++)
            {
                var site = co.sims[i];
                var d = S.sites[i];
                int idx = i;
                var row = K.Row(c, "card");
                var col = K.Div(row);
                col.style.width = 230;
                K.Text(col, (i == S.activeSite ? "> " : "") + d.name, "bold");
                K.Text(col, $"{d.city.name} - {d.city.climate} - ${d.city.powerPrice:0.000}/kWh", "dim");
                K.Text(s, row, () => $"{site.Racks.Count} racks\n{site.loadKW:0}/{site.availableKW:0} kW", "small").style.width = 110;
                K.Text(s, row, () => $"Hot {(site.zones.Count > 0 ? site.zones.Max(z => z.temp) : 0):0.0}C\n{d.staff.Count} staff", "small").style.width = 90;
                var stl = K.Text(s, row, () =>
                {
                    int down = site.racks.Values.Count(x => !x.online && !x.empty);
                    int broken = d.objects.Count(o => o.failed || o.pduTripped) + d.objects.Sum(o => o.devices.Count(x => x.failed));
                    return down > 0 || broken > 0 ? $"{down} racks down\n{broken} broken" : "All good";
                }, "small");
                stl.style.width = 110;
                K.DynClass(s, stl, () => stl.text == "All good" ? "good" : "warn");
                K.Flex(row);
                if (i != S.activeSite) K.Btn(row, "Go to", () => { OpenWindow(Window.None); gm.SwitchSite(idx); }).style.width = 80;
            }
            if (tab == 0) return;
            K.Text(c, $"Each site has its own climate, power prices, grid, land and staff. Contracts can use racks in any site, and geo-redundant customers require racks in at least two. Unlocks at reputation level {Company.MinLevelForSites}.", "dim");
            foreach (var city in cands)
            {
                var ct = city;
                float cost = co.SiteCost(city);
                var row = K.Row(c, "card");
                var col = K.Div(row, "grow");
                col.style.flexShrink = 1;
                K.Text(col, $"{city.name}  ({city.climate})", "bold");
                K.Text(col, $"Cooling x{city.coolingFactor:0.00}   Power ${city.powerPrice:0.000}/kWh{(city.carbonTax > 0 ? $" +${city.carbonTax:0.000} carbon" : "")}   Land x{city.landPrice:0.00}   Risk x{city.disasterRate:0.00}   Demand x{city.demandFactor:0.00}", "small", "wrap");
                K.Text(col, city.blurb, "dim");
                K.Btn(s, row, () => co.CanOpenSite(ct) ?? $"Open site {K.Money(cost)}",
                    () => Confirm($"Buy land and open a data center in {ct.name} for {K.Money(cost)}?", () => { OpenWindow(Window.None); gm.OpenSite(ct); }),
                    () => co.CanOpenSite(ct) == null).style.width = 210;
            }
        }

        // ---------------------------------------------------------------- sandbox
        void SandboxWindow(VisualElement p, Scope s)
        {
            K.Text(p, "Creative mode: change the rules and poke the simulation. Achievements are disabled for this game.", "dim");
            int tab = Tabs(p, windowSection, "sandbox", "Rules & Money", "World", "Events");
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            if (tab == 1) { SandboxWorld(c); return; }
            if (tab == 2) { SandboxEvents(c); return; }
            K.Text(c, "RULES", "section-title");
            void Toggle(string label, Func<bool> get, Action<bool> set) => K.Btn(s, c, () => (get() ? "[x] " : "[ ] ") + label, () => set(!get()), null, get);
            Toggle("Unlimited money", () => S.cheatInfiniteMoney, v => S.cheatInfiniteMoney = v);
            Toggle("No hardware or facility failures", () => S.cheatNoFailures, v => S.cheatNoFailures = v);
            Toggle("No random events", () => S.cheatNoEvents, v => S.cheatNoEvents = v);
            Toggle("No bankruptcy", () => S.cheatNoBankruptcy, v => S.cheatNoBankruptcy = v);

            K.Text(c, "MONEY & PROGRESSION", "section-title");
            var m = K.Row(c);
            K.Small(m, "+$100k", () => Sim.SandboxAddMoney(100_000));
            K.Small(m, "+$1M", () => Sim.SandboxAddMoney(1_000_000));
            K.Small(m, "+$10M", () => Sim.SandboxAddMoney(10_000_000));
            K.Small(m, "Unlock all tech", Sim.SandboxUnlockAll);
            var lv = K.Row(c);
            K.Text(s, lv, () => $"Reputation level {S.Level}").style.width = 160;
            K.Small(lv, "-1", () => Sim.SandboxSetLevel(S.Level - 1));
            K.Small(lv, "+1", () => Sim.SandboxSetLevel(S.Level + 1));
            K.Small(lv, "Max", () => Sim.SandboxSetLevel(Catalog.MaxLevel));
            var rep = K.Row(c);
            K.Text(s, rep, () => $"Rating {S.rating:0}").style.width = 160;
            K.Small(rep, "-10", () => S.rating = Mathf.Max(0, S.rating - 10));
            K.Small(rep, "+10", () => S.rating = Mathf.Min(100, S.rating + 10));
        }

        void SandboxWorld(VisualElement c)
        {
            var w = K.Row(c);
            K.Small(w, "New contract offer", Sim.SandboxSpawnOffer);
            K.Small(w, "Repair everything", Sim.SandboxRepairAll);
            K.Small(w, "Skip 1 day", () => Sim.SandboxSkipDays(1));
            K.Small(w, "Skip 7 days", () => Sim.SandboxSkipDays(7));
            var wx = K.Row(c);
            foreach (var weather in new[] { "Clear", "Cloudy", "Rain", "Storm", "Snow" })
            {
                var wv = weather;
                K.Small(wx, wv, () => S.weather = wv);
            }
        }

        void SandboxEvents(VisualElement c)
        {
            K.Text(c, "Trigger an event right now:", "dim");
            var ev = K.Row(c);
            ev.style.flexWrap = Wrap.Wrap;
            foreach (var e in Simulation.SandboxEvents)
            {
                var type = e;
                K.Small(ev, type, () => Sim.SandboxTrigger(type)).style.width = 100;
            }
        }
    }
}
