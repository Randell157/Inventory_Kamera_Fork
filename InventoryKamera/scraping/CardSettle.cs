using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace InventoryKamera
{
    /// <summary>
    /// Waits until a weapon or artifact card has finished changing before it is read.
    /// After clicking an item, the game fades the old card out and the new one in. A single capture
    /// after a fixed wait sometimes caught the new card with the old padlock, or the old card entirely
    /// (the same artifact exported twice). So the card is captured again every <see cref="PollMs"/> ms
    /// until two captures in a row match.
    /// A settled card that matches the previous item's card is taken at once if the card was seen
    /// changing after the click (two identical weapons in a row: the game redraws the card, so it
    /// changes and settles again). Only a card that never changed at all, which may still be the old
    /// one, waits up to <see cref="SameAsPreviousLimitMs"/> ms.
    /// On the Auto scan speed there is no fixed wait before this, so it also does that job.
    /// </summary>
    internal sealed class CardSettle
    {
        internal const int PollMs = 15;
        // The slowest card switch seen in two full scans (2,489 cards each) was 259 ms after the click.
        internal const int SameAsPreviousLimitMs = 300;
        internal const int GiveUpMs = 600;       // after a fixed speed's wait
        internal const int AutoGiveUpMs = 800;   // on Auto, counted from the click

        // A pixel counts as changed when a color channel moved by more than this.
        private const int Tolerance = 24;

        private Bitmap previous;   // copy of the last card accepted

        // For the end-of-scan summary
        internal int Cards { get; private set; }
        internal long TotalExtraMs { get; private set; }
        internal long LongestExtraMs { get; private set; }
        internal long TotalSinceClickMs { get; private set; }     // the fixed wait before plus the extra wait
        internal int DuplicatesTakenAtOnce { get; private set; }  // matched the previous card, but was seen changing
        internal int NeverChangedWaited { get; private set; }     // matched the previous card and never changed: waited
        internal int StillSameAfterLimit { get; private set; }    // ...and still matched after the limit
        internal int TimedOut { get; private set; }
        internal int LateSwitches { get; private set; }           // first showed the previous card, then changed
        internal long LongestLateSwitchMs { get; private set; }   // ...the slowest of those, from the start of the check

        /// <summary>
        /// Captures until the card is settled and returns it (the caller owns it).
        /// <paramref name="capture"/> returns a new card bitmap each call; <paramref name="regions"/>
        /// gives the parts of a card to compare; <paramref name="wait"/> and <paramref name="nowMs"/>
        /// are Thread.Sleep and a stopwatch in the scan, and fakes in tests. <paramref name="waitedBeforeMs"/>
        /// is the fixed wait that ran after the click (0 on Auto), used only for the summary.
        /// </summary>
        internal Bitmap Settle(Func<Bitmap> capture, Func<Size, IList<Rectangle>> regions, Action<int> wait, Func<long> nowMs,
            int giveUpMs = GiveUpMs, int waitedBeforeMs = 0)
        {
            long start = nowMs();
            Bitmap last = capture();
            bool sawChange = previous != null && !Matches(last, previous, regions(last.Size));
            bool startedAsPrevious = previous != null && !sawChange;
            long lateSwitchMs = -1;
            bool waitedForCopy = false, copy = false, timedOut = false;

            while (true)
            {
                if (nowMs() - start >= giveUpMs) { timedOut = true; break; }
                wait(PollMs);
                Bitmap next = capture();
                bool stable = Matches(last, next, regions(next.Size));
                last.Dispose();
                last = next;
                if (!stable)
                {
                    if (startedAsPrevious && lateSwitchMs < 0) lateSwitchMs = nowMs() - start;
                    sawChange = true;
                    continue;
                }

                copy = previous != null && Matches(next, previous, regions(next.Size));
                if (copy && !sawChange && nowMs() - start < SameAsPreviousLimitMs) { waitedForCopy = true; continue; }
                break;
            }

            long extra = nowMs() - start;
            Cards++;
            TotalExtraMs += extra;
            TotalSinceClickMs += extra + waitedBeforeMs;
            LongestExtraMs = Math.Max(LongestExtraMs, extra);
            if (copy && sawChange && !timedOut) DuplicatesTakenAtOnce++;
            if (waitedForCopy) NeverChangedWaited++;
            if (copy && !sawChange && !timedOut) StillSameAfterLimit++;
            if (timedOut) TimedOut++;
            if (lateSwitchMs >= 0) { LateSwitches++; LongestLateSwitchMs = Math.Max(LongestLateSwitchMs, lateSwitchMs); }

            previous?.Dispose();
            previous = (Bitmap)last.Clone();
            return last;
        }

        internal string Summary(string items) =>
            Cards == 0
                ? $"Card settle ({items}): no cards"
                : $"Card settle ({items}): {Cards} cards, settled on average {TotalSinceClickMs / Cards} ms after the click " +
                  $"(extra wait average {TotalExtraMs / Cards} ms, longest {LongestExtraMs} ms); " +
                  $"{DuplicatesTakenAtOnce} matched the previous card after changing (taken at once); " +
                  $"{NeverChangedWaited} never changed and waited ({StillSameAfterLimit} still matched after {SameAsPreviousLimitMs} ms); " +
                  $"{LateSwitches} showed the previous card first (slowest switched after {LongestLateSwitchMs} ms); " +
                  $"{TimedOut} hit the time limit";

        /// <summary>
        /// The parts of a card that are read: the name, the main stat or gear slot, the block with the
        /// level, lock and stats, the equipped character, and the padlock itself (also on its own,
        /// so a padlock that is still fading counts even though it is small).
        /// </summary>
        internal static IList<Rectangle> CardRegions(Size card, bool isNormal, bool isWeapon)
        {
            int W(double f) => (int)(card.Width * f);
            int H(double f) => (int)(card.Height * f);
            var list = new List<Rectangle>
            {
                new Rectangle(0, 0, card.Width, H(isNormal ? 0.07 : 0.06)),                    // name
                new Rectangle(W(0.04), H(isNormal ? 0.07 : 0.06), W(0.46), H(0.13)),             // gear slot, main stat
                new Rectangle(W(0.04), H(isNormal ? 0.30 : 0.27), W(0.92), H(0.36)),             // level, lock, stats
                new Rectangle(W(0.15), H(isNormal ? 0.938 : 0.943), W(0.85), H(isNormal ? 0.062 : 0.057)), // equipped
                InventoryScraper.LockRegion(card, isNormal, isSanctified: false, isWeapon: isWeapon),
            };
            if (!isWeapon) list.Add(InventoryScraper.LockRegion(card, isNormal, isSanctified: true));
            return list;
        }

        /// <summary>
        /// True when two captures show the same thing in every region: in each, at most
        /// <paramref name="allowedFraction"/> of the pixels (0.5% by default, and at least 3) may differ
        /// by more than a small color tolerance.
        /// </summary>
        internal static bool Matches(Bitmap a, Bitmap b, IList<Rectangle> regions, double allowedFraction = 0.005)
        {
            if (ReferenceEquals(a, b)) return true;   // a bitmap can't be locked twice at once
            if (a.Size != b.Size) return false;
            var whole = new Rectangle(Point.Empty, a.Size);
            var da = a.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var db = b.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = da.Stride;
                byte[] pa = new byte[stride * a.Height], pb = new byte[db.Stride * b.Height];
                Marshal.Copy(da.Scan0, pa, 0, pa.Length);
                Marshal.Copy(db.Scan0, pb, 0, pb.Length);

                foreach (Rectangle r0 in regions)
                {
                    Rectangle r = Rectangle.Intersect(r0, whole);
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    int allowed = Math.Max(3, (int)(r.Width * r.Height * allowedFraction));
                    int changed = 0;
                    for (int y = r.Top; y < r.Bottom; y++)
                    {
                        int row = y * stride;
                        for (int x = r.Left; x < r.Right; x++)
                        {
                            int i = row + x * 3;
                            if (Math.Abs(pa[i] - pb[i]) > Tolerance || Math.Abs(pa[i + 1] - pb[i + 1]) > Tolerance ||
                                Math.Abs(pa[i + 2] - pb[i + 2]) > Tolerance)
                            {
                                if (++changed > allowed) return false;
                            }
                        }
                    }
                }
                return true;
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }
    }
}
