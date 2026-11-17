using System;
using System.Collections.Generic;

namespace DCT
{
    public enum ContractType { Colocation, Dedicated, Compute, Storage, GpuCloud }
    public enum ContractStatus { Offer, Setup, Active, Completed, Cancelled }

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
        public bool failed;
        public int repairLeft;
        public int placedOrder;
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
    }

    [Serializable]
    public class MonthReport
    {
        public int month;
        public float income, bonuses, penalties, power, network, salaries, capex, other;
        public float Net => income + bonuses - penalties - power - network - salaries - capex - other;
    }

    [Serializable]
    public class GameState
    {
        public int version = 1;
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

        public int Day => hour / Catalog.HoursPerDay;
        public int Month => Day / Catalog.DaysPerMonth;
        public int Level => Catalog.LevelFromXp(xp);
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < mapW && y < mapH;
        public bool Owned(int x, int y) => x >= ownX0 && x < ownX1 && y >= ownY0 && y < ownY1;
        public RoomType Room(int x, int y) => InBounds(x, y) ? (RoomType)rooms[y * mapW + x] : RoomType.None;
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
