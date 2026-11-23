using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// Live 2026-11-22 (1.7.1 check, Classic at 125–175 %): the check squares stayed their 100 % size while
	// everything else grew. Every look's checkboxes are now ZoomCheckBoxes.
	[TestClass]
	public sealed class ZoomCheckBoxTests
	{
		private static IEnumerable<ZoomCheckBox> Boxes(Control root)
		{
			foreach (Control c in root.Controls)
			{
				if (c is ZoomCheckBox box) yield return box;
				foreach (var inner in Boxes(c)) yield return inner;
			}
		}

		private static void InEveryLook(Action<string, Form> check)
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var simple = new SimpleForm())
				using (var advanced = new AdvancedForm())
				using (var classic = new MainForm())
				{
					foreach (var (name, form) in new (string, Form)[] { ("Simple", simple), ("Advanced", advanced), ("Classic", classic) })
					{
						SimpleLookTests.Bind(form);
						check(name, form);
					}
				}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		[TestMethod]
		public void EveryCheckboxInEveryLookIsAZoomCheckBox()
		{
			InEveryLook((name, form) =>
			{
				var all = new List<CheckBox>();
				void Collect(Control c) { foreach (Control child in c.Controls) { if (child is CheckBox cb) all.Add(cb); Collect(child); } }
				Collect(form);
				Assert.IsTrue(all.Count >= 5, name);
				foreach (var cb in all) Assert.IsInstanceOfType(cb, typeof(ZoomCheckBox), $"{name}: {cb.Text}");
			});
		}

		[TestMethod]
		public void SquaresGrowWithZoomAndTheTextStillFits()
		{
			InEveryLook((name, form) =>
			{
				var boxes = Boxes(form).ToList();
				int dpi = boxes[0].DeviceDpi;
				foreach (int step in Zoom.Steps.Concat(new[] { 150, 100 }))
				{
					Zoom.Set(form, step);
					foreach (var box in boxes)
					{
						string where = $"{name} {box.Text} at {step} %";
						Assert.AreEqual(step, box.ZoomPercent, where);
						if (step == Zoom.Normal) { Assert.IsFalse(box.Zoomed, where); continue; }

						// The square is within a few pixels of 13 px × zoom × screen scale.
						int expected = (int)Math.Round(13 * step / 100f * dpi / 96f);
						Assert.IsTrue(Math.Abs(box.GlyphSize.Width - expected) <= Math.Max(3, expected / 6), $"{where}: square {box.GlyphSize.Width} px, expected about {expected}");
						Assert.IsTrue(box.GlyphSize.Height <= box.ClientSize.Height, $"{where}: square taller than the box");

						// Square, gap and text fit inside the box.
						int needed = box.Padding.Horizontal + box.GlyphSize.Width + box.Gap + TextRenderer.MeasureText(box.Text, box.Font).Width - 2;
						Assert.IsTrue(box.ClientSize.Width >= needed, $"{where}: {box.ClientSize.Width} px wide, needs {needed}");
					}
					// Side by side boxes (Advanced's Scan row) never overlap.
					foreach (var pair in boxes.GroupBy(b => b.Parent))
						foreach (var a in pair) foreach (var b in pair)
							if (a != b && a.Visible == b.Visible) Assert.IsFalse(a.Bounds.IntersectsWith(b.Bounds) && a.Top == b.Top, $"{name}: {a.Text} overlaps {b.Text} at {step} %");
				}
			});
		}

		// The zoomed square is really drawn at its new size: a checked box's picture has the check's colour
		// across about the square's width.
		[TestMethod]
		public void ZoomedSquareIsDrawnAtItsSize()
		{
			using (var form = new Form { Font = new Font("Microsoft Sans Serif", 8.25f) })
			{
				var box = new ZoomCheckBox { Text = "Weapons", AutoSize = true, Checked = true, Location = new Point(10, 10) };
				form.Controls.Add(box);
				foreach (int step in new[] { 175, 300 })
				{
					Zoom.Set(form, step);
					using (var picture = new Bitmap(box.Width, box.Height))
					{
						box.DrawToBitmap(picture, new Rectangle(Point.Empty, box.Size));
						var back = picture.GetPixel(picture.Width - 1, 0);
						int first = -1, last = -1;
						for (int x = 0; x < box.GlyphSize.Width + 4 && x < picture.Width; x++)
							for (int y = 0; y < picture.Height; y++)
								if (Distance(picture.GetPixel(x, y), back) > 120) { if (first < 0) first = x; last = x; break; }
						Assert.IsTrue(first >= 0, $"nothing drawn at {step} %");
						int drawn = last - first + 1;
						Assert.IsTrue(drawn >= box.GlyphSize.Width * 3 / 4, $"square drawn {drawn} px wide at {step} %, expected about {box.GlyphSize.Width}");
					}
				}
				Zoom.Set(form, 100);
				Assert.IsFalse(box.Zoomed);
			}
		}

		private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
	}
}
