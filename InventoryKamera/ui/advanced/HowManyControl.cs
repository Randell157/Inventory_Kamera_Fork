using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	// (•) All characters
	// ( ) Only the first [5] characters
	// Value is the setting's number: 0 for all, N for the first N. Bindable ("Value" + ValueChanged).
	public sealed class HowManyControl : UserControl
	{
		internal readonly RadioButton AllChoice = new RadioButton(), OnlyChoice = new RadioButton();
		internal readonly NumericUpDown Number = new NumericUpDown();
		private readonly Label unit = new Label();
		private int value = HowMany.All;
		private int lastOnly = 1;
		private bool updating;

		public event EventHandler ValueChanged;

		public HowManyControl() : this("All characters", "characters", 100) { }

		internal HowManyControl(string allText, string unitText, int max)
		{
			AutoScaleMode = AutoScaleMode.Inherit;
			Size = new Size(230, 50);
			AllChoice.Text = allText;
			AllChoice.AutoSize = true;
			AllChoice.Location = new Point(0, 0);
			OnlyChoice.Text = "Only the first";
			OnlyChoice.AutoSize = true;
			OnlyChoice.Location = new Point(0, 26);
			Number.Minimum = 1;
			Number.Maximum = max;
			Number.SetBounds(104, 24, 52, 23);
			Number.AccessibleName = unitText + " to scan";
			unit.Text = unitText;
			unit.AutoSize = true;
			unit.Location = new Point(160, 28);
			Controls.AddRange(new Control[] { AllChoice, OnlyChoice, Number, unit });

			AllChoice.CheckedChanged += (s, e) => FromControls();
			OnlyChoice.CheckedChanged += (s, e) => FromControls();
			Number.ValueChanged += (s, e) => { if (!OnlyChoice.Checked && !updating) OnlyChoice.Checked = true; else FromControls(); };
			ToControls();
		}

		internal int Max => (int)Number.Maximum;

		public int Value
		{
			get => value;
			set
			{
				int clean = value <= HowMany.All ? HowMany.All : Math.Min(value, Max);
				if (clean == this.value) return;
				this.value = clean;
				ToControls();
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void ToControls()
		{
			updating = true;
			if (value > HowMany.All) lastOnly = value;
			Number.Value = HowMany.Shown(value, lastOnly, Max);
			AllChoice.Checked = value == HowMany.All;
			OnlyChoice.Checked = value != HowMany.All;
			updating = false;
		}

		private void FromControls()
		{
			if (updating) return;
			Value = HowMany.Value(OnlyChoice.Checked, (int)Number.Value, Max);
		}
	}

	// Minimum rarity as five stars you click (or set with the arrow keys), with "3★ and up" spelled out.
	// Value 1–5. Bindable ("Value" + ValueChanged).
	public sealed class StarRating : Control
	{
		internal const int Stars = 5;
		private int value = 1;

		// Amber for chosen stars, 3.6:1 on the window; the text beside them says the same in words.
		internal static readonly Color On = Color.FromArgb(176, 112, 0), Off = Color.FromArgb(200, 200, 200);

		public event EventHandler ValueChanged;

		public StarRating()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
			TabStop = true;
			Size = new Size(170, 22);
			AccessibleRole = AccessibleRole.Slider;
			AccessibleDescription = AdvancedText.Rarity(value);
		}

		public int Value
		{
			get => value;
			set
			{
				int clean = Math.Max(1, Math.Min(Stars, value));
				if (clean == this.value) return;
				this.value = clean;
				AccessibleDescription = AdvancedText.Rarity(clean);
				Invalidate();
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private Font StarFont => new Font(Font.FontFamily, Font.Size * 1.25f);

		// Each star's box, worked out from the font so it grows with the zoom.
		internal Rectangle StarBounds(int index)
		{
			int size = (int)Math.Round(Font.Height * 1.15);
			return new Rectangle(index * size, (Height - size) / 2, size, size);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			using (var stars = StarFont)
			{
				for (int i = 0; i < Stars; i++)
					TextRenderer.DrawText(e.Graphics, "★", stars, StarBounds(i), i < value ? On : Off,
						TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			}
			var textBox = new Rectangle(StarBounds(Stars).Left + 4, 0, Width - StarBounds(Stars).Left - 4, Height);
			TextRenderer.DrawText(e.Graphics, AdvancedText.Rarity(value), Font, textBox, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
			if (Focused && ShowFocusCues)
				ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, StarBounds(Stars).Left, Height));
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			for (int i = 0; i < Stars; i++)
				if (StarBounds(i).Contains(e.Location)) Value = i + 1;
		}

		protected override bool IsInputKey(Keys keyData) =>
			keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			switch (e.KeyCode)
			{
				case Keys.Left: case Keys.Down: Value--; break;
				case Keys.Right: case Keys.Up: Value++; break;
				case Keys.Home: Value = 1; break;
				case Keys.End: Value = Stars; break;
				default:
					if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) Value = e.KeyCode - Keys.D0;
					return;
			}
			e.Handled = true;
		}

		protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
		protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
	}
}
