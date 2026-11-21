using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// On the Auto scan speed, the character screens wait until they have finished changing instead
    /// of a fixed wait. Before a click (or Esc) a part of the screen is captured; after it, the same
    /// part is captured every <see cref="PollMs"/> ms until it has clearly changed and then shows
    /// the same thing twice in a row. It gives up at the step's Normal wait, so Auto is never slower
    /// than Normal. On the fixed speeds the old waits run unchanged.
    /// Only parts of the screen that hold still once loaded are watched (panels and text), never
    /// the character, whose idle animation keeps moving.
    /// </summary>
    internal static class ScreenSettle
    {
        // Longer than the card check's 15 ms: a slow fade changes too little in 15 ms to be seen.
        internal const int PollMs = 40;
        // A screen switch changes most of a watched area (87-100% in screenshots); drifting sparkles
        // change far less than this.
        internal const double ChangedFraction = 0.05;
        // The game ignores clicks for a moment after switching character (2026-11-21: Chiori's and
        // Navia's "open Constellation" clicks 0.6 s after the switch were dropped). If nothing has
        // changed at all this long after a click, the screen is still the same, so clicking again is safe.
        internal const int ClickAgainAfterMs = 500;
        internal const int MaxExtraClicks = 2;

        internal enum Area { Name, Attributes, ConstellationList, ConstellationDetail, TalentList, TalentDetail, CharacterMenu }

        /// <summary>The watched part of the window, measured on 1920x1080 screenshots.</summary>
        internal static Rectangle AreaRect(Area area, Size window)
        {
            Rectangle F(double l, double t, double r, double b) => Rectangle.FromLTRB(
                (int)(l * window.Width), (int)(t * window.Height), (int)(r * window.Width), (int)(b * window.Height));
            switch (area)
            {
                case Area.Name:                return F(85 / 1280.0, 10 / 720.0, 355 / 1280.0, 55 / 720.0); // "Anemo / Vesna"
                case Area.Attributes:          return F(1460 / 1920.0, 130 / 1080.0, 1800 / 1920.0, 460 / 1080.0); // name, level, stats
                case Area.ConstellationList:   return F(1470 / 1920.0, 230 / 1080.0, 1830 / 1920.0, 870 / 1080.0); // the six nodes' names
                case Area.ConstellationDetail: return F(30 / 1920.0, 220 / 1080.0, 490 / 1920.0, 1055 / 1080.0); // left panel to "Activate"
                case Area.TalentList:          return F(1390 / 1920.0, 130 / 1080.0, 1800 / 1920.0, 750 / 1080.0); // talent names and levels
                case Area.TalentDetail:        return F(0, 15 / 1080.0, 575 / 1920.0, 200 / 1080.0); // talent type, name, level
                case Area.CharacterMenu:       return F(260 / 1920.0, 120 / 1080.0, 430 / 1920.0, 470 / 1080.0); // ends of the menu words
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private sealed class StepStats { internal int Count, GaveUp, ExtraClicks; internal long TotalMs, LongestMs; }
        private static readonly List<string> order = new List<string>();
        private static readonly Dictionary<string, StepStats> stats = new Dictionary<string, StepStats>();

        internal static void Reset() { order.Clear(); stats.Clear(); }

        /// <summary>
        /// Runs <paramref name="act"/> (a click or key press) and waits: on Auto until the area has
        /// settled, otherwise <paramref name="baseWaitMs"/> times the speed factor, as before.
        /// With <paramref name="clickAgain"/>, Auto repeats <paramref name="act"/> when nothing has
        /// changed at all (only for clicks where a second one can't skip anything: tabs, nodes, talents).
        /// </summary>
        internal static void Run(string step, Area area, Action act, double baseWaitMs, bool clickAgain = false)
        {
            var rect = AreaRect(area, new Size(Navigation.GetWidth(), Navigation.GetHeight()));
            var clock = Stopwatch.StartNew();
            Run(step, act, baseWaitMs, Navigation.AutoItemWait, Navigation.GetDelay(),
                () => Navigation.CaptureRegion(rect), ms => Navigation.Wait(ms), () => clock.ElapsedMilliseconds, clickAgain);
        }

        /// <returns>False on Auto when the step gave up.</returns>
        internal static bool Run(string step, Action act, double baseWaitMs, bool auto, double delay,
            Func<Bitmap> capture, Action<int> wait, Func<long> nowMs, bool clickAgain = false)
        {
            int limit = (int)(baseWaitMs * delay);
            if (!auto)
            {
                act();
                wait(limit);
                return true;
            }

            long took;
            bool gaveUp, sawChange;
            int extraClicks;
            using (Bitmap before = capture())
            {
                act();
                took = WaitForChange(before, capture, limit, wait, nowMs, out gaveUp, clickAgain ? act : null, out extraClicks, out sawChange);
            }
            Record(step, took, gaveUp, extraClicks);
            if (extraClicks > 0) Logger.Debug("Screen settle: {0} needed {1} extra click(s)", step, extraClicks);
            if (gaveUp) Logger.Debug(GaveUpText(step, took, sawChange));
            return !gaveUp;
        }

        // For the debug log: tells a screen that never reacted from one that kept moving.
        internal static string GaveUpText(string step, long ms, bool sawChange) =>
            $"Screen settle: {step} gave up after {ms} ms (" +
            (sawChange ? "it changed but never held still" : "no change seen") + ")";

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal static long WaitForChange(Bitmap before, Func<Bitmap> capture, int limitMs, Action<int> wait, Func<long> nowMs, out bool gaveUp) =>
            WaitForChange(before, capture, limitMs, wait, nowMs, out gaveUp, null, out _, out _);

        internal static long WaitForChange(Bitmap before, Func<Bitmap> capture, int limitMs, Action<int> wait, Func<long> nowMs,
            out bool gaveUp, Action clickAgain, out int extraClicks) =>
            WaitForChange(before, capture, limitMs, wait, nowMs, out gaveUp, clickAgain, out extraClicks, out _);

        /// <summary>
        /// Waits until the captured area differs clearly from <paramref name="before"/> and then shows
        /// the same thing in two captures in a row, still different from before (a flicker that goes back
        /// doesn't count). Returns how long that took. If <paramref name="clickAgain"/>
        /// is given and nothing has changed <see cref="ClickAgainAfterMs"/> after a click, it is run again
        /// (at most <see cref="MaxExtraClicks"/> times); the time limit then counts from the last click.
        /// </summary>
        internal static long WaitForChange(Bitmap before, Func<Bitmap> capture, int limitMs, Action<int> wait, Func<long> nowMs,
            out bool gaveUp, Action clickAgain, out int extraClicks, out bool sawChange)
        {
            long start = nowMs(), lastClick = start;
            Bitmap last = null;
            sawChange = false;   // once anything changed, the click is on its way: never click again
            gaveUp = false;
            extraClicks = 0;
            try
            {
                while (true)
                {
                    if (nowMs() - lastClick >= limitMs) { gaveUp = true; break; }
                    if (!sawChange && clickAgain != null && extraClicks < MaxExtraClicks && nowMs() - lastClick >= ClickAgainAfterMs)
                    {
                        clickAgain();
                        extraClicks++;
                        lastClick = nowMs();
                    }
                    wait(PollMs);
                    Bitmap next = capture();
                    var whole = new[] { new Rectangle(Point.Empty, next.Size) };
                    if (last == null)
                    {
                        if (CardSettle.Matches(before, next, whole, ChangedFraction)) { next.Dispose(); continue; }
                        last = next;   // changed; now wait for it to hold still
                        sawChange = true;
                        continue;
                    }
                    bool stable = CardSettle.Matches(last, next, whole);
                    last.Dispose();
                    last = next;
                    if (!stable) continue;
                    // Settled. It only counts if it still differs from before the click: Tartaglia's name
                    // bar flickered and came back, and the scan went on before his constellations opened (2026-11-21).
                    if (CardSettle.Matches(before, next, whole, ChangedFraction)) { last.Dispose(); last = null; continue; }
                    break;
                }
                return nowMs() - start;
            }
            finally
            {
                last?.Dispose();
            }
        }

        // How much longer to look for a screen that opens late before deciding it didn't open.
        internal const int LastLookMs = 2000;
        // The highlighted entry of the character menu on the left is drawn larger, so its word reaches
        // further right. These patches (1920x1080, relative to Area.CharacterMenu at 260,120) cover the
        // end of each large word, where the small one has nothing:
        //   "Constellation" 370-425 x 340-390, "Attributes" 300-360 x 130-180, "Talents" 270-305 x 410-460.
        // White pixels measured: just-opened constellation screen 486 / 0 / 0; Attributes 0 / 566-588 / 0;
        // Talents 0 / 0 / 284; Details overlay 191 / 191 / 245 (white everywhere); talent detail 11 / 26 / 221.
        private static readonly Rectangle ConstellationEnd = Rectangle.FromLTRB(110, 220, 165, 270);
        private static readonly Rectangle AttributesEnd = Rectangle.FromLTRB(40, 10, 100, 60);
        private static readonly Rectangle TalentsEnd = Rectangle.FromLTRB(10, 290, 45, 340);
        internal const double SelectedWhite = 0.06, OtherWhite = 0.03;

        /// <summary>
        /// True when the character menu shows "Constellation" highlighted and neither "Attributes" nor
        /// "Talents": the constellation screen has opened. Just after opening it keeps the name bar, the
        /// portraits and the X button, so only the menu tells it apart (Vesna, 2026-11-21); comparing with
        /// "before" was fooled by Clorinde's loading panel and by snowflakes over Ganyu's name bar.
        /// </summary>
        internal static bool ConstellationTabSelected(Bitmap menu)
        {
            double sx = menu.Width / 170.0, sy = menu.Height / 350.0;
            Rectangle S(Rectangle r) => Rectangle.FromLTRB((int)(r.Left * sx), (int)(r.Top * sy), (int)(r.Right * sx), (int)(r.Bottom * sy));
            return WhiteShare(menu, S(ConstellationEnd)) >= SelectedWhite &&
                   WhiteShare(menu, S(AttributesEnd)) < OtherWhite &&
                   WhiteShare(menu, S(TalentsEnd)) < OtherWhite;
        }

        // Share of the rectangle's pixels that are white (every channel above 200).
        internal static double WhiteShare(Bitmap bm, Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, bm.Width, bm.Height));
            if (r.Width <= 0 || r.Height <= 0) return 0;
            var data = bm.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                var px = new byte[data.Stride * r.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                int white = 0;
                for (int y = 0; y < r.Height; y++)
                    for (int x = 0; x < r.Width; x++)
                    {
                        int i = y * data.Stride + x * 3;
                        if (px[i] > 200 && px[i + 1] > 200 && px[i + 2] > 200) white++;
                    }
                return (double)white / (r.Width * r.Height);
            }
            finally
            {
                bm.UnlockBits(data);
            }
        }

        /// <summary>
        /// Clicks, then waits until <paramref name="opened"/> is true in two checks in a row. While it is
        /// still false <see cref="ClickAgainAfterMs"/> after a click, and has never been true, it clicks
        /// again (at most <see cref="MaxExtraClicks"/> times). After <paramref name="limitMs"/> from the
        /// last click it keeps looking for another <see cref="LastLookMs"/> without clicking, so a screen
        /// that opens late is still used (Ineffa's opened after the step gave up, 2026-11-21).
        /// Returns false only if it never opened; the screen then still is the one before the click.
        /// </summary>
        internal static bool ClickUntil(string step, Action click, Func<bool> opened, int limitMs, Action<int> wait, Func<long> nowMs)
        {
            long start = nowMs(), lastClick = start;
            int extraClicks = 0, inARow = 0;
            bool seenOpen = false;
            click();
            bool ok;
            while (true)
            {
                long sinceClick = nowMs() - lastClick;
                if (sinceClick >= limitMs + LastLookMs) { ok = false; break; }
                if (!seenOpen && extraClicks < MaxExtraClicks && sinceClick >= ClickAgainAfterMs && sinceClick < limitMs)
                {
                    click();
                    extraClicks++;
                    lastClick = nowMs();
                }
                wait(PollMs);
                if (opened())
                {
                    seenOpen = true;
                    if (++inARow >= 2) { ok = true; break; }
                }
                else inARow = 0;
            }
            long took = nowMs() - start;
            bool late = ok && nowMs() - lastClick > limitMs;
            Record(step, took, !ok, extraClicks);
            if (extraClicks > 0) Logger.Debug("Screen settle: {0} needed {1} extra click(s)", step, extraClicks);
            if (late) Logger.Debug("Screen settle: {0} opened late, {1} ms after the click", step, took);
            if (!ok) Logger.Debug("Screen settle: {0} never opened ({1} ms)", step, took);
            return ok;
        }

        private static void Record(string step, long ms, bool gaveUp, int extraClicks)
        {
            if (!stats.TryGetValue(step, out var s)) { s = new StepStats(); stats[step] = s; order.Add(step); }
            s.Count++;
            s.ExtraClicks += extraClicks;
            s.TotalMs += ms;
            s.LongestMs = Math.Max(s.LongestMs, ms);
            if (gaveUp) s.GaveUp++;
        }

        internal static string Summary() =>
            order.Count == 0
                ? "Screen settle: not used (fixed scan speed)"
                : "Screen settle (characters, Auto): " + string.Join("; ", order.Select(step =>
                  {
                      var s = stats[step];
                      return $"{step} {s.Count}x, average {s.TotalMs / s.Count} ms, longest {s.LongestMs} ms, {s.GaveUp} gave up, {s.ExtraClicks} extra clicks";
                  }));
    }
}
