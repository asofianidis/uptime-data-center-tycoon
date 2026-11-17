using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DCT.Tests
{
    public class FloorTests
    {
        Simulation sim;
        RectInt building;

        [SetUp]
        public void Setup()
        {
            var s = Simulation.NewGame(42, "Normal", "sandbox");
            sim = new Simulation(s);
            AutoManager.BuildStarter(sim);
            sim.S.money = 50_000_000;
            int w = s.ownX1 - s.ownX0, h = s.ownY1 - s.ownY0;
            int cx = s.ownX0 + w / 2, cy = s.ownY0 + h / 2;
            building = new RectInt(cx - 7, cy - 5, 15, 11);   // the starter building footprint
        }

        PlacedObject PlaceOn(int floor, string id, RoomType room, int x, int y)
        {
            sim.SetFloor(floor);
            sim.PaintRoom(new RectInt(x, y, 1, 1), room, true, out _);
            var o = sim.Place(Catalog.Facility(id), x, y, 0);
            Assert.NotNull(o, $"{id} on floor {floor}: {sim.CanPlace(Catalog.Facility(id), x, y, 0)}");
            return o;
        }

        [Test]
        public void UpperFloor_NeedsStructureBelow()
        {
            sim.SetFloor(1);
            Assert.AreEqual(1, sim.S.floor);
            int outside = sim.BuildFoundation(new RectInt(sim.S.ownX0, sim.S.ownY0, 2, 2), false, out _);
            Assert.AreEqual(0, outside, "no floor slab over open land");
            int over = sim.BuildFoundation(building, true, out float cost);
            Assert.AreEqual(building.width * building.height, over);
            Assert.AreEqual(1, sim.S.TopFloor);
            sim.SetFloor(3);
            Assert.AreEqual(2, sim.S.floor, "can only go one floor above the top floor");
            // Ground-floor slab under floor 1 can't be removed while it holds the floor up.
            sim.SetFloor(0);
            var tile = new RectInt(building.x + 7, building.y + 3, 1, 1);
            var o = sim.ObjectAt(tile.x, tile.y);
            if (o != null) sim.RemoveObject(o);
            sim.Demolish(tile, true, out _, out _);                // room layer -> corridor (if painted)
            int n = sim.Demolish(tile, false, out _, out string what);
            Assert.AreEqual(0, n);
            Assert.That(what, Does.Contain("floor above"));
        }

        [Test]
        public void CrossFloorCable_NeedsRiserColumn()
        {
            sim.SetFloor(1);
            sim.BuildFoundation(building, true, out _);
            var core = sim.ObjectsOfKind(FacilityKind.CoreSwitch).First();
            sim.SetFloor(1);
            sim.PaintRoom(new RectInt(building.x, building.y + 6, 6, 4), RoomType.ServerHall, true, out _);
            var rack = sim.Place(Catalog.Facility("rack_std"), building.x + 2, building.y + 7, 0);
            Assert.NotNull(rack);
            Assert.AreEqual(1, rack.floor);
            Assert.That(sim.CableError(rack, core), Does.Contain("Riser"));

            // Riser column at the same spot on floors 0 and 1, next to the network room.
            int ex = core.x - 1, ey = core.y - 1;
            sim.SetFloor(0);
            var e0 = sim.ObjectAt(ex, ey);
            if (e0 != null) sim.RemoveObject(e0);
            PlaceOn(0, "elevator", RoomType.Corridor, ex, ey);
            PlaceOn(1, "elevator", RoomType.Corridor, ex, ey);
            sim.SetFloor(1);
            AutoManager.LayTray(sim, rack, sim.ObjectAtF(1, ex, ey));
            Assert.IsNull(sim.CableError(rack, core));
            sim.AddDevice(rack, Catalog.Device("tor_1g"));
            sim.AddDevice(rack, Catalog.Device("srv_basic"));
            Assert.IsNull(sim.Connect(rack, core));
            sim.Recompute();
            Assert.IsTrue(sim.racks[rack.id].online, sim.racks[rack.id].issue);
        }

        [Test]
        public void Staff_RideTheElevatorToRepairUpstairs()
        {
            var core = sim.ObjectsOfKind(FacilityKind.CoreSwitch).First();
            sim.SetFloor(1);
            sim.BuildFoundation(building, true, out _);
            sim.PaintRoom(new RectInt(building.x, building.y + 6, 6, 4), RoomType.ServerHall, true, out _);
            var rack = sim.Place(Catalog.Facility("rack_std"), building.x + 2, building.y + 7, 0);
            var dev = sim.AddDevice(rack, Catalog.Device("srv_basic"));
            int ex = building.x + 6, ey = building.y + 2;
            PlaceOn(0, "elevator", RoomType.Corridor, ex, ey);
            PlaceOn(1, "elevator", RoomType.Corridor, ex, ey);
            sim.SetFloor(0);
            var m = sim.S.staff[0];
            m.shift = sim.OnShift(new StaffMember { shift = "Day" }) ? "Day" : "Night";
            dev.failed = true;
            for (int h = 0; h < 24 && dev.failed; h++) sim.TickHour();
            Assert.IsFalse(dev.failed, "technician reached floor 2 through the elevator");
        }

        [Test]
        public void Flood_DoesNotReachUpperFloors()
        {
            sim.SetFloor(1);
            sim.BuildFoundation(building, true, out _);
            sim.PaintRoom(new RectInt(building.x, building.y + 6, 6, 4), RoomType.ServerHall, true, out _);
            var rack = sim.Place(Catalog.Facility("rack_std"), building.x + 2, building.y + 7, 0);
            for (int i = 0; i < 4; i++) sim.AddDevice(rack, Catalog.Device("srv_basic"));
            for (int k = 0; k < 20; k++) sim.TriggerEvent("flood");
            Assert.IsFalse(rack.devices.Any(d => d.failed));
        }

        [Test]
        public void OldSaves_GetExpandedFloorArrays()
        {
            var s = Simulation.NewGame(5, "Normal");
            // Simulate a pre-floors save: single-plane arrays and no site list.
            s.rooms = new int[s.mapW * s.mapH];
            s.rooms[10] = (int)RoomType.Corridor;
            s.flags = new int[s.mapW * s.mapH];
            s.sites.Clear();
            var json = JsonUtility.ToJson(s);
            var copy = JsonUtility.FromJson<GameState>(json);
            var sim2 = new Simulation(copy);
            Assert.AreEqual(copy.mapW * copy.mapH * GameState.MaxFloors, copy.rooms.Length);
            Assert.AreEqual((int)RoomType.Corridor, copy.rooms[10]);
            Assert.DoesNotThrow(() => sim2.TickHour());
        }

        [Test]
        public void Zones_AreSeparatePerFloor()
        {
            sim.SetFloor(1);
            sim.BuildFoundation(building, true, out _);
            sim.PaintRoom(new RectInt(building.x, building.y + 6, 6, 4), RoomType.ServerHall, true, out _);
            sim.Recompute();
            Assert.IsTrue(sim.zones.Any(z => z.floor == 0));
            Assert.IsTrue(sim.zones.Any(z => z.floor == 1));
        }
    }
}
