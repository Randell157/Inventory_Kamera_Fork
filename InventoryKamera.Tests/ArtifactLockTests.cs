using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Artifact lock status comes from the red padlock, like weapons. Reading the box behind it
    // misread 79 of 2,181 locked artifacts (20-Nov scan): each came right after an unlocked one,
    // so its box was still fading. These are the scanner's own lock crops from that scan
    // (account data, so they live in IK_TEST_IMAGES, not the repo).
    [TestClass]
    public sealed class ArtifactLockTests
    {
        private static bool ReadCrop(string file)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            using (var crop = new Bitmap(path)) return InventoryScraper.IsLockIconRed(crop);
        }

        [TestMethod]
        public void LockedArtifact() => Assert.IsTrue(ReadCrop("artifact-lock-locked-1920x1080.png"));

        [TestMethod]
        public void LockedArtifactRightAfterAnUnlockedOne()
        {
            // Artifact 3: lock badge in game, box still fading (87,95,111); exported unlocked before.
            Assert.IsTrue(ReadCrop("artifact-lock-locked-fading-1920x1080.png"));
        }

        [TestMethod]
        public void LockedArtifactCaughtWithThePadlockHalfFaded()
        {
            // Artifact 114: the weakest case, 141 red pixels; the old 5% cut-off left little room.
            Assert.IsTrue(ReadCrop("artifact-lock-locked-half-faded-1920x1080.png"));
        }

        [TestMethod]
        public void UnlockedArtifact() => Assert.IsFalse(ReadCrop("artifact-lock-unlocked-1920x1080.png"));

        [TestMethod]
        public void CutOffIsTwoPercentOfTheCrop()
        {
            // 47x46 = 2,162 pixels: 43 red pixels is locked, 42 is not.
            Assert.IsTrue(InventoryScraper.IsLockIconRed(CropWithRedPixels(43)));
            Assert.IsFalse(InventoryScraper.IsLockIconRed(CropWithRedPixels(42)));
        }

        private static Bitmap CropWithRedPixels(int count)
        {
            var crop = new Bitmap(47, 46);
            using (var g = Graphics.FromImage(crop)) g.Clear(Color.FromArgb(109, 114, 129));   // a fading box
            for (int i = 0; i < count; i++) crop.SetPixel(i % 47, i / 47, Color.FromArgb(250, 136, 117));
            return crop;
        }
    }
}
