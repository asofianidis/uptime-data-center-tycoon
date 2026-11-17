using UnityEngine;

namespace DCT
{
    /// <summary>Procedural low-poly models for every facility type. Local origin is the footprint center at floor level, front faces -Z.</summary>
    public static class Models
    {
        public const float UHeight = 0.042f;

        public static float RackHeight(FacilityDef d) => d.rackU * UHeight + 0.12f;

        public static float Build(MeshBuilder mb, FacilityDef d, PlacedObject o, Simulation sim)
        {
            switch (d.kind)
            {
                case FacilityKind.Rack: return Rack(mb, d, o, sim);
                case FacilityKind.Cooling: return Cooling(mb, d);
                case FacilityKind.CoreSwitch: return Core(mb, d, o);
                case FacilityKind.Isp: return Isp(mb, d);
                case FacilityKind.Transformer: return Transformer(mb, d);
                case FacilityKind.Switchgear: return Switchgear(mb, d);
                case FacilityKind.Ups: return Ups(mb, d);
                case FacilityKind.Generator: return Generator(mb, d);
                case FacilityKind.Desk: return Desk(mb, d);
            }
            mb.Box(new Vector3(0, 0.5f, 0), new Vector3(0.8f, 1f, 0.8f), d.color);
            return 1f;
        }

        static float Rack(MeshBuilder mb, FacilityDef d, PlacedObject o, Simulation sim)
        {
            float H = RackHeight(d);
            float W = 0.64f, D = 0.92f;
            Color frame = d.color;
            // Plinth + main body
            mb.Box(new Vector3(0, 0.03f, 0), new Vector3(W, 0.06f, D), MeshBuilder.Shade(frame, 0.6f));
            mb.Box(new Vector3(0, H * 0.5f + 0.03f, 0), new Vector3(W, H - 0.06f, D), frame);
            // Side rails on the front
            float fz = -D * 0.5f - 0.012f;
            mb.Box(new Vector3(-W * 0.5f + 0.03f, H * 0.5f, fz), new Vector3(0.04f, H - 0.06f, 0.025f), MeshBuilder.Shade(frame, 1.6f));
            mb.Box(new Vector3(W * 0.5f - 0.03f, H * 0.5f, fz), new Vector3(0.04f, H - 0.06f, 0.025f), MeshBuilder.Shade(frame, 1.6f));

            Contract c = o != null && o.contractId > 0 ? sim?.FindContract(o.contractId) : null;
            Color cust = c != null ? Catalog.CustomerColors[c.colorIdx] : new Color(0.3f, 0.3f, 0.32f);

            float baseY = 0.08f;
            int used = 0;
            if (o != null)
            {
                foreach (var dev in o.devices)
                {
                    var dd = Catalog.Device(dev.def);
                    if (dd == null) continue;
                    used += dd.heightU;
                    float y0 = baseY + dev.u * UHeight;
                    float hgt = dd.heightU * UHeight - 0.006f;
                    Color body = dev.failed ? new Color(0.45f, 0.12f, 0.1f) : dd.color;
                    mb.Box(new Vector3(0, y0 + hgt * 0.5f, fz - 0.01f), new Vector3(W - 0.1f, hgt, 0.03f), body);
                    // Status LEDs on the faceplate
                    Color led = dev.failed ? new Color(1f, 0.1f, 0.05f) : dd.kind == DeviceKind.Switch ? new Color(0.2f, 0.9f, 1f) : new Color(0.2f, 1f, 0.35f);
                    int leds = dd.kind == DeviceKind.Switch ? 6 : 2;
                    for (int i = 0; i < leds; i++)
                        mb.Box(new Vector3(-W * 0.5f + 0.1f + i * 0.045f, y0 + hgt * 0.5f, fz - 0.03f), new Vector3(0.022f, Mathf.Min(0.02f, hgt * 0.6f), 0.01f), MeshBuilder.Led(led));
                    if (dd.gpus > 0)
                        mb.Box(new Vector3(0.12f, y0 + hgt * 0.5f, fz - 0.03f), new Vector3(0.2f, hgt * 0.5f, 0.01f), MeshBuilder.Led(new Color(0.4f, 1f, 0.3f)));
                }
                // Colocation customer gear fills the rack proportionally to their power draw.
                if (c != null && c.type == ContractType.Colocation && (c.status == ContractStatus.Active || c.status == ContractStatus.Setup))
                {
                    int units = Mathf.Clamp(Mathf.RoundToInt(c.reqKWPerRack / d.rackKW * d.rackU), 4, d.rackU - used - 1);
                    int u = d.rackU - 1;
                    for (int k = 0; k < units && u > used; k += 2, u -= 2)
                    {
                        float y0 = baseY + (u - 1) * UHeight;
                        mb.Box(new Vector3(0, y0 + UHeight, fz - 0.01f), new Vector3(W - 0.1f, UHeight * 2 - 0.008f, 0.03f), MeshBuilder.Shade(cust, 0.45f));
                        mb.Box(new Vector3(W * 0.5f - 0.12f, y0 + UHeight, fz - 0.03f), new Vector3(0.03f, 0.02f, 0.01f), MeshBuilder.Led(cust));
                    }
                    used += units;
                }
            }

            // Roof: customer color band + a fill gauge so you can read racks from above.
            float topY = H + 0.001f;
            mb.FlatQuad(-W * 0.5f + 0.04f, -D * 0.5f + 0.04f, W * 0.5f - 0.04f, D * 0.5f - 0.04f, topY, MeshBuilder.Shade(frame, 1.5f));
            mb.FlatQuad(-W * 0.5f + 0.06f, D * 0.5f - 0.3f, W * 0.5f - 0.06f, D * 0.5f - 0.06f, topY + 0.002f, c != null ? cust : new Color(0.25f, 0.25f, 0.27f));
            float fill = Mathf.Clamp01(used / (float)d.rackU);
            float gx0 = -W * 0.5f + 0.08f, gx1 = W * 0.5f - 0.08f;
            mb.FlatQuad(gx0, -D * 0.5f + 0.1f, gx1, -D * 0.5f + 0.22f, topY + 0.002f, new Color(0.08f, 0.08f, 0.09f));
            if (fill > 0) mb.FlatQuad(gx0, -D * 0.5f + 0.1f, gx0 + (gx1 - gx0) * fill, -D * 0.5f + 0.22f, topY + 0.004f, MeshBuilder.Led(new Color(0.25f, 0.6f, 0.9f)));
            // Vent slots
            for (int i = 0; i < 4; i++)
                mb.FlatQuad(-W * 0.5f + 0.1f, -0.12f + i * 0.08f, W * 0.5f - 0.1f, -0.09f + i * 0.08f, topY + 0.002f, new Color(0.05f, 0.05f, 0.06f));
            return H;
        }

        static float Cooling(MeshBuilder mb, FacilityDef d)
        {
            if (d.w > 1)
            {
                // Liquid CDU
                mb.Box(new Vector3(0, 0.7f, 0), new Vector3(1.8f, 1.4f, 0.8f), d.color);
                for (int i = 0; i < 4; i++)
                {
                    mb.Prism(new Vector3(-0.6f + i * 0.4f, 1.4f, 0.1f), 0.07f, 0.07f, 0.35f, 8, new Color(0.75f, 0.75f, 0.8f));
                    mb.Box(new Vector3(-0.6f + i * 0.4f, 1.76f, 0.1f), new Vector3(0.16f, 0.06f, 0.16f), i % 2 == 0 ? new Color(0.2f, 0.4f, 1f) : new Color(1f, 0.3f, 0.2f));
                }
                mb.Box(new Vector3(0.55f, 0.95f, -0.41f), new Vector3(0.4f, 0.25f, 0.02f), MeshBuilder.Led(new Color(0.3f, 0.8f, 1f)));
                return 1.8f;
            }
            float H = d.coolingKW > 50 ? 2.0f : 1.8f;
            mb.Box(new Vector3(0, H * 0.5f, 0), new Vector3(0.9f, H, 0.86f), d.color);
            for (int i = 0; i < 6; i++)
                mb.Box(new Vector3(0, 0.25f + i * 0.12f, -0.44f), new Vector3(0.75f, 0.05f, 0.02f), new Color(0.35f, 0.38f, 0.42f));
            mb.Box(new Vector3(0, H - 0.3f, -0.44f), new Vector3(0.3f, 0.15f, 0.02f), MeshBuilder.Led(new Color(0.3f, 0.75f, 1f)));
            // Fans on top
            mb.Prism(new Vector3(-0.2f, H, 0), 0.17f, 0.17f, 0.02f, 10, new Color(0.15f, 0.16f, 0.18f));
            mb.Prism(new Vector3(0.2f, H, 0), 0.17f, 0.17f, 0.02f, 10, new Color(0.15f, 0.16f, 0.18f));
            mb.Box(new Vector3(-0.2f, H + 0.03f, 0), new Vector3(0.3f, 0.01f, 0.04f), new Color(0.6f, 0.6f, 0.65f));
            mb.Box(new Vector3(0.2f, H + 0.03f, 0), new Vector3(0.04f, 0.01f, 0.3f), new Color(0.6f, 0.6f, 0.65f));
            return H + 0.05f;
        }

        static float Core(MeshBuilder mb, FacilityDef d, PlacedObject o)
        {
            float H = 2.0f;
            mb.Box(new Vector3(0, H * 0.5f, 0), new Vector3(0.7f, H, 0.95f), new Color(0.1f, 0.11f, 0.14f));
            int rows = 4 + d.ports / 16;
            var rnd = new System.Random(o != null ? o.id : 1);
            for (int r = 0; r < rows; r++)
            {
                float y = 0.4f + r * 0.16f;
                mb.Box(new Vector3(0, y, -0.485f), new Vector3(0.6f, 0.12f, 0.02f), d.color);
                for (int i = 0; i < 10; i++)
                {
                    Color led = rnd.NextDouble() < 0.8 ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.7f, 0.1f);
                    mb.Box(new Vector3(-0.25f + i * 0.055f, y + 0.03f, -0.5f), new Vector3(0.025f, 0.02f, 0.01f), MeshBuilder.Led(led));
                }
            }
            mb.FlatQuad(-0.3f, -0.42f, 0.3f, 0.42f, H + 0.001f, d.color);
            mb.FlatQuad(-0.2f, -0.3f, 0.2f, 0.3f, H + 0.003f, MeshBuilder.Led(MeshBuilder.Shade(d.color, 0.8f)));
            return H;
        }

        static float Isp(MeshBuilder mb, FacilityDef d)
        {
            mb.Box(new Vector3(0, 0.65f, 0.1f), new Vector3(0.75f, 1.3f, 0.55f), new Color(0.55f, 0.57f, 0.6f));
            mb.Box(new Vector3(0, 0.9f, -0.18f), new Vector3(0.6f, 0.5f, 0.02f), new Color(0.3f, 0.32f, 0.35f));
            mb.Box(new Vector3(0, 1.31f, 0.1f), new Vector3(0.6f, 0.02f, 0.4f), d.color);
            mb.Prism(new Vector3(0, 1.32f, 0.1f), 0.18f, 0.18f, 0.08f, 10, new Color(1f, 0.85f, 0.1f));
            mb.Box(new Vector3(0, 1.1f, -0.2f), new Vector3(0.4f, 0.04f, 0.01f), MeshBuilder.Led(d.color));
            mb.Box(new Vector3(-0.25f, 0.4f, -0.2f), new Vector3(0.05f, 0.05f, 0.01f), MeshBuilder.Led(new Color(0.2f, 1f, 0.3f)));
            return 1.4f;
        }

        static float Transformer(MeshBuilder mb, FacilityDef d)
        {
            float s = d.w;
            mb.Box(new Vector3(0, 0.05f, 0), new Vector3(0.95f * s, 0.1f, 0.95f * s), new Color(0.5f, 0.5f, 0.5f));
            float H = 1.1f * (s > 1 ? 1.4f : 1f);
            mb.Box(new Vector3(0, H * 0.5f + 0.1f, 0), new Vector3(0.6f * s, H, 0.6f * s), d.color);
            for (int i = -2; i <= 2; i++)
            {
                mb.Box(new Vector3(0.33f * s, H * 0.5f + 0.1f, i * 0.1f * s), new Vector3(0.08f, H * 0.85f, 0.03f), MeshBuilder.Shade(d.color, 0.8f));
                mb.Box(new Vector3(-0.33f * s, H * 0.5f + 0.1f, i * 0.1f * s), new Vector3(0.08f, H * 0.85f, 0.03f), MeshBuilder.Shade(d.color, 0.8f));
            }
            for (int i = -1; i <= 1; i++)
            {
                mb.Prism(new Vector3(i * 0.17f * s, H + 0.1f, 0), 0.05f, 0.03f, 0.3f, 6, new Color(0.6f, 0.35f, 0.25f));
            }
            mb.Box(new Vector3(0, H * 0.6f, -0.31f * s), new Vector3(0.2f, 0.2f, 0.01f), new Color(1f, 0.85f, 0.1f));
            if (s > 1)
            {
                // Fence posts around the substation
                for (int i = -2; i <= 2; i++)
                {
                    mb.Box(new Vector3(i * 0.45f, 0.5f, -0.95f), new Vector3(0.04f, 1f, 0.04f), new Color(0.6f, 0.6f, 0.6f));
                    mb.Box(new Vector3(i * 0.45f, 0.5f, 0.95f), new Vector3(0.04f, 1f, 0.04f), new Color(0.6f, 0.6f, 0.6f));
                }
            }
            return H + 0.4f;
        }

        static float Switchgear(MeshBuilder mb, FacilityDef d)
        {
            int panels = d.w > 1 ? 4 : 2;
            float W = d.w * 0.92f, H = 1.9f;
            mb.Box(new Vector3(0, H * 0.5f, 0.1f), new Vector3(W, H, 0.6f), d.color);
            for (int i = 0; i < panels; i++)
            {
                float x = -W * 0.5f + (i + 0.5f) * W / panels;
                mb.Box(new Vector3(x, H * 0.5f, -0.205f), new Vector3(W / panels - 0.04f, H - 0.15f, 0.02f), MeshBuilder.Shade(d.color, 1.1f));
                mb.Box(new Vector3(x, H * 0.75f, -0.22f), new Vector3(0.12f, 0.1f, 0.01f), new Color(0.15f, 0.15f, 0.15f));
                mb.Box(new Vector3(x - 0.05f, H * 0.62f, -0.22f), new Vector3(0.03f, 0.03f, 0.01f), MeshBuilder.Led(new Color(0.2f, 1f, 0.3f)));
                mb.Box(new Vector3(x + 0.05f, H * 0.62f, -0.22f), new Vector3(0.03f, 0.03f, 0.01f), MeshBuilder.Led(new Color(1f, 0.2f, 0.1f)));
            }
            mb.FlatQuad(-W * 0.5f + 0.05f, -0.15f, W * 0.5f - 0.05f, 0.35f, H + 0.002f, new Color(0.95f, 0.8f, 0.1f));
            return H;
        }

        static float Ups(MeshBuilder mb, FacilityDef d)
        {
            float H = 1.7f;
            mb.Box(new Vector3(0, H * 0.5f, 0), new Vector3(0.9f, H, 0.75f), d.color);
            for (int i = 0; i < 7; i++)
                mb.Box(new Vector3(0, 0.2f + i * 0.2f, -0.38f), new Vector3(0.78f, 0.14f, 0.02f), new Color(0.32f, 0.34f, 0.38f));
            mb.Box(new Vector3(0.25f, H - 0.15f, -0.39f), new Vector3(0.2f, 0.08f, 0.01f), MeshBuilder.Led(new Color(0.2f, 1f, 0.4f)));
            mb.FlatQuad(-0.4f, -0.3f, 0.4f, 0.3f, H + 0.002f, new Color(0.2f, 0.75f, 0.35f));
            return H;
        }

        static float Generator(MeshBuilder mb, FacilityDef d)
        {
            float L = d.w * 0.92f, Wd = d.h * 0.85f;
            mb.Box(new Vector3(0, 0.08f, 0), new Vector3(L, 0.16f, Wd), new Color(0.15f, 0.15f, 0.15f));
            float H = d.h > 1 ? 1.6f : 1.15f;
            mb.Box(new Vector3(0, 0.16f + H * 0.5f, 0), new Vector3(L - 0.1f, H, Wd - 0.1f), d.color);
            for (int i = 0; i < 6; i++)
                mb.Box(new Vector3(-L * 0.35f + i * L * 0.14f, 0.16f + H * 0.55f, -Wd * 0.5f + 0.04f), new Vector3(0.06f, H * 0.6f, 0.02f), MeshBuilder.Shade(d.color, 0.7f));
            mb.Prism(new Vector3(L * 0.3f, 0.16f + H, 0), 0.08f, 0.07f, 0.55f, 8, new Color(0.3f, 0.3f, 0.32f));
            mb.Box(new Vector3(-L * 0.25f, 0.16f + H + 0.01f, 0), new Vector3(0.5f, 0.02f, 0.4f), new Color(0.2f, 0.2f, 0.2f));
            return H + 0.75f;
        }

        static float Desk(MeshBuilder mb, FacilityDef d)
        {
            Color wood = d.color;
            mb.Box(new Vector3(0, 0.74f, 0.05f), new Vector3(0.9f, 0.04f, 0.55f), wood);
            mb.Box(new Vector3(-0.4f, 0.37f, 0.05f), new Vector3(0.04f, 0.72f, 0.5f), MeshBuilder.Shade(wood, 0.7f));
            mb.Box(new Vector3(0.4f, 0.37f, 0.05f), new Vector3(0.04f, 0.72f, 0.5f), MeshBuilder.Shade(wood, 0.7f));
            mb.Box(new Vector3(0, 0.98f, 0.22f), new Vector3(0.45f, 0.28f, 0.03f), new Color(0.08f, 0.08f, 0.09f));
            mb.Box(new Vector3(0, 0.98f, 0.2f), new Vector3(0.41f, 0.24f, 0.01f), MeshBuilder.Led(new Color(0.25f, 0.5f, 0.85f)));
            mb.Box(new Vector3(0, 0.77f, 0.0f), new Vector3(0.3f, 0.02f, 0.1f), new Color(0.15f, 0.15f, 0.15f));
            // chair
            mb.Box(new Vector3(0, 0.45f, -0.35f), new Vector3(0.38f, 0.06f, 0.36f), new Color(0.15f, 0.17f, 0.22f));
            mb.Box(new Vector3(0, 0.72f, -0.52f), new Vector3(0.36f, 0.5f, 0.05f), new Color(0.15f, 0.17f, 0.22f));
            mb.Prism(new Vector3(0, 0.02f, -0.35f), 0.03f, 0.03f, 0.42f, 6, new Color(0.3f, 0.3f, 0.3f));
            return 1.2f;
        }

        // ---- Scenery ----
        public static void Tree(MeshBuilder mb, Vector3 p, System.Random r)
        {
            float s = 0.7f + (float)r.NextDouble() * 0.6f;
            mb.Prism(p, 0.08f * s, 0.06f * s, 0.5f * s, 6, new Color(0.38f, 0.26f, 0.16f));
            Color leaf = new Color(0.16f + (float)r.NextDouble() * 0.1f, 0.38f + (float)r.NextDouble() * 0.15f, 0.14f);
            if (r.NextDouble() < 0.5)
            {
                mb.Prism(p + Vector3.up * 0.35f * s, 0.45f * s, 0.0f, 0.8f * s, 7, leaf);
                mb.Prism(p + Vector3.up * 0.75f * s, 0.32f * s, 0.0f, 0.7f * s, 7, MeshBuilder.Shade(leaf, 1.1f));
            }
            else
            {
                mb.Prism(p + Vector3.up * 0.4f * s, 0.32f * s, 0.42f * s, 0.25f * s, 8, leaf);
                mb.Prism(p + Vector3.up * 0.65f * s, 0.42f * s, 0.2f * s, 0.35f * s, 8, MeshBuilder.Shade(leaf, 1.1f));
            }
        }

        public static void Rock(MeshBuilder mb, Vector3 p, System.Random r)
        {
            float s = 0.5f + (float)r.NextDouble() * 0.4f;
            Color c = new Color(0.45f, 0.45f, 0.47f) * (0.85f + (float)r.NextDouble() * 0.3f);
            c.a = 1;
            mb.Prism(p, 0.4f * s, 0.22f * s, 0.32f * s, 6, c);
        }
    }
}
