using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // The character screen's name area cuts off long names. On 20-Nov "Kaedehara Kazuh" was read
    // as "kaedeharakazul" in all 75 tries, matched nothing, and Kazuha was left out of the export.
    [TestClass]
    public sealed class CharacterNameTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // temp folder, isolated settings and logs
            isolation.IsolateSettingsDataAndLogs();
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), @"{
  ""traveler"": { ""GOOD"": ""Traveler"", ""Element"": [""anemo"", ""cryo""], ""WeaponType"": 0 },
  ""kaedeharakazuha"": { ""GOOD"": ""KaedeharaKazuha"", ""Element"": [""anemo""], ""WeaponType"": 0 },
  ""kamisatoayaka"": { ""GOOD"": ""KamisatoAyaka"", ""Element"": [""cryo""], ""WeaponType"": 0 },
  ""kamisatoayato"": { ""GOOD"": ""KamisatoAyato"", ""Element"": [""hydro""], ""WeaponType"": 0 },
  ""skirk"": { ""GOOD"": ""Skirk"", ""Element"": [""cryo""], ""WeaponType"": 0 }
}");
            GenshinProcesor.ReloadData();
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        [TestMethod]
        public void MisreadLastLetterStillMatches()
        {
            Assert.AreEqual("KaedeharaKazuha", GenshinProcesor.FindClosestCharacterName("kaedeharakazul"));
        }

        [TestMethod]
        public void FullAndPartialNamesStillMatchAsBefore()
        {
            Assert.AreEqual("KaedeharaKazuha", GenshinProcesor.FindClosestCharacterName("kaedeharakazuha"));
            Assert.AreEqual("KaedeharaKazuha", GenshinProcesor.FindClosestCharacterName("kazuha"));
        }

        [TestMethod]
        public void ShortenedNameThatFitsTwoCharactersDoesNotMatch()
        {
            // "kamisatoay" fits both Ayaka and Ayato, so it must not pick one.
            Assert.AreEqual("", GenshinProcesor.FindClosestCharacterName("kamisatoayq"));
        }

        [TestMethod]
        public void ShortJunkIsNotShortened()
        {
            Assert.AreEqual("", GenshinProcesor.FindClosestCharacterName("kaex"), "4 letters or fewer are not retried");
        }

        [TestMethod]
        public void RealCutOffKazuhaNameIsRead()
        {
            // The name area from the 20-Nov scan, cut off at "Kaedehara Kazuh" (account data, not in the repo).
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, "character-name-kazuha-cut-1920x1080.png");
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");

            using (var bm = new Bitmap(path))
            {
                CharacterScraper.ReadNameAndElement(bm, out string name, out string element);
                Assert.AreEqual("KaedeharaKazuha", name);
                Assert.AreEqual("Anemo", element);
            }
        }

        // The name area as the scan captures it now at 1920x1080 (85..355 x 10..55 on the 1280x720 scale).
        private static readonly Rectangle NameArea = Rectangle.FromLTRB(127, 15, 532, 82);

        private static Bitmap FullScreenNameArea()
        {
            // A full character screen with "Anemo / Kaedehara Kazuha" and the Q badge (account data, not in the repo).
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, "character-screen-kazuha-1920x1080.png");
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            using (var screen = new Bitmap(path))
                return screen.Clone(NameArea, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        }

        [TestMethod]
        public void NameAreaIsCutAtTheKeyBadge()
        {
            using (var bm = FullScreenNameArea())
            {
                int badge = CharacterScraper.KeyBadgeColumn(bm);
                // The badge starts at column 477 of the screen; the name text ends at 476.
                Assert.IsTrue(Math.Abs(badge + NameArea.Left - 477) <= 2, $"badge found at screen column {badge + NameArea.Left}");
            }
        }

        [TestMethod]
        public void WiderNameAreaReadsTheWholeName()
        {
            using (var bm = FullScreenNameArea())
            {
                string text = CharacterScraper.ReadNameText(bm);
                var parts = text.Split('/');
                Assert.AreEqual(2, parts.Length, text);   // no stray "/" before the element
                StringAssert.StartsWith(text, "anemo");
                Assert.AreEqual("kaedeharakazuha", System.Text.RegularExpressions.Regex.Replace(parts[1], @"\W", ""), "read in full, without the badge");

                CharacterScraper.ReadNameAndElement(bm, out string name, out string element);
                Assert.AreEqual("KaedeharaKazuha", name);
                Assert.AreEqual("Anemo", element);
            }
        }

        [TestMethod]
        public void NameAreaWithoutBadgeIsNotCut()
        {
            using (var bm = new Bitmap(300, 60))
            using (var g = Graphics.FromImage(bm))
            {
                g.Clear(Color.FromArgb(16, 53, 50));
                g.FillRectangle(new SolidBrush(Color.FromArgb(216, 196, 160)), 20, 20, 200, 20);   // name-coloured text
                g.FillRectangle(Brushes.White, 250, 10, 2, 2);                                       // a small sparkle
                Assert.AreEqual(-1, CharacterScraper.KeyBadgeColumn(bm));

                g.FillRectangle(Brushes.White, 240, 15, 30, 30);
                Assert.AreEqual(240, CharacterScraper.KeyBadgeColumn(bm));
            }
        }

        [TestMethod]
        public void EmblemEdgeBeforeTheElementIsIgnored()
        {
            // OCR read the Cryo emblem's edge as "»" or ">»" in all 24 of Skirk's failed tries (2026-11-21).
            foreach (var text in new[] { "» cryo / skirk", ">» cryo / skirk", ", cryo / skirk —", "cryo / skirk" })
            {
                CharacterScraper.ParseNameAndElement(text, out string name, out string element);
                Assert.AreEqual("Skirk", name, text);
                Assert.AreEqual("Cryo", element, text);
            }
        }

        [TestMethod]
        public void ParsingStillHandlesWrappedNamesAndJunk()
        {
            CharacterScraper.ParseNameAndElement("anemo kaedehara\n/ kazuha", out string name, out string element);
            Assert.AreEqual("KaedeharaKazuha", name);
            Assert.AreEqual("Anemo", element);

            foreach (var junk in new[] { "/", "» / skirk", "cryo skirk", "" })
            {
                CharacterScraper.ParseNameAndElement(junk, out name, out element);
                Assert.IsTrue(name == "" && element == "", $"'{junk}' gave {name}/{element}");
            }
        }

        [TestMethod]
        public void RealSkirkCropsWithEmblemEdgeRead()
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            foreach (var file in new[] { "character-name-skirk-emblem-1920x1080.png", "character-name-skirk-emblem-2-1920x1080.png" })
            {
                var path = Path.Combine(folder, file);
                if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
                using (var bm = new Bitmap(path))
                {
                    CharacterScraper.ReadNameAndElement(bm, out string name, out string element);
                    Assert.AreEqual("Skirk", name, file);
                    Assert.AreEqual("Cryo", element, file);
                }
            }
        }

        [TestMethod]
        public void OldCutOffCropHasNoBadge()
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, "character-name-kazuha-cut-1920x1080.png");
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            using (var bm = new Bitmap(path))
                Assert.AreEqual(-1, CharacterScraper.KeyBadgeColumn(bm));
        }
    }
}
