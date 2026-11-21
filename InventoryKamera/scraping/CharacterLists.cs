using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    /// <summary>
    /// Reads a character's constellation and talent levels from the lists on the right of the
    /// Constellation and Talents screens, instead of clicking each node and talent.
    /// Measured on 1920x1080 screenshots; on other aspect ratios nothing is read and the scan clicks
    /// as before. Anything unclear returns null, and the scan then clicks as before too.
    /// </summary>
    internal static class CharacterLists
    {
        internal enum NodeState { Unlocked, Locked, Unknown }

        // The six constellation nodes, 1920x1080, relative to NodeListArea (1460,230).
        private static readonly Point[] NodeCenters =
            { new Point(60, 47), new Point(114, 158), new Point(150, 268), new Point(150, 379), new Point(114, 490), new Point(42, 601) };

        // Unlocked nodes have a bright ring in the element's colour (cyan, red, gold, green...): 53-71%
        // of it has a channel above 170 in every element measured. Locked ones a dark circle (0-31%)
        // with a white padlock (62-68% of the centre white). Anything else is still loading.
        internal const double UnlockedRing = 0.45, LockedRing = 0.35, LockedPadlock = 0.55;

        internal static bool Supported(Size window) => window.Width * 9 == window.Height * 16;

        internal static Rectangle NodeListArea(Size window) => Scale(Rectangle.FromLTRB(1460, 230, 1660, 880), window);

        // The talent list: one entry per 91 px from y 168 (icons), text left of the icons.
        internal static Rectangle TalentListArea(Size window) => Scale(Rectangle.FromLTRB(1380, 120, 1705, 490), window);

        private static Rectangle Scale(Rectangle r, Size window)
        {
            double sx = window.Width / 1920.0, sy = window.Height / 1080.0;
            return Rectangle.FromLTRB((int)(r.Left * sx), (int)(r.Top * sy), (int)(r.Right * sx), (int)(r.Bottom * sy));
        }

        /// <summary>The state of each of the six nodes in a capture of <see cref="NodeListArea"/>.</summary>
        internal static NodeState[] ReadNodes(Bitmap list)
        {
            double s = list.Height / 650.0;   // NodeListArea is 650 px high at 1080
            var states = new NodeState[6];
            var data = list.LockBits(new Rectangle(0, 0, list.Width, list.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var px = new byte[data.Stride * list.Height];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                for (int n = 0; n < 6; n++)
                {
                    int cx = (int)(NodeCenters[n].X * list.Width / 200.0), cy = (int)(NodeCenters[n].Y * s);
                    int ring = 0, ringAll = 0, white = 0, centreAll = 0;
                    int outer = (int)(38 * s), inner = (int)(28 * s), hw = (int)(12 * s), hh = (int)(14 * s);
                    for (int dy = -outer; dy <= outer; dy++)
                        for (int dx = -outer; dx <= outer; dx++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x < 0 || y < 0 || x >= list.Width || y >= list.Height) continue;
                            int i = y * data.Stride + x * 3;   // B, G, R
                            double r = Math.Sqrt(dx * dx + dy * dy);
                            if (r >= inner && r <= outer)
                            {
                                ringAll++;
                                if (Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) > 170) ring++;   // bright in any colour
                            }
                            if (Math.Abs(dx) <= hw && Math.Abs(dy) <= hh)
                            {
                                centreAll++;
                                if (px[i] > 200 && px[i + 1] > 200 && px[i + 2] > 200) white++;
                            }
                        }
                    double ringShare = ringAll == 0 ? 0 : (double)ring / ringAll;
                    double whiteShare = centreAll == 0 ? 0 : (double)white / centreAll;
                    states[n] = ringShare >= UnlockedRing ? NodeState.Unlocked
                              : ringShare < LockedRing && whiteShare >= LockedPadlock ? NodeState.Locked
                              : NodeState.Unknown;
                }
                return states;
            }
            finally
            {
                list.UnlockBits(data);
            }
        }

        /// <summary>
        /// The constellation level: the unlocked nodes from the top. Null when a node is unclear or an
        /// unlocked node follows a locked one (the scan then clicks the nodes).
        /// </summary>
        internal static int? Constellation(NodeState[] nodes)
        {
            int unlocked = 0;
            bool lockedSeen = false;
            foreach (var n in nodes)
            {
                if (n == NodeState.Unknown) return null;
                if (n == NodeState.Locked) lockedSeen = true;
                else if (lockedSeen) return null;
                else unlocked++;
            }
            return unlocked;
        }

        /// <summary>The text block of talent entry <paramref name="index"/> (0 = Normal Attack) in a capture of TalentListArea.</summary>
        internal static Rectangle TalentEntry(int index, Size list)
        {
            double s = list.Height / 370.0;   // TalentListArea is 370 px high at 1080
            int centre = (int)((168 - 120 + 91 * index) * s);
            int half = (int)(42 * s);
            return Rectangle.Intersect(new Rectangle(0, centre - half, list.Width, 2 * half), new Rectangle(Point.Empty, list));
        }

        // "Lv. 6", "LV.13", "ly. 4". A digit-like letter right after the number ("Lv. 1O") makes it
        // unclear rather than 1.
        private static readonly Regex LevelText = new Regex(@"l\s*[vy]\s*[.,:]?\s*(\d{1,2})(?![\dOoIlSB|])", RegexOptions.IgnoreCase);

        /// <summary>"Lv. N" from an OCR reading of a talent entry, or null.</summary>
        internal static int? ParseLevel(string text)
        {
            var matches = LevelText.Matches(text ?? "");
            if (matches.Count == 0) return null;
            int level = int.Parse(matches[matches.Count - 1].Groups[1].Value);
            return level >= 1 && level <= 15 ? level : (int?)null;
        }

        /// <summary>
        /// A level only when at least two readings read a number and every one that did reads the
        /// same; null as soon as two disagree. One reading alone misread Navia's 5 as 4 and Yoimiya's
        /// 10 as 1, and the first two that agreed both read Rosaria's boosted 11 as 1 (live scans).
        /// </summary>
        internal static int? Agreed(IEnumerable<int?> readings)
        {
            int? seen = null;
            int count = 0;
            foreach (var r in readings)
            {
                if (r == null) continue;
                if (seen != null && r != seen) return null;
                seen = r;
                count++;
            }
            return count >= 2 ? seen : null;
        }

        // Image settings, all tried (contrast, or black-and-white threshold). Each one misses some
        // entries over a bright background or a boosted (C3/C5) level, and two of them dropped the
        // second 1 of Rosaria's cyan "Lv. 11"; on every saved list the others then disagree.
        private static readonly (bool threshold, int value)[] Settings = { (false, 60), (false, 30), (true, 160), (true, 200) };

        /// <summary>Reads "Lv. N" from talent entry <paramref name="index"/>; null if it can't be read.</summary>
        internal static int? ReadTalentLevel(Bitmap list, int index)
        {
            var rect = TalentEntry(index, list.Size);
            if (rect.Height <= 0) return null;
            using (Bitmap entry = list.Clone(rect, PixelFormat.Format24bppRgb))
                return Agreed(Readings(entry));
        }

        private static IEnumerable<int?> Readings(Bitmap entry)
        {
            foreach (var (threshold, value) in Settings)
            {
                Bitmap n = GenshinProcesor.ConvertToGrayscale(entry);
                if (threshold) GenshinProcesor.SetThreshold(value, ref n); else GenshinProcesor.SetContrast(value, ref n);
                GenshinProcesor.SetInvert(ref n);
                n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
                string text = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock);
                n.Dispose();
                yield return ParseLevel(text);
            }
        }
    }
}
