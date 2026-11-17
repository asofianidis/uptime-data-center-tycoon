using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// Company-wide layer above the per-site simulations. Owns contract provisioning (contracts can be
    /// served by racks in any site), the hourly tick order, logging/popups and site management.
    /// </summary>
    public class Company
    {
        public readonly GameState S;
        public readonly List<Simulation> sims = new List<Simulation>();
        public readonly Dictionary<int, Provision> provision = new Dictionary<int, Provision>();
        public readonly Queue<Popup> popups = new Queue<Popup>();
        public event Action<string, int> OnLog;
        public event Action BeforeChange;
        public float hourlyIncome, hourlyCost;

        public Company(GameState s) { S = s; }

        public Simulation Active => sims[Mathf.Clamp(S.activeSite, 0, sims.Count - 1)];
        public bool MultiSite => sims.Count > 1;

        internal void RaiseLog(string text, int sev) => OnLog?.Invoke(text, sev);
        internal void RaiseBeforeChange() => BeforeChange?.Invoke();

        public void BindActive() => S.Bind(S.activeSite);

        /// <summary>Runs an action with another site's data bound, then restores the previous binding.</summary>
        public void WithSite(int site, Action a)
        {
            int prev = S.bound;
            S.Bind(site);
            try { a(); }
            finally { if (prev >= 0) S.Bind(prev); }
        }

        // ------------------------------------------------------------------ time
        public void TickHour()
        {
            if (S.gameOver) return;
            S.hour++;
            for (int i = 0; i < sims.Count; i++)
            {
                S.Bind(i);
                sims[i].TickSite();
            }
            BindActive();
            RecomputeProvision();
            Active.TickCompany();
            BindActive();
        }

        // ------------------------------------------------------------------ cross-site lookups
        public IEnumerable<(Simulation sim, PlacedObject rack)> AllRacks()
        {
            foreach (var sim in sims)
                foreach (var r in sim.Racks) yield return (sim, r);
        }

        public bool FindObject(int id, out Simulation sim, out PlacedObject obj)
        {
            foreach (var s in sims)
                if (s.objById.TryGetValue(id, out obj)) { sim = s; return true; }
            sim = null; obj = null;
            return false;
        }

        public void MarkContractRacks(int contractId)
        {
            foreach (var (sim, r) in AllRacks()) if (r.contractId == contractId) sim.MarkDirty(r);
        }

        public void ReleaseContractRacks(int contractId)
        {
            foreach (var (sim, r) in AllRacks()) if (r.contractId == contractId) { r.contractId = -1; sim.MarkDirty(r); }
        }

        // ------------------------------------------------------------------ provisioning
        /// <summary>Per-site WAN demand and contract provisioning across every site.</summary>
        public void RecomputeProvision()
        {
            var byContract = new Dictionary<int, List<(Simulation, PlacedObject)>>();
            var siteRackCount = new Dictionary<int, Dictionary<Simulation, int>>();
            foreach (var (sim, r) in AllRacks())
            {
                if (r.contractId <= 0) continue;
                if (!byContract.TryGetValue(r.contractId, out var l)) byContract[r.contractId] = l = new List<(Simulation, PlacedObject)>();
                l.Add((sim, r));
                if (!siteRackCount.TryGetValue(r.contractId, out var m)) siteRackCount[r.contractId] = m = new Dictionary<Simulation, int>();
                m[sim] = (m.TryGetValue(sim, out int n) ? n : 0) + 1;
            }

            // WAN demand is split between sites by where each contract's racks live.
            foreach (var sim in sims) sim.bwDemand = 0;
            foreach (var c in S.contracts)
            {
                if (c.status != ContractStatus.Active) continue;
                if (!siteRackCount.TryGetValue(c.id, out var m) || m.Count == 0) { sims[0].bwDemand += c.reqBandwidth; continue; }
                int total = m.Values.Sum();
                foreach (var kv in m) kv.Key.bwDemand += c.reqBandwidth * kv.Value / total;
            }
            foreach (var sim in sims)
            {
                sim.bwDemand *= sim.SiteEventMag("surge", 1f);
                sim.netQuality = sim.bwDemand <= 0.001f ? 1f : Mathf.Min(1f, sim.ispGbps / sim.bwDemand);
            }

            provision.Clear();
            var none = new List<(Simulation, PlacedObject)>();
            foreach (var c in S.contracts)
            {
                if (c.status != ContractStatus.Setup && c.status != ContractStatus.Active) continue;
                provision[c.id] = Evaluate(c, byContract.TryGetValue(c.id, out var l) ? l : none);
            }
        }

        Provision Evaluate(Contract c, List<(Simulation sim, PlacedObject o)> list)
        {
            var p = new Provision();
            string rackIssue = null;
            int assigned = 0, nonRedundant = 0;
            bool saturated = false;
            var onlineSites = new HashSet<Simulation>();
            foreach (var (sim, o) in list)
            {
                assigned++;
                if (!sim.racks.TryGetValue(o.id, out var info)) continue;
                if (!info.online) { rackIssue ??= (MultiSite ? $"[{sim.Site.name}] " : "") + info.issue; continue; }
                if (c.redundant && !info.redundant) nonRedundant++;
                onlineSites.Add(sim);
                p.racks++;
                p.bandwidth += info.linkGbps * sim.netQuality;
                if (sim.netQuality < 1f) saturated = true;
                p.cores += info.cores; p.storage += info.storage; p.gpus += info.gpus;
                if (c.type == ContractType.Dedicated)
                {
                    int portsLeft = info.ports;
                    foreach (var dev in o.devices)
                    {
                        if (dev.failed) continue;
                        var dd = Catalog.Device(dev.def);
                        if (dd == null || dd.kind != DeviceKind.Server) continue;
                        if (portsLeft-- <= 0) break;
                        if (dd.cores >= c.reqCoresPerServer) p.qualServers++;
                    }
                }
            }

            float main = 1f;
            switch (c.type)
            {
                case ContractType.Colocation: main = c.reqRacks > 0 ? (float)p.racks / c.reqRacks : 1f; break;
                case ContractType.Dedicated: main = c.reqServers > 0 ? (float)p.qualServers / c.reqServers : 1f; break;
                case ContractType.Compute: main = c.reqCores > 0 ? (float)p.cores / c.reqCores : 1f; break;
                case ContractType.Storage: main = c.reqStorageTB > 0 ? p.storage / c.reqStorageTB : 1f; break;
                case ContractType.GpuCloud: main = c.reqGpus > 0 ? (float)p.gpus / c.reqGpus : 1f; break;
            }
            float bwFrac = c.reqBandwidth > 0 ? p.bandwidth / c.reqBandwidth : 1f;
            p.service = Mathf.Clamp01(Mathf.Min(main, bwFrac));

            string need = null;
            switch (c.type)
            {
                case ContractType.Colocation: if (p.racks < c.reqRacks) need = $"Needs {c.reqRacks} online racks (has {p.racks})"; break;
                case ContractType.Dedicated: if (p.qualServers < c.reqServers) need = $"Needs {c.reqServers} servers with {c.reqCoresPerServer}+ cores (has {p.qualServers})"; break;
                case ContractType.Compute: if (p.cores < c.reqCores) need = $"Needs {c.reqCores} cores (has {p.cores})"; break;
                case ContractType.Storage: if (p.storage < c.reqStorageTB - 0.01f) need = $"Needs {c.reqStorageTB:0} TB (has {p.storage:0})"; break;
                case ContractType.GpuCloud: if (p.gpus < c.reqGpus) need = $"Needs {c.reqGpus} GPUs (has {p.gpus})"; break;
            }
            if (need == null && p.bandwidth + 0.001f < c.reqBandwidth)
                need = saturated ? $"ISP saturated: {p.bandwidth:0.0}/{c.reqBandwidth:0.0} Gbps" : $"Needs {c.reqBandwidth:0.0} Gbps uplink (has {p.bandwidth:0.0})";
            if (need == null && c.redundant && nonRedundant > 0)
            {
                need = $"Redundancy required: {nonRedundant} rack(s) lack dual PDU / 2 ToR / 2 core uplinks";
                p.service = Mathf.Min(p.service, 0.97f);
            }
            if (need == null && c.geoRedundant && onlineSites.Count < 2)
            {
                need = "Geo-redundancy required: online racks in at least 2 sites";
                p.service = Mathf.Min(p.service, 0.97f);
            }
            if (need != null && assigned == 0) need = "No racks assigned";
            else if (need != null && rackIssue != null) need += $" - rack issue: {rackIssue}";
            p.meets = need == null;
            p.issue = need;
            return p;
        }

        // ------------------------------------------------------------------ sites
        public const int MinLevelForSites = 4;

        public float SiteCost(CityProfile city) => Mathf.Round(220000f * city.landPrice * (1f + 0.5f * (sims.Count - 1)) / 1000f) * 1000f;

        /// <summary>Three candidate cities for the next site, stable until a site is opened.</summary>
        public List<CityProfile> SiteCandidates()
        {
            var r = new System.Random(S.seed * 131 + sims.Count * 7919);
            var list = new List<CityProfile>();
            for (int i = 0; i < 3; i++)
            {
                var c = ProcGen.GenerateCity(r);
                c.carbonTax = r.NextDouble() < 0.4 ? (float)Math.Round(ProcGen.Range(r, 0.005f, 0.03f), 3) : 0f;
                c.solarFactor = c.climate == "Desert" ? 1.3f : c.climate == "Hot" ? 1.15f : c.climate == "Arctic" ? 0.6f : c.climate == "Cold" ? 0.8f : 1f;
                list.Add(c);
            }
            return list;
        }

        public string CanOpenSite(CityProfile city)
        {
            if (S.Level < MinLevelForSites && S.scenario != "aiboom") return $"Requires reputation level {MinLevelForSites}";
            if (sims.Count >= 6) return "Maximum of 6 sites";
            if (S.money < SiteCost(city)) return "Not enough money";
            return null;
        }

        public Simulation OpenSite(CityProfile city)
        {
            if (CanOpenSite(city) != null) return null;
            float cost = SiteCost(city);
            BeforeChange?.Invoke();
            S.money -= cost;
            S.current.capex += cost;
            var d = new SiteData { name = city.name, city = city, mapW = 64, mapH = 48, openedHour = S.hour };
            S.sites.Add(d);
            int idx = S.sites.Count - 1;
            int prev = S.bound;
            S.Bind(idx);
            var r = new System.Random(S.seed * 17 + idx * 1009);
            ProcGen.GenerateLot(S, r);
            S.flags = new int[S.mapW * S.mapH];
            S.objects = new List<PlacedObject>();
            S.staff = new List<StaffMember>();
            S.events = new List<ActiveEvent>();
            S.weather = "Clear";
            S.monthSolarKWh = S.monthGridKWh = 0;
            S.landPurchases = 0;
            S.siteName = city.name;
            var sim = Simulation.CreateSite(S, this, idx);
            sims.Add(sim);
            if (prev >= 0) S.Bind(prev);
            RaiseLog($"Opened a new data center site in {city.name} ({city.climate}) for ${cost:N0}.", 1);
            return sim;
        }

        public Simulation SetActive(int i)
        {
            S.activeSite = Mathf.Clamp(i, 0, sims.Count - 1);
            BindActive();
            Active.Recompute();
            return Active;
        }

        // Aggregates for the UI.
        public float TotalLoadKW => sims.Sum(s => s.loadKW);
        public int TotalRacks => sims.Sum(s => s.Racks.Count);
    }
}
