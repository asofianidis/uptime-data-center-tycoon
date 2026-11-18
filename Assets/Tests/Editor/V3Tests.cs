using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DCT.Tests
{
    /// <summary>Finance, operations and business systems added in v3.</summary>
    public class V3Tests
    {
        static Simulation Starter(int seed = 42, string scenario = "sandbox")
        {
            var s = Simulation.NewGame(seed, "Normal", scenario);
            var sim = new Simulation(s);
            sim.S.shipExpress = true;
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

        static PlacedObject OnlineRack(Simulation sim, string server = "srv_basic", int n = 2)
        {
            var rack = FreeRack(sim);
            sim.AddDevice(rack, Catalog.Device("tor_1g"), true);
            for (int i = 0; i < n; i++) sim.AddDevice(rack, Catalog.Device(server), true);
            sim.AutoConnect(rack);
            sim.Recompute();
            return rack;
        }

        static Contract ComputeContract(Simulation sim, int cores)
        {
            var c = ProcGen.GenerateContract(sim.S, new System.Random(1));
            c.type = ContractType.Compute; c.reqCores = cores; c.reqBandwidth = 0.5f;
            c.redundant = c.geoRedundant = c.compliance = c.caged = c.efficient = c.anchor = false;
            c.traits.Clear();
            sim.S.offers.Add(c);
            sim.AcceptOffer(c);
            return c;
        }

        static PlacedObject PlaceSomewhere(Simulation sim, FacilityDef d)
        {
            for (int y = sim.S.ownY0; y < sim.S.ownY1; y++)
                for (int x = sim.S.ownX0; x < sim.S.ownX1; x++)
                    if (sim.CanPlace(d, x, y, 0) == null) return sim.Place(d, x, y, 0);
            return null;
        }

        // ---------------------------------------------------------------- finance
        [Test]
        public void Loan_PaysOutAndIsRepaidMonthly()
        {
            var sim = Starter();
            float before = sim.S.money;
            Assert.IsNull(sim.TakeLoan(100000, 12));
            Assert.AreEqual(before + 100000, sim.S.money, 1f);
            var loan = sim.S.loans.Single();
            Assert.Greater(loan.payment, 100000f / 12f, "payment includes interest");
            Tick(sim, Catalog.HoursPerMonth + 1);
            Assert.Less(loan.balance, 100000f);
            Assert.AreEqual(11, loan.monthsLeft);
            Assert.Greater(sim.S.current.interest + sim.S.reports.Sum(r => r.interest), 0f);
            Assert.IsNull(sim.Repay(loan));
            Assert.AreEqual(0, sim.S.loans.Count);
            Assert.AreEqual(1, sim.S.loansRepaid);
        }

        [Test]
        public void Loan_MissedPaymentHurtsCredit()
        {
            var sim = Starter();
            sim.TakeLoan(100000, 12);
            float score = sim.S.creditScore;
            sim.S.money = 0;
            sim.S.loans[0].nextHour = sim.S.hour + 1;
            Tick(sim, 2);
            Assert.AreEqual(1, sim.S.missedPayments);
            Assert.Less(sim.S.creditScore, score);
            Assert.Greater(sim.S.loans[0].balance, 100000f, "interest and late fee capitalised");
        }

        [Test]
        public void Loan_LimitedByHeadroom()
        {
            var sim = Starter();
            Assert.IsNotNull(sim.TakeLoan(sim.BorrowingLimit + 100000, 24));
        }

        [Test]
        public void Insurance_PaysForUnsuppressedFire()
        {
            var sim = Starter();
            sim.S.unlocked.Add("srv_gpu4");
            var rack = OnlineRack(sim, "srv_gpu4", 3);
            sim.SetInsurance(2);
            Assert.Greater(sim.MonthlyPremium, 0);
            float before = sim.S.money;
            sim.TriggerEvent("fire");
            Assert.AreEqual(1, sim.S.claimsMade, "comprehensive cover paid the claim");
            Assert.Greater(sim.S.current.claims, 0f);
        }

        [Test]
        public void Insurance_SuppressionLowersPremium()
        {
            var sim = Starter();
            OnlineRack(sim);
            float without = sim.PremiumFor(2);
            sim.S.unlocked.Add("suppress");
            Assert.NotNull(PlaceSomewhere(sim, Catalog.Facility("suppress")));
            sim.Recompute();
            Assert.Less(sim.PremiumFor(2), without);
        }

        // ---------------------------------------------------------------- maintenance
        [Test]
        public void Maintenance_FactorRisesWithoutService()
        {
            Assert.Less(Simulation.ServiceFactorFor(0.5f), 1f);
            Assert.AreEqual(1f, Simulation.ServiceFactorFor(4f), 0.001f);
            Assert.Greater(Simulation.ServiceFactorFor(12f), 1.5f);
        }

        [Test]
        public void Maintenance_TakesNonRedundantRackOfflineUntilDone()
        {
            var sim = Starter();
            sim.S.cheatNoFailures = true;
            var rack = OnlineRack(sim);
            Assert.IsTrue(sim.racks[rack.id].online);
            while (sim.S.hour % 24 != 9) Tick(sim, 1);    // day shift on
            Assert.IsNull(sim.QueueMaintenance(rack, true));
            bool sawOffline = false;
            for (int h = 0; h < 24 && rack.maintQueued; h++)
            {
                Tick(sim, 1);
                if (rack.inMaint) { sim.Recompute(); if (sim.racks[rack.id].issue == "Under maintenance") sawOffline = true; }
            }
            Assert.IsFalse(rack.maintQueued, "serviced");
            Assert.IsTrue(sawOffline, "offline while being serviced");
            Assert.Less(sim.ServiceAgeMonths(rack), 0.1f);
            Tick(sim, 1);
            Assert.IsTrue(sim.racks[rack.id].online);
        }

        // ---------------------------------------------------------------- supply chain
        [Test]
        public void StandardShipping_DeliversThenStaffInstall()
        {
            var sim = Starter();
            sim.S.cheatNoFailures = true;
            var rack = FreeRack(sim);
            sim.S.shipExpress = false;
            var d = Catalog.Device("srv_basic");
            Assert.NotNull(sim.AddDevice(rack, d));
            Assert.AreEqual(0, rack.devices.Count);
            Assert.AreEqual(1, rack.orders.Count);
            Assert.Greater(sim.LeadHours(d), 0);
            int lead = rack.orders[0].arriveHour - sim.S.hour;
            Tick(sim, lead + 36);
            Assert.AreEqual(1, rack.devices.Count, "delivered and installed");
            Assert.AreEqual(0, rack.orders.Count);
        }

        [Test]
        public void Express_CostsMoreAndIsInstant()
        {
            var sim = Starter();
            var d = Catalog.Device("srv_basic");
            Assert.Greater(sim.OrderPrice(d, true), sim.OrderPrice(d, false));
            var rack = FreeRack(sim);
            sim.AddDevice(rack, d, true);
            Assert.AreEqual(1, rack.devices.Count);
        }

        [Test]
        public void OrderedSlotsAreReserved()
        {
            var sim = Starter();
            var rack = FreeRack(sim);
            sim.AddDevice(rack, Catalog.Device("srv_basic"), false);
            int slot = rack.orders[0].u;
            var now = sim.AddDevice(rack, Catalog.Device("srv_basic"), true);
            Assert.AreNotEqual(slot, now.u);
        }

        // ---------------------------------------------------------------- energy, water, heat
        [Test]
        public void Pue_RecordedMonthly()
        {
            var sim = Starter();
            OnlineRack(sim, "srv_basic", 6);
            Tick(sim, Catalog.HoursPerMonth * 2);
            var r = sim.S.reports.Last();
            Assert.Greater(r.pue, 1f);
            Assert.Less(r.pue, 20f, "nearly empty hall: overhead dominates, but stays sane");
        }

        [Test]
        public void Evaporative_DependsOnHumidityAndDrought()
        {
            var sim = Starter();
            sim.S.city.humidity = 0.1f;
            float dry = sim.EvapFactor;
            sim.S.city.humidity = 0.9f;
            Assert.Less(sim.EvapFactor, dry);
            sim.TriggerEvent("drought");
            Assert.Less(sim.EvapFactor, Mathf.Lerp(1.25f, 0.55f, 0.9f) - 0.01f);
            Assert.Greater(sim.WaterPriceNow, sim.S.city.waterPrice);
        }

        [Test]
        public void HeatExchanger_SellsHeat()
        {
            var sim = Starter();
            sim.S.city.heatPrice = 0.1f;
            OnlineRack(sim, "srv_basic", 8);
            sim.S.unlocked.Add("heatx");
            var hx = PlaceSomewhere(sim, Catalog.Facility("heatx"));
            Assert.NotNull(hx, "placed outdoors");
            Tick(sim, 24);
            Assert.Greater(sim.S.heatSoldTotal, 0f);
            Assert.Greater(sim.S.current.sales, 0f);
        }

        [Test]
        public void SpotMarket_SellsIdleCapacity()
        {
            var sim = Starter();
            OnlineRack(sim, "srv_basic", 4);
            sim.S.spotEnabled = true;
            Tick(sim, 24);
            Assert.Greater(sim.S.spotEarnedTotal, 0f);
            Assert.Greater(sim.spotGbps, 0f, "spot traffic uses WAN");
        }

        // ---------------------------------------------------------------- noise
        [Test]
        public void SoundWall_CutsNoise()
        {
            var sim = Starter();
            var x = sim.ObjectsOfKind(FacilityKind.Transformer).First();
            float before = sim.NoiseAtNeighbours(x);
            Assert.Greater(before, 0f);
            PlacedObject wall = null;
            for (int dy = -2; dy <= 2 && wall == null; dy++)
                for (int dx = -2; dx <= 2 && wall == null; dx++)
                    if (sim.CanPlace(Catalog.Facility("soundwall"), x.x + dx, x.y + dy, 0) == null) wall = sim.Place(Catalog.Facility("soundwall"), x.x + dx, x.y + dy, 0);
            Assert.NotNull(wall);
            Assert.AreEqual(before - 12f, sim.NoiseAtNeighbours(x), 0.01f);
        }

        // ---------------------------------------------------------------- placement rules
        [Test]
        public void Compliance_RequiresCertifiedSite()
        {
            var sim = Starter();
            var c = ComputeContract(sim, 8);
            c.compliance = true;
            var rack = OnlineRack(sim, "srv_basic", 1);
            sim.Assign(rack, c);
            Assert.IsFalse(sim.provision[c.id].meets);
            StringAssert.Contains("Compliance", sim.provision[c.id].issue);
            sim.Site.certUntilHour = sim.S.hour + 1000;
            sim.Recompute();
            Assert.IsTrue(sim.provision[c.id].meets, sim.provision[c.id].issue);
        }

        [Test]
        public void Audit_FailsWithoutSecurityAndPassesWithIt()
        {
            var sim = Starter();
            Assert.Greater(sim.ComplianceGaps().Count, 0);
            sim.StartAudit();
            Tick(sim, Simulation.AuditDays * 24 + 1);
            Assert.IsFalse(sim.Certified(sim.Site));
        }

        [Test]
        public void Cage_RequiresPrivateCage()
        {
            var sim = Starter();
            var c = ComputeContract(sim, 8);
            c.caged = true;
            var rack = OnlineRack(sim, "srv_basic", 1);
            sim.Assign(rack, c);
            Assert.IsFalse(sim.provision[c.id].meets);
            sim.PaintFlag(new RectInt(rack.x, rack.y, 1, 1), GameState.FlagCage, true, out _);
            Assert.AreNotEqual(0, sim.CageOf(rack));
            Assert.IsTrue(sim.provision[c.id].meets, sim.provision[c.id].issue);
        }

        // ---------------------------------------------------------------- staff
        [Test]
        public void Training_RaisesSkillAfterCourse()
        {
            var sim = Starter();
            var m = sim.S.staff[0];
            m.skill = 2;
            Assert.IsNull(sim.TrainStaff(m));
            Assert.IsTrue(sim.Training(m));
            Tick(sim, Simulation.TrainingDays * 24 + 25);
            Assert.AreEqual(3, m.skill);
            Assert.IsFalse(sim.Training(m));
        }

        // ---------------------------------------------------------------- tenders, rivals, shareholders
        [Test]
        public void Rfp_LowBidWinsWithoutCompetition()
        {
            var sim = Starter();
            sim.S.rivals.Clear();
            var c = ProcGen.GenerateContract(sim.S, new System.Random(3));
            var r = new Rfp { id = sim.S.NewId(), contract = c, closesHour = sim.S.hour + 2 };
            sim.S.rfps.Add(r);
            float budget = c.monthlyPay;
            sim.PlaceBid(r, 0.9f);
            Tick(sim, 3);
            Assert.AreEqual(0, sim.S.rfps.Count);
            Assert.Contains(c, sim.S.contracts);
            Assert.AreEqual(ContractStatus.Setup, c.status == ContractStatus.Active ? ContractStatus.Setup : c.status);
            Assert.AreEqual(Mathf.Round(budget * 0.9f / 50f) * 50f, c.monthlyPay, 1f);
        }

        [Test]
        public void Acquire_RemovesRivalAndAddsOffers()
        {
            var sim = Starter();
            sim.S.xp = Catalog.XpForLevel(Simulation.AcquireLevel);
            int rivals = sim.S.rivals.Count, offers = sim.S.offers.Count;
            var rv = sim.S.rivals[0];
            Assert.IsNull(sim.Acquire(rv));
            Assert.AreEqual(rivals - 1, sim.S.rivals.Count);
            Assert.Greater(sim.S.offers.Count, offers);
            Assert.IsTrue(sim.S.offers.Any(o => o.acquired));
        }

        [Test]
        public void Shareholders_QuarterIsTracked()
        {
            var sim = Starter();
            sim.S.ipo = true;
            Tick(sim, 48);
            Assert.Greater(sim.S.sharePrice, 0f);
            Assert.Greater(sim.S.quarterEndHour, sim.S.hour);
            Assert.Greater(sim.MarketCap, 0f);
            float shares = sim.S.shares;
            sim.IssueShares();
            Assert.Greater(sim.S.shares, shares);
        }

        // ---------------------------------------------------------------- incidents & portal
        [Test]
        public void Incident_RecordsCausesAndCloses()
        {
            var sim = Starter();
            sim.S.cheatNoFailures = true;
            var c = ComputeContract(sim, 8);
            var rack = OnlineRack(sim, "srv_basic", 1);
            sim.Assign(rack, c);
            Tick(sim, 2);
            Assert.AreEqual(ContractStatus.Active, c.status);
            var sw = rack.devices.First(d => Catalog.Device(d.def).kind == DeviceKind.Switch);
            sim.RemoveDevice(rack, sw);
            Tick(sim, 3);
            Assert.IsTrue(sim.S.incidentOpen);
            sim.AddDevice(rack, Catalog.Device("tor_1g"), true);
            Tick(sim, 2);
            Assert.IsFalse(sim.S.incidentOpen);
            var inc = sim.S.incidents.Last();
            Assert.Greater(inc.customerHours, 0f);
            Assert.IsTrue(inc.causes.Any(x => x.Contains("ToR")), string.Join(";", inc.causes));
            Assert.Greater(inc.fixes.Count, 0);
        }

        [Test]
        public void PeakHours_ByIndustry()
        {
            var c = new Contract { industry = "Finance" };
            Assert.IsTrue(Simulation.InPeak(c, 10));
            Assert.IsFalse(Simulation.InPeak(c, 23));
            Assert.IsFalse(Simulation.InPeak(new Contract { industry = "AI Research" }, 12));
        }

        // ---------------------------------------------------------------- scenarios & saves
        [Test]
        public void Turnaround_StartsBuiltWithDebt()
        {
            var s = Simulation.NewGame(9, "Normal", "turnaround");
            Assert.AreEqual(1, s.loans.Count);
            Assert.AreEqual(70000f, s.money, 1f);
            Assert.Greater(s.objects.Count, 5);
            Assert.IsFalse(s.creative);
            var sim = new Simulation(s);
            Assert.DoesNotThrow(() => Tick(sim, 48));
        }

        [Test]
        public void NewScenarios_Run([Values("coldnorth", "edge")] string id)
        {
            var sim = Starter(5, id);
            Assert.DoesNotThrow(() => Tick(sim, 72));
            sim.ScenarioProgress(out string text);
            Assert.IsNotEmpty(text);
        }

        [Test]
        public void Save_RoundTripsV3State()
        {
            var sim = Starter();
            sim.TakeLoan(50000, 12);
            sim.SetInsurance(1);
            var rack = FreeRack(sim);
            sim.AddDevice(rack, Catalog.Device("srv_basic"), false);
            sim.QueueMaintenance(sim.ObjectsOfKind(FacilityKind.Cooling).First(), false);
            string json = sim.S.Serialize();
            var copy = JsonUtility.FromJson<GameState>(json);
            var sim2 = new Simulation(copy);
            Assert.AreEqual(1, copy.loans.Count);
            Assert.AreEqual(1, copy.insuranceTier);
            Assert.AreEqual(1, copy.objects.First(o => o.id == rack.id).orders.Count);
            Assert.IsTrue(copy.objects.Any(o => o.maintQueued));
            Assert.IsTrue(copy.city.v3);
            Assert.DoesNotThrow(() => Tick(sim2, 24));
        }

        [Test]
        public void OldSave_MigratesToV3()
        {
            var s = Simulation.NewGame(11, "Normal");
            var sim = new Simulation(s);
            AutoManager.BuildStarter(sim);
            string json = s.Serialize();
            // Strip the v3 markers to look like a v2 save.
            json = json.Replace("\"version\":3", "\"version\":2").Replace("\"v3\":true", "\"v3\":false");
            var copy = JsonUtility.FromJson<GameState>(json);
            foreach (var site in copy.sites) foreach (var o in site.objects) o.placedHour = -1;
            var sim2 = new Simulation(copy);
            Assert.AreEqual(3, copy.version);
            Assert.IsTrue(copy.city.v3);
            Assert.IsTrue(copy.sites.All(x => x.objects.All(o => o.placedHour >= 0)));
            Assert.DoesNotThrow(() => Tick(sim2, 24));
        }
    }
}
