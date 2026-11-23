using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	// 1.7.1: which Genshin version the scanner's lookup tables are for, next to the Scan button in every look
	// (mockup .mockups/genshin-version.html, you picked D). It shares the row with the status line and steps
	// aside when the status needs the room (a scan, "Updates available · Open Updates").
	internal static class GameDataText
	{
		internal const string GetIt = "Get it";

		// The line's words and its link (start, length; length 0 = no link).
		internal static (string Text, int LinkStart, int LinkLength, bool Missing) For(bool tablesMissing, Version local, UpdateResult lastCheck, bool twoLines = false)
		{
			string gap = twoLines ? "\n" : " · ";
			if (tablesMissing || local == null)
			{
				string missing = "Game data missing" + gap;
				return (missing + GetIt, missing.Length, GetIt.Length, true);
			}
			string text = "Game data: Genshin " + local.ToString(2);
			var newest = lastCheck?.DataError == null ? lastCheck?.DataRemote : null;
			if (newest == null || newest <= local) return (text, 0, 0, false);
			string newer = newest.ToString(2) + " available";
			return (text + gap + newer, text.Length + gap.Length, newer.Length, false);
		}
	}

	internal sealed class GameDataLine : LinkLabel
	{
		private readonly bool twoLines;
		private Control status;
		private bool downloading;

		// Whether the line wants to be seen (Visible also needs the window to be on screen).
		internal bool Showing { get; private set; } = true;

		internal GameDataLine(bool twoLines = false)
		{
			this.twoLines = twoLines;
			AutoSize = false;
			LinkColor = ActiveLinkColor = VisitedLinkColor = UpdatesForm.Blue;
			LinkBehavior = LinkBehavior.HoverUnderline;
			AccessibleName = "Game data version";
			LinkClicked += OnLinkClicked;
			UpdateNotice.Changed += OnNotice;
			ShowData();
		}

		protected override void Dispose(bool disposing)
		{
			UpdateNotice.Changed -= OnNotice;
			base.Dispose(disposing);
		}

		private void OnNotice()
		{
			if (IsDisposed) return;
			if (InvokeRequired) { try { BeginInvoke((MethodInvoker)ShowData); } catch (InvalidOperationException) { } }   // the check ends on another thread
			else ShowData();
		}

		// Reads the tables' version and the last update check again.
		internal void ShowData()
		{
			var database = AppStartup.Database;
			var (text, start, length, missing) = GameDataText.For(database?.TablesMissing ?? true, database?.TablesMissing == false ? database.LocalVersion : null, UpdateNotice.Last, twoLines);
			Text = text;
			LinkArea = new LinkArea(start, length);
			ForeColor = missing ? UserInterface.ErrorColor : SystemColors.GrayText;
			Fit();
		}

		// Shares a row with the status line: shown only while the status text ends before this line's text.
		// atRowEnd: the line hugs its text at the right end of the status line's row (Simple, Advanced), so
		// it never covers the status text (a label paints its whole box). Otherwise it keeps its own bounds.
		internal void Beside(Control statusLine, bool atRowEnd)
		{
			status = statusLine;
			hug = atRowEnd;
			BringToFront();   // over the status line's box; Fit keeps it off the status text
			status.TextChanged += (s, e) => Fit();
			status.FontChanged += (s, e) => Fit();
			status.SizeChanged += (s, e) => Fit();
			status.LocationChanged += (s, e) => Fit();
			Fit();
		}

		protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
		protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
		protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
		protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Fit(); }

		// Where this line's text starts and ends, in its parent's coordinates.
		internal (int Left, int Right) TextSpan
		{
			get
			{
				int width = TextWidth;
				bool right = TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.BottomRight;
				return right ? (Right - width, Right) : (Left, Left + width);
			}
		}

		// Measured the way a LinkLabel draws (with a link it uses GDI+, a little wider than TextRenderer says).
		internal int TextWidth => GetPreferredSize(Size.Empty).Width;

		private bool hug, fitting;

		private void Fit()
		{
			if (fitting) return;
			fitting = true;
			try
			{
				if (hug && status != null)
				{
					int width = TextWidth + 2;
					var bounds = new Rectangle(status.Right - width, status.Top, width, status.Height);
					if (Bounds != bounds) Bounds = bounds;
				}
				bool show = true;
				if (status != null && !string.IsNullOrEmpty(status.Text))
				{
					int statusRight = status.Left + TextRenderer.MeasureText(status.Text, status.Font).Width;
					show = statusRight + Font.Height / 2 <= TextSpan.Left;
				}
				Showing = show;
				Visible = show;
			}
			finally { fitting = false; }
		}

		private async void OnLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var database = AppStartup.Database;
			if (database?.TablesMissing == false) { UpdatesForm.ShowFor(FindForm()); return; }   // "7.3 available"
			if (downloading || ScanSession.IsRunning) return;
			downloading = true;
			try { await AppStartup.UpdateGameDataAsync(force: false); }   // "Get it": the first download, with its status and popups
			finally { downloading = false; }
			UpdateNotice.Refresh();
			ShowData();
		}
	}
}
