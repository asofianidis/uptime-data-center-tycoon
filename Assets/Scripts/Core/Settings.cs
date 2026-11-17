using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>Player preferences: UI scale, audio, toggles and rebindable keys. Stored in PlayerPrefs.</summary>
    public static class Settings
    {
        public static float UiScale = 1f;
        public static float Master = 0.8f, Music = 0.45f, Sfx = 0.8f;
        public static bool EdgePan;
        public static bool Autosave = true;
        public static bool DayNight = true;
        public static bool WeatherFx = true;
        public static bool ShowStaff = true;

        public static readonly List<KeyValuePair<string, KeyCode>> Defaults = new List<KeyValuePair<string, KeyCode>>
        {
            new KeyValuePair<string, KeyCode>("Pause", KeyCode.Space),
            new KeyValuePair<string, KeyCode>("Foundation", KeyCode.F),
            new KeyValuePair<string, KeyCode>("Equipment", KeyCode.B),
            new KeyValuePair<string, KeyCode>("Demolish", KeyCode.X),
            new KeyValuePair<string, KeyCode>("Rotate", KeyCode.R),
            new KeyValuePair<string, KeyCode>("Undo", KeyCode.Z),
            new KeyValuePair<string, KeyCode>("Market", KeyCode.M),
            new KeyValuePair<string, KeyCode>("Contracts", KeyCode.C),
            new KeyValuePair<string, KeyCode>("Research", KeyCode.T),
            new KeyValuePair<string, KeyCode>("Staff", KeyCode.K),
            new KeyValuePair<string, KeyCode>("Goals", KeyCode.G),
            new KeyValuePair<string, KeyCode>("Dashboard", KeyCode.J),
            new KeyValuePair<string, KeyCode>("HeatMap", KeyCode.H),
            new KeyValuePair<string, KeyCode>("CycleOverlay", KeyCode.O),
            new KeyValuePair<string, KeyCode>("CamLeft", KeyCode.Q),
            new KeyValuePair<string, KeyCode>("CamRight", KeyCode.E),
            new KeyValuePair<string, KeyCode>("QuickSave", KeyCode.F5),
            new KeyValuePair<string, KeyCode>("QuickLoad", KeyCode.F9),
        };

        public static readonly Dictionary<string, KeyCode> Keys = new Dictionary<string, KeyCode>();
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            foreach (var kv in Defaults) Keys[kv.Key] = kv.Value;
            try
            {
                UiScale = PlayerPrefs.GetFloat("set_ui", 1f);
                Master = PlayerPrefs.GetFloat("set_master", 0.8f);
                Music = PlayerPrefs.GetFloat("set_music", 0.45f);
                Sfx = PlayerPrefs.GetFloat("set_sfx", 0.8f);
                EdgePan = PlayerPrefs.GetInt("set_edge", 0) == 1;
                Autosave = PlayerPrefs.GetInt("set_autosave", 1) == 1;
                DayNight = PlayerPrefs.GetInt("set_daynight", 1) == 1;
                WeatherFx = PlayerPrefs.GetInt("set_weather", 1) == 1;
                ShowStaff = PlayerPrefs.GetInt("set_staff", 1) == 1;
                foreach (var kv in Defaults)
                {
                    int v = PlayerPrefs.GetInt("key_" + kv.Key, (int)kv.Value);
                    if (Enum.IsDefined(typeof(KeyCode), v)) Keys[kv.Key] = (KeyCode)v;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetFloat("set_ui", UiScale);
                PlayerPrefs.SetFloat("set_master", Master);
                PlayerPrefs.SetFloat("set_music", Music);
                PlayerPrefs.SetFloat("set_sfx", Sfx);
                PlayerPrefs.SetInt("set_edge", EdgePan ? 1 : 0);
                PlayerPrefs.SetInt("set_autosave", Autosave ? 1 : 0);
                PlayerPrefs.SetInt("set_daynight", DayNight ? 1 : 0);
                PlayerPrefs.SetInt("set_weather", WeatherFx ? 1 : 0);
                PlayerPrefs.SetInt("set_staff", ShowStaff ? 1 : 0);
                foreach (var kv in Keys) PlayerPrefs.SetInt("key_" + kv.Key, (int)kv.Value);
                PlayerPrefs.Save();
            }
            catch { }
        }

        public static void ResetKeys()
        {
            foreach (var kv in Defaults) Keys[kv.Key] = kv.Value;
            Save();
        }

        public static KeyCode Key(string action) { Load(); return Keys.TryGetValue(action, out var k) ? k : KeyCode.None; }
        public static bool Down(string action) => Input.GetKeyDown(Key(action));
        public static bool Held(string action) => Input.GetKey(Key(action));
        public static string KeyName(string action) => Key(action).ToString().Replace("Alpha", "");
    }

    [Serializable]
    public class RackTemplate
    {
        public string name;
        public List<string> devices = new List<string>();
        public bool dualPdu;
        public bool secondUplink;
    }

    /// <summary>Rack layout templates shared across all saves.</summary>
    public static class Templates
    {
        [Serializable] class Wrapper { public List<RackTemplate> list = new List<RackTemplate>(); }

        static List<RackTemplate> cache;

        public static List<RackTemplate> All
        {
            get
            {
                if (cache != null) return cache;
                cache = new List<RackTemplate>();
                try
                {
                    var json = PlayerPrefs.GetString("rack_templates", "");
                    if (!string.IsNullOrEmpty(json)) cache = JsonUtility.FromJson<Wrapper>(json).list ?? new List<RackTemplate>();
                }
                catch { }
                if (cache.Count == 0)
                {
                    cache.Add(new RackTemplate { name = "Starter compute", devices = new List<string> { "tor_1g" }.Concat(Enumerable.Repeat("srv_basic", 12)).ToList() });
                    cache.Add(new RackTemplate { name = "Bulk storage", devices = new List<string> { "tor_1g" }.Concat(Enumerable.Repeat("srv_stor2u", 8)).ToList() });
                    cache.Add(new RackTemplate { name = "Colo shell (redundant)", devices = new List<string> { "tor_1g", "tor_1g" }, dualPdu = true, secondUplink = true });
                }
                return cache;
            }
        }

        public static void Save()
        {
            try { PlayerPrefs.SetString("rack_templates", JsonUtility.ToJson(new Wrapper { list = All })); PlayerPrefs.Save(); } catch { }
        }

        public static RackTemplate FromRack(PlacedObject rack, string name)
        {
            return new RackTemplate
            {
                name = name,
                devices = rack.devices.OrderBy(d => d.u).Select(d => d.def).ToList(),
                dualPdu = rack.dualPdu,
                secondUplink = rack.linkCore2 > 0,
            };
        }

        public static float Cost(RackTemplate t, Simulation sim)
        {
            float c = t.devices.Sum(id => { var d = Catalog.Device(id); return d != null ? sim.DeviceCost(d) : 0; });
            if (t.dualPdu) c += Simulation.DualPduCost;
            return c;
        }

        /// <summary>Applies a template to an empty rack. Returns number of devices installed.</summary>
        public static int Apply(RackTemplate t, PlacedObject rack, Simulation sim, out string error)
        {
            error = null;
            int n = 0;
            foreach (var id in t.devices)
            {
                var d = Catalog.Device(id);
                if (d == null) continue;
                var e = sim.CanAddDevice(rack, d);
                if (e != null) { error = $"{d.name}: {e}"; break; }
                sim.AddDevice(rack, d);
                n++;
            }
            if (t.dualPdu && !rack.dualPdu) sim.BuyDualPdu(rack);
            if (rack.linkCore < 0) sim.AutoConnect(rack);
            if (t.secondUplink && rack.linkCore2 < 0) sim.AutoConnectSecondary(rack);
            return n;
        }
    }
}
