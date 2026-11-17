using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    /// <summary>A set of live bindings refreshed every frame (label text, bar values, button states).</summary>
    public class Scope
    {
        readonly List<Action> updaters = new List<Action>();
        public void Add(Action a) => updaters.Add(a);
        public void Clear() => updaters.Clear();
        public void Update()
        {
            for (int i = 0; i < updaters.Count; i++)
            {
                try { updaters[i](); }
                catch (Exception e) { Debug.LogWarning("UI binding failed: " + e.Message); }
            }
        }
    }

    /// <summary>
    /// Retained UI region that is rebuilt only when its signature changes (e.g. the list of offers),
    /// while its bindings refresh every frame. This keeps scroll positions and avoids flicker.
    /// </summary>
    public class Section
    {
        public readonly VisualElement root;
        readonly Func<string> signature;
        readonly Action<VisualElement, Scope> build;
        readonly Scope scope = new Scope();
        string last;

        public Section(VisualElement parent, Func<string> signature, Action<VisualElement, Scope> build, params string[] classes)
        {
            root = K.Div(parent, classes);
            this.signature = signature;
            this.build = build;
        }

        public void Invalidate() => last = null;

        public void Update()
        {
            string s;
            try { s = signature(); } catch (Exception e) { Debug.LogWarning("UI signature failed: " + e.Message); return; }
            if (s != last)
            {
                last = s;
                root.Clear();
                scope.Clear();
                try { build(root, scope); }
                catch (Exception e) { Debug.LogError("UI build failed: " + e); }
            }
            scope.Update();
        }
    }

    /// <summary>Element builders.</summary>
    public static class K
    {
        public static T Cls<T>(T e, params string[] classes) where T : VisualElement
        {
            if (classes != null) foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static VisualElement Div(VisualElement parent, params string[] classes)
        {
            var e = Cls(new VisualElement(), classes);
            parent?.Add(e);
            return e;
        }

        public static VisualElement Row(VisualElement parent, params string[] classes)
        {
            var e = Div(parent, classes);
            e.AddToClassList("row");
            return e;
        }

        public static VisualElement Space(VisualElement parent, float h = 6)
        {
            var e = Div(parent);
            e.style.height = h;
            e.style.flexShrink = 0;
            return e;
        }

        public static VisualElement Flex(VisualElement parent) => Div(parent, "grow");

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = Cls(new Label(text), "lbl");
            Cls(l, classes);
            parent?.Add(l);
            return l;
        }

        public static Label Text(Scope s, VisualElement parent, Func<string> text, params string[] classes)
        {
            var l = Text(parent, "", classes);
            s.Add(() => { var t = text(); if (l.text != t) l.text = t; });
            return l;
        }

        /// <summary>Applies whichever class the function returns, removing the previous one.</summary>
        public static void DynClass(Scope s, VisualElement e, Func<string> cls)
        {
            string prev = null;
            s.Add(() =>
            {
                var c = cls();
                if (c == prev) return;
                if (!string.IsNullOrEmpty(prev)) e.RemoveFromClassList(prev);
                if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
                prev = c;
            });
        }

        public static void Show(Scope s, VisualElement e, Func<bool> visible)
        {
            s.Add(() =>
            {
                var d = visible() ? DisplayStyle.Flex : DisplayStyle.None;
                if (e.style.display != d) e.style.display = d;
            });
        }

        public static Button Btn(VisualElement parent, string text, Action click, params string[] classes)
        {
            var b = new Button(() => { try { click?.Invoke(); } catch (Exception e) { Debug.LogError(e); } }) { text = text };
            Cls(b, "btn");
            Cls(b, classes);
            parent?.Add(b);
            return b;
        }

        public static Button Btn(Scope s, VisualElement parent, Func<string> text, Action click, Func<bool> enabled = null, Func<bool> on = null, params string[] classes)
        {
            var b = Btn(parent, text(), click, classes);
            s.Add(() =>
            {
                var t = text();
                if (b.text != t) b.text = t;
                if (enabled != null) { bool en = enabled(); if (b.enabledSelf != en) b.SetEnabled(en); }
                if (on != null) b.EnableInClassList("on", on());
            });
            return b;
        }

        public static Button Small(VisualElement parent, string text, Action click, params string[] classes)
        {
            var b = Btn(parent, text, click, classes);
            b.AddToClassList("btn-small");
            return b;
        }

        public static Button Small(Scope s, VisualElement parent, Func<string> text, Action click, Func<bool> enabled = null)
        {
            var b = Btn(s, parent, text, click, enabled);
            b.AddToClassList("btn-small");
            return b;
        }

        public static VisualElement Swatch(VisualElement parent, Color c, float size = 14)
        {
            var e = Div(parent, "swatch");
            e.style.width = size; e.style.height = size;
            e.style.backgroundColor = c;
            return e;
        }

        /// <summary>Progress bar with an optional centered label.</summary>
        public static VisualElement Bar(Scope s, VisualElement parent, Func<float> frac, Func<Color> color, Func<string> label = null, float height = 16)
        {
            var bg = Div(parent, "bar-bg");
            bg.style.height = height;
            var fill = Div(bg, "bar-fill");
            Label txt = label != null ? Text(bg, "", "bar-text") : null;
            s.Add(() =>
            {
                float f = Mathf.Clamp01(frac());
                fill.style.width = Length.Percent(f * 100f);
                fill.style.backgroundColor = color();
                if (txt != null) { var t = label(); if (txt.text != t) txt.text = t; }
            });
            return bg;
        }

        public static ScrollView Scroll(VisualElement parent, params string[] classes)
        {
            var sv = Cls(new ScrollView(ScrollViewMode.Vertical), classes);
            sv.AddToClassList("scroll");
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            parent?.Add(sv);
            return sv;
        }

        public static VisualElement Panel(VisualElement parent, params string[] classes)
        {
            var p = Div(parent, "panel");
            Cls(p, classes);
            p.pickingMode = PickingMode.Position;
            return p;
        }

        public static VisualElement Card(VisualElement parent) => Div(parent, "card");

        public static void Abs(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null, float? width = null, float? height = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            if (width.HasValue) e.style.width = width.Value;
            if (height.HasValue) e.style.height = height.Value;
        }

        public static string Money(float v) => (v < 0 ? "-$" : "$") + Mathf.Abs(v).ToString("N0");
    }

    /// <summary>Line chart drawn with the vector API.</summary>
    public class LineChart : VisualElement
    {
        public Func<IList<float>> data;
        public Color color = Color.green;
        public string format = "{0:N0}";
        readonly Label minL, maxL, nowL;
        int lastCount = -1;
        float lastValue;

        public LineChart()
        {
            AddToClassList("chart");
            generateVisualContent += Draw;
            maxL = K.Text(this, "", "chart-label");
            K.Abs(maxL, left: 4, top: 2);
            minL = K.Text(this, "", "chart-label");
            K.Abs(minL, left: 4, bottom: 2);
            nowL = K.Text(this, "", "chart-label");
            K.Abs(nowL, right: 4, top: 2);
        }

        public void Refresh()
        {
            var d = data?.Invoke();
            if (d == null) return;
            float v = d.Count > 0 ? d[d.Count - 1] : 0;
            if (d.Count == lastCount && Mathf.Approximately(v, lastValue)) return;
            lastCount = d.Count; lastValue = v;
            if (d.Count >= 2)
            {
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var x in d) { mn = Mathf.Min(mn, x); mx = Mathf.Max(mx, x); }
                maxL.text = string.Format(format, mx);
                minL.text = string.Format(format, mn);
                nowL.text = "now " + string.Format(format, v);
            }
            else { maxL.text = "Not enough data yet"; minL.text = nowL.text = ""; }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var d = data?.Invoke();
            if (d == null || d.Count < 2) return;
            var r = contentRect;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var x in d) { mn = Mathf.Min(mn, x); mx = Mathf.Max(mx, x); }
            if (Mathf.Approximately(mn, mx)) { mx += 1; mn -= 1; }
            var p = ctx.painter2D;
            p.strokeColor = color;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            float pad = 6;
            for (int i = 0; i < d.Count; i++)
            {
                var pt = new Vector2(pad + (r.width - 2 * pad) * i / (d.Count - 1f), r.height - pad - (r.height - 2 * pad) * (d[i] - mn) / (mx - mn));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }
    }

    /// <summary>Bar chart of monthly values (positive green, negative red).</summary>
    public class BarChart : VisualElement
    {
        public Func<IList<float>> data;
        public Func<IList<string>> labels;
        int lastHash;

        public BarChart()
        {
            AddToClassList("chart");
            generateVisualContent += Draw;
        }

        public void Refresh()
        {
            var d = data?.Invoke();
            int h = d == null ? 0 : d.Count;
            if (d != null) foreach (var v in d) h = h * 31 + v.GetHashCode();
            if (h == lastHash) return;
            lastHash = h;
            Clear();
            var l = labels?.Invoke();
            if (d != null && l != null)
            {
                float bw = 100f / 12f;
                float max = 1000; foreach (var v in d) max = Mathf.Max(max, Mathf.Abs(v));
                for (int i = 0; i < d.Count; i++)
                {
                    var lab = K.Text(this, l[i], "chart-label");
                    K.Abs(lab, bottom: 0);
                    lab.style.left = Length.Percent(i * bw);
                    lab.style.width = Length.Percent(bw);
                    lab.style.unityTextAlign = TextAnchor.MiddleCenter;
                    var val = K.Text(this, $"{d[i] / 1000f:0.#}k", "chart-label");
                    K.Abs(val, top: 0);
                    val.style.left = Length.Percent(i * bw);
                    val.style.width = Length.Percent(bw);
                    val.style.unityTextAlign = TextAnchor.MiddleCenter;
                }
            }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var d = data?.Invoke();
            var r = contentRect;
            var p = ctx.painter2D;
            p.strokeColor = new Color(1, 1, 1, 0.2f);
            p.lineWidth = 1;
            p.BeginPath(); p.MoveTo(new Vector2(0, r.height / 2)); p.LineTo(new Vector2(r.width, r.height / 2)); p.Stroke();
            if (d == null || d.Count == 0) return;
            float max = 1000; foreach (var v in d) max = Mathf.Max(max, Mathf.Abs(v));
            float bw = r.width / 12f, mid = r.height / 2, room = mid - 16;
            for (int i = 0; i < d.Count; i++)
            {
                float hgt = Mathf.Abs(d[i]) / max * room;
                p.fillColor = d[i] >= 0 ? new Color(0.3f, 0.85f, 0.45f) : new Color(1f, 0.4f, 0.3f);
                float x0 = i * bw + 6, x1 = (i + 1) * bw - 6;
                float y0 = d[i] >= 0 ? mid - hgt : mid, y1 = d[i] >= 0 ? mid : mid + hgt;
                p.BeginPath();
                p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
