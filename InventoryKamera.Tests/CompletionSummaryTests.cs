using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Text of the "Scan complete" popup that replaced the Genshin Optimizer prompt.
    [TestClass]
    public sealed class CompletionSummaryTests
    {
        private MaterialQuantityTests isolation;

        [TestInitialize]
        public void Isolate()
        {
            isolation = new MaterialQuantityTests();   // isolated settings and logs; GOOD reads settings
            isolation.IsolateSettingsDataAndLogs();
        }

        [TestCleanup]
        public void Restore() => isolation?.RestoreGlobalsAndRemoveOwnSandbox();

        private static readonly string File = Path.Combine(@"C:\Exports", "genshinData_GOOD_2026_11_12_04_08.json");

        [TestMethod]
        public void ListsWhatWasExportedWhereAndNoErrors()
        {
            var scan = new global::InventoryKamera.InventoryKamera();
            scan.Inventory.Materials.Add(new Material("Mora", 56411823));
            scan.Inventory.Materials.Add(new Material("Medaka", 10));
            scan.Inventory.Materials.Add(new Material("Unreadable", 0));   // left out of the export, so not counted

            string text = new GOOD(scan).CompletionSummary(File, 0);

            StringAssert.Contains(text, "Exported: 2 materials");
            StringAssert.Contains(text, "File: genshinData_GOOD_2026_11_12_04_08.json");
            StringAssert.Contains(text, @"Folder: C:\Exports");
            StringAssert.Contains(text, "No errors.");
        }

        [TestMethod]
        public void UsesSingularForOne()
        {
            var scan = new global::InventoryKamera.InventoryKamera();
            scan.Inventory.Materials.Add(new Material("Mora", 1));

            string text = new GOOD(scan).CompletionSummary(File, 1);

            StringAssert.Contains(text, "Exported: 1 material");
            Assert.IsFalse(text.Contains("1 materials"));
            StringAssert.Contains(text, "1 error: see the error log in the window.");
        }

        [TestMethod]
        public void ReportsErrorCountAndEmptyExport()
        {
            string text = new GOOD(new global::InventoryKamera.InventoryKamera()).CompletionSummary(File, 3);

            StringAssert.Contains(text, "Nothing was exported.");
            StringAssert.Contains(text, "3 errors: see the error log in the window.");
        }
    }
}
