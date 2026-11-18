using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// v3 business systems: compliance certification, private cages, competitive RFP bids, hyperscale anchor
    /// tenants, shareholders after the IPO, rival acquisitions, incident post-mortems and customer-portal notes.
    /// </summary>
    public partial class Simulation
    {
        // ================================================================ satisfaction notes (customer portal)
        public void AddSat(Contract c, float delta, string reason)
        {
            c.satisfaction = Mathf.Clamp(c.satisfaction + delta, 0, 100);
            if (string.IsNullOrEmpty(reason)) return;
            c.satNotes.Add($"D{S.Day + 1}|{(delta >= 0 ? "+" : "")}{delta:0}|{reason}");
            if (c.satNotes.Count > 10) c.satNotes.RemoveAt(0);
        }

        void DailySatHistory()
        {
            foreach (var c in S.contracts)
            {
                if (c.status != ContractStatus.Active && c.status != ContractStatus.Setup) continue;
                c.satHistory.Add(c.satisfaction);
                if (c.satHistory.Count > 60) c.satHistory.RemoveAt(0);
            }
        }

        // ================================================================ compliance certification
        public const float AuditCost = 35000f;
        public const int AuditDays = 5, CertDays = 360;

        public bool Certified(SiteData site) => site.certUntilHour > S.hour;
        public bool AuditRunning(SiteData site) => site.auditEndHour > S.hour;

        /// <summary>What an auditor checks at this site. Empty list = would pass.</summary>
        public List<string> ComplianceGaps()
        {
            var gaps = new List<string>();
            if (!S.objects.Any(o => !o.failed && Catalog.Facility(o.def).kind == FacilityKind.AccessControl))
                gaps.Add("Access Control & CCTV in a corridor");
            var occupied = zones.Where(z => Racks.Any(r => r.devices.Count > 0 && ZoneAt(r) == z.id)).ToList();
            int unprotected = occupied.Count(z => !z.suppressed);
            if (unprotected > 0) gaps.Add($"Fire suppression in every occupied server hall ({unprotected} missing)");
            bool day = S.staff.Any(m => m.role == "Security" && (m.shift ?? "Day") == "Day" && HasDesk(m));
            bool night = S.staff.Any(m => m.role == "Security" && m.shift == "Night" && HasDesk(m));
            if (!day || !night) gaps.Add("Security staff on both the day and night shifts");
            if (S.hour - Site.lastBreachHour < 90 * Catalog.HoursPerDay) gaps.Add("No successful break-in in the last 90 days");
            return gaps;
        }

        public string StartAudit()
        {
            if (AuditRunning(Site)) return "An audit is already under way";
            if (!Spend(AuditCost, false)) return "Not enough money";
            Site.auditEndHour = S.hour + AuditDays * Catalog.HoursPerDay;
            Log($"Compliance auditors arrived at {S.siteName}. Results in {AuditDays} days.", 0);
            return null;
        }

        void AuditHour()
        {
            if (Site.auditEndHour < 0 || S.hour < Site.auditEndHour) return;
            Site.auditEndHour = -1;
            var gaps = ComplianceGaps();
            if (gaps.Count == 0)
            {
                Site.certUntilHour = S.hour + CertDays * Catalog.HoursPerDay;
                Unlock("certified");
                Log($"{S.siteName} passed its compliance audit. Certified for a year.", 1);
                popups.Enqueue(new Popup { title = "Audit passed", body = $"{S.siteName} is now certified for compliance-sensitive customers (finance, healthcare, government) for {CertDays} days.\n\nA successful break-in voids the certificate." }.Add("OK", null));
            }
            else
            {
                Log($"{S.siteName} failed its compliance audit: {string.Join("; ", gaps)}.", 3);
                popups.Enqueue(new Popup { title = "Audit failed", body = $"The auditors found problems at {S.siteName}:\n- " + string.Join("\n- ", gaps) + "\n\nFix them and book another audit." }.Add("OK", null));
            }
        }

        void OnSecurityBreach()
        {
            Site.lastBreachHour = S.hour;
            if (Certified(Site))
            {
                Site.certUntilHour = -1;
                Log($"The break-in voided {S.siteName}'s compliance certificate.", 3);
            }
        }

        // ================================================================ private cages
        public const float CageCost = 120f;
        int[] cageComp;

        void RebuildCageComponents()
        {
            cageComp = new int[S.mapW * S.mapH * GameState.MaxFloors];
            if (S.flags == null) return;
            int next = 1;
            var stack = new Stack<int>();
            for (int i = 0; i < cageComp.Length; i++)
            {
                if (cageComp[i] != 0 || (S.flags[i] & GameState.FlagCage) == 0) continue;
                cageComp[i] = next;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    int fl = t / S.Plane, rem = t % S.Plane;
                    int x = rem % S.mapW, y = rem / S.mapW;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (!S.InBounds(nx, ny)) continue;
                        int n = S.Idx(fl, nx, ny);
                        if (cageComp[n] != 0 || (S.flags[n] & GameState.FlagCage) == 0) continue;
                        cageComp[n] = next;
                        stack.Push(n);
                    }
                }
                next++;
            }
        }

        /// <summary>Cage the rack stands in (0 = not caged).</summary>
        public int CageOf(PlacedObject o) => cageComp == null ? 0 : cageComp[S.Idx(o.floor, o.x, o.y)];

        // ================================================================ RFP bids
        public float YourBidReputation
        {
            get
            {
                var active = S.contracts.Where(c => c.status == ContractStatus.Active).ToList();
                float sat = active.Count > 0 ? active.Average(c => c.satisfaction) : 60f;
                float up = active.Count > 0 ? active.Average(c => Uptime(c, false)) : 99.5f;
                return Mathf.Clamp(25f + S.Level * 5f + (sat - 50f) * 0.3f + Mathf.Clamp((up - 99f) * 15f, -20f, 15f), 5f, 100f);
            }
        }

        static float BidScore(float mul, float rep) => 60f / Mathf.Max(0.5f, mul) + rep * 0.4f;

        void MaybeNewRfp()
        {
            if (S.Level < 3 || S.rfps.Count >= 2 || rng.NextDouble() > 0.05) return;
            var c = ProcGen.GenerateContract(S, rng);
            float k = ProcGen.Range(rng, 2.2f, 3.2f);
            ScaleContract(c, k, k * 0.92f);
            c.months = Mathf.RoundToInt(c.months * 1.5f);
            c.setupDays += 8;
            c.rfp = true;
            if (!c.traits.Contains("RFP")) c.traits.Add("RFP");
            c.flavor = $"{c.customer} has opened a competitive tender. " + c.flavor;
            var rfp = new Rfp { id = S.NewId(), contract = c, closesHour = S.hour + rng.Next(6, 10) * Catalog.HoursPerDay };
            S.rfps.Add(rfp);
            Log($"Request for proposals: {c.customer} wants {ProcGen.Describe(c).ToLower()}. Bids close in {(rfp.closesHour - S.hour) / 24} days.", 0);
        }

        /// <summary>Multiplies a contract's requirements by <paramref name="k"/> and its pay by <paramref name="pay"/>.</summary>
        static void ScaleContract(Contract c, float k, float pay)
        {
            c.reqRacks = Mathf.Max(1, Mathf.RoundToInt(c.reqRacks * k));
            c.reqServers = Mathf.RoundToInt(c.reqServers * k);
            c.reqCores = Mathf.CeilToInt(c.reqCores * k / 8f) * 8;
            c.reqStorageTB = Mathf.Ceil(c.reqStorageTB * k / 10f) * 10f;
            c.reqGpus = Mathf.CeilToInt(c.reqGpus * k / 4f) * 4;
            c.reqBandwidth = Mathf.Round(c.reqBandwidth * k * 2f) / 2f;
            c.monthlyPay = Mathf.Round(c.monthlyPay * pay / 50f) * 50f;
            c.signingBonus = Mathf.Round(c.signingBonus * pay / 50f) * 50f;
        }

        public string PlaceBid(Rfp r, float mul)
        {
            if (!S.rfps.Contains(r)) return "This tender has closed";
            r.bidMul = Mathf.Clamp(mul, 0.7f, 1.2f);
            Log($"Bid {K0(r.contract.monthlyPay * r.bidMul)}/month for {r.contract.customer}'s tender.", 0);
            return null;
        }

        /// <summary>Chance-free estimate of whether a bid at <paramref name="mul"/> beats the field (rivals bid around 0.82-1.02).</summary>
        public float BidWinChance(float mul)
        {
            float mine = BidScore(mul, YourBidReputation);
            int beat = 0, total = 0;
            foreach (var rv in S.rivals)
                for (float m = 0.82f; m <= 1.021f; m += 0.02f) { total++; if (mine > BidScore(m - rv.aggression * 0.05f, rv.reputation)) beat++; }
            if (total == 0) return 1f;
            // All rivals must be beaten.
            return Mathf.Pow(beat / (float)total, Mathf.Max(1, S.rivals.Count) * 0.6f);
        }

        void RfpHour()
        {
            foreach (var r in S.rfps.ToList())
            {
                if (S.hour < r.closesHour) continue;
                S.rfps.Remove(r);
                var c = r.contract;
                string winner = null; float best = r.bidMul > 0 ? BidScore(r.bidMul, YourBidReputation) : -1f;
                foreach (var rv in S.rivals)
                {
                    float m = ProcGen.Range(rng, 0.82f, 1.02f) - rv.aggression * 0.05f;
                    float sc = BidScore(m, rv.reputation);
                    if (sc > best) { best = sc; winner = rv.name; r.bestRivalMul = m; }
                }
                if (r.bidMul <= 0) { if (winner != null) { var rv0 = S.rivals.First(x => x.name == winner); rv0.customers++; } continue; }
                if (winner == null)
                {
                    c.monthlyPay = Mathf.Round(c.monthlyPay * r.bidMul / 50f) * 50f;
                    c.status = ContractStatus.Setup;
                    c.deadlineHour = S.hour + c.setupDays * Catalog.HoursPerDay;
                    S.contracts.Add(c);
                    S.rfpsWon++;
                    Unlock("rfp");
                    Log($"You won the {c.customer} tender at {K0(c.monthlyPay)}/month! Setup deadline in {c.setupDays} days.", 1);
                    popups.Enqueue(new Popup { title = "Tender won", body = $"{c.customer} chose your bid: {ProcGen.Describe(c)} at {K0(c.monthlyPay)}/month for {c.months} months.\n\nYou have {c.setupDays} days to bring it online." }.Add("Great", null));
                    Recompute();
                }
                else
                {
                    S.rfpsLost++;
                    var rv = S.rivals.First(x => x.name == winner);
                    rv.customers++;
                    Log($"{winner} won the {c.customer} tender (they bid about {r.bestRivalMul * 100:0}% of budget).", 2);
                }
            }
        }

        // ================================================================ hyperscale anchor tenants
        public const int AnchorLevel = 7;

        void MaybeNewAnchor()
        {
            if (S.Level < AnchorLevel || rng.NextDouble() > 0.015) return;
            if (S.offers.Any(o => o.anchor) || S.contracts.Any(c => c.anchor && (c.status == ContractStatus.Active || c.status == ContractStatus.Setup))) return;
            Contract c = null;
            for (int i = 0; i < 12 && c == null; i++)
            {
                var t = ProcGen.GenerateContract(S, rng);
                if (t.type == ContractType.Compute || t.type == ContractType.GpuCloud) c = t;
            }
            if (c == null) return;
            ScaleContract(c, 8f, 7f);
            c.anchor = true;
            c.months = 36;
            c.setupDays = 30;
            c.traits.RemoveAll(t => t == "Rush" || t == "Flexible" || t == "Long-term");
            c.traits.Add("Anchor");
            c.reqRacksFull = c.reqRacks; c.reqServersFull = c.reqServers; c.reqCoresFull = c.reqCores;
            c.reqGpusFull = c.reqGpus; c.reqStorageFull = c.reqStorageTB; c.reqBandwidthFull = c.reqBandwidth;
            SetAnchorPhase(c, 0.4f);
            c.expiresHour = S.hour + 20 * Catalog.HoursPerDay;
            c.flavor = $"{c.customer} is a hyperscaler looking for an anchor site: a dedicated hall that ramps up to {ProcGen.Describe(FullCopy(c)).ToLower()} over the first six months.";
            S.offers.Add(c);
            Log($"Hyperscale anchor tenant! {c.customer} wants a dedicated hall for 36 months.", 1);
            popups.Enqueue(new Popup { title = "Anchor tenant", body = c.flavor + $"\n\nPhase 1 (40%) must be live within {c.setupDays} days, 70% by month 3 and 100% by month 6. Pay ramps with each phase, up to {K0(c.monthlyPay / c.phase)}/month.\n\nSee the Market." }.Add("OK", null));
        }

        static Contract FullCopy(Contract c) => new Contract { type = c.type, reqRacks = c.reqRacksFull, reqServers = c.reqServersFull, reqCores = c.reqCoresFull, reqGpus = c.reqGpusFull, reqStorageTB = c.reqStorageFull, reqCoresPerServer = c.reqCoresPerServer, reqKWPerRack = c.reqKWPerRack };

        static void SetAnchorPhase(Contract c, float phase)
        {
            float full = c.monthlyPay / Mathf.Max(0.01f, c.phase);
            c.phase = phase;
            c.reqRacks = Mathf.Max(1, Mathf.RoundToInt(c.reqRacksFull * phase));
            c.reqServers = Mathf.RoundToInt(c.reqServersFull * phase);
            c.reqCores = Mathf.CeilToInt(c.reqCoresFull * phase / 8f) * 8;
            c.reqGpus = Mathf.CeilToInt(c.reqGpusFull * phase / 4f) * 4;
            c.reqStorageTB = Mathf.Ceil(c.reqStorageFull * phase / 10f) * 10f;
            c.reqBandwidth = Mathf.Round(c.reqBandwidthFull * phase * 2f) / 2f;
            c.monthlyPay = Mathf.Round(full * phase / 50f) * 50f;
        }

        void AnchorMonth(Contract c)
        {
            if (!c.anchor) return;
            float next = c.monthsServed >= 6 ? 1f : c.monthsServed >= 3 ? 0.7f : 0.4f;
            if (next <= c.phase + 0.001f) return;
            SetAnchorPhase(c, next);
            Log($"{c.customer} is ramping up to {next * 100:0}%: {ProcGen.Describe(c).ToLower()}.", 2);
            popups.Enqueue(new Popup { title = "Anchor ramp-up", body = $"{c.customer} has moved to phase {(next >= 1f ? 3 : 2)}: they now need {ProcGen.Describe(c)} and pay {K0(c.monthlyPay)}/month.\n\nProvision the extra capacity in their dedicated hall right away." }.Add("OK", null));
            Recompute();
        }

        // ================================================================ shareholders (after the IPO)
        public const int QuarterDays = 90;

        void StartQuarter()
        {
            S.quarterEndHour = S.hour + QuarterDays * Catalog.HoursPerDay;
            S.quarterStartMrr = MonthlyRevenue;
            S.quarterTarget = Mathf.Round(MonthlyRevenue * 1.06f / 1000f) * 1000f;
        }

        void ShareholdersDaily()
        {
            if (!S.ipo) return;
            if (S.sharePrice <= 0 || S.shares <= 0) ListShares();
            float progress = S.quarterTarget > 0 ? MonthlyRevenue / S.quarterTarget : 1f;
            float drift = 0.0015f * (S.boardConfidence - 50f) / 50f + 0.002f * Mathf.Clamp(progress - 1f, -0.5f, 0.5f);
            float noise = ProcGen.Range(rng, -0.025f, 0.025f);
            S.sharePrice = Mathf.Max(0.5f, S.sharePrice * (1f + drift + noise));
            if (S.hour >= S.quarterEndHour) EndQuarter();
        }

        void EndQuarter()
        {
            var active = S.contracts.Where(c => c.status == ContractStatus.Active).ToList();
            float up = active.Count > 0 ? active.Average(c => Uptime(c, false)) : 100f;
            bool met = MonthlyRevenue >= S.quarterTarget && up >= 99.5f;
            float target = S.quarterTarget;
            if (met)
            {
                S.quartersMet++;
                S.boardConfidence = Mathf.Min(100, S.boardConfidence + 10);
                S.sharePrice *= 1.07f;
                Log($"Quarter closed: you beat guidance ({K0(MonthlyRevenue)}/mo vs {K0(target)}). Shares +7%.", 1);
            }
            else
            {
                S.quartersMissed++;
                S.boardConfidence = Mathf.Max(0, S.boardConfidence - 18);
                S.sharePrice *= 0.88f;
                Log($"Quarter closed: you missed guidance ({K0(MonthlyRevenue)}/mo vs {K0(target)}, uptime {up:0.00}%). Shares -12%.", 3);
                if (S.boardConfidence <= 0 && !S.cheatNoBankruptcy && !S.gameOver)
                {
                    S.gameOver = true;
                    popups.Enqueue(new Popup { title = "Replaced by the board", body = "After quarter upon quarter of missed guidance, the board has voted to replace you as CEO. Game over.\n\nScore: " + Score.ToString("N0") }.Add("OK", null));
                }
                else if (S.boardConfidence < 25)
                    popups.Enqueue(new Popup { title = "Board warning", body = $"Board confidence is down to {S.boardConfidence:0}/100. Miss guidance again and they may replace you.\n\nGuidance: grow recurring revenue 6% per quarter with 99.5%+ average uptime." }.Add("OK", null));
            }
            StartQuarter();
        }

        /// <summary>Sets up the share register at the IPO: valued at three years of recurring revenue, $20 a share.</summary>
        void ListShares()
        {
            S.sharePrice = S.ipoSharePrice = 20f;
            S.shares = Mathf.Round(Mathf.Max(100000f, MonthlyRevenue) * 36f / S.sharePrice);
            S.boardConfidence = 70f;
            StartQuarter();
        }

        /// <summary>Market value of the company.</summary>
        public float MarketCap => S.ipo ? S.sharePrice * S.shares : 0f;

        public float IssueSharesAmount => Mathf.Round(MarketCap * 0.06f / 10000f) * 10000f;

        public string IssueShares()
        {
            if (!S.ipo) return "Only listed companies can issue shares";
            float amt = IssueSharesAmount;
            S.shares += amt / S.sharePrice;
            S.money += amt;
            S.current.borrowed += amt;
            S.sharePrice *= 0.92f;
            S.boardConfidence = Mathf.Max(0, S.boardConfidence - 6);
            Log($"Issued new shares and raised {K0(amt)}. Shareholders were diluted (-8%).", 2);
            return null;
        }

        public float BuybackCost => Mathf.Round(MarketCap * 0.03f / 10000f) * 10000f;

        public string Buyback()
        {
            if (!S.ipo) return "Only listed companies can buy back shares";
            float cost = BuybackCost;
            if (S.money < cost) return "Not enough money";
            S.money -= cost;
            S.current.repaid += cost;
            S.shares = Mathf.Max(1f, S.shares - cost / S.sharePrice);
            S.sharePrice *= 1.05f;
            S.boardConfidence = Mathf.Min(100, S.boardConfidence + 4);
            Log($"Bought back {K0(cost)} of shares. Shares +5%.", 1);
            return null;
        }

        // ================================================================ rival acquisitions
        public const int AcquireLevel = 6;

        public float AcquisitionPrice(Rival r) => Mathf.Round((150000f + r.customers * 70000f) * (0.5f + r.reputation / 100f) / 10000f) * 10000f;

        /// <summary>Cash part of the price: half of it is paid in shares after the IPO.</summary>
        public float AcquisitionCash(Rival r) => S.ipo ? Mathf.Round(AcquisitionPrice(r) * 0.5f / 10000f) * 10000f : AcquisitionPrice(r);

        public string CanAcquire(Rival r)
        {
            if (S.Level < AcquireLevel && !S.ipo) return $"Requires reputation level {AcquireLevel} or an IPO";
            if (S.rivals.Count <= 1) return "Regulators won't let you buy the last competitor";
            if (S.money < AcquisitionCash(r)) return "Not enough money";
            return null;
        }

        public string Acquire(Rival r)
        {
            var err = CanAcquire(r);
            if (err != null) return err;
            float cash = AcquisitionCash(r);
            S.money -= cash;
            S.current.capex += cash;
            if (S.ipo) { S.sharePrice *= 0.95f; S.boardConfidence = Mathf.Max(0, S.boardConfidence - 3); }
            S.rivals.Remove(r);
            S.rivalsAcquired++;
            S.xp += 250 + r.customers * 20;
            int n = Mathf.Clamp(r.customers / 2, 1, 5);
            for (int i = 0; i < n; i++)
            {
                var c = ProcGen.GenerateContract(S, rng);
                c.acquired = true;
                if (!c.traits.Contains("Acquired")) c.traits.Add("Acquired");
                c.expiresHour += 10 * Catalog.HoursPerDay;
                c.flavor = $"Former {r.name} customer. " + c.flavor;
                S.offers.Add(c);
            }
            Unlock("takeover");
            Log($"You acquired {r.name} for {K0(AcquisitionPrice(r))}{(S.ipo ? " (half in shares)" : "")}. {n} of their customers want to move to your sites.", 1);
            popups.Enqueue(new Popup { title = "Acquisition complete", body = $"{r.name} is now part of your company. {n} of their customers are in the Market waiting to sign with you, and there's one less competitor poaching your deals." }.Add("OK", null));
            return null;
        }

        void MaybeNewRival()
        {
            if (S.rivals.Count >= 2 || rng.NextDouble() > 0.01) return;
            var r = new System.Random(S.seed + S.hour);
            var rv = new Rival
            {
                name = ProcGen.Pick(r, RivalA) + " " + ProcGen.Pick(r, RivalB),
                reputation = ProcGen.Range(r, 25, 45),
                aggression = ProcGen.Range(r, 0.5f, 1f),
                customers = r.Next(1, 4),
                colorIdx = r.Next(Catalog.CustomerColors.Length),
            };
            if (S.rivals.Any(x => x.name == rv.name)) return;
            S.rivals.Add(rv);
            Log($"A new competitor, {rv.name}, opened in the market.", 2);
        }

        // ================================================================ incidents & post-mortems
        public Incident OpenIncident => S.incidentOpen && S.incidents.Count > 0 ? S.incidents[S.incidents.Count - 1] : null;

        static string NormalizeCause(string issue)
        {
            if (string.IsNullOrEmpty(issue)) return null;
            int ri = issue.IndexOf("rack issue:");
            string k = ri >= 0 ? issue.Substring(ri + 11).Trim() : issue;
            k = System.Text.RegularExpressions.Regex.Replace(k, "#[0-9]+", "");
            k = System.Text.RegularExpressions.Regex.Replace(k, @"\([^)]*\)", "").Trim();
            return k;
        }

        void IncidentHour()
        {
            var down = S.contracts.Where(c => c.status == ContractStatus.Active && !c.meeting).ToList();
            var inc = OpenIncident;
            if (down.Count > 0)
            {
                if (inc == null)
                {
                    inc = new Incident { id = S.NewId(), startHour = S.hour, site = S.activeSite };
                    S.incidents.Add(inc);
                    S.incidentOpen = true;
                    if (S.incidents.Count > 30) S.incidents.RemoveAt(0);
                }
                foreach (var c in down)
                {
                    inc.customerHours += 1f - c.service;
                    if (!inc.customers.Contains(c.customer)) inc.customers.Add(c.customer);
                    var cause = NormalizeCause(c.lastIssue);
                    if (cause != null && !inc.causes.Contains(cause) && inc.causes.Count < 8) inc.causes.Add(cause);
                }
                for (int i = 0; i < S.sites.Count; i++)
                    foreach (var e in S.sites[i].events)
                    {
                        string label = company.MultiSite ? $"{e.label} ({S.sites[i].name})" : e.label;
                        if (!inc.events.Contains(label) && inc.events.Count < 6) inc.events.Add(label);
                    }
                return;
            }
            if (inc == null) return;
            S.incidentOpen = false;
            inc.endHour = S.hour;
            inc.fixes = SuggestFixes(inc);
            inc.major = inc.customerHours >= 3f || inc.endHour - inc.startHour >= 4;
            if (inc.major && S.ipo) { S.sharePrice *= 0.97f; S.boardConfidence = Mathf.Max(0, S.boardConfidence - 2); }
            if (inc.major && S.postMortemPopups)
                popups.Enqueue(new Popup { title = "Incident post-mortem", body = PostMortemText(inc) }.Add("OK", null).Add("Stop showing these", () => S.postMortemPopups = false));
        }

        public string PostMortemText(Incident inc)
        {
            int hours = (inc.endHour >= 0 ? inc.endHour : S.hour) - inc.startHour;
            var sb = new System.Text.StringBuilder();
            sb.Append($"Duration {hours}h, {inc.customerHours:0.0} customer-hours of downtime affecting {string.Join(", ", inc.customers)}.\n");
            if (inc.events.Count > 0) sb.Append("\nWhat was happening: " + string.Join(", ", inc.events) + ".\n");
            if (inc.causes.Count > 0) sb.Append("\nCause chain:\n- " + string.Join("\n- ", inc.causes) + "\n");
            var fixes = inc.fixes.Count > 0 ? inc.fixes : SuggestFixes(inc);
            if (fixes.Count > 0) sb.Append("\nSuggested fixes:\n- " + string.Join("\n- ", fixes));
            return sb.ToString();
        }

        static List<string> SuggestFixes(Incident inc)
        {
            var f = new List<string>();
            void Add(string s) { if (!f.Contains(s)) f.Add(s); }
            foreach (var c in inc.causes.Concat(inc.events))
            {
                string k = c.ToLowerInvariant();
                if (k.Contains("no backup") || k.Contains("grid outage") || k.Contains("power blip")) Add("Keep UPS capacity above your load, and add generators (or battery storage) for long outages.");
                if (k.Contains("insufficient power")) Add("Add transformer and switchgear capacity - keep 20% headroom over peak load.");
                if (k.Contains("thermal") || k.Contains("heat wave")) Add("Add cooling headroom in the hot hall and fix hot/cold aisle orientation.");
                if (k.Contains("pdu")) Add("Fit dual PDUs so one tripped feed doesn't take a rack down; hire an electrician.");
                if (k.Contains("tor switch") || k.Contains("no tor")) Add("Run two ToR switches per rack.");
                if (k.Contains("core switch") || k.Contains("core uplink")) Add("Add a second core switch and cable uplink B to it.");
                if (k.Contains("isp") || k.Contains("fiber") || k.Contains("saturated") || k.Contains("ddos") || k.Contains("traffic surge")) Add("Add ISP capacity or a second ISP line, and Security staff against DDoS.");
                if (k.Contains("maintenance")) Add("Schedule maintenance in the night window, outside customer peak hours, or make the racks redundant.");
                if (k.Contains("needs") || k.Contains("has ")) Add("Provision ~10% spare capacity so a single failure doesn't breach the SLA.");
                if (k.Contains("fire")) Add("Install fire suppression in every server hall.");
                if (k.Contains("flood")) Add("Lay raised floors in ground-floor server halls.");
                if (k.Contains("leak")) Add("Service cooling units regularly (Maintenance) - worn equipment fails more.");
                if (k.Contains("cage") || k.Contains("certified") || k.Contains("dedicated hall")) Add("Fix the contract's placement rules (cage, certification or dedicated hall) shown in Contracts.");
            }
            if (f.Count == 0) Add("Spread customers over redundant racks and keep staff on both shifts for fast repairs.");
            return f;
        }
    }
}
