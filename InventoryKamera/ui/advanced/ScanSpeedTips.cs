using System;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The scan speed drop-down's tooltips, as in Classic: hovering the closed box (or its label and hint)
	// explains the chosen speed; while the list is open, the choice under the pointer is explained beside it.
	// Same texts as Classic (ScanSpeeds.TooltipText).
	internal sealed class ScanSpeedTips : IDisposable
	{
		private readonly ComboBox box;
		private readonly Control[] tipped;
		private readonly ToolTip current = new ToolTip { AutoPopDelay = 30000 };
		private readonly ToolTip choice = new ToolTip();
		private int shownChoice = -1;

		internal ScanSpeedTips(ComboBox box, params Control[] alsoTipped)
		{
			this.box = box;
			tipped = new Control[alsoTipped.Length + 1];
			tipped[0] = box;
			alsoTipped.CopyTo(tipped, 1);
			// Drawn here only to know which choice is under the pointer while the list is open.
			box.DrawMode = DrawMode.OwnerDrawFixed;
			box.DrawItem += DrawItem;
			box.DropDownClosed += (s, e) => { choice.Hide(box); shownChoice = -1; };
		}

		// The tooltip of the chosen speed, on the box and its label and hint.
		internal void ShowCurrent(int step)
		{
			string tip = ScanSpeeds.TooltipText(step);
			foreach (var control in tipped) current.SetToolTip(control, tip);
		}

		internal string CurrentTip(Control control) => current.GetToolTip(control);

		private void DrawItem(object sender, DrawItemEventArgs e)
		{
			if (e.Index < 0) return;
			e.DrawBackground();
			TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), e.Font, e.Bounds, e.ForeColor,
				TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			e.DrawFocusRectangle();

			bool inOpenList = box.DroppedDown && (e.State & DrawItemState.ComboBoxEdit) == 0;
			if (inOpenList && (e.State & DrawItemState.Selected) != 0 && e.Index != shownChoice)
			{
				shownChoice = e.Index;
				// Beside the list, level with the choice. The list opens right under the box.
				choice.Show(ScanSpeeds.TooltipText(ScanSpeeds.StepAt(e.Index), forChoice: true), box,
					box.Width + 6, box.Height + e.Bounds.Top);
			}
		}

		public void Dispose()
		{
			current.Dispose();
			choice.Dispose();
		}
	}
}
