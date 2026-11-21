using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // On Auto, the character screens wait until the watched area has changed and then holds still,
    // instead of fixed waits sized for slow PCs.
    [TestClass]
    public sealed class ScreenSettleTests
    {
        private static Bitmap Frame(Color c, bool sparkle = false)
        {
            var bm = new Bitmap(100, 50);
            using (var g = Graphics.FromImage(bm))
            {
                g.Clear(c);
                if (sparkle) g.FillRectangle(Brushes.White, 40, 20, 3, 3);   // 9 of 5,000 pixels
            }
            return bm;
        }

        private sealed class FakeScreen
        {
            private readonly System.Collections.Generic.Queue<Func<Bitmap>> frames;
            private readonly Func<Bitmap> afterLast;
            internal long Now;
            internal int Captures;
            internal readonly List<int> Waits = new List<int>();

            internal FakeScreen(IEnumerable<Func<Bitmap>> frames, Func<Bitmap> afterLast = null)
            {
                this.frames = new System.Collections.Generic.Queue<Func<Bitmap>>(frames);
                this.afterLast = afterLast;
            }

            internal Bitmap Capture()
            {
                Captures++;
                if (frames.Count > 0) return frames.Dequeue()();
                if (afterLast != null) return afterLast();
                throw new InvalidOperationException("no more frames");
            }

            internal void Wait(int ms) { Waits.Add(ms); Now += ms; }
        }

        private static readonly Color Old = Color.FromArgb(20, 60, 55), New = Color.FromArgb(200, 190, 150), Mid = Color.FromArgb(110, 125, 100);

        [TestMethod]
        public void FixedSpeedRunsTheOldWaitWithoutCapturing()
        {
            var screen = new FakeScreen(new Func<Bitmap>[0]);
            int clicks = 0;
            ScreenSettle.Run("open talents", () => clicks++, 2000, auto: false, delay: 0.5,
                capture: screen.Capture, wait: screen.Wait, nowMs: () => screen.Now);
            Assert.AreEqual(1, clicks);
            CollectionAssert.AreEqual(new[] { 1000 }, screen.Waits, "Fastest: 2000 ms x 0.5, as before");
            Assert.AreEqual(0, screen.Captures);
        }

        [TestMethod]
        public void FixedSpeedWaitsMatchTheOldSystemWait()
        {
            // The old code called Navigation.SystemWait(speed) = Wait((int)(BaseWait(speed) * delay)).
            foreach (double delay in new[] { 0.5, 0.75, 1.0, 1.25, 1.5 })
                foreach (var speed in new[] { Navigation.Speed.CharacterUI, Navigation.Speed.SelectNextCharacter, Navigation.Speed.Normal })
                {
                    var screen = new FakeScreen(new Func<Bitmap>[0]);
                    ScreenSettle.Run("x", () => { }, Navigation.BaseWait(speed), false, delay, screen.Capture, screen.Wait, () => screen.Now);
                    Assert.AreEqual((int)(Navigation.BaseWait(speed) * delay), screen.Waits.Single(), $"{speed} at {delay}");
                }
        }

        [TestMethod]
        public void WaitsForTheChangeThenForTwoMatchingCaptures()
        {
            // still the old screen twice, then fading, then the new screen twice
            var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Old), () => Frame(Old), () => Frame(Mid), () => Frame(New), () => Frame(New) });
            using (var before = Frame(Old))
            {
                long took = ScreenSettle.WaitForChange(before, screen.Capture, 2000, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsFalse(gaveUp);
                Assert.AreEqual(5 * ScreenSettle.PollMs, took);
                Assert.AreEqual(5, screen.Captures);
            }
        }

        [TestMethod]
        public void ASparkleIsNotAScreenChange()
        {
            // Only a sparkle differs from before: the click hasn't shown yet, so keep waiting until the limit.
            var screen = new FakeScreen(new Func<Bitmap>[0], () => Frame(Old, sparkle: true));
            using (var before = Frame(Old))
            {
                long took = ScreenSettle.WaitForChange(before, screen.Capture, 500, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsTrue(gaveUp);
                Assert.IsTrue(took >= 500 && took < 500 + ScreenSettle.PollMs, took.ToString());
            }
        }

        [TestMethod]
        public void AScreenThatKeepsChangingGivesUpAtTheStepsNormalWait()
        {
            int n = 0;
            var screen = new FakeScreen(new Func<Bitmap>[0], () => Frame(n++ % 2 == 0 ? New : Mid));
            using (var before = Frame(Old))
            {
                long took = ScreenSettle.WaitForChange(before, screen.Capture, 700, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsTrue(gaveUp);
                Assert.IsTrue(took >= 700 && took < 700 + ScreenSettle.PollMs, took.ToString());
            }
        }

        [TestMethod]
        public void OneChangedCaptureIsNotSettled()
        {
            // The first changed capture is never accepted on its own: it needs a matching one after it.
            var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Mid), () => Frame(New), () => Frame(New) });
            using (var before = Frame(Old))
            {
                ScreenSettle.WaitForChange(before, screen.Capture, 2000, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsFalse(gaveUp);
                Assert.AreEqual(3, screen.Captures);
            }
        }

        [TestMethod]
        public void AutoRunClicksOnceAndLogsTheStep()
        {
            ScreenSettle.Reset();
            try
            {
                var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Old), () => Frame(New), () => Frame(New) });
                int clicks = 0;
                ScreenSettle.Run("next character", () => clicks++, 700, auto: true, delay: 1.0,
                    capture: screen.Capture, wait: screen.Wait, nowMs: () => screen.Now);
                Assert.AreEqual(1, clicks);
                Assert.AreEqual(3, screen.Captures, "one before the click, two after");
                StringAssert.Contains(ScreenSettle.Summary(), "next character 1x, average 80 ms, longest 80 ms, 0 gave up");
            }
            finally
            {
                ScreenSettle.Reset();
            }
            Assert.AreEqual("Screen settle: not used (fixed scan speed)", ScreenSettle.Summary());
        }

        // --- Clicking again when the game ignored a click ---

        // A screen that shows Old until clicks have reached the given count, then New.
        private sealed class ClickableScreen
        {
            internal int Clicks;
            private readonly int clicksNeeded;
            internal ClickableScreen(int clicksNeeded) { this.clicksNeeded = clicksNeeded; }
            internal void Click() => Clicks++;
            internal Bitmap Capture() => Frame(Clicks >= clicksNeeded ? New : Old);
        }

        [TestMethod]
        public void IgnoredClickIsRepeatedAfter500Ms()
        {
            var game = new ClickableScreen(clicksNeeded: 2);   // the first click is dropped
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            using (var before = Frame(Old))
            {
                game.Click();
                long took = ScreenSettle.WaitForChange(before, game.Capture, 2000, clock.Wait, () => clock.Now,
                    out bool gaveUp, game.Click, out int extra);
                Assert.IsFalse(gaveUp);
                Assert.AreEqual(1, extra);
                Assert.AreEqual(2, game.Clicks);
                Assert.IsTrue(took >= ScreenSettle.ClickAgainAfterMs && took < ScreenSettle.ClickAgainAfterMs + 3 * ScreenSettle.PollMs, took.ToString());
            }
        }

        [TestMethod]
        public void SlowChangeGetsNoExtraClick()
        {
            // The screen starts changing 400 ms after the click: before the 500 ms mark, so no second click.
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            int clicks = 1;
            Bitmap Capture() => Frame(clock.Now >= 400 ? New : Old);
            using (var before = Frame(Old))
            {
                ScreenSettle.WaitForChange(before, Capture, 2000, clock.Wait, () => clock.Now, out bool gaveUp, () => clicks++, out int extra);
                Assert.IsFalse(gaveUp);
                Assert.AreEqual(0, extra);
                Assert.AreEqual(1, clicks);
            }
        }

        [TestMethod]
        public void AtMostTwoExtraClicksThenGivesUp()
        {
            var game = new ClickableScreen(clicksNeeded: 99);   // never responds
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            using (var before = Frame(Old))
            {
                game.Click();
                long took = ScreenSettle.WaitForChange(before, game.Capture, 2000, clock.Wait, () => clock.Now,
                    out bool gaveUp, game.Click, out int extra);
                Assert.IsTrue(gaveUp);
                Assert.AreEqual(ScreenSettle.MaxExtraClicks, extra);
                Assert.AreEqual(3, game.Clicks);
                // two re-clicks 500 ms apart, then the step's limit from the last click
                Assert.IsTrue(took >= 2 * ScreenSettle.ClickAgainAfterMs + 2000, took.ToString());
            }
        }

        [TestMethod]
        public void FlickerBackToBeforeIsIgnoredUntilTheRealChange()
        {
            // flash, back to the old screen (holds still), then the real switch
            var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Mid), () => Frame(Old), () => Frame(Old), () => Frame(New), () => Frame(New) });
            using (var before = Frame(Old))
            {
                long took = ScreenSettle.WaitForChange(before, screen.Capture, 2000, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsFalse(gaveUp);
                Assert.AreEqual(5, screen.Captures, "settled on New, not on the old screen after the flash");
                Assert.AreEqual(5 * ScreenSettle.PollMs, took);
            }
        }

        [TestMethod]
        public void FlickerThatStaysBackGivesUp()
        {
            // Only a flash: the screen never really switches, so the step gives up (and the constellation check then fails).
            var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Mid) }, () => Frame(Old));
            using (var before = Frame(Old))
            {
                long took = ScreenSettle.WaitForChange(before, screen.Capture, 2000, screen.Wait, () => screen.Now, out bool gaveUp);
                Assert.IsTrue(gaveUp);
                Assert.IsTrue(took >= 2000 && took < 2000 + ScreenSettle.PollMs, took.ToString());
            }
        }

        [TestMethod]
        public void NoExtraClickAfterAFlicker()
        {
            // A flash means the click is on its way; a second click could land on the new screen.
            var screen = new FakeScreen(new Func<Bitmap>[] { () => Frame(Mid) }, () => Frame(Old));
            int clicks = 1;
            using (var before = Frame(Old))
            {
                ScreenSettle.WaitForChange(before, screen.Capture, 2000, screen.Wait, () => screen.Now, out bool gaveUp, () => clicks++, out int extra);
                Assert.IsTrue(gaveUp);
                Assert.AreEqual(0, extra);
                Assert.AreEqual(1, clicks);
            }
        }

        // --- Opening Constellation: wait for the X close button to go ---

        // A game screen for ClickUntil: "opened" after the given number of clicks, from the given time on.
        private sealed class OpeningScreen
        {
            internal int Clicks;
            internal long OpenAtMs = long.MaxValue;   // set when the needed click arrives
            private readonly int clicksNeeded, delayMs;
            private readonly FakeScreen clock;
            internal OpeningScreen(FakeScreen clock, int clicksNeeded, int delayMs = 0) { this.clock = clock; this.clicksNeeded = clicksNeeded; this.delayMs = delayMs; }
            internal void Click() { if (++Clicks == clicksNeeded) OpenAtMs = clock.Now + delayMs; }
            internal bool Opened() => clock.Now >= OpenAtMs;
        }

        [TestMethod]
        public void OpensOnTheFirstClick()
        {
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            var game = new OpeningScreen(clock, clicksNeeded: 1, delayMs: 200);
            Assert.IsTrue(ScreenSettle.ClickUntil("open constellation", game.Click, game.Opened, 2000, clock.Wait, () => clock.Now));
            Assert.AreEqual(1, game.Clicks);
            Assert.IsTrue(clock.Now >= 240 && clock.Now <= 280, clock.Now.ToString());
        }

        [TestMethod]
        public void XStillThereAfter500MsGetsAnotherClick()
        {
            // Ganyu, Chiori, Navia: the first click 0.5-0.7 s after switching character was ignored.
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            var game = new OpeningScreen(clock, clicksNeeded: 2, delayMs: 200);
            Assert.IsTrue(ScreenSettle.ClickUntil("open constellation", game.Click, game.Opened, 2000, clock.Wait, () => clock.Now));
            Assert.AreEqual(2, game.Clicks);
        }

        [TestMethod]
        public void XGoneForOneCheckOnlyIsNotOpened()
        {
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            int checks = 0;
            bool FlashOnce() => ++checks == 3;   // one capture without the X, then it is back for good
            int clicks = 0;
            Assert.IsFalse(ScreenSettle.ClickUntil("open constellation", () => clicks++, FlashOnce, 2000, clock.Wait, () => clock.Now));
            Assert.AreEqual(1, clicks, "after a flash no extra click: the screen may be switching");
        }

        [TestMethod]
        public void XNeverGoesIsSkippedAfterTheLimitAndTheLastLook()
        {
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            var game = new OpeningScreen(clock, clicksNeeded: 99);
            Assert.IsFalse(ScreenSettle.ClickUntil("open constellation", game.Click, game.Opened, 2000, clock.Wait, () => clock.Now));
            Assert.AreEqual(1 + ScreenSettle.MaxExtraClicks, game.Clicks);
            // two extra clicks 500 ms apart, then the limit and the last look from the last click
            long expected = 2 * ScreenSettle.ClickAgainAfterMs + 2000 + ScreenSettle.LastLookMs;
            Assert.IsTrue(clock.Now >= expected && clock.Now < expected + 2 * ScreenSettle.PollMs, clock.Now.ToString());
        }

        [TestMethod]
        public void XGoingDuringTheLastLookCountsAsOpened()
        {
            // Ineffa: the screen opened after the step's limit. No more clicks are sent in the last look.
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            var game = new OpeningScreen(clock, clicksNeeded: 3, delayMs: 2600);
            Assert.IsTrue(ScreenSettle.ClickUntil("open constellation", game.Click, game.Opened, 2000, clock.Wait, () => clock.Now));
            Assert.AreEqual(3, game.Clicks);
            Assert.IsTrue(clock.Now >= 1000 + 2600, clock.Now.ToString());
        }
        [TestMethod]
        public void GaveUpLogSaysWhichCase()
        {
            Assert.AreEqual("Screen settle: open constellation gave up after 3092 ms (no change seen)",
                ScreenSettle.GaveUpText("open constellation", 3092, sawChange: false));
            Assert.AreEqual("Screen settle: close constellation gave up after 527 ms (it changed but never held still)",
                ScreenSettle.GaveUpText("close constellation", 527, sawChange: true));
        }

        [TestMethod]
        public void NextCharacterAndEscAreNeverRepeated()
        {
            // Run without clickAgain (as for "next character" and the closes): one press, even if nothing changes.
            var game = new ClickableScreen(clicksNeeded: 99);
            var clock = new FakeScreen(new Func<Bitmap>[0]);
            ScreenSettle.Reset();
            try
            {
                ScreenSettle.Run("next character", game.Click, 700, true, 1.0, game.Capture, clock.Wait, () => clock.Now);
                Assert.AreEqual(1, game.Clicks);
                StringAssert.Contains(ScreenSettle.Summary(), "1 gave up, 0 extra clicks");
            }
            finally
            {
                ScreenSettle.Reset();
            }
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "InventoryKamera", "scraping", "CharacterScraper.cs")) +
                         File.ReadAllText(Path.Combine(RepoRoot(), "InventoryKamera", "game", "Navigation.cs"));
            foreach (var line in source.Split('\n').Where(l => l.Contains("ScreenSettle.Run(\"")))
                if (line.Contains("\"next character\"") || line.Contains("\"close "))
                    Assert.IsFalse(line.Contains("clickAgain"), line.Trim());
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InventoryKamera.sln"))) dir = dir.Parent;
            if (dir == null) Assert.Inconclusive("repository not found from the test folder");
            return dir.FullName;
        }

        [TestMethod]
        public void ConstellationTabIsSelectedOnlyOnTheJustOpenedConstellationScreen()
        {
            bool Selected(string file)
            {
                using (var screen = new Bitmap(Image(file)))
                using (var crop = screen.Clone(ScreenSettle.AreaRect(ScreenSettle.Area.CharacterMenu, screen.Size), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    return ScreenSettle.ConstellationTabSelected(crop);
            }
            // The state the scan checks: Constellation clicked, no node yet (name bar, portraits and X still there).
            Assert.IsTrue(Selected("character-constellation-opened-1920x1080.png"), "Vesna, Constellation just opened");
            Assert.IsFalse(Selected(Attributes), "Kazuha, Attributes");
            Assert.IsFalse(Selected("character-attributes-ganyu-1920x1080.png"), "Ganyu, Attributes (snowflakes)");
            Assert.IsFalse(Selected("character-details-overlay-ganyu-1920x1080.png"), "Ganyu, Details overlay (white everywhere)");
            Assert.IsFalse(Selected(Talents), "Vesna, Talents");
            Assert.IsFalse(Selected(TalentDetail), "Vesna, Talent detail");
        }

        [TestMethod]
        public void CharacterMenuAreaAt1920x1080()
        {
            Assert.AreEqual(Rectangle.FromLTRB(260, 120, 430, 470),
                ScreenSettle.AreaRect(ScreenSettle.Area.CharacterMenu, new Size(1920, 1080)));
        }

        // --- Real screenshots (account data in IK_TEST_IMAGES, not in the repo) ---

        private static string Image(string name)
        {
            var folder = Environment.GetEnvironmentVariable("IK_TEST_IMAGES");
            if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive("IK_TEST_IMAGES is not set; real screenshots skipped.");
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) Assert.Inconclusive($"{path} is missing.");
            return path;
        }

        // True when the area counts as a screen change between the two screenshots.
        private static bool Changes(string from, string to, ScreenSettle.Area area)
        {
            using (var a = new Bitmap(Image(from)))
            using (var b = new Bitmap(Image(to)))
            {
                var rect = ScreenSettle.AreaRect(area, a.Size);
                using (var ca = a.Clone(rect, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                using (var cb = b.Clone(rect, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    return !CardSettle.Matches(ca, cb, new[] { new Rectangle(Point.Empty, ca.Size) }, ScreenSettle.ChangedFraction);
            }
        }

        private const string Attributes = "character-screen-kazuha-1920x1080.png";   // Kazuha, Attributes
        private const string Talents = "character-talents-1920x1080.png";           // Vesna, Talents list
        private const string TalentDetail = "character-talent-detail-1920x1080.png"; // Vesna, Normal Attack open
        private const string Constellation = "character-constellation-detail-1920x1080.png"; // Vesna, C1 open

        [TestMethod]
        public void EachStepsAreaChangesWithItsScreen()
        {
            Assert.IsTrue(Changes(Talents, Attributes, ScreenSettle.Area.Name), "next character: Vesna to Kazuha");
            Assert.IsTrue(Changes(Talents, Attributes, ScreenSettle.Area.Attributes), "open attributes");
            Assert.IsTrue(Changes(Attributes, Constellation, ScreenSettle.Area.ConstellationList), "constellation list");
            Assert.IsTrue(Changes(Constellation, Talents, ScreenSettle.Area.ConstellationDetail), "close constellation");
            Assert.IsTrue(Changes(Constellation, Talents, ScreenSettle.Area.TalentList), "open talents");
            Assert.IsTrue(Changes(Talents, TalentDetail, ScreenSettle.Area.TalentDetail), "open a talent");
            Assert.IsTrue(Changes(TalentDetail, Talents, ScreenSettle.Area.TalentDetail), "close talents");
        }

        [TestMethod]
        public void AreasMatchTheScanAreasAt1920x1080()
        {
            var size = new Size(1920, 1080);
            Assert.AreEqual(Rectangle.FromLTRB(127, 15, 532, 82), ScreenSettle.AreaRect(ScreenSettle.Area.Name, size), "same as the name read");
            var detail = ScreenSettle.AreaRect(ScreenSettle.Area.ConstellationDetail, size);
            // The "Activate" check (70..100 x 665..695 on the 1280x720 scale) lies inside the watched panel.
            Assert.IsTrue(detail.Contains(Rectangle.FromLTRB(105, 997, 150, 1042)), detail.ToString());
        }
    }
}
