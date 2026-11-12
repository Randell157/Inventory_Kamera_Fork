using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    [TestClass]
    public class SmokeTests
    {
        // Proves the test project builds against the app and the test runner works.
        // Real tests arrive with fixes a, b and c.
        [TestMethod]
        public void AppAssemblyLoads()
        {
            var assembly = typeof(Inventory).Assembly;
            Assert.AreEqual("InventoryKamera", assembly.GetName().Name);
        }

        // The project-file conversion changed how .resx images are embedded.
        // Reading every resource proves the windows' images still load at runtime.
        // Names match the 1.4.5 release exe. In 1.4.5 only MainForm has an entry (its image);
        // the other three resource sets are empty there too.
        [DataTestMethod]
        [DataRow("InventoryKamera.Properties.Resources", false)]
        [DataRow("InventoryKamera.MainForm", true)]
        [DataRow("InventoryKamera.ui.main.MainUI", false)]
        [DataRow("InventoryKamera.ui.ExecutablesForm", false)]
        public void EmbeddedResourcesLoad(string baseName, bool expectEntries)
        {
            var manager = new System.Resources.ResourceManager(baseName, typeof(Inventory).Assembly);
            var set = manager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true);
            Assert.IsNotNull(set, "resource set missing: " + baseName);

            int count = 0;
            foreach (System.Collections.DictionaryEntry entry in set)
            {
                Assert.IsNotNull(entry.Value, "resource failed to load: " + entry.Key);
                count++;
            }
            if (expectEntries) Assert.IsTrue(count > 0, "no resources in " + baseName);
        }
    }
}
