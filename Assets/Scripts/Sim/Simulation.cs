using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    public class RackInfo
    {
        public float loadKW;
        public bool drawing;          // consuming power this hour
        public bool online;
        public string issue;
        public float linkGbps;
        public int servers, activeServers, ports, switches, usedU, failedDevices;
        public int cores, gpus;
        public float storage;
        public int zone = -1;
        public bool empty;
        public bool colo;
        public float airflow = 1f;
        public bool looseCabling;
        public string airflowNote;
        public bool redundant;
        public bool degraded;      // running on one path of a redundant pair
    }

    public class ZoneInfo
    {
        public int id;
        public List<int> tiles = new List<int>();
        public float heatKW, coolKW, temp = 21f;
        public Vector2 center;
        public int floor;
        public bool suppressed;
        public float raisedFrac;
    }

    public class Provision
    {
        public int racks, cores, gpus, qualServers;
        public float storage, bandwidth;
        public bool meets;
        public float service;   // 0..1 fraction of the requirement being delivered
        public string issue;
    }

    public class Popup
    {
        public string title, body;
        public List<KeyValuePair<string, Action>> options = new List<KeyValuePair<string, Action>>();
        public Popup Add(string label, Action a) { options.Add(new KeyValuePair<string, Action>(label, a)); return this; }
    }

    public partial class Simulation
    {
        public GameState S;
        public System.Random rng;

        public readonly Dictionary<int, PlacedObject> objById = new Dictionary<int, PlacedObject>();
        public int[] occupancy;
        public int[] zoneOf;
        public List<ZoneInfo> zones = new List<ZoneInfo>();
        public readonly Dictionary<int, RackInfo> racks = new Dictionary<int, RackInfo>();
        public Dictionary<int, Provision> provision => company.provision;

        // ---- multi-site ----
        public Company company;
        public int siteIndex;
        public SiteData Site => S.sites[siteIndex];
        public void Bind() => S.Bind(siteIndex);
        bool inSiteTick;
        public float siteHourlyCost;
        public float SiteEventMag(string type, float def) { var e = Site.events.FirstOrDefault(x => x.type == type); return e != null ? e.magnitude : def; }
        public readonly Dictionary<int, int> coreUsage = new Dictionary<int, int>();

        public float loadKW, itLoadKW, facilityKW, supplyKW, transformerKW, switchgearKW, upsKW, genKW, availableKW;
        public float ispGbps, ispNominal, bwDemand, netQuality = 1f, coolCapKW, heatKW;
        public int corePorts, corePortsUsed, deskCount;
        public bool outageActive, outageLong, powerShort;
        public float hourlyIncome { get => company.hourlyIncome; set => company.hourlyIncome = value; }
        public float hourlyCost { get => company.hourlyCost; set => company.hourlyCost = value; }

        public int tilesVersion = 1, objectsVersion = 1;
        public readonly HashSet<int> dirtyObjects = new HashSet<int>();
        public Queue<Popup> popups => company.popups;
        public event Action<string, int> OnLog { add => company.OnLog += value; remove => company.OnLog -= value; }

        bool zonesDirty = true;
        int placeCounter;

        public float FailureMul => S.difficulty == "Easy" ? 0.6f : S.difficulty == "Hard" ? 1.4f : 1f;

        /// <summary>Creates the company with one simulation per site and returns the active site's simulation.</summary>
        public Simulation(GameState state)
        {
            S = state;
            state.EnsureSites();
            company = new Company(state);
            siteIndex = Mathf.Clamp(state.activeSite, 0, state.sites.Count - 1);
            for (int i = 0; i < state.sites.Count; i++) company.sims.Add(i == siteIndex ? this : null);
            for (int i = 0; i < state.sites.Count; i++)
                if (i != siteIndex) company.sims[i] = new Simulation(state, company, i);
            InitSite();
            company.BindActive();
            foreach (var sim in company.sims) { sim.Bind(); sim.Recompute(); }
            company.BindActive();
        }

        Simulation(GameState state, Company c, int site)
        {
            S = state;
            company = c;
            siteIndex = site;
            InitSite();
        }

        internal static Simulation CreateSite(GameState state, Company c, int site) => new Simulation(state, c, site);

        void InitSite()
        {
            Bind();
            rng = new System.Random(S.seed * 7919 + S.hour + siteIndex * 104729);
            RebuildIndex();
            EnsureRivals();
            if (company.sims.All(x => x != null)) Recompute();
            if (S.candidates.Count == 0) RefreshCandidates();
        }

        // =====================================================================
        //  New game
        // =====================================================================
        public static GameState NewGame(int seed, string difficulty) => NewGame(seed, difficulty, "sandbox");

        public static GameState NewGame(int seed, string difficulty, string scenario)
        {
            var r = new System.Random(seed);
            var s = new GameState { seed = seed, difficulty = difficulty };
            s.city = ProcGen.GenerateCity(r);
            ProcGen.GenerateLot(s, r);
            s.money = difficulty == "Easy" ? 550000 : difficulty == "Hard" ? 200000 : 350000;
            s.rating = difficulty == "Hard" ? 40 : 50;
            s.hour = 8;
            s.nextOfferHour = s.hour + 24;
            s.scenario = scenario ?? "sandbox";
            s.city.carbonTax = r.NextDouble() < 0.4 ? (float)Math.Round(ProcGen.Range(r, 0.005f, 0.03f), 3) : 0f;
            s.city.solarFactor = s.city.climate == "Desert" ? 1.3f : s.city.climate == "Hot" ? 1.15f : s.city.climate == "Arctic" ? 0.6f : s.city.climate == "Cold" ? 0.8f : 1f;
            ApplyScenario(s);
            for (int i = 0; i < 3; i++) s.offers.Add(ProcGen.GenerateContract(s, r));
            s.current.month = 0;
            s.log.Add(new LogEntry { hour = s.hour, text = $"Welcome to {s.city.name}! {s.city.blurb}", severity = 1 });
            return s;
        }

        // =====================================================================
        //  Indexing & helpers
        // =====================================================================
        public void RebuildIndex()
        {
            objById.Clear();
            occupancy = new int[S.mapW * S.mapH * GameState.MaxFloors];
            placeCounter = 0;
            foreach (var o in S.objects)
            {
                objById[o.id] = o;
                MarkOccupancy(o, o.id);
                placeCounter = Math.Max(placeCounter, o.placedOrder);
            }
            zonesDirty = true;
            tilesVersion++;
            objectsVersion++;
        }

        void MarkOccupancy(PlacedObject o, int value)
        {
            var d = Catalog.Facility(o.def);
            if (d == null) return;
            Footprint(d, o.rot, out int w, out int h);
            for (int y = o.y; y < o.y + h; y++)
                for (int x = o.x; x < o.x + w; x++)
                    if (S.InBounds(x, y)) occupancy[S.Idx(o.floor, x, y)] = value;
        }

        public static void Footprint(FacilityDef d, int rot, out int w, out int h)
        {
            if (rot % 2 == 1) { w = d.h; h = d.w; } else { w = d.w; h = d.h; }
        }

        /// <summary>Object at a tile on the floor currently being viewed.</summary>
        public PlacedObject ObjectAt(int x, int y) => ObjectAtF(S.floor, x, y);

        public PlacedObject ObjectAtF(int f, int x, int y)
        {
            if (!S.InBounds(x, y) || f < 0 || f >= GameState.MaxFloors) return null;
            int id = occupancy[S.Idx(f, x, y)];
            return id != 0 && objById.TryGetValue(id, out var o) ? o : null;
        }

        /// <summary>Changes the floor being viewed and edited.</summary>
        public void SetFloor(int f)
        {
            f = Mathf.Clamp(f, 0, Mathf.Min(GameState.MaxFloors - 1, S.TopFloor + 1));
            if (f == S.floor) return;
            S.floor = f;
            tilesVersion++;
            objectsVersion++;
        }

        public void Log(string text, int severity = 0)
        {
            if (inSiteTick && company.MultiSite) text = $"[{S.siteName}] {text}";
            S.log.Add(new LogEntry { hour = S.hour, text = text, severity = severity });
            if (S.log.Count > 300) S.log.RemoveRange(0, S.log.Count - 300);
            company.RaiseLog(text, severity);
        }

        public bool Spend(float amount, bool capex = true)
        {
            if (amount > 0 && S.money < amount) return false;
            S.money -= amount;
            if (capex) S.current.capex += amount; else S.current.other += amount;
            return true;
        }

        void Refund(float amount)
        {
            S.money += amount;
            S.current.capex -= amount;
        }

        public bool IsUnlocked(string id)
        {
            var d = Catalog.Device(id);
            if (d != null) return d.research <= 0 || S.unlocked.Contains(id);
            var f = Catalog.Facility(id);
            if (f != null) return f.research <= 0 || S.unlocked.Contains(id);
            return false;
        }

        readonly Dictionary<int, Contract> contractCache = new Dictionary<int, Contract>();

        public Contract FindContract(int id)
        {
            if (contractCache.TryGetValue(id, out var c) && S.contracts.Contains(c)) return c;
            c = S.contracts.FirstOrDefault(x => x.id == id);
            if (c != null) contractCache[id] = c;
            return c;
        }

        List<PlacedObject> rackCache;
        int rackCacheVersion = -1, rackCacheCount = -1;

        /// <summary>All racks, cached until objects are added or removed.</summary>
        public List<PlacedObject> Racks
        {
            get
            {
                if (rackCache == null || rackCacheVersion != objectsVersion || rackCacheCount != Site.objects.Count)
                {
                    rackCache = Site.objects.Where(o => Catalog.Facility(o.def)?.kind == FacilityKind.Rack).ToList();
                    rackCacheVersion = objectsVersion;
                    rackCacheCount = Site.objects.Count;
                }
                return rackCache;
            }
        }

        public IEnumerable<PlacedObject> ObjectsOfKind(FacilityKind k) => S.objects.Where(o => Catalog.Facility(o.def)?.kind == k);

        public void MarkDirty(PlacedObject o) { if (o != null) dirtyObjects.Add(o.id); }

        bool EventActive(string type) => S.events.Any(e => e.type == type);
        float EventMag(string type, float def) { var e = S.events.FirstOrDefault(x => x.type == type); return e != null ? e.magnitude : def; }

        // =====================================================================
        //  Derived state
        // =====================================================================
        void RebuildZones()
        {
            var oldTemps = new Dictionary<int, float>();
            if (zoneOf != null)
                foreach (var z in zones) foreach (var t in z.tiles) oldTemps[t] = z.temp;

            zoneOf = new int[S.mapW * S.mapH * GameState.MaxFloors];
            for (int i = 0; i < zoneOf.Length; i++) zoneOf[i] = -1;
            zones.Clear();
            var stack = new Stack<int>();
            for (int i = 0; i < zoneOf.Length; i++)
            {
                if (zoneOf[i] != -1 || S.rooms[i] != (int)RoomType.ServerHall) continue;
                var z = new ZoneInfo { id = zones.Count };
                zones.Add(z);
                stack.Push(i);
                zoneOf[i] = z.id;
                float sumT = 0; int nT = 0;
                Vector2 c = Vector2.zero;
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    z.tiles.Add(t);
                    int fl = t / S.Plane, rem = t % S.Plane;
                    int x = rem % S.mapW, y = rem / S.mapW;
                    z.floor = fl;
                    c += new Vector2(x, y);
                    if (oldTemps.TryGetValue(t, out float ot)) { sumT += ot; nT++; }
                    TryPush(x + 1, y); TryPush(x - 1, y); TryPush(x, y + 1); TryPush(x, y - 1);
                    void TryPush(int nx, int ny)
                    {
                        if (!S.InBounds(nx, ny)) return;
                        int ni = S.Idx(z.floor, nx, ny);
                        if (zoneOf[ni] != -1 || S.rooms[ni] != (int)RoomType.ServerHall) return;
                        zoneOf[ni] = z.id;
                        stack.Push(ni);
                    }
                }
                z.center = c / Mathf.Max(1, z.tiles.Count);
                int raised = 0;
                foreach (var t in z.tiles) if (S.flags != null && (S.flags[t] & GameState.FlagRaised) != 0) raised++;
                z.raisedFrac = raised / (float)Mathf.Max(1, z.tiles.Count);
                z.temp = nT > 0 ? sumT / nT : 21f;
            }
            RebuildTrayComponents();
            zonesDirty = false;
        }

        public int ZoneAt(PlacedObject o) => zoneOf == null ? -1 : zoneOf[S.Idx(o.floor, o.x, o.y)];

        public void Recompute()
        {
            if (zonesDirty || zoneOf == null) RebuildZones();

            transformerKW = switchgearKW = upsKW = genKW = ispNominal = 0;
            corePorts = 0; deskCount = 0; facilityKW = 0; coolCapKW = 0;
            batteryKW = 0; batteryCapKWh = 0; batteryChargeKWh = 0;
            foreach (var z in zones) { z.coolKW = 0; z.heatKW = 0; }
            float heatMul = EventMag("heatwave", 1f) * S.city.coolingFactor;

            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d == null || d.kind == FacilityKind.Rack) continue;
                if (o.failed) continue;
                facilityKW += d.watts / 1000f;
                switch (d.kind)
                {
                    case FacilityKind.Transformer: transformerKW += d.supplyKW; break;
                    case FacilityKind.Switchgear: switchgearKW += d.supplyKW; break;
                    case FacilityKind.Ups: upsKW += d.backupKW; break;
                    case FacilityKind.Generator: genKW += d.backupKW; break;
                    case FacilityKind.Isp: ispNominal += d.bandwidth; break;
                    case FacilityKind.CoreSwitch: corePorts += d.ports; break;
                    case FacilityKind.Desk: deskCount += d.staff; break;
                    case FacilityKind.Battery: if (o.charge > 1f) batteryKW += d.supplyKW; batteryCapKWh += d.capacityKWh; batteryChargeKWh += o.charge; break;
                    case FacilityKind.Suppression:
                        int sz = ZoneAt(o);
                        if (sz >= 0) zones[sz].suppressed = true;
                        break;
                    case FacilityKind.Cooling:
                        int zi = ZoneAt(o);
                        if (zi >= 0) zones[zi].coolKW += d.coolingKW * heatMul * (1f + 0.1f * zones[zi].raisedFrac);
                        coolCapKW += d.coolingKW * heatMul;
                        break;
                }
            }
            supplyKW = Mathf.Min(transformerKW, switchgearKW);
            ComputeSolar();
            outageActive = EventActive("outage");
            outageLong = outageActive && EventMag("outage", 0) > 0.5f;
            availableKW = outageActive ? (outageLong ? Mathf.Min(upsKW, genKW) : upsKW) + batteryKW : supplyKW;
            ispGbps = ispNominal * EventMag("fibercut", 1f);

            coreUsage.Clear();
            foreach (var o in Racks)
            {
                if (o.linkCore > 0) coreUsage[o.linkCore] = (coreUsage.TryGetValue(o.linkCore, out int u) ? u : 0) + 1;
                if (o.linkCore2 > 0) coreUsage[o.linkCore2] = (coreUsage.TryGetValue(o.linkCore2, out int u2) ? u2 : 0) + 1;
            }
            corePortsUsed = coreUsage.Values.Sum();

            // ---- Per-rack evaluation ----
            racks.Clear();
            var rackList = Racks.OrderBy(o => o.placedOrder).ToList();
            float rackLoad = 0;
            foreach (var o in rackList)
            {
                var d = Catalog.Facility(o.def);
                var info = new RackInfo { zone = ZoneAt(o) };
                racks[o.id] = info;
                var contract = o.contractId > 0 ? FindContract(o.contractId) : null;
                info.colo = contract != null && contract.type == ContractType.Colocation &&
                            (contract.status == ContractStatus.Setup || contract.status == ContractStatus.Active);

                float bestUplink = 0;
                foreach (var dev in o.devices)
                {
                    var dd = Catalog.Device(dev.def);
                    if (dd == null) continue;
                    info.usedU += dd.heightU;
                    if (dev.failed) { info.failedDevices++; continue; }
                    info.loadKW += dd.watts / 1000f;
                    if (dd.kind == DeviceKind.Switch) { info.switches++; info.ports += dd.ports; bestUplink = Mathf.Max(bestUplink, dd.uplinkGbps); }
                    else info.servers++;
                }
                if (info.colo) info.loadKW += contract.reqKWPerRack;
                info.empty = o.devices.Count == 0 && !info.colo;
                info.drawing = true;

                bool ok1 = CoreOk(o.linkCore, out var cd1, out string coreIssue);
                bool ok2 = CoreOk(o.linkCore2, out var cd2, out _);
                bool anyCore = ok1 || ok2;
                bool hasSecond = o.linkCore2 > 0;

                if (info.empty) info.issue = "Empty rack";
                else if (info.loadKW > d.rackKW) { info.issue = $"PDU overloaded ({info.loadKW:0.0}/{d.rackKW:0} kW)"; info.drawing = false; }
                else if (o.pduTripped && !o.dualPdu) { info.issue = "PDU tripped - waiting for an electrician"; info.drawing = false; }
                else if (info.zone >= 0 && zones[info.zone].temp >= 45f) { info.issue = "Thermal shutdown"; info.drawing = false; }
                else if (info.switches == 0) info.issue = info.failedDevices > 0 && o.devices.Any(x => x.failed && Catalog.Device(x.def)?.kind == DeviceKind.Switch) ? "ToR switch failed" : "No ToR switch";
                else if (!anyCore) info.issue = hasSecond ? "Both core uplinks down" : coreIssue;
                else if (ispGbps <= 0) info.issue = ispNominal > 0 ? "ISP fiber cut" : "No ISP uplink";
                else info.issue = null;

                float port = Mathf.Max(ok1 ? cd1.portGbps : 0, ok2 ? cd2.portGbps : 0);
                info.linkGbps = Mathf.Min(bestUplink, port);
                info.redundant = o.dualPdu && info.switches >= 2 && ok1 && ok2 && o.linkCore != o.linkCore2;
                int swTotal = o.devices.Count(x => Catalog.Device(x.def)?.kind == DeviceKind.Switch);
                info.degraded = (o.pduTripped && o.dualPdu) || (hasSecond && (ok1 != ok2)) || (swTotal >= 2 && info.switches == 1);
                info.airflow = AirflowFactor(o, out info.airflowNote);
                info.looseCabling = (o.linkCore > 0 && !TrayRouted(o, o.linkCore)) || (o.linkCore2 > 0 && !TrayRouted(o, o.linkCore2));
                if (info.looseCabling) { info.airflow *= 1.08f; info.airflowNote += " - loose cabling blocks airflow (+8%)"; }
                if (info.drawing) rackLoad += info.loadKW;
            }

            // ---- Power budget: shed the newest racks first when short ----
            itLoadKW = rackLoad;
            loadKW = rackLoad + facilityKW;
            powerShort = false;
            if (loadKW > availableKW + 0.001f)
            {
                powerShort = true;
                for (int i = rackList.Count - 1; i >= 0 && loadKW > availableKW + 0.001f; i--)
                {
                    var info = racks[rackList[i].id];
                    if (!info.drawing || info.loadKW <= 0) continue;
                    info.drawing = false;
                    loadKW -= info.loadKW;
                    itLoadKW -= info.loadKW;
                    info.issue = outageActive ? "Grid outage - no backup power" : "Insufficient power capacity";
                }
                if (loadKW > availableKW + 0.001f) loadKW = Mathf.Max(0, availableKW);
            }

            heatKW = 0;
            foreach (var o in rackList)
            {
                var info = racks[o.id];
                if (info.drawing && info.zone >= 0) { zones[info.zone].heatKW += info.loadKW * info.airflow; heatKW += info.loadKW * info.airflow; }
                info.online = info.drawing && info.issue == null;

                // Count usable servers (limited by switch ports).
                if (info.online)
                {
                    int portsLeft = info.ports;
                    foreach (var dev in o.devices)
                    {
                        if (dev.failed) continue;
                        var dd = Catalog.Device(dev.def);
                        if (dd == null || dd.kind != DeviceKind.Server) continue;
                        if (portsLeft <= 0) break;
                        portsLeft--;
                        info.activeServers++;
                        info.cores += dd.cores; info.storage += dd.storageTB; info.gpus += dd.gpus;
                    }
                    if (info.servers > info.ports && info.ports > 0 && !info.colo) info.issue = null; // still online, warn in UI
                }
            }

            company.RecomputeProvision();
        }

        // =====================================================================
        //  Time
        // =====================================================================
        /// <summary>Advances the whole company (every site) by one hour.</summary>
        public void TickHour() => company.TickHour();

        /// <summary>Per-site hour: events, thermals, power, failures, staff, energy and site costs. Site must be bound.</summary>
        internal void TickSite()
        {
            inSiteTick = true;
            try
            {
                S.events.RemoveAll(e =>
                {
                    if (S.hour < e.endHour) return false;
                    if (!e.global || siteIndex == 0) Log($"{e.label} has ended.", 1);
                    OnEventEnded(e);
                    return true;
                });
                UpdateTemperatures();
                Recompute();
                RollFailures();
                StaffTick();
                HourSite();
                FinancesSite();
                if (S.hour % Catalog.HoursPerDay == 0) DailyWeather();
            }
            finally { inSiteTick = false; }
        }

        /// <summary>Company hour: contracts, tickets, income, offers, events, progression. Runs once on the active site.</summary>
        internal void TickCompany()
        {
            UpdateContracts();
            TicketsHour();
            FinancesCompany();
            if (S.hour % Catalog.HoursPerDay == 0) DailyTick();
            CheckLevel();
        }

        void UpdateTemperatures()
        {
            foreach (var z in zones)
            {
                float eq;
                if (z.heatKW <= 0.01f) eq = 19f;
                else if (z.coolKW <= 0.01f) eq = 75f;
                else
                {
                    float ratio = z.heatKW / z.coolKW;
                    eq = ratio <= 1f ? 19f + 10f * ratio : 29f + 38f * (ratio - 1f);
                }
                eq = Mathf.Clamp(eq, 15f, 85f);
                z.temp += (eq - z.temp) * 0.3f;
            }
        }

        void RollFailures()
        {
            if (S.cheatNoFailures) return;
            float mul = FailureMul;
            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d == null) continue;
                float tempFactor = 1f;
                int zi = ZoneAt(o);
                if (zi >= 0 && zones[zi].temp > 30f) tempFactor = 1f + (zones[zi].temp - 30f) * 0.45f;

                if (d.kind == FacilityKind.Rack)
                {
                    if (racks.TryGetValue(o.id, out var info) && !info.drawing) continue;
                    if (!o.pduTripped && o.devices.Count > 0 && rng.NextDouble() < 0.0007f / 24f * mul)
                    {
                        o.pduTripped = true;
                        MarkDirty(o);
                        Log(o.dualPdu ? $"PDU A tripped in rack #{o.id} - running on feed B." : $"PDU tripped in rack #{o.id}! Rack is down.", o.dualPdu ? 2 : 3);
                    }
                    foreach (var dev in o.devices)
                    {
                        if (dev.failed) continue;
                        var dd = Catalog.Device(dev.def);
                        float daily = dd.kind == DeviceKind.Switch ? 0.0006f : 0.0011f;
                        if (rng.NextDouble() < daily / 24f * tempFactor * mul * AgeFailureFactor(dev))
                        {
                            dev.failed = true;
                            dev.repairLeft = 4;
                            MarkDirty(o);
                            Log($"{dd.name} failed in rack #{o.id}.", 2);
                        }
                    }
                }
                else if (!o.failed && d.kind != FacilityKind.Desk && d.kind != FacilityKind.Ups && d.kind != FacilityKind.Suppression)
                {
                    if (rng.NextDouble() < 0.0005f / 24f * mul)
                    {
                        o.failed = true;
                        o.repairLeft = 10;
                        MarkDirty(o);
                        Log($"{d.name} #{o.id} has broken down!", 3);
                    }
                }
            }
        }


        void DoRepairs()
        {
            int techs = EffectiveTechs;
            if (techs <= 0) return;
            // Facilities first (they take down whole halls), then rack devices.
            foreach (var o in S.objects)
            {
                if (techs <= 0) break;
                if (!o.failed) continue;
                techs--;
                if (--o.repairLeft <= 0)
                {
                    o.failed = false;
                    var d = Catalog.Facility(o.def);
                    Spend(d.cost * 0.05f, false);
                    MarkDirty(o);
                    Log($"Technicians repaired {d.name} #{o.id}.", 1);
                }
            }
            foreach (var o in S.objects)
            {
                if (techs <= 0) break;
                foreach (var dev in o.devices)
                {
                    if (techs <= 0) break;
                    if (!dev.failed) continue;
                    techs--;
                    if (--dev.repairLeft <= 0)
                    {
                        dev.failed = false;
                        var dd = Catalog.Device(dev.def);
                        Spend(dd.cost * 0.05f, false);
                        MarkDirty(o);
                        Log($"Technicians replaced a failed {dd.name}.", 1);
                    }
                }
            }
        }

        public float ManualRepairCost(PlacedObject o, RackDevice dev)
        {
            if (dev != null) return Mathf.Round(Catalog.Device(dev.def).cost * 0.25f);
            return Mathf.Round(Catalog.Facility(o.def).cost * 0.25f);
        }

        public string ManualRepair(PlacedObject o, RackDevice dev)
        {
            float cost = ManualRepairCost(o, dev);
            if (!Spend(cost, false)) return "Not enough money";
            if (dev != null) dev.failed = false; else { o.failed = false; o.pduTripped = false; }
            MarkDirty(o);
            Recompute();
            return null;
        }

        void UpdateContracts()
        {
            foreach (var c in S.contracts.ToList())
            {
                if (c.status == ContractStatus.Setup)
                {
                    provision.TryGetValue(c.id, out var p);
                    c.meeting = p != null && p.meets;
                    c.lastIssue = p?.issue;
                    if (c.meeting)
                    {
                        c.status = ContractStatus.Active;
                        c.startHour = S.hour;
                        c.nextMonthHour = S.hour + Catalog.HoursPerMonth;
                        S.money += c.signingBonus;
                        S.current.bonuses += c.signingBonus;
                        S.lifetimeRevenue += c.signingBonus;
                        Log($"{c.customer} is live! Signing bonus ${c.signingBonus:N0} received.", 1);
                        MarkContractRacks(c);
                    }
                    else if (S.hour >= c.deadlineHour)
                    {
                        float penalty = Mathf.Round(c.monthlyPay * 0.5f);
                        S.money -= penalty;
                        S.current.penalties += penalty;
                        S.rating = Mathf.Max(0, S.rating - 8);
                        S.contractsFailed++;
                        EndContract(c, ContractStatus.Cancelled);
                        Log($"Missed setup deadline for {c.customer}. Contract cancelled, ${penalty:N0} penalty.", 3);
                    }
                }
                else if (c.status == ContractStatus.Active)
                {
                    provision.TryGetValue(c.id, out var p);
                    bool meets = p != null && p.meets;
                    if (meets != c.meeting) MarkContractRacks(c);
                    c.meeting = meets;
                    c.lastIssue = p?.issue;
                    c.monthHours++; c.totalHours++;
                    c.service = p != null ? p.service : 0f;
                    SatisfactionHour(c, meets);
                    // Partial capacity counts as partial downtime; a total outage counts as a full hour.
                    if (!meets) { float down = 1f - c.service; c.monthDown += down; c.totalDown += down; }
                    float inc = c.monthlyPay / Catalog.HoursPerMonth;
                    S.money += inc; S.current.income += inc; c.earned += inc; S.lifetimeRevenue += inc;
                    if (S.hour >= c.nextMonthHour) MonthEnd(c);
                }
            }
            S.offers.RemoveAll(o => S.hour >= o.expiresHour);
        }

        void MarkContractRacks(Contract c) => company.MarkContractRacks(c.id);

        public float Uptime(Contract c, bool month)
        {
            int h = month ? c.monthHours : c.totalHours;
            float d = month ? c.monthDown : c.totalDown;
            return h <= 0 ? 100f : 100f * (1f - d / h);
        }

        void MonthEnd(Contract c)
        {
            float uptime = Uptime(c, true);
            c.monthsServed++;
            c.nextMonthHour += Catalog.HoursPerMonth;
            if (uptime + 0.0001f < c.sla)
            {
                float credit = Mathf.Round(c.monthlyPay * (uptime < c.sla - 1f ? 0.5f : 0.25f));
                S.money -= credit;
                S.current.penalties += credit;
                S.rating = Mathf.Max(0, S.rating - 3);
                c.satisfaction = Mathf.Max(0, c.satisfaction - 12);
                c.breaches++;
                int maxBreaches = c.traits.Contains("Strict") ? 2 : 3;
                Log($"SLA breach for {c.customer}: {uptime:0.00}% uptime vs {c.sla}% SLA. ${credit:N0} service credit.", 3);
                if (c.breaches >= maxBreaches)
                {
                    S.rating = Mathf.Max(0, S.rating - 8);
                    S.contractsFailed++;
                    EndContract(c, ContractStatus.Cancelled);
                    popups.Enqueue(new Popup { title = "Contract terminated", body = $"{c.customer} has terminated their contract after {c.breaches} SLA breaches. Your reputation has suffered." }.Add("OK", null));
                    return;
                }
            }
            else
            {
                S.xp += MonthXp(c);
                S.rating = Mathf.Min(100, S.rating + 1.2f);
                c.satisfaction = Mathf.Min(100, c.satisfaction + 2);
            }
            EcoCheck(c);
            c.monthHours = 0; c.monthDown = 0;

            if (c.monthsServed >= c.months)
            {
                S.xp += MonthXp(c) * 3;
                S.rating = Mathf.Min(100, S.rating + 3f);
                S.contractsCompleted++;
                var rackIds = company.AllRacks().Where(x => x.rack.contractId == c.id).Select(x => x.rack.id).ToList();
                EndContract(c, ContractStatus.Completed);
                Log($"Contract with {c.customer} completed successfully! (+reputation)", 1);
                float overall = Uptime(c, false);
                if (c.satisfaction > 75) GenerateReferral(c);
                if (overall >= c.sla - 0.5f && rng.NextDouble() < 0.2 + c.satisfaction / 125f)
                {
                    int extra = rng.Next(4, 10);
                    float newPay = Mathf.Round(c.monthlyPay * ProcGen.Range(rng, 1.05f, 1.2f) / 50f) * 50f;
                    popups.Enqueue(new Popup
                    {
                        title = "Renewal offer",
                        body = $"{c.customer} was happy with your service ({overall:0.00}% uptime) and wants to renew for {extra} more months at ${newPay:N0}/month, keeping the same racks."
                    }.Add("Renew", () =>
                    {
                        c.status = ContractStatus.Active;
                        c.months += extra;
                        c.monthlyPay = newPay;
                        c.isRenewal = true;
                        if (!c.traits.Contains("Renewal")) c.traits.Add("Renewal");
                        foreach (var id in rackIds)
                            if (company.FindObject(id, out var rs, out var r) && r.contractId < 0) { r.contractId = c.id; rs.MarkDirty(r); }
                        Log($"Renewed {c.customer} for {extra} months.", 1);
                        company.RecomputeProvision();
                    }).Add("Decline", null));
                }
            }
        }

        public static int MonthXp(Contract c) => Mathf.RoundToInt(15f + Mathf.Sqrt(c.monthlyPay) * 0.6f);

        void EndContract(Contract c, ContractStatus status)
        {
            c.status = status;
            company.ReleaseContractRacks(c.id);
            Recompute();
        }

        void FinancesSite()
        {
            float grid = gridKW;
            float powerCost = grid * (PowerPriceNow + S.city.carbonTax);
            float netCost = 0;
            foreach (var o in ObjectsOfKind(FacilityKind.Isp)) netCost += Catalog.Facility(o.def).monthly / Catalog.HoursPerMonth;
            float salaries = (S.technicians * Catalog.TechSalary + S.staff.Sum(m => m.salary)) / Catalog.HoursPerMonth;
            S.money -= powerCost + netCost + salaries;
            S.current.power += powerCost;
            S.current.network += netCost;
            S.current.salaries += salaries;
            siteHourlyCost = powerCost + netCost + salaries;
        }

        void FinancesCompany()
        {
            hourlyCost = company.sims.Sum(x => x.siteHourlyCost);
            hourlyIncome = S.contracts.Where(c => c.status == ContractStatus.Active).Sum(c => c.monthlyPay) / Catalog.HoursPerMonth;

            if (S.cheatInfiniteMoney && S.money < 500_000_000f) S.money = 1_000_000_000f;
            if (S.money < 0 && !S.cheatNoBankruptcy)
            {
                if (S.bankruptHours == 0) Log("You are in debt! Get back above $0 within 30 days or the bank will foreclose.", 3);
                S.bankruptHours++;
                if (S.bankruptHours >= 30 * 24 && !S.gameOver)
                {
                    S.gameOver = true;
                    popups.Enqueue(new Popup { title = "Bankrupt", body = "After 30 days in debt the bank has foreclosed on your data center. Game over.\n\nStart a new game with a fresh seed to try again." }.Add("OK", null));
                }
            }
            else S.bankruptHours = 0;
        }

        void DailyTick()
        {
            // New contract offers.
            int maxOffers = 4 + S.Level / 2;
            if (S.hour >= S.nextOfferHour)
            {
                if (S.offers.Count < maxOffers)
                {
                    var c = ProcGen.GenerateContract(S, rng);
                    S.offers.Add(c);
                    Log($"New contract offer: {c.customer} wants {ProcGen.Describe(c).ToLower()}.", 0);
                }
                float speed = Mathf.Lerp(3.2f, 1.2f, S.rating / 100f);
                S.nextOfferHour = S.hour + Mathf.RoundToInt(24 * speed * ProcGen.Range(rng, 0.6f, 1.4f));
            }

            RollEvents();
            DailyExtras();

            if (S.Day % Catalog.DaysPerMonth == 0)
            {
                MonthlyExtras();
                S.current.month = S.Month - 1;
                S.reports.Add(S.current);
                if (S.reports.Count > 36) S.reports.RemoveAt(0);
                var r = S.current;
                Log($"Month closed. Operating profit: {(r.Operating >= 0 ? "+" : "-")}${Mathf.Abs(r.Operating):N0}" + (r.capex > 0 ? $" (invested ${r.capex:N0} in construction & hardware)" : ""), r.Operating >= 0 ? 1 : 2);
                S.current = new MonthReport { month = S.Month };
                AutoSaveRequested = true;
            }
        }

        public bool AutoSaveRequested;

        void RollEvents()
        {
            if (S.Day < 3 || S.cheatNoEvents) return;
            float bad = 0.07f * S.city.disasterRate;
            if (rng.NextDouble() < bad)
            {
                var opts = new List<KeyValuePair<string, float>>
                {
                    new KeyValuePair<string, float>("outage", 2f),
                    new KeyValuePair<string, float>("heatwave", S.city.coolingFactor < 0.95f ? 2f : 0.8f),
                    new KeyValuePair<string, float>("powerspike", 1f),
                    new KeyValuePair<string, float>("fibercut", ispNominal > 0 ? 1f : 0f),
                    new KeyValuePair<string, float>("recall", Racks.Sum(r => r.devices.Count) > 8 ? 0.8f : 0f),
                    new KeyValuePair<string, float>("surge", bwDemand > 0 ? 1f : 0f),
                    new KeyValuePair<string, float>("fire", Racks.Any(r => r.devices.Count > 0) ? 0.45f : 0f),
                    new KeyValuePair<string, float>("leak", ObjectsOfKind(FacilityKind.Cooling).Any() ? 0.5f : 0f),
                    new KeyValuePair<string, float>("ddos", S.contracts.Any(c => c.status == ContractStatus.Active) ? 0.8f : 0f),
                    new KeyValuePair<string, float>("shortage", 0.5f),
                    new KeyValuePair<string, float>("bankrupt", S.contracts.Count(c => c.status == ContractStatus.Active) >= 3 ? 0.3f : 0f),
                    new KeyValuePair<string, float>("regulation", 0.2f),
                    new KeyValuePair<string, float>("pricewar", 0.4f),
                    new KeyValuePair<string, float>("breakin", Racks.Any(r => r.devices.Count > 0) ? 0.35f : 0f),
                    new KeyValuePair<string, float>("flood", S.city.climate == "Desert" ? 0f : (S.weather == "Rain" || S.weather == "Storm") ? 1.0f : 0.15f),
                };
                string pick = WeightedPick(opts);
                if (pick != null) TriggerOnSite(pick);
            }
            if (rng.NextDouble() < 0.05f)
            {
                var active = S.contracts.Where(c => c.status == ContractStatus.Active && !c.expanded && c.type != ContractType.Colocation).ToList();
                var opts = new List<KeyValuePair<string, float>>
                {
                    new KeyValuePair<string, float>("expansion", active.Count > 0 ? (active.Any(c => c.traits.Contains("Growing")) ? 4f : 1.5f) : 0f),
                    new KeyValuePair<string, float>("grant", 0.8f),
                    new KeyValuePair<string, float>("press", S.contractsCompleted > 0 ? 0.6f : 0f),
                };
                string pick = WeightedPick(opts);
                if (pick != null) TriggerEvent(pick);
            }
        }

        string WeightedPick(List<KeyValuePair<string, float>> opts)
        {
            float total = opts.Sum(o => o.Value);
            if (total <= 0) return null;
            float r = (float)rng.NextDouble() * total;
            foreach (var o in opts) { r -= o.Value; if (r <= 0) return o.Key; }
            return opts.Last().Key;
        }

        public void TriggerEvent(string type)
        {
            switch (type)
            {
                case "outage":
                {
                    bool longOut = S.Day > 60 && rng.NextDouble() < 0.3;
                    int hours = longOut ? rng.Next(6, 19) : rng.Next(1, 4);
                    AddEvent(type, hours, longOut ? 1f : 0f, longOut ? "Major grid outage" : "Grid power blip");
                    string cover = longOut ? (genKW > 0 && upsKW > 0 ? "Generators are taking the load." : "No generator backup - expect downtime!")
                                           : (upsKW > 0 ? "UPS is carrying the load." : "No UPS - racks are going dark!");
                    Log($"{(longOut ? "Major grid outage" : "Brief power outage")} ({hours}h). {cover}", 3);
                    break;
                }
                case "heatwave":
                {
                    int days = rng.Next(3, 7);
                    AddEvent(type, days * 24, 0.75f, "Heat wave");
                    Log($"Heat wave! Cooling capacity reduced by 25% for {days} days.", 2);
                    break;
                }
                case "powerspike":
                {
                    int days = rng.Next(7, 16);
                    AddEvent(type, days * 24, 1.6f, "Energy price spike");
                    Log($"Energy prices spike 60% for {days} days.", 2);
                    break;
                }
                case "fibercut":
                {
                    int hours = rng.Next(4, 21);
                    AddEvent(type, hours, 0.5f, "Fiber cut");
                    Log($"Construction crew cut a fiber line! ISP capacity halved for {hours}h.", 3);
                    break;
                }
                case "surge":
                {
                    int hours = rng.Next(12, 49);
                    AddEvent(type, hours, 1.6f, "Traffic surge");
                    Log($"Traffic surge: customer bandwidth demand +60% for {hours}h.", 2);
                    break;
                }
                case "recall":
                {
                    var all = new List<(PlacedObject, RackDevice)>();
                    foreach (var r in Racks) foreach (var d in r.devices) if (!d.failed && Catalog.Device(d.def).kind == DeviceKind.Server) all.Add((r, d));
                    int n = Mathf.Min(all.Count, rng.Next(2, 6));
                    for (int i = 0; i < n; i++)
                    {
                        int k = rng.Next(all.Count);
                        all[k].Item2.failed = true; all[k].Item2.repairLeft = 4;
                        MarkDirty(all[k].Item1);
                        all.RemoveAt(k);
                    }
                    Log($"Faulty firmware batch! {n} servers crashed and need repair.", 3);
                    break;
                }
                case "grant":
                {
                    float amt = Mathf.Round(ProcGen.Range(rng, 5000, 20000) * (1 + S.Level * 0.5f) / 500f) * 500f;
                    S.money += amt; S.current.bonuses += amt;
                    Log($"{S.city.name} awarded you a ${amt:N0} tech-investment grant!", 1);
                    break;
                }
                case "press":
                {
                    S.rating = Mathf.Min(100, S.rating + 5);
                    Log("A trade magazine praised your reliability. Reputation +5.", 1);
                    break;
                }
                case "expansion":
                {
                    var active = S.contracts.Where(c => c.status == ContractStatus.Active && !c.expanded && c.type != ContractType.Colocation).ToList();
                    if (active.Count == 0) break;
                    var growing = active.Where(c => c.traits.Contains("Growing")).ToList();
                    var c = growing.Count > 0 ? growing[rng.Next(growing.Count)] : active[rng.Next(active.Count)];
                    float f = ProcGen.Range(rng, 1.3f, 1.8f);
                    float payMul = 1f + (f - 1f) * 1.1f;
                    popups.Enqueue(new Popup
                    {
                        title = "Expansion request",
                        body = $"{c.customer} is growing fast and wants to scale up their {Catalog.TypeName(c.type)} contract by {(f - 1) * 100:0}%.\n\nMonthly pay rises from ${c.monthlyPay:N0} to ${Mathf.Round(c.monthlyPay * payMul / 50) * 50:N0}. You'll need to provision the extra capacity immediately or start accruing downtime."
                    }.Add("Accept", () =>
                    {
                        c.expanded = true;
                        c.reqServers = Mathf.CeilToInt(c.reqServers * f);
                        c.reqCores = Mathf.CeilToInt(c.reqCores * f / 8f) * 8;
                        c.reqStorageTB = Mathf.Ceil(c.reqStorageTB * f / 10f) * 10f;
                        c.reqGpus = Mathf.CeilToInt(c.reqGpus * f / 4f) * 4;
                        c.reqBandwidth = Mathf.Round(c.reqBandwidth * f * 2f) / 2f;
                        c.monthlyPay = Mathf.Round(c.monthlyPay * payMul / 50) * 50;
                        Log($"{c.customer} expanded their contract.", 1);
                        Recompute();
                    }).Add("Decline", () => { c.expanded = true; }));
                    break;
                }
                default:
                    TriggerExtraEvent(type);
                    break;
            }
            Recompute();
        }

        void AddEvent(string type, int hours, float mag, string label)
        {
            S.events.Add(new ActiveEvent { type = type, startHour = S.hour, endHour = S.hour + hours, magnitude = mag, label = label });
        }

        void CheckLevel()
        {
            int lvl = S.Level;
            if (lvl > S.peakLevel)
            {
                S.peakLevel = lvl;
                var newTech = Catalog.Devices.Where(d => d.level == lvl).Select(d => d.name)
                    .Concat(Catalog.Facilities.Where(f => f.level == lvl).Select(f => f.name)).ToList();
                string list = newTech.Count > 0 ? "\n\nNew technology available to research:\n- " + string.Join("\n- ", newTech) : "";
                Log($"Reputation level {lvl} reached!", 1);
                popups.Enqueue(new Popup { title = $"Level {lvl}!", body = $"Your data center's reputation has grown. Larger, more lucrative contracts will start appearing.{list}" }.Add("Great", null));
            }
        }

        // =====================================================================
        //  Player actions: building
        // =====================================================================
        public static RectInt Normalize(Vector2Int a, Vector2Int b)
        {
            int x0 = Mathf.Min(a.x, b.x), y0 = Mathf.Min(a.y, b.y);
            return new RectInt(x0, y0, Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);
        }

        bool CanFoundationAt(int f, int x, int y)
        {
            if (!S.InBounds(x, y) || !S.Owned(x, y)) return false;
            int i = S.Idx(f, x, y);
            if (S.rooms[i] != 0 || occupancy[i] != 0) return false;
            if (f == 0) return S.obstacles[y * S.mapW + x] == 0;
            return S.RoomAt(f - 1, x, y) != RoomType.None;   // upper floors need structure below
        }

        public int BuildFoundation(RectInt r, bool apply, out float cost)
        {
            int f = S.floor;
            cost = 0; int n = 0;
            float per = f == 0 ? Catalog.FoundationCost : Catalog.UpperFloorCost;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (CanFoundationAt(f, x, y)) { n++; cost += per; }
            if (!apply || n == 0) return n;
            Checkpoint();
            if (!Spend(cost)) { Log("Not enough money to build that.", 2); return 0; }
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (CanFoundationAt(f, x, y)) S.rooms[S.Idx(f, x, y)] = (int)RoomType.Corridor;
            TilesChanged();
            return n;
        }

        public int PaintRoom(RectInt r, RoomType t, bool apply, out float cost)
        {
            int f = S.floor;
            cost = 0; int n = 0;
            var tiles = new List<int>();
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (!S.InBounds(x, y)) continue;
                    int i = S.Idx(f, x, y);
                    if (S.rooms[i] == 0 || S.rooms[i] == (int)t) continue;
                    var o = ObjectAt(x, y);
                    if (o != null && Catalog.Facility(o.def).room != t) continue;
                    tiles.Add(i);
                }
            n = tiles.Count;
            cost = t == RoomType.Corridor ? 0 : n * Catalog.RoomPaintCost;
            if (!apply || n == 0) return n;
            Checkpoint();
            if (!Spend(cost)) { Log("Not enough money.", 2); return 0; }
            foreach (var i in tiles) S.rooms[i] = (int)t;
            TilesChanged();
            return n;
        }

        /// <summary>Demolish works in layers on the current floor: objects, trays/raised floor, rooms, foundation, then (ground only) trees and rocks.</summary>
        public int Demolish(RectInt r, bool apply, out float cost, out string what)
        {
            int f = S.floor;
            cost = 0; what = "";
            if (apply) Checkpoint();
            var objs = new HashSet<PlacedObject>();
            int rooms = 0, floorsN = 0, obst = 0, blocked = 0;
            float obstCost = 0;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (!S.InBounds(x, y)) continue;
                    int i = S.Idx(f, x, y);
                    var o = ObjectAt(x, y);
                    if (o != null) objs.Add(o);
                    if (S.rooms[i] > (int)RoomType.Corridor) rooms++;
                    else if (S.rooms[i] == (int)RoomType.Corridor)
                    {
                        if (f + 1 < GameState.MaxFloors && S.RoomAt(f + 1, x, y) != RoomType.None) blocked++;
                        else floorsN++;
                    }
                    if (f == 0 && S.obstacles[y * S.mapW + x] != 0 && S.Owned(x, y)) { obst++; obstCost += S.obstacles[y * S.mapW + x] == 1 ? 150 : 400; }
                }
            if (objs.Count > 0)
            {
                cost = -objs.Sum(o => RemovalRefund(o));
                what = $"Remove {objs.Count} object(s)";
                if (apply) foreach (var o in objs.ToList()) RemoveObject(o);
                return objs.Count;
            }
            int trayN = 0, raisedN = 0;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (S.HasFlag(x, y, GameState.FlagTray)) trayN++;
                    if (S.HasFlag(x, y, GameState.FlagRaised)) raisedN++;
                }
            if (trayN > 0 || raisedN > 0)
            {
                int flag = trayN > 0 ? GameState.FlagTray : GameState.FlagRaised;
                int n = trayN > 0 ? trayN : raisedN;
                cost = -n * (trayN > 0 ? TrayCost : RaisedCost) * 0.5f;
                what = trayN > 0 ? $"Remove {n} cable tray tile(s)" : $"Remove {n} raised floor tile(s)";
                if (apply)
                {
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            if (S.InBounds(x, y)) S.flags[S.Idx(f, x, y)] &= ~flag;
                    Refund(-cost);
                    TilesChanged();
                    objectsVersion++;
                }
                return n;
            }
            if (rooms > 0)
            {
                what = $"Revert {rooms} tile(s) to corridor";
                if (apply)
                {
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            if (S.InBounds(x, y) && S.rooms[S.Idx(f, x, y)] > 1) S.rooms[S.Idx(f, x, y)] = 1;
                    TilesChanged();
                }
                return rooms;
            }
            if (floorsN > 0)
            {
                float per = f == 0 ? Catalog.FoundationCost : Catalog.UpperFloorCost;
                cost = -floorsN * per * 0.5f;
                what = $"Remove {floorsN} {(f == 0 ? "foundation" : "floor slab")} tile(s)" + (blocked > 0 ? $" ({blocked} hold up the floor above)" : "");
                if (apply)
                {
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                        {
                            if (!S.InBounds(x, y)) continue;
                            int i = S.Idx(f, x, y);
                            if (S.rooms[i] != 1) continue;
                            if (f + 1 < GameState.MaxFloors && S.RoomAt(f + 1, x, y) != RoomType.None) continue;
                            S.rooms[i] = 0;
                            S.flags[i] = 0;
                        }
                    Refund(-cost);
                    TilesChanged();
                }
                return floorsN;
            }
            if (blocked > 0) { what = "Remove the floor above first"; return 0; }
            if (obst > 0)
            {
                cost = obstCost;
                what = $"Clear {obst} tree(s)/rock(s)";
                if (apply)
                {
                    if (!Spend(cost)) { Log("Not enough money to clear the land.", 2); return 0; }
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            if (S.InBounds(x, y) && S.Owned(x, y)) S.obstacles[y * S.mapW + x] = 0;
                    TilesChanged();
                }
                return obst;
            }
            return 0;
        }

        void TilesChanged()
        {
            zonesDirty = true;
            tilesVersion++;
            Recompute();
        }

        public string CanPlace(FacilityDef d, int x, int y, int rot)
        {
            int f = S.floor;
            if (!IsUnlocked(d.id)) return "Not researched yet";
            if (d.room == RoomType.Outdoor && f > 0) return "Outdoor equipment goes on the ground floor";
            Footprint(d, rot, out int w, out int h);
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    if (!S.InBounds(xx, yy) || !S.Owned(xx, yy)) return "Must be on land you own";
                    int i = S.Idx(f, xx, yy);
                    if (f == 0 && S.obstacles[yy * S.mapW + xx] != 0) return "Clear the trees/rocks first";
                    if (occupancy[i] != 0) return "Tile is occupied";
                    var room = (RoomType)S.rooms[i];
                    if (d.room == RoomType.Outdoor) { if (room != RoomType.None) return "Must be placed outdoors (not on the building)"; }
                    else if (room != d.room) return $"Must be placed in a {Catalog.RoomName(d.room)}";
                }
            if (S.money < d.cost) return "Not enough money";
            return null;
        }

        public bool CanPlaceAnywhere(FacilityDef d)
        {
            for (int y = S.ownY0; y < S.ownY1; y++)
                for (int x = S.ownX0; x < S.ownX1; x++)
                    if (CanPlace(d, x, y, 0) == null) return true;
            return false;
        }

        public PlacedObject Place(FacilityDef d, int x, int y, int rot)
        {
            if (CanPlace(d, x, y, rot) != null) return null;
            Checkpoint();
            Spend(d.cost);
            var o = new PlacedObject { id = S.NewId(), def = d.id, x = x, y = y, rot = rot, placedOrder = ++placeCounter, floor = S.floor };
            S.objects.Add(o);
            objById[o.id] = o;
            MarkOccupancy(o, o.id);
            objectsVersion++;
            Recompute();
            return o;
        }

        public float RemovalRefund(PlacedObject o)
        {
            float v = Catalog.Facility(o.def).cost * 0.5f;
            foreach (var d in o.devices) v += ResaleValue(d);
            if (o.dualPdu) v += DualPduCost * 0.5f;
            return v;
        }

        public void RemoveObject(PlacedObject o)
        {
            Checkpoint();
            Refund(RemovalRefund(o));
            MarkOccupancy(o, 0);
            S.objects.Remove(o);
            objById.Remove(o.id);
            foreach (var r in Racks.ToList())
            {
                if (r.linkCore == o.id) { r.linkCore = -1; MarkDirty(r); }
                if (r.linkCore2 == o.id) { r.linkCore2 = -1; MarkDirty(r); }
            }
            foreach (var m in S.staff) if (m.taskObj == o.id) { m.taskObj = -1; m.state = "returning"; m.path = null; }
            objectsVersion++;
            Recompute();
        }

        // =====================================================================
        //  Player actions: racks
        // =====================================================================
        public int FindSlot(PlacedObject rack, int heightU)
        {
            var d = Catalog.Facility(rack.def);
            var used = new bool[d.rackU];
            foreach (var dev in rack.devices)
            {
                var dd = Catalog.Device(dev.def);
                for (int u = dev.u; u < dev.u + dd.heightU && u < used.Length; u++) used[u] = true;
            }
            for (int u = 0; u + heightU <= d.rackU; u++)
            {
                bool ok = true;
                for (int k = u; k < u + heightU; k++) if (used[k]) { ok = false; break; }
                if (ok) return u;
            }
            return -1;
        }

        public string CanAddDevice(PlacedObject rack, DeviceDef d)
        {
            if (!IsUnlocked(d.id)) return "Not researched";
            var c = rack.contractId > 0 ? FindContract(rack.contractId) : null;
            if (c != null && c.type == ContractType.Colocation && d.kind == DeviceKind.Server) return "Colocation customers bring their own servers";
            if (FindSlot(rack, d.heightU) < 0) return "Not enough free rack units";
            if (S.money < DeviceCost(d)) return "Not enough money";
            return null;
        }

        public RackDevice AddDevice(PlacedObject rack, DeviceDef d)
        {
            if (CanAddDevice(rack, d) != null) return null;
            Checkpoint();
            int u = FindSlot(rack, d.heightU);
            Spend(DeviceCost(d));
            var dev = new RackDevice { id = S.NewId(), def = d.id, u = u };
            rack.devices.Add(dev);
            MarkDirty(rack);
            Recompute();
            return dev;
        }

        public int FillRack(PlacedObject rack, DeviceDef d, int max)
        {
            int n = 0;
            while (n < max && CanAddDevice(rack, d) == null) { AddDevice(rack, d); n++; }
            return n;
        }

        public void RemoveDevice(PlacedObject rack, RackDevice dev)
        {
            Checkpoint();
            if (!rack.devices.Remove(dev)) return;
            Refund(ResaleValue(dev));
            MarkDirty(rack);
            Recompute();
        }

        public const int MaxLooseCable = 14;

        public float CableCost(PlacedObject rack, PlacedObject core)
        {
            var route = CableRouteFor(rack, core);
            return Catalog.CableBaseCost + route.dist * Catalog.CablePerTile * (route.routed ? 0.5f : 1f);
        }

        public string CableError(PlacedObject rack, PlacedObject core) => CableRouteFor(rack, core).error;

        public int FreePorts(PlacedObject core)
        {
            var d = Catalog.Facility(core.def);
            return d.ports - (coreUsage.TryGetValue(core.id, out int u) ? u : 0);
        }

        public string Connect(PlacedObject rack, PlacedObject core)
        {
            if (rack.linkCore == core.id) return "Already connected";
            if (FreePorts(core) <= 0) return "Core switch has no free ports";
            var ce = CableError(rack, core);
            if (ce != null) return ce;
            Checkpoint();
            float cost = CableCost(rack, core);
            if (!Spend(cost)) return "Not enough money";
            rack.linkCore = core.id;
            MarkDirty(rack);
            objectsVersion++;
            Recompute();
            return null;
        }

        public PlacedObject BestCore(PlacedObject rack)
        {
            PlacedObject best = null; float bestScore = float.MaxValue;
            foreach (var c in ObjectsOfKind(FacilityKind.CoreSwitch))
            {
                if (c.failed || FreePorts(c) <= 0 || S.RoomAt(c.floor, c.x, c.y) != RoomType.Network || CableError(rack, c) != null) continue;
                float score = CableCost(rack, c) - Catalog.Facility(c.def).portGbps * 50f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        public string AutoConnect(PlacedObject rack)
        {
            var c = BestCore(rack);
            if (c == null) return "No reachable core switch with free ports in a Network Room (long runs need cable trays)";
            return Connect(rack, c);
        }

        public void Disconnect(PlacedObject rack)
        {
            rack.linkCore = -1;
            MarkDirty(rack);
            objectsVersion++;
            Recompute();
        }

        public string Assign(PlacedObject rack, Contract c)
        {
            Checkpoint();
            if (c == null) { rack.contractId = -1; MarkDirty(rack); Recompute(); return null; }
            if (c.status != ContractStatus.Setup && c.status != ContractStatus.Active) return "Contract is not active";
            if (c.type == ContractType.Colocation && rack.devices.Any(d => Catalog.Device(d.def).kind == DeviceKind.Server))
                return "Colocation racks must not contain your servers (switches only)";
            rack.contractId = c.id;
            MarkDirty(rack);
            Recompute();
            return null;
        }

        // =====================================================================
        //  Player actions: contracts, research, staff, land
        // =====================================================================
        public void AcceptOffer(Contract c)
        {
            if (!S.offers.Remove(c)) return;
            c.status = ContractStatus.Setup;
            c.deadlineHour = S.hour + c.setupDays * Catalog.HoursPerDay;
            S.contracts.Add(c);
            Log($"Signed {c.customer}: {ProcGen.Describe(c)}. Setup deadline in {c.setupDays} days.", 1);
            Recompute();
        }

        public void DeclineOffer(Contract c)
        {
            S.offers.Remove(c);
        }

        public float CancelPenalty(Contract c) => c.status == ContractStatus.Setup ? Mathf.Round(c.monthlyPay * 0.5f) : Mathf.Round(c.monthlyPay * 1.5f);

        public void CancelContract(Contract c)
        {
            float pen = CancelPenalty(c);
            S.money -= pen;
            S.current.penalties += pen;
            S.rating = Mathf.Max(0, S.rating - 6);
            S.contractsFailed++;
            EndContract(c, ContractStatus.Cancelled);
            Log($"You cancelled the contract with {c.customer}. ${pen:N0} early termination fee.", 3);
        }

        public string Research(string id)
        {
            if (IsUnlocked(id)) return "Already researched";
            int lvl; float cost; string name;
            var d = Catalog.Device(id);
            var f = Catalog.Facility(id);
            if (d != null) { lvl = d.level; cost = d.research; name = d.name; }
            else if (f != null) { lvl = f.level; cost = f.research; name = f.name; }
            else return "Unknown";
            if (S.Level < lvl) return $"Requires reputation level {lvl}";
            if (!Spend(cost, false)) return "Not enough money";
            S.unlocked.Add(id);
            Log($"Research complete: {name}", 1);
            return null;
        }

        string LegacyHire()
        {
            if (S.technicians >= deskCount) return "Build more technician desks in an Office first";
            if (!Spend(1000, false)) return "Not enough money";
            S.technicians++;
            Log("Hired a technician.", 1);
            return null;
        }

        void LegacyFire()
        {
            if (S.technicians > 0) { S.technicians--; Log("Let a technician go.", 0); }
        }

        static readonly Vector2Int[] Dirs = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };
        public static readonly string[] DirNames = { "North", "South", "East", "West" };

        public bool CanExpand(int dir)
        {
            switch (dir)
            {
                case 0: return S.ownY1 < S.mapH;
                case 1: return S.ownY0 > 0;
                case 2: return S.ownX1 < S.mapW;
                default: return S.ownX0 > 0;
            }
        }

        public float LandCost(int dir)
        {
            int len = dir < 2 ? (S.ownX1 - S.ownX0) : (S.ownY1 - S.ownY0);
            return Mathf.Round(len * 4 * 90f * S.city.landPrice * (1f + S.landPurchases * 0.2f) / 100f) * 100f;
        }

        public string BuyLand(int dir)
        {
            if (!CanExpand(dir)) return "Reached the edge of the map";
            float cost = LandCost(dir);
            if (!Spend(cost)) return "Not enough money";
            switch (dir)
            {
                case 0: S.ownY1 = Mathf.Min(S.mapH, S.ownY1 + 4); break;
                case 1: S.ownY0 = Mathf.Max(0, S.ownY0 - 4); break;
                case 2: S.ownX1 = Mathf.Min(S.mapW, S.ownX1 + 4); break;
                default: S.ownX0 = Mathf.Max(0, S.ownX0 - 4); break;
            }
            S.landPurchases++;
            Log($"Purchased land to the {DirNames[dir].ToLower()} for ${cost:N0}.", 1);
            TilesChanged();
            return null;
        }
    }
}
