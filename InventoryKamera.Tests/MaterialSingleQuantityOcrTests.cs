// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT).

using System;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class MaterialSingleQuantityOcrTests
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
        [Description("Native OCR recognizes a complete two-digit material quantity on its pale tile strip")]
        public void NativeOcrRecognizesCompleteRepeatedFours()
        {
            using (var image = new Bitmap(126, 59))
            using (var graphics = Graphics.FromImage(image))
            using (var pale = new SolidBrush(Color.FromArgb(235, 231, 222)))
            using (var ink = new SolidBrush(Color.FromArgb(59, 66, 79)))
            using (var font = new Font("Arial", 20, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(81, 65, 51));
                graphics.FillRectangle(pale, 0, 7, 126, 33);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("44", font, ink, 50, 10, StringFormat.GenericTypographic);

                var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
                var actual = (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { image, null });

                Assert.AreEqual(44, actual,
                    "A complete, legible two-digit quantity must survive native OCR without guessed or supplied readings.");
            }
        }
    }
}
