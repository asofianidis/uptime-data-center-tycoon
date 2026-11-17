using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DCT.EditorTools
{
    public static class BuildTools
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Uptime/Setup Main Scene")]
        public static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            new GameObject("GameManager").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Uptime - Data Center Tycoon";
            PlayerSettings.companyName = "Uptime";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Uptime] Scene set up at " + ScenePath);
        }

        [MenuItem("Uptime/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) SetupScene();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Uptime.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Uptime] Build result: {report.summary.result}, size {report.summary.totalSize / (1024 * 1024)} MB, errors {report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Writes the equipment catalog to Resources/Data/catalog.json, which then overrides the in-code defaults.</summary>
        [MenuItem("Uptime/Export Catalog JSON")]
        public static void ExportCatalog()
        {
            Directory.CreateDirectory("Assets/Resources/Data");
            File.WriteAllText("Assets/Resources/Data/catalog.json", Catalog.ExportJson());
            AssetDatabase.Refresh();
            Debug.Log("[Uptime] Catalog exported to Assets/Resources/Data/catalog.json");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("Uptime/Check Models")]
        public static void CheckModels()
        {
            var sb = new StringBuilder();
            foreach (var go in Resources.LoadAll<GameObject>("Models"))
            {
                var inst = UnityEngine.Object.Instantiate(go);
                var rs = inst.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"{go.name}: size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00}  center {b.center.x:0.00},{b.center.y:0.00},{b.center.z:0.00}  rot {inst.transform.localEulerAngles}  mats {string.Join(",", rs.SelectMany(r => r.sharedMaterials).Select(m => m.name + ":" + m.shader.name).Distinct().Take(4))}");
                UnityEngine.Object.DestroyImmediate(inst);
            }
            File.WriteAllText("Logs/models.txt", sb.ToString());
            Debug.Log("[Uptime] Models checked");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Headless balance/crash test: several seeds, an automated player, two in-game years.</summary>
        [MenuItem("Uptime/Run Simulation Test")]
        public static void RunSimTest()
        {
            var sb = new StringBuilder();
            bool ok = true;
            var causes = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var seed in new[] { 1, 42, 1337, 98765, 555 })
            {
                foreach (var diff in new[] { "Normal", "Hard" })
                {
                    try
                    {
                        var s = Simulation.NewGame(seed, diff);
                        var sim = new Simulation(s);
                        AutoManager.BuildStarter(sim);
                        int days = 720;
                        float minMoney = float.MaxValue;
                        for (int d = 0; d < days && !s.gameOver; d++)
                        {
                            AutoManager.DailyManage(sim);
                            for (int h = 0; h < 24; h++)
                            {
                                sim.TickHour();
                                foreach (var c in s.contracts)
                                    if (c.status == ContractStatus.Active && !c.meeting && c.lastIssue != null)
                                    {
                                        string k = c.lastIssue;
                                        int ri = k.IndexOf("rack issue:");
                                        if (ri >= 0) k = "RACK " + k.Substring(ri + 11).Trim();
                                        k = System.Text.RegularExpressions.Regex.Replace(k, "[0-9.]+", "#");
                                        causes[k] = (causes.TryGetValue(k, out int n) ? n : 0) + 1;
                                    }
                                while (sim.popups.Count > 0)
                                {
                                    var p = sim.popups.Dequeue();
                                    p.options[0].Value?.Invoke();
                                }
                            }
                            minMoney = Mathf.Min(minMoney, s.money);
                            if (d % 90 == 89)
                                sb.AppendLine($"  seed {seed} {diff} day {d + 1}: ${s.money:N0} lvl {s.Level} xp {s.xp} rep {s.rating:0} racks {sim.Racks.Count()} servers {s.objects.Sum(o => o.devices.Count)} active {s.contracts.Count(c => c.status == ContractStatus.Active)} done {s.contractsCompleted} failed {s.contractsFailed} load {sim.loadKW:0}kW staff {s.staff.Count}/{sim.deskCount} [{string.Join(",", s.staff.Select(m => m.state[0]))}] failedDev {s.objects.Sum(o => o.devices.Count(x => x.failed))} failedFac {s.objects.Count(o => o.failed)} pdu {s.objects.Count(o => o.pduTripped)} tickets {s.tickets.Count} done {s.ticketsDone} miss {s.ticketsMissed}");
                        }
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int h = 0; h < 48; h++) sim.TickHour();
                        sw.Stop();
                        sb.AppendLine($"  perf: {sw.Elapsed.TotalMilliseconds / 48:0.00} ms per game hour with {sim.Racks.Count} racks, {s.objects.Sum(o => o.devices.Count)} devices, {s.contracts.Count} contracts");
                        // Round-trip save
                        string json = JsonUtility.ToJson(s);
                        var s2 = JsonUtility.FromJson<GameState>(json);
                        var sim2 = new Simulation(s2);
                        sim2.TickHour();
                        sb.AppendLine($"seed {seed} {diff}: final ${s.money:N0}, min ${minMoney:N0}, level {s.Level}, unlocked {s.unlocked.Count}, completed {s.contractsCompleted}, failed {s.contractsFailed}, gameOver {s.gameOver}, save {json.Length / 1024}KB");
                    }
                    catch (Exception e)
                    {
                        ok = false;
                        sb.AppendLine($"seed {seed} {diff}: EXCEPTION {e}");
                    }
                }
            }
            foreach (var sc in Simulation.Scenarios)
            {
                try
                {
                    var s = Simulation.NewGame(77, "Normal", sc.id);
                    var sim = new Simulation(s);
                    AutoManager.BuildStarter(sim);
                    for (int d = 0; d < 360 && !s.gameOver; d++)
                    {
                        AutoManager.DailyManage(sim);
                        for (int h = 0; h < 24; h++)
                        {
                            sim.TickHour();
                            while (sim.popups.Count > 0) sim.popups.Dequeue().options[0].Value?.Invoke();
                        }
                    }
                    float prog = sim.ScenarioProgress(out string txt);
                    sb.AppendLine($"scenario {sc.id}: ${s.money:N0} lvl {s.Level} progress {prog:P0} ({txt}) done {s.scenarioDone} achievements {s.achievements.Count} staff {s.staff.Count} solar {sim.solarCapKW:0}kW");
                }
                catch (Exception e) { ok = false; sb.AppendLine($"scenario {sc.id}: EXCEPTION {e}"); }
            }
            foreach (var kv in causes.OrderByDescending(x => x.Value)) sb.AppendLine($"  downtime cause: {kv.Value}h  {kv.Key}");
            // Contract generator variety sample
            var gs = Simulation.NewGame(7, "Normal");
            var rng = new System.Random(3);
            for (int lvl = 1; lvl <= 9; lvl += 4)
            {
                gs.xp = Catalog.XpForLevel(lvl);
                for (int i = 0; i < 4; i++)
                {
                    var c = ProcGen.GenerateContract(gs, rng);
                    sb.AppendLine($"  L{lvl} sample: {c.customer} [{c.industry}] {ProcGen.Describe(c)} {c.reqBandwidth}G SLA {c.sla} {c.months}mo ${c.monthlyPay:N0}/mo traits {string.Join(",", c.traits)}");
                }
            }
            File.WriteAllText("Logs/simtest.txt", sb.ToString());
            Debug.Log("[Uptime] SimTest\n" + sb);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
