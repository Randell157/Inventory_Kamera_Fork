using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The View menu every look shares: Simple / Advanced / Classic (the open one ticked), then Zoom.
	// Nothing in it works during a scan (the scan reports to the open window).
	internal static class LookMenu
	{
		internal static ToolStripMenuItem Build(Look current, Form window)
		{
			var view = new ToolStripMenuItem("View");
			foreach (var look in new[] { Look.Simple, Look.Advanced, Look.Classic })
			{
				var item = new ToolStripMenuItem(Looks.MenuText(look))
				{
					Checked = look == current,
					Tag = look,
					ToolTipText = Looks.IsBuilt(look) ? null : Looks.NotBuiltTip(look),
				};
				item.Click += (s, e) => LookContext.Current?.SwitchTo(look);
				view.DropDownItems.Add(item);
			}
			view.DropDownItems.Add(new ToolStripSeparator());

			var zoom = new ToolStripMenuItem("Zoom");
			foreach (int step in Zoom.Steps)
			{
				var item = new ToolStripMenuItem(Zoom.MenuText(step)) { Tag = step };
				item.Click += (s, e) => LookContext.Current?.SetZoom(step);
				zoom.DropDownItems.Add(item);
			}
			view.DropDownItems.Add(zoom);

			view.DropDownOpening += (s, e) =>
			{
				foreach (var item in view.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.Tag is Look))
					item.Enabled = Looks.IsBuilt((Look)item.Tag) && !ScanSession.IsRunning;
				zoom.Enabled = !ScanSession.IsRunning;
			};
			zoom.DropDownOpening += (s, e) =>
			{
				int shown = LookContext.Current?.ShownZoom ?? Zoom.Normal;
				var normal = new System.Drawing.Size(window.Width * Zoom.Normal / shown, window.Height * Zoom.Normal / shown);
				var area = Screen.FromControl(window).WorkingArea;
				foreach (ToolStripMenuItem item in zoom.DropDownItems)
				{
					int step = (int)item.Tag;
					bool fits = Zoom.Fits(normal, step, area);
					item.Checked = step == shown;
					item.Enabled = fits;
					item.Text = Zoom.MenuText(step) + (fits ? "" : "  (too big for this screen)");   // greyed items show no tooltip
				}
			};
			return view;
		}
	}
}
