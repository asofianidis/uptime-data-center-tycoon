using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    public enum Overlay { None, Heat, Power, Network, Customer, Age, Airflow }

    public partial class WorldView
    {
        public GameManager gm;
        public Overlay overlay = Overlay.None;

        public bool heatOverlay
        {
            get => overlay == Overlay.Heat;
            set => overlay = value ? Overlay.Heat : (overlay == Overlay.Heat ? Overlay.None : overlay);
        }

        public void SetOverlay(Overlay o) => overlay = o;

        public void CycleOverlay() => overlay = (Overlay)(((int)overlay + 1) % System.Enum.GetValues(typeof(Overlay)).Length);

        public static string OverlayLegend(Overlay o)
        {
            switch (o)
            {
                case Overlay.Heat: return "Hall temperature: blue cool - green ok - yellow warm - red hot";
                case Overlay.Power: return "Rack PDU load: green low - yellow high - red overloaded";
                case Overlay.Network: return "Uplink: red offline - orange degraded - yellow 10G - blue 40G - teal 100G+ - cyan = fully redundant";
                case Overlay.Customer: return "Rack roofs colored by customer (gray = unassigned)";
                case Overlay.Age: return "Average hardware age: green new - yellow 2y - red 4y+ (failure risk)";
                case Overlay.Airflow: return "Airflow: blue = cold-aisle containment, gray = open, red = recirculating hot air";
            }
            return "";
        }

        // ------------------------------------------------------------------ overlay markers on rack roofs
        readonly Dictionary<int, GameObject> overlayMarkers = new Dictionary<int, GameObject>();
        float overlayMarkerTimer;
        Overlay lastOverlay = Overlay.None;

        void UpdateOverlayMarkers()
        {
            overlayMarkerTimer -= Time.unscaledDeltaTime;
            if (overlayMarkerTimer > 0 && overlay == lastOverlay) return;
            lastOverlay = overlay;
            overlayMarkerTimer = 0.2f;
            bool show = overlay != Overlay.None && overlay != Overlay.Heat;
            var alive = new HashSet<int>();
            if (show)
            {
                foreach (var o in sim.S.objects)
                {
                    var d = Catalog.Facility(o.def);
                    if (d.kind != FacilityKind.Rack || !views.TryGetValue(o.id, out var v)) continue;
                    alive.Add(o.id);
                    if (!overlayMarkers.TryGetValue(o.id, out var go) || go == null)
                    {
                        go = new GameObject("Overlay");
                        go.transform.SetParent(transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = markerMesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = new Material(transMat);
                        overlayMarkers[o.id] = go;
                    }
                    go.SetActive(true);
                    go.transform.position = new Vector3(o.x + 0.14f, v.height + 0.01f, o.y + 0.03f);
                    go.transform.localScale = new Vector3(0.72f, 1, 0.94f);
                    var c = OverlayColor(o, d);
                    c.a = 0.85f;
                    go.GetComponent<MeshRenderer>().sharedMaterial.color = c;
                }
            }
            foreach (var kv in overlayMarkers.ToList())
                if (!alive.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value); overlayMarkers.Remove(kv.Key); }
        }

        Color OverlayColor(PlacedObject o, FacilityDef d)
        {
            sim.racks.TryGetValue(o.id, out var info);
            switch (overlay)
            {
                case Overlay.Power:
                {
                    if (info == null) return Color.gray;
                    float f = info.loadKW / d.rackKW;
                    if (f > 1f) return new Color(1f, 0.1f, 0.1f);
                    return f < 0.6f ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(1f, 0.9f, 0.2f), f / 0.6f) : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.4f, 0.1f), (f - 0.6f) / 0.4f);
                }
                case Overlay.Network:
                {
                    if (info == null || info.empty) return new Color(0.35f, 0.35f, 0.35f);
                    if (!info.online) return new Color(1f, 0.15f, 0.1f);
                    if (info.redundant) return new Color(0.3f, 1f, 1f);
                    if (info.degraded) return new Color(1f, 0.55f, 0.1f);
                    float g = info.linkGbps;
                    return g >= 100 ? new Color(0.2f, 0.85f, 0.75f) : g >= 40 ? new Color(0.3f, 0.55f, 1f) : new Color(1f, 0.85f, 0.25f);
                }
                case Overlay.Customer:
                {
                    var c = o.contractId > 0 ? sim.FindContract(o.contractId) : null;
                    return c != null ? Catalog.CustomerColors[c.colorIdx] : new Color(0.3f, 0.3f, 0.32f);
                }
                case Overlay.Age:
                {
                    if (o.devices.Count == 0) return new Color(0.3f, 0.3f, 0.32f);
                    float y = o.devices.Average(x => Simulation.AgeYears(x));
                    return y < 2f ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(1f, 0.9f, 0.2f), y / 2f) : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.15f, 0.1f), (y - 2f) / 2f);
                }
                case Overlay.Airflow:
                {
                    float a = info != null ? info.airflow : 1f;
                    if (a < 1f) return new Color(0.25f, 0.55f, 1f);
                    if (a <= 1.001f) return new Color(0.55f, 0.58f, 0.6f);
                    return Color.Lerp(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.1f, 0.1f), (a - 1.1f) / 0.25f);
                }
            }
            return Color.white;
        }

        // ------------------------------------------------------------------ staff figures
        class StaffView { public GameObject go; public Transform body; public Vector3 lastPos; public float facing; }
        readonly Dictionary<int, StaffView> staffViews = new Dictionary<int, StaffView>();

        public static Color RoleColor(string role)
        {
            switch (role)
            {
                case "Electrician": return new Color(1f, 0.78f, 0.15f);
                case "NetEngineer": return new Color(0.65f, 0.4f, 1f);
                case "Security": return new Color(0.2f, 0.22f, 0.26f);
                default: return new Color(0.2f, 0.55f, 1f);
            }
        }

        void UpdateStaff()
        {
            var alive = new HashSet<int>();
            float t = gm != null ? gm.HourFraction : 1f;
            foreach (var m in sim.S.staff)
            {
                alive.Add(m.id);
                if (!staffViews.TryGetValue(m.id, out var v))
                {
                    v = new StaffView { go = new GameObject("Staff " + m.name) };
                    v.go.transform.SetParent(transform, false);
                    var body = ModelLibrary.Instantiate("person", v.go.transform);
                    if (body == null)
                    {
                        body = new GameObject("Body");
                        body.transform.SetParent(v.go.transform, false);
                        var mb = new MeshBuilder();
                        Models.Person(mb, RoleColor(m.role));
                        body.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh();
                        body.AddComponent<MeshRenderer>().sharedMaterial = litMat;
                    }
                    else ModelLibrary.Tint(body, "Shirt", RoleColor(m.role));
                    v.body = body.transform;
                    staffViews[m.id] = v;
                }
                v.go.SetActive(Settings.ShowStaff && m.state != "off");
                var pos = new Vector3(Mathf.Lerp(m.px, m.x, t), 0, Mathf.Lerp(m.py, m.y, t));
                var delta = pos - v.lastPos;
                if (delta.sqrMagnitude > 0.0001f) v.facing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                v.lastPos = pos;
                v.go.transform.position = pos;
                v.go.transform.rotation = Quaternion.Lerp(v.go.transform.rotation, Quaternion.Euler(0, v.facing, 0), 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                float bob = 0;
                if (m.state == "walking" || m.state == "returning") bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 12f)) * 0.05f;
                else if (m.state == "working") bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 0.03f;
                v.body.localPosition = new Vector3(0, bob, 0);
            }
            foreach (var kv in staffViews.ToList())
                if (!alive.Contains(kv.Key)) { Destroy(kv.Value.go); staffViews.Remove(kv.Key); }
        }

        // ------------------------------------------------------------------ environment: day/night, weather, fire
        ParticleSystem rain;
        Material rainMat;
        readonly Dictionary<int, Light> fireLights = new Dictionary<int, Light>();

        void UpdateEnvironment()
        {
            var S = sim.S;
            var sun = gm != null ? gm.sun : null;
            float hour = (S.hour % 24) + (gm != null ? gm.HourFraction : 0f);
            if (sun != null)
            {
                float weatherDim = S.weather == "Storm" ? 0.55f : S.weather == "Rain" || S.weather == "Snow" ? 0.7f : S.weather == "Cloudy" ? 0.82f : 1f;
                if (!Settings.DayNight) hour = 13f;
                float day = Mathf.Clamp01(Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
                float elev = Mathf.Lerp(12f, 62f, day);
                float az = Mathf.Lerp(-80f, 80f, Mathf.InverseLerp(6f, 18f, hour)) - 20f;
                sun.transform.rotation = Quaternion.Euler(hour < 6 || hour > 18 ? 35f : elev, az, 0);
                float dawn = Mathf.Clamp01(1f - Mathf.Abs(hour - 6.5f) / 1.5f) + Mathf.Clamp01(1f - Mathf.Abs(hour - 18f) / 1.5f);
                Color daylight = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.65f, 0.4f), Mathf.Clamp01(dawn));
                Color night = new Color(0.45f, 0.55f, 0.85f);
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((day + 0.15f) * 1.6f));
                sun.color = Color.Lerp(night, daylight, k);
                sun.intensity = Mathf.Lerp(0.28f, 1.15f, k) * weatherDim;
                float amb = Mathf.Lerp(0.42f, 1f, k) * Mathf.Lerp(0.85f, 1f, weatherDim);
                RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.68f) * amb + (1 - k) * new Color(0.02f, 0.03f, 0.08f);
                RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.46f) * amb;
                RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.25f) * amb;
                var cam = Camera.main;
                if (cam != null) cam.backgroundColor = Color.Lerp(new Color(0.03f, 0.05f, 0.1f), new Color(0.12f, 0.16f, 0.2f), k);
            }
            UpdateWeatherFx();
            UpdateFires();
        }

        void UpdateWeatherFx()
        {
            string w = sim.S.weather;
            bool wet = Settings.WeatherFx && (w == "Rain" || w == "Storm" || w == "Snow");
            if (rain == null)
            {
                if (!wet) return;
                var go = new GameObject("Weather");
                go.transform.SetParent(transform, false);
                rain = go.AddComponent<ParticleSystem>();
                rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = rain.main;
                main.loop = true;
                main.startLifetime = 1.4f;
                main.maxParticles = 4000;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var shape = rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(55, 1, 55);
                var r = go.GetComponent<ParticleSystemRenderer>();
                rainMat = new Material(transMat);
                r.sharedMaterial = rainMat;
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 3f;
            }
            if (!wet) { if (rain.isPlaying) rain.Stop(); return; }
            var focus = gm != null ? gm.cam.focus : Vector3.zero;
            rain.transform.position = focus + Vector3.up * 22f;
            var m = rain.main;
            var em = rain.emission;
            if (w == "Snow")
            {
                m.startSpeed = 4f; m.startSize = 0.12f; m.startLifetime = 6f; em.rateOverTime = 500;
                rain.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
                rainMat.color = new Color(1, 1, 1, 0.8f);
            }
            else
            {
                m.startSpeed = 24f; m.startSize = 0.04f; m.startLifetime = 1.2f; em.rateOverTime = w == "Storm" ? 2600 : 1300;
                rain.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
                rainMat.color = new Color(0.75f, 0.85f, 1f, 0.45f);
            }
            rain.transform.rotation = Quaternion.Euler(90f, 0, 0);
            if (!rain.isPlaying) rain.Play();
        }

        void UpdateFires()
        {
            var active = new HashSet<int>();
            foreach (var e in sim.S.events)
            {
                if (e.type != "fire") continue;
                int id = Mathf.RoundToInt(e.magnitude);
                if (!sim.objById.TryGetValue(id, out var o)) continue;
                active.Add(id);
                if (!fireLights.TryGetValue(id, out var l) || l == null)
                {
                    var go = new GameObject("Fire");
                    go.transform.SetParent(transform, false);
                    l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.45f, 0.1f);
                    l.range = 6f;
                    fireLights[id] = l;
                }
                l.transform.position = new Vector3(o.x + 0.5f, 2.4f, o.y + 0.5f);
                l.intensity = 3f + Mathf.PerlinNoise(Time.unscaledTime * 8f, id) * 4f;
            }
            foreach (var kv in fireLights.ToList())
                if (!active.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value.gameObject); fireLights.Remove(kv.Key); }
        }
    }

    /// <summary>Optional Blender-made models in Resources/Models. Falls back to procedural meshes if missing.</summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        public static GameObject Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!cache.TryGetValue(name, out var go))
            {
                go = Resources.Load<GameObject>("Models/" + name);
                cache[name] = go;
            }
            return go;
        }

        public static GameObject Instantiate(string name, Transform parent)
        {
            var prefab = Get(name);
            if (prefab == null) return null;
            var inst = Object.Instantiate(prefab, parent, false);
            inst.name = name;
            // Blender export puts the model's front at +Z; the game's convention is front = -Z.
            inst.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return inst;
        }

        /// <summary>Recolors renderers whose material name contains the given part name.</summary>
        public static void Tint(GameObject go, string part, Color c)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m.name.Contains(part)) m.color = c;
        }

        public static float Height(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 1f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.max.y - go.transform.position.y;
        }
    }
}
