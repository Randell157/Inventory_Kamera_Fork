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
  ""kamisatoayato"": { ""GOOD"": ""KamisatoAyato"", ""Element"": [""hydro""], ""WeaponType"": 0 }
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
    }
}
