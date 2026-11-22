using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	public static class UserInterface
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		// Artifacts and Weapons
		private static PictureBox gear_PictureBox;

		private static TextBox gear_TextBox;

		// Character
		private static PictureBox cName_PictureBox;

		private static PictureBox cLevel_PictureBox;
		private static PictureBox[] cTalent_PictureBoxes = new PictureBox[3];
		private static TextBox character_TextBox;

		// Counters
		private static Label weaponCount_Label;

		private static Label weaponMax_Label;

		private static Label artifactCount_Label;
		private static Label artifactMax_Label;

		private static Label characterCount_Label;

		// Status
		private static Label programStatus_Label;

		// Error box
		private static TextBox error_TextBox;

		// Current Images
		private static PictureBox navigation_PictureBox;

		// 1.7: the counts and status are kept here, not only in labels, so a look without some of these
		// controls (Simple has no item images or text output) still tracks them and can show them its own
		// way. Any control passed to Init may be null; updates to a missing control are skipped.
		private static int weapons, weaponsMax = -1, artifacts, artifactsMax = -1, characters;

		internal static int Weapons => System.Threading.Volatile.Read(ref weapons);
		internal static int WeaponsMax => System.Threading.Volatile.Read(ref weaponsMax);   // -1: not known yet
		internal static int Artifacts => System.Threading.Volatile.Read(ref artifacts);
		internal static int ArtifactsMax => System.Threading.Volatile.Read(ref artifactsMax);
		internal static int Characters => System.Threading.Volatile.Read(ref characters);

		internal static string Status { get; private set; } = "";
		internal static Color StatusColor { get; private set; } = Color.Empty;

		// Raised on the thread that made the change (often the scan thread).
		internal static event Action CountsChanged;
		internal static event Action StatusChanged;
		internal static event Action ErrorsChanged;

		public static void Init(PictureBox _gear_PictureBox, TextBox _a_textbox, PictureBox _c_name, PictureBox _c_level, PictureBox[] _c_talent, TextBox _c_textbox, Label _weaponCount, Label _weaponMax, Label _artifactCount, Label _artifactMax, Label _characterCount, Label _programStatus, TextBox _error_textBox, PictureBox _navigation_Image)
		{
			// Artifacts and Weapons
			gear_PictureBox = _gear_PictureBox;
			gear_TextBox = _a_textbox;

			// Characters
			cName_PictureBox = _c_name;
			cLevel_PictureBox = _c_level;
			cTalent_PictureBoxes = _c_talent;
			character_TextBox = _c_textbox;

			// Counters
			weaponCount_Label = _weaponCount;
			weaponMax_Label = _weaponMax;
			artifactCount_Label = _artifactCount;
			artifactMax_Label = _artifactMax;
			characterCount_Label = _characterCount;

			// Status
			programStatus_Label = _programStatus;

			// Error
			error_TextBox = _error_textBox;

			// Navigation Image
			navigation_PictureBox = _navigation_Image;
		}

		private static void UpdateElements(Bitmap bm, string text, PictureBox pictureBox, TextBox textBox)
		{
			UpdatePictureBox(bm, pictureBox);
			UpdateTextBox(text, textBox);
		}

		private static void UpdatePictureBox(Bitmap bm, PictureBox pictureBox)
		{
			if (pictureBox == null) return;
			try
			{
				Bitmap clone = new Bitmap(bm.Width, bm.Height);
				using (var copy = Graphics.FromImage(clone))
				{
					copy.DrawImage(bm, 0, 0);
				}
				MethodInvoker pictureBoxAction = delegate
			{
				pictureBox.Image = clone;
				pictureBox.Refresh();
			};
				pictureBox.Invoke(pictureBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", pictureBox.Name, e);
			}
		}

		private static void UpdateTextBox(string text, TextBox textBox)
		{
			if (textBox == null) return;
			try
			{
				MethodInvoker textBoxAction = delegate
				{
					textBox.AppendText(text.Replace("\n", Environment.NewLine));
					textBox.AppendText(Environment.NewLine);
					textBox.Refresh();
				};
				textBox.Invoke(textBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", textBox.Name, e);
			}
		}

		private static void UpdateLabel(string text, Label label)
		{
			if (label == null) return;
			try
			{
				MethodInvoker labelAction =  delegate
			{
				label.Text = text;
				label.Refresh();
			};
				label.Invoke(labelAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", label.Name, e);

			}
		}

		public static void SetGear(Bitmap bm, Weapon weapon)
		{
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(weapon.ToString());
		}

		public static void SetGear(Bitmap bm, Artifact artifact)
		{
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(artifact.ToString());
		}

		public static void SetGearPictureBox(Bitmap bm)
		{
			UpdatePictureBox(bm, gear_PictureBox);
		}

		public static void SetGearTextBox(string text)
		{
			UpdateTextBox(text, gear_TextBox);
		}

		internal static void SetMainCharacterName(string text)
		{
			UpdateTextBox($"Traveler name: {text}", character_TextBox);
		}

		public static void SetCharacter_NameAndElement(Bitmap bm, string name, string element)
		{
			UpdateElements(bm, $"Name: {name}\nElement: {element}", cName_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Level(Bitmap bm, int level, int maxLevel)
		{
			UpdateElements(bm, $"Level: {level} / {maxLevel}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Constellation(int level)
		{
			UpdateTextBox($"Constellation: {level}", character_TextBox);
		}

		internal static void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count)
		{
			UpdateElements(nameplate, $"Name: {name}", cName_PictureBox, character_TextBox);
			UpdateElements(quantity, $"Count: {count}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetMora(Bitmap mora, int count)
		{
			UpdateElements(mora, $"Mora: {count}", navigation_PictureBox, character_TextBox);
		}


		public static void SetCharacter_Talent(Bitmap bm, string text, int i)
		{
			if (i > -1 && i < 3 && cTalent_PictureBoxes != null)
			{
				UpdatePictureBox(bm, cTalent_PictureBoxes[i]);
				UpdateTextBox($"Talent {i + 1}: {text}", character_TextBox);
			}
		}

		public static void SetWeapon_Max(int value)
		{
			System.Threading.Volatile.Write(ref weaponsMax, value);
			UpdateLabel(value.ToString(), weaponMax_Label);
			CountsChanged?.Invoke();
			Logger.Info("Parsed {value} weapons to scan", value);
		}

		public static void SetArtifact_Max(int value)
		{
			System.Threading.Volatile.Write(ref artifactsMax, value);
			UpdateLabel(value.ToString(), artifactMax_Label);
			CountsChanged?.Invoke();
			Logger.Info("Parsed {value} artifacts to scan", value);
		}

		public static void IncrementArtifactCount()
		{
			UpdateLabel(System.Threading.Interlocked.Increment(ref artifacts).ToString(), artifactCount_Label);
			CountsChanged?.Invoke();
		}

		public static void IncrementWeaponCount()
		{
			UpdateLabel(System.Threading.Interlocked.Increment(ref weapons).ToString(), weaponCount_Label);
			CountsChanged?.Invoke();
		}

		public static void IncrementCharacterCount()
		{
			UpdateLabel(System.Threading.Interlocked.Increment(ref characters).ToString(), characterCount_Label);
			CountsChanged?.Invoke();
		}

		// Amber for a step that prepares the scan, so it reads differently from "Scanning".
		// Dark enough to keep 4.5:1 contrast on the window's gray background.
		public static readonly Color PreparingColor = Color.FromArgb(146, 64, 14);

		// Green and red for the other states, also dark enough for 4.5:1 on the window's gray
		// (plain Color.Green and Color.Red were 4.15:1 and 3.23:1).
		public static readonly Color OkColor = Color.FromArgb(0, 110, 0);
		public static readonly Color ErrorColor = Color.FromArgb(192, 0, 0);

		// The main window's background; the status label sits on it.
		public static readonly Color WindowColor = Color.FromArgb(234, 230, 230);

		public static void SetProgramStatus(string status, bool ok = true) =>
			SetProgramStatus(status, ok ? OkColor : ErrorColor);

		// The status line has 148 × 36 px: left of the output panel, between the speed hint and the Scan button.
		internal const int StatusWidth = 148, StatusHeight = 36;
		internal const float StatusSize = 15, StatusSmallSize = 11;
		private static int statusTop = -1;

		// A status that fits on one line stays at 15 pt. A longer one ("Downloading lookup tables…") drops to
		// 11 pt and is split onto two lines, the first as long as fits, so it isn't cut off by the panel.
		internal static (float Size, string Text) FitStatus(string status, FontFamily family)
		{
			if (string.IsNullOrEmpty(status)) return (StatusSize, status);
			using (var big = new Font(family, StatusSize))
				if (TextRenderer.MeasureText(status, big).Width <= StatusWidth) return (StatusSize, status);

			using (var small = new Font(family, StatusSmallSize))
			{
				var words = status.Split(' ');
				for (int split = words.Length - 1; split > 0; split--)
				{
					string first = string.Join(" ", words, 0, split), second = string.Join(" ", words, split, words.Length - split);
					if (TextRenderer.MeasureText(first, small).Width <= StatusWidth && TextRenderer.MeasureText(second, small).Width <= StatusWidth)
						return (StatusSmallSize, first + "\n" + second);
				}
			}
			return (StatusSmallSize, status);   // a single long word: smaller is the best we can do
		}

		public static void SetProgramStatus(string status, Color color)
		{
			Status = status ?? "";
			StatusColor = color;
			StatusChanged?.Invoke();
			if (programStatus_Label == null) return;

			MethodInvoker statusAction = delegate
			{
				if (statusTop < 0) statusTop = programStatus_Label.Top;
				var (size, text) = FitStatus(status, programStatus_Label.Font.FontFamily);
				programStatus_Label.Text = text;
				programStatus_Label.ForeColor = color;
				programStatus_Label.Font = new Font(programStatus_Label.Font.FontFamily, size);
				programStatus_Label.Top = text != null && text.Contains("\n") ? statusTop - 3 : statusTop;   // two lines end above the Scan button
				programStatus_Label.Refresh();
			};

			programStatus_Label.Invoke(statusAction);
		}

		private static int errorCount;

		/// <summary>Errors reported since the last reset (start of a scan). Shown in the scan-complete summary.</summary>
		public static int ErrorCount => System.Threading.Volatile.Read(ref errorCount);

		public static void AddError(string error)
		{
			System.Threading.Interlocked.Increment(ref errorCount);
			UpdateTextBox($"{error.Replace("\n", Environment.NewLine)}" + Environment.NewLine, error_TextBox);
			Logger.Error(error);
			ErrorsChanged?.Invoke();
		}

		public static void SetNavigation_Image(Bitmap bm)
		{
			UpdatePictureBox(bm, navigation_PictureBox);
		}

		// Runs an action on a control's thread; skipped when the open look has no such control.
		private static void OnControl(Control control, MethodInvoker action)
		{
			if (control != null) control.Invoke(action);
		}

		public static void ResetCharacterDisplay()
		{
			OnControl(cName_PictureBox, delegate { cName_PictureBox.Image = null; });
			OnControl(cLevel_PictureBox, delegate { cLevel_PictureBox.Image = null; });
			if (cTalent_PictureBoxes != null)
				foreach (var talent in cTalent_PictureBoxes)
					OnControl(talent, delegate { talent.Image = null; });
			OnControl(character_TextBox, delegate { character_TextBox.Clear(); });
		}

		public static void ResetGearDisplay()
		{
			OnControl(gear_PictureBox, delegate { gear_PictureBox.Image = null; });
			OnControl(gear_TextBox, delegate { gear_TextBox.Clear(); });
		}

		public static void ResetCounters()
		{
			System.Threading.Interlocked.Exchange(ref weapons, 0);
			System.Threading.Interlocked.Exchange(ref weaponsMax, -1);
			System.Threading.Interlocked.Exchange(ref artifacts, 0);
			System.Threading.Interlocked.Exchange(ref artifactsMax, -1);
			System.Threading.Interlocked.Exchange(ref characters, 0);

			OnControl(characterCount_Label, delegate { characterCount_Label.Text = "0"; characterCount_Label.Refresh(); });
			OnControl(weaponCount_Label, delegate { weaponCount_Label.Text = "0"; weaponCount_Label.Refresh(); });
			OnControl(weaponMax_Label, delegate { weaponMax_Label.Text = "?"; weaponMax_Label.Refresh(); });
			OnControl(artifactCount_Label, delegate { artifactCount_Label.Text = "0"; artifactCount_Label.Refresh(); });
			OnControl(artifactMax_Label, delegate { artifactMax_Label.Text = "?"; artifactMax_Label.Refresh(); });
			CountsChanged?.Invoke();
		}

		public static void ResetErrors()
		{
			System.Threading.Interlocked.Exchange(ref errorCount, 0);
			OnControl(error_TextBox, delegate { error_TextBox.Clear(); });
			ErrorsChanged?.Invoke();
		}

		public static void ResetAll()
		{
			ResetGearDisplay();

			ResetCharacterDisplay();

			ResetCounters();

			ResetErrors();
		}
	}
}