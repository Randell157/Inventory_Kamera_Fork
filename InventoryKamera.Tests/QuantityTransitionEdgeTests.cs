// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT).

using System;
using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class QuantityTransitionEdgeTests
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

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [Description("A partial light margin at the tile-to-scenery transition must not turn complete quantity digits into a clipping error")]
        public void CompleteQuantityAboveAnUnevenSceneryBoundaryRemainsReadable(bool mirror)
        {
            using (var image = new Bitmap(126, 61))
            using (var graphics = Graphics.FromImage(image))
            using (var pale = new SolidBrush(Color.FromArgb(235, 231, 222)))
            using (var ink = new SolidBrush(Color.FromArgb(59, 66, 79)))
            using (var scenery = new SolidBrush(Color.FromArgb(112, 108, 100)))
            {
                graphics.Clear(Color.FromArgb(81, 65, 51));
                graphics.FillRectangle(pale, 0, 9, 126, 30);
                // The complete central glyphs have a clear lower margin.
                graphics.FillRectangle(ink, 54, 14, 4, 15);
                graphics.FillRectangle(ink, 68, 14, 4, 15);
                graphics.FillRectangle(scenery, 0, 39, 126, 22);
                // At the curved tile edge, only one side still belongs to the pale strip.
                graphics.FillRectangle(pale, mirror ? 111 : 0, 39, 15, 1);
                // A separate piece of blue-gray scenery is not connected to either glyph.
                graphics.FillRectangle(ink, mirror ? 4 : 114, 39, 8, 1);

                Func<Bitmap, string> recognize = processed => "14";
                var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
                int actual = (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { image, recognize });

                Assert.AreEqual(14, actual,
                    "Scenery after a complete pale quantity strip must not be treated as a bottom-clipped number.");
            }
        }
    }
}
