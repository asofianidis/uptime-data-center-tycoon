using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    /// <summary>Panels for the v3 systems: loans, insurance, maintenance, tenders, spot market, customer portal,
    /// incidents, shareholders, acquisitions and compliance. Each is a tab inside an existing window.</summary>
    public partial class GameUI
    {
        static readonly Color Good = new Color(0.3f, 0.85f, 0.4f), Warn = new Color(1f, 0.75f, 0.2f), Bad = new Color(1f, 0.3f, 0.25f), Blue = new Color(0.4f, 0.6f, 1f);

        static VisualElement Grow(VisualElement e) { e.style.flexGrow = 1; e.style.flexShrink = 1; e.style.flexBasis = 0; return e; }

        /// <summary>Compact money: $950, $12.5k, $2.4M.</summary>
        static string Short(float v)
        {
            float a = Mathf.Abs(v);
            string s = a >= 1_000_000f ? $"{a / 1_000_000f:0.#}M" : a >= 10_000f ? $"{a / 1000f:0}k" : a >= 1000f ? $"{a / 1000f:0.#}k" : $"{a:0}";
            return (v < 0 ? "-$" : "$") + s;
        }

        // ---------------------------------------------------------------- finance: loans
        void FinanceLoans(VisualElement p, Scope s)
        {
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            var top = K.Row(c);
            top.style.alignItems = Align.FlexStart;
            var score = K.Card(top);
            score.style.width = 330;
            K.Text(s, score, () => $"Credit score {S.creditScore:0} - {Simulation.CreditBand(S.creditScore)}", "header");
            K.Bar(s, score, () => (S.creditScore - 300f) / 550f, () => S.creditScore >= 700 ? Good : S.creditScore >= 600 ? Warn : Bad, null, 10);
            K.Text(score, "Heading towards (updated monthly):", "small", "dim");
            K.Text(s, score, () => { var why = new List<string>(); float t = Sim.CreditTarget(why); return $"{t:0}\n" + string.Join("\n", why); }, "small", "wrap");

            var lim = K.Card(top);
            Grow(lim);
            K.Text(lim, "Borrowing", "bold");
            K.Text(s, lim, () => $"Debt {K.Money(Sim.TotalDebt)}   Limit {K.Money(Sim.BorrowingLimit)}   Available {K.Money(Sim.LoanHeadroom)}", "wrap");
            K.Text(s, lim, () => $"Monthly debt service: {K.Money(Sim.MonthlyDebtService)}", "small");
            K.Text(lim, "The limit grows with your recurring revenue, owned equipment and credit score. Missed payments add a late fee and hurt your score.", "small", "dim", "wrap");

            K.Text(c, "TERM LOANS", "section-title");
            K.Text(c, "Fixed monthly payments. Repaying early costs a 1% fee.", "small", "dim");
            float[] amounts = { 50000, 100000, 250000, 500000, 1000000, 2500000 };
            foreach (int months in new[] { 12, 24, 48 })
            {
                int m = months;
                var row = K.Row(c, "card");
                row.style.flexWrap = Wrap.Wrap;
                K.Text(s, row, () => $"{m} months at {Sim.LoanRate(m) * 100:0.0}%", "bold").style.width = 150;
                foreach (var a in amounts)
                {
                    float amt = a;
                    Tip(K.Small(s, row, () => $"{Short(amt)}: {Short(Simulation.Payment(amt, Sim.LoanRate(m), m))}/mo", () => Report(Sim.TakeLoan(amt, m)),
                        () => amt <= Sim.LoanHeadroom + 1 && S.creditScore >= 480), $"Borrow {K.Money(amt)} over {m} months").style.flexGrow = 1;
                }
            }

            K.Text(c, "CREDIT LINE", "section-title");
            var cl = K.Row(c, "card");
            K.Text(s, cl, () => $"Used {K.Money(Sim.CreditLine?.balance ?? 0)} of {K.Money(Sim.CreditLineLimit)} at {Sim.CreditLineRate * 100:0.0}% (interest only)", "small").style.width = 330;
            K.Flex(cl);
            foreach (var a in new float[] { 25000, 100000 })
            {
                float amt = a;
                K.Small(s, cl, () => $"Draw {K.Money(amt)}", () => Report(Sim.DrawCredit(amt)), () => (Sim.CreditLine?.balance ?? 0) + amt <= Sim.CreditLineLimit + 1);
            }
            K.Small(s, cl, () => "Repay all", () => { var l = Sim.CreditLine; if (l != null) Report(Sim.Repay(l)); }, () => Sim.CreditLine != null && S.money >= Sim.CreditLine.balance);

            K.Text(c, $"YOUR LOANS ({S.loans.Count(l => !l.creditLine)})", "section-title");
            if (!S.loans.Any(l => !l.creditLine)) K.Text(c, "No outstanding loans.", "dim");
            foreach (var l in S.loans.Where(x => !x.creditLine).ToList())
            {
                var ln = l;
                var row = K.Row(c, "card");
                K.Text(row, ln.lender, "bold").style.width = 150;
                K.Text(s, row, () => $"Balance {K.Money(ln.balance)}   {K.Money(ln.payment)}/mo   {ln.monthsLeft} months left   {ln.rate * 100:0.0}%", "small").style.flexShrink = 1;
                K.Flex(row);
                K.Small(s, row, () => "Pay $50k", () => Report(Sim.Repay(ln, 50000)), () => ln.balance > 50000 && S.money >= 50500);
                K.Small(s, row, () => $"Repay all ({K.Money(ln.balance * 1.01f)})", () => Report(Sim.Repay(ln)), () => S.money >= ln.balance * 1.01f);
            }
        }

        // ---------------------------------------------------------------- finance: insurance
        void FinanceInsurance(VisualElement p, Scope s)
        {
            var sv = K.Scroll(p, "fit");
            var c = sv.contentContainer;
            var top = K.Row(c);
            top.style.alignItems = Align.FlexStart;
            var info = K.Card(top);
            info.style.width = 330;
            K.Text(s, info, () => $"Insured value {K.Money(Sim.InsuredValue)}", "bold");
            K.Text(s, info, () => $"Claims paid {S.claimsMade} ({K.Money(S.claimsPaidTotal)} total)", "small");
            K.Text(info, "Your premium depends on how well protected you are:", "small", "dim", "wrap");
            K.Text(s, info, () => { var why = new List<string>(); float m = Sim.InsuranceRisk(why); return $"Risk x{m:0.00}\n" + string.Join("\n", why); }, "small", "wrap");
            var tiers = K.Div(top);
            Grow(tiers);
            for (int t = 0; t < Simulation.InsuranceTiers.Length; t++)
            {
                int tier = t;
                var card = K.Card(tiers);
                var head = K.Row(card);
                K.Text(head, Simulation.InsuranceTiers[t], "bold");
                K.Flex(head);
                K.Text(s, head, () => tier == 0 ? "$0" : $"{K.Money(Sim.PremiumFor(tier))}/mo");
                K.Text(card, Simulation.InsuranceDesc(t), "small", "dim", "wrap");
                K.Btn(s, card, () => S.insuranceTier == tier ? "Current policy" : "Switch to this policy", () => Sim.SetInsurance(tier), () => S.insuranceTier != tier, () => S.insuranceTier == tier);
            }
        }

        // ---------------------------------------------------------------- staff: maintenance
        void StaffMaintenance(VisualElement c, Scope s)
        {
            K.Text(c, "Equipment that hasn't been serviced fails more often: 0.6x right after a service, normal after 4 months, rising past that. While staff service a rack it goes offline unless it has dual PDUs and two ToR switches; a serviced facility is out of action until the job is done.", "dim", "wrap");
            var row = K.Row(c, "card");
            K.Btn(s, row, () => (S.autoMaintenance ? "[x] " : "[ ] ") + "Auto-schedule maintenance", () => S.autoMaintenance = !S.autoMaintenance, null, () => S.autoMaintenance).style.width = 250;
            K.Btn(s, row, () => (S.maintNightOnly ? "[x] " : "[ ] ") + "Only in the night window (22-06)", () => S.maintNightOnly = !S.maintNightOnly, null, () => S.maintNightOnly).style.width = 270;
            var iv = K.Row(c);
            K.Text(iv, "Service every:").style.width = 110;
            foreach (int d in new[] { 60, 90, 120, 180 })
            {
                int dd = d;
                K.Btn(s, iv, () => $"{dd} days", () => S.maintIntervalDays = dd, null, () => S.maintIntervalDays == dd).style.width = 90;
            }
            K.Text(c, "Customer peak hours: downtime during a customer's peak counts 50% extra against their SLA. The night window avoids most peaks. Auto-maintenance skips units with no spare (N+1) capacity - service those by hand when you can afford the risk, or add a spare.", "small", "dim", "wrap");

            var items = S.objects.Where(o => Simulation.Serviceable(Catalog.Facility(o.def)) && (Catalog.Facility(o.def).kind != FacilityKind.Rack || o.devices.Count > 0))
                .OrderByDescending(o => Sim.ServiceAgeMonths(o)).ToList();
            var due = items.Where(o => Sim.MaintDue(o) && !o.maintQueued).ToList();
            var head = K.Row(c);
            K.Text(head, $"OVERDUE ({due.Count})   QUEUED ({items.Count(o => o.maintQueued)})", "section-title");
            K.Flex(head);
            if (due.Count > 0) K.Small(head, "Queue all overdue", () => { foreach (var o in due) Sim.QueueMaintenance(o, false); windowSection.Invalidate(); });
            foreach (var o in items.Take(40))
            {
                var ob = o;
                var r = K.Row(c, "card");
                var d = Catalog.Facility(o.def);
                K.Text(r, $"{d.name} #{o.id}" + (o.floor > 0 ? $" (F{o.floor + 1})" : ""), "small").style.width = 220;
                float months = Sim.ServiceAgeMonths(o);
                K.Text(r, $"{months:0.0} mo", "small", Sim.MaintDue(o) ? "warn" : "dim").style.width = 60;
                K.Text(r, $"fail x{Simulation.ServiceFactorFor(months):0.00}", "small", "dim").style.width = 80;
                var stt = K.Text(s, r, () => ob.inMaint ? "Being serviced" : ob.maintQueued ? (ob.maintNow || !S.maintNightOnly ? "Queued" : "Queued (night)") : Sim.MaintRisk(ob) != null ? "No spare - manual" : Sim.MaintSafe(ob) ? "Redundant" : "", "small");
                stt.style.width = 120;
                K.DynClass(s, stt, () => !ob.maintQueued && Sim.MaintRisk(ob) != null ? "warn" : null);
                s.Add(() => { var risk = Sim.MaintRisk(ob); stt.tooltip = risk ?? ""; });
                K.Flex(r);
                if (o.maintQueued) K.Small(r, "Cancel", () => { Sim.CancelMaintenance(ob); windowSection.Invalidate(); });
                else K.Small(r, $"Service now ({K.Money(Sim.MaintCost(o))})", () => { Report(Sim.QueueMaintenance(ob, true)); windowSection.Invalidate(); });
            }
        }

        // ---------------------------------------------------------------- market: tenders
        void MarketTenders(VisualElement c, Scope s)
        {
            K.Text(c, "Big customers run competitive tenders. Bid a share of their budget: lower bids win more often, and your reputation (level, uptime, customer satisfaction) counts too. Rivals bid 80-100%.", "dim", "wrap");
            K.Text(s, c, () => $"Your bid reputation {Sim.YourBidReputation:0}/100   Tenders won {S.rfpsWon}, lost {S.rfpsLost}", "small");
            if (S.rfps.Count == 0) K.Text(c, S.Level < 3 ? "Tenders open up at reputation level 3." : "No open tenders right now.", "dim");
            foreach (var r in S.rfps.ToList())
            {
                var rfp = r;
                var con = r.contract;
                var card = K.Card(c);
                ContractHeader(card, s, con, () => $"closes in {HoursToText(rfp.closesHour - S.hour)}");
                K.Text(card, $"Requires: {Requirement(con)}   SLA {con.sla}%   Term {con.months} months   Setup {con.setupDays} days", "small", "wrap");
                string est = Estimate(con);
                K.Text(card, est.StartsWith("!") ? est.Substring(1) : "Estimate: " + est, est.StartsWith("!") ? "bad" : "dim");
                var row = K.Row(card);
                K.Text(s, row, () => rfp.bidMul > 0 ? $"Your bid: {K.Money(con.monthlyPay * rfp.bidMul)}/mo ({rfp.bidMul * 100:0}%)" : "No bid yet", rfp.bidMul > 0 ? "good" : "dim").style.width = 230;
                foreach (var m in new[] { 0.8f, 0.9f, 1.0f, 1.1f })
                {
                    float mul = m;
                    K.Small(s, row, () => $"{mul * 100:0}%: {K.Money(con.monthlyPay * mul)} ({Sim.BidWinChance(mul) * 100:0}% win)", () => { Report(Sim.PlaceBid(rfp, mul)); windowSection.Invalidate(); }, null).style.flexGrow = 1;
                }
            }
        }

        // ---------------------------------------------------------------- market: spot
        void MarketSpot(VisualElement c, Scope s)
        {
            K.Text(c, "Sell the idle capacity of online racks that aren't assigned to a contract. Spot buyers pay a fraction of contract rates, prices move daily, and their traffic shares your ISP links with your SLA customers.", "dim", "wrap");
            K.Btn(s, c, () => (S.spotEnabled ? "[x] " : "[ ] ") + "Sell idle capacity on the spot market", () => S.spotEnabled = !S.spotEnabled, null, () => S.spotEnabled);
            var card = K.Card(c);
            K.Text(s, card, () => $"Prices now: {K.Money(Sim.SpotCoreHour * 1000)} per 1,000 core-hours (x{S.spotCoreMul:0.00})   {K.Money(Sim.SpotGpuHour)} per GPU-hour (x{S.spotGpuMul:0.00})   {K.Money(Sim.SpotTBHour * 1000)} per 1,000 TB-hours", "wrap");
            K.Text(s, card, () => $"Earned so far: {K.Money(S.spotEarnedTotal)}   Waste heat sold: {K.Money(S.heatSoldTotal)}", "small");
            foreach (var sim in Sim.company.sims)
            {
                var si = sim;
                var row = K.Row(c, "card");
                K.Text(row, si.Site.name, "bold").style.width = 140;
                K.Text(s, row, () =>
                {
                    si.SpotCapacity(out int cores, out int gpus, out float tb);
                    return $"Idle: {cores} cores, {gpus} GPUs, {tb:0} TB   Spot traffic {si.spotGbps:0.0} Gbps   WAN {si.bwDemand:0.0}/{si.ispGbps:0} G" + (S.spotEnabled ? $"   {K.Money(si.spotRevenueHour * 24)}/day" : "");
                }, "small", "wrap").style.flexShrink = 1;
            }
        }

        // ---------------------------------------------------------------- contracts: customer portal
        int portalContract = -1;

        void ContractPortal(VisualElement c, Scope s)
        {
            var open = S.contracts.Where(x => x.status == ContractStatus.Active || x.status == ContractStatus.Setup).ToList();
            if (open.Count == 0) { K.Text(c, "No live customers yet.", "dim"); return; }
            var con = open.FirstOrDefault(x => x.id == portalContract) ?? open[0];
            portalContract = con.id;
            var pick = K.Row(c);
            pick.style.flexWrap = Wrap.Wrap;
            foreach (var x in open)
            {
                var xx = x;
                var b = K.Btn(s, pick, () => xx.customer, () => { portalContract = xx.id; windowSection.Invalidate(); }, null, () => portalContract == xx.id);
                b.AddToClassList("btn-small");
                if (x.satisfaction < 40 || (x.status == ContractStatus.Active && !x.meeting)) b.AddToClassList("bad");
            }
            var card = K.Card(c);
            ContractHeader(card, s, con);
            var cols = K.Row(card);
            cols.style.alignItems = Align.FlexStart;
            var left = K.Div(cols);
            Grow(left);
            K.Text(left, "THIS MONTH", "section-title");
            float budget = Simulation.DowntimeBudget(con);
            var dbar = K.Bar(s, left, () => con.monthDown / budget, () => con.monthDown > budget ? Bad : con.monthDown > budget * 0.6f ? Warn : Good,
                () => $"{con.monthDown:0.0}h of {budget:0}h downtime allowed ({con.sla}% SLA)", 16);
            dbar.style.flexGrow = 0;
            K.Text(s, left, () => con.status == ContractStatus.Setup ? $"Setting up - deadline in {HoursToText(con.deadlineHour - S.hour)}" : $"Uptime this month {Sim.Uptime(con, true):0.000}%   overall {Sim.Uptime(con, false):0.000}%", "small");
            K.Text(left, $"Peak hours {Simulation.PeakText(con)} - downtime then counts 1.5x", "small", "dim");
            K.Text(s, left, () => Simulation.InPeak(con, S.hour) ? "In peak hours now" : "Off-peak now", "small");
            var rules = new List<string>();
            if (con.redundant) rules.Add("Redundant racks");
            if (con.geoRedundant) rules.Add("Two sites");
            if (con.compliance) rules.Add("Certified sites only");
            if (con.caged) rules.Add("One private cage");
            if (con.efficient) rules.Add($"PUE {Simulation.EfficientPue:0.00} or better");
            if (con.traits.Contains("Eco")) rules.Add("25%+ solar");
            if (con.anchor) rules.Add($"Dedicated hall, phase {con.phase * 100:0}%");
            if (rules.Count > 0) K.Text(left, "Rules: " + string.Join(", ", rules), "small", "wrap");
            var prob = K.Text(s, left, () => !string.IsNullOrEmpty(con.lastIssue) && !con.meeting ? "Problem: " + con.lastIssue : "", "small", "bad", "wrap");
            K.Show(s, prob, () => prob.text.Length > 0);

            var right = K.Div(cols);
            Grow(right);
            K.Text(right, "SATISFACTION (last 60 days)", "section-title");
            var ch = new LineChart { color = Good, format = "{0:0}", data = () => con.satHistory.Count > 0 ? con.satHistory.Concat(new[] { con.satisfaction }).ToList() : new List<float> { con.satisfaction } };
            ch.style.height = 110;
            right.Add(ch);
            s.Add(ch.Refresh);
            K.Text(c, "WHY IT CHANGED", "section-title");
            if (con.satNotes.Count == 0) K.Text(c, "Nothing notable yet.", "dim");
            foreach (var n in Enumerable.Reverse(con.satNotes))
            {
                var parts = n.Split('|');
                if (parts.Length < 3) continue;
                var r = K.Row(c);
                K.Text(r, parts[0], "small", "dim").style.width = 60;
                K.Text(r, parts[1], "small", parts[1].StartsWith("-") ? "bad" : "good").style.width = 40;
                K.Text(r, parts[2], "small", "wrap").style.flexShrink = 1;
            }
        }

        // ---------------------------------------------------------------- log: incidents
        void LogIncidents(VisualElement p, Scope s)
        {
            var bar = K.Row(p);
            K.Text(bar, "Every stretch of customer downtime is recorded with what caused it and how to prevent it.", "small", "dim");
            K.Flex(bar);
            K.Btn(s, bar, () => (S.postMortemPopups ? "[x] " : "[ ] ") + "Pop up major incidents", () => S.postMortemPopups = !S.postMortemPopups, null, () => S.postMortemPopups);
            var sv = K.Scroll(p, "fit");
            if (S.incidents.Count == 0) K.Text(sv.contentContainer, "No incidents yet. Long may it last.", "dim");
            foreach (var inc in Enumerable.Reverse(S.incidents).ToList())
            {
                var i = inc;
                var card = K.Card(sv.contentContainer);
                int d = inc.startHour / 24;
                var head = K.Row(card);
                K.Text(head, $"Y{d / 360 + 1} M{(d / 30) % 12 + 1:00} D{d % 30 + 1:00} {inc.startHour % 24:00}:00", "bold").style.width = 140;
                K.Text(s, head, () => i.endHour < 0 ? "ONGOING" : $"{i.endHour - i.startHour}h", i.endHour < 0 ? "bad" : "small").style.width = 70;
                K.Text(s, head, () => $"{i.customerHours:0.0} customer-hours", inc.major ? "warn" : "dim").style.width = 150;
                K.Text(head, string.Join(", ", inc.customers.Take(3)) + (inc.customers.Count > 3 ? $" +{inc.customers.Count - 3}" : ""), "small", "dim").style.flexShrink = 1;
                K.Text(s, card, () => Sim.PostMortemText(i), "small", "wrap");
            }
        }

        // ---------------------------------------------------------------- goals: corporate (shareholders & acquisitions)
        void GoalsCorporate(VisualElement c, Scope s)
        {
            var sh = K.Card(c);
            K.Text(sh, "Shareholders", "bold");
            if (!S.ipo)
            {
                K.Text(s, sh, () => Sim.CanIpo(out string why) ? "You qualify for an IPO (Overview tab)." : "Not listed yet. " + why, "dim", "wrap");
                K.Text(sh, "After the IPO, shareholders expect 6% recurring-revenue growth every quarter with 99.5%+ uptime. Beat guidance and the share price climbs; keep missing it and the board will replace you.", "small", "dim", "wrap");
            }
            else
            {
                K.Text(s, sh, () => $"Share price ${S.sharePrice:0.00} (IPO ${S.ipoSharePrice:0.00})   Market cap {K.Money(Sim.MarketCap)}");
                K.Text(s, sh, () => $"Board confidence {S.boardConfidence:0}/100", S.boardConfidence < 30 ? "bad" : "small");
                K.Bar(s, sh, () => S.boardConfidence / 100f, () => S.boardConfidence >= 60 ? Good : S.boardConfidence >= 30 ? Warn : Bad, null, 8);
                K.Text(s, sh, () => $"This quarter: {K.Money(Sim.MonthlyRevenue)} / {K.Money(S.quarterTarget)} per month target, {HoursToText(S.quarterEndHour - S.hour)} left.   Quarters met {S.quartersMet}, missed {S.quartersMissed}", "small", "wrap");
                K.Bar(s, sh, () => S.quarterTarget > 0 ? Sim.MonthlyRevenue / S.quarterTarget : 1, () => Sim.MonthlyRevenue >= S.quarterTarget ? Good : Blue, null, 8);
                var row = K.Row(sh);
                K.Small(s, row, () => $"Issue shares (+{K.Money(Sim.IssueSharesAmount)})", () => Report(Sim.IssueShares()));
                K.Small(s, row, () => $"Buy back shares ({K.Money(Sim.BuybackCost)})", () => Report(Sim.Buyback()), () => S.money >= Sim.BuybackCost);
                var chart = new LineChart { color = Blue, format = "${0:0.00}", data = () => S.dayStats.Where(x => x.share > 0).Select(x => x.share).ToList() };
                chart.style.height = 100;
                sh.Add(chart);
                s.Add(chart.Refresh);
            }

            K.Text(c, "ACQUISITIONS", "section-title");
            K.Text(c, $"Buy a rival to take over their customers and remove a competitor. Available from reputation level {Simulation.AcquireLevel} or after the IPO, when half the price is paid in shares.", "small", "dim", "wrap");
            foreach (var rv in S.rivals.ToList())
            {
                var r = rv;
                var row = K.Row(c, "card");
                K.Swatch(row, Catalog.CustomerColors[rv.colorIdx], 12);
                K.Text(row, rv.name, "bold").style.width = 180;
                K.Text(s, row, () => $"rep {r.reputation:0}   {r.customers} customers   price {K.Money(Sim.AcquisitionPrice(r))}" + (S.ipo ? $" ({K.Money(Sim.AcquisitionCash(r))} cash)" : ""), "small").style.flexShrink = 1;
                K.Flex(row);
                K.Small(s, row, () => Sim.CanAcquire(r) ?? "Acquire", () => Confirm($"Buy {r.name} for {K.Money(Sim.AcquisitionPrice(r))}?", () => { Report(Sim.Acquire(r)); windowSection.Invalidate(); }), () => Sim.CanAcquire(r) == null);
            }
            K.Text(s, c, () => $"Rivals acquired: {S.rivalsAcquired}", "small", "dim");
        }

        // ---------------------------------------------------------------- sites: compliance
        void SitesCompliance(VisualElement c, Scope s)
        {
            K.Text(c, "Finance, healthcare and government customers may require their racks to be in certified sites. An audit checks each site and certifies it for a year; a successful break-in voids the certificate.", "dim", "wrap");
            for (int i = 0; i < Sim.company.sims.Count; i++)
            {
                var sim = Sim.company.sims[i];
                var site = S.sites[i];
                int idx = i;
                var card = K.Card(c);
                var head = K.Row(card);
                K.Text(head, site.name, "bold").style.width = 160;
                var st = K.Text(s, head, () => sim.AuditRunning(site) ? $"Audit in progress ({HoursToText(site.auditEndHour - S.hour)})" : sim.Certified(site) ? $"Certified ({HoursToText(site.certUntilHour - S.hour)} left)" : "Not certified");
                K.DynClass(s, st, () => sim.Certified(site) ? "good" : sim.AuditRunning(site) ? "warn" : "dim");
                K.Flex(head);
                K.Small(s, head, () => $"Book audit ({K.Money(Simulation.AuditCost)})", () => Sim.company.WithSite(idx, () => Report(sim.StartAudit())), () => !sim.AuditRunning(site) && S.money >= Simulation.AuditCost);
                K.Text(s, card, () =>
                {
                    List<string> gaps = null;
                    Sim.company.WithSite(idx, () => gaps = sim.ComplianceGaps());
                    return gaps.Count == 0 ? "Ready: would pass an audit today." : "Missing:\n- " + string.Join("\n- ", gaps);
                }, "small", "wrap");
            }
        }
    }
}
