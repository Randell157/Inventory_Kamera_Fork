using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static InventoryKamera.CharacterLists;

namespace InventoryKamera.Tests
{
    // Reading constellation and talent levels from the lists on the right, without clicking.
    [TestClass]
    public sealed class CharacterListsTests
    {
        private static string Image(string name)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            return path;
        }

        private static int? ConstellationOf(string file)
        {
            using (var screen = new Bitmap(Image(file)))
            using (var list = screen.Clone(NodeListArea(screen.Size), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                return Constellation(ReadNodes(list));
        }

        [TestMethod]
        public void ConstellationFromTheNodeRings()
        {
            // Screenshots from the 2026-11-21 scans; levels as in the export.
            Assert.AreEqual(0, ConstellationOf("character-constellation-opened-1920x1080.png"), "Vesna, just opened");
            Assert.AreEqual(0, ConstellationOf("character-constellation-detail-1920x1080.png"), "Vesna, node 1 selected");
            Assert.AreEqual(1, ConstellationOf("constellation-ganyu-c1-1920x1080.png"), "Ganyu");
            Assert.AreEqual(3, ConstellationOf("constellation-lanyan-c3-1920x1080.png"), "Lan Yan");
            Assert.AreEqual(4, ConstellationOf("constellation-qiqi-c4-1920x1080.png"), "Qiqi");
            Assert.AreEqual(5, ConstellationOf("constellation-sethos-c5-1920x1080.png"), "Sethos");
            Assert.AreEqual(6, ConstellationOf("constellation-faruzan-c6-1920x1080.png"), "Faruzan");
        }

        [TestMethod]
        public void RingsInEveryElementColour()
        {
            // The ring takes the element's colour; these fell back to clicking when only cyan counted
            // (2026-11-21 04:14 scan; levels from the clicks).
            Assert.AreEqual(6, ConstellationOf("constellation-bennett-c6-1920x1080.png"), "Bennett, Pyro");
            Assert.AreEqual(3, ConstellationOf("constellation-dehya-c3-1920x1080.png"), "Dehya, Pyro");
            Assert.AreEqual(1, ConstellationOf("constellation-baizhu-c1-1920x1080.png"), "Baizhu, Dendro");
            Assert.AreEqual(4, ConstellationOf("constellation-yaoyao-c4-1920x1080.png"), "Yaoyao, Dendro");
            Assert.AreEqual(6, ConstellationOf("constellation-collei-c6-1920x1080.png"), "Collei, Dendro");
            Assert.AreEqual(6, ConstellationOf("constellation-noelle-c6-1920x1080.png"), "Noelle, Geo");
        }

        [TestMethod]
        public void OtherScreensAreNotReadAsAConstellation()
        {
            // Not the constellation screen (or not loaded yet): unclear, so the scan clicks instead.
            Assert.IsNull(ConstellationOf("character-screen-kazuha-1920x1080.png"), "Attributes");
            Assert.IsNull(ConstellationOf("character-talents-1920x1080.png"), "Talents");
        }

        [TestMethod]
        public void UnclearOrImpossibleNodesGiveNoLevel()
        {
            var U = NodeState.Unlocked; var L = NodeState.Locked; var X = NodeState.Unknown;
            Assert.AreEqual(2, Constellation(new[] { U, U, L, L, L, L }));
            Assert.IsNull(Constellation(new[] { U, L, U, L, L, L }), "unlocked after locked");
            Assert.IsNull(Constellation(new[] { U, U, X, L, L, L }), "one node unclear");
        }

        [TestMethod]
        public void OnlySixteenByNineIsRead()
        {
            Assert.IsTrue(Supported(new Size(1920, 1080)));
            Assert.IsTrue(Supported(new Size(2560, 1440)));
            Assert.IsFalse(Supported(new Size(1920, 1200)), "16:10 clicks as before");
        }

        [TestMethod]
        public void LevelTextIsParsed()
        {
            Assert.AreEqual(6, ParseLevel("Vila Blade Dance | r . . Lv. 6"));
            Assert.AreEqual(10, ParseLevel("The Art of Victory | Lv. 10"));
            Assert.AreEqual(13, ParseLevel("Some Skill | LV.13"));
            Assert.AreEqual(4, ParseLevel("Name | ly. 4"));
            Assert.IsNull(ParseLevel("Rite of Spring's Procession"), "no level");
            Assert.IsNull(ParseLevel("Lv. 16"), "out of range");
            Assert.IsNull(ParseLevel("Lv. 1O"), "10 misread with a letter O: unclear, not 1");
            Assert.IsNull(ParseLevel("Lv. G"), "6 misread as G");
            Assert.IsNull(ParseLevel(null));
        }

        [TestMethod]
        public void ALevelNeedsTwoReadingsThatAgree()
        {
            Assert.AreEqual(8, Agreed(new int?[] { 8, 8 }));
            Assert.AreEqual(8, Agreed(new int?[] { null, 8, null, 8 }), "unread settings are skipped");
            Assert.IsNull(Agreed(new int?[] { 1, null, 1, 11 }), "every reading must agree (Rosaria: two read 11 as 1)");
            Assert.IsNull(Agreed(new int?[] { 5, 4, 4 }), "a disagreement makes it unclear");
            Assert.IsNull(Agreed(new int?[] { null, null, null, 9 }), "one reading alone");
            Assert.IsNull(Agreed(new int?[0]));
        }

        private static int?[] TalentsOf(string file, int entries = 4)
        {
            using (var screen = new Bitmap(Image(file)))
            using (var list = screen.Clone(TalentListArea(screen.Size), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                var levels = new int?[entries];
                for (int i = 0; i < entries; i++) levels[i] = ReadTalentLevel(list, i);
                return levels;
            }
        }

        [TestMethod]
        public void TalentLevelsFromTheList()
        {
            var isolation = new MaterialQuantityTests();
            isolation.IsolateSettingsDataAndLogs();
            try
            {
                foreach (var file in new[] { "character-talents-1920x1080.png", "character-talent-detail-1920x1080.png" })
                {
                    // Vesna: Normal Attack 6, Skill 10, Burst 6; the passive has no level
                    CollectionAssert.AreEqual(new int?[] { 6, 10, 6, null }, TalentsOf(file), file);
                }
                // Lists that fell back to clicking in the 2026-11-21 04:14 scan (levels as shown, before the C3/C5 adjustment)
                CollectionAssert.AreEqual(new int?[] { 10, 8, 9 }, TalentsOf("talents-ganyu-1920x1080.png", 3), "Ganyu");
                CollectionAssert.AreEqual(new int?[] { 3, 8, 8 }, TalentsOf("talents-zhongli-1920x1080.png", 3), "Zhongli");
                // Yaoyao C4: the boosted Skill (9) is read by one setting only, so it is clicked rather than trusted
                CollectionAssert.AreEqual(new int?[] { 1, null, 6 }, TalentsOf("talents-yaoyao-1920x1080.png", 3), "Yaoyao");
                // Rosaria C6 (04:35 scan, the saved list area): Skill "Lv. 11" in cyan read as 1 by two settings, 11 by another
                using (var list = new Bitmap(Image("talent-list-rosaria-11-1920x1080.png")))
                {
                    Assert.AreEqual(2, ReadTalentLevel(list, 0), "Rosaria Normal Attack");
                    Assert.IsNull(ReadTalentLevel(list, 1), "Rosaria Skill: settings disagree, so it is clicked");
                    Assert.AreEqual(11, ReadTalentLevel(list, 2), "Rosaria Burst");
                }
            }
            finally
            {
                isolation.RestoreGlobalsAndRemoveOwnSandbox();
            }
        }
    }
}
