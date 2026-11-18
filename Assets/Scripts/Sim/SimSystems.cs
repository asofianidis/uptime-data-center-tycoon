using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// v2 gameplay systems: airflow, redundancy, energy, staff &amp; walking technicians, tickets,
    /// satisfaction, rivals, negotiation, aging, weather, extra events, scenarios, achievements, undo hooks.
    /// </summary>
    public partial class Simulation
    {
        // ================================================================ undo hook
        public event Action BeforeChange { add => company.BeforeChange += value; remove => company.BeforeChange -= value; }
        public bool suppressCheckpoints;
        void Checkpoint() { if (!suppressCheckpoints) company.RaiseBeforeChange(); }

        // ================================================================ airflow (hot/cold aisles)
        public static Vector2Int FrontDir(int rot)
        {
            switch (rot & 3)
            {
                case 0: return new Vector2Int(0, -1);
                case 1: return new Vector2Int(-1, 0);
                case 2: return new Vector2Int(0, 1);
                default: return new Vector2Int(1, 0);
            }
        }

        bool IsRack(PlacedObject o) => o != null && Catalog.Facility(o.def)?.kind == FacilityKind.Rack;

        /// <summary>Heat multiplier for a rack based on what its intake faces.</summary>
        public float AirflowFactor(PlacedObject o, out string note)
        {
            var f = FrontDir(o.rot);
            var a1 = ObjectAtF(o.floor, o.x + f.x, o.y + f.y);
            var a2 = ObjectAtF(o.floor, o.x + 2 * f.x, o.y + 2 * f.y);
            // Another rack directly in front, facing the same way: its hot exhaust blows into our intake.
            if (IsRack(a1) && a1.rot == o.rot) { note = "Intake sucks in another rack's exhaust (+35% heat)"; return 1.35f; }
            if (a1 != null && !IsRack(a1)) { note = "Intake blocked by equipment (+15% heat)"; return 1.15f; }
            if (IsRack(a1)) { note = "No aisle in front of the rack (+20% heat)"; return 1.2f; }
            if (IsRack(a2) && a2.rot == o.rot) { note = "Faces the hot aisle of the next row (+25% heat)"; return 1.25f; }
            if (IsRack(a2) && ((a2.rot + 2) & 3) == (o.rot & 3)) { note = "Cold-aisle containment (-12% heat)"; return 0.88f; }
            note = "Open airflow";
            return 1f;
        }

        public void RotateRack(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            if (d.w != d.h) return;
            Checkpoint();
            o.rot = (o.rot + 1) & 3;
            MarkDirty(o);
            objectsVersion++;
            Recompute();
        }

        // ================================================================ redundancy
        bool CoreOk(int id, out FacilityDef def, out string issue)
        {
            def = null; issue = null;
            if (id <= 0 || !objById.TryGetValue(id, out var core)) { issue = "Not cabled to a core switch"; return false; }
            def = Catalog.Facility(core.def);
            if (def == null) { issue = "Not cabled to a core switch"; return false; }
            if (core.failed) { issue = "Core switch failed"; return false; }
            if (S.RoomAt(core.floor, core.x, core.y) != RoomType.Network) { issue = "Core switch outside network room"; return false; }
            return true;
        }

        public string RedundancyGap(PlacedObject o)
        {
            if (!racks.TryGetValue(o.id, out var info)) return "Unknown";
            var gaps = new List<string>();
            if (!o.dualPdu) gaps.Add("dual PDU");
            if (o.devices.Count(d => !d.failed && Catalog.Device(d.def)?.kind == DeviceKind.Switch) < 2) gaps.Add("2 ToR switches");
            if (!(CoreOk(o.linkCore, out _, out _) && CoreOk(o.linkCore2, out _, out _) && o.linkCore != o.linkCore2)) gaps.Add("uplinks to 2 different core switches");
            return gaps.Count == 0 ? null : "Missing " + string.Join(", ", gaps);
        }

        public const float DualPduCost = 675f;

        public string BuyDualPdu(PlacedObject rack)
        {
            if (rack.dualPdu) return "Already installed";
            Checkpoint();
            if (!Spend(DualPduCost)) return "Not enough money";
            rack.dualPdu = true;
            MarkDirty(rack);
            Recompute();
            return null;
        }

        public string ConnectSecondary(PlacedObject rack, PlacedObject core)
        {
            if (rack.linkCore == core.id) return "Second uplink must go to a different core switch";
            if (rack.linkCore2 == core.id) return "Already connected";
            if (FreePorts(core) <= 0) return "Core switch has no free ports";
            var ce = CableError(rack, core);
            if (ce != null) return ce;
            Checkpoint();
            if (!Spend(CableCost(rack, core))) return "Not enough money";
            rack.linkCore2 = core.id;
            MarkDirty(rack);
            objectsVersion++;
            Recompute();
            return null;
        }

        public string AutoConnectSecondary(PlacedObject rack)
        {
            PlacedObject best = null; float bestScore = float.MaxValue;
            foreach (var c in ObjectsOfKind(FacilityKind.CoreSwitch))
            {
                if (c.id == rack.linkCore || c.failed || FreePorts(c) <= 0 || S.RoomAt(c.floor, c.x, c.y) != RoomType.Network || CableError(rack, c) != null) continue;
                float score = CableCost(rack, c) - Catalog.Facility(c.def).portGbps * 50f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            if (best == null) return "Need a second reachable core switch with free ports (long runs need cable trays)";
            return ConnectSecondary(rack, best);
        }

        public void DisconnectSecondary(PlacedObject rack)
        {
            Checkpoint();
            rack.linkCore2 = -1;
            MarkDirty(rack);
            objectsVersion++;
            Recompute();
        }

        /// <summary>Make a rack fully redundant: dual PDU, second ToR, second uplink.</summary>
        public string MakeRedundant(PlacedObject rack)
        {
            if (!rack.dualPdu) { var e = BuyDualPdu(rack); if (e != null) return e; }
            int sw = rack.devices.Count(d => Catalog.Device(d.def)?.kind == DeviceKind.Switch);
            if (sw < 2)
            {
                var existing = rack.devices.Select(d => Catalog.Device(d.def)).FirstOrDefault(d => d.kind == DeviceKind.Switch);
                var swDef = existing ?? Catalog.Devices.Where(d => d.kind == DeviceKind.Switch && IsUnlocked(d.id)).OrderBy(d => d.cost).First();
                for (int i = sw; i < 2; i++) if (AddDevice(rack, swDef) == null) return "Could not add a second ToR switch: " + CanAddDevice(rack, swDef);
            }
            if (rack.linkCore < 0) { var e = AutoConnect(rack); if (e != null) return e; }
            if (rack.linkCore2 < 0) { var e = AutoConnectSecondary(rack); if (e != null) return e; }
            return null;
        }

        // ================================================================ energy
        public float solarCapKW, solarKW, gridKW;

        public static float SunCurve(int hourOfDay) => hourOfDay < 6 || hourOfDay > 18 ? 0f : Mathf.Sin(Mathf.PI * (hourOfDay - 6) / 12f);

        public float WeatherSolar
        {
            get
            {
                switch (S.weather)
                {
                    case "Cloudy": return 0.45f;
                    case "Rain": return 0.2f;
                    case "Storm": return 0.1f;
                    case "Snow": return 0.15f;
                    default: return 1f;
                }
            }
        }

        public float TimeOfDayPriceMul
        {
            get
            {
                int h = S.hour % 24;
                if (h < 6) return 0.7f;
                if (h >= 17 && h < 21) return 1.35f;
                return 1f;
            }
        }

        public float PowerPriceNow => S.city.powerPrice * EventMag("powerspike", 1f) * TimeOfDayPriceMul;

        void ComputeSolar()
        {
            solarCapKW = 0;
            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d != null && d.kind == FacilityKind.Solar && !o.failed) solarCapKW += d.supplyKW;
            }
            solarKW = solarCapKW * SunCurve(S.hour % 24) * WeatherSolar * S.city.solarFactor;
        }

        // ================================================================ battery storage
        public float batteryKW, batteryCapKWh, batteryChargeKWh, batteryChargeKW, batteryDischargeKW;

        void BatteryHour()
        {
            batteryChargeKW = batteryDischargeKW = 0;
            float need = Mathf.Max(0, loadKW - solarKW);
            foreach (var o in S.objects)
            {
                var d = Catalog.Facility(o.def);
                if (d == null || d.kind != FacilityKind.Battery || o.failed) continue;
                if (outageActive)
                {
                    float use = Mathf.Min(d.supplyKW, o.charge);
                    o.charge -= use;
                }
                else if (TimeOfDayPriceMul < 1f)
                {
                    float c = Mathf.Min(d.supplyKW, d.capacityKWh - o.charge);
                    if (c > 0) { o.charge += c * 0.92f; batteryChargeKW += c; }
                }
                else if (TimeOfDayPriceMul > 1f)
                {
                    float dis = Mathf.Min(d.supplyKW, o.charge, Mathf.Max(0, need - batteryDischargeKW));
                    if (dis > 0) { o.charge -= dis; batteryDischargeKW += dis; }
                }
                o.charge = Mathf.Clamp(o.charge, 0, d.capacityKWh);
            }
        }

        // ================================================================ cable trays & raised floors
        public const float TrayCost = 30f, RaisedCost = 50f;
        int[] trayComp;

        void RebuildTrayComponents()
        {
            trayComp = new int[S.mapW * S.mapH * GameState.MaxFloors];
            if (S.flags == null) return;
            int next = 1;
            var stack = new Stack<int>();
            for (int i = 0; i < trayComp.Length; i++)
            {
                if (trayComp[i] != 0 || (S.flags[i] & GameState.FlagTray) == 0) continue;
                trayComp[i] = next;
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
                        if (trayComp[n] != 0 || (S.flags[n] & GameState.FlagTray) == 0) continue;
                        trayComp[n] = next;
                        stack.Push(n);
                    }
                }
                next++;
            }
        }

        HashSet<int> TrayCompsNear(PlacedObject o)
        {
            var set = new HashSet<int>();
            if (trayComp == null) return set;
            var d = Catalog.Facility(o.def);
            Footprint(d, o.rot, out int w, out int h);
            for (int y = o.y - 1; y <= o.y + h; y++)
                for (int x = o.x - 1; x <= o.x + w; x++)
                    if (S.InBounds(x, y) && trayComp[S.Idx(o.floor, x, y)] != 0) set.Add(trayComp[S.Idx(o.floor, x, y)]);
            return set;
        }

        /// <summary>True when a continuous run of cable tray connects the rack to the core switch.</summary>
        public bool TrayRouted(PlacedObject rack, int coreId)
        {
            if (!objById.TryGetValue(coreId, out var core)) return false;
            return CableRouteFor(rack, core).routed;
        }

        bool TrayLinked(PlacedObject a, PlacedObject b)
        {
            if (a.floor != b.floor) return false;
            var ca = TrayCompsNear(a);
            return ca.Count > 0 && TrayCompsNear(b).Overlaps(ca);
        }

        public struct CableRoute
        {
            public int dist;
            public bool routed;
            public string error;
            public PlacedObject shaftRackFloor, shaftCoreFloor;
        }

        /// <summary>
        /// Route for a cable from rack to core. Same floor: direct (tray or loose). Different floors: via an
        /// Elevator &amp; Cable Riser column that exists on every floor in between.
        /// </summary>
        public CableRoute CableRouteFor(PlacedObject rack, PlacedObject core)
        {
            var r = new CableRoute();
            if (rack.floor == core.floor)
            {
                r.dist = Mathf.Abs(rack.x - core.x) + Mathf.Abs(rack.y - core.y);
                r.routed = TrayLinked(rack, core);
                if (r.dist > MaxLooseCable && !r.routed)
                    r.error = $"{r.dist} tiles is too far for loose cabling (max {MaxLooseCable}). Build cable trays linking the rack to the network room.";
                return r;
            }
            int lo = Mathf.Min(rack.floor, core.floor), hi = Mathf.Max(rack.floor, core.floor);
            int best = int.MaxValue;
            foreach (var e in S.objects)
            {
                if (e.floor != rack.floor || Catalog.Facility(e.def).kind != FacilityKind.Elevator) continue;
                bool column = true;
                for (int f = lo; f <= hi && column; f++) column = IsElevator(f, e.x, e.y);
                if (!column) continue;
                var top = ObjectAtF(core.floor, e.x, e.y);
                int d1 = Mathf.Abs(rack.x - e.x) + Mathf.Abs(rack.y - e.y);
                int d2 = Mathf.Abs(core.x - e.x) + Mathf.Abs(core.y - e.y);
                bool r1 = TrayLinked(rack, e), r2 = TrayLinked(top, core);
                string err = null;
                if (d1 > MaxLooseCable && !r1) err = $"Rack is {d1} tiles from the riser - add cable trays (max {MaxLooseCable} loose)";
                else if (d2 > MaxLooseCable && !r2) err = $"Core switch is {d2} tiles from the riser - add cable trays (max {MaxLooseCable} loose)";
                int dist = d1 + d2 + 3 * (hi - lo);
                if (err == null && dist < best)
                {
                    best = dist;
                    r.dist = dist; r.routed = r1 && r2; r.error = null;
                    r.shaftRackFloor = e; r.shaftCoreFloor = top;
                }
                else if (best == int.MaxValue && r.error == null) r.error = err;
            }
            if (best == int.MaxValue)
            {
                r.dist = Mathf.Abs(rack.x - core.x) + Mathf.Abs(rack.y - core.y) + 3 * (hi - lo);
                r.error ??= $"Cross-floor cabling needs an Elevator & Cable Riser stacked on floors {lo + 1}-{hi + 1}.";
            }
            return r;
        }

        /// <summary>Tile path along trays from near the rack to near the core (for drawing cables).</summary>
        public List<Vector2Int> TrayPath(PlacedObject rack, PlacedObject core)
        {
            if (trayComp == null) return null;
            if (rack.floor != core.floor)
            {
                var route = CableRouteFor(rack, core);
                return route.shaftRackFloor != null && TrayLinked(rack, route.shaftRackFloor) ? TrayPath(rack, route.shaftRackFloor) : null;
            }
            var shared = TrayCompsNear(rack);
            shared.IntersectWith(TrayCompsNear(core));
            if (shared.Count == 0) return null;
            int comp = shared.First();
            List<int> Near(PlacedObject o)
            {
                var d = Catalog.Facility(o.def);
                Footprint(d, o.rot, out int w, out int h);
                var l = new List<int>();
                for (int y = o.y - 1; y <= o.y + h; y++)
                    for (int x = o.x - 1; x <= o.x + w; x++)
                        if (S.InBounds(x, y) && trayComp[S.Idx(o.floor, x, y)] == comp) l.Add(S.Idx(o.floor, x, y));
                return l;
            }
            var starts = Near(rack); var goals = new HashSet<int>(Near(core));
            var prev = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var st in starts) { prev[st] = -1; q.Enqueue(st); }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                if (goals.Contains(c))
                {
                    var path = new List<Vector2Int>();
                    for (int p = c; p != -1; p = prev[p]) path.Add(new Vector2Int((p % S.Plane) % S.mapW, (p % S.Plane) / S.mapW));
                    path.Reverse();
                    return path;
                }
                int cfl = c / S.Plane, crem = c % S.Plane;
                int cx = crem % S.mapW, cy = crem / S.mapW;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!S.InBounds(nx, ny)) continue;
                    int n = S.Idx(cfl, nx, ny);
                    if (prev.ContainsKey(n) || trayComp[n] != comp) continue;
                    prev[n] = c;
                    q.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>Paints cable trays (any building tile) or raised floor (server halls only).</summary>
        public int PaintFlag(RectInt r, int flag, bool apply, out float cost)
        {
            var tiles = new List<int>();
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (!S.InBounds(x, y) || S.HasFlag(x, y, flag)) continue;
                    var room = S.Room(x, y);
                    if (flag == GameState.FlagRaised && room != RoomType.ServerHall) continue;
                    if (flag == GameState.FlagTray && room == RoomType.None && (S.floor > 0 || !S.Owned(x, y))) continue;
                    tiles.Add(S.Idx(S.floor, x, y));
                }
            cost = tiles.Count * (flag == GameState.FlagTray ? TrayCost : RaisedCost);
            if (!apply || tiles.Count == 0) return tiles.Count;
            Checkpoint();
            if (!Spend(cost)) { Log("Not enough money.", 2); return 0; }
            foreach (var t in tiles) S.flags[t] |= flag;
            zonesDirty = true;
            tilesVersion++;
            objectsVersion++;
            Recompute();
            return tiles.Count;
        }

        // ================================================================ hardware prices & aging
        public float DeviceCost(DeviceDef d)
        {
            float m = EventMag("shortage", 1f);
            if (d.gpus > 0) m *= EventMag("gpushortage", 1f);
            return Mathf.Round(d.cost * m);
        }

        public const int HoursPerYear = Catalog.HoursPerMonth * 12;

        public static float AgeYears(RackDevice d) => d.ageHours / (float)HoursPerYear;

        public static float AgeFailureFactor(RackDevice d)
        {
            float y = AgeYears(d);
            return y < 1.5f ? 1f : 1f + (y - 1.5f) * (y - 1.5f) * 1.6f;
        }

        public float ResaleValue(RackDevice d)
        {
            var dd = Catalog.Device(d.def);
            if (dd == null) return 0;
            float f = Mathf.Lerp(0.5f, 0.08f, Mathf.Clamp01(AgeYears(d) / 4f));
            return Mathf.Round(dd.cost * f);
        }

        void AgeHardware()
        {
            foreach (var o in S.objects)
                if (o.devices.Count > 0 && racks.TryGetValue(o.id, out var info) && info.drawing)
                    foreach (var d in o.devices) if (!d.failed) d.ageHours++;
        }

        /// <summary>Replace a device with a fresh unit of the same model (sells the old one).</summary>
        public string RefreshDevice(PlacedObject rack, RackDevice dev)
        {
            var dd = Catalog.Device(dev.def);
            float cost = DeviceCost(dd) - ResaleValue(dev);
            Checkpoint();
            if (!Spend(cost)) return "Not enough money";
            dev.ageHours = 0; dev.failed = false;
            MarkDirty(rack);
            Recompute();
            return null;
        }

        // ================================================================ weather
        static readonly string[] Weathers = { "Clear", "Cloudy", "Rain", "Storm" };

        void DailyWeather()
        {
            if (EventActive("heatwave")) { S.weather = "Heat"; return; }
            string climate = S.city.climate;
            float r = (float)rng.NextDouble();
            if (climate == "Desert") S.weather = r < 0.85f ? "Clear" : r < 0.97f ? "Cloudy" : "Storm";
            else if (climate == "Arctic") S.weather = r < 0.35f ? "Clear" : r < 0.7f ? "Cloudy" : r < 0.93f ? "Snow" : "Storm";
            else if (climate == "Cold") S.weather = r < 0.4f ? "Clear" : r < 0.7f ? "Cloudy" : r < 0.93f ? "Rain" : "Storm";
            else S.weather = r < 0.5f ? "Clear" : r < 0.75f ? "Cloudy" : r < 0.94f ? "Rain" : "Storm";
            if (S.weather == "Storm" && rng.NextDouble() < 0.35 * S.city.disasterRate && !EventActive("outage"))
            {
                Log("A thunderstorm is battering the grid.", 2);
                TriggerEvent("outage");
            }
        }

        // ================================================================ staff
        public static readonly string[] Roles = { "Technician", "Electrician", "NetEngineer", "Security" };

        public static string RoleName(string r) => r == "NetEngineer" ? "Network Engineer" : r;

        public static string RoleDesc(string r)
        {
            switch (r)
            {
                case "Technician": return "Replaces failed servers and switches and handles customer tickets.";
                case "Electrician": return "Fixes power and cooling equipment and tripped PDUs, 50% faster.";
                case "NetEngineer": return "Fixes core switches, ISP links and ToR switches; resolves tickets 50% faster.";
                case "Security": return "Doesn't repair. Each point of skill helps stop DDoS attacks and break-ins.";
            }
            return "";
        }

        static readonly string[] FirstNames = { "Alex", "Sam", "Jordan", "Riley", "Casey", "Morgan", "Taylor", "Jamie", "Avery", "Quinn", "Drew", "Rowan", "Skyler", "Hayden", "Emerson", "Kai", "Noor", "Mika", "Ari", "Devon", "Sasha", "Remy", "Lee", "Robin" };
        static readonly string[] LastNames = { "Chen", "Patel", "Garcia", "Okafor", "Smith", "Kowalski", "Nakamura", "Silva", "Novak", "Haddad", "Larsen", "Murphy", "Ivanova", "Mendes", "Kim", "Osei", "Reyes", "Fischer", "Rossi", "Singh" };

        public static float BaseSalary(string role)
        {
            switch (role)
            {
                case "Electrician": return 3800;
                case "NetEngineer": return 4300;
                case "Security": return 2800;
                default: return 3100;
            }
        }

        public void RefreshCandidates()
        {
            S.candidates.Clear();
            for (int i = 0; i < 5; i++)
            {
                string role = i < Roles.Length && rng.NextDouble() < 0.7 ? Roles[i] : Roles[rng.Next(Roles.Length)];
                int skill = Mathf.Clamp(1 + (int)(rng.NextDouble() * 3.2 + S.Level * 0.15), 1, 5);
                S.candidates.Add(new Candidate
                {
                    name = ProcGen.Pick(rng, FirstNames) + " " + ProcGen.Pick(rng, LastNames),
                    role = role,
                    skill = skill,
                    salary = Mathf.Round(BaseSalary(role) * (0.75f + 0.15f * skill) * ProcGen.Range(rng, 0.9f, 1.1f) / 50f) * 50f,
                });
            }
        }

        public float HireFee(Candidate c) => Mathf.Round(c.salary * 0.35f / 50f) * 50f;

        public bool OnShift(StaffMember m)
        {
            int h = S.hour % 24;
            bool day = h >= 7 && h < 19;
            return (m.shift == "Night") ? !day : day;
        }

        /// <summary>Desks are shared between shifts: each shift can seat up to the desk count.</summary>
        public int ShiftRank(StaffMember m)
        {
            int rank = 0;
            foreach (var o in S.staff) { if (o == m) break; if ((o.shift ?? "Day") == (m.shift ?? "Day")) rank++; }
            return rank;
        }

        public bool HasDesk(StaffMember m) => ShiftRank(m) < deskCount;
        public int ShiftCount(string shift) => S.staff.Count(m => (m.shift ?? "Day") == shift);
        public int WorkingStaffCount => S.staff.Count(m => OnShift(m) && HasDesk(m));
        public int EffectiveTechs => S.staff.Count(m => OnShift(m) && HasDesk(m) && m.role != "Security");
        public int SecurityLevel => S.staff.Where(m => OnShift(m) && HasDesk(m) && m.role == "Security").Sum(m => m.skill);

        public void SetShift(StaffMember m, string shift)
        {
            if (m.shift == shift) return;
            m.shift = shift;
            m.salary = Mathf.Round(m.salary * (shift == "Night" ? 1.15f : 1f / 1.15f) / 50f) * 50f;
            Log($"{m.name} moved to the {shift.ToLower()} shift.", 0);
        }

        public string HireCandidate(Candidate c) => HireCandidate(c, ShiftCount("Night") < ShiftCount("Day") ? "Night" : "Day");

        public string HireCandidate(Candidate c, string shift)
        {
            if (ShiftCount(shift) >= deskCount) return $"All desks are taken on the {shift.ToLower()} shift - build more desks";
            float fee = HireFee(c);
            if (!Spend(fee, false)) return "Not enough money";
            var home = DeskPosition(ShiftCount(shift));
            float salary = shift == "Night" ? Mathf.Round(c.salary * 1.15f / 50f) * 50f : c.salary;
            S.staff.Add(new StaffMember { id = S.NewId(), name = c.name, role = c.role, skill = c.skill, salary = salary, shift = shift, x = home.x, y = home.y, px = home.x, py = home.y, hiredHour = S.hour, state = OnShift(new StaffMember { shift = shift }) ? "idle" : "off" });
            S.candidates.Remove(c);
            Log($"Hired {c.name} ({RoleName(c.role)}, skill {c.skill}, {shift.ToLower()} shift).", 1);
            return null;
        }

        /// <summary>Quick hire of the best available technician (used by the auto manager).</summary>
        public string Hire()
        {
            if (S.candidates.Count == 0) RefreshCandidates();
            var c = S.candidates.Where(x => x.role != "Security").OrderByDescending(x => x.skill).FirstOrDefault() ?? S.candidates[0];
            string shift = ShiftCount("Night") < ShiftCount("Day") ? "Night" : "Day";
            if (ShiftCount(shift) >= deskCount) shift = shift == "Night" ? "Day" : "Night";
            return HireCandidate(c, shift);
        }

        public void FireStaff(StaffMember m)
        {
            if (!S.staff.Remove(m)) return;
            foreach (var t in S.tickets) if (t.assigned == m.id) t.assigned = -1;
            Log($"{m.name} has left the company.", 0);
        }

        public void Fire() { if (S.staff.Count > 0) FireStaff(S.staff[S.staff.Count - 1]); }

        public int DeskFloor(int index)
        {
            var desks = ObjectsOfKind(FacilityKind.Desk).OrderBy(d => d.id).ToList();
            return desks.Count == 0 ? 0 : desks[index % desks.Count].floor;
        }

        public Vector2 DeskPosition(int index)
        {
            var desks = ObjectsOfKind(FacilityKind.Desk).OrderBy(d => d.id).ToList();
            if (desks.Count == 0) return new Vector2((S.ownX0 + S.ownX1) * 0.5f, S.ownY0 + 0.5f);
            var d = desks[index % desks.Count];
            var f = FrontDir(d.rot);
            return new Vector2(d.x + 0.5f + f.x * 0.45f, d.y + 0.5f + f.y * 0.45f);
        }

        bool looseWalk;

        bool IsElevator(int f, int x, int y)
        {
            var o = ObjectAtF(f, x, y);
            return o != null && Catalog.Facility(o.def).kind == FacilityKind.Elevator;
        }

        bool Walkable(int f, int x, int y)
        {
            if (!S.InBounds(x, y) || f < 0 || f >= GameState.MaxFloors) return false;
            int i = S.Idx(f, x, y);
            if (occupancy[i] != 0 && !looseWalk && !IsElevator(f, x, y)) return false;
            if (f == 0) return S.obstacles[y * S.mapW + x] == 0 && (S.rooms[i] != 0 || S.Owned(x, y));
            return S.rooms[i] != 0;
        }

        IEnumerable<int> Neighbors(int c)
        {
            int f = c / S.Plane, rem = c % S.Plane, cx = rem % S.mapW, cy = rem / S.mapW;
            if (cx + 1 < S.mapW) yield return S.Idx(f, cx + 1, cy);
            if (cx > 0) yield return S.Idx(f, cx - 1, cy);
            if (cy + 1 < S.mapH) yield return S.Idx(f, cx, cy + 1);
            if (cy > 0) yield return S.Idx(f, cx, cy - 1);
            if (IsElevator(f, cx, cy))
            {
                if (f + 1 < GameState.MaxFloors && IsElevator(f + 1, cx, cy)) yield return S.Idx(f + 1, cx, cy);
                if (f > 0 && IsElevator(f - 1, cx, cy)) yield return S.Idx(f - 1, cx, cy);
            }
        }

        bool WalkableIdx(int i) { int f = i / S.Plane, rem = i % S.Plane; return Walkable(f, rem % S.mapW, rem / S.mapW); }

        List<int> Bfs(int start, HashSet<int> goals, int exactGoal = -1)
        {
            if (goals != null && goals.Contains(start)) return new List<int>();
            if (start == exactGoal) return new List<int>();
            var prev = new Dictionary<int, int> { [start] = -1 };
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int n in Neighbors(c))
                {
                    if (prev.ContainsKey(n)) continue;
                    bool isGoal = n == exactGoal || (goals != null && goals.Contains(n));
                    if (!isGoal && !WalkableIdx(n)) continue;
                    prev[n] = c;
                    if (isGoal)
                    {
                        var path = new List<int>();
                        for (int p = n; p != start; p = prev[p]) path.Add(p);
                        path.Reverse();
                        return path;
                    }
                    q.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>Strict path first; if the target is boxed in, squeeze past equipment (slower repair).</summary>
        List<int> FindPath(int sf, int sx, int sy, PlacedObject target, out bool squeezed)
        {
            squeezed = false;
            var p = FindPathInner(sf, sx, sy, target);
            if (p != null) return p;
            looseWalk = true;
            try { p = FindPathInner(sf, sx, sy, target); }
            finally { looseWalk = false; }
            squeezed = p != null;
            return p;
        }

        List<int> FindPathInner(int sf, int sx, int sy, PlacedObject target)
        {
            var d = Catalog.Facility(target.def);
            Footprint(d, target.rot, out int w, out int h);
            var goals = new HashSet<int>();
            for (int y = target.y - 1; y <= target.y + h; y++)
                for (int x = target.x - 1; x <= target.x + w; x++)
                {
                    bool inside = x >= target.x && x < target.x + w && y >= target.y && y < target.y + h;
                    bool diag = (x < target.x || x >= target.x + w) && (y < target.y || y >= target.y + h);
                    if (inside || diag) continue;
                    if (Walkable(target.floor, x, y)) goals.Add(S.Idx(target.floor, x, y));
                }
            if (goals.Count == 0 || !S.InBounds(sx, sy)) return null;
            return Bfs(S.Idx(sf, sx, sy), goals);
        }

        List<int> FindPathToPoint(int sf, int sx, int sy, int gf, int gx, int gy)
        {
            if (!S.InBounds(sx, sy) || !S.InBounds(gx, gy)) return null;
            return Bfs(S.Idx(sf, sx, sy), null, S.Idx(gf, gx, gy));
        }

        class Job
        {
            public PlacedObject obj;
            public RackDevice dev;
            public Ticket ticket;
            public string kind;   // device, facility, pdu, ticket
            public float work;
        }

        List<Job> OpenJobs()
        {
            var jobs = new List<Job>();
            var taken = new HashSet<string>(S.staff.Where(s => s.state != "idle").Select(s => $"{s.taskObj}:{s.taskDev}:{s.taskTicket}"));
            foreach (var o in S.objects)
            {
                if (o.failed && !taken.Contains($"{o.id}:-1:-1")) jobs.Add(new Job { obj = o, kind = "facility", work = 10 });
                if (o.pduTripped && !taken.Contains($"{o.id}:-2:-1")) jobs.Add(new Job { obj = o, kind = "pdu", work = 3 });
                foreach (var d in o.devices)
                    if (d.failed && !taken.Contains($"{o.id}:{d.id}:-1")) jobs.Add(new Job { obj = o, dev = d, kind = "device", work = 4 });
            }
            foreach (var t in S.tickets)
            {
                if (t.assigned >= 0) continue;
                if (!objById.TryGetValue(t.rackId, out var rack)) continue;
                jobs.Add(new Job { obj = rack, ticket = t, kind = "ticket", work = t.work });
            }
            return jobs;
        }

        static bool RoleMatches(string role, Job j)
        {
            switch (role)
            {
                case "Electrician":
                    if (j.kind == "pdu") return true;
                    if (j.kind == "facility")
                    {
                        var k = Catalog.Facility(j.obj.def).kind;
                        return k != FacilityKind.CoreSwitch && k != FacilityKind.Isp;
                    }
                    return false;
                case "NetEngineer":
                    if (j.kind == "ticket") return true;
                    if (j.kind == "facility") { var k = Catalog.Facility(j.obj.def).kind; return k == FacilityKind.CoreSwitch || k == FacilityKind.Isp; }
                    if (j.kind == "device") return Catalog.Device(j.dev.def)?.kind == DeviceKind.Switch;
                    return false;
                case "Technician":
                    return j.kind == "device" || j.kind == "ticket";
            }
            return false;
        }

        static int JobPriority(Job j)
        {
            switch (j.kind)
            {
                case "facility": return 0;
                case "pdu": return 1;
                case "device": return 2;
                default: return 3;
            }
        }

        public const float WalkTilesPerHour = 12f;

        /// <summary>Objects technicians couldn't path to this hour (boxed in by other equipment).</summary>
        public readonly HashSet<int> unreachable = new HashSet<int>();

        readonly Dictionary<int, bool> reachCache = new Dictionary<int, bool>();
        int reachVersion = -1, reachTiles = -1;

        public bool IsReachable(PlacedObject o)
        {
            if (reachVersion != objectsVersion || reachTiles != tilesVersion) { reachCache.Clear(); reachVersion = objectsVersion; reachTiles = tilesVersion; }
            if (reachCache.TryGetValue(o.id, out bool cached)) return cached;
            return reachCache[o.id] = IsReachableNow(o);
        }

        bool IsReachableNow(PlacedObject o)
        {
            var any = S.staff.FirstOrDefault();
            var start = any != null ? new Vector2(any.x, any.y) : DeskPosition(0);
            int sf = any != null ? any.floor : DeskFloor(0);
            return FindPathInner(sf, Mathf.FloorToInt(start.x), Mathf.FloorToInt(start.y), o) != null;
        }

        void StaffTick()
        {
            unreachable.Clear();
            // Migrate legacy saves that only tracked a technician count.
            if (S.technicians > 0 && S.staff.Count == 0)
            {
                for (int i = 0; i < S.technicians; i++)
                {
                    var home = DeskPosition(i);
                    S.staff.Add(new StaffMember { id = S.NewId(), name = ProcGen.Pick(rng, FirstNames) + " " + ProcGen.Pick(rng, LastNames), role = "Technician", skill = 2, salary = Catalog.TechSalary, x = home.x, y = home.y, px = home.x, py = home.y });
                }
                S.technicians = 0;
            }

            List<Job> jobs = null;
            for (int i = 0; i < S.staff.Count; i++)
            {
                var m = S.staff[i];
                if (string.IsNullOrEmpty(m.shift)) m.shift = "Day";
                m.px = m.x; m.py = m.y;
                int rank = ShiftRank(m);
                bool hasDesk = rank < deskCount;
                if (!OnShift(m) || !hasDesk)
                {
                    // Off shift (or no desk): go home, release any task, recover.
                    if (m.state != "off")
                    {
                        foreach (var t in S.tickets) if (t.assigned == m.id) t.assigned = -1;
                        m.taskObj = m.taskDev = m.taskTicket = -1;
                        m.path = null;
                        m.state = "off";
                    }
                    var h0 = DeskPosition(rank);
                    m.x = m.px = h0.x; m.y = m.py = h0.y;
                    m.floor = DeskFloor(rank);
                    m.fatigue = Mathf.Max(0, m.fatigue - 12f);
                    continue;
                }
                if (m.state == "off") m.state = "idle";
                m.fatigue = Mathf.Clamp(m.fatigue + (m.state == "working" ? 5f : m.state == "walking" || m.state == "returning" ? 2.5f : -3f), 0, 100);
                if (m.role == "Security") continue;

                if (m.state == "walking" || m.state == "working")
                {
                    if (!TaskStillValid(m)) { m.state = "returning"; m.path = null; }
                }

                if (m.state == "idle" || m.state == "returning")
                {
                    jobs ??= OpenJobs();
                    var ranked = jobs.Where(j => !(j.ticket != null && j.ticket.assigned >= 0) && !unreachable.Contains(j.obj.id))
                        .OrderBy(j => JobPriority(j) - (RoleMatches(m.role, j) ? 1 : 0))
                        .ThenBy(j => Mathf.Abs(j.obj.x - m.x) + Mathf.Abs(j.obj.y - m.y))
                        .Take(6).ToList();
                    foreach (var mine in ranked)
                    {
                        var path = FindPath(m.floor, Mathf.FloorToInt(m.x), Mathf.FloorToInt(m.y), mine.obj, out bool squeezed);
                        if (path == null) { unreachable.Add(mine.obj.id); continue; }
                        {
                            jobs.Remove(mine);
                            m.taskObj = mine.obj.id;
                            m.taskDev = mine.kind == "pdu" ? -2 : mine.dev != null ? mine.dev.id : -1;
                            m.taskTicket = mine.ticket != null ? mine.ticket.id : -1;
                            if (mine.ticket != null) mine.ticket.assigned = m.id;
                            m.work = squeezed ? -mine.work * 0.5f : 0;   // awkward access: 50% longer
                            m.path = path;
                            m.state = "walking";
                        }
                        break;
                    }
                    if (m.state == "returning")
                    {
                        var home = DeskPosition(rank);
                        if (m.path == null || m.path.Count == 0)
                            m.path = FindPathToPoint(m.floor, Mathf.FloorToInt(m.x), Mathf.FloorToInt(m.y), DeskFloor(rank), Mathf.FloorToInt(home.x), Mathf.FloorToInt(home.y)) ?? new List<int>();
                        Walk(m, WalkTilesPerHour);
                        if (m.path.Count == 0) { m.x = home.x; m.y = home.y; m.floor = DeskFloor(rank); m.state = "idle"; }
                        continue;
                    }
                }

                if (m.state == "walking")
                {
                    Walk(m, WalkTilesPerHour);
                    if (m.path.Count == 0) m.state = "working";
                    else continue;
                }

                if (m.state == "working")
                {
                    float rate = (0.7f + 0.2f * m.skill) * FatigueMul(m);
                    var job = CurrentJob(m);
                    if (job == null) { m.state = "returning"; continue; }
                    if (RoleMatches(m.role, job)) rate *= 1.5f;
                    m.work += rate;
                    if (m.work >= job.work) { CompleteJob(m, job); m.state = "returning"; m.path = null; }
                }
            }
        }

        public static float FatigueMul(StaffMember m) => m.fatigue <= 60f ? 1f : Mathf.Max(0.5f, 1f - (m.fatigue - 60f) / 80f);

        void Walk(StaffMember m, float tiles)
        {
            if (m.path == null) { m.path = new List<int>(); return; }
            looseWalk = true;
            while (tiles > 0 && m.path.Count > 0)
            {
                int t = m.path[0];
                int tf = t / S.Plane, trem = t % S.Plane;
                float tx = trem % S.mapW + 0.5f, ty = trem / S.mapW + 0.5f;
                if (tf != m.floor)
                {
                    // Elevator ride: costs time, no interpolation between floors.
                    m.floor = tf; m.x = m.px = tx; m.y = m.py = ty;
                    tiles -= 2f;
                    m.path.RemoveAt(0);
                    continue;
                }
                float dx = tx - m.x, dy = ty - m.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= tiles) { m.x = tx; m.y = ty; tiles -= dist; m.path.RemoveAt(0); }
                else { m.x += dx / dist * tiles; m.y += dy / dist * tiles; tiles = 0; }
            }
            looseWalk = false;
        }

        bool TaskStillValid(StaffMember m)
        {
            if (!objById.TryGetValue(m.taskObj, out var o)) return false;
            if (m.taskTicket >= 0) return S.tickets.Any(t => t.id == m.taskTicket);
            if (m.taskDev == -2) return o.pduTripped;
            if (m.taskDev >= 0) return o.devices.Any(d => d.id == m.taskDev && d.failed);
            return o.failed;
        }

        Job CurrentJob(StaffMember m)
        {
            if (!objById.TryGetValue(m.taskObj, out var o)) return null;
            if (m.taskTicket >= 0)
            {
                var t = S.tickets.FirstOrDefault(x => x.id == m.taskTicket);
                return t == null ? null : new Job { obj = o, ticket = t, kind = "ticket", work = t.work };
            }
            if (m.taskDev == -2) return o.pduTripped ? new Job { obj = o, kind = "pdu", work = 3 } : null;
            if (m.taskDev >= 0)
            {
                var d = o.devices.FirstOrDefault(x => x.id == m.taskDev);
                return d != null && d.failed ? new Job { obj = o, dev = d, kind = "device", work = 4 } : null;
            }
            return o.failed ? new Job { obj = o, kind = "facility", work = 10 } : null;
        }

        void CompleteJob(StaffMember m, Job j)
        {
            switch (j.kind)
            {
                case "facility":
                {
                    var d = Catalog.Facility(j.obj.def);
                    j.obj.failed = false;
                    Spend(d.cost * 0.05f, false);
                    Log($"{m.name} repaired {d.name} #{j.obj.id}.", 1);
                    break;
                }
                case "pdu":
                    j.obj.pduTripped = false;
                    Log($"{m.name} reset the tripped PDU in rack #{j.obj.id}.", 1);
                    break;
                case "device":
                {
                    var dd = Catalog.Device(j.dev.def);
                    j.dev.failed = false;
                    Spend(DeviceCost(dd) * 0.05f, false);
                    Log($"{m.name} replaced a failed {dd.name}.", 1);
                    break;
                }
                case "ticket":
                    CompleteTicket(j.ticket, false);
                    break;
            }
            MarkDirty(j.obj);
            m.taskObj = m.taskDev = m.taskTicket = -1;
            Recompute();
        }

        // ================================================================ tickets & satisfaction
        static readonly string[] TicketTexts =
        {
            "Reboot a hung server", "Replace a customer's failed disk", "Install a cross-connect", "Re-seat a flaky network cable",
            "Rack and stack customer gear", "Apply an urgent firmware update", "Investigate packet loss", "Escort a customer auditor",
            "Swap a dead power supply", "Label and trace cabling", "Restore a server from KVM", "Upgrade RAM in a server",
        };

        void DailyTickets()
        {
            foreach (var c in S.contracts.Where(x => x.status == ContractStatus.Active).ToList())
            {
                // Customers settle in for a few days before filing tickets.
                if (c.totalHours < 5 * Catalog.HoursPerDay) continue;
                if (rng.NextDouble() > 0.14) continue;
                var rackIds = company.AllRacks().Where(x => x.rack.contractId == c.id).Select(x => x.rack.id).ToList();
                if (rackIds.Count == 0) continue;
                var t = new Ticket
                {
                    id = S.NewId(),
                    contractId = c.id,
                    text = ProcGen.Pick(rng, TicketTexts),
                    createdHour = S.hour,
                    deadlineHour = S.hour + rng.Next(30, 72),
                    fee = Mathf.Round(ProcGen.Range(rng, 150, 700) * (1 + S.Level * 0.3f) * ProcGen.IncomeScale / 10f) * 10f,
                    work = rng.Next(1, 5),
                    rackId = rackIds[rng.Next(rackIds.Count)],
                };
                S.tickets.Add(t);
                Log($"New ticket from {c.customer}: {t.text} (rack #{t.rackId}).", 0);
            }
        }

        void TicketsHour()
        {
            foreach (var t in S.tickets.ToList())
            {
                var c = FindContract(t.contractId);
                if (c == null || c.status != ContractStatus.Active || !company.FindObject(t.rackId, out _, out _)) { S.tickets.Remove(t); continue; }
                if (S.hour >= t.deadlineHour)
                {
                    S.tickets.Remove(t);
                    c.satisfaction = Mathf.Max(0, c.satisfaction - 10);
                    S.ticketsMissed++;
                    foreach (var site in S.sites) foreach (var m in site.staff) if (m.taskTicket == t.id) { m.taskTicket = -1; m.state = "returning"; }
                    Log($"Missed a support ticket for {c.customer}: {t.text}. Satisfaction dropped.", 2);
                }
            }
        }

        public float ExpediteCost(Ticket t) => Mathf.Round(t.fee * 1.5f);

        public string ExpediteTicket(Ticket t)
        {
            if (!Spend(ExpediteCost(t), false)) return "Not enough money";
            CompleteTicket(t, true);
            return null;
        }

        void CompleteTicket(Ticket t, bool contractor)
        {
            if (!S.tickets.Remove(t)) return;
            var c = FindContract(t.contractId);
            if (c != null)
            {
                c.satisfaction = Mathf.Min(100, c.satisfaction + (contractor ? 3 : 6));
                S.money += t.fee; S.current.income += t.fee; S.lifetimeRevenue += t.fee;
                Log($"Ticket resolved for {c.customer}: {t.text}. +${t.fee:N0}", 1);
            }
            S.ticketsDone++;
        }

        void SatisfactionHour(Contract c, bool meets)
        {
            float delta = meets ? 0.025f : -0.5f * (1f - c.service) - 0.05f;
            c.satisfaction = Mathf.Clamp(c.satisfaction + delta, 0, 100);
        }

        void GenerateReferral(Contract from)
        {
            var c = ProcGen.GenerateContract(S, rng);
            c.referral = true;
            c.industry = from.industry;
            c.monthlyPay = Mathf.Round(c.monthlyPay * 1.12f / 50f) * 50f;
            if (!c.traits.Contains("Referral")) c.traits.Add("Referral");
            c.flavor = $"Referred by {from.customer}. " + c.flavor;
            S.offers.Add(c);
            Log($"{from.customer} referred a new customer to you: {c.customer}!", 1);
        }

        // ================================================================ rivals
        static readonly string[] RivalA = { "Iron", "Blue", "Apex", "Summit", "Polar", "Vertex", "Granite", "Nimbus", "Atlas", "Helix", "Quantum", "Stratus" };
        static readonly string[] RivalB = { "Hosting", "DataWorks", "Colo", "Cloud", "Compute", "Networks", "Digital", "Infrastructure" };

        public void EnsureRivals()
        {
            if (S.rivals.Count > 0) return;
            var r = new System.Random(S.seed * 31 + 7);
            int n = 3;
            for (int i = 0; i < n; i++)
                S.rivals.Add(new Rival
                {
                    name = ProcGen.Pick(r, RivalA) + " " + ProcGen.Pick(r, RivalB),
                    reputation = ProcGen.Range(r, 35, 70),
                    aggression = ProcGen.Range(r, 0.2f, 0.9f),
                    customers = r.Next(3, 15),
                    colorIdx = r.Next(Catalog.CustomerColors.Length),
                });
        }

        void DailyRivals()
        {
            EnsureRivals();
            float aggr = S.rivals.Sum(x => x.aggression);
            foreach (var o in S.offers.ToList())
            {
                // Offers that have been sitting around get snapped up by competitors.
                float age = (S.hour - (o.expiresHour - 24 * 8)) / 24f;
                if (age > 1.5f && rng.NextDouble() < 0.025 * aggr)
                {
                    var rv = S.rivals[rng.Next(S.rivals.Count)];
                    S.offers.Remove(o);
                    rv.customers++;
                    Log($"{rv.name} signed {o.customer} before you did.", 2);
                }
            }
            foreach (var c in S.contracts.Where(x => x.status == ContractStatus.Active && x.satisfaction < 35).ToList())
            {
                if (rng.NextDouble() < 0.035)
                {
                    var rv = S.rivals[rng.Next(S.rivals.Count)];
                    rv.customers++;
                    S.contractsFailed++;
                    EndContract(c, ContractStatus.Cancelled);
                    Log($"Unhappy with your service, {c.customer} moved to {rv.name}!", 3);
                    popups.Enqueue(new Popup { title = "Customer poached", body = $"{c.customer} was unhappy (satisfaction {c.satisfaction:0}) and has moved to your rival {rv.name}. Keep satisfaction up with good uptime and fast ticket handling." }.Add("OK", null));
                }
            }
            foreach (var rv in S.rivals)
            {
                rv.reputation = Mathf.Clamp(rv.reputation + ProcGen.Range(rng, -0.6f, 0.7f), 10, 95);
                if (rng.NextDouble() < 0.02) rv.customers = Mathf.Max(0, rv.customers - 1);
            }
        }

        public int YourCustomers => S.contracts.Count(c => c.status == ContractStatus.Active || c.status == ContractStatus.Setup);

        // ================================================================ negotiation
        public string Negotiate(Contract c, string ask)
        {
            if (c.negotiated) return "You already negotiated this offer.";
            c.negotiated = true;
            var ind = c.industry;
            const float bias = 0.25f;
            switch (ask)
            {
                case "pay":
                {
                    double p = 0.3 + bias + (c.traits.Contains("Budget") ? -0.15 : 0) + (c.traits.Contains("Rush") ? 0.1 : 0);
                    if (rng.NextDouble() < p)
                    {
                        c.monthlyPay = Mathf.Round(c.monthlyPay * 1.15f / 50f) * 50f;
                        return $"{c.customer} agreed: ${c.monthlyPay:N0}/month.";
                    }
                    if (rng.NextDouble() < 0.4) { S.offers.Remove(c); return $"{c.customer} was offended and walked away."; }
                    return $"{c.customer} refused but the original offer stands.";
                }
                case "time":
                    if (rng.NextDouble() < 0.75 + bias * 0.3) { c.setupDays += 7; return $"{c.customer} gave you 7 more days to set up."; }
                    return $"{c.customer} can't wait any longer.";
                case "sla":
                {
                    if (c.sla <= 99f) return "The SLA is already at the lowest tier.";
                    if (rng.NextDouble() < 0.7 + bias * 0.3)
                    {
                        c.sla = c.sla >= 99.99f ? 99.9f : c.sla >= 99.9f ? 99.5f : 99f;
                        c.monthlyPay = Mathf.Round(c.monthlyPay * 0.9f / 50f) * 50f;
                        return $"{c.customer} accepted {c.sla}% uptime for ${c.monthlyPay:N0}/month.";
                    }
                    return $"{c.customer} insists on their SLA.";
                }
            }
            return "";
        }

        // ================================================================ extra events
        void TriggerExtraEvent(string type)
        {
            switch (type)
            {
                case "fire":
                {
                    var candidates = Racks.Where(r => r.devices.Count > 0).ToList();
                    if (candidates.Count == 0) break;
                    var rack = candidates[rng.Next(candidates.Count)];
                    int zi = ZoneAt(rack);
                    bool suppressed = zi >= 0 && S.objects.Any(o => !o.failed && Catalog.Facility(o.def).kind == FacilityKind.Suppression && ZoneAt(o) == zi);
                    if (suppressed)
                    {
                        var dev = rack.devices[rng.Next(rack.devices.Count)];
                        dev.failed = true; dev.repairLeft = 4;
                        S.firesFought++;
                        MarkDirty(rack);
                        Log($"Fire in rack #{rack.id}! The suppression system put it out; one device was damaged.", 2);
                    }
                    else
                    {
                        int destroyed = Mathf.Min(rack.devices.Count, rng.Next(2, 6));
                        for (int i = 0; i < destroyed; i++) rack.devices.RemoveAt(rng.Next(rack.devices.Count));
                        foreach (var n in Racks.Where(r => r != rack && Mathf.Abs(r.x - rack.x) <= 1 && Mathf.Abs(r.y - rack.y) <= 1))
                            foreach (var d in n.devices) if (rng.NextDouble() < 0.35) { d.failed = true; d.repairLeft = 4; MarkDirty(n); }
                        MarkDirty(rack);
                        AddEvent("fire", 2, rack.id, "Fire");
                        Log($"FIRE in rack #{rack.id}! {destroyed} devices destroyed and neighbours damaged. Install fire suppression!", 3);
                        popups.Enqueue(new Popup { title = "Fire!", body = $"An electrical fire broke out in rack #{rack.id}, destroying {destroyed} devices and damaging nearby racks.\n\nA Fire Suppression System in each server hall would have contained it." }.Add("OK", null));
                    }
                    break;
                }
                case "leak":
                {
                    var crac = ObjectsOfKind(FacilityKind.Cooling).Where(o => !o.failed).ToList();
                    if (crac.Count == 0) break;
                    var unit = crac[rng.Next(crac.Count)];
                    unit.failed = true; unit.repairLeft = 10;
                    MarkDirty(unit);
                    int hits = 0;
                    foreach (var n in Racks.Where(r => Mathf.Abs(r.x - unit.x) <= 2 && Mathf.Abs(r.y - unit.y) <= 2))
                        foreach (var d in n.devices) if (rng.NextDouble() < 0.2) { d.failed = true; d.repairLeft = 4; hits++; MarkDirty(n); }
                    Log($"Coolant leak at {Catalog.Facility(unit.def).name} #{unit.id}! Unit offline, {hits} nearby devices shorted.", 3);
                    break;
                }
                case "ddos":
                {
                    var active = S.contracts.Where(c => c.status == ContractStatus.Active).ToList();
                    if (active.Count == 0) break;
                    var c = active[rng.Next(active.Count)];
                    int sec = SecurityLevel;
                    if (rng.NextDouble() < Mathf.Min(0.9f, sec * 0.18f))
                    {
                        Log($"Your security team blocked a DDoS attack aimed at {c.customer}.", 1);
                        c.satisfaction = Mathf.Min(100, c.satisfaction + 3);
                    }
                    else
                    {
                        int hours = rng.Next(6, 16);
                        AddEvent("surge", hours, 2.2f, "DDoS attack");
                        c.satisfaction = Mathf.Max(0, c.satisfaction - 6);
                        Log($"DDoS attack on {c.customer}! Bandwidth demand x2.2 for {hours}h. Hire Security staff to fend these off.", 3);
                    }
                    break;
                }
                case "shortage":
                {
                    bool gpu = rng.NextDouble() < 0.5 && S.Level >= 4;
                    int days = rng.Next(20, 41);
                    if (gpu) { AddGlobalEvent("gpushortage", days * 24, 1.6f, "GPU shortage"); Log($"Global GPU shortage! GPU server prices +60% for {days} days.", 2); }
                    else { AddGlobalEvent("shortage", days * 24, 1.3f, "Chip shortage"); Log($"Chip shortage! All hardware prices +30% for {days} days.", 2); }
                    break;
                }
                case "bankrupt":
                {
                    var active = S.contracts.Where(c => c.status == ContractStatus.Active).ToList();
                    if (active.Count < 3) break;
                    var c = active[rng.Next(active.Count)];
                    EndContract(c, ContractStatus.Cancelled);
                    Log($"{c.customer} went bankrupt. Their contract ends immediately.", 3);
                    popups.Enqueue(new Popup { title = "Customer bankrupt", body = $"{c.customer} has filed for bankruptcy and their contract is void. Their racks are free to re-use." }.Add("OK", null));
                    break;
                }
                case "regulation":
                {
                    float add = Mathf.Round(ProcGen.Range(rng, 0.008f, 0.02f) * 1000f) / 1000f;
                    S.city.carbonTax += add;
                    Log($"{S.city.name} introduced a carbon levy: +${add:0.000}/kWh on grid power. Solar looks better every day.", 2);
                    break;
                }
                case "pricewar":
                {
                    EnsureRivals();
                    var rv = S.rivals[rng.Next(S.rivals.Count)];
                    int days = rng.Next(20, 41);
                    AddGlobalEvent("pricewar", days * 24, 0.85f, $"Price war ({rv.name})");
                    Log($"{rv.name} slashed prices! New contract offers pay 15% less for {days} days.", 2);
                    break;
                }
                case "flood":
                {
                    int dmg = 0, protectedRacks = 0;
                    foreach (var o in S.objects)
                    {
                        var d = Catalog.Facility(o.def);
                        bool indoors = d.room != RoomType.Outdoor;
                        if (!indoors || o.floor > 0) continue;
                        if (S.HasFlagAt(o.floor, o.x, o.y, GameState.FlagRaised)) { if (d.kind == FacilityKind.Rack) protectedRacks++; continue; }
                        if (d.kind == FacilityKind.Rack)
                        {
                            foreach (var dev in o.devices)
                                if (dev.u < 3 && !dev.failed && rng.NextDouble() < 0.6) { dev.failed = true; dev.repairLeft = 4; dmg++; MarkDirty(o); }
                        }
                        else if ((d.kind == FacilityKind.Ups || d.kind == FacilityKind.Switchgear || d.kind == FacilityKind.Battery) && !o.failed && rng.NextDouble() < 0.25)
                        {
                            o.failed = true; o.repairLeft = 10; dmg++; MarkDirty(o);
                        }
                    }
                    if (dmg == 0 && protectedRacks > 0) { S.floodsSurvived++; Log("Flash flood! Raised floors kept all your hardware dry.", 1); }
                    else if (dmg > 0) Log($"Flash flood! Water reached the floor and damaged {dmg} device(s). Raised floors in server halls prevent this.", 3);
                    else Log("Flash flood nearby - no damage this time.", 2);
                    break;
                }
                case "breakin":
                {
                    if (SecurityLevel >= 2 || rng.NextDouble() < SecurityLevel * 0.3f) { Log("Security stopped an attempted break-in at the loading dock.", 1); break; }
                    var all = Racks.Where(r => r.devices.Count > 0 && r.contractId < 0).ToList();
                    if (all.Count == 0) break;
                    var rack = all[rng.Next(all.Count)];
                    var dev = rack.devices[rng.Next(rack.devices.Count)];
                    rack.devices.Remove(dev);
                    MarkDirty(rack);
                    Log($"Thieves stole a {Catalog.Device(dev.def).name} from rack #{rack.id}. Hire Security staff!", 3);
                    break;
                }
            }
        }

        // ================================================================ scenarios
        public class ScenarioDef
        {
            public string id, name, desc, goal;
            public int days;
        }

        public static readonly List<ScenarioDef> Scenarios = new List<ScenarioDef>
        {
            new ScenarioDef { id = "sandbox", name = "Endless", desc = "Endless play on a random map with the normal economy.", goal = "No goal - build the biggest, most reliable data center you can.", days = 0 },
            new ScenarioDef { id = "creative", name = "Sandbox", desc = "Creative mode: unlimited money, all technology unlocked, max level, and switches for failures, events and bankruptcy. Achievements are disabled.", goal = "No goal - experiment freely.", days = 0 },
            new ScenarioDef { id = "desert", name = "Desert Heat", desc = "A scorching desert city where cooling is brutal.", goal = "Reach $150,000/month recurring revenue within 3 years.", days = 1080 },
            new ScenarioDef { id = "shoestring", name = "Shoestring", desc = "Start with only $110,000 - every dollar counts.", goal = "Reach reputation level 5 within 2 years.", days = 720 },
            new ScenarioDef { id = "aiboom", name = "AI Boom", desc = "Everyone wants GPUs. You start at level 5 with GPU servers researched.", goal = "Run 96 GPUs online for customers within 2 years.", days = 720 },
            new ScenarioDef { id = "blackout", name = "Blackout Belt", desc = "A storm-battered grid with constant outages. Generators are researched.", goal = "Complete 15 contracts with an average uptime of 99.9%+ within 2 years.", days = 720 },
            new ScenarioDef { id = "green", name = "Green Mandate", desc = "A heavy carbon tax and eco-conscious customers.", goal = "Hit 40% solar energy in a month while earning $100,000/month.", days = 1080 },
        };

        public static ScenarioDef Scenario(string id) => Scenarios.FirstOrDefault(s => s.id == id) ?? Scenarios[0];

        public static void ApplyScenario(GameState s)
        {
            switch (s.scenario)
            {
                case "creative":
                    s.creative = true;
                    s.cheatInfiniteMoney = true;
                    s.cheatNoBankruptcy = true;
                    s.money = 1_000_000_000f;
                    s.xp = Catalog.XpForLevel(Catalog.MaxLevel);
                    s.peakLevel = Catalog.MaxLevel;
                    foreach (var d in Catalog.Devices) if (d.research > 0) s.unlocked.Add(d.id);
                    foreach (var f in Catalog.Facilities) if (f.research > 0) s.unlocked.Add(f.id);
                    break;
                case "desert": s.city.climate = "Desert"; s.city.coolingFactor = 0.68f; s.city.solarFactor = 1.3f; s.city.blurb = "Relentless desert heat. " + s.city.blurb; break;
                case "shoestring": s.money = 110000; break;
                case "aiboom":
                    s.xp = Catalog.XpForLevel(5); s.peakLevel = 5;
                    s.unlocked.AddRange(new[] { "srv_gpu4", "rack_hd", "tor_25g", "isp_100", "gen" });
                    s.money += 250000;
                    break;
                case "blackout": s.city.disasterRate = 2.6f; s.unlocked.Add("gen"); break;
                case "green": s.city.carbonTax = 0.07f; s.city.solarFactor = 1.2f; s.unlocked.Add("solar"); break;
            }
        }

        public int MonthlyRevenue => Mathf.RoundToInt(S.contracts.Where(c => c.status == ContractStatus.Active).Sum(c => c.monthlyPay));
        public int OnlineCustomerGpus => S.contracts.Where(c => c.status == ContractStatus.Active).Sum(c => provision.TryGetValue(c.id, out var p) ? p.gpus : 0);

        public float ScenarioProgress(out string text)
        {
            var sc = Scenario(S.scenario);
            switch (sc.id)
            {
                case "desert": text = $"${MonthlyRevenue:N0} / $150,000 per month"; return MonthlyRevenue / 150000f;
                case "shoestring": text = $"Level {S.Level} / 5"; return S.Level / 5f;
                case "aiboom": text = $"{OnlineCustomerGpus} / 96 GPUs"; return OnlineCustomerGpus / 96f;
                case "blackout":
                {
                    float avg = BlackoutUptime();
                    text = $"{S.contractsCompleted}/15 contracts completed, avg uptime {avg:0.00}%";
                    return Mathf.Min(1f, S.contractsCompleted / 15f) * (avg >= 99.9f ? 1f : 0.95f);
                }
                case "green": text = $"Solar {S.lastSolarShare * 100:0}% / 40%, ${MonthlyRevenue:N0} / $100,000"; return Mathf.Min(S.lastSolarShare / 0.4f, MonthlyRevenue / 100000f);
            }
            text = "";
            return 0;
        }

        float BlackoutUptime()
        {
            var served = S.contracts.Where(c => c.totalHours > 0).ToList();
            if (served.Count == 0) return 0f;
            float hours = served.Sum(c => (float)c.totalHours);
            return 100f * (1f - served.Sum(c => c.totalDown) / hours);
        }

        void CheckScenario()
        {
            var sc = Scenario(S.scenario);
            if (sc.id == "sandbox" || sc.id == "creative" || S.scenarioDone) return;
            ScenarioProgress(out _);
            bool won = false;
            switch (sc.id)
            {
                case "desert": won = MonthlyRevenue >= 150000; break;
                case "shoestring": won = S.Level >= 5; break;
                case "aiboom": won = OnlineCustomerGpus >= 96; break;
                case "blackout": won = S.contractsCompleted >= 15 && BlackoutUptime() >= 99.9f; break;
                case "green": won = S.lastSolarShare >= 0.4f && MonthlyRevenue >= 100000; break;
            }
            if (won)
            {
                S.scenarioDone = true;
                Unlock("scenario_" + sc.id);
                popups.Enqueue(new Popup { title = "Scenario complete!", body = $"You beat \"{sc.name}\" on day {S.Day + 1}.\n\nScore: {Score:N0}\n\nYou can keep playing in endless mode." }.Add("Keep playing", null));
            }
            else if (S.Day >= sc.days)
            {
                S.scenarioDone = true;
                popups.Enqueue(new Popup { title = "Scenario failed", body = $"Time ran out on \"{sc.name}\".\n\nScore: {Score:N0}\n\nYou can keep playing in endless mode." }.Add("Keep playing", null));
            }
        }

        // ================================================================ achievements & score
        public class AchievementDef { public string id, name, desc; }

        public static readonly List<AchievementDef> Achievements = new List<AchievementDef>
        {
            new AchievementDef { id = "first_contract", name = "Open for Business", desc = "Bring your first contract online." },
            new AchievementDef { id = "ten_racks", name = "Rack 'em Up", desc = "Own 10 racks." },
            new AchievementDef { id = "hundred_servers", name = "Server Farm", desc = "Run 100 servers." },
            new AchievementDef { id = "thousand_servers", name = "Hyperscaler", desc = "Run 1,000 servers." },
            new AchievementDef { id = "mrr_50k", name = "Recurring Dreams", desc = "Reach $50,000/month recurring revenue." },
            new AchievementDef { id = "mrr_250k", name = "Quarter Million", desc = "Reach $250,000/month recurring revenue." },
            new AchievementDef { id = "millionaire", name = "Millionaire", desc = "Have $1,000,000 in the bank." },
            new AchievementDef { id = "level5", name = "Respected", desc = "Reach reputation level 5." },
            new AchievementDef { id = "level10", name = "Industry Legend", desc = "Reach reputation level 10." },
            new AchievementDef { id = "five_nines", name = "Five Nines", desc = "Complete a 99.99% SLA contract without a breach." },
            new AchievementDef { id = "sunpowered", name = "Sunpowered", desc = "Get 50% of a month's energy from solar." },
            new AchievementDef { id = "lights_on", name = "Lights Stay On", desc = "Ride out a major grid outage with zero downtime." },
            new AchievementDef { id = "firefighter", name = "Firefighter", desc = "Have fire suppression put out a fire." },
            new AchievementDef { id = "helpdesk", name = "Help Desk Hero", desc = "Resolve 50 support tickets." },
            new AchievementDef { id = "ai_factory", name = "AI Factory", desc = "Run 64 GPUs online for customers." },
            new AchievementDef { id = "belt_braces", name = "Belt and Braces", desc = "Bring a fully redundant contract online." },
            new AchievementDef { id = "cool_rows", name = "Cool Rows", desc = "Have 20 racks with cold-aisle containment." },
            new AchievementDef { id = "ipo", name = "Going Public", desc = "Take your company public." },
            new AchievementDef { id = "multinational", name = "Multinational", desc = "Operate data centers in 3 cities." },
            new AchievementDef { id = "geo", name = "Across the Map", desc = "Bring a geo-redundant contract online." },
            new AchievementDef { id = "flood", name = "High and Dry", desc = "Ride out a flood thanks to raised floors." },
        };

        public void Unlock(string id)
        {
            if (S.creative || S.achievements.Contains(id)) return;
            S.achievements.Add(id);
            try { PlayerPrefs.SetInt("ach_" + id, 1); PlayerPrefs.Save(); } catch { }
            var a = Achievements.FirstOrDefault(x => x.id == id);
            if (a != null) Log($"Achievement unlocked: {a.name} - {a.desc}", 1);
        }

        public static bool UnlockedEver(string id) { try { return PlayerPrefs.GetInt("ach_" + id, 0) == 1; } catch { return false; } }

        void CheckAchievements()
        {
            int servers = company.sims.Sum(x => x.racks.Values.Sum(i => i.activeServers));
            if (S.contracts.Any(c => c.status == ContractStatus.Active || c.status == ContractStatus.Completed)) Unlock("first_contract");
            if (company.TotalRacks >= 10) Unlock("ten_racks");
            if (company.sims.Count >= 3) Unlock("multinational");
            if (S.contracts.Any(c => c.geoRedundant && c.status == ContractStatus.Active && c.meeting)) Unlock("geo");
            if (servers >= 100) Unlock("hundred_servers");
            if (servers >= 1000) Unlock("thousand_servers");
            if (MonthlyRevenue >= 50000) Unlock("mrr_50k");
            if (MonthlyRevenue >= 250000) Unlock("mrr_250k");
            if (S.money >= 1000000) Unlock("millionaire");
            if (S.Level >= 5) Unlock("level5");
            if (S.Level >= 10) Unlock("level10");
            if (S.contracts.Any(c => c.status == ContractStatus.Completed && c.sla >= 99.99f && c.breaches == 0)) Unlock("five_nines");
            if (S.lastSolarShare >= 0.5f) Unlock("sunpowered");
            if (S.outagesSurvived > 0) Unlock("lights_on");
            if (S.firesFought > 0) Unlock("firefighter");
            if (S.ticketsDone >= 50) Unlock("helpdesk");
            if (OnlineCustomerGpus >= 64) Unlock("ai_factory");
            if (S.contracts.Any(c => c.redundant && c.status == ContractStatus.Active && c.meeting)) Unlock("belt_braces");
            if (Racks.Count(r => AirflowFactor(r, out _) < 1f) >= 20) Unlock("cool_rows");
            if (S.ipo) Unlock("ipo");
            if (S.floodsSurvived > 0) Unlock("flood");
            S.peakMonthlyRevenue = Mathf.Max(S.peakMonthlyRevenue, MonthlyRevenue);
        }

        public int Score => Mathf.RoundToInt(S.lifetimeRevenue / 1000f + S.contractsCompleted * 100 + S.Level * 500 + S.achievements.Count * 250 - S.contractsFailed * 150 + (S.ipo ? 10000 : 0));

        public bool CanIpo(out string why)
        {
            var gaps = new List<string>();
            if (MonthlyRevenue < 400000) gaps.Add($"${MonthlyRevenue:N0}/$400,000 monthly revenue");
            if (S.Level < 8) gaps.Add($"level {S.Level}/8");
            why = gaps.Count == 0 ? null : "Needs " + string.Join(", ", gaps);
            return gaps.Count == 0 && !S.ipo;
        }

        public void DoIpo()
        {
            if (!CanIpo(out _)) return;
            S.ipo = true;
            float raise = Mathf.Round(MonthlyRevenue * 12f / 100000f) * 100000f;
            S.money += raise;
            S.current.bonuses += raise;
            Unlock("ipo");
            popups.Enqueue(new Popup { title = "IPO!", body = $"Your data center company is now publicly traded. The offering raised ${raise:N0}.\n\nFinal score: {Score:N0}\n\nThe market will keep sending customers - play on as long as you like." }.Add("Ring the bell", null));
        }

        // ================================================================ sandbox tools
        public static readonly string[] SandboxEvents = { "outage", "heatwave", "powerspike", "fibercut", "surge", "recall", "fire", "leak", "ddos", "shortage", "bankrupt", "regulation", "pricewar", "breakin", "flood", "expansion", "grant", "press" };

        public void SandboxAddMoney(float amount) { S.money += amount; Log($"Sandbox: added ${amount:N0}.", 0); }

        public void SandboxSetLevel(int level)
        {
            level = Mathf.Clamp(level, 1, Catalog.MaxLevel);
            S.xp = Catalog.XpForLevel(level);
            S.peakLevel = Mathf.Max(S.peakLevel, level);
        }

        public void SandboxUnlockAll()
        {
            foreach (var d in Catalog.Devices) if (d.research > 0 && !S.unlocked.Contains(d.id)) S.unlocked.Add(d.id);
            foreach (var f in Catalog.Facilities) if (f.research > 0 && !S.unlocked.Contains(f.id)) S.unlocked.Add(f.id);
            Log("Sandbox: all technology unlocked.", 1);
        }

        public void SandboxSpawnOffer()
        {
            var c = ProcGen.GenerateContract(S, rng);
            S.offers.Add(c);
            Log($"Sandbox: new offer from {c.customer}.", 0);
        }

        public void SandboxRepairAll()
        {
            foreach (var site in S.sites)
                foreach (var o in site.objects)
                {
                    o.failed = false; o.pduTripped = false;
                    foreach (var d in o.devices) d.failed = false;
                }
            foreach (var sim in company.sims) foreach (var o in sim.Site.objects) sim.MarkDirty(o);
            Recompute();
            Log("Sandbox: everything repaired.", 1);
        }

        public void SandboxTrigger(string type)
        {
            if (TriggerEventNow(type)) Log($"Sandbox: triggered {type}.", 0);
        }

        bool TriggerEventNow(string type)
        {
            if (EventActive(type)) return false;
            inSiteTick = true;
            try { TriggerEvent(type); } finally { inSiteTick = false; }
            return true;
        }

        public void SandboxSkipDays(int days)
        {
            for (int i = 0; i < days * Catalog.HoursPerDay && !S.gameOver; i++)
            {
                TickHour();
                while (popups.Count > 0) popups.Dequeue();
            }
        }

        // ================================================================ daily & monthly extras
        bool outageHadDowntime;

        static readonly HashSet<string> CompanyEvents = new HashSet<string> { "shortage", "pricewar", "bankrupt", "expansion", "grant", "press" };

        /// <summary>Local events hit a random site; company-wide events run here.</summary>
        void TriggerOnSite(string type)
        {
            if (CompanyEvents.Contains(type) || company.sims.Count == 1)
            {
                if (!EventActive(type)) TriggerEvent(type);
                return;
            }
            int i = rng.Next(company.sims.Count);
            var target = company.sims[i];
            company.WithSite(i, () =>
            {
                target.inSiteTick = true;
                try { if (!target.EventActive(type)) target.TriggerEvent(type); }
                finally { target.inSiteTick = false; }
            });
        }

        void AddGlobalEvent(string type, int hours, float mag, string label)
        {
            foreach (var site in S.sites)
                site.events.Add(new ActiveEvent { type = type, startHour = S.hour, endHour = S.hour + hours, magnitude = mag, label = label, global = true });
        }

        void HourSite()
        {
            if (outageActive && powerShort) outageHadDowntime = true;
            BatteryHour();
            float gridKWh = Mathf.Max(0, loadKW - solarKW - batteryDischargeKW) + batteryChargeKW;
            gridKW = gridKWh;
            S.monthSolarKWh += Mathf.Min(loadKW, solarKW);
            S.monthGridKWh += gridKWh;
            AgeHardware();
        }

        void OnEventEnded(ActiveEvent e)
        {
            if (e.type == "outage")
            {
                if (e.magnitude > 0.5f && !outageHadDowntime) { S.outagesSurvived++; Log("Your backup power carried the whole outage. Zero downtime!", 1); }
                outageHadDowntime = false;
            }
        }

        void DailyExtras()
        {
            DailyTickets();
            DailyRivals();
            if (S.Day % 7 == 0 || S.candidates.Count == 0) RefreshCandidates();
            S.dayStats.Add(new DayStat
            {
                day = S.Day,
                money = S.money,
                revenue = MonthlyRevenue,
                cost = hourlyCost * 24,
                loadKW = company.TotalLoadKW,
                solarKW = company.sims.Sum(x => x.solarCapKW),
                uptime = S.contracts.Where(c => c.status == ContractStatus.Active).Select(c => c.service).DefaultIfEmpty(1f).Average() * 100f,
                temp = company.sims.SelectMany(x => x.zones).Select(z => z.temp).DefaultIfEmpty(20f).Max(),
            });
            if (S.dayStats.Count > 180) S.dayStats.RemoveAt(0);
            CheckAchievements();
            CheckScenario();
        }

        void MonthlyExtras()
        {
            float solar = 0, grid = 0;
            for (int i = 0; i < company.sims.Count; i++)
                company.WithSite(i, () => { solar += S.monthSolarKWh; grid += S.monthGridKWh; S.monthSolarKWh = S.monthGridKWh = 0; });
            float total = solar + grid;
            S.lastSolarShare = total > 0 ? solar / total : 0;
        }

        void EcoCheck(Contract c)
        {
            if (!c.traits.Contains("Eco")) return;
            if (S.lastSolarShare >= 0.25f) { c.satisfaction = Mathf.Min(100, c.satisfaction + 4); return; }
            float credit = Mathf.Round(c.monthlyPay * 0.2f);
            S.money -= credit;
            S.current.penalties += credit;
            c.satisfaction = Mathf.Max(0, c.satisfaction - 6);
            Log($"{c.customer} wanted 25%+ solar energy (you had {S.lastSolarShare * 100:0}%). ${credit:N0} green-pledge credit.", 2);
        }
    }
}
