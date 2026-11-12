// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT).

using System;
using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class MaterialClippingTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void IsolateSettingsDataAndLogs()
        {
            isolation = new MaterialQuantityTests();
            isolation.IsolateSettingsDataAndLogs();
        }

        [TestCleanup]
        public void RestoreGlobalsAndRemoveOwnSandbox()
        {
            isolation?.RestoreGlobalsAndRemoveOwnSandbox();
        }

        [TestMethod]
        [Description("ERR-3: a quantity glyph cut by the bottom of its capture remains unknown even when OCR repeatedly guesses the same digit")]
        public void ERR3_ClippedQuantityDoesNotBecomeAConfidentWrongNumber()
        {
            using (var image = QuantityStrip())
            {
                // A central number stroke crosses the last captured row: its lower part is missing.
                using (var graphics = Graphics.FromImage(image))
                    graphics.FillRectangle(Brushes.Black, 56, 30, 5, 8);

                Assert.AreEqual(0, ParseQuantity(image, "2"),
                    "Agreement between repeated OCR guesses cannot recover pixels missing from the capture.");
            }
        }

        [TestMethod]
        [Description("EC-3: complete quantity glyphs remain readable, including when the selected tile frame reaches the image edge")]
        public void EC3_CompleteQuantityAndSelectedTileFrameRemainReadable()
        {
            using (var image = QuantityStrip())
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.FillRectangle(Brushes.Black, 50, 20, 5, 13);
                    graphics.FillRectangle(Brushes.Black, 62, 20, 5, 13);
                }
                Assert.AreEqual(62, ParseQuantity(image, "62"), "A complete quantity must still be accepted.");

                using (var graphics = Graphics.FromImage(image))
                    graphics.FillRectangle(Brushes.Black, 80, 37, 46, 1);
                Assert.AreEqual(62, ParseQuantity(image, "62"),
                    "The selected tile's frame continuing to the right image edge is not a clipped central glyph.");
            }
        }

        private static Bitmap QuantityStrip()
        {
            var image = new Bitmap(126, 38);
            using (var graphics = Graphics.FromImage(image))
                graphics.Clear(Color.FromArgb(235, 231, 222));
            return image;
        }

        private static int ParseQuantity(Bitmap image, string reading)
        {
            Func<Bitmap, string> recognize = processed => reading;
            var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
            return (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { image, recognize });
        }
    }
}
