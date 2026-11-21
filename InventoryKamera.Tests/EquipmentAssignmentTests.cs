using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Matching scanned weapons to characters, and the starter weapons given to the rest.
    [TestClass]
    public sealed class EquipmentAssignmentTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // isolated settings and logs; InventoryKamera logs
            isolation.IsolateSettingsDataAndLogs();
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        private static Character Make(string name, string element = "Anemo") =>
            new Character { NameGOOD = name, Element = element };

        private static Weapon Scanned(string name, string location, int id) =>
            new Weapon(name, 90, true, 1, false, location, id, 5);

        private static List<Weapon> Assign(List<Character> characters, List<Weapon> scanned) =>
            global::InventoryKamera.InventoryKamera.AssignWeapons(
                characters,
                scanned.Where(w => !string.IsNullOrEmpty(w.EquippedCharacter)),
                scanned,
                c => WeaponType.Sword);

        [TestMethod]
        public void TravelerKeyKeepsElementButLocationIsBare()
        {
            var traveler = Make("Traveler", "Cryo");
            Assert.AreEqual("TravelerCryo", traveler.NameGOOD);
            Assert.AreEqual("Traveler", traveler.LocationGOOD);

            var xiao = Make("Xiao");
            Assert.AreEqual("Xiao", xiao.NameGOOD);
            Assert.AreEqual("Xiao", xiao.LocationGOOD);
        }

        [TestMethod]
        public void TravelerWithScannedWeaponGetsNoStarterWeapon()
        {
            var traveler = Make("Traveler", "Cryo");
            var blade = Scanned("ExaiphanesBlade", "Traveler", 11);

            var defaults = Assign(new List<Character> { traveler }, new List<Weapon> { blade });

            Assert.AreEqual(0, defaults.Count);
            Assert.AreSame(blade, traveler.Weapon);
        }

        [TestMethod]
        public void UnarmedTravelerGetsStarterAtBareLocation()
        {
            var defaults = Assign(new List<Character> { Make("Traveler", "Cryo") }, new List<Weapon>());

            Assert.AreEqual(1, defaults.Count);
            Assert.AreEqual("Traveler", defaults[0].EquippedCharacter);
            Assert.AreEqual("DullBlade", defaults[0].Name);
        }

        [TestMethod]
        public void StarterWeaponsGetIdsAfterTheScannedOnes()
        {
            var scanned = new List<Weapon>
            {
                Scanned("ElegyForTheEnd", "Gorou", 0),
                Scanned("AmosBow", "Ganyu", 1),
                Scanned("CoolSteel", "", 305),
            };
            var characters = new List<Character> { Make("Gorou"), Make("Ganyu"), Make("Xiao"), Make("Clorinde"), Make("Ifa") };

            var defaults = Assign(characters, scanned);

            CollectionAssert.AreEqual(new[] { 306, 307, 308 }, defaults.Select(w => w.Id).ToArray());
            var all = scanned.Concat(defaults).Select(w => w.Id).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "weapon ids must be unique");
        }

        [TestMethod]
        public void StarterIdsStartAtZeroWhenNoWeaponsWereScanned()
        {
            var defaults = Assign(new List<Character> { Make("Xiao"), Make("Ifa") }, new List<Weapon>());

            CollectionAssert.AreEqual(new[] { 0, 1 }, defaults.Select(w => w.Id).ToArray());
        }
    }
}
