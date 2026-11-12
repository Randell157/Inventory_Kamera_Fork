using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Inventory tab check: after clicking a tab, the scanner reads the tab's name at the top left
    // and retries until it shows the expected tab, or gives up so that section is skipped.
    [TestClass]
    public sealed class InventoryTabTests
    {
        private static readonly InventoryPage[] Tabs =
        {
            InventoryPage.Weapons, InventoryPage.Artifacts, InventoryPage.CharacterDevelopmentItems, InventoryPage.Materials,
        };

        // ---- Name check -------------------------------------------------------------------

        [DataTestMethod]
        [DataRow("Weapons", InventoryPage.Weapons)]
        [DataRow("Artifacts", InventoryPage.Artifacts)]
        [DataRow("Character Development\nItems", InventoryPage.CharacterDevelopmentItems)]
        [DataRow("Character Development Items", InventoryPage.CharacterDevelopmentItems)]
        [DataRow("Materials", InventoryPage.Materials)]
        // One- or two-letter OCR slips are still accepted.
        [DataRow("Matenals", InventoryPage.Materials)]
        [DataRow("Weapon", InventoryPage.Weapons)]
        [DataRow("Artfacts", InventoryPage.Artifacts)]
        [DataRow("Character Deve1opment\nltems", InventoryPage.CharacterDevelopmentItems)]
        public void HeaderNamesOnlyItsOwnTab(string header, InventoryPage expected)
        {
            foreach (var tab in Tabs)
                Assert.AreEqual(tab == expected, Navigation.HeaderShowsTab(header, tab),
                    $"header \"{header}\" checked as {tab}");
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Inventory capacity 1527/2600")]
        [DataRow("Food")]
        [DataRow("Gadgets")]
        public void UnrelatedTextNamesNoTab(string header)
        {
            foreach (var tab in Tabs)
                Assert.IsFalse(Navigation.HeaderShowsTab(header, tab), $"\"{header}\" must not count as {tab}");
        }

        // ---- Click, check, retry ------------------------------------------------------------

        [TestMethod]
        public void WrongTabThenRightTabClicksTwiceAndSucceeds()
        {
            var headers = new[] { "Weapons", "Materials" };
            int clicks = 0;
            var waits = new List<int>();

            // The header read after each click: first the old tab, then the requested one.
            bool ok = Navigation.SelectInventoryTab(InventoryPage.Materials,
                () => clicks++, () => headers[clicks - 1], waits.Add);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, clicks);
            CollectionAssert.AreEqual(new[] { 1 }, waits, "one growing wait before the second click");
        }

        [TestMethod]
        public void RightTabFirstTimeClicksOnce()
        {
            int clicks = 0;
            var waits = new List<int>();
            Assert.IsTrue(Navigation.SelectInventoryTab(InventoryPage.CharacterDevelopmentItems,
                () => clicks++, () => "Character Development\nItems", waits.Add));
            Assert.AreEqual(1, clicks);
            Assert.AreEqual(0, waits.Count);
        }

        [TestMethod]
        public void TabThatNeverAppearsGivesUpAfterFiveTries()
        {
            int clicks = 0;
            var waits = new List<int>();

            bool ok = Navigation.SelectInventoryTab(InventoryPage.Materials,
                () => clicks++, () => "Weapons", waits.Add);

            Assert.IsFalse(ok, "the caller skips the section when this is false");
            Assert.AreEqual(5, clicks);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, waits, "waits grow and there is none after the last try");
        }

        // ---- Real screenshots ----------------------------------------------------------------
        // Full-window captures from a real scan (account data, so not in the repo). Set IK_TEST_IMAGES
        // to the folder that holds them; otherwise these report Inconclusive (skipped).

        [DataTestMethod]
        [DataRow("tab-materials-1920x1080.png", InventoryPage.Materials)]
        [DataRow("tab-chardev-1920x1080.png", InventoryPage.CharacterDevelopmentItems)]
        public void RealScreenshotIsRecognisedAsItsTab(string file, InventoryPage expected)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) Assert.Inconclusive("Missing " + path);

            var isolation = new MaterialQuantityTests();   // isolated settings, logs and OCR data
            isolation.IsolateSettingsDataAndLogs();
            try
            {
                using (var window = new Bitmap(path))
                {
                    var region = Navigation.GetInventoryHeaderRegion(window.Width, window.Height);
                    using (var header = window.Clone(new Rectangle(region.Left, region.Top, region.Width, region.Height), window.PixelFormat))
                    {
                        string text = Navigation.ReadHeaderText(header);
                        Console.WriteLine($"{file}: header read as \"{text.Trim()}\"");
                        foreach (var tab in Tabs)
                            Assert.AreEqual(tab == expected, Navigation.HeaderShowsTab(text, tab), $"{file} checked as {tab}; read \"{text.Trim()}\"");
                    }
                }
            }
            finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
        }
    }
}
