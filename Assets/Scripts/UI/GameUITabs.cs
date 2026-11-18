using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCT
{
    public partial class GameUI
    {
        // Selected tab per tab bar, keyed by name. Lives for the session so windows reopen on the last tab.
        readonly Dictionary<string, int> tabState = new Dictionary<string, int>();

        int Tab(string key, int def = 0) => tabState.TryGetValue(key, out var v) ? v : def;

        void SetTab(string key, int idx, Section sec)
        {
            if (tabState.TryGetValue(key, out var cur) && cur == idx) return;
            tabState[key] = idx;
            sec?.Invalidate();
        }

        /// <summary>Draws a tab bar and returns the selected tab index. Clicking a tab rebuilds <paramref name="sec"/>.</summary>
        int Tabs(VisualElement p, Section sec, string key, params string[] labels) => Tabs(p, sec, key, out _, labels);

        /// <summary>Tab bar variant that also hands back the bar, so callers can add a status text at its right end.</summary>
        int Tabs(VisualElement p, Section sec, string key, out VisualElement bar, params string[] labels) => Tabs(p, sec, key, 0, out bar, labels);

        /// <summary>Tab bar whose selection falls back to <paramref name="def"/> until the player picks a tab.</summary>
        int Tabs(VisualElement p, Section sec, string key, int def, out VisualElement bar, params string[] labels)
        {
            int sel = Mathf.Clamp(Tab(key, def), 0, Mathf.Max(0, labels.Length - 1));
            bar = K.Div(p, "tabbar");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = K.Btn(bar, labels[i], () => SetTab(key, idx, sec), "tab");
                b.EnableInClassList("on", i == sel);
            }
            return sel;
        }
    }
}
