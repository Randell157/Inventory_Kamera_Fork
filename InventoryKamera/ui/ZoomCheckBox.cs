using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace InventoryKamera
{
	// 1.7.1: a checkbox whose square grows with View → Zoom. Windows draws a normal checkbox's square at a
	// fixed size for the screen, so at 175 % it looked tiny next to the text. At 100 % this is a plain
	// CheckBox; zoomed, it draws the square at the size Windows gives for the zoomed DPI (as smooth shapes in
	// Windows' colours, 1.7.3), then the text after it, and sizes itself to fit both.
	internal class ZoomCheckBox : CheckBox
	{
		private int zoom = Zoom.Normal;
		private bool hot, pressed;

		// Set by Zoom before it changes the fonts, so the new size is worked out with the new square.
		internal int ZoomPercent
		{
			get => zoom;
			set
			{
				if (value == zoom) return;
				zoom = value;
				Invalidate();
			}
		}

		internal bool Zoomed => zoom != Zoom.Normal;

		// The square's side and the gap after it, in pixels, at this zoom.
		internal Size GlyphSize => Glyphs.SizeAt(Dpi);
		internal int Gap => (int)Math.Round(3 * Dpi / 96f);
		private int Dpi => (int)Math.Round(DeviceDpi * zoom / 100f);

		public override Size GetPreferredSize(Size proposedSize)
		{
			var size = base.GetPreferredSize(proposedSize);
			if (!Zoomed || !AutoSize) return size;
			var glyph = GlyphSize;
			var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFlags);
			return new Size(
				Padding.Horizontal + glyph.Width + Gap + text.Width + 1,
				Math.Max(size.Height, Math.Max(glyph.Height, text.Height) + Padding.Vertical));
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (!Zoomed) { base.OnPaint(e); return; }

			var g = e.Graphics;
			if (BackColor.A < 255) ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
			else using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);

			var inside = new Rectangle(Padding.Left, Padding.Top, ClientSize.Width - Padding.Horizontal, ClientSize.Height - Padding.Vertical);
			var glyph = GlyphSize;
			var square = new Rectangle(inside.Left, inside.Top + (inside.Height - glyph.Height) / 2, glyph.Width, glyph.Height);
			Glyphs.Draw(g, square, Dpi, State);

			int textLeft = square.Right + Gap;
			var textArea = new Rectangle(textLeft, inside.Top, Math.Max(0, inside.Right - textLeft), inside.Height);
			var flags = TextFlags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);
			TextRenderer.DrawText(g, Text, Font, textArea, Enabled ? ForeColor : SystemColors.GrayText, flags);

			if (Focused && ShowFocusCues)
			{
				var used = TextRenderer.MeasureText(g, Text, Font, textArea.Size, flags);
				ControlPaint.DrawFocusRectangle(g, new Rectangle(textLeft, textArea.Top + (textArea.Height - used.Height) / 2, Math.Min(used.Width, textArea.Width), used.Height));
			}
		}

		private TextFormatFlags TextFlags =>
			TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (UseMnemonic ? 0 : TextFormatFlags.NoPrefix);

		private CheckBoxState State
		{
			get
			{
				int look = !Enabled ? 3 : pressed ? 2 : hot ? 1 : 0;   // normal, hot, pressed, disabled
				int first = CheckState == CheckState.Checked ? (int)CheckBoxState.CheckedNormal
					: CheckState == CheckState.Indeterminate ? (int)CheckBoxState.MixedNormal : (int)CheckBoxState.UncheckedNormal;
				return (CheckBoxState)(first + look);
			}
		}

		protected override void OnMouseEnter(EventArgs e) { hot = true; base.OnMouseEnter(e); if (Zoomed) Invalidate(); }
		protected override void OnMouseLeave(EventArgs e) { hot = false; pressed = false; base.OnMouseLeave(e); if (Zoomed) Invalidate(); }
		protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); if (Zoomed) Invalidate(); }
		protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); if (Zoomed) Invalidate(); }

		// Windows' themed checkbox images for a given DPI (Windows 10 1703 and later). Without themes or on
		// older Windows, a plain square drawn at the same size.
		internal static class Glyphs
		{
			private const int BP_CHECKBOX = 3, TS_DRAW = 2;
			private static readonly Dictionary<int, IntPtr> themes = new Dictionary<int, IntPtr>();
			private static bool noDpiThemes;

			internal static Size SizeAt(int dpi)
			{
				var theme = Theme(dpi);
				if (theme != IntPtr.Zero && GetThemePartSize(theme, IntPtr.Zero, BP_CHECKBOX, (int)CheckBoxState.UncheckedNormal, IntPtr.Zero, TS_DRAW, out var size) == 0 && size.cx > 0)
					return new Size(size.cx, size.cy);
				int side = (int)Math.Round(13 * dpi / 96f);
				return new Size(side, side);
			}

			// 1.7.3: Windows has one small checkbox picture and stretches it for any other size, so zoomed ticks
			// looked jagged (live 2026-11-22). Zoomed, the square and tick are drawn as smooth shapes instead, in
			// the colours of Windows' own picture for that state (so they match 100 % and the Windows theme).
			internal static void Draw(Graphics g, Rectangle square, int dpi, CheckBoxState state)
			{
				var look = LookOf(state);
				if (look != null) { DrawSmooth(g, square, look); return; }
				var flat = ButtonState.Flat
					| (state >= CheckBoxState.CheckedNormal && state <= CheckBoxState.CheckedDisabled ? ButtonState.Checked : 0)
					| (state == CheckBoxState.UncheckedDisabled || state == CheckBoxState.CheckedDisabled || state == CheckBoxState.MixedDisabled ? ButtonState.Inactive : 0);
				ControlPaint.DrawCheckBox(g, square, flat);
			}

			// The colours of Windows' checkbox picture for one state, read from it at 100 %.
			internal sealed class Look
			{
				internal Color Fill, Border;
				internal Color? Mark;      // the tick (or the dash of a half-ticked box); none when unticked
				internal bool Rounded;     // Windows 11 rounds the corners, Windows 10 doesn't
				internal bool Dash;        // half-ticked
			}
			private static readonly Dictionary<CheckBoxState, Look> looks = new Dictionary<CheckBoxState, Look>();
			private static readonly Color Behind = Color.FromArgb(240, 240, 240);

			// Null without themes (then the plain square is drawn).
			internal static Look LookOf(CheckBoxState state)
			{
				var theme = Theme(96);
				if (theme == IntPtr.Zero) return null;
				lock (looks)
				{
					if (looks.TryGetValue(state, out var known)) return known;
					var size = SizeAt(96);
					Look look = null;
					using (var picture = new Bitmap(size.Width, size.Height))
					{
						bool drawn;
						using (var g = Graphics.FromImage(picture))
						{
							g.Clear(Behind);
							drawn = DrawPicture(g, new Rectangle(Point.Empty, size), 96, state);
						}
						if (drawn)
						{
							look = Read(picture);
							look.Dash = state >= CheckBoxState.MixedNormal && state <= CheckBoxState.MixedDisabled;
						}
					}
					looks[state] = look;
					return look;
				}
			}

			// Windows' own picture for a DPI (stretched by Windows when it has none that size). False if it couldn't.
			internal static bool DrawPicture(Graphics g, Rectangle square, int dpi, CheckBoxState state)
			{
				var theme = Theme(dpi);
				if (theme == IntPtr.Zero) return false;
				var rect = new RECT { left = square.Left, top = square.Top, right = square.Right, bottom = square.Bottom };
				IntPtr hdc = g.GetHdc();
				try { return DrawThemeBackground(theme, hdc, BP_CHECKBOX, (int)state, ref rect, IntPtr.Zero) == 0; }
				finally { g.ReleaseHdc(hdc); }
			}

			// Border: the middle of the top edge. Fill: just inside it, above where any tick goes. Mark: the
			// pixel most unlike the fill, if it's clearly different.
			private static Look Read(Bitmap p)
			{
				int mid = p.Width / 2;
				var look = new Look
				{
					Border = p.GetPixel(mid, 0),
					Fill = p.GetPixel(mid, Math.Max(1, p.Height / 6)),
					Rounded = p.GetPixel(0, 0).ToArgb() != p.GetPixel(mid, 0).ToArgb(),
				};
				int best = 0; Color mark = look.Fill;
				for (int y = 2; y < p.Height - 2; y++)
					for (int x = 2; x < p.Width - 2; x++)
					{
						var c = p.GetPixel(x, y);
						int d = Math.Abs(c.R - look.Fill.R) + Math.Abs(c.G - look.Fill.G) + Math.Abs(c.B - look.Fill.B);
						if (d > best) { best = d; mark = c; }
					}
				if (best > 150) look.Mark = mark;
				return look;
			}

			internal static void DrawSmooth(Graphics g, Rectangle square, Look look)
			{
				var smoothing = g.SmoothingMode; var offset = g.PixelOffsetMode;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.PixelOffsetMode = PixelOffsetMode.Half;   // pixel n covers n..n+1, so the 1-pixel border is crisp
				try
				{
					// Whole pixels, so the border is crisp and in Windows' colour; a little thinner than a stretched
					// 1-pixel border, which looked heavy at 250–300 %. Drawn as the outer shape in the border colour
					// with the inner shape in the fill colour on top.
					int border = Math.Max(1, (int)Math.Round(square.Width / 16f));
					float radius = look.Rounded ? square.Width * 0.17f : 0;
					var inner = RectangleF.Inflate(square, -border, -border);
					using (var outer = Outline(square, radius))
					using (var inside = Outline(inner, Math.Max(0, radius - border)))
					using (var borderBrush = new SolidBrush(look.Border))
					using (var fill = new SolidBrush(look.Fill))
					{
						g.FillPath(borderBrush, outer);
						g.FillPath(fill, inside);
					}
					if (look.Mark is Color mark)
					{
						if (!look.Dash) ZoomMenuRenderer.DrawTick(g, square, mark);
						else using (var pen = new Pen(mark, Math.Max(1.5f, square.Width / 10.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
							g.DrawLine(pen, square.X + square.Width * 0.3f, square.Y + square.Height * 0.5f, square.X + square.Width * 0.7f, square.Y + square.Height * 0.5f);
					}
				}
				finally { g.SmoothingMode = smoothing; g.PixelOffsetMode = offset; }
			}

			private static GraphicsPath Outline(RectangleF r, float radius)
			{
				var path = new GraphicsPath();
				if (radius <= 0) { path.AddRectangle(r); return path; }
				float d = radius * 2;
				path.AddArc(r.Left, r.Top, d, d, 180, 90);
				path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
				path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
				path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
				path.CloseFigure();
				return path;
			}

			// Tests can't always switch visual styles on (only before a process's first window); the theme
			// pictures themselves don't need them.
			internal static bool ThemedForTests;

			private static IntPtr Theme(int dpi)
			{
				if (noDpiThemes || !(Application.RenderWithVisualStyles || ThemedForTests)) return IntPtr.Zero;
				lock (themes)
				{
					if (themes.TryGetValue(dpi, out var theme)) return theme;
					try { theme = OpenThemeDataForDpi(IntPtr.Zero, "BUTTON", (uint)dpi); }
					catch (EntryPointNotFoundException) { noDpiThemes = true; return IntPtr.Zero; }
					themes[dpi] = theme;   // kept for the life of the app: one per zoom step
					return theme;
				}
			}

			[StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
			[StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

			[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
			private static extern IntPtr OpenThemeDataForDpi(IntPtr hwnd, string classList, uint dpi);

			[DllImport("uxtheme.dll")]
			private static extern int GetThemePartSize(IntPtr theme, IntPtr hdc, int part, int state, IntPtr rect, int eSize, out SIZE size);

			[DllImport("uxtheme.dll")]
			private static extern int DrawThemeBackground(IntPtr theme, IntPtr hdc, int part, int state, ref RECT rect, IntPtr clip);
		}
	}
}
