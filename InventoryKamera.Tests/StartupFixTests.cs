using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace InventoryKamera.Tests
{
    // Two problems at the start of a scan: clearing the last scan's screenshots made the scan look
    // stuck, and the slot 1 key was stored as the left mouse button, so the character scan started
    // on whoever was active instead of party slot 1.
    [TestClass]
    public sealed class StartupFixTests
    {
        private MaterialQuantityTests isolation;
        private MemoryTarget log;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // temp working folder, isolated settings and logs
            isolation.IsolateSettingsDataAndLogs();
            log = new MemoryTarget("startup") { Layout = "${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, log);
            LogManager.Configuration = configuration;
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        private static readonly string[] Folders = { "weapons", "artifacts", "characters", "materials" };

        private static void FillOldScan(int filesPerFolder)
        {
            foreach (string name in Folders)
                for (int i = 0; i < filesPerFolder; i++)
                {
                    string dir = Path.Combine("logging", name, $"item{i / 10}");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, $"{i}.png"), "x");
                }
        }

        private static int FilesIn(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();

        [TestMethod]
        public void ResetReturnsQuicklyAndClearsOldScreenshotsInTheBackground()
        {
            FillOldScan(750);   // 3,000 files

            var watch = Stopwatch.StartNew();
            var cleanup = global::InventoryKamera.InventoryKamera.ResetLoggingFolders("logging");
            watch.Stop();

            Assert.IsTrue(watch.ElapsedMilliseconds < 1000, $"reset took {watch.ElapsedMilliseconds} ms");
            foreach (string name in Folders) Assert.AreEqual(0, FilesIn(Path.Combine("logging", name)), name + " starts empty");

            Assert.IsTrue(cleanup.Wait(30000), "background delete finished");
            Assert.AreEqual(3000, cleanup.Result);
            Assert.AreEqual(0, Directory.GetDirectories("logging", "old-*").Length, "old screenshots deleted");
        }

        [TestMethod]
        public void ResetLogsHowManyOldFilesAreCleared()
        {
            FillOldScan(5);
            global::InventoryKamera.InventoryKamera.ResetLoggingFolders("logging").Wait(30000);

            CollectionAssert.Contains(log.Logs.ToList(), "Cleared previous screenshots (20 files)");
            Assert.IsTrue(log.Logs.Any(l => l.StartsWith("Previous screenshots moved aside in ")), "timing of the step the scan waits for");
        }

        [TestMethod]
        public void LockedFileInOneFolderDoesNotStopTheOthersResetting()
        {
            FillOldScan(5);
            string locked = Path.Combine("logging", "weapons", "item0", "0.png");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var cleanup = global::InventoryKamera.InventoryKamera.ResetLoggingFolders("logging");
                cleanup.Wait(30000);

                foreach (string name in new[] { "artifacts", "characters", "materials" })
                    Assert.AreEqual(0, FilesIn(Path.Combine("logging", name)), name + " was reset");
                Assert.IsTrue(FilesIn(Path.Combine("logging", "weapons")) <= 1, "only the locked file can stay");
            }
        }

        [TestMethod]
        public void LeftoverOldFoldersFromAnEarlierRunAreDeleted()
        {
            Directory.CreateDirectory(Path.Combine("logging", "old-artifacts-earlier", "x"));
            File.WriteAllText(Path.Combine("logging", "old-artifacts-earlier", "x", "1.png"), "x");

            global::InventoryKamera.InventoryKamera.ResetLoggingFolders("logging").Wait(30000);

            Assert.AreEqual(0, Directory.GetDirectories("logging", "old-*").Length);
        }

        [TestMethod]
        public void MouseButtonKeyCodesFallBackToTheDefaultKey()
        {
            Assert.AreEqual((int)Keys.D1, MainForm.KeyOrDefault(1, Keys.D1), "old slot 1 default (left mouse button)");
            Assert.AreEqual((int)Keys.D1, MainForm.KeyOrDefault(0, Keys.D1));
            Assert.AreEqual((int)Keys.D1, MainForm.KeyOrDefault(6, Keys.D1));
        }

        [TestMethod]
        public void RealKeyCodesAreKept()
        {
            Assert.AreEqual(49, MainForm.KeyOrDefault(49, Keys.D1));
            Assert.AreEqual(66, MainForm.KeyOrDefault(66, Keys.B));
            Assert.AreEqual(67, MainForm.KeyOrDefault(67, Keys.C));
            Assert.AreEqual((int)Keys.D2, MainForm.KeyOrDefault((int)Keys.D2, Keys.D1), "a key the user chose");
        }

        [TestMethod]
        public void NewSettingsDefaultSlot1KeyIsTheOneKey()
        {
            Assert.AreEqual(49, Properties.Settings.Default.Slot1Key);
        }
    }
}
