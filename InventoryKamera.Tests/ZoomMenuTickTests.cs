using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7.3: the tick marks in the drop-down menus (View → look, View → Zoom, Options …) stayed 16 px while
	// the rest of the window grew with View → Zoom.
	[TestClass]
	public sealed class ZoomMenuTickTests
	{
		private static (Form Form, ToolStripMenuItem View, ToolStripMenuItem ZoomMenu) MenuForm()
		{
			var form = new Form { ClientSize = new Size(300, 200), Font = new Font("Segoe UI", 9F) };
			var zoom = new ToolStripMenuItem("Zoom");
			zoom.DropDownItems.Add(new ToolStripMenuItem("100%") { Checked = true });
			zoom.DropDownItems.Add(new ToolStripMenuItem("150%"));
			var view = new ToolStripMenuItem("View");
			view.DropDownItems.Add(new ToolStripMenuItem("Simple") { Checked = true });
			view.DropDownItems.Add(zoom);
			var menu = new MenuStrip();
			menu.Items.Add(view);
			form.Controls.Add(menu);
			form.MainMenuStrip = menu;
			return (form, view, zoom);
		}

		[TestMethod]
		public void DropDownTickAreaGrowsWithZoomAndComesBack()
		{
			var (form, view, zoom) = MenuForm();
			using (form)
			{
				Assert.AreEqual(new Size(16, 16), view.DropDown.ImageScalingSize, "as designed");
				foreach (int step in Zoom.Steps.Concat(new[] { 150, 100 }))
				{
					Zoom.Set(form, step);
					int want = (int)Math.Round(16 * step / 100f);
					Assert.AreEqual(new Size(want, want), view.DropDown.ImageScalingSize, $"View at {step} %");
					Assert.AreEqual(new Size(want, want), zoom.DropDown.ImageScalingSize, $"View → Zoom (a sub-menu) at {step} %");
				}
				Assert.AreEqual(new Size(16, 16), view.DropDown.ImageScalingSize, "back at 100 %");
			}
		}

		// What the renderer is handed for a ticked item when the drop-down is drawn.
		private sealed class Recorder : ZoomMenuRendererProbe
		{
			internal readonly List<Rectangle> Boxes = new List<Rectangle>();
			internal readonly List<Rectangle> Texts = new List<Rectangle>();
			protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
			{
				Boxes.Add(e.ImageRectangle);
				base.OnRenderItemCheck(e);
			}
			protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
			{
				Texts.Add(e.TextRectangle);
				base.OnRenderItemText(e);
			}
		}

		// Live 2026-11-22: at 175 % the blue square around the tick ran into "Classic" and "175%". The text
		// must start clear of the square (drawn 2 px around the tick box) at every zoom.
		[TestMethod]
		public void ZoomedDropDownGivesTheTickABiggerBoxAndMovesTheTextOver()
		{
			var (form, view, _) = MenuForm();
			var appRenderer = ToolStripManager.Renderer;
			var recorder = new Recorder();
			ToolStripManager.Renderer = recorder;   // like Program.Main sets ZoomMenuRenderer for every menu
			using (form)
			try
			{
				Rectangle BoxAt(int step)
				{
					Zoom.Set(form, step);
					recorder.Boxes.Clear();
					recorder.Texts.Clear();
					view.DropDown.PerformLayout();
					// A hidden drop-down doesn't paint itself, so the ticked item is painted the way the menu would.
					var ticked = view.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Checked);
					using (var bitmap = new Bitmap(Math.Max(1, ticked.Width), Math.Max(1, ticked.Height)))
					using (var g = Graphics.FromImage(bitmap))
						typeof(ToolStripItem).GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
							.Invoke(ticked, new object[] { new PaintEventArgs(g, new Rectangle(Point.Empty, bitmap.Size)) });
					Assert.AreEqual(1, recorder.Boxes.Count, $"one ticked item at {step} %");
					var box = recorder.Boxes[0];
					Assert.IsTrue(recorder.Texts.Count > 0 && recorder.Texts.All(t => t.Left >= box.Right + 2 + 4),
						$"{step} %: text at {string.Join(", ", recorder.Texts)} clear of the square around the tick box {box}");
					return box;
				}
				var original = BoxAt(100);
				Assert.IsTrue(original.Width <= 17, $"100 %: tick box {original.Size}, Windows' own");
				foreach (int step in Zoom.Steps)
				{
					var box = BoxAt(step);
					int want = (int)Math.Round(16 * step / 100f);
					Assert.IsTrue(Math.Abs(box.Width - want) <= 1 && Math.Abs(box.Height - want) <= 1, $"{step} %: tick box {box.Size}, want about {want}");
				}
				Assert.AreEqual(original, BoxAt(100), "back at 100 %: exactly as before");
			}
			finally { ToolStripManager.Renderer = appRenderer; }
		}

		// The dark pixels of a tick drawn into a box of size px.
		private static Rectangle InkOf(int px)
		{
			using (var bitmap = new Bitmap(px + 8, px + 8))
			using (var g = Graphics.FromImage(bitmap))
			{
				g.Clear(Color.White);
				ZoomMenuRenderer.DrawTick(g, new Rectangle(4, 4, px, px), Color.Black);
				int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
				for (int y = 0; y < bitmap.Height; y++)
					for (int x = 0; x < bitmap.Width; x++)
						if (bitmap.GetPixel(x, y).GetBrightness() < 0.5f)
						{
							left = Math.Min(left, x); right = Math.Max(right, x);
							top = Math.Min(top, y); bottom = Math.Max(bottom, y);
						}
				return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
			}
		}

		[TestMethod]
		public void TickFillsAZoomedBoxWithRoomAroundIt()
		{
			foreach (int px in new[] { 20, 28, 48 })
			{
				var ink = InkOf(px);
				var box = new Rectangle(4, 4, px, px);
				Assert.IsTrue(box.Contains(ink), $"{px} px: tick {ink} inside the box");
				Assert.IsTrue(ink.Width >= px * 0.45, $"{px} px: tick {ink.Width} px wide");
				Assert.IsTrue(ink.Height >= px * 0.3, $"{px} px: tick {ink.Height} px tall");
				// Live 2026-11-22: the first tick looked too big; it's now about 15 % smaller, with room around it.
				Assert.IsTrue(ink.Width <= px * 0.6, $"{px} px: tick {ink.Width} px wide, not more than 60 %");
				Assert.IsTrue(ink.Left - box.Left >= px * 0.15 && box.Right - ink.Right >= px * 0.15, $"{px} px: tick {ink} has room on both sides");
			}
		}

		[TestMethod]
		public void RendererDrawsATickForACheckedItemAtZoomAndLeavesWindowsTickAt100()
		{
			var renderer = new ZoomMenuRenderer();
			var item = new ToolStripMenuItem("Simple") { Checked = true };
			Rectangle Ink(int px, Image image)
			{
				using (var bitmap = new Bitmap(px + 8, px + 8))
				using (var g = Graphics.FromImage(bitmap))
				{
					g.Clear(Color.White);
					renderer.DrawItemCheck(new ToolStripItemImageRenderEventArgs(g, item, image, new Rectangle(4, 4, px, px)));
					var dark = new List<Point>();
					for (int y = 0; y < bitmap.Height; y++)
						for (int x = 0; x < bitmap.Width; x++)
							if (bitmap.GetPixel(x, y).GetBrightness() < 0.4f) dark.Add(new Point(x, y));
					return dark.Count == 0 ? Rectangle.Empty
						: Rectangle.FromLTRB(dark.Min(p => p.X), dark.Min(p => p.Y), dark.Max(p => p.X) + 1, dark.Max(p => p.Y) + 1);
				}
			}
			using (var windowsTick = new Bitmap(16, 16))
			{
				using (var g = Graphics.FromImage(windowsTick)) g.Clear(Color.Transparent);
				var zoomed = Ink(28, windowsTick);   // an empty picture: anything dark is the drawn tick
				Assert.IsTrue(zoomed.Width >= 12, $"28 px box: drawn tick {zoomed}");
				var normal = Ink(16, windowsTick);
				Assert.AreEqual(Rectangle.Empty, normal, "16 px box: Windows' picture as it is (here an empty one)");
			}
		}
	}

	// Lets the tests watch what ZoomMenuRenderer is asked to draw.
	internal class ZoomMenuRendererProbe : ToolStripProfessionalRenderer { }
}
