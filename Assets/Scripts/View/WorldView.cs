using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    public partial class WorldView : MonoBehaviour
    {
        public Simulation sim;
        public bool showCables = true;
        public bool showGrid;
        public int highlightContract = -1;

        Material litMat, transMat, unlitMat;
        GameObject groundGO, wallsGO, sceneryGO, gridGO, traysGO, hintGO, massGO;
        Mesh groundMesh, wallsMesh, sceneryMesh, gridMesh, traysMesh, hintMesh, massMesh;
        Transform floorRoot;
        public float FloorY => sim != null ? sim.S.floor * GameState.FloorHeight : 0f;
        Material hintMat;
        Color[] groundColors;
        Color[] baseGroundColors;
        int builtTilesVersion = -1, builtObjectsVersion = -1;

        class ObjView
        {
            public GameObject go;
            public Mesh mesh;
            public Transform led;
            public Material ledMat;
            public LineRenderer cable, cable2;
            public float height;
            public bool hasModel;
        }
        readonly Dictionary<int, ObjView> views = new Dictionary<int, ObjView>();

        // Preview objects
        GameObject ghostGO, rectGO;
        Mesh ghostMesh, rectMesh;
        Material ghostMat, rectMat;
        string ghostDef;
        int ghostRot = -1;
        readonly List<GameObject> selectionMarkers = new List<GameObject>();
        Mesh markerMesh;

        public void Init(Simulation s)
        {
            sim = s;
            litMat = new Material(Shader.Find("DC/Lit"));
            transMat = new Material(Shader.Find("DC/UnlitTransparent"));
            unlitMat = new Material(Shader.Find("DC/Unlit"));
            floorRoot = new GameObject("FloorRoot").transform;
            floorRoot.SetParent(transform, false);
            groundGO = MakeGO("Ground", litMat, out groundMesh);
            groundGO.transform.SetParent(floorRoot, false);
            massGO = MakeGO("LowerFloors", litMat, out massMesh);
            wallsGO = MakeGO("Walls", litMat, out wallsMesh);
            wallsGO.transform.SetParent(floorRoot, false);
            sceneryGO = MakeGO("Scenery", litMat, out sceneryMesh);
            gridGO = MakeGO("Grid", transMat, out gridMesh);
            gridGO.transform.SetParent(floorRoot, false);
            traysGO = MakeGO("Trays", litMat, out traysMesh);
            traysGO.transform.SetParent(floorRoot, false);
            hintGO = MakeGO("Hint", transMat, out hintMesh);
            hintGO.transform.SetParent(floorRoot, false);
            hintMat = hintGO.GetComponent<MeshRenderer>().material;
            hintGO.SetActive(false);
            gridGO.GetComponent<MeshRenderer>().material.color = new Color(1, 1, 1, 0.12f);
            groundGO.GetComponent<MeshRenderer>().material.SetFloat("_Glossiness", 0.1f);

            ghostGO = MakeGO("Ghost", transMat, out ghostMesh);
            ghostGO.transform.SetParent(floorRoot, false);
            ghostMat = ghostGO.GetComponent<MeshRenderer>().material;
            ghostGO.SetActive(false);
            rectGO = MakeGO("DragRect", transMat, out rectMesh);
            rectGO.transform.SetParent(floorRoot, false);
            rectMat = rectGO.GetComponent<MeshRenderer>().material;
            rectGO.SetActive(false);

            var mb = new MeshBuilder();
            mb.FlatQuad(0, 0, 1, 1, 0.03f, Color.white);
            markerMesh = mb.ToMesh();
            Rebuild();
        }

        public void ResetSim(Simulation s)
        {
            sim = s;
            foreach (var v in views.Values) Destroy(v.go);
            views.Clear();
            foreach (var v in staffViews.Values) Destroy(v.go);
            staffViews.Clear();
            foreach (var g in overlayMarkers.Values) if (g != null) Destroy(g);
            overlayMarkers.Clear();
            builtTilesVersion = -1; builtObjectsVersion = -1;
            Rebuild();
        }

        GameObject MakeGO(string name, Material mat, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mesh = new Mesh();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.material = mat;
            return go;
        }

        public static Vector3 TileCenter(int x, int y) => new Vector3(x + 0.5f, 0, y + 0.5f);

        public static Vector3 ObjectCenter(FacilityDef d, int x, int y, int rot)
        {
            Simulation.Footprint(d, rot, out int w, out int h);
            return new Vector3(x + w * 0.5f, 0, y + h * 0.5f);
        }

        void Rebuild()
        {
            if (builtTilesVersion != sim.tilesVersion) { BuildGround(); BuildWalls(); BuildScenery(); BuildGrid(); builtTilesVersion = sim.tilesVersion; }
            SyncObjects(true);
        }

        void LateUpdate()
        {
            if (sim == null) return;
            floorRoot.localPosition = new Vector3(0, FloorY, 0);
            if (builtTilesVersion != sim.tilesVersion) { BuildGround(); BuildWalls(); BuildScenery(); BuildGrid(); builtTilesVersion = sim.tilesVersion; }
            SyncObjects(builtObjectsVersion != sim.objectsVersion);
            UpdateGroundOverlay();
            UpdateStatusLights();
            UpdateOverlayMarkers();
            UpdateStaff();
            UpdateEnvironment();
            gridGO.SetActive(showGrid);
            traysGO.SetActive(showCables || (gm != null && gm.tool == Tool.Tray));
            if (hintGO.activeSelf) hintMat.color = new Color(1f, 0.85f, 0.2f, 0.25f + 0.25f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
        }

        // ------------------------------------------------------------------ ground
        void BuildGround()
        {
            var S = sim.S;
            var mb = new MeshBuilder();
            var r = new System.Random(S.seed);
            // Big skirt around the map so the camera never sees the void.
            float fy = FloorY;
            mb.FlatQuad(-60, -60, S.mapW + 60, S.mapH + 60, -0.02f - fy, new Color(0.2f, 0.33f, 0.17f));
            for (int y = 0; y < S.mapH; y++)
                for (int x = 0; x < S.mapW; x++)
                {
                    // Tile height relative to the viewed floor.
                    float h = 0f;
                    if (S.RoomAt(S.floor, x, y) == RoomType.None)
                    {
                        int top = -1;
                        for (int f = S.floor - 1; f >= 0; f--) if (S.RoomAt(f, x, y) != RoomType.None) { top = f; break; }
                        h = (top >= 0 ? (top + 1) * GameState.FloorHeight : 0f) - fy;
                    }
                    mb.FlatQuad(x, y, x + 1, y + 1, h, Color.white);
                }
            mb.ToMesh(groundMesh);
            baseGroundColors = new Color[groundMesh.vertexCount];
            groundColors = new Color[groundMesh.vertexCount];
            for (int k = 0; k < 4; k++) baseGroundColors[k] = new Color(0.2f, 0.33f, 0.17f);
            for (int y = 0; y < S.mapH; y++)
                for (int x = 0; x < S.mapW; x++)
                {
                    int i = y * S.mapW + x;
                    var room = S.Room(x, y);
                    Color c;
                    float n = (float)r.NextDouble();
                    bool roof = room == RoomType.None && S.floor > 0 && Enumerable.Range(0, S.floor).Any(f => S.RoomAt(f, x, y) != RoomType.None);
                    if (roof)
                    {
                        c = new Color(0.5f + n * 0.03f, 0.52f + n * 0.03f, 0.55f);
                    }
                    else if (room == RoomType.None)
                    {
                        bool owned = S.Owned(x, y);
                        c = owned ? new Color(0.36f + n * 0.04f, 0.56f + n * 0.05f, 0.27f) : new Color(0.24f + n * 0.03f, 0.38f + n * 0.04f, 0.2f);
                        if (owned && (x == S.ownX0 || y == S.ownY0 || x == S.ownX1 - 1 || y == S.ownY1 - 1)) c = MeshBuilder.Shade(c, 0.85f);
                    }
                    else if (room == RoomType.ServerHall && S.HasFlag(x, y, GameState.FlagRaised))
                    {
                        // Raised floor: brighter perforated tiles.
                        c = ((x + y) & 1) == 0 ? new Color(0.88f, 0.9f, 0.95f) : new Color(0.8f, 0.84f, 0.92f);
                    }
                    else
                    {
                        c = Catalog.RoomColor(room);
                        if (room == RoomType.ServerHall && ((x + y) & 1) == 0) c = MeshBuilder.Shade(c, 0.94f);
                        else if (room != RoomType.ServerHall) c = MeshBuilder.Shade(c, 0.97f + n * 0.03f);
                    }
                    c.a = 1;
                    int v = 4 + i * 4;
                    for (int k = 0; k < 4; k++) baseGroundColors[v + k] = c;
                }
            System.Array.Copy(baseGroundColors, groundColors, baseGroundColors.Length);
            groundMesh.colors = groundColors;
        }

        float overlayTimer;
        void UpdateGroundOverlay()
        {
            overlayTimer -= Time.unscaledDeltaTime;
            if (overlayTimer > 0) return;
            overlayTimer = 0.25f;
            var S = sim.S;
            bool changed = false;
            int plane = S.mapW * S.mapH;
            if (baseGroundColors == null || baseGroundColors.Length < 4 + plane * 4) return;
            for (int i = 0; i < plane; i++)
            {
                int v = 4 + i * 4;
                Color c = baseGroundColors[v];
                int fi = S.floor * plane + i;
                if (heatOverlay && S.rooms[fi] == (int)RoomType.ServerHall && sim.zoneOf != null && sim.zoneOf[fi] >= 0 && sim.zoneOf[fi] < sim.zones.Count)
                {
                    float t = sim.zones[sim.zoneOf[fi]].temp;
                    c = HeatColor(t);
                }
                if (groundColors[v] != c) { for (int k = 0; k < 4; k++) groundColors[v + k] = c; changed = true; }
            }
            if (changed) groundMesh.colors = groundColors;
        }

        public static Color HeatColor(float t)
        {
            if (t < 24) return Color.Lerp(new Color(0.3f, 0.5f, 1f), new Color(0.3f, 0.85f, 0.5f), Mathf.InverseLerp(16, 24, t));
            if (t < 32) return Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(1f, 0.85f, 0.2f), Mathf.InverseLerp(24, 32, t));
            return Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.15f, 0.1f), Mathf.InverseLerp(32, 45, t));
        }

        void BuildWalls()
        {
            var S = sim.S;
            var mb = new MeshBuilder();
            Color ext = new Color(0.82f, 0.82f, 0.8f);
            const float extH = 1.1f, extT = 0.14f, intH = 0.55f, intT = 0.08f;
            for (int y = 0; y < S.mapH; y++)
                for (int x = 0; x < S.mapW; x++)
                {
                    var room = S.Room(x, y);
                    if (room == RoomType.None) continue;
                    // Exterior walls: any edge that faces open land.
                    if (S.Room(x, y - 1) == RoomType.None) mb.Box(new Vector3(x + 0.5f, extH * 0.5f, y + extT * 0.5f), new Vector3(1f, extH, extT), ext);
                    if (S.Room(x, y + 1) == RoomType.None) mb.Box(new Vector3(x + 0.5f, extH * 0.5f, y + 1 - extT * 0.5f), new Vector3(1f, extH, extT), ext);
                    if (S.Room(x - 1, y) == RoomType.None) mb.Box(new Vector3(x + extT * 0.5f, extH * 0.5f, y + 0.5f), new Vector3(extT, extH, 1f), ext);
                    if (S.Room(x + 1, y) == RoomType.None) mb.Box(new Vector3(x + 1 - extT * 0.5f, extH * 0.5f, y + 0.5f), new Vector3(extT, extH, 1f), ext);
                    // Interior partitions between different room types (each edge once). Leave doorways every few tiles.
                    var east = S.Room(x + 1, y);
                    if (east != RoomType.None && east != room && !Doorway(x, y, true))
                        mb.Box(new Vector3(x + 1, intH * 0.5f, y + 0.5f), new Vector3(intT, intH, 1f), WallColor(room, east));
                    var north = S.Room(x, y + 1);
                    if (north != RoomType.None && north != room && !Doorway(x, y, false))
                        mb.Box(new Vector3(x + 0.5f, intH * 0.5f, y + 1), new Vector3(1f, intH, intT), WallColor(room, north));
                }
            mb.ToMesh(wallsMesh);
            BuildMass();
            BuildTrays();
        }

        /// <summary>Floors below the viewed one, drawn as solid storeys with windows.</summary>
        void BuildMass()
        {
            var S = sim.S;
            var mb = new MeshBuilder();
            Color wall = new Color(0.74f, 0.75f, 0.76f), band = new Color(0.25f, 0.35f, 0.45f);
            float H = GameState.FloorHeight;
            for (int f = 0; f < S.floor; f++)
                for (int y = 0; y < S.mapH; y++)
                    for (int x = 0; x < S.mapW; x++)
                    {
                        if (S.RoomAt(f, x, y) == RoomType.None) continue;
                        float y0 = f * H;
                        void Face(int nx, int ny, Vector3 c, Vector3 size)
                        {
                            if (S.RoomAt(f, nx, ny) != RoomType.None) return;
                            mb.Box(c + new Vector3(0, y0 + H * 0.5f, 0), size + new Vector3(0, H, 0), wall);
                            mb.Box(c + new Vector3(0, y0 + H * 0.62f, 0), size * 1.05f + new Vector3(0, 0.7f, 0), band);
                        }
                        Face(x, y - 1, new Vector3(x + 0.5f, 0, y + 0.07f), new Vector3(1f, 0, 0.14f));
                        Face(x, y + 1, new Vector3(x + 0.5f, 0, y + 0.93f), new Vector3(1f, 0, 0.14f));
                        Face(x - 1, y, new Vector3(x + 0.07f, 0, y + 0.5f), new Vector3(0.14f, 0, 1f));
                        Face(x + 1, y, new Vector3(x + 0.93f, 0, y + 0.5f), new Vector3(0.14f, 0, 1f));
                    }
            mb.ToMesh(massMesh);
        }

        bool Doorway(int x, int y, bool vertical)
        {
            // Door where the boundary run hits a multiple of 5 on the running axis.
            int along = vertical ? y : x;
            return along % 5 == 2;
        }

        static Color WallColor(RoomType a, RoomType b)
        {
            var c = Color.Lerp(Catalog.RoomColor(a), Catalog.RoomColor(b), 0.5f);
            return MeshBuilder.Shade(c, 0.75f);
        }

        void BuildScenery()
        {
            var S = sim.S;
            var mb = new MeshBuilder();
            for (int y = 0; y < S.mapH; y++)
                for (int x = 0; x < S.mapW; x++)
                {
                    int o = S.obstacles[y * S.mapW + x];
                    if (o == 0) continue;
                    var r = new System.Random(S.seed + x * 7349 + y * 1931);
                    var p = TileCenter(x, y) + new Vector3(((float)r.NextDouble() - 0.5f) * 0.3f, 0, ((float)r.NextDouble() - 0.5f) * 0.3f);
                    if (o == 1) Models.Tree(mb, p, r); else Models.Rock(mb, p, r);
                }
            // Owned-land boundary fence
            Color fence = new Color(0.55f, 0.5f, 0.42f);
            for (int x = S.ownX0; x < S.ownX1; x++)
            {
                mb.Box(new Vector3(x + 0.5f, 0.25f, S.ownY0 + 0.02f), new Vector3(1f, 0.04f, 0.03f), fence);
                mb.Box(new Vector3(x + 0.5f, 0.25f, S.ownY1 - 0.02f), new Vector3(1f, 0.04f, 0.03f), fence);
                mb.Box(new Vector3(x, 0.17f, S.ownY0 + 0.02f), new Vector3(0.05f, 0.34f, 0.05f), fence);
                mb.Box(new Vector3(x, 0.17f, S.ownY1 - 0.02f), new Vector3(0.05f, 0.34f, 0.05f), fence);
            }
            for (int y = S.ownY0; y < S.ownY1; y++)
            {
                mb.Box(new Vector3(S.ownX0 + 0.02f, 0.25f, y + 0.5f), new Vector3(0.03f, 0.04f, 1f), fence);
                mb.Box(new Vector3(S.ownX1 - 0.02f, 0.25f, y + 0.5f), new Vector3(0.03f, 0.04f, 1f), fence);
                mb.Box(new Vector3(S.ownX0 + 0.02f, 0.17f, y), new Vector3(0.05f, 0.34f, 0.05f), fence);
                mb.Box(new Vector3(S.ownX1 - 0.02f, 0.17f, y), new Vector3(0.05f, 0.34f, 0.05f), fence);
            }
            // Access road leading to the lot
            mb.FlatQuad(S.ownX0 - 0.0f, -60, S.ownX0 + 2.5f, S.ownY0, 0.01f, new Color(0.25f, 0.25f, 0.27f));
            mb.ToMesh(sceneryMesh);
        }

        void BuildGrid()
        {
            var S = sim.S;
            var verts = new List<Vector3>();
            var idx = new List<int>();
            var cols = new List<Color>();
            for (int x = S.ownX0; x <= S.ownX1; x++) { verts.Add(new Vector3(x, 0.02f, S.ownY0)); verts.Add(new Vector3(x, 0.02f, S.ownY1)); }
            for (int y = S.ownY0; y <= S.ownY1; y++) { verts.Add(new Vector3(S.ownX0, 0.02f, y)); verts.Add(new Vector3(S.ownX1, 0.02f, y)); }
            for (int i = 0; i < verts.Count; i++) { idx.Add(i); cols.Add(Color.white); }
            gridMesh.Clear();
            gridMesh.SetVertices(verts);
            gridMesh.SetColors(cols);
            gridMesh.SetIndices(idx, MeshTopology.Lines, 0);
        }

        // ------------------------------------------------------------------ objects
        void SyncObjects(bool full)
        {
            var S = sim.S;
            if (full)
            {
                var alive = new HashSet<int>();
                foreach (var o in S.objects)
                {
                    alive.Add(o.id);
                    if (!views.ContainsKey(o.id)) { CreateView(o); sim.dirtyObjects.Remove(o.id); }
                }
                foreach (var id in views.Keys.ToList())
                    if (!alive.Contains(id)) { Destroy(views[id].go); views.Remove(id); }
                builtObjectsVersion = sim.objectsVersion;
                foreach (var o in S.objects) UpdateCable(o);
            }
            if (sim.dirtyObjects.Count > 0)
            {
                foreach (var id in sim.dirtyObjects.ToList())
                    if (sim.objById.TryGetValue(id, out var o) && views.TryGetValue(id, out var v))
                    {
                        RebuildMesh(o, v);
                        UpdateCable(o);
                    }
                sim.dirtyObjects.Clear();
            }
        }

        public const float TrayHeight = 2.32f;

        void BuildTrays()
        {
            var S = sim.S;
            var mb = new MeshBuilder();
            Color steel = new Color(0.62f, 0.64f, 0.68f);
            Color rail = new Color(0.95f, 0.75f, 0.15f);
            if (S.flags != null)
                for (int y = 0; y < S.mapH; y++)
                    for (int x = 0; x < S.mapW; x++)
                    {
                        if (!S.HasFlag(x, y, GameState.FlagTray)) continue;
                        var c = new Vector3(x + 0.5f, TrayHeight, y + 0.5f);
                        mb.Box(c, new Vector3(0.34f, 0.04f, 0.34f), steel);
                        if (S.HasFlag(x + 1, y, GameState.FlagTray)) mb.Box(c + new Vector3(0.5f, 0, 0), new Vector3(0.66f, 0.04f, 0.3f), steel);
                        if (S.HasFlag(x, y + 1, GameState.FlagTray)) mb.Box(c + new Vector3(0, 0, 0.5f), new Vector3(0.3f, 0.04f, 0.66f), steel);
                        mb.Box(c + new Vector3(0, 0.04f, 0.16f), new Vector3(0.34f, 0.05f, 0.02f), rail);
                        mb.Box(c + new Vector3(0, 0.04f, -0.16f), new Vector3(0.34f, 0.05f, 0.02f), rail);
                        // Hanger rod
                        mb.Box(c + new Vector3(0, 0.35f, 0), new Vector3(0.02f, 0.7f, 0.02f), steel);
                    }
            mb.ToMesh(traysMesh);
        }

        public void ShowHint(RectInt r)
        {
            if (!hintGO.activeSelf || lastHint != r)
            {
                var mb = new MeshBuilder();
                float t = 0.12f;
                mb.FlatQuad(r.xMin, r.yMin, r.xMax, r.yMax, 0.07f, new Color(1, 1, 1, 0.35f));
                mb.FlatQuad(r.xMin, r.yMin, r.xMax, r.yMin + t, 0.08f, Color.white);
                mb.FlatQuad(r.xMin, r.yMax - t, r.xMax, r.yMax, 0.08f, Color.white);
                mb.FlatQuad(r.xMin, r.yMin, r.xMin + t, r.yMax, 0.08f, Color.white);
                mb.FlatQuad(r.xMax - t, r.yMin, r.xMax, r.yMax, 0.08f, Color.white);
                mb.ToMesh(hintMesh);
                lastHint = r;
            }
            hintGO.SetActive(true);
        }

        RectInt lastHint;

        public void HideHint() { if (hintGO != null) hintGO.SetActive(false); }

        public void ForceRebuildObject(int id)
        {
            if (views.TryGetValue(id, out var v)) { Destroy(v.go); views.Remove(id); }
            builtObjectsVersion = -1;
        }

        void CreateView(PlacedObject o)
        {
            var d = Catalog.Facility(o.def);
            var v = new ObjView();
            v.go = new GameObject($"{d.name} #{o.id}");
            v.go.transform.SetParent(floorRoot, false);
            v.go.transform.localPosition = ObjectCenter(d, o.x, o.y, o.rot);
            v.go.transform.rotation = Quaternion.Euler(0, o.rot * 90f, 0);
            v.mesh = new Mesh();
            v.go.AddComponent<MeshFilter>().sharedMesh = v.mesh;
            v.go.AddComponent<MeshRenderer>().sharedMaterial = litMat;

            var led = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(led.GetComponent<Collider>());
            led.name = "StatusLight";
            led.transform.SetParent(v.go.transform, false);
            led.transform.localScale = new Vector3(0.14f, 0.05f, 0.14f);
            v.ledMat = new Material(litMat);
            led.GetComponent<MeshRenderer>().sharedMaterial = v.ledMat;
            v.led = led.transform;
            views[o.id] = v;
            if (d.kind != FacilityKind.Rack)
            {
                var model = ModelLibrary.Instantiate(string.IsNullOrEmpty(d.model) ? d.id : d.model, v.go.transform);
                if (model != null)
                {
                    v.hasModel = true;
                    v.height = ModelLibrary.Height(model);
                    v.led.localPosition = new Vector3(0, v.height + 0.04f, 0);
                    return;
                }
            }
            RebuildMesh(o, v);
        }

        void RebuildMesh(PlacedObject o, ObjView v)
        {
            if (v.hasModel) return;
            var d = Catalog.Facility(o.def);
            var mb = new MeshBuilder();
            v.height = Models.Build(mb, d, o, sim);
            mb.ToMesh(v.mesh);
            v.led.localPosition = new Vector3(d.kind == FacilityKind.Rack ? 0.18f : 0f, v.height + 0.04f, d.kind == FacilityKind.Rack ? 0.32f : 0f);
        }

        void UpdateCable(PlacedObject o)
        {
            if (!views.TryGetValue(o.id, out var v)) return;
            UpdateCableLine(o, v, o.linkCore, ref v.cable, 0f);
            UpdateCableLine(o, v, o.linkCore2, ref v.cable2, 0.12f);
        }

        void UpdateCableLine(PlacedObject o, ObjView v, int coreId, ref LineRenderer line, float extraOffset)
        {
            PlacedObject core = coreId > 0 && sim.objById.TryGetValue(coreId, out var c) ? c : null;
            if (core == null)
            {
                if (line != null) { Destroy(line.gameObject); line = null; }
                return;
            }
            if (line == null)
            {
                var go = new GameObject("Cable");
                go.transform.SetParent(v.go.transform, false);
                line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = unlitMat;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.06f;
                line.numCornerVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var fyv = Vector3.up * (o.floor * GameState.FloorHeight);
            var a = ObjectCenter(Catalog.Facility(o.def), o.x, o.y, o.rot) + fyv;
            var b = ObjectCenter(Catalog.Facility(core.def), core.x, core.y, core.rot) + fyv;
            if (core.floor != o.floor)
            {
                var route = sim.CableRouteFor(o, core);
                if (route.shaftRackFloor != null)
                {
                    var sh = route.shaftRackFloor;
                    b = new Vector3(sh.x + 0.5f, 0, sh.y + 0.5f) + fyv;
                    core = sh;   // draw to the riser; the vertical run continues inside the shaft
                }
            }
            float tray = 2.45f + (o.id % 5) * 0.04f + extraOffset + o.floor * GameState.FloorHeight;
            var trayPath = sim.TrayPath(o, core);
            if (trayPath != null && trayPath.Count > 0)
            {
                float ty = TrayHeight + 0.06f + extraOffset * 0.3f;
                var list = new List<Vector3> { a + Vector3.up * views[o.id].height };
                foreach (var t in trayPath) list.Add(new Vector3(t.x + 0.5f, ty, t.y + 0.5f) + fyv);
                list.Add(b + Vector3.up * (views.TryGetValue(core.id, out var cv0) ? cv0.height : 2f));
                line.positionCount = list.Count;
                line.SetPositions(list.ToArray());
                sim.racks.TryGetValue(o.id, out var info0);
                float g0 = info0 != null ? info0.linkGbps : 10f;
                Color col0 = g0 >= 400 ? new Color(0.85f, 0.4f, 1f) : g0 >= 100 ? new Color(0.2f, 0.95f, 0.9f) : g0 >= 40 ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.75f, 0.2f);
                if (extraOffset > 0) col0 = Color.Lerp(col0, Color.white, 0.35f);
                line.startColor = line.endColor = col0;
                return;
            }
            float ha = views[o.id].height, hb = views.TryGetValue(core.id, out var cv) ? cv.height : 2f;
            float off = ((o.id % 7) - 3) * 0.05f + extraOffset;
            var pts = new[]
            {
                a + Vector3.up * ha,
                new Vector3(a.x, tray, a.z),
                new Vector3(a.x, tray, b.z + off),
                new Vector3(b.x + off, tray, b.z + off),
                b + Vector3.up * hb,
            };
            line.positionCount = pts.Length;
            line.SetPositions(pts);
            sim.racks.TryGetValue(o.id, out var info);
            float g = info != null ? info.linkGbps : 10f;
            Color col = g >= 400 ? new Color(0.85f, 0.4f, 1f) : g >= 100 ? new Color(0.2f, 0.95f, 0.9f) : g >= 40 ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.75f, 0.2f);
            if (extraOffset > 0) col = Color.Lerp(col, Color.white, 0.35f);
            line.startColor = line.endColor = col;
        }

        void UpdateStatusLights()
        {
            float blink = Mathf.PingPong(Time.unscaledTime * 3f, 1f) > 0.5f ? 1f : 0.25f;
            foreach (var o in sim.S.objects)
            {
                if (!views.TryGetValue(o.id, out var v)) continue;
                bool here = o.floor == sim.S.floor;
                if (v.go.activeSelf != here) v.go.SetActive(here);
                if (!here) continue;
                if (v.cable != null) v.cable.enabled = showCables;
                if (v.cable2 != null) v.cable2.enabled = showCables;
                Color c; bool flash = false;
                var d = Catalog.Facility(o.def);
                if (d.kind == FacilityKind.Rack)
                {
                    sim.racks.TryGetValue(o.id, out var info);
                    if (info == null || info.empty) c = new Color(0.35f, 0.35f, 0.35f);
                    else if (!info.online) { c = new Color(1f, 0.15f, 0.1f); flash = true; }
                    else if (info.failedDevices > 0 || info.servers > info.ports) c = new Color(1f, 0.65f, 0.1f);
                    else if (o.contractId < 0) c = new Color(0.3f, 0.6f, 1f);
                    else c = new Color(0.2f, 1f, 0.3f);
                    if (highlightContract > 0 && o.contractId == highlightContract) { c = Color.white; flash = true; }
                }
                else if (o.failed || o.pduTripped) { c = new Color(1f, 0.15f, 0.1f); flash = true; }
                else c = new Color(0.2f, 1f, 0.3f);
                float k = flash ? blink : 1f;
                v.ledMat.color = c * 0.4f;
                v.ledMat.SetColor("_Emission", c * k * 1.4f);
            }
        }

        // ------------------------------------------------------------------ previews
        public void ShowGhost(FacilityDef d, int x, int y, int rot, bool ok)
        {
            if (d == null) { ghostGO.SetActive(false); return; }
            ghostGO.SetActive(true);
            if (ghostDef != d.id || ghostRot != rot)
            {
                var mb = new MeshBuilder();
                Models.BuildPreview(mb, d, sim);
                Simulation.Footprint(d, 0, out int w, out int h);
                mb.FlatQuad(-w * 0.5f, -h * 0.5f, w * 0.5f, h * 0.5f, 0.04f, Color.white);
                mb.ToMesh(ghostMesh);
                ghostDef = d.id; ghostRot = rot;
            }
            ghostGO.transform.localPosition = ObjectCenter(d, x, y, rot);
            ghostGO.transform.rotation = Quaternion.Euler(0, rot * 90f, 0);
            ghostMat.color = ok ? new Color(0.4f, 1f, 0.5f, 0.55f) : new Color(1f, 0.3f, 0.25f, 0.55f);
        }

        public void HideGhost() => ghostGO.SetActive(false);

        public void ShowRect(RectInt r, Color c)
        {
            rectGO.SetActive(true);
            var mb = new MeshBuilder();
            mb.FlatQuad(r.xMin, r.yMin, r.xMax, r.yMax, 0.05f, Color.white);
            float t = 0.06f;
            mb.FlatQuad(r.xMin, r.yMin, r.xMax, r.yMin + t, 0.06f, Color.white);
            mb.FlatQuad(r.xMin, r.yMax - t, r.xMax, r.yMax, 0.06f, Color.white);
            mb.FlatQuad(r.xMin, r.yMin, r.xMin + t, r.yMax, 0.06f, Color.white);
            mb.FlatQuad(r.xMax - t, r.yMin, r.xMax, r.yMax, 0.06f, Color.white);
            mb.ToMesh(rectMesh);
            rectMat.color = c;
        }

        public void HideRect() => rectGO.SetActive(false);

        public void ShowSelection(IList<PlacedObject> objs, Color c)
        {
            while (selectionMarkers.Count < objs.Count)
            {
                var go = new GameObject("SelMarker");
                go.transform.SetParent(floorRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = markerMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = new Material(transMat);
                selectionMarkers.Add(go);
            }
            for (int i = 0; i < selectionMarkers.Count; i++)
            {
                var go = selectionMarkers[i];
                if (i >= objs.Count) { go.SetActive(false); continue; }
                var o = objs[i];
                var d = Catalog.Facility(o.def);
                if (d == null) { go.SetActive(false); continue; }
                Simulation.Footprint(d, o.rot, out int w, out int h);
                go.SetActive(true);
                go.transform.localPosition = new Vector3(o.x - 0.08f, 0, o.y - 0.08f);
                if (o.floor != sim.S.floor) { go.SetActive(false); continue; }
                go.transform.localScale = new Vector3(w + 0.16f, 1, h + 0.16f);
                go.GetComponent<MeshRenderer>().sharedMaterial.color = new Color(c.r, c.g, c.b, 0.35f + 0.2f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
            }
        }
    }
}
