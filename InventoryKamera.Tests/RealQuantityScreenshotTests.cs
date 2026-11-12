using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Real quantity captures from a 1.4.5 scan that exported these items as 0.
    // The images are account data, so they are NOT in the repo: set IK_TEST_IMAGES to the folder
    // that holds them. Without it these tests report Inconclusive (skipped) instead of failing.
    [TestClass]
    public sealed class RealQuantityScreenshotTests
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

        // True counts from the user's 6-Nov scans; each image shows "37".
        [DataTestMethod]
        [DataRow("DandelionSeed_Quantity.png", 37)]
        [DataRow("PineAmber_Quantity.png", 37)]
        [DataRow("SkysplitGembloom_Quantity.png", 37)]
        [DataRow("Trishiraite_Quantity.png", 37)]
        public void ReadsTheRealCount(string file, int expected)
        {
            Assert.AreEqual(expected, Parse(file), file);
        }

        // Medaka was 11 on 6 Nov; the 11-Nov image appears to show 10 with gold rarity stars above it.
        // Acceptable: the real number (10 or 11) or 0 ("unknown"). Never any other number.
        [TestMethod]
        public void MedakaIsReadCorrectlyOrReportedUnknown()
        {
            int actual = Parse("Medaka_Quantity.png");
            Console.WriteLine("Medaka read as " + actual);
            CollectionAssert.Contains(new[] { 0, 10, 11 }, actual, "Medaka read as a wrong number: " + actual);
        }

        private static int Parse(string file)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) Assert.Inconclusive("Missing " + path);

            var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
            using (var image = new Bitmap(path))
            {
                return (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { image, null });
            }
        }
    }
}
