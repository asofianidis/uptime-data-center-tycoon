using System.Collections.Generic;
using UnityEngine;

namespace DCT
{
    /// <summary>Accumulates colored primitive shapes into a single mesh. Vertex alpha &lt; 1 renders as emissive (LEDs).</summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;

        public void Clear() { verts.Clear(); normals.Clear(); colors.Clear(); tris.Clear(); }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++) { normals.Add(n); colors.Add(col); }
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        /// <summary>Horizontal quad facing up.</summary>
        public void FlatQuad(float x0, float z0, float x1, float z1, float y, Color c)
        {
            Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, c);
        }

        public void Box(Vector3 center, Vector3 size, Color c, bool bottom = false) => Box(center, size, c, Quaternion.identity, bottom);

        public void Box(Vector3 center, Vector3 size, Color c, Quaternion rot, bool bottom = false)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 N(Vector3 n) => rot * n;
            // top
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), N(Vector3.up), c);
            // front (-z)
            Color side = Shade(c, 0.92f);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), N(Vector3.back), side);
            // back (+z)
            Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), N(Vector3.forward), side);
            // left (-x)
            Color side2 = Shade(c, 0.85f);
            Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), N(Vector3.left), side2);
            // right (+x)
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), N(Vector3.right), side2);
            if (bottom) Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), N(Vector3.down), c);
        }

        /// <summary>Vertical n-sided prism / frustum (cylinders, cones, tree canopies).</summary>
        public void Prism(Vector3 baseCenter, float rBottom, float rTop, float height, int sides, Color c)
        {
            float step = Mathf.PI * 2f / sides;
            Vector3 top = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = baseCenter + d0 * rBottom, b1 = baseCenter + d1 * rBottom;
                Vector3 t0 = top + d0 * rTop, t1 = top + d1 * rTop;
                Vector3 n = ((d0 + d1) * 0.5f + Vector3.up * ((rBottom - rTop) / Mathf.Max(0.01f, height))).normalized;
                Quad(b0, t0, t1, b1, n, Shade(c, 0.85f + 0.15f * Mathf.Abs(Mathf.Cos((a0 + a1) * 0.5f))));
                if (rTop > 0.001f)
                {
                    int i0 = verts.Count;
                    verts.Add(top); verts.Add(t0); verts.Add(t1);
                    for (int k = 0; k < 3; k++) { normals.Add(Vector3.up); colors.Add(c); }
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                }
            }
        }

        class ModelParts
        {
            public Matrix4x4 local;
            public Vector3[] verts, normals;
            public int[][] tris;
            public Color[] colors;
            public string[] names;
        }
        static readonly Dictionary<GameObject, List<ModelParts>> modelCache = new Dictionary<GameObject, List<ModelParts>>();

        /// <summary>Front-facing rotation for Blender models (exported with their front at +Z; the game's front is -Z).</summary>
        public static readonly Quaternion ModelFlip = Quaternion.Euler(0, 180f, 0);

        static List<ModelParts> PartsOf(GameObject prefab)
        {
            if (modelCache.TryGetValue(prefab, out var parts)) return parts;
            parts = new List<ModelParts>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var mats = mf.GetComponent<MeshRenderer>()?.sharedMaterials ?? new Material[0];
                var p = new ModelParts
                {
                    // Keep the root's own offset: Blender exports joined models with the pivot wherever the first part was.
                    local = mf.transform.localToWorldMatrix,
                    verts = mesh.vertices,
                    normals = mesh.normals,
                    tris = new int[mesh.subMeshCount][],
                    colors = new Color[mesh.subMeshCount],
                    names = new string[mesh.subMeshCount],
                };
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    p.tris[s] = mesh.GetTriangles(s);
                    var m = s < mats.Length ? mats[s] : null;
                    Color c = m != null && (m.HasProperty("_BaseColor") || m.HasProperty("_Color")) ? m.color : Color.gray;
                    c.a = 1f;
                    bool emissive = m != null && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.01f;
                    p.colors[s] = emissive ? Led(c) : c;
                    p.names[s] = m != null ? m.name : "";
                }
                parts.Add(p);
            }
            modelCache[prefab] = parts;
            return parts;
        }

        /// <summary>True if the prefab's meshes can be baked (imported with Read/Write enabled).</summary>
        public static bool CanAppend(GameObject prefab) => prefab != null && PartsOf(prefab).Count > 0;

        /// <summary>Bakes a Blender model into this mesh, turning each material into vertex colors.
        /// <paramref name="recolor"/> can override a color by material name (e.g. random foliage tints).</summary>
        public void AppendModel(GameObject prefab, Matrix4x4 m, System.Func<string, Color, Color> recolor = null)
        {
            foreach (var p in PartsOf(prefab))
            {
                var xf = m * p.local;
                for (int s = 0; s < p.tris.Length; s++)
                {
                    Color c = recolor != null ? recolor(p.names[s], p.colors[s]) : p.colors[s];
                    // Submeshes share the vertex array; copy just the vertices this submesh uses.
                    var remap = new Dictionary<int, int>();
                    foreach (int t in p.tris[s])
                    {
                        if (!remap.TryGetValue(t, out int ni))
                        {
                            ni = verts.Count;
                            remap[t] = ni;
                            verts.Add(xf.MultiplyPoint3x4(p.verts[t]));
                            normals.Add(p.normals.Length > t ? xf.MultiplyVector(p.normals[t]).normalized : Vector3.up);
                            colors.Add(c);
                        }
                        tris.Add(ni);
                    }
                }
            }
        }

        public static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        public static Color Led(Color c) => new Color(c.r, c.g, c.b, 0f);

        public Mesh ToMesh(Mesh m = null)
        {
            if (m == null) m = new Mesh();
            m.Clear();
            m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
