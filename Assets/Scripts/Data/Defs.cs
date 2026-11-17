using System.Collections.Generic;
using UnityEngine;

namespace DCT
{
    public enum RoomType { None = 0, Corridor = 1, ServerHall = 2, Network = 3, Power = 4, Office = 5, Outdoor = 99 }
    public enum DeviceKind { Server, Switch }
    public enum FacilityKind { Rack, Cooling, CoreSwitch, Isp, Transformer, Switchgear, Ups, Generator, Desk, Solar, Suppression }

    /// <summary>Rack-mountable equipment (servers and top-of-rack switches).</summary>
    [System.Serializable]
    public class DeviceDef
    {
        public string id, name, desc;
        public DeviceKind kind;
        public int heightU = 1;
        public float cost;
        public float watts;
        public int cores;
        public float storageTB;
        public int gpus;
        public int ports;          // switch: server-facing ports
        public float uplinkGbps;   // switch: uplink speed to core
        public int level = 1;
        public float research;
        public Color color = Color.gray;
    }

    /// <summary>Floor-standing facility equipment.</summary>
    [System.Serializable]
    public class FacilityDef
    {
        public string id, name, desc;
        public FacilityKind kind;
        public RoomType room;      // Outdoor = owned land with no building on it
        public int w = 1, h = 1;
        public float cost;
        public float watts;
        public float coolingKW;
        public float supplyKW;
        public float backupKW;
        public float bandwidth;    // ISP Gbps
        public float monthly;      // recurring cost
        public int ports;          // core switch ports
        public float portGbps;
        public int rackU;
        public float rackKW;       // rack PDU limit
        public int staff;
        public string model;       // optional Resources/Models prefab (Blender asset)
        public int level = 1;
        public float research;
        public Color color = Color.gray;
    }

    public static class Catalog
    {
        public const float FoundationCost = 150f;
        public const float RoomPaintCost = 25f;
        public const float CableBaseCost = 200f;
        public const float CablePerTile = 30f;
        public const float TechSalary = 3200f;
        public const int HoursPerDay = 24;
        public const int DaysPerMonth = 30;
        public const int HoursPerMonth = HoursPerDay * DaysPerMonth;
        public const int MaxLevel = 10;

        public static readonly List<DeviceDef> Devices = new List<DeviceDef>
        {
            // ---- Top-of-rack switches ----
            new DeviceDef { id = "tor_1g", name = "ToR Switch 24x1G", kind = DeviceKind.Switch, heightU = 1, cost = 800, watts = 60, ports = 24, uplinkGbps = 10, level = 1,
                desc = "Entry-level top-of-rack switch. 24 server ports, 10G uplink.", color = new Color(0.2f, 0.45f, 0.85f) },
            new DeviceDef { id = "tor_10g", name = "ToR Switch 48x10G", kind = DeviceKind.Switch, heightU = 1, cost = 4200, watts = 150, ports = 48, uplinkGbps = 40, level = 2, research = 15000,
                desc = "48 server ports with a 40G uplink.", color = new Color(0.15f, 0.55f, 0.95f) },
            new DeviceDef { id = "tor_25g", name = "ToR Switch 48x25G", kind = DeviceKind.Switch, heightU = 1, cost = 9500, watts = 260, ports = 48, uplinkGbps = 100, level = 4, research = 45000,
                desc = "High-throughput switch with a 100G uplink.", color = new Color(0.1f, 0.7f, 1f) },
            new DeviceDef { id = "tor_100g", name = "ToR Switch 32x100G", kind = DeviceKind.Switch, heightU = 1, cost = 26000, watts = 420, ports = 32, uplinkGbps = 400, level = 7, research = 160000,
                desc = "Hyperscale-grade switch with a 400G uplink.", color = new Color(0.3f, 0.9f, 1f) },

            // ---- Servers ----
            new DeviceDef { id = "srv_basic", name = "Basic 1U Server", kind = DeviceKind.Server, heightU = 1, cost = 1500, watts = 220, cores = 8, storageTB = 1, level = 1,
                desc = "Cheap single-socket box. Good enough to get started.", color = new Color(0.55f, 0.57f, 0.6f) },
            new DeviceDef { id = "srv_stor2u", name = "Storage 2U (12-bay)", kind = DeviceKind.Server, heightU = 2, cost = 4200, watts = 320, cores = 4, storageTB = 48, level = 1,
                desc = "Twelve spinning disks of bulk storage.", color = new Color(0.6f, 0.5f, 0.35f) },
            new DeviceDef { id = "srv_pro", name = "Pro 1U Dual-CPU", kind = DeviceKind.Server, heightU = 1, cost = 5200, watts = 380, cores = 32, storageTB = 2, level = 2, research = 12000,
                desc = "Dual-socket workhorse with 32 cores.", color = new Color(0.45f, 0.5f, 0.65f) },
            new DeviceDef { id = "srv_comp2u", name = "Compute 2U", kind = DeviceKind.Server, heightU = 2, cost = 9800, watts = 520, cores = 64, storageTB = 4, level = 3, research = 30000,
                desc = "64 cores of dense compute.", color = new Color(0.4f, 0.55f, 0.75f) },
            new DeviceDef { id = "srv_stor4u", name = "Storage 4U (60-bay)", kind = DeviceKind.Server, heightU = 4, cost = 19000, watts = 650, cores = 8, storageTB = 260, level = 3, research = 35000,
                desc = "Sixty-bay storage chassis.", color = new Color(0.7f, 0.55f, 0.3f) },
            new DeviceDef { id = "srv_arm", name = "ARM Efficiency 1U", kind = DeviceKind.Server, heightU = 1, cost = 8500, watts = 240, cores = 80, storageTB = 2, level = 4, research = 60000,
                desc = "80 efficient ARM cores. Great performance per watt.", color = new Color(0.35f, 0.7f, 0.55f) },
            new DeviceDef { id = "srv_nvme", name = "NVMe Storage 1U", kind = DeviceKind.Server, heightU = 1, cost = 16000, watts = 380, cores = 16, storageTB = 120, level = 4, research = 70000,
                desc = "All-flash storage, very dense.", color = new Color(0.85f, 0.65f, 0.25f) },
            new DeviceDef { id = "srv_gpu4", name = "GPU Server 4U (4x GPU)", kind = DeviceKind.Server, heightU = 4, cost = 46000, watts = 1900, cores = 32, storageTB = 4, gpus = 4, level = 5, research = 120000,
                desc = "Four accelerators for ML and rendering workloads.", color = new Color(0.45f, 0.75f, 0.2f) },
            new DeviceDef { id = "srv_hd2u4n", name = "HD Compute 2U4N", kind = DeviceKind.Server, heightU = 2, cost = 24000, watts = 1250, cores = 192, storageTB = 8, level = 6, research = 150000,
                desc = "Four nodes in 2U: 192 cores.", color = new Color(0.55f, 0.45f, 0.85f) },
            new DeviceDef { id = "srv_gpu8", name = "AI Server 8U (8x GPU)", kind = DeviceKind.Server, heightU = 8, cost = 240000, watts = 6500, cores = 96, storageTB = 30, gpus = 8, level = 7, research = 400000,
                desc = "Flagship AI training node. Needs serious power and cooling.", color = new Color(0.3f, 0.95f, 0.35f) },
            new DeviceDef { id = "srv_quantum", name = "Quantum Co-Processor 6U", kind = DeviceKind.Server, heightU = 6, cost = 600000, watts = 3000, cores = 64, storageTB = 10, gpus = 24, level = 9, research = 1200000,
                desc = "Experimental hybrid accelerator, counts as 24 GPUs of capacity.", color = new Color(0.9f, 0.3f, 0.95f) },
        };

        public static readonly List<FacilityDef> Facilities = new List<FacilityDef>
        {
            // ---- Racks ----
            new FacilityDef { id = "rack_std", name = "42U Rack", kind = FacilityKind.Rack, room = RoomType.ServerHall, cost = 1500, rackU = 42, rackKW = 8, level = 1,
                desc = "Standard rack with an 8 kW PDU.", color = new Color(0.12f, 0.13f, 0.15f) },
            new FacilityDef { id = "rack_hd", name = "48U High-Density Rack", kind = FacilityKind.Rack, room = RoomType.ServerHall, cost = 4500, rackU = 48, rackKW = 20, level = 4, research = 40000,
                desc = "Taller rack with a 20 kW PDU.", color = new Color(0.1f, 0.1f, 0.12f) },
            new FacilityDef { id = "rack_ai", name = "52U AI Pod Rack", kind = FacilityKind.Rack, room = RoomType.ServerHall, cost = 15000, rackU = 52, rackKW = 60, level = 7, research = 200000,
                desc = "Reinforced rack with a 60 kW PDU for GPU clusters.", color = new Color(0.08f, 0.1f, 0.1f) },

            // ---- Cooling ----
            new FacilityDef { id = "crac", model = "crac", name = "CRAC Unit", kind = FacilityKind.Cooling, room = RoomType.ServerHall, cost = 8000, watts = 7000, coolingKW = 40, level = 1,
                desc = "Computer-room air conditioner. Cools 40 kW of heat in its server hall.", color = new Color(0.85f, 0.88f, 0.9f) },
            new FacilityDef { id = "inrow", model = "inrow", name = "In-Row Cooler", kind = FacilityKind.Cooling, room = RoomType.ServerHall, cost = 16000, watts = 8000, coolingKW = 75, level = 3, research = 25000,
                desc = "Close-coupled cooling, 75 kW.", color = new Color(0.75f, 0.85f, 0.95f) },
            new FacilityDef { id = "liquid", name = "Liquid Cooling CDU", kind = FacilityKind.Cooling, room = RoomType.ServerHall, w = 2, h = 1, cost = 70000, watts = 15000, coolingKW = 300, level = 6, research = 180000,
                desc = "Coolant distribution unit, 300 kW of cooling.", color = new Color(0.4f, 0.75f, 0.95f) },

            // ---- Network ----
            new FacilityDef { id = "core_10g", model = "core", name = "Core Switch 24x10G", kind = FacilityKind.CoreSwitch, room = RoomType.Network, cost = 12000, watts = 800, ports = 24, portGbps = 10, level = 1,
                desc = "Aggregates up to 24 racks at 10G each.", color = new Color(0.15f, 0.3f, 0.6f) },
            new FacilityDef { id = "core_40g", model = "core", name = "Core Switch 48x40G", kind = FacilityKind.CoreSwitch, room = RoomType.Network, cost = 38000, watts = 1500, ports = 48, portGbps = 40, level = 3, research = 35000,
                desc = "48 rack ports at 40G.", color = new Color(0.15f, 0.35f, 0.75f) },
            new FacilityDef { id = "core_100g", model = "core", name = "Spine Router 64x100G", kind = FacilityKind.CoreSwitch, room = RoomType.Network, cost = 95000, watts = 2500, ports = 64, portGbps = 100, level = 5, research = 110000,
                desc = "64 rack ports at 100G.", color = new Color(0.2f, 0.45f, 0.9f) },
            new FacilityDef { id = "core_400g", model = "core", name = "Spine Router 64x400G", kind = FacilityKind.CoreSwitch, room = RoomType.Network, cost = 260000, watts = 4500, ports = 64, portGbps = 400, level = 8, research = 450000,
                desc = "64 rack ports at 400G.", color = new Color(0.35f, 0.55f, 1f) },
            new FacilityDef { id = "isp_10", model = "isp", name = "ISP Fiber 10G", kind = FacilityKind.Isp, room = RoomType.Network, cost = 5000, watts = 200, bandwidth = 10, monthly = 900, level = 1,
                desc = "10 Gbps of internet transit. Monthly fee applies.", color = new Color(0.95f, 0.55f, 0.15f) },
            new FacilityDef { id = "isp_100", model = "isp", name = "ISP Fiber 100G", kind = FacilityKind.Isp, room = RoomType.Network, cost = 25000, watts = 400, bandwidth = 100, monthly = 6000, level = 4, research = 50000,
                desc = "100 Gbps transit line.", color = new Color(1f, 0.65f, 0.2f) },
            new FacilityDef { id = "isp_400", model = "isp", name = "Dark Fiber 400G", kind = FacilityKind.Isp, room = RoomType.Network, cost = 90000, watts = 800, bandwidth = 400, monthly = 18000, level = 7, research = 250000,
                desc = "Private dark fiber with 400 Gbps.", color = new Color(1f, 0.8f, 0.3f) },

            // ---- Power ----
            new FacilityDef { id = "xfmr", model = "xfmr", name = "Utility Transformer", kind = FacilityKind.Transformer, room = RoomType.Outdoor, cost = 20000, supplyKW = 150, level = 1,
                desc = "Outdoor grid connection, 150 kW. Must sit on owned land outside the building.", color = new Color(0.45f, 0.55f, 0.45f) },
            new FacilityDef { id = "xfmr_big", name = "Substation Feed", kind = FacilityKind.Transformer, room = RoomType.Outdoor, w = 2, h = 2, cost = 120000, supplyKW = 1200, level = 5, research = 90000,
                desc = "Dedicated substation, 1.2 MW.", color = new Color(0.4f, 0.5f, 0.4f) },
            new FacilityDef { id = "swgear", model = "swgear", name = "Switchgear Panel", kind = FacilityKind.Switchgear, room = RoomType.Power, cost = 10000, supplyKW = 250, level = 1,
                desc = "Distributes up to 250 kW to the building. Usable supply is the lower of transformer and switchgear capacity.", color = new Color(0.6f, 0.6f, 0.62f) },
            new FacilityDef { id = "swgear_big", name = "Main Distribution Board", kind = FacilityKind.Switchgear, room = RoomType.Power, w = 2, h = 1, cost = 60000, supplyKW = 1500, level = 5, research = 80000,
                desc = "Distributes 1.5 MW.", color = new Color(0.55f, 0.55f, 0.6f) },
            new FacilityDef { id = "ups", model = "ups", name = "UPS Battery Bank", kind = FacilityKind.Ups, room = RoomType.Power, cost = 15000, backupKW = 100, level = 1,
                desc = "Rides through short grid outages for up to 100 kW of load. Needed for generators to take over.", color = new Color(0.2f, 0.22f, 0.25f) },
            new FacilityDef { id = "ups_li", model = "ups", name = "Li-ion UPS", kind = FacilityKind.Ups, room = RoomType.Power, cost = 60000, backupKW = 500, level = 4, research = 60000,
                desc = "500 kW lithium-ion UPS.", color = new Color(0.15f, 0.25f, 0.3f) },
            new FacilityDef { id = "gen", model = "gen", name = "Diesel Generator", kind = FacilityKind.Generator, room = RoomType.Outdoor, w = 2, h = 1, cost = 45000, backupKW = 300, level = 2, research = 10000,
                desc = "Outdoor backup generator, 300 kW. Covers long outages together with UPS.", color = new Color(0.9f, 0.75f, 0.15f) },
            new FacilityDef { id = "gen_big", name = "Turbine Generator", kind = FacilityKind.Generator, room = RoomType.Outdoor, w = 2, h = 2, cost = 200000, backupKW = 1500, level = 6, research = 150000,
                desc = "1.5 MW gas turbine.", color = new Color(0.85f, 0.6f, 0.15f) },

            // ---- Energy & safety ----
            new FacilityDef { id = "solar", model = "solar", name = "Solar Array", kind = FacilityKind.Solar, room = RoomType.Outdoor, w = 2, h = 2, cost = 35000, supplyKW = 60, level = 3, research = 25000,
                desc = "Generates up to 60 kW in full sun, cutting your grid bill and carbon tax. Output follows the sun and weather.", color = new Color(0.15f, 0.25f, 0.5f) },
            new FacilityDef { id = "solar_big", name = "Solar Farm Block", kind = FacilityKind.Solar, room = RoomType.Outdoor, w = 3, h = 3, cost = 110000, supplyKW = 220, level = 6, research = 90000,
                desc = "High-efficiency bifacial panels, up to 220 kW.", color = new Color(0.12f, 0.2f, 0.45f) },
            new FacilityDef { id = "suppress", model = "suppress", name = "Fire Suppression System", kind = FacilityKind.Suppression, room = RoomType.ServerHall, cost = 12000, watts = 100, level = 2, research = 8000,
                desc = "Inert-gas suppression for its whole server hall: fires are put out before they destroy hardware.", color = new Color(0.85f, 0.15f, 0.12f) },

            // ---- Office ----
            new FacilityDef { id = "desk", model = "desk", name = "Technician Desk", kind = FacilityKind.Desk, room = RoomType.Office, cost = 1500, watts = 300, staff = 1, level = 1,
                desc = "Workspace for one technician. Technicians repair failed hardware automatically.", color = new Color(0.55f, 0.4f, 0.28f) },
        };

        [System.Serializable]
        public class CatalogData { public List<DeviceDef> devices = new List<DeviceDef>(); public List<FacilityDef> facilities = new List<FacilityDef>(); }

        static bool loadedData;

        /// <summary>Loads Resources/Data/catalog.json if present so equipment can be rebalanced without code changes.</summary>
        public static void EnsureLoaded()
        {
            if (loadedData) return;
            loadedData = true;
            var ta = Resources.Load<TextAsset>("Data/catalog");
            if (ta == null) return;
            try
            {
                var data = JsonUtility.FromJson<CatalogData>(ta.text);
                if (data != null && data.devices != null && data.devices.Count > 0) { Devices.Clear(); Devices.AddRange(data.devices); }
                if (data != null && data.facilities != null && data.facilities.Count > 0) { Facilities.Clear(); Facilities.AddRange(data.facilities); }
                deviceMap = null; facilityMap = null;
            }
            catch (System.Exception e) { Debug.LogWarning("catalog.json failed to load: " + e.Message); }
        }

        public static string ExportJson() => JsonUtility.ToJson(new CatalogData { devices = Devices, facilities = Facilities }, true);

        static Dictionary<string, DeviceDef> deviceMap;
        static Dictionary<string, FacilityDef> facilityMap;

        public static DeviceDef Device(string id)
        {
            EnsureLoaded();
            if (deviceMap == null) { deviceMap = new Dictionary<string, DeviceDef>(); foreach (var d in Devices) deviceMap[d.id] = d; }
            return id != null && deviceMap.TryGetValue(id, out var r) ? r : null;
        }

        public static FacilityDef Facility(string id)
        {
            EnsureLoaded();
            if (facilityMap == null) { facilityMap = new Dictionary<string, FacilityDef>(); foreach (var d in Facilities) facilityMap[d.id] = d; }
            return id != null && facilityMap.TryGetValue(id, out var r) ? r : null;
        }

        public static string RoomName(RoomType t)
        {
            switch (t)
            {
                case RoomType.None: return "Empty Land";
                case RoomType.Corridor: return "Corridor";
                case RoomType.ServerHall: return "Server Hall";
                case RoomType.Network: return "Network Room";
                case RoomType.Power: return "Power Room";
                case RoomType.Office: return "Office";
                case RoomType.Outdoor: return "Outdoors (owned land)";
            }
            return t.ToString();
        }

        public static Color RoomColor(RoomType t)
        {
            switch (t)
            {
                case RoomType.Corridor: return new Color(0.62f, 0.63f, 0.65f);
                case RoomType.ServerHall: return new Color(0.78f, 0.8f, 0.84f);
                case RoomType.Network: return new Color(0.55f, 0.66f, 0.85f);
                case RoomType.Power: return new Color(0.85f, 0.78f, 0.5f);
                case RoomType.Office: return new Color(0.78f, 0.68f, 0.55f);
            }
            return new Color(0.3f, 0.5f, 0.25f);
        }

        public static string KindName(FacilityKind k)
        {
            switch (k)
            {
                case FacilityKind.Rack: return "Racks";
                case FacilityKind.Cooling: return "Cooling";
                case FacilityKind.CoreSwitch: return "Core Network";
                case FacilityKind.Isp: return "ISP Uplinks";
                case FacilityKind.Transformer: return "Grid Power";
                case FacilityKind.Switchgear: return "Distribution";
                case FacilityKind.Ups: return "UPS";
                case FacilityKind.Generator: return "Generators";
                case FacilityKind.Desk: return "Office";
                case FacilityKind.Solar: return "Solar";
                case FacilityKind.Suppression: return "Fire Safety";
            }
            return k.ToString();
        }

        public static readonly Color[] CustomerColors =
        {
            new Color(0.95f, 0.3f, 0.3f), new Color(0.3f, 0.75f, 0.95f), new Color(0.98f, 0.75f, 0.2f), new Color(0.45f, 0.9f, 0.4f),
            new Color(0.8f, 0.4f, 0.95f), new Color(1f, 0.55f, 0.2f), new Color(0.2f, 0.9f, 0.8f), new Color(0.95f, 0.45f, 0.7f),
            new Color(0.6f, 0.6f, 1f), new Color(0.75f, 0.9f, 0.3f), new Color(0.9f, 0.9f, 0.9f), new Color(0.6f, 0.35f, 0.2f),
        };

        // ---- Progression ----
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            return Mathf.RoundToInt(450f * Mathf.Pow(level - 1, 1.9f));
        }

        public static int LevelFromXp(int xp)
        {
            int l = 1;
            while (l < MaxLevel && xp >= XpForLevel(l + 1)) l++;
            return l;
        }

        public static string TypeName(ContractType t)
        {
            switch (t)
            {
                case ContractType.Colocation: return "Colocation";
                case ContractType.Dedicated: return "Dedicated Servers";
                case ContractType.Compute: return "Cloud Compute";
                case ContractType.Storage: return "Storage";
                case ContractType.GpuCloud: return "GPU Cloud";
            }
            return t.ToString();
        }
    }
}
