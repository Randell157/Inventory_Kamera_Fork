using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace InventoryKamera.Tests
{
	// Live 2026-11-22 (1.7.3 check, Classic at 175 %): the scan checkboxes' ticks were pixelated. Windows has
	// one small checkbox picture and stretches it; zoomed, ZoomCheckBox now draws the square and tick itself
	// in the colours of Windows' picture.
	[TestClass]
	public sealed class ZoomCheckBoxDrawingTests
	{
		private static readonly Color Behind = Color.FromArgb(236, 233, 233);   // Classic's grey

		[TestCleanup]
		public void ThemesBackAsTheRunHasThem() => ZoomCheckBox.Glyphs.ThemedForTests = false;

		private static ZoomCheckBox.Glyphs.Look LookOrSkip(CheckBoxState state)
		{
			ZoomCheckBox.Glyphs.ThemedForTests = true;   // as Program.Main's EnableVisualStyles
			var look = ZoomCheckBox.Glyphs.LookOf(state);
			if (look == null) Assert.Inconclusive("no Windows themes here");
			return look;
		}

		private static Bitmap Drawn(int percent, CheckBoxState state, bool windowsPicture = false)
		{
			int dpi = 96 * percent / 100;
			var size = ZoomCheckBox.Glyphs.SizeAt(dpi);
			var bitmap = new Bitmap(size.Width, size.Height);
			using (var g = Graphics.FromImage(bitmap))
			{
				g.Clear(Behind);
				var square = new Rectangle(Point.Empty, size);
				if (windowsPicture) ZoomCheckBox.Glyphs.DrawPicture(g, square, dpi, state);
				else ZoomCheckBox.Glyphs.Draw(g, square, dpi, state);
			}
			return bitmap;
		}

		private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

		private static int Colours(Bitmap b)
		{
			var seen = new HashSet<int>();
			for (int y = 0; y < b.Height; y++)
				for (int x = 0; x < b.Width; x++) seen.Add(b.GetPixel(x, y).ToArgb());
			return seen.Count;
		}

		[TestMethod]
		public void ZoomedTickIsSmoothNotAStretchedPicture()
		{
			LookOrSkip(CheckBoxState.CheckedNormal);
			foreach (int percent in new[] { 150, 175, 250, 300 })
				using (var stretched = Drawn(percent, CheckBoxState.CheckedNormal, windowsPicture: true))
				using (var smooth = Drawn(percent, CheckBoxState.CheckedNormal))
				{
					// A stretched picture only repeats the small picture's few colours; smooth edges blend.
					Assert.IsTrue(Colours(smooth) > Colours(stretched) + 10,
						$"{percent} %: {Colours(smooth)} shades drawn vs {Colours(stretched)} in Windows' stretched picture");
				}
		}

		[TestMethod]
		public void ZoomedSquareUsesWindowsColoursForEachState()
		{
			foreach (var state in new[] { CheckBoxState.CheckedNormal, CheckBoxState.CheckedHot, CheckBoxState.CheckedDisabled,
				CheckBoxState.UncheckedNormal, CheckBoxState.UncheckedHot, CheckBoxState.UncheckedDisabled, CheckBoxState.MixedNormal })
			{
				var look = LookOrSkip(state);
				using (var b = Drawn(175, state))
				{
					int mid = b.Width / 2;
					Assert.IsTrue(Distance(b.GetPixel(mid, Math.Max(2, b.Height / 6)), look.Fill) <= 12, $"{state}: fill {b.GetPixel(mid, b.Height / 6)} vs Windows' {look.Fill}");
					// The border's own colour shows on the top edge (the closer of the top two rows, in case of smoothing).
					var edge = new[] { b.GetPixel(mid, 0), b.GetPixel(mid, 1) }.OrderBy(c => Distance(c, look.Border)).First();
					Assert.IsTrue(Distance(edge, look.Border) <= 40, $"{state}: border {edge} vs Windows' {look.Border}");
					bool ticked = state <= CheckBoxState.CheckedDisabled && state >= CheckBoxState.CheckedNormal || state == CheckBoxState.MixedNormal;
					Assert.AreEqual(ticked, look.Mark.HasValue, $"{state}: a tick or dash only when ticked");
					if (ticked)
					{
						bool markShown = false;
						for (int y = 0; y < b.Height && !markShown; y++)
							for (int x = 0; x < b.Width && !markShown; x++)
								markShown = Distance(b.GetPixel(x, y), look.Mark.Value) <= 24;
						Assert.IsTrue(markShown, $"{state}: the mark in Windows' colour {look.Mark}");
					}
				}
			}
		}

		[TestMethod]
		public void SquareKeepsWindowsSizeAtEveryZoom()
		{
			LookOrSkip(CheckBoxState.CheckedNormal);
			foreach (int percent in Zoom.Steps)
				using (var b = Drawn(percent, CheckBoxState.CheckedNormal))
				{
					// The edge pixels are the square's, not the background: it fills the size Windows gives.
					int mid = b.Width / 2;
					Assert.AreNotEqual(Behind.ToArgb(), b.GetPixel(mid, 0).ToArgb(), $"{percent} %: top edge");
					Assert.AreNotEqual(Behind.ToArgb(), b.GetPixel(0, b.Height / 2).ToArgb(), $"{percent} %: left edge");
					Assert.AreNotEqual(Behind.ToArgb(), b.GetPixel(b.Width - 1, b.Height / 2).ToArgb(), $"{percent} %: right edge");
					Assert.AreNotEqual(Behind.ToArgb(), b.GetPixel(mid, b.Height - 1).ToArgb(), $"{percent} %: bottom edge");
				}
		}
	}
}
