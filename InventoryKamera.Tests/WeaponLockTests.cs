using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Reading a weapon card's lock icon. The lock sits at the card's right edge, not where
    // the artifact card's lock is; before this, no weapon was ever exported as locked.
    // Lock status comes from the padlock's red colour, because the box behind it may still be
    // fading from the previous item's lock state when the card is captured.
    [TestClass]
    public sealed class WeaponLockTests
    {
        private static readonly Color CardBackground = Color.FromArgb(236, 229, 216);
        private static readonly Color LockedBox = Color.FromArgb(73, 83, 102);
        private static readonly Color FadingBox = Color.FromArgb(109, 114, 129);   // measured on a real card
        private static readonly Color UnlockedBox = Color.FromArgb(219, 217, 215);
        private static readonly Color RedPadlock = Color.FromArgb(250, 136, 117);
        private static readonly Color GreyPadlock = Color.FromArgb(160, 163, 170);

        private static bool ReadLock(Bitmap card, bool isNormal, bool isWeapon = true)
        {
            Rectangle r = InventoryScraper.LockRegion(card.Size, isNormal, isWeapon: isWeapon);
            using (Bitmap crop = card.Clone(r, card.PixelFormat)) return InventoryScraper.IsLockIconRed(crop);
        }

        // A plain card with a lock box and padlock where the 1920x1080 weapon card has them
        // (box x 86.6-94.7%, y 36.0-40.7%).
        private static Bitmap SyntheticCard(Color box, Color padlock)
        {
            var card = new Bitmap(494, 840);
            using (var g = Graphics.FromImage(card))
            {
                g.Clear(CardBackground);
                using (var brush = new SolidBrush(box)) g.FillRectangle(brush, 428, 302, 41, 41);
                using (var brush = new SolidBrush(padlock)) g.FillRectangle(brush, 440, 312, 17, 20);
            }
            return card;
        }

        [TestMethod]
        public void LockedWeaponCardReadsLocked()
        {
            using (var card = SyntheticCard(LockedBox, RedPadlock)) Assert.IsTrue(ReadLock(card, isNormal: true));
        }

        [TestMethod]
        public void LockedCardWithFadingBoxReadsLocked()
        {
            using (var card = SyntheticCard(FadingBox, RedPadlock)) Assert.IsTrue(ReadLock(card, isNormal: true));
        }

        [TestMethod]
        public void UnlockedWeaponCardReadsUnlocked()
        {
            using (var card = SyntheticCard(UnlockedBox, GreyPadlock)) Assert.IsFalse(ReadLock(card, isNormal: true));
        }

        [TestMethod]
        public void ArtifactLockPositionIsUnchanged()
        {
            Rectangle artifact = InventoryScraper.LockRegion(new Size(494, 840), isNormal: true);
            Assert.AreEqual((int)(494 * 0.75), artifact.X);
            Assert.AreEqual((int)(840 * 0.353), artifact.Y);
        }

        // Cards from real 1920x1080 scans (account data, so not in the repo).
        private static Bitmap RealCard(string file)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            return new Bitmap(path);
        }

        [TestMethod]
        public void RealLockedWeaponCard()
        {
            using (var card = RealCard("weapon-card-locked-1920x1080.png"))
            {
                Assert.IsTrue(ReadLock(card, isNormal: true), "weapon lock position");
                Assert.IsFalse(ReadLock(card, isNormal: true, isWeapon: false), "the artifact position misses a weapon's lock");
            }
        }

        [TestMethod]
        public void RealLockedCardCapturedMidFade()
        {
            // Mouun's Moon id 91 (20-Nov scan): locked, scanned right after an unlocked copy.
            using (var card = RealCard("weapon-card-locked-fading-1920x1080.png")) Assert.IsTrue(ReadLock(card, isNormal: true));
        }

        [TestMethod]
        public void RealUnlockedWeaponCard()
        {
            // Mouun's Moon id 90 (20-Nov scan): unlocked.
            using (var card = RealCard("weapon-card-unlocked-1920x1080.png")) Assert.IsFalse(ReadLock(card, isNormal: true));
        }
    }
}
