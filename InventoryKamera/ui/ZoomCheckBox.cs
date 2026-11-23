using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace InventoryKamera
{
	// 1.7.1: a checkbox whose square grows with View → Zoom. Windows draws a normal checkbox's square at a
	// fixed size for the screen, so at 175 % it looked tiny next to the text. At 100 % this is a plain
	// CheckBox; zoomed, it draws Windows' own checkbox image asked for at the zoomed DPI (so it stays sharp),
	// then the text after it, and sizes itself to fit both.
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

			internal static void Draw(Graphics g, Rectangle square, int dpi, CheckBoxState state)
			{
				var theme = Theme(dpi);
				if (theme != IntPtr.Zero)
				{
					var rect = new RECT { left = square.Left, top = square.Top, right = square.Right, bottom = square.Bottom };
					var offset = g.Transform.OffsetX;   // a double-buffered paint has no offset; keep it general
					rect.left += (int)offset; rect.right += (int)offset;
					rect.top += (int)g.Transform.OffsetY; rect.bottom += (int)g.Transform.OffsetY;
					IntPtr hdc = g.GetHdc();
					try { if (DrawThemeBackground(theme, hdc, BP_CHECKBOX, (int)state, ref rect, IntPtr.Zero) == 0) return; }
					finally { g.ReleaseHdc(hdc); }
				}
				var flat = ButtonState.Flat
					| (state >= CheckBoxState.CheckedNormal && state <= CheckBoxState.CheckedDisabled ? ButtonState.Checked : 0)
					| (state == CheckBoxState.UncheckedDisabled || state == CheckBoxState.CheckedDisabled || state == CheckBoxState.MixedDisabled ? ButtonState.Inactive : 0);
				ControlPaint.DrawCheckBox(g, square, flat);
			}

			private static IntPtr Theme(int dpi)
			{
				if (noDpiThemes || !Application.RenderWithVisualStyles) return IntPtr.Zero;
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
