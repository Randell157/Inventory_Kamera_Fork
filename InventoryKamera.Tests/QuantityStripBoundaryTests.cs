// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT).

using System;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class QuantityStripBoundaryTests
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
        [Description("A complete number on its pale strip remains readable when the capture extends into dark scenery below the tile")]
        public void CompleteQuantityWithDarkSceneryBelowReturnsItsCount()
        {
            using (var image = QuantityTile())
            {
                DrawDigitStrokes(image, 2);
                Assert.AreEqual(62, ParseQuantity(image, processed => "62"),
                    "Dark scenery below a visible tile boundary is not a truncated quantity glyph.");
            }
        }

        [TestMethod]
        [Description("The selected tile frame may float inside a taller capture without invalidating a complete quantity")]
        public void CompleteQuantityWithFloatingSelectionFrameReturnsItsCount()
        {
            using (var image = QuantityTile())
            {
                DrawDigitStrokes(image, 1);
                using (var graphics = Graphics.FromImage(image))
                using (var frame = new Pen(Color.FromArgb(86, 92, 110), 2))
                    graphics.DrawRectangle(frame, 5, 36, 115, 7);

                Assert.AreEqual(4, ParseQuantity(image, processed => "4"),
                    "A selected tile border below the pale quantity strip must not be interpreted as clipped digit ink.");
            }
        }

        [TestMethod]
        [Description("A wide complete quantity remains readable when digit rows contain more than fifteen percent ink")]
        public void WideQuantityUsesTheFullPaleStripAboveDarkScenery()
        {
            using (var image = QuantityTile())
            {
                DrawDigitStrokes(image, 4);
                Assert.AreEqual(1191, ParseQuantity(image, processed => "1191"),
                    "Dense digit rows must remain part of the quantity strip after its blank upper margin.");
            }
        }

        [DataTestMethod]
        [DataRow(62)]
        [DataRow(1191)]
        [Description("Native OCR reads all digits when gray scenery follows the pale quantity strip")]
        public void NativeOcrReadsQuantityAboveGrayScenery(int expected)
        {
            using (var image = QuantityTile())
            {
                using (var graphics = Graphics.FromImage(image))
                using (var font = new Font("Arial", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString(expected.ToString(), font, Brushes.Black, 40, 12, StringFormat.GenericTypographic);
                }

                // The short capture proves this synthetic text is readable by the real OCR model.
                using (var stripOnly = image.Clone(new Rectangle(0, 0, image.Width, 36), image.PixelFormat))
                    Assert.AreEqual(expected, ParseQuantity(stripOnly, null), "The native OCR fixture must be readable.");

                Assert.AreEqual(expected, ParseQuantity(image, null),
                    "Including scenery below the tile must not destroy a complete, readable count.");
            }
        }

        private static Bitmap QuantityTile()
        {
            var image = new Bitmap(126, 58);
            using (var graphics = Graphics.FromImage(image))
            using (var scenery = new SolidBrush(Color.FromArgb(112, 108, 100)))
            using (var gray = new SolidBrush(Color.FromArgb(87, 94, 111)))
            using (var pale = new SolidBrush(Color.FromArgb(235, 231, 222)))
            using (var gold = new SolidBrush(Color.FromArgb(240, 197, 99)))
            {
                graphics.Clear(Color.FromArgb(81, 65, 51));
                graphics.FillRectangle(gold, 45, 1, 9, 7);
                graphics.FillRectangle(gold, 61, 1, 9, 7);
                graphics.FillRectangle(pale, 0, 10, image.Width, 26);
                graphics.FillRectangle(Brushes.White, 0, 36, image.Width, 5);
                graphics.FillRectangle(scenery, 0, 41, image.Width, 17);
                graphics.FillRectangle(gray, 15, 41, 96, 17);
            }
            return image;
        }

        private static void DrawDigitStrokes(Bitmap image, int count)
        {
            using (var graphics = Graphics.FromImage(image))
            using (var ink = new SolidBrush(Color.FromArgb(59, 66, 79)))
                for (int i = 0; i < count; i++)
                    graphics.FillRectangle(ink, 63 - count * 6 + i * 12, 14, 7, 17);
        }

        private static int ParseQuantity(Bitmap image, Func<Bitmap, string> recognize)
        {
            var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
            return (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { image, recognize });
        }
    }
}
