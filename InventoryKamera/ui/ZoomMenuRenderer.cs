using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventoryKamera
{
	// 1.7.3: menu tick marks (View → Simple / Advanced / Classic, View → Zoom, Options …) grow with View → Zoom.
	// Windows' tick is a 16 px picture; Zoom now makes each drop-down's tick area bigger, and there this draws
	// the tick as lines that fill it, so it stays sharp. At 100 % it's Windows' own tick, unchanged.
	// Set for the whole app in Program.Main (ToolStripManager.Renderer), so every menu uses it.
	internal sealed class ZoomMenuRenderer : ToolStripProfessionalRenderer
	{
		protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
		{
			var box = e.ImageRectangle;
			int normal = (int)Math.Round(16 * e.Graphics.DpiX / 96f);
			if (box.Width <= normal + 1 || !(e.Item is ToolStripMenuItem))
			{
				base.OnRenderItemCheck(e);
				return;
			}
			// The blue checked square from the normal renderer (no picture), then the tick drawn into it.
			base.OnRenderItemCheck(new ToolStripItemImageRenderEventArgs(e.Graphics, e.Item, null, box));
			DrawTick(e.Graphics, box, e.Item.Enabled ? e.Item.ForeColor : SystemColors.GrayText);
		}

		// The tick is about 15 % smaller than the square's inside (live 2026-11-22: the first one looked too big).
		internal static void DrawTick(Graphics g, Rectangle box, Color color)
		{
			float x = box.X, y = box.Y, w = box.Width, h = box.Height;
			var points = new[]
			{
				new PointF(x + w * 0.28f, y + h * 0.52f),
				new PointF(x + w * 0.43f, y + h * 0.67f),
				new PointF(x + w * 0.72f, y + h * 0.35f),
			};
			var smoothing = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var pen = new Pen(color, Math.Max(1.5f, w / 10.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
				g.DrawLines(pen, points);
			g.SmoothingMode = smoothing;
		}

		// A drop-down menu lays out its tick column, paddings and sub-menu arrow from the screen's DPI, not from
		// ImageScalingSize: with only the tick area grown, the ticked square ran into the text (live 2026-11-22).
		// Windows' own step for a DPI change (ResetScaling, not public) is used with "DPI × zoom", so the whole
		// menu grows together and comes back exactly at 100 %. Without it (another .NET), menus keep their size.
		private static readonly System.Reflection.MethodInfo resetScaling = typeof(ToolStripDropDownMenu).GetMethod(
			"ResetScaling", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

		internal static bool CanScaleMenus => resetScaling != null;

		internal static void ScaleMenu(ToolStripDropDownMenu menu, int percent)
		{
			if (resetScaling == null) return;
			int dpi;
			using (var screen = Graphics.FromHwnd(IntPtr.Zero)) dpi = (int)Math.Round(screen.DpiX);
			resetScaling.Invoke(menu, new object[] { (int)Math.Round(dpi * percent / 100f) });
		}
	}
}
