using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace InventoryKamera.Tests
{
    // Item 12: a Name box turns yellow only for another character's name, never for its own.
    [TestClass]
    public sealed class CustomNameTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // temp folder; its table has only the Traveler
            isolation.IsolateSettingsDataAndLogs();
            GenshinProcesor.ReloadData();               // adds the two mannequins in memory
            foreach (var (key, good) in new[] { ("wanderer", "Wanderer"), ("ganyu", "Ganyu"), ("kaedeharakazuha", "KaedeharaKazuha") })
                GenshinProcesor.Characters[key] = new JObject { ["GOOD"] = good };
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        [TestMethod]
        public void OwnNameOrEmptyIsNotAWarning()
        {
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Wanderer", "wanderer"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Manequin1", "manequin1"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Manequin2", "manequin2"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Traveler", "traveler"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Aster", "traveler"), "a name no character has");
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("", "wanderer"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("   ", "manequin1"));
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter(null, "traveler"));
        }

        [TestMethod]
        public void EmptyNamesFallBackToTheirDefault()
        {
            Assert.AreEqual("Wanderer", CustomNames.OrDefault("WandererName", ""));
            Assert.AreEqual("Manequin1", CustomNames.OrDefault("Manequin1Name", "   "));
            Assert.AreEqual("Manequin2", CustomNames.OrDefault("Manequin2Name", null));
            Assert.AreEqual("Scara", CustomNames.OrDefault("WandererName", "Scara"), "a real name stays");
            Assert.AreEqual("", CustomNames.OrDefault("TravelerName", ""), "an empty Traveler name means: read it from the game");
            Assert.AreEqual("", CustomNames.DefaultFor("TravelerName"));
        }

        [TestMethod]
        public void AnotherCharactersNameIsAWarning()
        {
            Assert.IsTrue(GenshinProcesor.NameBelongsToAnotherCharacter("Ganyu", "wanderer"));
            Assert.IsTrue(GenshinProcesor.NameBelongsToAnotherCharacter("Manequin2", "manequin1"));
            Assert.IsTrue(GenshinProcesor.NameBelongsToAnotherCharacter("Wanderer", "traveler"));
            Assert.IsTrue(GenshinProcesor.NameBelongsToAnotherCharacter("Kaedehara Kazuha", "traveler"), "spaces and case don't hide it");
        }
    }
}
