using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCT
{
    public enum ContractType { Colocation, Dedicated, Compute, Storage, GpuCloud }
    public enum ContractStatus { Offer, Setup, Active, Completed, Cancelled }

    /// <summary>Everything that belongs to one physical data center site.</summary>
    [Serializable]
    public class SiteData
    {
        public string name;
        public CityProfile city;
        public int mapW = 64, mapH = 48;
        public int ownX0, ownY0, ownX1, ownY1;
        public int[] rooms;
        public int[] obstacles;
        public int[] flags;
        public List<PlacedObject> objects = new List<PlacedObject>();
        public List<StaffMember> staff = new List<StaffMember>();
        public List<ActiveEvent> events = new List<ActiveEvent>();
        public string weather = "Clear";
        public float monthSolarKWh, monthGridKWh;
        public int landPurchases;
        public int openedHour;
    }

    [Serializable]
    public class CityProfile
    {
        public string name;
        public string climate;
        public float coolingFactor = 1f;   // multiplier on cooling capacity
        public float powerPrice = 0.12f;   // $ per kWh
        public float landPrice = 1f;       // multiplier on land expansion
        public float disasterRate = 1f;    // multiplier on bad event chance
        public float demandFactor = 1f;    // multiplier on contract pay
        public float carbonTax;            // $ per grid kWh
        public float solarFactor = 1f;     // sunniness
        public string blurb;
    }

    [Serializable]
    public class RackDevice
    {
        public int id;
        public string def;
        public int u;            // lowest occupied slot (0-based)
        public bool failed;
        public int repairLeft;   // hours of technician work remaining
        public int ageHours;
    }

    [Serializable]
    public class PlacedObject
    {
        public int id;
        public string def;
        public int x, y;
        public int rot;          // 0..3, 90 degree steps
        public List<RackDevice> devices = new List<RackDevice>();
        public int contractId = -1;
        public int linkCore = -1;
        public int linkCore2 = -1;     // redundant uplink
        public bool dualPdu;           // redundant power feed
        public bool pduTripped;
        public float charge;           // battery storage state of charge (kWh)
        public bool failed;
        public int repairLeft;
        public int placedOrder;
        public int floor;
    }

    [Serializable]
    public class Contract
    {
        public int id;
        public string customer;
        public string industry;
        public string flavor;
        public int colorIdx;
        public ContractType type;
        public ContractStatus status;

        public int reqRacks;
        public float reqKWPerRack;
        public int reqServers;
        public int reqCoresPerServer;
        public int reqCores;
        public float reqStorageTB;
        public int reqGpus;
        public float reqBandwidth;

        public float sla = 99f;
        public int months;
        public float monthlyPay;
        public float signingBonus;
        public int setupDays;
        public List<string> traits = new List<string>();

        public int expiresHour;
        public int deadlineHour;
        public int startHour;
        public int nextMonthHour;
        public int monthsServed;
        public int monthHours;
        public float monthDown;
        public int totalHours;
        public float totalDown;
        public int breaches;
        public bool meeting;
        public float service = 1f;
        public bool expanded;
        public bool isRenewal;
        public float earned;
        public string lastIssue;
        public bool redundant;          // requires dual power, dual ToR, dual uplink on every rack
        public bool geoRedundant;       // requires online racks in at least two sites
        public float satisfaction = 70f;
        public bool negotiated;
        public bool referral;
    }

    [Serializable]
    public class StaffMember
    {
        public int id;
        public string name;
        public string role;            // Technician, Electrician, NetEngineer, Security
        public int skill = 1;          // 1..5
        public float salary;
        public float x, y;             // tile-space position
        public float px, py;           // previous-hour position (for smooth rendering)
        public string state = "idle";  // idle, walking, working, returning, off
        public string shift = "Day";   // Day (07-19) or Night (19-07)
        public int floor;
        public float fatigue;
        public int taskObj = -1, taskDev = -1, taskTicket = -1;
        public float work;
        public List<int> path = new List<int>();
        public int hiredHour;
    }

    [Serializable]
    public class Ticket
    {
        public int id;
        public int contractId;
        public string text;
        public int createdHour, deadlineHour;
        public float fee;
        public float work = 2f;
        public int rackId = -1;
        public int assigned = -1;
    }

    [Serializable]
    public class Rival
    {
        public string name;
        public float reputation;
        public float aggression;
        public int customers;
        public int colorIdx;
    }

    [Serializable]
    public class DayStat
    {
        public int day;
        public float money, revenue, cost, loadKW, solarKW, uptime, temp;
    }

    [Serializable]
    public class Candidate
    {
        public string name, role;
        public int skill;
        public float salary;
    }

    [Serializable]
    public class LogEntry
    {
        public int hour;
        public string text;
        public int severity; // 0 info, 1 good, 2 warning, 3 bad
    }

    [Serializable]
    public class ActiveEvent
    {
        public string type;
        public int startHour;
        public int endHour;
        public float magnitude;
        public string label;
        public bool global;
    }

    [Serializable]
    public class MonthReport
    {
        public int month;
        public float income, bonuses, penalties, power, network, salaries, capex, other;
        public float Net => income + bonuses - penalties - power - network - salaries - capex - other;
        /// <summary>Profit from running the business, before construction and hardware purchases (investment).</summary>
        public float Operating => income + bonuses - penalties - power - network - salaries - other;
    }

    [Serializable]
    public class GameState
    {
        public int version = 2;
        public int seed;
        public string difficulty = "Normal";
        public float money;
        public int hour;
        public int xp;
        public float rating = 50f;
        public CityProfile city = new CityProfile();

        public int mapW = 64, mapH = 48;
        public int ownX0, ownY0, ownX1, ownY1;   // owned land rectangle, [x0,x1) x [y0,y1)
        public int[] rooms;
        public int[] obstacles;                    // 0 none, 1 tree, 2 rock

        public List<PlacedObject> objects = new List<PlacedObject>();
        public List<Contract> contracts = new List<Contract>();
        public List<Contract> offers = new List<Contract>();
        public List<string> unlocked = new List<string>();
        public List<LogEntry> log = new List<LogEntry>();
        public List<ActiveEvent> events = new List<ActiveEvent>();
        public List<MonthReport> reports = new List<MonthReport>();
        public MonthReport current = new MonthReport();

        public int nextId = 1;
        public int technicians;
        public int nextOfferHour;
        public int bankruptHours;
        public int contractsCompleted;
        public int contractsFailed;
        public float lifetimeRevenue;
        public int landPurchases;
        public bool gameOver;
        public int peakLevel = 1;

        // ---- v2 systems ----
        public List<StaffMember> staff = new List<StaffMember>();
        public List<Candidate> candidates = new List<Candidate>();
        public List<Ticket> tickets = new List<Ticket>();
        public List<Rival> rivals = new List<Rival>();
        public List<DayStat> dayStats = new List<DayStat>();
        public List<string> achievements = new List<string>();
        public string weather = "Clear";
        public float hardwarePriceMul = 1f;
        public float gpuPriceMul = 1f;
        public float monthSolarKWh, monthGridKWh;
        public float lastSolarShare;
        public string scenario = "sandbox";
        public bool scenarioDone;
        public bool ipo;
        public int ticketsDone, ticketsMissed, firesFought, outagesSurvived, floodsSurvived;
        public float peakMonthlyRevenue;

        // ---- multi-site ----
        public int[] flags;                                // per tile: 1 = cable tray, 2 = raised floor
        public string siteName = "HQ";
        public List<SiteData> sites = new List<SiteData>();
        public int activeSite;
        public bool tutorial;
        public int tutorialStep;

        // ---- sandbox (creative) mode ----
        public bool creative;
        public bool cheatInfiniteMoney, cheatNoFailures, cheatNoEvents, cheatNoBankruptcy;
        [NonSerialized] public int bound = -1;

        public const int FlagTray = 1, FlagRaised = 2;
        public const int MaxFloors = 4;
        public const float FloorHeight = 3.2f;

        /// <summary>Floor currently being viewed/edited by the player (not saved).</summary>
        [NonSerialized] public int floor;

        public int Idx(int f, int x, int y) => (f * mapH + y) * mapW + x;
        public int Plane => mapW * mapH;
        public bool HasFlag(int x, int y, int f) => HasFlagAt(floor, x, y, f);
        public bool HasFlagAt(int fl, int x, int y, int f) => InBounds(x, y) && fl >= 0 && fl < MaxFloors && flags != null && (flags[Idx(fl, x, y)] & f) != 0;
        public RoomType RoomAt(int fl, int x, int y) => InBounds(x, y) && fl >= 0 && fl < MaxFloors ? (RoomType)rooms[Idx(fl, x, y)] : RoomType.None;

        /// <summary>Highest floor that has any building on it.</summary>
        public int TopFloor
        {
            get
            {
                for (int f = MaxFloors - 1; f > 0; f--)
                {
                    int a = f * Plane;
                    for (int i = 0; i < Plane; i++) if (rooms[a + i] != 0) return f;
                }
                return 0;
            }
        }

        static int[] Expand(int[] a, int plane)
        {
            if (a == null) return new int[plane * MaxFloors];
            if (a.Length >= plane * MaxFloors) return a;
            var b = new int[plane * MaxFloors];
            System.Array.Copy(a, b, Mathf.Min(a.Length, plane));
            return b;
        }

        /// <summary>Creates the site list for new games and for saves made before multi-site support.</summary>
        public void EnsureSites()
        {
            rooms = Expand(rooms, mapW * mapH);
            flags = Expand(flags, mapW * mapH);
            if (sites.Count == 0)
            {
                var d = new SiteData();
                bound = 0;
                activeSite = 0;
                sites.Add(d);
                StoreTo(d);
                d.name = string.IsNullOrEmpty(siteName) ? "HQ" : siteName;
            }
            else
            {
                // The top-level fields are authoritative for the active site (they were serialized while bound).
                bound = Mathf.Clamp(activeSite, 0, sites.Count - 1);
                activeSite = bound;
                StoreTo(sites[bound]);
            }
            foreach (var s in sites)
            {
                s.rooms = Expand(s.rooms, s.mapW * s.mapH);
                s.flags = Expand(s.flags, s.mapW * s.mapH);
            }
            if (bound >= 0) { rooms = sites[bound].rooms; flags = sites[bound].flags; }
        }

        /// <summary>Swaps the given site's data into the top-level fields so per-site code can run on it.</summary>
        public void Bind(int i)
        {
            if (bound == i) return;
            if (bound >= 0 && bound < sites.Count) StoreTo(sites[bound]);
            LoadFrom(sites[i]);
            bound = i;
        }

        public void SyncForSave() { if (bound >= 0 && bound < sites.Count) StoreTo(sites[bound]); }

        public string Serialize()
        {
            int was = bound;
            if (was != activeSite && activeSite < sites.Count) Bind(activeSite);
            SyncForSave();
            string json = UnityEngine.JsonUtility.ToJson(this);
            if (was >= 0 && was != bound) Bind(was);
            return json;
        }

        void StoreTo(SiteData d)
        {
            d.city = city; d.mapW = mapW; d.mapH = mapH;
            d.ownX0 = ownX0; d.ownY0 = ownY0; d.ownX1 = ownX1; d.ownY1 = ownY1;
            d.rooms = rooms; d.obstacles = obstacles; d.flags = flags;
            d.objects = objects; d.staff = staff; d.events = events;
            d.weather = weather; d.monthSolarKWh = monthSolarKWh; d.monthGridKWh = monthGridKWh;
            d.landPurchases = landPurchases;
            d.name = siteName;
        }

        void LoadFrom(SiteData d)
        {
            city = d.city; mapW = d.mapW; mapH = d.mapH;
            ownX0 = d.ownX0; ownY0 = d.ownY0; ownX1 = d.ownX1; ownY1 = d.ownY1;
            rooms = d.rooms; obstacles = d.obstacles; flags = d.flags;
            objects = d.objects; staff = d.staff; events = d.events;
            weather = d.weather; monthSolarKWh = d.monthSolarKWh; monthGridKWh = d.monthGridKWh;
            landPurchases = d.landPurchases;
            siteName = d.name;
        }

        public int Day => hour / Catalog.HoursPerDay;
        public int Month => Day / Catalog.DaysPerMonth;
        public int Level => Catalog.LevelFromXp(xp);
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < mapW && y < mapH;
        public bool Owned(int x, int y) => x >= ownX0 && x < ownX1 && y >= ownY0 && y < ownY1;
        public RoomType Room(int x, int y) => RoomAt(floor, x, y);
        public int NewId() => nextId++;

        public string DateString()
        {
            int d = Day;
            int year = d / 360 + 1;
            int month = (d / 30) % 12 + 1;
            int day = d % 30 + 1;
            return $"Y{year} M{month:00} D{day:00}  {hour % 24:00}:00";
        }
    }
}
