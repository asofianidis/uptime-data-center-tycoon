using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DCT.Tests
{
    public class Phase3Tests
    {
        static Simulation Starter(int seed = 42)
        {
            var s = Simulation.NewGame(seed, "Normal", "sandbox");
            var sim = new Simulation(s);
            AutoManager.BuildStarter(sim);
            sim.S.money = 5_000_000;
            return sim;
        }

        static void Tick(Simulation sim, int hours)
        {
            for (int h = 0; h < hours; h++)
            {
                sim.TickHour();
                while (sim.popups.Count > 0) sim.popups.Dequeue().options[0].Value?.Invoke();
            }
        }

        static PlacedObject FreeRack(Simulation sim)
        {
            var def = Catalog.Facility("rack_std");
            for (int y = sim.S.ownY1 - 1; y >= sim.S.ownY0; y--)
                for (int x = sim.S.ownX0; x < sim.S.ownX1; x++)
                    if (sim.CanPlace(def, x, y, 0) == null && sim.ObjectAt(x, y - 1) == null) return sim.Place(def, x, y, 0);
            return null;
        }

        static Simulation OpenSecondSite(Simulation sim)
        {
            sim.S.xp = Catalog.XpForLevel(Company.MinLevelForSites);
            var city = sim.company.SiteCandidates()[0];
            var site = sim.company.OpenSite(city);
            Assert.NotNull(site, sim.company.CanOpenSite(city));
            sim.company.WithSite(site.siteIndex, () => AutoManager.BuildStarter(site));
            return site;
        }

        [Test]
        public void MultiSite_OpenSiteBuildsIndependentFacility()
        {
            var sim = Starter();
            int hqObjects = sim.S.objects.Count;
            var site2 = OpenSecondSite(sim);
            Assert.AreEqual(2, sim.company.sims.Count);
            Assert.AreEqual(2, sim.S.sites.Count);
            Assert.AreEqual(hqObjects, sim.S.objects.Count, "HQ objects untouched while bound to HQ");
            Assert.Greater(sim.S.sites[1].objects.Count, 0, "site 2 has its own equipment");
            Assert.AreNotSame(sim.S.sites[0].objects, sim.S.sites[1].objects);
            Tick(sim, 72);
            Assert.AreEqual(0, sim.S.bound, "active site stays bound after ticking");
            Assert.Greater(site2.supplyKW, 0);
        }

        [Test]
        public void MultiSite_ContractCanUseRacksInBothSites()
        {
            var sim = Starter();
            var site2 = OpenSecondSite(sim);
            var c = ProcGen.GenerateContract(sim.S, new System.Random(1));
            c.type = ContractType.Compute; c.reqCores = 16; c.reqBandwidth = 0.5f; c.geoRedundant = true;
            sim.S.offers.Add(c);
            sim.AcceptOffer(c);
            // One rack per site, each with 8 cores.
            foreach (var s in new[] { sim, site2 })
                sim.company.WithSite(s.siteIndex, () =>
                {
                    var rack = FreeRack(s);
                    s.AddDevice(rack, Catalog.Device("tor_1g"));
                    s.AddDevice(rack, Catalog.Device("srv_basic"));
                    s.AutoConnect(rack);
                    s.Assign(rack, c);
                });
            sim.company.BindActive();
            Tick(sim, 3);
            var p = sim.provision[c.id];
            Assert.AreEqual(16, p.cores, "cores summed across sites");
            Assert.IsTrue(p.meets, p.issue);
            Assert.AreEqual(ContractStatus.Active, c.status);
        }

        [Test]
        public void MultiSite_SaveLoadKeepsAllSites()
        {
            var sim = Starter();
            OpenSecondSite(sim);
            Tick(sim, 30);
            string json = sim.S.Serialize();
            var copy = JsonUtility.FromJson<GameState>(json);
            var sim2 = new Simulation(copy);
            Assert.AreEqual(2, sim2.company.sims.Count);
            Assert.AreEqual(sim.S.sites[1].objects.Count, copy.sites[1].objects.Count);
            Assert.AreEqual(sim.S.sites[0].objects.Count, copy.objects.Count, "active site restored into top-level fields");
            Assert.DoesNotThrow(() => Tick(sim2, 48));
            // Switching sites rebinds the data.
            var other = sim2.company.SetActive(1);
            Assert.AreEqual(1, copy.bound);
            Assert.AreSame(copy.sites[1].objects, copy.objects);
            Assert.DoesNotThrow(() => Tick(other, 24));
        }

        [Test]
        public void CableTrays_RequiredForLongRunsAndHalveCost()
        {
            var sim = Starter();
            var core = sim.ObjectsOfKind(FacilityKind.CoreSwitch).First();
            // Far-away rack on new foundation.
            var r = new RectInt(sim.S.ownX0 + 1, sim.S.ownY0 + 1, 2, 2);
            sim.Demolish(r, true, out _, out _);
            sim.BuildFoundation(r, true, out _);
            sim.PaintRoom(r, RoomType.ServerHall, true, out _);
            var rack = sim.Place(Catalog.Facility("rack_std"), r.x, r.y, 0);
            Assert.NotNull(rack);
            int dist = Mathf.Abs(rack.x - core.x) + Mathf.Abs(rack.y - core.y);
            Assume.That(dist > Simulation.MaxLooseCable, "lot layout put the rack close to the core");
            Assert.IsNotNull(sim.CableError(rack, core), "long loose run is refused");
            float loose = sim.CableCost(rack, core);
            AutoManager.LayTrayToCore(sim, rack);
            Assert.IsTrue(sim.TrayRouted(rack, core.id));
            Assert.IsNull(sim.CableError(rack, core));
            Assert.Less(sim.CableCost(rack, core), loose);
            Assert.AreEqual(core.id, rack.linkCore);
            Assert.NotNull(sim.TrayPath(rack, core));
        }

        [Test]
        public void Battery_ChargesAtNightAndDischargesAtPeak()
        {
            var sim = Starter();
            sim.S.unlocked.Add("bess");
            var def = Catalog.Facility("bess");
            PlacedObject bat = null;
            for (int y = sim.S.ownY0; y < sim.S.ownY1 && bat == null; y++)
                for (int x = sim.S.ownX0; x < sim.S.ownX1 && bat == null; x++)
                    if (sim.CanPlace(def, x, y, 0) == null) bat = sim.Place(def, x, y, 0);
            if (bat == null)
            {
                // Make room: a dedicated power room strip outside the starter building.
                var r = new RectInt(sim.S.ownX0 + 1, sim.S.ownY0 + 1, 3, 2);
                sim.Demolish(r, true, out _, out _);
                sim.BuildFoundation(r, true, out _);
                sim.PaintRoom(r, RoomType.Power, true, out _);
                bat = sim.Place(def, r.x, r.y, 0);
            }
            Assert.NotNull(bat);
            while (sim.S.hour % 24 != 1) Tick(sim, 1);
            Tick(sim, 4);                         // 01:00-05:00 cheap power
            Assert.Greater(bat.charge, 100f, "charged overnight");
            while (sim.S.hour % 24 != 17) Tick(sim, 1);
            float before = bat.charge;
            Tick(sim, 3);
            if (sim.loadKW > sim.solarKW) Assert.Less(bat.charge, before, "discharged at the evening peak");
        }

        [Test]
        public void Shifts_DayStaffGoOffShiftAtNight()
        {
            var sim = Starter();
            var m = sim.S.staff[0];
            m.shift = "Day";
            while (sim.S.hour % 24 != 22) Tick(sim, 1);
            Assert.AreEqual("off", m.state);
            Assert.IsFalse(sim.OnShift(m));
            while (sim.S.hour % 24 != 9) Tick(sim, 1);
            Assert.AreNotEqual("off", m.state);
        }

        [Test]
        public void Fatigue_SlowsWorkAboveSixty()
        {
            Assert.AreEqual(1f, Simulation.FatigueMul(new StaffMember { fatigue = 30 }));
            Assert.Less(Simulation.FatigueMul(new StaffMember { fatigue = 95 }), 0.6f);
        }

        [Test]
        public void Flood_RaisedFloorProtectsRacks()
        {
            var sim = Starter();
            var rack = FreeRack(sim);
            for (int i = 0; i < 4; i++) sim.AddDevice(rack, Catalog.Device("srv_basic"));
            sim.PaintFlag(new RectInt(rack.x, rack.y, 1, 1), GameState.FlagRaised, true, out _);
            Assert.IsTrue(sim.S.HasFlag(rack.x, rack.y, GameState.FlagRaised));
            for (int k = 0; k < 20; k++) sim.TriggerEvent("flood");
            Assert.IsFalse(rack.devices.Any(d => d.failed), "raised floor kept the rack dry");
        }
    }
}
