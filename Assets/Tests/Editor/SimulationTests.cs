using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DCT.Tests
{
    public class SimulationTests
    {
        static Simulation Starter(int seed = 42, string scenario = "sandbox")
        {
            var s = Simulation.NewGame(seed, "Normal", scenario);
            var sim = new Simulation(s);
            sim.S.shipExpress = true;   // tests install hardware instantly unless they test deliveries
            AutoManager.BuildStarter(sim);
            return sim;
        }

        static void RunDays(Simulation sim, int days, bool manage = true)
        {
            for (int d = 0; d < days; d++)
            {
                if (manage) AutoManager.DailyManage(sim);
                for (int h = 0; h < 24; h++)
                {
                    sim.TickHour();
                    while (sim.popups.Count > 0) sim.popups.Dequeue().options[0].Value?.Invoke();
                }
            }
        }

        [Test]
        public void NewGame_IsDeterministicPerSeed()
        {
            var a = Simulation.NewGame(1234, "Normal");
            var b = Simulation.NewGame(1234, "Normal");
            var c = Simulation.NewGame(4321, "Normal");
            Assert.AreEqual(a.city.name, b.city.name);
            Assert.AreEqual(a.offers.Select(o => o.customer), b.offers.Select(o => o.customer));
            Assert.AreEqual(a.ownX0, b.ownX0);
            Assert.IsFalse(a.city.name == c.city.name && a.ownX0 == c.ownX0 && a.offers[0].customer == c.offers[0].customer, "different seeds should differ");
        }

        [Test]
        public void StarterBuild_HasPowerNetworkAndCooling()
        {
            var sim = Starter();
            Assert.Greater(sim.supplyKW, 0, "supply");
            Assert.Greater(sim.corePorts, 0, "core ports");
            Assert.Greater(sim.ispGbps, 0, "isp");
            Assert.Greater(sim.zones.Count, 0, "server hall zone");
            Assert.Greater(sim.zones[0].coolKW, 0, "cooling");
            Assert.AreEqual(1, sim.S.staff.Count, "one hire");
        }

        [Test]
        public void Simulation_RunsSixMonthsWithoutExceptions([Values(1, 98765)] int seed)
        {
            var sim = Starter(seed);
            RunDays(sim, 180);
            Assert.Greater(sim.S.contracts.Count, 0);
            Assert.Greater(sim.S.lifetimeRevenue, 0);
        }

        [Test]
        public void SaveLoad_RoundTripsState()
        {
            var sim = Starter();
            RunDays(sim, 40);
            string json = JsonUtility.ToJson(sim.S);
            var copy = JsonUtility.FromJson<GameState>(json);
            Assert.AreEqual(sim.S.money, copy.money, 0.01);
            Assert.AreEqual(sim.S.objects.Count, copy.objects.Count);
            Assert.AreEqual(sim.S.staff.Count, copy.staff.Count);
            Assert.AreEqual(sim.S.contracts.Count, copy.contracts.Count);
            Assert.AreEqual(sim.S.objects.Sum(o => o.devices.Count), copy.objects.Sum(o => o.devices.Count));
            var sim2 = new Simulation(copy);
            Assert.DoesNotThrow(() => { for (int i = 0; i < 48; i++) sim2.TickHour(); });
        }

        static PlacedObject PlaceRack(Simulation sim, int rot)
        {
            var def = Catalog.Facility("rack_std");
            for (int y = sim.S.ownY0; y < sim.S.ownY1; y++)
                for (int x = sim.S.ownX0; x < sim.S.ownX1; x++)
                    if (sim.CanPlace(def, x, y, rot) == null && sim.CanPlace(def, x, y + 1, rot) == null && sim.ObjectAt(x, y + 2) == null && sim.ObjectAt(x, y - 1) == null)
                        return sim.Place(def, x, y, rot);
            return null;
        }

        [Test]
        public void Airflow_RackBreathingExhaustRunsHotter()
        {
            var sim = Starter();
            var def = Catalog.Facility("rack_std");
            PlacedObject a = null, b = null;
            for (int y = sim.S.ownY0; y < sim.S.ownY1 && a == null; y++)
                for (int x = sim.S.ownX0; x < sim.S.ownX1 && a == null; x++)
                    if (sim.CanPlace(def, x, y, 0) == null && sim.CanPlace(def, x, y - 1, 0) == null && sim.ObjectAt(x, y - 2) == null)
                    {
                        a = sim.Place(def, x, y, 0);      // faces -y
                        b = sim.Place(def, x, y - 1, 0);  // directly in front, same direction
                    }
            Assert.NotNull(a); Assert.NotNull(b);
            Assert.AreEqual(1.35f, sim.AirflowFactor(a, out _), 0.001f);
            sim.RotateRack(b); sim.RotateRack(b);           // now faces a: front-to-front, no aisle
            Assert.AreEqual(1.2f, sim.AirflowFactor(a, out _), 0.001f);
        }

        [Test]
        public void Redundancy_RackSurvivesPrimaryCoreFailure()
        {
            var sim = Starter();
            sim.S.money = 1_000_000;
            // Second core switch in the network room.
            var coreDef = Catalog.Facility("core_10g");
            bool placed = false;
            for (int y = sim.S.ownY0; y < sim.S.ownY1 && !placed; y++)
                for (int x = sim.S.ownX0; x < sim.S.ownX1 && !placed; x++)
                    if (sim.CanPlace(coreDef, x, y, 0) == null) placed = sim.Place(coreDef, x, y, 0) != null;
            Assert.IsTrue(placed, "second core");
            var rack = PlaceRack(sim, 0);
            Assert.NotNull(rack);
            sim.AddDevice(rack, Catalog.Device("srv_basic"));
            foreach (var core in sim.ObjectsOfKind(FacilityKind.CoreSwitch).ToList()) AutoManager.LayTray(sim, rack, core);
            Assert.IsNull(sim.MakeRedundant(rack));
            sim.Recompute();
            Assert.IsTrue(sim.racks[rack.id].redundant, "redundant after MakeRedundant");
            sim.objById[rack.linkCore].failed = true;
            sim.Recompute();
            Assert.IsTrue(sim.racks[rack.id].online, "still online on uplink B");
            Assert.IsTrue(sim.racks[rack.id].degraded, "flagged degraded");
        }

        [Test]
        public void Staff_WalkOverAndRepairFailedDevice()
        {
            var sim = Starter();
            sim.S.money = 1_000_000;
            var rack = PlaceRack(sim, 0);
            sim.AddDevice(rack, Catalog.Device("tor_1g"));
            var dev = sim.AddDevice(rack, Catalog.Device("srv_basic"));
            dev.failed = true;
            for (int h = 0; h < 24 && dev.failed; h++) sim.TickHour();
            Assert.IsFalse(dev.failed, "technician should have repaired the server within a day");
        }

        [Test]
        public void Undo_CheckpointFiresOnBuildActions()
        {
            var sim = Starter();
            int n = 0;
            sim.BeforeChange += () => n++;
            PlaceRack(sim, 0);
            Assert.Greater(n, 0);
        }

        [Test]
        public void Contracts_GeneratedForEveryScenarioAreValid()
        {
            foreach (var sc in Simulation.Scenarios)
            {
                var s = Simulation.NewGame(9, "Normal", sc.id);
                var r = new System.Random(5);
                for (int lvl = 1; lvl <= 10; lvl += 3)
                {
                    s.xp = Catalog.XpForLevel(lvl);
                    for (int i = 0; i < 25; i++)
                    {
                        var c = ProcGen.GenerateContract(s, r);
                        Assert.Greater(c.monthlyPay, 0, $"{sc.id} pay");
                        Assert.Greater(c.reqBandwidth, 0, $"{sc.id} bandwidth");
                        Assert.Greater(c.months, 0);
                        Assert.IsFalse(string.IsNullOrEmpty(c.customer));
                    }
                }
            }
        }

        [Test]
        public void Catalog_JsonRoundTrip()
        {
            var json = Catalog.ExportJson();
            var data = JsonUtility.FromJson<Catalog.CatalogData>(json);
            Assert.AreEqual(Catalog.Devices.Count, data.devices.Count);
            Assert.AreEqual(Catalog.Facilities.Count, data.facilities.Count);
            Assert.AreEqual(Catalog.Facilities[0].rackKW, data.facilities[0].rackKW);
        }

        [Test]
        public void Solar_ProducesOnlyInDaylight()
        {
            Assert.AreEqual(0f, Simulation.SunCurve(2));
            Assert.AreEqual(0f, Simulation.SunCurve(22));
            Assert.Greater(Simulation.SunCurve(12), 0.95f);
        }

        [Test]
        public void HardwareAging_IncreasesFailureRateAndLowersResale()
        {
            var sim = Starter();
            var fresh = new RackDevice { def = "srv_basic", ageHours = 0 };
            var old = new RackDevice { def = "srv_basic", ageHours = Simulation.HoursPerYear * 4 };
            Assert.AreEqual(1f, Simulation.AgeFailureFactor(fresh));
            Assert.Greater(Simulation.AgeFailureFactor(old), 3f);
            Assert.Greater(sim.ResaleValue(fresh), sim.ResaleValue(old));
        }
    }
}
