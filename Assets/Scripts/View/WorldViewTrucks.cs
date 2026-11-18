using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>Delivery trucks: when hardware orders arrive at the viewed site, a truck drives up the access road
    /// to the loading dock (or the kerb if there isn't one), waits, and leaves. Purely visual.</summary>
    public partial class WorldView
    {
        class TruckAnim { public GameObject go; public Vector3 a, b; public float t; }

        readonly List<TruckAnim> trucks = new List<TruckAnim>();
        readonly HashSet<Simulation> deliverySubs = new HashSet<Simulation>();
        Mesh truckMesh;
        const float TruckDrive = 3.5f, TruckWait = 2.5f;

        void SubscribeDeliveries()
        {
            if (sim?.company == null) return;
            foreach (var s in sim.company.sims)
                if (s != null && deliverySubs.Add(s)) s.OnDelivery += OnDelivery;
        }

        void OnDelivery(int site)
        {
            if (sim == null || site != sim.S.activeSite || trucks.Count >= 3) return;
            var S = sim.S;
            float roadX = S.ownX0 + 1.25f;
            Vector3 end;
            var dock = S.objects.FirstOrDefault(o => !o.failed && Catalog.Facility(o.def)?.kind == FacilityKind.LoadingDock);
            if (dock != null)
            {
                var d = Catalog.Facility(dock.def);
                var c = ObjectCenter(d, dock.x, dock.y, dock.rot);
                end = c - (Quaternion.Euler(0, dock.rot * 90f, 0) * new Vector3(0, 0, 0.9f));
            }
            else end = new Vector3(roadX, 0, S.ownY0 - 1.5f);
            if (truckMesh == null)
            {
                var mb = new MeshBuilder();
                Models.Truck(mb, new Color(0.85f, 0.35f, 0.2f));
                truckMesh = mb.ToMesh();
            }
            var go = new GameObject("Truck");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = truckMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = litMat;
            trucks.Add(new TruckAnim { go = go, a = new Vector3(roadX, 0, S.ownY0 - 25f), b = end });
        }

        void UpdateTrucks()
        {
            SubscribeDeliveries();
            bool ground = sim.S.floor == 0;
            for (int i = trucks.Count - 1; i >= 0; i--)
            {
                var tr = trucks[i];
                tr.t += Time.unscaledDeltaTime;
                float total = TruckDrive * 2 + TruckWait;
                if (tr.t >= total || tr.go == null) { if (tr.go != null) Destroy(tr.go); trucks.RemoveAt(i); continue; }
                tr.go.SetActive(ground);
                // In along the road, then across to the dock; reversed on the way out.
                Vector3 corner = new Vector3(tr.a.x, 0, tr.b.z);
                float u = tr.t < TruckDrive ? tr.t / TruckDrive : tr.t < TruckDrive + TruckWait ? 1f : 1f - (tr.t - TruckDrive - TruckWait) / TruckDrive;
                u = Mathf.SmoothStep(0, 1, u);
                float l1 = Vector3.Distance(tr.a, corner), l2 = Vector3.Distance(corner, tr.b);
                float dist = u * (l1 + l2);
                Vector3 pos, dir;
                if (dist <= l1 || l2 < 0.01f) { pos = Vector3.Lerp(tr.a, corner, l1 > 0 ? dist / l1 : 1f); dir = corner - tr.a; }
                else { pos = Vector3.Lerp(corner, tr.b, (dist - l1) / l2); dir = tr.b - corner; }
                if (tr.t > TruckDrive + TruckWait) dir = -dir;
                tr.go.transform.position = pos;
                if (dir.sqrMagnitude > 0.0001f) tr.go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }
    }
}
