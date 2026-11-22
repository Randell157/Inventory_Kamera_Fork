using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;

namespace InventoryKamera.Tests
{
    // 1.6.1: a new install no longer downloads the lookup tables while the window is being built
    // (silently, and a failure crashed the startup). The window downloads them once it's shown,
    // then shows one of the FirstDownload messages. All offline: nothing here downloads.
    [TestClass]
    public sealed class FirstDownloadTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // temp folder with small fixture tables
            isolation.IsolateSettingsDataAndLogs();
        }

        [TestCleanup]
        public void Restore()
        {
            // Put the fixture tables back and reload, so the shared lists are as the sandbox left them.
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), "{\"traveler\":{\"GOOD\":\"Traveler\"}}");
            File.WriteAllText(Path.Combine("inventorylists", "artifacts.json"), "{\"fixture\":{}}");
            foreach (var name in new[] { "weapons", "materials" })
                File.WriteAllText(Path.Combine("inventorylists", name + ".json"), "{\"fixture\":\"Fixture\"}");
            GenshinProcesor.ReloadData();
            isolation?.RestoreGlobalsAndRemoveOwnSandbox();
        }

        [TestMethod]
        public void MissingTablesAreReportedNotDownloaded()
        {
            Assert.IsFalse(new DatabaseManager().TablesMissing, "all four fixture tables are there");

            File.Delete(Path.Combine("inventorylists", "characters.json"));
            var watch = Stopwatch.StartNew();
            var database = new DatabaseManager();
            watch.Stop();
            Assert.IsTrue(database.TablesMissing);
            Assert.IsFalse(File.Exists(Path.Combine("inventorylists", "characters.json")), "the constructor must not download");
            Assert.IsTrue(watch.ElapsedMilliseconds < 2000, $"the constructor took {watch.ElapsedMilliseconds} ms");

            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), "{}");
            Assert.IsTrue(new DatabaseManager().TablesMissing, "an empty table counts as missing");
        }

        [TestMethod]
        public void MissingTablesGiveEmptyListsInsteadOfACrash()
        {
            File.Delete(Path.Combine("inventorylists", "weapons.json"));
            GenshinProcesor.ReloadData();
            Assert.AreEqual(0, GenshinProcesor.Weapons.Count);
            Assert.AreEqual(0, GenshinProcesor.Artifacts.Count);
            Assert.AreEqual(0, GenshinProcesor.Materials.Count);
            CollectionAssert.AreEquivalent(new[] { "manequin1", "manequin2" }, new System.Collections.Generic.List<string>(GenshinProcesor.Characters.Keys),
                "only the in-memory mannequins");
            Assert.IsFalse(GenshinProcesor.NameBelongsToAnotherCharacter("Ganyu", "traveler"), "the name boxes still work");
        }

        [TestMethod]
        public void CountsComeFromTheSavedTables()
        {
            File.WriteAllText(Path.Combine("inventorylists", "weapons.json"), "{\"a\":\"A\",\"b\":\"B\",\"c\":\"C\"}");
            var counts = new DatabaseManager().TableCounts();
            Assert.AreEqual((1, 3, 1, 1), counts);
        }

        [TestMethod]
        public void RetryAfterIsSecondsOrADate()
        {
            var now = new System.DateTime(2026, 11, 22, 2, 29, 15, System.DateTimeKind.Utc);
            Assert.AreEqual(now.AddSeconds(1845), DatabaseManager.RetryTime("1845", now), "today's answer: 1845 s");
            Assert.AreEqual(new System.DateTime(2026, 11, 22, 3, 0, 0, System.DateTimeKind.Utc),
                DatabaseManager.RetryTime("Sun, 22 Nov 2026 03:00:00 GMT", now));
            Assert.IsNull(DatabaseManager.RetryTime(null, now));
            Assert.IsNull(DatabaseManager.RetryTime("soon", now));
        }

        [TestMethod]
        public void FailureNamesGitLabsTimeRoundedUp()
        {
            var us = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var now = new System.DateTime(2026, 11, 21, 18, 29, 15);
            string text = FirstDownload.FailTextFor(new System.DateTime(2026, 11, 21, 18, 59, 50), now, us);
            StringAssert.EndsWith(text, "Try again after 7:00 PM: Options → Update Lookup Tables.");
            StringAssert.Contains(text, "GitLab limits how much can be downloaded without an account and asked Inventory Kamera to wait.");
            StringAssert.Contains(FirstDownload.FailTextFor(new System.DateTime(2026, 11, 21, 19, 0, 0), now, us), "after 7:00 PM:", "a whole minute stays");
            StringAssert.Contains(FirstDownload.FailTextFor(new System.DateTime(2026, 11, 22, 0, 30, 0), now, us), "after 11/22/2026 12:30 AM:", "another day names the date");
            Assert.AreEqual(FirstDownload.FailText, FirstDownload.FailTextFor(new System.DateTime(2026, 11, 21, 18, 0, 0), now, us), "a time already past");
            Assert.AreEqual(FirstDownload.FailText, FirstDownload.FailTextFor(null), "offline: no time");
            Assert.AreEqual(new System.DateTime(2026, 11, 21, 19, 0, 0), FirstDownload.RoundUpToMinute(new System.DateTime(2026, 11, 21, 18, 59, 59, 600)),
                "the log rounds the same way (it said 6:59 PM while the popup said 7:00 PM)");
        }

        [TestMethod]
        public void LongStatusesWrapOntoTwoSmallerLines()
        {
            var family = new System.Drawing.FontFamily("Microsoft Sans Serif");
            foreach (var status in new[] { "Scanning", "Finished", "Scan aborted", "Clearing files…", "Scan Stopped" })
                Assert.AreEqual((UserInterface.StatusSize, status), UserInterface.FitStatus(status, family), status + " fits on one line");

            foreach (var status in new[] { FirstDownload.Status, FirstDownload.MissingStatus })
            {
                var (size, text) = UserInterface.FitStatus(status, family);
                Assert.AreEqual(UserInterface.StatusSmallSize, size, status);
                var lines = text.Split('\n');
                Assert.AreEqual(2, lines.Length, text);
                Assert.AreEqual(status, string.Join(" ", lines), "same words");
                using (var font = new System.Drawing.Font(family, size))
                {
                    foreach (var line in lines)
                        Assert.IsTrue(System.Windows.Forms.TextRenderer.MeasureText(line, font).Width <= UserInterface.StatusWidth, line);
                    Assert.IsTrue(System.Windows.Forms.TextRenderer.MeasureText(text, font).Height <= UserInterface.StatusHeight, text);
                }
            }
        }

        [TestMethod]
        public void MessagesMatchTheMockup()
        {
            Assert.AreEqual("Inventory Kamera downloaded the game data for Genshin Impact 7.2: " +
                "123 characters, 266 weapons, 63 artifact sets and 779 materials.\n\nYou can start scanning.",
                FirstDownload.SuccessText("7.2", (123, 266, 63, 779)));
            StringAssert.StartsWith(FirstDownload.SuccessText("0.0", (1, 2, 3, 4)), "Inventory Kamera downloaded the latest game data: 1 characters");
            Assert.AreEqual("New game data", FirstDownload.SuccessTitle);
            Assert.AreEqual("Lookup tables not downloaded", FirstDownload.FailTitle);
            StringAssert.Contains(FirstDownload.FailText, "Scans won't work until the data is downloaded.");
            StringAssert.EndsWith(FirstDownload.FailText, "Try again in a few minutes: Options → Update Lookup Tables.");
            Assert.AreEqual("Downloading lookup tables…", FirstDownload.Status);
        }
    }
}
