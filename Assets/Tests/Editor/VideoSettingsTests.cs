using NUnit.Framework;
using UnityEngine;

namespace DCT.Tests
{
    public class VideoSettingsTests
    {
        [Test]
        public void DisplayModeIndexRoundTrips()
        {
            for (int i = 0; i < Settings.DisplayModeNames.Length; i++)
                Assert.AreEqual(i, Settings.ModeIndex(Settings.ToFullScreenMode(i)));
        }

        [Test]
        public void ResolutionListIsSortedDistinctAndHasNative()
        {
            var list = Settings.AvailableResolutions();
            CollectionAssert.Contains(list, Settings.NativeResolution);
            CollectionAssert.AllItemsAreUnique(list);
            for (int i = 1; i < list.Count; i++)
                Assert.LessOrEqual(list[i - 1].x * list[i - 1].y, list[i].x * list[i].y);
        }

        [Test]
        public void ZeroResolutionMeansNative()
        {
            Assert.AreEqual(Settings.NativeResolution, Settings.EffectiveResolution(0, 0));
            Assert.AreEqual(new Vector2Int(1280, 720), Settings.EffectiveResolution(1280, 720));
        }
    }
}
