// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT), MaterialQuantityAndMannequinTests.cs.
// The two mannequin tests are left out: that fix is not ported. The setup below isolates settings,
// logs and OCR data in a temp folder, so tests never touch the real settings.json.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Tesseract;

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class MaterialQuantityTests
    {
        private string previousDirectory;
        private string sandbox;
        private FieldInfo defaultSettings;
        private object previousSettings;
        private LoggingConfiguration previousLogging;
        private MemoryTarget log;

        [TestInitialize]
        public void IsolateSettingsDataAndLogs()
        {
            previousDirectory = Environment.CurrentDirectory;
            sandbox = Path.Combine(Path.GetTempPath(), "InventoryKamera.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandbox, "inventorylists"));
            Directory.CreateDirectory(Path.Combine(sandbox, "tessdata"));
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata", "genshin_fast_09_04_21.traineddata"),
                Path.Combine(sandbox, "tessdata", "genshin_fast_09_04_21.traineddata"));
            Environment.CurrentDirectory = sandbox;
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), "{\"traveler\":{\"GOOD\":\"Traveler\"}}");
            File.WriteAllText(Path.Combine("inventorylists", "artifacts.json"), "{\"fixture\":{}}");
            foreach (var name in new[] { "weapons", "materials" })
                File.WriteAllText(Path.Combine("inventorylists", name + ".json"), "{\"fixture\":\"Fixture\"}");
            File.WriteAllText(Path.Combine("inventorylists", "version.txt"), "7.1.0");

            var settingsType = typeof(MainForm).Assembly.GetType("InventoryKamera.Properties.Settings", true);
            defaultSettings = settingsType.GetField("defaultInstance", BindingFlags.Static | BindingFlags.NonPublic);
            previousSettings = defaultSettings.GetValue(null);
            var settings = (ApplicationSettingsBase)Activator.CreateInstance(settingsType, true);
            var provider = new MemorySettingsProvider();
            provider.Initialize("QuantityRegression", new NameValueCollection());
            foreach (SettingsProperty property in settings.Properties) property.Provider = provider;
            settings.Providers.Clear();
            settings.Providers.Add(provider);
            settings["NumOfCharToScan"] = 0;
            defaultSettings.SetValue(null, settings);

            previousLogging = LogManager.Configuration;
            log = new MemoryTarget("quantity-regression") { Layout = "${level}|${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, log);
            LogManager.Configuration = configuration;
        }

        [TestCleanup]
        public void RestoreGlobalsAndRemoveOwnSandbox()
        {
            defaultSettings?.SetValue(null, previousSettings);
            LogManager.Configuration = previousLogging;
            Environment.CurrentDirectory = previousDirectory;
            if (sandbox != null && Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }

        [TestMethod]
        [Description("AC-1: a repeated noisy quantity is exported as its numeric value, not its vote count")]
        public void AC1_RepeatedQuantityReturnsQuantityInsteadOfVoteCount()
        {
            Assert.AreEqual(62, ParseQuantity("62]", "62]", "62]", "8]", "8]"));
        }

        [TestMethod]
        [Description("ERR-1: conflicting one-off OCR guesses do not invent a quantity")]
        public void ERR1_DifferentSingleReadingsRemainUnknown()
        {
            Assert.AreEqual(0, ParseQuantity("12]", "34]", "56]", "78]", "90]"));
        }

        [TestMethod]
        [Description("ERR-2: tied noisy readings do not invent a quantity")]
        public void ERR2_TiedReadingsRemainUnknown()
        {
            Assert.AreEqual(0, ParseQuantity("62]", "8]", "62]", "8]", ""));
        }

        [TestMethod]
        [Description("EC-1: empty OCR remains unknown, while an unambiguous quantity still works")]
        public void EC1_EmptyAndClearReadingsKeepTheirMeaning()
        {
            Assert.AreEqual(0, ParseQuantity("", " ", "", "", ""));
            Assert.AreEqual(124, ParseQuantity("124", "124", "124", "124", "124"));
        }

        [TestMethod]
        [Description("AC-3: GOOD omits failed quantities while preserving known material counts")]
        public void AC3_UnknownQuantitiesAreNotExportedAsZeroInventory()
        {
            var scanner = new global::InventoryKamera.InventoryKamera();
            scanner.Inventory.Materials.Add(new Material { name = "UnknownFixture", count = 0 });
            scanner.Inventory.Materials.Add(new Material { name = "KnownFixture", count = 62 });
            scanner.Inventory.DevMaterials.Add(new Material { name = "UnknownDevelopmentFixture", count = 0 });
            scanner.Inventory.DevMaterials.Add(new Material { name = "KnownDevelopmentFixture", count = 124 });

            var materials = (JObject)JObject.Parse(new GOOD(scanner).ToString())["materials"];

            CollectionAssert.AreEquivalent(new[] { "KnownFixture", "KnownDevelopmentFixture" },
                materials.Properties().Select(property => property.Name).ToArray());
            Assert.AreEqual(62, (int)materials["KnownFixture"]);
            Assert.AreEqual(124, (int)materials["KnownDevelopmentFixture"]);
        }

        [DataTestMethod]
        [DataRow(1280, 720, 20)]
        [DataRow(1920, 1080, 30)]
        [Description("AC-4: quantity capture includes the full digit height at each supported display scale")]
        public void AC4_QuantityCaptureScalesItsBottomMargin(int width, int height, int expectedMargin)
        {
            var geometry = typeof(Navigation).GetField("WindowSize", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = geometry.GetValue(null);
            geometry.SetValue(null, new RECT(0, 0, width, height));
            try
            {
                var item = new Rectangle(100, 200, 126, 135);
                var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
                var region = (RECT)scraper.GetMethod("GetMaterialQuantityRegion", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { item });
                Assert.AreEqual(item.Bottom + expectedMargin, region.Bottom);
                Assert.AreEqual(item.Left, region.Left);
                Assert.AreEqual(item.Right, region.Right);
            }
            finally { geometry.SetValue(null, previous); }
        }

        [TestMethod]
        [Description("AC-5: recognizing a number must not stop the reused OCR engine from recognizing a subsequent material name")]
        public void AC5_NumericRecognitionDoesNotDisableSubsequentNameRecognition()
        {
            var enginePool = typeof(GenshinProcesor).GetField("engines", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = enginePool.GetValue(null);
            using (var engine = new TesseractEngine("./tessdata", "genshin_fast_09_04_21", EngineMode.LstmOnly))
            using (var name = RenderText("APPLE"))
            using (var count = RenderText("124"))
            {
                enginePool.SetValue(null, new System.Collections.Concurrent.ConcurrentBag<TesseractEngine>(new[] { engine }));
                try
                {
                    Assert.AreEqual("APPLE", Recognize(name, false), "The synthetic name must be readable before numeric recognition.");
                    Assert.AreEqual("124", Recognize(count, true));
                    Assert.AreEqual("APPLE", Recognize(name, false), "A reused engine must restore full text recognition after numeric OCR.");
                }
                finally { enginePool.SetValue(null, previous); }
            }
        }

        private static Bitmap RenderText(string text)
        {
            var image = new Bitmap(300, 85);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Arial", 36, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.White);
                graphics.DrawString(text, font, Brushes.Black, 20, 20);
            }
            return image;
        }

        private static string Recognize(Bitmap image, bool numbersOnly)
        {
            return ((string)typeof(GenshinProcesor).GetMethod("AnalyzeText", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { image, PageSegMode.SingleLine, numbersOnly })).Trim();
        }

        [TestMethod]
        [Description("Item 19: an unclear pass is read again on the enlarged crop")]
        public void UnclearPassIsReadAgainEnlarged()
        {
            // Deathly Statuette, 21-Nov: "", 1, 1, 11, 11 on the crop; enlarged 1.25x it reads 11 five times.
            Assert.AreEqual(11, ParseQuantity("", "1", "1", "11", "11", "11", "11", "11", "11", "11"));
        }

        [TestMethod]
        [Description("Item 19: if every pass ties, the count stays unknown")]
        public void TieInEveryPassStaysUnknown()
        {
            Assert.AreEqual(0, ParseQuantity(out int reads, "62", "8", "62", "8", "", "62", "8", "62", "8", "", "62", "8", "62", "8", ""));
            Assert.AreEqual(15, reads, "the crop, 1.25x and 1.5x");
        }

        [TestMethod]
        [Description("Item 19: a clear first pass needs no second one")]
        public void ClearReadingsUseOnePass()
        {
            Assert.AreEqual(62, ParseQuantity(out int reads, "62", "62", "62", "8", "8", "999", "999", "999", "999", "999"));
            Assert.AreEqual(5, reads);
            Assert.AreEqual(11, ParseQuantity(out reads, "", "1", "1", "11", "11", "11", "11", "11", "11", "11"));
            Assert.AreEqual(10, reads, "an unclear pass takes one more, at 1.25x");
        }

        [TestMethod]
        [Description("19b: small specks are removed, the digits are kept")]
        public void SpecksAreRemovedDigitsKept()
        {
            using (var image = RenderText("52"))
            {
                // Two frame specks near the bottom corners, as in the 21-Nov crops.
                foreach (var x in new[] { 3, image.Width - 4 })
                    for (int dy = 0; dy < 2; dy++) image.SetPixel(x, image.Height - 3 + dy, Color.Black);
                int inkBefore = InkPixels(image);

                int removed = MaterialScraper.RemoveSpecks(image);

                Assert.AreEqual(4, removed, "both specks, 2 px each");
                Assert.AreEqual(Color.White.ToArgb(), image.GetPixel(3, image.Height - 3).ToArgb());
                Assert.AreEqual(inkBefore - 4, InkPixels(image), "every digit pixel stays");
                Assert.AreEqual(0, MaterialScraper.RemoveSpecks(image), "nothing left to remove");
            }
        }

        private static int InkPixels(Bitmap image)
        {
            int ink = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).R < 160) ink++;
            return ink;
        }

        private static int ParseQuantity(params string[] readings) => ParseQuantity(out _, readings);

        private static int ParseQuantity(out int reads, params string[] readings)
        {
            var pending = new System.Collections.Generic.Queue<string>(readings);
            int calls = 0;
            Func<Bitmap, string> recognize = image => { calls++; return pending.Count == 0 ? "" : pending.Dequeue(); };
            try { return ParseQuantityWith(recognize); }
            finally { reads = calls; }
        }

        private static int ParseQuantityWith(Func<Bitmap, string> recognize)
        {
            var scraper = typeof(MainForm).Assembly.GetType("InventoryKamera.MaterialScraper", true);
            using (var image = new Bitmap(120, 40))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
                return (int)scraper.GetMethod("ParseMaterialCount", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { image, recognize });
            }
        }

        private sealed class MemorySettingsProvider : SettingsProvider
        {
            public override string ApplicationName { get; set; } = "InventoryKamera.QuantityRegression";
            public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
            {
                var values = new SettingsPropertyValueCollection();
                foreach (SettingsProperty property in properties)
                    values.Add(new SettingsPropertyValue(property) { SerializedValue = property.DefaultValue, IsDirty = false });
                return values;
            }
            public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values) { }
        }
    }
}
