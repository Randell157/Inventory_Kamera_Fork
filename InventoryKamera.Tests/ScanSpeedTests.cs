using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // The "Delay per item" drop-down: five speeds, each a multiplier on Navigation.BaseWait,
    // and the first-launch conversion from the old 3-position Scanner Delay slider.
    [TestClass]
    public sealed class ScanSpeedTests
    {
        [TestMethod]
        public void ChoicesShowNameAndWaitPerItem()
        {
            string[] expected = { "Fastest – 100 ms", "Fast – 150 ms", "Normal – 200 ms", "Slow – 250 ms", "Slowest – 300 ms" };
            Assert.AreEqual(expected.Length, ScanSpeeds.Count);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], ScanSpeeds.ItemText(i));
        }

        [TestMethod]
        public void FactorsAndFallback()
        {
            CollectionAssert.AreEqual(new[] { 0.5, 0.75, 1, 1.25, 1.5 },
                new[] { ScanSpeeds.Factor(0), ScanSpeeds.Factor(1), ScanSpeeds.Factor(2), ScanSpeeds.Factor(3), ScanSpeeds.Factor(4) });
            Assert.AreEqual(1, ScanSpeeds.Factor(-1), "a missing or broken step scans at Normal");
            Assert.AreEqual(1, ScanSpeeds.Factor(9));
        }

        [TestMethod]
        public void OldSliderSettingKeepsItsSpeed()
        {
            // Fast 0.5x → Fastest, Slower 1x → Normal, Slow 1.5x → Slowest.
            Assert.AreEqual(0, ScanSpeeds.FromScannerDelay(0));
            Assert.AreEqual(2, ScanSpeeds.FromScannerDelay(1));
            Assert.AreEqual(4, ScanSpeeds.FromScannerDelay(2));
            Assert.AreEqual(2, ScanSpeeds.FromScannerDelay(7));
            for (int old = 0; old <= 2; old++)
                Assert.AreEqual(MainFormOldFactor(old), ScanSpeeds.Factor(ScanSpeeds.FromScannerDelay(old)));
        }

        // What the 1.4.5 slider multiplied the waits by.
        private static double MainFormOldFactor(int old) => old == 0 ? 0.5 : old == 2 ? 1.5 : 1;

        [TestMethod]
        public void SavedSpeedWinsOnceSet()
        {
            Assert.AreEqual(0, ScanSpeeds.Stored(-1, 0), "first launch: from the old setting");
            Assert.AreEqual(4, ScanSpeeds.Stored(-1, 2));
            Assert.AreEqual(3, ScanSpeeds.Stored(3, 0), "after that: the drop-down's own setting");
            Assert.AreEqual(2, ScanSpeeds.Stored(12, 1), "a broken value is replaced from the old setting");
        }

        [TestMethod]
        public void WorkersAndHint()
        {
            Assert.AreEqual(3, ScanSpeeds.Workers(0));
            for (int i = 1; i < ScanSpeeds.Count; i++) Assert.AreEqual(2, ScanSpeeds.Workers(i));
            Assert.AreEqual("= 3 OCR workers", ScanSpeeds.HintText(0));
            Assert.AreEqual("= 2 OCR workers", ScanSpeeds.HintText(3));
        }

        [TestMethod]
        public void TooltipListsWaitsAndTradeoffs()
        {
            Assert.AreEqual(
                "Waits at this setting:\n" +
                "after each item: 100 ms\n" +
                "after switching character: 350 ms\n" +
                "after opening a menu: 1 s\n" +
                "OCR workers: 3\n" +
                "Waiting for 1,000 items: 1.7 min\n" +
                "\n" +
                "Faster: the scan finishes sooner, but on a slow PC or\n" +
                "with lag a card can be read before it has loaded\n" +
                "(wrong lock, or the same item twice). Fastest also uses\n" +
                "3 OCR workers, so it needs more CPU.\n" +
                "Slower: fewer misread cards on a slow or busy PC,\n" +
                "but the scan takes longer.",
                ScanSpeeds.TooltipText(0));

            string slowest = ScanSpeeds.TooltipText(4, forChoice: true);
            StringAssert.StartsWith(slowest, "Waits with Slowest:\n");
            StringAssert.Contains(slowest, "after each item: 300 ms");
            StringAssert.Contains(slowest, "after switching character: 1.05 s");
            StringAssert.Contains(slowest, "after opening a menu: 3 s");
            StringAssert.Contains(slowest, "Waiting for 1,000 items: 5 min");
            StringAssert.Contains(ScanSpeeds.TooltipText(1), "Waiting for 1,000 items: 2.5 min");
            StringAssert.Contains(ScanSpeeds.TooltipText(3), "after switching character: 875 ms");
        }

        [TestMethod]
        public void LogLineNamesTheSpeed()
        {
            Assert.AreEqual("Fastest (0.5x, 100 ms per item)", ScanSpeeds.LogText(0));
            Assert.AreEqual("Fast (0.75x, 150 ms per item)", ScanSpeeds.LogText(1));
            Assert.AreEqual("Slowest (1.5x, 300 ms per item)", ScanSpeeds.LogText(4));
        }

        [TestMethod]
        public void WaitTableIsUnchanged()
        {
            Assert.AreEqual(200, Navigation.BaseWait(Navigation.Speed.SelectNextInventoryItem));
            Assert.AreEqual(700, Navigation.BaseWait(Navigation.Speed.SelectNextCharacter));
            Assert.AreEqual(2000, Navigation.BaseWait(Navigation.Speed.UI));
            Assert.AreEqual(1000, Navigation.BaseWait((Navigation.Speed)999), "unknown waits keep the 1 s default");
        }
    }
}
