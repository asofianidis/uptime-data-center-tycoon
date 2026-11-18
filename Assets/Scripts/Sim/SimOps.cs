using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// v3 operations: preventive maintenance, staff growth (XP, training, morale, raises), PUE tracking,
    /// water for evaporative cooling, district-heat sales, the spot compute market, neighbour noise and
    /// hardware deliveries.
    /// </summary>
    public partial class Simulation
    {
        // ================================================================ v3 migration
        /// <summary>Fills in climate-derived values for cities generated before v3. Deterministic per city name.</summary>
        public static void EnsureCityV3(CityProfile c)
        {
            if (c == null || c.v3) return;
            c.v3 = true;
            var r = new System.Random((c.name ?? "city").GetHashCode() ^ 0x5bd1e995);
            float j = ProcGen.Range(r, -0.08f, 0.08f);
            switch (c.climate)
            {
                case "Arctic": c.humidity = 0.4f + j; c.waterPrice = ProcGen.Range(r, 1.5f, 2.5f); c.heatPrice = 0.09f; break;
                case "Cold": c.humidity = 0.55f + j; c.waterPrice = ProcGen.Range(r, 1.5f, 3f); c.heatPrice = 0.06f; break;
                case "Hot": c.humidity = 0.78f + j; c.waterPrice = ProcGen.Range(r, 2.5f, 4f); c.heatPrice = 0.01f; break;
                case "Desert": c.humidity = 0.12f + j * 0.5f; c.waterPrice = ProcGen.Range(r, 4.5f, 7f); c.heatPrice = 0f; break;
                default: c.humidity = 0.6f + j; c.waterPrice = ProcGen.Range(r, 1.8f, 3.2f); c.heatPrice = 0.035f; break;
            }
            c.waterPrice = Mathf.Round(c.waterPrice * 100f) / 100f;
            c.noiseLimit = Mathf.Round(ProcGen.Range(r, 50f, 60f));
        }

        void MigrateV3()
        {
            if (S.version >= 3) return;
            foreach (var site in S.sites)
                foreach (var o in site.objects)
                    if (o.placedHour < 0) o.placedHour = S.hour;
            S.version = 3;
        }

        // ================================================================ preventive maintenance
        public static bool Serviceable(FacilityDef d) =>
            d != null && d.kind != FacilityKind.Desk && d.kind != FacilityKind.SoundWall && d.kind != FacilityKind.LoadingDock && d.kind != FacilityKind.Elevator;

        public float ServiceAgeMonths(PlacedObject o)
        {
            int from = o.servicedHour >= 0 ? o.servicedHour : o.placedHour >= 0 ? o.placedHour : 0;
            return Mathf.Max(0, S.hour - from) / (float)Catalog.HoursPerMonth;
        }

        /// <summary>Failure-rate multiplier from time since the last service: fresh 0.6x, normal by 4 months, then rising.</summary>
        public static float ServiceFactorFor(float months) =>
            months < 1f ? 0.6f : months < 4f ? 0.6f + (months - 1f) * 0.4f / 3f : Mathf.Min(3f, 1f + (months - 4f) * 0.12f);

        public float ServiceFactor(PlacedObject o) => ServiceFactorFor(ServiceAgeMonths(o));

        public bool MaintDue(PlacedObject o) => ServiceAgeMonths(o) * Catalog.DaysPerMonth >= S.maintIntervalDays;

        public float MaintCost(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            if (d.kind == FacilityKind.Rack) return 150f + o.devices.Count * 25f;
            return Mathf.Round(d.cost * 0.02f / 10f) * 10f;
        }

        static float MaintWork(PlacedObject o)
        {
            var k = Catalog.Facility(o.def).kind;
            return k == FacilityKind.Rack || k == FacilityKind.CoreSwitch || k == FacilityKind.Isp ? 2f : 4f;
        }

        /// <summary>True when this object keeps working while it is serviced (redundant rack, or another unit covers it).</summary>
        public bool MaintSafe(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            if (d.kind == FacilityKind.Rack) return o.dualPdu && o.devices.Count(x => !x.failed && Catalog.Device(x.def)?.kind == DeviceKind.Switch) >= 2;
            return false;
        }

        public bool NightWindow { get { int h = S.hour % 24; return h >= 22 || h < 6; } }

        /// <summary>
        /// Why servicing this unit now would hurt (no N+1 spare), or null when it is safe. Auto-maintenance skips
        /// risky units; the player can still service them by hand.
        /// </summary>
        public string MaintRisk(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            if (d == null || o.inMaint) return null;
            switch (d.kind)
            {
                case FacilityKind.Transformer:
                    if (Mathf.Min(transformerKW - d.supplyKW, switchgearKW) < loadKW) return "No spare grid capacity - servicing it would cut power";
                    break;
                case FacilityKind.Switchgear:
                    if (Mathf.Min(transformerKW, switchgearKW - d.supplyKW) < loadKW) return "No spare distribution capacity - servicing it would cut power";
                    break;
                case FacilityKind.Cooling:
                {
                    int zi = ZoneAt(o);
                    float cap = d.coolingKW * S.city.coolingFactor * (d.water > 0 ? EvapFactor : 1f);
                    if (zi >= 0 && zones[zi].coolKW - cap < zones[zi].heatKW) return "No spare cooling in this hall - it would overheat";
                    break;
                }
                case FacilityKind.CoreSwitch:
                    if (Racks.Any(r => r.contractId > 0 && ((r.linkCore == o.id && (r.linkCore2 <= 0 || r.linkCore2 == o.id)) || (r.linkCore2 == o.id && r.linkCore <= 0))))
                        return "Customer racks have no uplink B - they would go offline";
                    break;
                case FacilityKind.Isp:
                    if (ispGbps - d.bandwidth < bwDemand) return "Not enough spare ISP bandwidth";
                    break;
            }
            return null;
        }

        public string QueueMaintenance(PlacedObject o, bool now = true)
        {
            var d = Catalog.Facility(o.def);
            if (!Serviceable(d)) return "Nothing to service";
            if (d.kind == FacilityKind.Rack && o.devices.Count == 0) return "Empty rack";
            if (o.maintQueued) { if (now) o.maintNow = true; return null; }
            o.maintQueued = true;
            o.maintNow = now;
            return null;
        }

        public void CancelMaintenance(PlacedObject o) { o.maintQueued = false; o.maintNow = false; o.inMaint = false; MarkDirty(o); }

        /// <summary>Company daily: queue overdue equipment for the night window when auto-maintenance is on.</summary>
        void AutoScheduleMaintenance()
        {
            if (!S.autoMaintenance) return;
            foreach (var o in Site.objects)
                if (!o.maintQueued && !o.failed && Serviceable(Catalog.Facility(o.def)) && MaintDue(o) && (Catalog.Facility(o.def).kind != FacilityKind.Rack || o.devices.Count > 0) && MaintRisk(o) == null)
                    QueueMaintenance(o, false);
        }

        bool MaintJobOpen(PlacedObject o) => o.maintQueued && (o.maintNow || !S.maintNightOnly || NightWindow);

        void CompleteMaintenance(StaffMember m, PlacedObject o)
        {
            bool manual = o.maintNow;
            o.maintQueued = false; o.maintNow = false; o.inMaint = false;
            o.servicedHour = S.hour;
            o.pduTripped = false;
            Spend(MaintCost(o), false);
            // Routine night-window services stay out of the log; ones the player asked for are reported.
            if (manual) Log($"{m.name} serviced {Catalog.Facility(o.def).name} #{o.id}.", 0);
        }

        // ================================================================ customer peak hours
        /// <summary>Hours of the day when a customer's traffic peaks; downtime then counts 1.5x. (0,0) = no peak.</summary>
        public static Vector2Int PeakHours(Contract c)
        {
            switch (c.industry)
            {
                case "Finance": return new Vector2Int(8, 18);
                case "Healthcare": return new Vector2Int(8, 18);
                case "Government": return new Vector2Int(9, 17);
                case "Education": return new Vector2Int(9, 17);
                case "Logistics": return new Vector2Int(5, 14);
                case "Startup": return new Vector2Int(10, 19);
                case "Retail": return new Vector2Int(17, 23);
                case "Gaming": return new Vector2Int(18, 24);
                case "Media": return new Vector2Int(19, 24);
                case "Telecom": return new Vector2Int(17, 23);
            }
            return Vector2Int.zero;
        }

        public static bool InPeak(Contract c, int hour)
        {
            var p = PeakHours(c);
            int h = hour % 24;
            return p.y > p.x && h >= p.x && h < p.y;
        }

        public static string PeakText(Contract c)
        {
            var p = PeakHours(c);
            return p.y > p.x ? $"{p.x:00}:00-{p.y % 24:00}:00" : "steady around the clock";
        }

        // ================================================================ staff growth
        public static float XpForSkill(int skill) => 60f * Mathf.Pow(skill, 1.5f);

        public bool Training(StaffMember m) => m.trainingUntil > S.hour;

        public float MarketSalary(StaffMember m) =>
            Mathf.Round(BaseSalary(m.role) * (0.75f + 0.15f * m.skill) * (m.shift == "Night" ? 1.15f : 1f) / 50f) * 50f;

        public float TrainingCost(StaffMember m) => 3000f * m.skill;
        public const int TrainingDays = 5;

        public string TrainStaff(StaffMember m)
        {
            if (m.skill >= 5) return "Already at the top skill level";
            if (Training(m)) return "Already on a course";
            float cost = TrainingCost(m);
            if (!Spend(cost, false)) return "Not enough money";
            m.trainingUntil = S.hour + TrainingDays * Catalog.HoursPerDay;
            foreach (var t in S.tickets) if (t.assigned == m.id) t.assigned = -1;
            m.taskObj = m.taskDev = m.taskTicket = -1;
            m.path = null;
            m.state = "off";
            m.morale = Mathf.Min(100, m.morale + 6);
            Log($"{m.name} is away on a {TrainingDays}-day training course.", 0);
            return null;
        }

        void GainXp(StaffMember m, float work)
        {
            m.jobsDone++;
            m.xp += 1f + work * 0.25f;
            if (m.skill >= 5 || m.xp < XpForSkill(m.skill)) return;
            m.xp -= XpForSkill(m.skill);
            SkillUp(m, "on-the-job experience");
        }

        void SkillUp(StaffMember m, string how)
        {
            m.skill = Mathf.Min(5, m.skill + 1);
            if (m.skill >= 5) Unlock("mentor");
            Log($"{m.name} is now skill {m.skill} through {how}.", 1);
            float ask = Mathf.Max(Mathf.Round(m.salary * 1.08f / 50f) * 50f, MarketSalary(m));
            if (ask <= m.salary) return;
            m.raiseRequested = true;
            var mm = m;
            popups.Enqueue(new Popup
            {
                title = "Raise request",
                body = $"{m.name} ({RoleName(m.role)}) has grown to skill {m.skill} and asks for a raise from {K0(m.salary)} to {K0(ask)}/month.\n\nTurning them down hurts morale, and unhappy staff quit or get poached by rivals."
            }.Add("Give the raise", () => { mm.salary = ask; mm.morale = Mathf.Min(100, mm.morale + 12); mm.raiseRequested = false; mm.lastRaiseHour = S.hour; })
             .Add("Decline", () => { mm.morale = Mathf.Max(0, mm.morale - 18); mm.raiseRequested = false; }));
        }

        /// <summary>Daily morale drift, course completions, resignations and poaching for this site's staff.</summary>
        void DailyStaffGrowth()
        {
            foreach (var m in S.staff.ToList())
            {
                if (m.trainingUntil > 0 && S.hour >= m.trainingUntil)
                {
                    m.trainingUntil = -1;
                    S.trainingsDone++;
                    SkillUp(m, "a training course");
                }
                float before = m.morale;
                m.morale += (70f - m.morale) * 0.05f;
                if (m.fatigue > 70) m.morale -= 3f;
                float market = MarketSalary(m);
                if (m.salary < market * 0.95f) m.morale -= Mathf.Min(3f, (market - m.salary) / market * 20f);
                if (S.hour - m.lastRaiseHour < 30 * 24 && m.lastRaiseHour > 0) m.morale += 0.5f;
                m.morale = Mathf.Clamp(m.morale, 0, 100);
                if (m.morale < 25 && rng.NextDouble() < 0.05)
                {
                    FireStaff(m, false);
                    Log($"{m.name} resigned (morale {m.morale:0}). Overwork and low pay drive people away.", 3);
                    popups.Enqueue(new Popup { title = "Resignation", body = $"{m.name}, your {RoleName(m.role)}, has quit. Their morale had fallen to {m.morale:0}/100.\n\nKeep fatigue down with enough staff on each shift, and pay people what the market pays." }.Add("OK", null));
                    continue;
                }
                if (m.skill >= 4 && m.morale < 55 && rng.NextDouble() < 0.012 && S.rivals.Count > 0)
                {
                    var rv = S.rivals[rng.Next(S.rivals.Count)];
                    FireStaff(m, false);
                    Log($"{rv.name} poached {m.name}, one of your best {RoleName(m.role).ToLower()}s.", 3);
                }
            }
        }

        // ================================================================ PUE, water, heat reuse
        public static bool IsLiquid(FacilityDef d) => d.kind == FacilityKind.Cooling && d.id == "liquid";

        public float EvapFactor => Mathf.Lerp(1.25f, 0.55f, Mathf.Clamp01(S.city.humidity)) * (EventActive("drought") ? 0.6f : 1f);

        public float WaterPriceNow => S.city.waterPrice * EventMag("drought", 1f);

        public float heatExportKW, heatCapKW, heatRevenueHour, waterM3Hour;

        /// <summary>Season multiplier for heat sales: winter (month 1) pays most, summer least.</summary>
        public float HeatSeason => 0.95f + 0.75f * Mathf.Cos(2f * Mathf.PI * ((S.Day / Catalog.DaysPerMonth) % 12) / 12f);

        public float HeatPriceNow => S.city.heatPrice * HeatSeason;

        public float SitePue => itLoadKW > 0.01f ? loadKW / itLoadKW : 0f;

        void HourOps()
        {
            Site.monthITKWh += itLoadKW;
            Site.monthTotalKWh += loadKW;

            // Water for evaporative cooling, in proportion to how hard the hall is working.
            waterM3Hour = 0;
            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d == null || d.water <= 0 || o.failed || o.inMaint) continue;
                int zi = ZoneAt(o);
                float u = zi >= 0 && zones[zi].coolKW > 0 ? Mathf.Clamp01(zones[zi].heatKW / zones[zi].coolKW) : 0f;
                waterM3Hour += d.water * u;
            }
            if (waterM3Hour > 0)
            {
                float cost = waterM3Hour * WaterPriceNow;
                S.money -= cost;
                S.current.water += cost;
                Site.monthWaterM3 += waterM3Hour;
            }

            // District heating: recovered heat from the halls, capped by exchanger capacity.
            heatCapKW = 0;
            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d != null && d.kind == FacilityKind.HeatExchanger && !o.failed && !o.inMaint) heatCapKW += d.heatKW;
            }
            heatExportKW = 0; heatRevenueHour = 0;
            if (heatCapKW > 0)
            {
                float avail = 0;
                foreach (var z in zones)
                {
                    if (z.coolKW <= 0) continue;
                    float eff = 0.35f + 0.55f * Mathf.Clamp01(z.liquidKW / z.coolKW);
                    avail += Mathf.Min(z.heatKW, z.coolKW) * eff;
                }
                heatExportKW = Mathf.Min(avail, heatCapKW);
                heatRevenueHour = heatExportKW * HeatPriceNow;
                if (heatRevenueHour > 0)
                {
                    S.money += heatRevenueHour;
                    S.current.sales += heatRevenueHour;
                    S.lifetimeRevenue += heatRevenueHour;
                    S.heatSoldTotal += heatRevenueHour;
                    Site.monthHeatKWh += heatExportKW;
                }
            }
            NoiseHour();
            DeliveriesHour();
        }

        /// <summary>Company PUE for the month that just ended, and per-site history.</summary>
        void MonthlyPue()
        {
            float it = 0, total = 0;
            foreach (var site in S.sites)
            {
                if (site.monthITKWh > 1f)
                {
                    site.lastPue = site.monthTotalKWh / site.monthITKWh;
                    site.pueHistory.Add(site.lastPue);
                    if (site.pueHistory.Count > 24) site.pueHistory.RemoveAt(0);
                }
                it += site.monthITKWh; total += site.monthTotalKWh;
                site.monthITKWh = site.monthTotalKWh = 0;
                site.monthWaterM3 = 0; site.monthHeatKWh = 0;
                site.complaints = 0;
            }
            S.current.pue = it > 1f ? total / it : 0f;
            if (S.current.pue > 0 && S.current.pue < 1.2f && it / Catalog.HoursPerMonth >= 50f) Unlock("pue12");
        }

        public float CompanyPue => S.reports.Count > 0 ? S.reports[S.reports.Count - 1].pue : 0f;

        public const float EfficientPue = 1.35f;

        void EfficientCheck(Contract c)
        {
            if (!c.efficient) return;
            float pue = S.current.pue > 0 ? S.current.pue : CompanyPue;
            // Judged on the running month (no data yet counts as a pass).
            float it = S.sites.Sum(s => s.monthITKWh), tot = S.sites.Sum(s => s.monthTotalKWh);
            if (it > 1f) pue = tot / it;
            if (pue <= 0 || pue <= EfficientPue) { AddSat(c, 3, $"Happy with your efficiency (PUE {(pue > 0 ? pue.ToString("0.00") : "-")})"); return; }
            float credit = Mathf.Round(c.monthlyPay * 0.15f);
            S.money -= credit;
            S.current.penalties += credit;
            AddSat(c, -6, $"PUE {pue:0.00} missed the {EfficientPue:0.00} efficiency pledge");
            Log($"{c.customer} wanted a PUE of {EfficientPue:0.00} or better (you ran {pue:0.00}). {K0(credit)} efficiency credit.", 2);
        }

        // ================================================================ spot compute market
        public const float SpotShare = 0.12f;
        public float spotGbps, spotRevenueHour;
        public int spotCores, spotGpus;
        public float spotTB;

        /// <summary>Idle capacity in unassigned online racks at this site.</summary>
        public void SpotCapacity(out int cores, out int gpus, out float tb)
        {
            cores = 0; gpus = 0; tb = 0;
            foreach (var o in Racks)
            {
                if (o.contractId > 0 || !racks.TryGetValue(o.id, out var info) || !info.online || info.colo) continue;
                cores += info.cores; gpus += info.gpus; tb += info.storage;
            }
        }

        public float SpotDemandGbps()
        {
            if (!S.spotEnabled) return 0;
            SpotCapacity(out int c, out int g, out float tb);
            return c / 80f + g * 0.4f + tb / 200f;
        }

        public float SpotCoreHour => 580f * SpotShare / Catalog.HoursPerMonth * S.spotCoreMul;
        public float SpotGpuHour => 39000f * SpotShare / Catalog.HoursPerMonth * S.spotGpuMul;
        public float SpotTBHour => 230f * SpotShare / Catalog.HoursPerMonth * S.spotCoreMul;

        /// <summary>Company hourly: sell idle capacity at every site.</summary>
        void SpotHour()
        {
            float total = 0;
            foreach (var sim in company.sims)
            {
                sim.spotRevenueHour = 0;
                if (!S.spotEnabled) continue;
                sim.SpotCapacity(out sim.spotCores, out sim.spotGpus, out sim.spotTB);
                float r = (sim.spotCores * SpotCoreHour + sim.spotGpus * SpotGpuHour + sim.spotTB * SpotTBHour) * sim.netQuality;
                sim.spotRevenueHour = r;
                total += r;
            }
            if (total <= 0) return;
            S.money += total;
            S.current.sales += total;
            S.lifetimeRevenue += total;
            S.spotEarnedTotal += total;
            if (S.spotEarnedTotal >= 100000) Unlock("spot");
        }

        void DailySpotPrices()
        {
            float gpuBoost = EventActive("gpushortage") ? 0.06f : 0f;
            S.spotCoreMul = Mathf.Clamp(S.spotCoreMul + (1f - S.spotCoreMul) * 0.08f + ProcGen.Range(rng, -0.09f, 0.09f), 0.3f, 1.8f);
            S.spotGpuMul = Mathf.Clamp(S.spotGpuMul + (1f - S.spotGpuMul) * 0.08f + ProcGen.Range(rng, -0.11f, 0.11f) + gpuBoost, 0.3f, 2.2f);
        }

        // ================================================================ noise & neighbours
        public static readonly string[] SideNames = { "north", "south", "east", "west" };
        float dailyMaxNoise;
        public float noiseNow;
        public string noiseLoudest;

        public int ResidentialSide
        {
            get
            {
                if (Site.residentialSide < 0) Site.residentialSide = new System.Random(S.seed * 31 + siteIndex * 977).Next(4);
                return Site.residentialSide;
            }
        }

        /// <summary>Tiles from a point to the neighbours' houses (3 tiles past the lot edge on the residential side).</summary>
        public float NeighbourDistance(float x, float y)
        {
            switch (ResidentialSide)
            {
                case 0: return Mathf.Max(1f, S.ownY1 + 3f - y);
                case 1: return Mathf.Max(1f, y - (S.ownY0 - 3f));
                case 2: return Mathf.Max(1f, S.ownX1 + 3f - x);
                default: return Mathf.Max(1f, x - (S.ownX0 - 3f));
            }
        }

        public float NoiseLimitNow => S.city.noiseLimit - (NightWindow ? 5f : 0f);

        bool GeneratorRunning => (outageActive && outageLong) || (S.Day % Catalog.DaysPerMonth == 15 && S.hour % 24 == 11);

        /// <summary>Level a source contributes at the neighbours' fence (dB), or -1 if it is silent right now.</summary>
        public float NoiseAtNeighbours(PlacedObject o, bool assumeRunning = false)
        {
            var d = Catalog.Facility(o.def);
            if (d == null || d.noise <= 0 || o.failed) return -1;
            if (d.kind == FacilityKind.Generator && !assumeRunning && !GeneratorRunning) return -1;
            Footprint(d, o.rot, out int w, out int h);
            float cx = o.x + w * 0.5f, cy = o.y + h * 0.5f;
            float l = d.noise - 20f * Mathf.Log10(NeighbourDistance(cx, cy));
            if (HasSoundWallNear(o)) l -= 12f;
            return l;
        }

        public bool HasSoundWallNear(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            Footprint(d, o.rot, out int w, out int h);
            for (int y = o.y - 2; y < o.y + h + 2; y++)
                for (int x = o.x - 2; x < o.x + w + 2; x++)
                {
                    var n = ObjectAtF(0, x, y);
                    if (n != null && n != o && Catalog.Facility(n.def).kind == FacilityKind.SoundWall) return true;
                }
            return false;
        }

        void NoiseHour()
        {
            double sum = 0; float loudest = -1; noiseLoudest = null;
            foreach (var o in S.objects)
            {
                float l = NoiseAtNeighbours(o);
                if (l <= 0) continue;
                sum += Math.Pow(10, l / 10.0);
                if (l > loudest) { loudest = l; noiseLoudest = $"{Catalog.Facility(o.def).name} #{o.id}"; }
            }
            noiseNow = sum > 0 ? (float)(10.0 * Math.Log10(sum)) : 0f;
            Site.noiseLevel = noiseNow;
            float excess = noiseNow - NoiseLimitNow;
            if (excess > dailyMaxNoise) dailyMaxNoise = excess;
        }

        /// <summary>Daily: neighbours complain when it was too loud; repeat offences escalate the fines.</summary>
        void DailyNoise()
        {
            float excess = dailyMaxNoise;
            dailyMaxNoise = -100f;
            if (excess <= 0) { Site.noiseStrikes = Mathf.Max(0, Site.noiseStrikes - 0.05f); return; }
            Site.complaints++;
            Site.complaintsTotal++;
            S.lastComplaintHour = S.hour;
            float fine = Mathf.Round(1500f * (1f + Site.noiseStrikes) * (1f + excess / 10f) / 50f) * 50f;
            Site.noiseStrikes += 1f;
            PayFine(fine);
            Log($"Neighbours to the {SideNames[ResidentialSide]} complained about noise ({excess:0} dB over the limit{(noiseLoudest != null ? $", loudest: {noiseLoudest}" : "")}). {K0(fine)} fine.", 2);
            if (Site.complaints == 4)
            {
                float big = 25000f;
                PayFine(big);
                popups.Enqueue(new Popup { title = "Permit violation", body = $"The city cited {S.siteName} for repeated noise violations and fined you {K0(big)}.\n\nMove generators and transformers away from the houses to the {SideNames[ResidentialSide]}, or put Acoustic Barriers around them." }.Add("OK", null));
            }
        }

        void PayFine(float amount)
        {
            S.money -= amount;
            S.current.fines += amount;
            S.finesTotal += amount;
        }

        // ================================================================ hardware orders & deliveries
        public bool HasDock => S.objects.Any(o => !o.failed && Catalog.Facility(o.def)?.kind == FacilityKind.LoadingDock);

        public int PendingOrders => Racks.Sum(r => r.orders.Count(x => !x.arrived));

        public float ExpressMul => EventActive("shortage") || EventActive("gpushortage") ? 1.5f : 1.25f;

        public float OrderPrice(DeviceDef d, bool express) => Mathf.Round(DeviceCost(d) * (express ? ExpressMul : 1f));

        /// <summary>Hours until a standard order arrives at this site.</summary>
        public int LeadHours(DeviceDef d)
        {
            int h = 48;
            if (!HasDock) h += 48;
            if (EventActive("shortage")) h += 96;
            if (d.gpus > 0 && EventActive("gpushortage")) h += 144;
            if (PendingOrders > 16) h += 24;
            return h;
        }

        public string CancelOrder(PlacedObject rack, RackDevice ord)
        {
            if (!rack.orders.Contains(ord)) return "Unknown order";
            if (ord.arrived) return "Already delivered - it will be installed shortly";
            rack.orders.Remove(ord);
            Refund(ord.paid * 0.9f);
            Log($"Cancelled an order for {Catalog.Device(ord.def).name} (10% restocking fee).", 0);
            MarkDirty(rack);
            return null;
        }

        public event Action<int> OnDelivery;   // site index

        bool HasInstaller => S.staff.Any(m => m.role != "Security" && !Training(m));

        void DeliveriesHour()
        {
            int n = 0;
            foreach (var r in Racks)
                foreach (var ord in r.orders)
                    if (!ord.arrived && S.hour >= ord.arriveHour) { ord.arrived = true; n++; }
            if (n == 0) return;
            Log($"A delivery truck dropped off {n} item(s) at {(HasDock ? "the loading dock" : "the kerb")}.", 0);
            OnDelivery?.Invoke(siteIndex);
            if (!HasInstaller)
                foreach (var r in Racks.Where(x => x.orders.Any(o => o.arrived)).ToList()) InstallArrived(r, null);
        }

        void InstallArrived(PlacedObject rack, StaffMember m)
        {
            var done = rack.orders.Where(o => o.arrived).ToList();
            if (done.Count == 0) return;
            foreach (var o in done)
            {
                rack.orders.Remove(o);
                rack.devices.Add(new RackDevice { id = o.id, def = o.def, u = o.u });
            }
            MarkDirty(rack);
            Log(m != null ? $"{m.name} installed {done.Count} delivered item(s) in rack #{rack.id}." : $"The vendor installed {done.Count} delivered item(s) in rack #{rack.id}.", 0);
            Recompute();
        }
    }
}
