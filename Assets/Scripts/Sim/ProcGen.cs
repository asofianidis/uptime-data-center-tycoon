using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCT
{
    /// <summary>All procedural generation: cities, lots, customers and contracts.</summary>
    public static class ProcGen
    {
        static readonly string[] Pre = { "Nim", "Kes", "Vor", "Ast", "Hal", "Zen", "Quan", "Bri", "Lum", "Cor", "Tel", "Ver", "Ox", "Sol", "Arc", "Nex", "Pyr", "Ion", "Del", "Mar", "Syl", "Tor", "Cy", "Eli", "Nov", "Kai", "Rho", "Vel", "Gal", "Hex", "Opt", "Ze", "Mor", "Fin", "Lyr" };
        static readonly string[] Mid = { "a", "o", "i", "e", "ar", "en", "ix", "", "", "", "u", "on" };
        static readonly string[] Suf = { "tek", "ra", "lytics", "sys", "ion", "ware", "tron", "core", "link", "nova", "grid", "flow", "path", "stack", "vault", "byte", "logic", "mind", "scale", "forge", "ly", "io", "wave", "shift", "loop" };

        public class Industry
        {
            public string name, noun, word;
            public float[] typeWeights; // Colocation, Dedicated, Compute, Storage, GPU
            public float slaBias;
        }

        static readonly Industry[] Industries =
        {
            new Industry { name = "Gaming", word = "Games", noun = "game studio", typeWeights = new[] { 1f, 3f, 2f, 0.5f, 1f }, slaBias = 0f },
            new Industry { name = "Finance", word = "Capital", noun = "trading firm", typeWeights = new[] { 3f, 2f, 1f, 1f, 0.5f }, slaBias = 1.5f },
            new Industry { name = "Healthcare", word = "Health", noun = "hospital network", typeWeights = new[] { 1f, 1f, 1f, 3f, 0.3f }, slaBias = 1.2f },
            new Industry { name = "Media", word = "Media", noun = "streaming service", typeWeights = new[] { 1f, 1f, 1.5f, 3f, 0.5f }, slaBias = 0.3f },
            new Industry { name = "AI Research", word = "AI", noun = "machine learning lab", typeWeights = new[] { 0.5f, 1f, 2f, 1f, 4f }, slaBias = 0f },
            new Industry { name = "Retail", word = "Commerce", noun = "online retailer", typeWeights = new[] { 1f, 1.5f, 3f, 1f, 0.2f }, slaBias = 0.6f },
            new Industry { name = "Logistics", word = "Logistics", noun = "shipping company", typeWeights = new[] { 2f, 1.5f, 2f, 1f, 0.2f }, slaBias = 0.4f },
            new Industry { name = "Government", word = "Agency", noun = "public agency", typeWeights = new[] { 3f, 1f, 1f, 2f, 0.3f }, slaBias = 1f },
            new Industry { name = "Startup", word = "Labs", noun = "venture-backed startup", typeWeights = new[] { 0.5f, 2f, 3f, 1f, 1f }, slaBias = -0.5f },
            new Industry { name = "Telecom", word = "Telecom", noun = "regional carrier", typeWeights = new[] { 3f, 1f, 1f, 1f, 0.2f }, slaBias = 1f },
            new Industry { name = "Education", word = "University", noun = "university", typeWeights = new[] { 1f, 1f, 2f, 2f, 1.5f }, slaBias = -0.3f },
            new Industry { name = "Crypto", word = "Chain", noun = "blockchain venture", typeWeights = new[] { 1f, 2f, 2f, 0.5f, 2f }, slaBias = -0.8f },
        };

        static readonly string[] CityA = { "North", "Port", "New", "East", "West", "Lake", "Fort", "Glen", "Red", "Silver", "Iron", "Stone", "Cedar", "Maple", "Ash" };
        static readonly string[] CityB = { "haven", "field", "ridge", "brook", "ford", "vale", "mouth", "gate", "ton", "wood", "crest", "port", "view", "hollow" };

        public static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        public static T Pick<T>(System.Random r, IList<T> list) => list[r.Next(list.Count)];
        public static bool Chance(System.Random r, double p) => r.NextDouble() < p;

        public static CityProfile GenerateCity(System.Random r)
        {
            var c = new CityProfile();
            c.name = Pick(r, CityA) + Pick(r, CityB);
            int climate = r.Next(5);
            switch (climate)
            {
                case 0: c.climate = "Arctic"; c.coolingFactor = 1.35f; break;
                case 1: c.climate = "Cold"; c.coolingFactor = 1.18f; break;
                case 2: c.climate = "Temperate"; c.coolingFactor = 1f; break;
                case 3: c.climate = "Hot"; c.coolingFactor = 0.85f; break;
                default: c.climate = "Desert"; c.coolingFactor = 0.72f; break;
            }
            c.powerPrice = (float)Math.Round(Range(r, 0.06f, 0.2f), 3);
            c.landPrice = (float)Math.Round(Range(r, 0.6f, 1.8f), 2);
            c.disasterRate = (float)Math.Round(Range(r, 0.5f, 1.7f), 2);
            c.demandFactor = (float)Math.Round(Range(r, 0.85f, 1.25f), 2);

            var notes = new List<string>();
            if (c.coolingFactor > 1.1f) notes.Add("cold air makes cooling cheap");
            if (c.coolingFactor < 0.9f) notes.Add("the heat makes cooling a constant battle");
            if (c.powerPrice < 0.09f) notes.Add("hydro power keeps electricity cheap");
            if (c.powerPrice > 0.16f) notes.Add("electricity is expensive");
            if (c.landPrice > 1.4f) notes.Add("land is pricey");
            if (c.landPrice < 0.8f) notes.Add("land is cheap");
            if (c.disasterRate > 1.3f) notes.Add("storms and grid trouble are common");
            if (c.disasterRate < 0.7f) notes.Add("the grid is famously stable");
            if (c.demandFactor > 1.12f) notes.Add("a booming tech scene pays well");
            if (c.demandFactor < 0.92f) notes.Add("customers are price-sensitive");
            c.blurb = notes.Count == 0 ? "An unremarkable, balanced market." : char.ToUpper(notes[0][0]) + notes[0].Substring(1) + (notes.Count > 1 ? ", " + string.Join(", ", notes.GetRange(1, notes.Count - 1)) : "") + ".";
            return c;
        }

        public static void GenerateLot(GameState s, System.Random r)
        {
            s.rooms = new int[s.mapW * s.mapH];
            s.obstacles = new int[s.mapW * s.mapH];
            int w = 20 + r.Next(5), h = 15 + r.Next(4);
            s.ownX0 = (s.mapW - w) / 2 - 4 + r.Next(9);
            s.ownY0 = (s.mapH - h) / 2 - 3 + r.Next(7);
            s.ownX1 = s.ownX0 + w;
            s.ownY1 = s.ownY0 + h;

            // Scatter tree clusters and rocks with simple noise.
            float ox = Range(r, 0, 1000), oy = Range(r, 0, 1000);
            float density = Range(r, 0.25f, 0.5f);
            for (int y = 0; y < s.mapH; y++)
                for (int x = 0; x < s.mapW; x++)
                {
                    float n = Mathf.PerlinNoise(ox + x * 0.12f, oy + y * 0.12f);
                    int i = y * s.mapW + x;
                    bool owned = s.Owned(x, y);
                    float thresh = owned ? 0.72f : 0.62f - density * 0.2f;
                    if (n > thresh && r.NextDouble() < 0.7) s.obstacles[i] = 1;
                    else if (r.NextDouble() < (owned ? 0.006 : 0.012)) s.obstacles[i] = 2;
                }
            // Keep a clear starting pad so the first building is easy to place.
            int cx = s.ownX0 + w / 2, cy = s.ownY0 + h / 2;
            for (int y = cy - 5; y <= cy + 5; y++)
                for (int x = cx - 7; x <= cx + 7; x++)
                    if (s.InBounds(x, y)) s.obstacles[y * s.mapW + x] = 0;
        }

        public static string CustomerName(System.Random r, Industry ind)
        {
            string n = Pick(r, Pre) + Pick(r, Mid) + Pick(r, Suf);
            n = char.ToUpper(n[0]) + n.Substring(1).ToLower();
            if (Chance(r, 0.6)) n += " " + ind.word;
            return n;
        }

        static Industry PickIndustry(System.Random r) => Pick(r, Industries);

        static ContractType PickType(System.Random r, Industry ind, int level, bool aiBoom)
        {
            float[] w = (float[])ind.typeWeights.Clone();
            if (level < 5) w[4] = 0f;              // GPU contracts need GPU hardware
            if (aiBoom) w[4] = (w[4] + 1f) * 4f;
            float total = 0; foreach (var x in w) total += x;
            float roll = (float)r.NextDouble() * total;
            for (int i = 0; i < w.Length; i++) { roll -= w[i]; if (roll <= 0) return (ContractType)i; }
            return ContractType.Compute;
        }

        static readonly string[] TraitPool = { "Rush", "Budget", "Premium", "Long-term", "Growing", "Flexible", "Strict", "Eco" };

        public static string TraitDesc(string t)
        {
            switch (t)
            {
                case "Rush": return "Tight setup deadline, pays 25% more.";
                case "Budget": return "Pays 15% less, relaxed SLA.";
                case "Premium": return "Pays 20% more, stricter SLA.";
                case "Long-term": return "Double length contract at a slight discount.";
                case "Growing": return "Likely to ask for expansion mid-contract.";
                case "Flexible": return "Generous setup window.";
                case "Strict": return "Will cancel after 2 SLA breaches instead of 3.";
                case "Renewal": return "Returning customer.";
                case "Eco": return "Wants 25%+ of your energy from solar each month; pays 10% more.";
                case "Referral": return "Referred by a happy customer; pays 12% more.";
                case "Redundant": return "Every rack needs dual PDU, two ToR switches and two core uplinks; pays 35% more.";
            }
            return t;
        }

        public static Contract GenerateContract(GameState s, System.Random r)
        {
            int level = s.Level;
            var ind = PickIndustry(r);
            var c = new Contract
            {
                id = s.NewId(),
                industry = ind.name,
                customer = CustomerName(r, ind),
                colorIdx = r.Next(Catalog.CustomerColors.Length),
                status = ContractStatus.Offer,
            };
            c.type = PickType(r, ind, level, s.scenario == "aiboom");
            float scale = (1f + (level - 1) * 0.65f) * Range(r, 0.6f, 1.4f);
            float pay = 0f;

            switch (c.type)
            {
                case ContractType.Colocation:
                {
                    c.reqRacks = Mathf.Clamp(Mathf.RoundToInt(Range(r, 0.8f, 2.2f) * scale * 0.7f), 1, 3 + level * 3);
                    var kws = new List<float> { 2, 3, 4, 5, 6 };
                    if (level >= 2) kws.Add(7);
                    if (level >= 4) { kws.Add(10); kws.Add(12); kws.Add(15); kws.Add(18); }
                    if (level >= 7) { kws.Add(30); kws.Add(45); }
                    c.reqKWPerRack = Pick(r, kws);
                    c.reqBandwidth = c.reqRacks * Range(r, 0.5f, 1.8f);
                    pay = c.reqRacks * (450f + c.reqKWPerRack * 270f);
                    break;
                }
                case ContractType.Dedicated:
                {
                    var cores = new List<int> { 8 };
                    if (level >= 2) cores.Add(32);
                    if (level >= 3) cores.Add(64);
                    if (level >= 4) cores.Add(80);
                    if (level >= 6) cores.Add(192);
                    c.reqCoresPerServer = Pick(r, cores);
                    c.reqServers = Mathf.Max(2, Mathf.RoundToInt(Range(r, 2f, 6f) * scale));
                    c.reqBandwidth = c.reqServers * 0.25f * (1 + c.reqCoresPerServer / 64f);
                    pay = c.reqServers * (90f + c.reqCoresPerServer * 64f);
                    break;
                }
                case ContractType.Compute:
                {
                    int cores = Mathf.RoundToInt(Range(r, 24f, 90f) * scale * (1 + (level - 1) * 0.35f));
                    c.reqCores = Mathf.Max(16, cores / 8 * 8);
                    c.reqBandwidth = c.reqCores / 40f;
                    pay = c.reqCores * 58f;
                    break;
                }
                case ContractType.Storage:
                {
                    float tb = Range(r, 40f, 180f) * scale * (1 + (level - 1) * 0.5f);
                    c.reqStorageTB = Mathf.Max(20, Mathf.Round(tb / 10f) * 10f);
                    c.reqBandwidth = c.reqStorageTB / 90f;
                    pay = c.reqStorageTB * 23f;
                    break;
                }
                case ContractType.GpuCloud:
                {
                    c.reqGpus = Mathf.Max(4, Mathf.RoundToInt(Range(r, 4f, 10f) * scale * 0.5f / 4f) * 4);
                    c.reqBandwidth = c.reqGpus * 0.8f;
                    pay = c.reqGpus * 3900f;
                    break;
                }
            }

            // SLA tier
            float slaRoll = (float)r.NextDouble() * 3f + ind.slaBias + level * 0.15f;
            float slaMul;
            if (slaRoll < 1.4f) { c.sla = 99.0f; slaMul = 1f; }
            else if (slaRoll < 2.6f) { c.sla = 99.5f; slaMul = 1.08f; }
            else if (slaRoll < 3.6f || level < 3) { c.sla = 99.9f; slaMul = 1.2f; }
            else { c.sla = 99.99f; slaMul = 1.42f; }

            c.months = r.Next(3, 9) + level / 2;
            c.setupDays = r.Next(7, 15);

            // Traits
            int traitCount = r.NextDouble() < 0.45 ? 0 : (r.NextDouble() < 0.75 ? 1 : 2);
            float traitMul = 1f;
            for (int i = 0; i < traitCount; i++)
            {
                string t = Pick(r, TraitPool);
                if (c.traits.Contains(t)) continue;
                if (t == "Eco" && level < 3 && s.scenario != "green") continue;
                if ((t == "Budget" && c.traits.Contains("Premium")) || (t == "Premium" && c.traits.Contains("Budget"))) continue;
                if ((t == "Rush" && c.traits.Contains("Flexible")) || (t == "Flexible" && c.traits.Contains("Rush"))) continue;
                c.traits.Add(t);
                switch (t)
                {
                    case "Rush": c.setupDays = r.Next(3, 5); traitMul *= 1.25f; break;
                    case "Budget": traitMul *= 0.85f; if (c.sla > 99.0f) { c.sla = c.sla >= 99.9f ? 99.5f : 99.0f; } break;
                    case "Premium": traitMul *= 1.2f; if (c.sla < 99.9f) c.sla = 99.9f; break;
                    case "Long-term": c.months *= 2; traitMul *= 0.95f; break;
                    case "Flexible": c.setupDays += 10; break;
                    case "Eco": traitMul *= 1.1f; break;
                }
            }

            if (s.scenario == "green" && !c.traits.Contains("Eco") && Chance(r, 0.5)) { c.traits.Add("Eco"); traitMul *= 1.1f; }
            if (level >= 3 && c.sla >= 99.9f && Chance(r, 0.35))
            {
                c.redundant = true;
                c.traits.Add("Redundant");
                traitMul *= 1.35f;
            }
            foreach (var e in s.events) if (e.type == "pricewar") traitMul *= e.magnitude;
            c.reqBandwidth = Mathf.Max(0.5f, Mathf.Round(c.reqBandwidth * 2f) / 2f);
            float ratingMul = 0.8f + s.rating / 250f;
            pay *= slaMul * traitMul * ratingMul * s.city.demandFactor * Range(r, 0.85f, 1.2f);
            pay += c.reqBandwidth * 120f;
            c.monthlyPay = Mathf.Round(pay / 50f) * 50f;
            c.signingBonus = Mathf.Round(c.monthlyPay * Range(r, 0.2f, 0.7f) / 50f) * 50f;
            c.expiresHour = s.hour + Catalog.HoursPerDay * r.Next(5, 13);
            c.flavor = $"{c.customer} is a {ind.noun} looking for {Describe(c).ToLower()}.";
            return c;
        }

        public static string Describe(Contract c)
        {
            switch (c.type)
            {
                case ContractType.Colocation: return $"{c.reqRacks} colocation rack{(c.reqRacks > 1 ? "s" : "")} at {c.reqKWPerRack:0} kW each";
                case ContractType.Dedicated: return $"{c.reqServers} dedicated servers with {c.reqCoresPerServer}+ cores each";
                case ContractType.Compute: return $"{c.reqCores} cores of cloud compute";
                case ContractType.Storage: return $"{c.reqStorageTB:0} TB of storage";
                case ContractType.GpuCloud: return $"{c.reqGpus} GPUs of accelerated compute";
            }
            return "";
        }
    }
}
