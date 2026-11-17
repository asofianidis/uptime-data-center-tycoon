using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// Scripted player used for headless balance tests and demo screenshots.
    /// Builds a starter facility and then reacts to contracts the way a reasonable player would.
    /// </summary>
    public static class AutoManager
    {
        public static void BuildStarter(Simulation sim)
        {
            var S = sim.S;
            int w = S.ownX1 - S.ownX0, h = S.ownY1 - S.ownY0;
            int cx = S.ownX0 + w / 2, cy = S.ownY0 + h / 2;
            int x0 = cx - 7, y0 = cy - 5;
            sim.BuildFoundation(new RectInt(x0, y0, 15, 11), true, out _);
            sim.PaintRoom(new RectInt(x0, y0 + 3, 10, 8), RoomType.ServerHall, true, out _);
            sim.PaintRoom(new RectInt(x0 + 11, y0 + 6, 4, 5), RoomType.Network, true, out _);
            sim.PaintRoom(new RectInt(x0 + 11, y0, 4, 5), RoomType.Power, true, out _);
            sim.PaintRoom(new RectInt(x0, y0, 5, 2), RoomType.Office, true, out _);

            // Outdoor transformer east of the building.
            sim.Demolish(new RectInt(x0 + 16, y0, 3, 4), true, out _, out _);
            sim.Place(Catalog.Facility("xfmr"), x0 + 16, y0 + 1, 0);
            sim.Place(Catalog.Facility("swgear"), x0 + 12, y0 + 4, 0);
            sim.Place(Catalog.Facility("ups"), x0 + 13, y0 + 4, 0);
            sim.Place(Catalog.Facility("core_10g"), x0 + 12, y0 + 10, 0);
            sim.Place(Catalog.Facility("isp_10"), x0 + 14, y0 + 10, 0);
            sim.Place(Catalog.Facility("crac"), x0, y0 + 10, 0);
            sim.Place(Catalog.Facility("desk"), x0 + 1, y0 + 1, 0);
            sim.Hire();
        }

        static bool TryPlaceAnywhere(Simulation sim, FacilityDef d, out PlacedObject placed, int rot = 0)
        {
            var S = sim.S;
            placed = null;
            for (int y = S.ownY1 - 1; y >= S.ownY0; y--)
                for (int x = S.ownX0; x < S.ownX1; x++)
                {
                    // Racks in rows with an aisle every other row.
                    if (d.kind == FacilityKind.Rack && (y % 3 == 0)) continue;
                    if (sim.CanPlace(d, x, y, rot) != null) continue;
                    placed = sim.Place(d, x, y, rot);
                    return placed != null;
                }
            return false;
        }

        static T Best<T>(System.Collections.Generic.IEnumerable<T> src, System.Func<T, float> key) where T : class
        {
            T best = null; float bk = float.MaxValue;
            foreach (var x in src) { float k = key(x); if (k < bk) { bk = k; best = x; } }
            return best;
        }

        /// <summary>Called once per in-game day by tests.</summary>
        public static void DailyManage(Simulation sim)
        {
            var S = sim.S;
            // Research anything affordable that keeps a healthy cash buffer.
            foreach (var d in Catalog.Devices.Where(x => x.research > 0 && !sim.IsUnlocked(x.id) && S.Level >= x.level))
                if (S.money > d.research * 3) sim.Research(d.id);
            foreach (var f in Catalog.Facilities.Where(x => x.research > 0 && !sim.IsUnlocked(x.id) && S.Level >= x.level))
                if (S.money > f.research * 3) sim.Research(f.id);

            // Accept offers when we have money and capacity.
            int open = S.contracts.Count(c => c.status == ContractStatus.Setup);
            foreach (var c in S.offers.ToList())
            {
                if (open >= 2 || S.money < 60000) break;
                if (c.type == ContractType.GpuCloud && !Catalog.Devices.Any(d => d.gpus > 0 && sim.IsUnlocked(d.id))) continue;
                if (c.type == ContractType.Colocation && !Catalog.Facilities.Any(f => f.kind == FacilityKind.Rack && sim.IsUnlocked(f.id) && f.rackKW >= c.reqKWPerRack)) continue;
                if (c.type == ContractType.Dedicated && !Catalog.Devices.Any(d => d.cores >= c.reqCoresPerServer && sim.IsUnlocked(d.id))) continue;
                sim.AcceptOffer(c);
                open++;
            }

            EnsureInfrastructure(sim);

            if (!sim.IsUnlocked("gen") && S.Level >= 2 && S.money > 30000) sim.Research("gen");
            foreach (var c in S.contracts.Where(x => x.status == ContractStatus.Setup || x.status == ContractStatus.Active).ToList())
                Provision(sim, c);

            // Keep a technician per ~40 devices.
            int devices = S.objects.Sum(o => o.devices.Count);
            int want = Mathf.Clamp(devices / 40 + 1, 1, 12);
            while (S.staff.Count < want && S.money > 20000)
            {
                if (sim.deskCount <= S.staff.Count && !TryPlaceAnywhere(sim, Catalog.Facility("desk"), out _)) break;
                if (sim.Hire() != null) break;
            }
        }

        static void EnsureInfrastructure(Simulation sim)
        {
            var S = sim.S;
            // Cooling per zone.
            foreach (var z in sim.zones)
                if (z.heatKW > z.coolKW * 0.75f && S.money > 30000)
                {
                    var def = Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Cooling && sim.IsUnlocked(f.id)), f => f.cost / f.coolingKW);
                    TryPlaceAnywhere(sim, def, out _);
                }
            if (sim.loadKW > sim.supplyKW * 0.75f && S.money > 50000)
            {
                if (sim.transformerKW <= sim.switchgearKW) TryPlaceAnywhere(sim, Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Transformer && sim.IsUnlocked(f.id)), f => f.cost / f.supplyKW), out _);
                else TryPlaceAnywhere(sim, Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Switchgear && sim.IsUnlocked(f.id)), f => f.cost / f.supplyKW), out _);
            }
            if (sim.loadKW > sim.upsKW * 0.9f && S.money > 60000)
                TryPlaceAnywhere(sim, Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Ups && sim.IsUnlocked(f.id)), f => f.cost / f.backupKW), out _);
            if (sim.IsUnlocked("suppress") && S.money > 40000)
                foreach (var z in sim.zones)
                    if (!z.suppressed) { TryPlaceAnywhere(sim, Catalog.Facility("suppress"), out _); break; }
            if (sim.IsUnlocked("solar") && sim.solarCapKW < sim.loadKW * 0.6f && S.money > 150000)
                TryPlaceAnywhere(sim, Catalog.Facility("solar"), out _);
            if (sim.ObjectsOfKind(FacilityKind.CoreSwitch).Count() < 2 && S.Level >= 3 && S.money > 60000)
                TryPlaceAnywhere(sim, Catalog.Facility("core_10g"), out _);
            if (sim.IsUnlocked("gen") && sim.genKW < sim.loadKW && S.money > 70000)
                TryPlaceAnywhere(sim, Catalog.Facility("gen"), out _);
            if (sim.bwDemand > sim.ispNominal * 0.7f && S.money > 30000)
                TryPlaceAnywhere(sim, Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Isp && sim.IsUnlocked(f.id)), f => f.cost + f.monthly * 6), out _);
            if (sim.corePortsUsed >= sim.corePorts - 1 && S.money > 60000)
                TryPlaceAnywhere(sim, Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.CoreSwitch && sim.IsUnlocked(f.id)), f => f.cost / f.ports), out _);

            // Grow the building when the server hall fills up.
            bool hallFull = sim.CanPlaceAnywhere(Catalog.Facility("rack_std")) == false;
            if (hallFull && S.money > 80000) ExpandHall(sim);
        }

        static void ExpandHall(Simulation sim)
        {
            var S = sim.S;
            // Try to extend the building westward/southward with a new hall block on owned land.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                for (int y = S.ownY0 + 1; y + 8 < S.ownY1; y += 2)
                    for (int x = S.ownX0 + 1; x + 8 < S.ownX1; x += 2)
                    {
                        var r = new RectInt(x, y, 8, 7);
                        if (!RectFree(sim, r)) continue;
                        sim.Demolish(r, true, out _, out _);
                        sim.BuildFoundation(r, true, out _);
                        sim.PaintRoom(r, RoomType.ServerHall, true, out _);
                        return;
                    }
                // Clear trees on a candidate block then retry, or buy land.
                int dir = attempt % 4;
                if (sim.CanExpand(dir) && S.money > sim.LandCost(dir) * 2) sim.BuyLand(dir);
            }
        }

        static bool RectFree(Simulation sim, RectInt r)
        {
            var S = sim.S;
            for (int y = r.yMin - 1; y <= r.yMax; y++)
                for (int x = r.xMin - 1; x <= r.xMax; x++)
                {
                    if (!S.Owned(x, y)) return false;
                    if (S.Room(x, y) != RoomType.None || sim.ObjectAt(x, y) != null) return false;
                }
            return true;
        }

        static void Provision(Simulation sim, Contract c)
        {
            var S = sim.S;
            for (int guard = 0; guard < 40; guard++)
            {
                sim.Recompute();
                if (sim.provision.TryGetValue(c.id, out var p) && p.meets)
                {
                    if (c.type == ContractType.Colocation || HasMargin(c, p) || S.money < 40000) return;
                }
                if (S.money < 15000) return;

                var racks = sim.Racks.Where(o => o.contractId == c.id).ToList();
                // Pick a rack with room, or grab a new one.
                PlacedObject rack = null;
                if (c.type == ContractType.Colocation)
                {
                    int online = p?.racks ?? 0;
                    if (racks.Count >= c.reqRacks)
                    {
                        foreach (var r in racks) FixRack(sim, r);
                        if (sim.provision.TryGetValue(c.id, out var p2) && !p2.meets && (p2.issue ?? "").Contains("Gbps")) { /* bandwidth limited */ GetRack(sim, c); }
                        return;
                    }
                    rack = GetRack(sim, c);
                    if (rack == null) return;
                    FixRack(sim, rack);
                    continue;
                }

                rack = racks.FirstOrDefault(r => sim.racks.TryGetValue(r.id, out var i) && i.loadKW < Catalog.Facility(r.def).rackKW * 0.8f && sim.FindSlot(r, 4) >= 0);
                if (rack == null) rack = GetRack(sim, c);
                if (rack == null) return;
                FixRack(sim, rack);

                DeviceDef dev = null;
                var unlocked = Catalog.Devices.Where(d => d.kind == DeviceKind.Server && sim.IsUnlocked(d.id)).ToList();
                switch (c.type)
                {
                    case ContractType.Dedicated: dev = Best(unlocked.Where(d => d.cores >= c.reqCoresPerServer), d => d.cost); break;
                    case ContractType.Compute: dev = Best(unlocked.Where(d => d.cores > 0), d => d.cost / d.cores); break;
                    case ContractType.Storage: dev = Best(unlocked.Where(d => d.storageTB > 0), d => d.cost / d.storageTB); break;
                    case ContractType.GpuCloud: dev = Best(unlocked.Where(d => d.gpus > 0), d => d.cost / d.gpus); break;
                }
                if (dev == null) return;
                sim.racks.TryGetValue(rack.id, out var info);
                if (info != null && info.loadKW + dev.watts / 1000f > Catalog.Facility(rack.def).rackKW) { rack = GetRack(sim, c); if (rack == null) return; FixRack(sim, rack); }
                if (sim.AddDevice(rack, dev) == null) return;
                // Make sure switch ports keep up.
                sim.Recompute();
                if (sim.racks.TryGetValue(rack.id, out info) && info.servers > info.ports)
                {
                    var sw = Best(Catalog.Devices.Where(d => d.kind == DeviceKind.Switch && sim.IsUnlocked(d.id)), d => d.cost);
                    sim.AddDevice(rack, sw);
                }
            }
        }

        // ~10% headroom so a single failure doesn't break the SLA.
        static bool HasMargin(Contract c, Provision p)
        {
            switch (c.type)
            {
                case ContractType.Dedicated: return p.qualServers >= Mathf.CeilToInt(c.reqServers * 1.1f) + (c.reqServers < 10 ? 1 : 0) - 1 + 1;
                case ContractType.Compute: return p.cores >= c.reqCores * 1.12f;
                case ContractType.Storage: return p.storage >= c.reqStorageTB * 1.12f;
                case ContractType.GpuCloud: return p.gpus >= c.reqGpus * 1.12f;
            }
            return true;
        }

        /// <summary>Runs an L-shaped cable tray from the rack to the nearest core switch, then cables it.</summary>
        public static void LayTrayToCore(Simulation sim, PlacedObject rack)
        {
            var core = sim.ObjectsOfKind(FacilityKind.CoreSwitch).Where(c => !c.failed && sim.FreePorts(c) > 0)
                .OrderBy(c => Mathf.Abs(c.x - rack.x) + Mathf.Abs(c.y - rack.y)).FirstOrDefault();
            if (core == null) return;
            sim.PaintFlag(new RectInt(Mathf.Min(rack.x, core.x), rack.y, Mathf.Abs(rack.x - core.x) + 1, 1), GameState.FlagTray, true, out _);
            sim.PaintFlag(new RectInt(core.x, Mathf.Min(rack.y, core.y), 1, Mathf.Abs(rack.y - core.y) + 1), GameState.FlagTray, true, out _);
            sim.AutoConnect(rack);
        }

        static PlacedObject GetRack(Simulation sim, Contract c)
        {
            var free = sim.Racks.FirstOrDefault(r => r.contractId < 0 && (c.type != ContractType.Colocation || !r.devices.Any(d => Catalog.Device(d.def).kind == DeviceKind.Server))
                                                     && Catalog.Facility(r.def).rackKW >= c.reqKWPerRack);
            if (free == null)
            {
                var def = Best(Catalog.Facilities.Where(f => f.kind == FacilityKind.Rack && sim.IsUnlocked(f.id) && f.rackKW >= c.reqKWPerRack), f => f.cost);
                if (def == null || !TryPlaceAnywhere(sim, def, out free)) return null;
            }
            sim.Assign(free, c);
            return free;
        }

        static void FixRack(Simulation sim, PlacedObject rack)
        {
            if (!rack.devices.Any(d => Catalog.Device(d.def).kind == DeviceKind.Switch))
            {
                var sw = Catalog.Devices.Where(d => d.kind == DeviceKind.Switch && sim.IsUnlocked(d.id)).OrderByDescending(d => d.uplinkGbps).First();
                sim.AddDevice(rack, sw);
            }
            if (rack.linkCore < 0 && sim.AutoConnect(rack) != null) LayTrayToCore(sim, rack);
            var con = rack.contractId > 0 ? sim.FindContract(rack.contractId) : null;
            if (con != null && con.redundant) sim.MakeRedundant(rack);
            foreach (var d in rack.devices.Where(x => x.failed).ToList())
                if (sim.S.money > 50000) sim.ManualRepair(rack, d);
        }
    }
}
