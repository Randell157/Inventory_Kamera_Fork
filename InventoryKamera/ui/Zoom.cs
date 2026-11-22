using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

		// A window not shown yet, at 100 %: its size in screen pixels (its designed size times the screen's DPI scaling).
		internal static Size BaseSize(Form window)
		{
			float dpi;
			using (var screen = Graphics.FromHwnd(IntPtr.Zero)) dpi = screen.DpiX;
			return new Size((int)Math.Round(window.Width * dpi / 96f), (int)Math.Round(window.Height * dpi / 96f));
		}

		// A window already shown: what its size would be at 100 %.
		internal static Size NormalSize(Form window)
		{
			int now = Current(window);
			return new Size(window.Width * Normal / now, window.Height * Normal / now);
		}

		private sealed class Percent { internal int Value = Normal; }
		private static readonly ConditionalWeakTable<Form, Percent> percents = new ConditionalWeakTable<Form, Percent>();

		// The zoom a window is at now.
		internal static int Current(Form window) => percents.TryGetValue(window, out var p) ? p.Value : Normal;

		// Scales a new window that is still at 100 % (before it's shown, so Windows' own DPI scaling comes after).
		internal static void Apply(Form window, int percent)
		{
			if (percent != Normal) Scale(window, Take(window, Normal), percent);
			percents.GetOrCreateValue(window).Value = percent;
		}

		// Changes the zoom of a window in place (shown or hidden). Its sizes and fonts are remembered the first
		// time and every later zoom is worked out from those, so going back and forth never drifts.
		internal static void Set(Form window, int percent)
		{
			int now = Current(window);
			if (percent == now) return;
			if (!snapshots.TryGetValue(window, out var snapshot))
			{
				snapshot = Take(window, now);
				snapshots.Add(window, snapshot);
			}

			bool redraw = window.IsHandleCreated && window.Visible;
			if (redraw) SendMessage(window.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);   // no half-drawn frames
			try { Scale(window, snapshot, percent); }
			finally
			{
				if (redraw)
				{
					SendMessage(window.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
					window.Refresh();
				}
			}
			percents.GetOrCreateValue(window).Value = percent;
		}

		// Every control's size, position and own font at one zoom.
		private sealed class Snapshot
		{
			internal int Percent;
			internal Size ClientSize;
			internal Font FormFont;
			internal readonly List<(Control Control, Rectangle Bounds, Padding Padding, Padding Margin)> Bounds = new List<(Control, Rectangle, Padding, Padding)>();
			internal readonly List<(ToolStripItem Item, Padding Padding, Padding Margin)> Items = new List<(ToolStripItem, Padding, Padding)>();
			internal readonly List<(Control Control, Font Font)> OwnFonts = new List<(Control, Font)>();
			internal readonly List<(ToolStrip Strip, Size Size)> Images = new List<(ToolStrip, Size)>();
		}
		private static readonly ConditionalWeakTable<Form, Snapshot> snapshots = new ConditionalWeakTable<Form, Snapshot>();

		private static Snapshot Take(Form window, int percent)
		{
			var snapshot = new Snapshot { Percent = percent, ClientSize = window.ClientSize, FormFont = window.Font };
			void Collect(Control parent)
			{
				foreach (Control child in parent.Controls)
				{
					snapshot.Bounds.Add((child, child.Bounds, child.Padding, child.Margin));   // Scale() scales the spacing too
					// Fonts set on a control itself; the rest inherit them. Menus and tool strips don't take
					// their parent's font, so they're always scaled.
					if (child is ToolStrip || !child.Font.Equals(parent.Font)) snapshot.OwnFonts.Add((child, child.Font));
					if (child is ToolStrip strip)
					{
						snapshot.Images.Add((strip, strip.ImageScalingSize));
						foreach (ToolStripItem item in strip.Items) snapshot.Items.Add((item, item.Padding, item.Margin));
					}
					Collect(child);
				}
			}
			Collect(window);
			return snapshot;
		}

		// Puts the window back as it was in the snapshot and scales it from there to percent.
		private static void Scale(Form window, Snapshot snapshot, int percent)
		{
			float factor = percent / (float)snapshot.Percent;
			window.SuspendLayout();
			window.Font = Scaled(snapshot.FormFont, factor);
			foreach (var (control, font) in snapshot.OwnFonts) control.Font = Scaled(font, factor);
			foreach (var (strip, size) in snapshot.Images)
				strip.ImageScalingSize = new Size((int)Math.Round(size.Width * factor), (int)Math.Round(size.Height * factor));
			foreach (var (control, bounds, padding, margin) in snapshot.Bounds)
			{
				control.Bounds = bounds;
				control.Padding = padding;
				control.Margin = margin;
			}
			foreach (var (item, padding, margin) in snapshot.Items)
			{
				item.Padding = padding;
				item.Margin = margin;
			}
			window.ClientSize = snapshot.ClientSize;
			if (factor != 1) window.Scale(new SizeF(factor, factor));
			window.ResumeLayout(true);
			// A menu measures its items again only when told to; otherwise it keeps the height of the last zoom.
			foreach (var (strip, _) in snapshot.Images) strip.PerformLayout();
			window.PerformLayout();
		}

		private static Font Scaled(Font font, float factor) =>
			factor == 1 ? font : new Font(font.FontFamily, font.Size * factor, font.Style, font.Unit);

		private const int WM_SETREDRAW = 0x000B;

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
	}

	// Where a window goes when it replaces another or changes size.
	internal static class Placement
	{
		// Keeps the top-left corner where it was (the menus stay under the mouse), moved only as far as needed
		// to keep the window on the screen; never above or left of the work area.
		internal static Point KeepTopLeft(Point topLeft, Size size, Rectangle area) =>
			new Point(
				Math.Max(area.Left, Math.Min(topLeft.X, area.Right - size.Width)),
				Math.Max(area.Top, Math.Min(topLeft.Y, area.Bottom - size.Height)));

		// The first window of a launch: centered on the screen.
		internal static Point Centered(Size size, Rectangle area) =>
			new Point(area.Left + Math.Max(0, (area.Width - size.Width) / 2), area.Top + Math.Max(0, (area.Height - size.Height) / 2));
	}
}
