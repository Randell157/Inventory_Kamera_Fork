using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace InventoryKamera.Tests
{
    // Level and cap reading (fix c). The old parser kept only digits and "/", so "Level 80 / 9C"
    // became 80/9 and Noelle was exported at ascension 5 instead of 6.
    [TestClass]
    public sealed class LevelReadingTests
    {
        // Every distinct reading in the 11-Nov 1.4.5 scan log, with the level and cap it shows on screen.
        [DataTestMethod]
        [DataRow("Level 90 / 90", 90, 90)]
        [DataRow("Level 80 / 80", 80, 80)]
        [DataRow("Level 70 / 80", 70, 80)]
        [DataRow("Level 80 / 90", 80, 90)]
        [DataRow("Level 90 / 90)", 90, 90)]
        [DataRow("Level 80 7 90.", 80, 90)]
        [DataRow("Level 80 7 90", 80, 90)]
        [DataRow("Level 79 / 80", 79, 80)]
        [DataRow("Level 1 / 20)", 1, 20)]
        [DataRow("Level 90 J 90", 90, 90)]
        [DataRow("Level 90 J 9()", 90, 90)]
        [DataRow("Level 80 J 90 .", 80, 90)]
        [DataRow("Level 80 1 90", 80, 90)]
        [DataRow("Level 50 / 50", 50, 50)]
        [DataRow("‘Level 81 / 90", 81, 90)]
        [DataRow("‘Level 80 / 90", 80, 90)]
        [DataRow("‘Level 70 / 80)", 70, 80)]
        [DataRow("| Level 70 / 80", 70, 80)]
        [DataRow("Level‘90 / 90", 90, 90)]
        [DataRow("Level 90:/ 90)", 90, 90)]
        [DataRow("Level 90/ 9)", 90, 90)]
        [DataRow("Level 90 _/ 90", 90, 90)]
        [DataRow("Level 90 / 9C", 90, 90)]
        [DataRow("Level 90 / 90:", 90, 90)]
        [DataRow("Level 90 / 9(r", 90, 90)]
        [DataRow("Level 90 / 9()", 90, 90)]
        [DataRow("Level 80 J] 90).", 80, 90)]
        [DataRow("Level 80 J 90) .", 80, 90)]
        [DataRow("Level 80 7 90).", 80, 90)]
        [DataRow("Level 80 / 9C", 80, 90)]          // Noelle, 11 Nov
        [DataRow("Level 80 / 90)", 80, 90)]
        [DataRow("Level 80 / 80)", 80, 80)]
        [DataRow("Level 80 / 80 «", 80, 80)]
        [DataRow("Level 73 / 80", 73, 80)]
        [DataRow("Level 70 / 80)", 70, 80)]
        [DataRow("Level 70 / 70", 70, 70)]
        [DataRow("Level 60 / 60).", 60, 60)]
        [DataRow("Level 60 / 60)", 60, 60)]
        [DataRow("Level 60 / 60", 60, 60)]
        [DataRow("Level 20 / 40)", 20, 40)]
        [DataRow("Level 1 / 2()", 1, 20)]
        [DataRow("@Level 90 / 90", 90, 90)]
        [DataRow(": Level-90 / 9C", 90, 90)]
        [DataRow("- Level 60 / 60", 60, 60)]
        [DataRow("\"Level 80 / 90", 80, 90)]
        [DataRow("\"Level 70 / 70", 70, 70)]
        // New shapes from the 12-Nov full character scan with this parser.
        [DataRow("Level 90 / 9(}", 90, 90)]
        [DataRow("Level 90 /-90", 90, 90)]
        [DataRow("Level 90 /-90 °", 90, 90)]
        [DataRow("Level 90 7 90", 90, 90)]
        [DataRow("\\ Level 73 / 80", 73, 80)]
        [DataRow("Rbyel 90 / 90", 90, 90)]
        // Beyond 90.
        [DataRow("Level 92 / 95", 92, 95)]
        [DataRow("Level 95 / 100", 95, 100)]
        public void LoggedReadingsGiveTheLevelAndCapOnScreen(string text, int level, int cap)
        {
            Assert.AreEqual(LevelReadResult.Valid, LevelReading.Read(text, LevelReading.CharacterCaps, out int readLevel, out int readCap), text);
            Assert.AreEqual(level, readLevel, text);
            Assert.AreEqual(cap, readCap, text);
        }

        [DataTestMethod]
        [DataRow("Level 80 / 9C", true)]    // 80/90: ascended, so ascension 6 (was 5)
        [DataRow("Level 80 / 80", false)]
        [DataRow("Level 70 / 80", true)]
        [DataRow("Level 79 / 80", true)]    // past the level-70 ascension; ascension 5
        [DataRow("Level 1 / 20)", false)]
        [DataRow("Level 90 / 90", false)]
        public void AscendedOnlyBetweenThePreviousCapAndThisOne(string text, bool ascended)
        {
            LevelReading.Read(text, LevelReading.CharacterCaps, out int level, out int cap);
            Assert.AreEqual(ascended, LevelReading.IsAscended(level, cap), text);
        }

        [TestMethod]
        public void NoelleIsAscensionSix()
        {
            LevelReading.Read("Level 80 / 9C", LevelReading.CharacterCaps, out int level, out int cap);
            var noelle = new Character { Level = level, Ascended = LevelReading.IsAscended(level, cap) };
            Assert.AreEqual(6, noelle.Ascension);
        }

        // Readings that must not be guessed: the level is kept, the cap is reported as unknown.
        [DataTestMethod]
        [DataRow("Level 80 / 9", 80)]      // a bare 9 is not a cap
        [DataRow("Level 80 / 85", 80)]     // 85 is not a cap
        [DataRow("Level 90 / 80", 90)]     // level above the cap
        [DataRow("Level 50 / 70", 50)]     // 70 needs a level of 60-70
        [DataRow("Level 80", 80)]          // no cap at all
        // Seen in the 12-Nov scan; each was retried until a clean reading came.
        [DataRow("Level 80 / ©", 80)]
        [DataRow("Level 80 /", 80)]
        [DataRow("Level 90 490°", 90)]  // must not become 49 or 490
        [DataRow("Level 90 +90", 90)]
        [DataRow("Level 90 £90°", 90)]
        public void ImpossibleCapsAreNotGuessed(string text, int level)
        {
            Assert.AreEqual(LevelReadResult.LevelOnly, LevelReading.Read(text, LevelReading.CharacterCaps, out int readLevel, out int cap), text);
            Assert.AreEqual(level, readLevel);
            Assert.AreEqual(0, cap);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("Level")]
        [DataRow("Level / 90")]
        [DataRow("Level 0 / 20")]
        [DataRow("Level 120 / 120")]
        public void UnreadableTextGivesNoLevel(string text)
        {
            Assert.AreEqual(LevelReadResult.Unreadable, LevelReading.Read(text, LevelReading.CharacterCaps, out int level, out _), text);
            Assert.AreEqual(0, level);
        }

        [DataTestMethod]
        [DataRow("Lv. 90/90", true, 90, 90)]
        [DataRow("Lv. 80/9C", true, 80, 90)]
        [DataRow("Lv. 1/20", true, 1, 20)]
        [DataRow("Lv. 90/95", false, 90, 0)]   // weapons stop at 90: level kept, cap unknown
        public void WeaponsUseWeaponCaps(string text, bool valid, int level, int cap)
        {
            var expected = valid ? LevelReadResult.Valid : LevelReadResult.LevelOnly;
            Assert.AreEqual(expected, LevelReading.Read(text, LevelReading.WeaponCaps, out int readLevel, out int readCap), text);
            Assert.AreEqual(level, readLevel);
            Assert.AreEqual(cap, readCap);
        }

        // Replays every level reading from the real 11-Nov scan log, in order, as the scanner would:
        // the first Valid reading per character is used. Compares with the (hand-checked) patched export.
        // Log and export are account data, so they are read from IK_TEST_IMAGES and are not in the repo.
        [TestMethod]
        public void RealScanLogMatchesTheCheckedExport()
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real log skipped.");
            var logPath = Path.Combine(folder, "1.4.5-InventoryKamera.debug.log");
            var exportPath = Path.Combine(folder, "1.4.5-export-patched.json");
            if (!File.Exists(logPath) || !File.Exists(exportPath)) Assert.Inconclusive("Log or export missing in " + folder);

            var expected = JObject.Parse(File.ReadAllText(exportPath))["characters"]
                .ToDictionary(c => (string)c["key"], c => (Level: (int)c["level"], Ascension: (int)c["ascension"]));

            var readings = new Dictionary<string, List<string>>();
            string current = null;
            foreach (var line in File.ReadLines(logPath))
            {
                var name = Regex.Match(line, @"Scanned character name as (\w+) with element");
                if (name.Success) { current = name.Groups[1].Value; readings[current] = new List<string>(); continue; }
                var level = Regex.Match(line, @"Scanned character level as (.*)$");
                if (level.Success && current != null) readings[current].Add(level.Groups[1].Value.Trim());
            }

            int readingCount = readings.Values.Sum(r => r.Count);
            Console.WriteLine($"{readings.Count} characters, {readingCount} readings");
            Assert.IsTrue(readingCount >= 100, "expected the full 11-Nov log");

            var problems = new List<string>();
            int compared = 0;
            foreach (var entry in readings.Where(e => e.Value.Count > 0))
            {
                if (!expected.TryGetValue(entry.Key, out var want)) continue;   // e.g. the first-scanned character's repeat
                compared++;
                string first = entry.Value.FirstOrDefault(t => LevelReading.Read(t, LevelReading.CharacterCaps, out _, out _) == LevelReadResult.Valid);
                if (first == null) { problems.Add($"{entry.Key}: no valid reading in {string.Join(" | ", entry.Value)}"); continue; }

                LevelReading.Read(first, LevelReading.CharacterCaps, out int lvl, out int cap);
                int ascension = new Character { Level = lvl, Ascended = LevelReading.IsAscended(lvl, cap) }.Ascension;
                if (lvl != want.Level || ascension != want.Ascension)
                    problems.Add($"{entry.Key}: \"{first}\" -> {lvl}/{cap}, ascension {ascension}; export has {want.Level}, ascension {want.Ascension}");
            }
            Console.WriteLine($"compared {compared} characters with the export");
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }
    }
}
