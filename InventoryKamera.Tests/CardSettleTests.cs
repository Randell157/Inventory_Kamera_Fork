using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Item 13: a weapon or artifact card is read only once it has stopped changing, and a card that
    // still shows the previous item is waited out. Offline: drawn cards, fake captures and a fake clock,
    // plus the real fading cards from IK_TEST_IMAGES when that folder is set.
    [TestClass]
    public sealed class CardSettleTests
    {
        private static readonly Size CardSize = new Size(494, 840);   // a 1920x1080 card

        // A card with something in every region, so changes anywhere can be seen.
        private static Bitmap DrawCard(Color lockColor, string name = "Hydro goblet")
        {
            var bm = new Bitmap(CardSize.Width, CardSize.Height);
            using (var g = Graphics.FromImage(bm))
            using (var font = new Font("Arial", 18))
            {
                g.Clear(Color.FromArgb(235, 230, 225));
                g.FillRectangle(Brushes.DarkGoldenrod, 0, 0, CardSize.Width, 58);
                g.DrawString(name, font, Brushes.White, 10, 10);
                g.DrawString("HP 46.6%", font, Brushes.Black, 25, 125);
                g.DrawString("+20   CRIT Rate 10.5%", font, Brushes.Black, 25, 320);
                Rectangle lockBox = InventoryScraper.LockRegion(CardSize, isNormal: true);
                using (var b = new SolidBrush(lockColor)) g.FillEllipse(b, lockBox);
            }
            return bm;
        }

        private static readonly Color Gray = Color.FromArgb(150, 150, 150);
        private static readonly Color Red = Color.FromArgb(220, 60, 50);

        private static IList<Rectangle> ArtifactRegions(Size s) => CardSettle.CardRegions(s, isNormal: true, isWeapon: false);

        [TestMethod]
        public void SameCardMatches()
        {
            using (var a = DrawCard(Red))
            using (var b = DrawCard(Red))
                Assert.IsTrue(CardSettle.Matches(a, b, ArtifactRegions(a.Size)));
        }

        [TestMethod]
        public void PadlockChangeAloneIsSeen()
        {
            // Artifact 361: the rest of the card had changed, only the padlock was still the old one.
            using (var gray = DrawCard(Gray))
            using (var red = DrawCard(Red))
                Assert.IsFalse(CardSettle.Matches(gray, red, ArtifactRegions(gray.Size)));
        }

        [TestMethod]
        public void DifferentItemIsSeen()
        {
            using (var a = DrawCard(Red, "Hydro goblet"))
            using (var b = DrawCard(Red, "Pyro goblet"))
                Assert.IsFalse(CardSettle.Matches(a, b, ArtifactRegions(a.Size)));
        }

        [TestMethod]
        public void SmallNoiseAndArtworkAreIgnored()
        {
            using (var a = DrawCard(Red))
            using (var b = DrawCard(Red))
            {
                Rectangle lockBox = InventoryScraper.LockRegion(CardSize, isNormal: true);
                b.SetPixel(lockBox.X + 2, lockBox.Y + 2, Color.Black);       // 2 stray pixels in the padlock
                b.SetPixel(lockBox.X + 3, lockBox.Y + 2, Color.Black);
                b.SetPixel(lockBox.X + 5, lockBox.Y + 5, Color.FromArgb(240, 70, 60));   // within tolerance
                using (var g = Graphics.FromImage(b))
                    g.FillRectangle(Brushes.Blue, (int)(CardSize.Width * 0.6), (int)(CardSize.Height * 0.10), 120, 80); // artwork area
                Assert.IsTrue(CardSettle.Matches(a, b, ArtifactRegions(a.Size)));
            }
        }

        [TestMethod]
        public void DifferentSizesNeverMatch()
        {
            using (var a = DrawCard(Red))
            using (var b = new Bitmap(a, new Size(400, 700)))
                Assert.IsFalse(CardSettle.Matches(a, b, ArtifactRegions(a.Size)));
        }

        [TestMethod]
        public void WeaponRegionsCoverTheWeaponPadlock()
        {
            IList<Rectangle> regions = CardSettle.CardRegions(CardSize, isNormal: true, isWeapon: true);
            CollectionAssert.Contains((System.Collections.ICollection)regions, InventoryScraper.LockRegion(CardSize, true, isWeapon: true));
            Assert.AreEqual(5, regions.Count, "weapons have one padlock position");
            Assert.AreEqual(6, CardSettle.CardRegions(CardSize, true, false).Count, "artifacts have two (sanctified moves it)");
        }

        // ---- The loop, with fake captures and a clock that only moves when the scan waits ----

        private sealed class FakeScreen
        {
            private readonly System.Collections.Generic.Queue<Bitmap> frames;
            private Bitmap last;
            public long Now;
            public int Captures;
            public FakeScreen(params Bitmap[] f) { frames = new System.Collections.Generic.Queue<Bitmap>(f); }
            public Bitmap Capture() { Captures++; if (frames.Count > 0) last = frames.Dequeue(); return (Bitmap)last.Clone(); }
            public void Wait(int ms) => Now += ms;
        }

        private static Bitmap Run(CardSettle settle, FakeScreen screen) =>
            settle.Settle(screen.Capture, ArtifactRegions, screen.Wait, () => screen.Now);

        private static bool Same(Bitmap a, Bitmap b) => CardSettle.Matches(a, b, new[] { new Rectangle(Point.Empty, a.Size) });

        [TestMethod]
        public void SettledCardIsTakenAfterOnePoll()
        {
            var settle = new CardSettle();
            using (var card = DrawCard(Red))
            using (var got = Run(settle, new FakeScreen(card, card)))
                Assert.IsTrue(Same(card, got));
            Assert.AreEqual(1, settle.Cards);
            Assert.AreEqual(CardSettle.PollMs, settle.TotalExtraMs, "a card that is already settled costs one poll");
        }

        [TestMethod]
        public void OldCardStillShowingIsWaitedOut()
        {
            // Artifact 873/874: the card for 874 was captured while 873's card was still on screen.
            var settle = new CardSettle();
            using (var old = DrawCard(Red, "Hydro goblet"))
            using (var next = DrawCard(Red, "Pyro goblet"))
            {
                Run(settle, new FakeScreen(old, old)).Dispose();
                using (var got = Run(settle, new FakeScreen(old, old, old, next, next)))
                    Assert.IsTrue(Same(next, got), "the new card is read, not a second copy of the old one");
            }
            Assert.AreEqual(1, settle.NeverChangedWaited);
            Assert.AreEqual(0, settle.StillSameAfterLimit);
            Assert.AreEqual(0, settle.TimedOut);
            Assert.AreEqual(1, settle.LateSwitches);
            Assert.AreEqual(3 * CardSettle.PollMs, settle.LongestLateSwitchMs, "three pictures of the old card, then the new one on the third check");
        }

        [TestMethod]
        public void FadingPadlockIsWaitedOut()
        {
            var settle = new CardSettle();
            using (var gray = DrawCard(Gray))
            using (var red = DrawCard(Red))
            using (var got = Run(settle, new FakeScreen(gray, red, red)))
                Assert.IsTrue(Same(red, got), "the red padlock is read, not the gray one");
        }

        [TestMethod]
        public void CardThatNeverSettlesStopsAtTheLimit()
        {
            var settle = new CardSettle();
            var frames = new List<Bitmap>();
            for (int i = 0; i < 80; i++) frames.Add(DrawCard(i % 2 == 0 ? Gray : Red));
            var screen = new FakeScreen(frames.ToArray());
            Run(settle, screen).Dispose();
            Assert.AreEqual(1, settle.TimedOut);
            Assert.IsTrue(screen.Now >= CardSettle.GiveUpMs && screen.Now < CardSettle.GiveUpMs + CardSettle.PollMs, $"stopped at {screen.Now} ms");
            frames.ForEach(f => f.Dispose());
        }

        [TestMethod]
        public void DuplicateSeenChangingIsTakenAtOnce()
        {
            // Two identical weapons in a row: the game redraws the card after the click, so it changes,
            // then settles on the same picture. That is the new item, not a leftover.
            var settle = new CardSettle();
            using (var card = DrawCard(Red))
            using (var redrawing = DrawCard(Gray))
            {
                Run(settle, new FakeScreen(card, card)).Dispose();
                var screen = new FakeScreen(redrawing, card, card);
                using (var got = Run(settle, screen)) Assert.IsTrue(Same(card, got));
                Assert.AreEqual(2 * CardSettle.PollMs, screen.Now, "no copy wait");
            }
            Assert.AreEqual(1, settle.DuplicatesTakenAtOnce);
            Assert.AreEqual(0, settle.NeverChangedWaited);
        }

        [TestMethod]
        public void AutoStopsAtItsOwnLimit()
        {
            var settle = new CardSettle();
            var frames = new List<Bitmap>();
            for (int i = 0; i < 80; i++) frames.Add(DrawCard(i % 2 == 0 ? Gray : Red));
            var screen = new FakeScreen(frames.ToArray());
            settle.Settle(screen.Capture, ArtifactRegions, screen.Wait, () => screen.Now, CardSettle.AutoGiveUpMs).Dispose();
            Assert.AreEqual(1, settle.TimedOut);
            Assert.IsTrue(screen.Now >= CardSettle.AutoGiveUpMs && screen.Now < CardSettle.AutoGiveUpMs + CardSettle.PollMs, $"stopped at {screen.Now} ms");
            frames.ForEach(f => f.Dispose());
        }

        [TestMethod]
        public void CardRectangleIsUnchanged()
        {
            Assert.AreEqual(new Rectangle(1306, 119, 494, 840), InventoryScraper.CardRectangle(new Size(1920, 1080), isNormal: true));
        }

        [TestMethod]
        public void IdenticalNextItemThatNeverChangedWaitsForTheLimit()
        {
            // The card never changed after the click: it may still be the old one, so wait.
            var settle = new CardSettle();
            using (var card = DrawCard(Red))
            {
                Run(settle, new FakeScreen(card, card)).Dispose();
                var screen = new FakeScreen(card);
                using (var got = Run(settle, screen)) Assert.IsTrue(Same(card, got));
                Assert.IsTrue(screen.Now >= CardSettle.SameAsPreviousLimitMs && screen.Now < CardSettle.GiveUpMs, $"accepted at {screen.Now} ms");
            }
            Assert.AreEqual(1, settle.StillSameAfterLimit);
            Assert.AreEqual(1, settle.NeverChangedWaited);
            Assert.AreEqual(0, settle.TimedOut);
        }

        [TestMethod]
        public void SummaryForTheLog()
        {
            var settle = new CardSettle();
            Assert.AreEqual("Card settle (weapons): no cards", settle.Summary("weapons"));
            using (var card = DrawCard(Red))
            {
                var screen = new FakeScreen(card, card);
                settle.Settle(screen.Capture, ArtifactRegions, screen.Wait, () => screen.Now, waitedBeforeMs: 100).Dispose();
            }
            Assert.AreEqual("Card settle (artifacts): 1 cards, settled on average 115 ms after the click " +
                "(extra wait average 15 ms, longest 15 ms); 0 matched the previous card after changing (taken at once); " +
                "0 never changed and waited (0 still matched after 300 ms); 0 showed the previous card first (slowest switched after 0 ms); 0 hit the time limit", settle.Summary("artifacts"));
        }

        // ---- Real cards from 1920x1080 scans (account data, so not in the repo) ----

        private static Bitmap Real(string file)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            return new Bitmap(path);
        }

        [TestMethod]
        public void RealWeaponCardsAreToldApart()
        {
            using (var locked = Real("weapon-card-locked-1920x1080.png"))
            using (var fading = Real("weapon-card-locked-fading-1920x1080.png"))
            using (var unlocked = Real("weapon-card-unlocked-1920x1080.png"))
            using (var copy = (Bitmap)locked.Clone())
            {
                var regions = CardSettle.CardRegions(locked.Size, isNormal: true, isWeapon: true);
                Assert.IsTrue(CardSettle.Matches(locked, copy, regions), "a card matches itself");
                Assert.IsFalse(CardSettle.Matches(fading, unlocked, regions), "the fading capture of id 91 differs from id 90");
                Assert.IsFalse(CardSettle.Matches(locked, unlocked, regions));
                Assert.IsFalse(CardSettle.Matches(locked, fading, regions));
            }
        }

        [TestMethod]
        public void RealArtifactPadlockStagesAreToldApart()
        {
            // Crops of the padlock alone: locked, fading, half faded and unlocked.
            string[] files = { "artifact-lock-locked-1920x1080.png", "artifact-lock-locked-fading-1920x1080.png",
                               "artifact-lock-locked-half-faded-1920x1080.png", "artifact-lock-unlocked-1920x1080.png" };
            var crops = new List<Bitmap>();
            try
            {
                foreach (var f in files) crops.Add(Real(f));
                for (int i = 0; i < crops.Count; i++)
                    for (int j = i + 1; j < crops.Count; j++)
                    {
                        if (crops[i].Size != crops[j].Size) Assert.Inconclusive("crops differ in size");
                        Assert.IsFalse(CardSettle.Matches(crops[i], crops[j], new[] { new Rectangle(Point.Empty, crops[i].Size) }),
                            $"{files[i]} vs {files[j]}");
                    }
            }
            finally { crops.ForEach(c => c.Dispose()); }
        }
    }
}
