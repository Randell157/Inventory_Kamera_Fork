using System;
using System.Drawing;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // The "Equipped: <character>" OCR runs only for a card whose bottom bar shows the equipped colour.
    [TestClass]
    public sealed class EquippedReadTests
    {
        [TestMethod]
        public void UnequippedCardNeverRunsTheRead()
        {
            int reads = 0;
            var task = InventoryScraper.ReadIfEquipped(false, () => { Interlocked.Increment(ref reads); return "Ganyu"; });
            Assert.IsTrue(task.IsCompleted, "nothing left running");
            Assert.IsNull(task.Result);
            Thread.Sleep(50);
            Assert.AreEqual(0, reads);
        }

        [TestMethod]
        public void EquippedCardReadsItsHolder()
        {
            int reads = 0;
            var task = InventoryScraper.ReadIfEquipped(true, () => { Interlocked.Increment(ref reads); return "Ganyu"; });
            Assert.AreEqual("Ganyu", task.Result);
            Assert.AreEqual(1, reads);
        }

        private static Bitmap Filled(Color c)
        {
            var b = new Bitmap(40, 20);
            using (var g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        // The bottom strip, as InventoryScraper.GetEquippedBitmap cuts it (16:9).
        private static Bitmap EquippedStrip(Bitmap card)
        {
            int x = (int)(card.Width * 0.15), y = (int)(card.Height * 0.938);
            return card.Clone(new Rectangle(x, y, card.Width - x, card.Height - y), card.PixelFormat);
        }

        [TestMethod]
        public void EquippedBarColourIsRecognised()
        {
            var isolation = new MaterialQuantityTests();
            isolation.IsolateSettingsDataAndLogs();
            try
            {
                using (var bar = Filled(InventoryScraper.EquippedBarColor)) Assert.IsTrue(InventoryScraper.LooksEquipped(bar));
                using (var card = Filled(Color.FromArgb(236, 229, 216))) Assert.IsFalse(InventoryScraper.LooksEquipped(card), "the card's own background");
                using (var bar = Filled(InventoryScraper.EquippedBarColor))
                {
                    // A portrait covering the bar's left end (Xinyan's hair) doesn't hide it.
                    using (var g = Graphics.FromImage(bar)) g.FillRectangle(Brushes.SaddleBrown, 0, 0, 15, 20);
                    Assert.IsTrue(InventoryScraper.LooksEquipped(bar));
                }

                var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
                if (string.IsNullOrWhiteSpace(folder)) return;
                // Strips saved by the 2026-11-21 05:34 scan, as GetEquippedBitmap cut them.
                foreach (var (name, equipped) in new[] { ("equipped-strip-xinyan-hair.png", true), ("equipped-strip-ganyu.png", true), ("equipped-strip-unequipped.png", false) })
                {
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    using (var strip = new Bitmap(path)) Assert.AreEqual(equipped, InventoryScraper.LooksEquipped(strip), name);
                }

                // Your saved weapon cards are all unequipped.
                foreach (var name in new[] { "weapon-card-locked-1920x1080.png", "weapon-card-locked-fading-1920x1080.png", "weapon-card-unlocked-1920x1080.png" })
                {
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    using (var card = new Bitmap(path))
                    using (var strip = EquippedStrip(card))
                        Assert.IsFalse(InventoryScraper.LooksEquipped(strip), name);
                }
            }
            finally
            {
                isolation.RestoreGlobalsAndRemoveOwnSandbox();
            }
        }
    }
}
