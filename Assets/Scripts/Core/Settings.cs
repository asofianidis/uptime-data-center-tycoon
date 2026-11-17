using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        // ---- video
        /// <summary>0 = borderless fullscreen, 1 = exclusive fullscreen, 2 = windowed.</summary>
        public static int DisplayMode;
        /// <summary>Requested resolution; 0 means "use the monitor's native resolution".</summary>
        public static int ResW, ResH;
        public static bool VSync = true;
        /// <summary>Frame rate cap when VSync is off; 0 = unlimited.</summary>
        public static int FpsCap = 120;
        /// <summary>Index into QualitySettings.names; -1 = project default.</summary>
        public static int Quality = -1;
        /// <summary>MSAA sample count: 0, 2, 4 or 8.</summary>
        public static int Msaa = 4;
        /// <summary>0 = off, 1 = hard, 2 = soft.</summary>
        public static int Shadows = 2;

        public static readonly string[] DisplayModeNames = { "Borderless fullscreen", "Exclusive fullscreen", "Windowed" };
        public static readonly int[] FpsCaps = { 30, 60, 75, 120, 144, 165, 240, 0 };
        public static readonly int[] MsaaLevels = { 0, 2, 4, 8 };
        public static readonly string[] ShadowNames = { "Off", "Hard", "Soft" };
        /// <summary>Default shadow mode adopted when picking each quality preset (Very Low .. Ultra).</summary>
        public static readonly int[] PresetShadows = { 0, 0, 1, 2, 2, 2 };

        /// <summary>Shadow type the directional sun light should use for the current setting.</summary>
        public static LightShadows SunShadows => Shadows == 0 ? LightShadows.None : Shadows == 1 ? LightShadows.Hard : LightShadows.Soft;

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
            new KeyValuePair<string, KeyCode>("FloorUp", KeyCode.PageUp),
            new KeyValuePair<string, KeyCode>("FloorDown", KeyCode.PageDown),
            new KeyValuePair<string, KeyCode>("CamLeft", KeyCode.Q),
            new KeyValuePair<string, KeyCode>("CamRight", KeyCode.E),
            new KeyValuePair<string, KeyCode>("QuickSave", KeyCode.F5),
            new KeyValuePair<string, KeyCode>("QuickLoad", KeyCode.F9),
            new KeyValuePair<string, KeyCode>("Fullscreen", KeyCode.F11),
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
                DisplayMode = Mathf.Clamp(PlayerPrefs.GetInt("set_dispmode", ModeIndex(Screen.fullScreenMode)), 0, 2);
                ResW = PlayerPrefs.GetInt("set_resw", 0);
                ResH = PlayerPrefs.GetInt("set_resh", 0);
                VSync = PlayerPrefs.GetInt("set_vsync", 1) == 1;
                FpsCap = PlayerPrefs.GetInt("set_fps", 120);
                Quality = PlayerPrefs.GetInt("set_quality", -1);
                Msaa = PlayerPrefs.GetInt("set_msaa", 4);
                Shadows = Mathf.Clamp(PlayerPrefs.GetInt("set_shadows", 2), 0, 2);
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
                PlayerPrefs.SetInt("set_dispmode", DisplayMode);
                PlayerPrefs.SetInt("set_resw", ResW);
                PlayerPrefs.SetInt("set_resh", ResH);
                PlayerPrefs.SetInt("set_vsync", VSync ? 1 : 0);
                PlayerPrefs.SetInt("set_fps", FpsCap);
                PlayerPrefs.SetInt("set_quality", Quality);
                PlayerPrefs.SetInt("set_msaa", Msaa);
                PlayerPrefs.SetInt("set_shadows", Shadows);
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

        // ---------------------------------------------------------------- video
        public static FullScreenMode ToFullScreenMode(int mode) =>
            mode == 1 ? (Application.platform == RuntimePlatform.OSXPlayer ? FullScreenMode.MaximizedWindow : FullScreenMode.ExclusiveFullScreen)
            : mode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;

        public static int ModeIndex(FullScreenMode m) =>
            m == FullScreenMode.ExclusiveFullScreen || m == FullScreenMode.MaximizedWindow ? 1 : m == FullScreenMode.Windowed ? 2 : 0;

        public static Vector2Int NativeResolution
        {
            get
            {
                var r = Screen.currentResolution;
                return r.width > 0 ? new Vector2Int(r.width, r.height) : new Vector2Int(Screen.width, Screen.height);
            }
        }

        /// <summary>Distinct resolutions the monitor supports (smallest first), always including the native one.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = new List<Vector2Int>();
            try { list.AddRange(Screen.resolutions.Select(r => new Vector2Int(r.width, r.height))); } catch { }
            var native = NativeResolution;
            list.Add(native);
            if (list.Count == 1)
                foreach (var r in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                    if (r.x <= native.x && r.y <= native.y) list.Add(r);
            return list.Where(r => (r.x >= 1024 && r.y >= 600) || r == native).Distinct()
                .OrderBy(r => r.x * r.y).ThenBy(r => r.x).ToList();
        }

        public static Vector2Int EffectiveResolution(int w, int h) => w > 0 && h > 0 ? new Vector2Int(w, h) : NativeResolution;

        /// <summary>Applies the display mode and resolution. Windowed sizes as large as the desktop are shrunk to fit.</summary>
        public static void ApplyDisplay()
        {
            if (Application.isBatchMode) return;
            var mode = ToFullScreenMode(DisplayMode);
            var res = EffectiveResolution(ResW, ResH);
            if (mode == FullScreenMode.Windowed)
            {
                var n = NativeResolution;
                if (res.x >= n.x || res.y >= n.y) res = WindowedFallback(n);
            }
            Screen.SetResolution(res.x, res.y, mode);
        }

        static Vector2Int WindowedFallback(Vector2Int native)
        {
            int w = Mathf.RoundToInt(native.x * 0.8f), h = Mathf.RoundToInt(native.y * 0.8f);
            return new Vector2Int(Mathf.Max(1024, w), Mathf.Max(600, h));
        }

        /// <summary>Toggles between windowed and the last fullscreen mode (F11 / Alt+Enter).</summary>
        public static void ToggleFullscreen()
        {
            if (DisplayMode == 2)
            {
                DisplayMode = PlayerPrefs.GetInt("set_lastfs", 0);
                ResW = ResH = 0;
            }
            else
            {
                PlayerPrefs.SetInt("set_lastfs", DisplayMode);
                DisplayMode = 2;
                var r = WindowedFallback(NativeResolution);
                ResW = r.x; ResH = r.y;
            }
            ApplyDisplay();
            Save();
        }

        /// <summary>Applies quality preset, VSync, frame cap, anti-aliasing and shadows.</summary>
        public static void ApplyGraphics()
        {
            if (Quality >= 0 && Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Quality)
                QualitySettings.SetQualityLevel(Quality, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FpsCap <= 0 ? -1 : FpsCap;
            // URP reads MSAA and shadow distance from the active pipeline asset, not QualitySettings.
            var urp = ActivePipeline();
            if (urp != null)
            {
                Remember(urp);
                urp.msaaSampleCount = MsaaLevels.Contains(Msaa) && Msaa > 1 ? Msaa : 1;
                urp.shadowDistance = 90f;
            }
            // Shadow on/hard/soft is a per-light setting under URP.
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.shadows = SunShadows;
        }

        /// <summary>Selects a quality preset and adopts its anti-aliasing / shadow defaults.</summary>
        public static void SetQualityPreset(int level)
        {
            Quality = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(Quality, true);
            var urp = ActivePipeline();
            int presetMsaa = urp == null ? 0 : originals.TryGetValue(urp, out var o) ? o.msaa : urp.msaaSampleCount;
            Msaa = presetMsaa > 1 ? presetMsaa : 0;
            Shadows = PresetShadows[Mathf.Clamp(Quality, 0, PresetShadows.Length - 1)];
            ApplyGraphics();
            Save();
        }

        static UniversalRenderPipelineAsset ActivePipeline() =>
            (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;

        // Original values of pipeline assets changed at runtime, so editor play sessions don't leave the
        // project's assets modified.
        static readonly Dictionary<UniversalRenderPipelineAsset, (int msaa, float shadowDistance)> originals =
            new Dictionary<UniversalRenderPipelineAsset, (int, float)>();

        static void Remember(UniversalRenderPipelineAsset a)
        {
            if (originals.ContainsKey(a)) return;
#if UNITY_EDITOR
            if (originals.Count == 0) Application.quitting += RestorePipelines;
#endif
            originals[a] = (a.msaaSampleCount, a.shadowDistance);
        }

        static void RestorePipelines()
        {
            foreach (var kv in originals)
                if (kv.Key != null) { kv.Key.msaaSampleCount = kv.Value.msaa; kv.Key.shadowDistance = kv.Value.shadowDistance; }
            originals.Clear();
            Application.quitting -= RestorePipelines;
        }

        public static string ResName(int w, int h) { var r = EffectiveResolution(w, h); return $"{r.x} x {r.y}" + (w <= 0 ? " (native)" : ""); }

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
