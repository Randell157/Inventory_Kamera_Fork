using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera
{
	// 1.7: View → Zoom makes the whole window bigger (text and layout), for every look. One saved
	// setting ("Zoom", in percent). A step that wouldn't fit on the screen is greyed out.
	internal static class Zoom
	{
		internal const int Normal = 100;
		internal static readonly int[] Steps = { 100, 125, 150, 175, 200, 250, 300 };

		internal static int Valid(int saved) => Steps.Contains(saved) ? saved : Normal;

		internal static string MenuText(int percent) => percent + "%";

		// A window of baseSize at 100 % still fits in the screen's work area (without the taskbar) at percent.
		internal static bool Fits(Size baseSize, int percent, Rectangle workArea) =>
			percent == Normal ||
			(baseSize.Width * percent / 100 <= workArea.Width && baseSize.Height * percent / 100 <= workArea.Height);

		// The saved zoom, or the biggest smaller step that fits (on a smaller screen, say). The setting is kept.
		internal static int Usable(int saved, Size baseSize, Rectangle workArea) =>
			Steps.Where(step => step <= Valid(saved) && Fits(baseSize, step, workArea)).DefaultIfEmpty(Normal).Max();

		// The window's size at 100 % now, in screen pixels: its designed size times the screen's DPI scaling.
		internal static Size BaseSize(Form window)
		{
			float dpi;
			using (var screen = Graphics.FromHwnd(IntPtr.Zero)) dpi = screen.DpiX;
			return new Size((int)Math.Round(window.Width * dpi / 96f), (int)Math.Round(window.Height * dpi / 96f));
		}

		// Scales a window that is still at 100 % (call once, before it's shown): every font and every
		// control's position and size. Fonts are scaled where set; the rest inherit them.
		internal static void Apply(Form window, int percent)
		{
			if (percent == Normal) return;
			float factor = percent / 100f;

			// Fonts set on a control itself (not inherited from its parent), read before any change.
			var ownFonts = new List<(Control Control, Font Font)>();
			void Collect(Control parent)
			{
				foreach (Control child in parent.Controls)
				{
					// Menus and tool strips don't take their parent's font, so they're always scaled.
					if (child is ToolStrip || !child.Font.Equals(parent.Font)) ownFonts.Add((child, child.Font));
					Collect(child);
				}
			}
			Collect(window);

			window.SuspendLayout();
			window.Font = Scaled(window.Font, factor);
			foreach (var (control, font) in ownFonts) control.Font = Scaled(font, factor);
			foreach (var strip in ownFonts.Select(o => o.Control).OfType<ToolStrip>())
				strip.ImageScalingSize = new Size((int)Math.Round(strip.ImageScalingSize.Width * factor), (int)Math.Round(strip.ImageScalingSize.Height * factor));
			window.Scale(new SizeF(factor, factor));
			window.ResumeLayout(true);
		}

		private static Font Scaled(Font font, float factor) => new Font(font.FontFamily, font.Size * factor, font.Style, font.Unit);
	}
}
