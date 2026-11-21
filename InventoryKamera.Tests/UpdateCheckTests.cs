using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // The startup update check: it reads this fork's latest GitHub release and offers it only when
    // its tag is a newer major.minor.patch than the running build.
    [TestClass]
    public sealed class UpdateCheckTests
    {
        // This fork's releases start at 1.5.0 (1.4.x belongs to the upstream forks). Checked as a
        // floor, not an exact value, so a version bump doesn't break the test.
        [TestMethod]
        public void BuildIsThisForksVersion()
        {
            var version = typeof(ForkRepository).Assembly.GetName().Version;
            Assert.IsTrue(new Version(version.Major, version.Minor, version.Build) >= new Version(1, 5, 0), version.ToString());
            Assert.AreEqual(1, version.Major, "a 2.x version would clash with taiwenlee's v2.0.0 pre-releases");
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
