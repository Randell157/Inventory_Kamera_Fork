using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // The startup update check: it reads this fork's latest GitHub release and offers it only when
    // its tag is a newer major.minor.patch than the running build.
    [TestClass]
    public sealed class UpdateCheckTests
    {
        [TestMethod]
        public void BuildIsVersionOnePointFive()
        {
            var version = typeof(ForkRepository).Assembly.GetName().Version;
            Assert.AreEqual("1.5.0", version.ToString(3));
        }

        [TestMethod]
        public void LinksPointAtThisFork()
        {
            Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/", ForkRepository.Url);
            Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/releases", ForkRepository.ReleasesUrl);
            Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/issues", ForkRepository.IssuesUrl);
        }

        [DataTestMethod]
        [DataRow("v1.5.1", true)]
        [DataRow("v1.6.0", true)]
        [DataRow("v2.0", true)]
        [DataRow("1.5.1", true)]
        [DataRow("v1.5.0", false)]     // the running build's wildcard revision must not count as older
        [DataRow("v1.5", false)]
        [DataRow("v1.4.11", false)]
        [DataRow("v1.4.5", false)]
        [DataRow("Inventory Kamera v1.5.2", true)]
        [DataRow("", false)]
        [DataRow(null, false)]
        [DataRow("latest", false)]
        public void OnlyANewerTagIsOffered(string tag, bool newer)
        {
            var running = new Version(1, 5, 0, 31337);
            Assert.AreEqual(newer, ForkRepository.IsNewerRelease(tag, running), tag ?? "null");
        }
    }
}
