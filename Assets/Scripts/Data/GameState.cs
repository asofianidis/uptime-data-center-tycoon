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

        // ---- v3 per-site systems (accessed directly through Simulation.Site, not swapped by Bind) ----
        public float monthITKWh, monthTotalKWh, monthWaterM3, monthHeatKWh;
        public float lastPue;                  // energy-weighted PUE of the last full month (0 = no data)
        public List<float> pueHistory = new List<float>();
        public int certUntilHour = -1;         // compliance certification valid until this hour
        public int auditEndHour = -1;          // audit in progress until this hour
        public int lastBreachHour = -100000;   // last successful break-in (voids certification)
        public int residentialSide = -1;       // 0 N, 1 S, 2 E, 3 W; -1 = not generated yet
        public float noiseLevel;               // loudest level at the neighbours' fence this hour (dB)
        public int complaints;                 // noise complaints this month
        public int complaintsTotal;
        public float noiseStrikes;             // escalates fines; decays when quiet
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
        // ---- v3 ----
        public bool v3;                    // false on cities from older saves: filled in from the climate on load
        public float humidity = 0.5f;      // 0 dry .. 1 humid (evaporative cooling works best when dry)
        public float waterPrice = 2.5f;    // $ per cubic metre
        public float heatPrice;            // $ per kWh of recovered heat sold to district heating (winter peak)
        public float noiseLimit = 55f;     // dB allowed at the residential boundary
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
        public int arriveHour;   // on order: delivery hour
        public bool arrived;     // on order: delivered to the dock, waiting to be installed
        public bool express;     // on order: shipped express
        public float paid;       // on order: price paid (refunded if the order is cancelled)
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
        public List<RackDevice> orders = new List<RackDevice>();   // hardware on order / awaiting install
        public int servicedHour = -1;  // last preventive maintenance (-1 = since placed)
        public bool maintQueued;       // a maintenance job is waiting for staff
        public bool maintNow;          // requested by the player: don't wait for the night window
        public bool inMaint;           // staff are servicing it right now (offline unless redundant)
        public int placedHour = -1;
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
        public bool extended;           // customer already granted one setup-deadline extension
        public bool isRenewal;
        public float earned;
        public string lastIssue;
        public bool redundant;          // requires dual power, dual ToR, dual uplink on every rack
        public bool geoRedundant;       // requires online racks in at least two sites
        public float satisfaction = 70f;
        public bool negotiated;
        public bool referral;
        // ---- v3 ----
        public bool compliance;         // racks must be in certified (audited) sites
        public bool caged;              // racks must sit inside one private cage
        public bool efficient;          // wants a monthly PUE at or below Simulation.EfficientPue
        public bool anchor;             // hyperscale anchor tenant: dedicated hall, phased ramp-up
        public float phase = 1f;        // anchor: current phase as a fraction of the full requirement
        public int reqRacksFull, reqServersFull, reqCoresFull, reqGpusFull;
        public float reqStorageFull, reqBandwidthFull;
        public bool rfp;                // won through a competitive bid
        public bool acquired;           // came with a rival acquisition
        public List<float> satHistory = new List<float>();   // one sample per day
        public List<string> satNotes = new List<string>();   // recent reasons for satisfaction changes
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
        // ---- v3: growth ----
        public float xp;
        public float morale = 75f;
        public int trainingUntil = -1;  // away on a course until this hour
        public bool raiseRequested;
        public int jobsDone;
        public int lastRaiseHour;
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
        public float pue, sales, share;
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
        // ---- v3 ----
        public float sales;          // spot compute and district-heat sales
        public float claims;         // insurance payouts
        public float interest, insurance, water, fines;
        public float borrowed, repaid;   // loan principal in / out (financing, not operating)
        public float pue;
        /// <summary>Profit from running the business, before construction and hardware purchases (investment).</summary>
        public float Operating => income + bonuses + sales + claims - penalties - power - network - salaries - other - interest - insurance - water - fines;
        /// <summary>Change in cash: operating profit minus investment, plus loan money in, minus principal repaid.</summary>
        public float Net => Operating - capex + borrowed - repaid;
    }

    [Serializable]
    public class Loan
    {
        public int id;
        public string lender;
        public float principal, balance;
        public float rate;          // annual interest rate
        public int months, monthsLeft;
        public float payment;       // fixed monthly payment (term loans)
        public int nextHour;
        public bool creditLine;     // revolving: interest only, repay any time
    }

    [Serializable]
    public class Rfp
    {
        public int id;
        public Contract contract;   // the deal on offer (monthlyPay = the customer's budget)
        public int closesHour;
        public float bidMul = -1f;  // your bid as a fraction of the budget; -1 = no bid
        public string rivalWinner;
        public float bestRivalMul;
    }

    [Serializable]
    public class Incident
    {
        public int id;
        public int site;
        public int startHour, endHour = -1;
        public float customerHours;         // summed contract downtime
        public List<string> causes = new List<string>();
        public List<string> events = new List<string>();
        public List<string> customers = new List<string>();
        public List<string> fixes = new List<string>();
        public bool major;
    }

    [Serializable]
    public class GameState
    {
        public int version = 3;
        public int seed;
        public string difficulty = "Normal";
        public float money;
        public int hour;
        public int xp;
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

        // ---- v3: finance, operations, business ----
        public List<Loan> loans = new List<Loan>();
        public int missedPayments, loansRepaid;
        public float creditScore = 650f;
        public int insuranceTier;                  // 0 none, 1 basic, 2 comprehensive
        public int claimsMade;
        public float claimsPaidTotal, claimsRecent;
        public bool shipExpress;
        public bool spotEnabled;
        public float spotCoreMul = 1f, spotGpuMul = 1f, spotEarnedTotal, heatSoldTotal;
        public bool autoMaintenance = true;
        public int maintIntervalDays = 120;
        public bool maintNightOnly = true;
        public int lastComplaintHour = -1;
        public List<Rfp> rfps = new List<Rfp>();
        public int rfpsWon, rfpsLost;
        public List<Incident> incidents = new List<Incident>();
        public bool incidentOpen;
        public bool postMortemPopups = true;
        public int rivalsAcquired;
        public float sharePrice, ipoSharePrice, shares, boardConfidence = 70f;
        public int quarterEndHour, quartersMet, quartersMissed;
        public float quarterStartMrr, quarterTarget;
        public float finesTotal;
        public int trainingsDone;
        public bool scenarioSetup;
        public float scenarioBaseline;

        // ---- sandbox (creative) mode ----
        public bool creative;
        public bool cheatInfiniteMoney, cheatNoFailures, cheatNoEvents, cheatNoBankruptcy;
        [NonSerialized] public int bound = -1;

        public const int FlagTray = 1, FlagRaised = 2, FlagCage = 4;
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
