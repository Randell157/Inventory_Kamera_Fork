using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Mannequins: their entries used to be added by editing characters.json as text and
    // restarting the whole scan, and the character scan logged them as errors.
    [TestClass]
    public sealed class CleanupTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // temp folder with a characters.json that has no mannequins
            isolation.IsolateSettingsDataAndLogs();
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        [TestMethod]
        public void MannequinEntriesAreAddedInMemoryOnly()
        {
            string path = Path.Combine("inventorylists", "characters.json");
            string before = File.ReadAllText(path);

            GenshinProcesor.ReloadData();

            Assert.IsTrue(GenshinProcesor.Characters.ContainsKey("manequin1"));
            Assert.IsTrue(GenshinProcesor.Characters.ContainsKey("manequin2"));
            Assert.AreEqual("Manequin1", (string)GenshinProcesor.Characters["manequin1"]["GOOD"]);
            Assert.AreEqual(before, File.ReadAllText(path), "characters.json is not edited");
        }

        [TestMethod]
        public void CustomMannequinNameWorksWithoutTableEntries()
        {
            GenshinProcesor.ReloadData();

            GenshinProcesor.UpdateCharacterName("manequin1", "Anneliese");   // used to throw, which edited the file and restarted the scan

            Assert.AreEqual("anneliese", (string)GenshinProcesor.Characters["manequin1"]["CustomName"]);
            Assert.AreEqual("Manequin1", GenshinProcesor.FindClosestCharacterName("anneliese"), "the custom name is recognized");
        }

        [TestMethod]
        public void ExistingMannequinEntriesAreKept()
        {
            GenshinProcesor.ReloadData();
            GenshinProcesor.Characters["manequin2"]["CustomName"] = "kept";

            GenshinProcesor.AddMannequins(GenshinProcesor.Characters);

            Assert.AreEqual("kept", (string)GenshinProcesor.Characters["manequin2"]["CustomName"]);
        }

        [TestMethod]
        public void OnlyTheTwoMannequinsAreSkipped()
        {
            Assert.IsTrue(Character.IsMannequin("Manequin1"));
            Assert.IsTrue(Character.IsMannequin("Manequin2"));
            Assert.IsFalse(Character.IsMannequin("manequin"), "the old check's name never comes back from a scan");
            Assert.IsFalse(Character.IsMannequin("TravelerCryo"));
            Assert.IsFalse(Character.IsMannequin(null));
        }

        [TestMethod]
        public void InventorySizeCountsEverything()
        {
            var inventory = new Inventory();
            inventory.Add(new Weapon("AmosBow", 90, true, 1));
            inventory.Materials.Add(new Material("Mora", 1));
            inventory.DevMaterials.Add(new Material("HerosWit", 2));

            Assert.AreEqual(3, inventory.Size);
            Assert.IsNull(typeof(Inventory).GetProperty("Size").SetMethod, "no self-calling setter");
        }
    }
}
