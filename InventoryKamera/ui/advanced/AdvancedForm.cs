using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The Advanced look (1.7, mockup "Advanced B, sections"): every option visible at once in labelled boxes
	// on the left; the Scan button, counts, item preview and error log on the right. It edits the same
	// settings as Classic and Simple, so a change shows in every look.
	public sealed class AdvancedForm : Form, ILookWindow, IScanWindow
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		private static Properties.Settings Settings => Properties.Settings.Default;

		// Layout at 100 % (96 DPI): an 864 × 561 client area; options 540 px wide, the right column 288 px.
		private const int Left_ = 12, Right_ = 564, RightWidth = 288, Half = 265;

		private readonly MenuStrip menu = new MenuStrip();
		internal readonly CheckBox Weapons = new ZoomCheckBox(), Artifacts = new ZoomCheckBox(), Characters = new ZoomCheckBox(), DevItems = new ZoomCheckBox(), Materials = new ZoomCheckBox();
		internal readonly CheckBox LogScreenshots = new ZoomCheckBox(), EquipWeapons = new ZoomCheckBox(), EquipArtifacts = new ZoomCheckBox();
		internal readonly ComboBox ScanSpeed = new ComboBox();
		private readonly Label scanSpeedHint = new Label();
		internal ScanSpeedTips SpeedTips { get; private set; }
		internal readonly TextBox OutputPath = new TextBox();
		internal readonly StarRating WeaponRarity = new StarRating(), ArtifactRarity = new StarRating();
		internal readonly NumericUpDown WeaponLevel = new NumericUpDown(), ArtifactLevel = new NumericUpDown();
		internal readonly Label WeaponLevelNote = new Label(), ArtifactLevelNote = new Label();
		internal readonly HowManyControl ArtifactPages = new HowManyControl("All artifact pages", "pages", 10);
		internal readonly HowManyControl CharacterCount = new HowManyControl("All characters", "characters", 100);
		internal readonly TextBox TravelerName = new TextBox(), WandererName = new TextBox(), Mannequin1Name = new TextBox(), Mannequin2Name = new TextBox();
		private readonly Button scan = new Button();
		internal readonly Button OpenOptimizer = new Button();
		internal readonly LinkLabel Status = new LinkLabel();
		internal readonly GameDataLine GameData = new GameDataLine();
		internal readonly Label ErrorTitle = new Label();
		private readonly Label weaponCount = new Label(), weaponMax = new Label(), artifactCount = new Label(), artifactMax = new Label(), characterCount = new Label();
		private readonly PictureBox gearPicture = new PictureBox(), characterPicture = new PictureBox();
		private readonly TextBox gearText = new TextBox(), characterText = new TextBox();
		internal readonly TextBox ErrorLog = new TextBox();
		private readonly ToolTip tips = new ToolTip();
		private bool listening;

		// Leaving one of these empty fills its default back in (not the Traveler's: empty there means
		// "read the name from the game"), as in Classic.
		private (TextBox Box, string Setting, string Key)[] Names => new[] {
			(TravelerName, "TravelerName", "traveler"), (WandererName, "WandererName", "wanderer"),
			(Mannequin1Name, "Manequin1Name", "manequin1"), (Mannequin2Name, "Manequin2Name", "manequin2") };

		public AdvancedForm()
		{
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Segoe UI", 9F);
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			ClientSize = new Size(864, 561);
			Text = $"Inventory Kamera V{LookContext.VersionText}";
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

			BuildMenu();
			BuildScanGroup();
			BuildWeaponsGroup();
			BuildArtifactsGroup();
			BuildCharactersGroup();
			BuildNamesGroup();
			BuildRightColumn();

			ResumeLayout(true);
			FormClosed += (s, e) => StopListening();
			FormClosing += (s, e) => { Settings.Save(); ScanSession.RemoveHotkey(); };
			Settings.PropertyChanged += SettingChanged;
			ShowScanSpeed();
		}

		#region Building the window

		private void BuildMenu()
		{
			var file = new ToolStripMenuItem("File");
			file.DropDownItems.Add("Quit", null, (s, e) => Application.Exit());
			var options = new ToolStripMenuItem("Options");
			options.DropDownItems.Add(GameKeys.MenuItem("Inventory Key", "InventoryKey", "Key to open inventory", () => (int)Navigation.inventoryKey));
			options.DropDownItems.Add(GameKeys.MenuItem("Character Screen Key", "CharacterKey", "Key to open the character screen", () => (int)Navigation.characterKey));
			options.DropDownItems.Add(GameKeys.MenuItem("Character Slot 1 Key", "slot1Key", "Key to switch to party slot 1", () => (int)Navigation.slotOneKey));
			options.DropDownItems.Add(new ToolStripSeparator());
			var update = new ToolStripMenuItem("Updates…");
			update.Click += (s, e) => UpdatesForm.ShowFor(this);
			options.DropDownItems.Add(update);
			options.DropDownItems.Add("Open Export Folder", null, (s, e) => ScanWindows.OpenFolder(OutputPath.Text));
			options.DropDownItems.Add("Update Executables", null, (s, e) => new ui.ExecutablesForm().Show());
			options.DropDownOpening += (s, e) => update.Enabled = !ScanSession.IsRunning;
			menu.Items.AddRange(new ToolStripItem[] { file, LookMenu.Build(Look.Advanced, this), options });
			UpdateMark.Attach(this, menu, update);
			MainMenuStrip = menu;
			Controls.Add(menu);
		}

		private GroupBox weaponsGroup, artifactsGroup, charactersGroup;

		private GroupBox Group(string text, Rectangle bounds)
		{
			var box = new GroupBox { Text = text, Bounds = bounds };
			box.Paint += PaintHighlightOutline;
			Controls.Add(box);
			return box;
		}

		private static Label Caption(Control parent, string text, int x, int y, int width = 0)
		{
			var label = new Label { Text = text, Location = new Point(x, y), AutoSize = width == 0 };
			if (width > 0) label.Size = new Size(width, 18);
			parent.Controls.Add(label);
			return label;
		}

		private static void Bind(Control control, string property, string setting) =>
			control.DataBindings.Add(new Binding(property, Properties.Settings.Default, setting, true, DataSourceUpdateMode.OnPropertyChanged));

		private void BuildScanGroup()
		{
			var group = Group("Scan", new Rectangle(Left_, 30, 540, 144));
			int x = 10;
			foreach (var (box, text, setting) in new[] {
				(Weapons, "Weapons", "ScanWeapons"), (Artifacts, "Artifacts", "ScanArtifacts"), (Characters, "Characters", "ScanCharacters"),
				(DevItems, "Dev items", "ScanCharDevItems"), (Materials, "Materials", "ScanMaterials") })
			{
				box.Text = text;
				box.AutoSize = true;
				box.Location = new Point(x, 22);
				Bind(box, "Checked", setting);
				group.Controls.Add(box);
				x += text.Length * 7 + 34;
			}
			tips.SetToolTip(DevItems, "Character development items");

			var delayLabel = Caption(group, "Delay per item", 10, 55);
			ScanSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
			ScanSpeed.SetBounds(110, 51, 190, 23);
			ScanSpeed.AccessibleName = "Delay per item";
			for (int i = 0; i < ScanSpeeds.ChoiceCount; i++) ScanSpeed.Items.Add(ScanSpeeds.ItemText(ScanSpeeds.StepAt(i)));
			ScanSpeed.SelectedIndexChanged += (s, e) =>
			{
				if (ScanSpeed.SelectedIndex < 0) return;
				int step = ScanSpeeds.StepAt(ScanSpeed.SelectedIndex);
				if (Settings.ScanSpeed != step) Settings.ScanSpeed = step;
				scanSpeedHint.Text = ScanSpeeds.HintText(step);
				SpeedTips.ShowCurrent(step);
			};
			group.Controls.Add(ScanSpeed);
			scanSpeedHint.SetBounds(306, 55, 220, 18);
			scanSpeedHint.ForeColor = UserInterface.OkColor;
			group.Controls.Add(scanSpeedHint);
			SpeedTips = new ScanSpeedTips(ScanSpeed, delayLabel, scanSpeedHint);

			Caption(group, "Save exports to", 10, 86);
			OutputPath.SetBounds(110, 83, 316, 23);
			Bind(OutputPath, "Text", "OutputPath");
			group.Controls.Add(OutputPath);
			var browse = new Button { Text = "Browse…", Bounds = new Rectangle(432, 82, 96, 25) };
			browse.Click += (s, e) => { var folder = ScanWindows.PickFolder(OutputPath.Text); if (folder != null) OutputPath.Text = folder; };
			group.Controls.Add(browse);

			LogScreenshots.Text = "Log all screenshots";
			LogScreenshots.AutoSize = true;
			LogScreenshots.Location = new Point(10, 114);
			Bind(LogScreenshots, "Checked", "LogScreenshots");
			tips.SetToolTip(LogScreenshots, "Saves every screenshot to the logging folder, not only the ones that failed. Slower; for reporting a problem.");
			group.Controls.Add(LogScreenshots);
		}

		private void RarityAndLevel(GroupBox group, StarRating stars, string raritySetting, string what,
			NumericUpDown level, Label note, string levelSetting, int min, int max, Func<int, string> meaning)
		{
			Caption(group, "Minimum rarity", 10, 27);
			stars.SetBounds(96, 23, 164, 24);
			stars.AccessibleName = $"Minimum {what} rarity";
			Bind(stars, "Value", raritySetting);
			group.Controls.Add(stars);

			Caption(group, "Minimum level", 10, 59);
			level.Minimum = min;
			level.Maximum = max;
			level.SetBounds(100, 56, 52, 23);
			level.AccessibleName = $"Minimum {what} level";
			Bind(level, "Value", levelSetting);
			group.Controls.Add(level);
			note.SetBounds(156, 59, 106, 18);
			note.ForeColor = UserInterface.OkColor;
			level.ValueChanged += (s, e) => note.Text = meaning((int)level.Value);
			note.Text = meaning((int)level.Value);
			group.Controls.Add(note);
		}

		private void BuildWeaponsGroup()
		{
			var group = weaponsGroup = Group("Weapons", new Rectangle(Left_, 180, Half, 116));
			RarityAndLevel(group, WeaponRarity, "MinimumWeaponRarity", "weapon", WeaponLevel, WeaponLevelNote, "MinimumWeaponLevel", 1, 90, AdvancedText.WeaponLevel);
			EquipWeapons.Text = "Equip weapons to their owners";
			EquipWeapons.AutoSize = true;
			EquipWeapons.Location = new Point(10, 88);
			Bind(EquipWeapons, "Checked", "EquipWeapons");
			group.Controls.Add(EquipWeapons);
		}

		private void BuildArtifactsGroup()
		{
			var group = artifactsGroup = Group("Artifacts", new Rectangle(Left_ + Half + 10, 180, Half, 204));
			RarityAndLevel(group, ArtifactRarity, "MinimumArtifactRarity", "artifact", ArtifactLevel, ArtifactLevelNote, "MinimumArtifactLevel", 0, 20, AdvancedText.ArtifactLevel);
			Caption(group, "Pages to scan", 10, 90);
			ArtifactPages.Location = new Point(10, 110);
			ArtifactPages.AccessibleName = "Artifact pages to scan";
			Bind(ArtifactPages, "Value", "SortByObtained");
			tips.SetToolTip(ArtifactPages.OnlyChoice, "Scans that many pages of your newest artifacts.");
			group.Controls.Add(ArtifactPages);
			EquipArtifacts.Text = "Equip artifacts to their owners";
			EquipArtifacts.AutoSize = true;
			EquipArtifacts.Location = new Point(10, 172);
			Bind(EquipArtifacts, "Checked", "EquipArtifacts");
			group.Controls.Add(EquipArtifacts);
		}

		private void BuildCharactersGroup()
		{
			var group = charactersGroup = Group("Characters", new Rectangle(Left_, 302, Half, 82));
			CharacterCount.Location = new Point(10, 22);
			CharacterCount.AccessibleName = "Characters to scan";
			Bind(CharacterCount, "Value", "NumOfCharToScan");
			tips.SetToolTip(CharacterCount.OnlyChoice, "Scans that many characters, starting from the first in your list.");
			group.Controls.Add(CharacterCount);
		}

		private void BuildNamesGroup()
		{
			var group = Group("Custom names", new Rectangle(Left_, 392, 540, 92));
			var places = new[] { ("Traveler", 10, 26), ("Wanderer", 280, 26), ("Mannequin (f)", 10, 58), ("Mannequin (m)", 280, 58) };
			var names = Names;
			for (int i = 0; i < names.Length; i++)
			{
				var (text, x, y) = places[i];
				var (box, setting, key) = names[i];
				Caption(group, text, x, y + 3);
				box.SetBounds(x + 92, y, 160, 23);
				box.AccessibleName = text + " name";
				Bind(box, "Text", setting);
				box.TextChanged += (s, e) => CheckName(box, key);
				if (key != "traveler")
					box.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(box.Text)) box.Text = CustomNames.OrDefault(setting, box.Text); };
				group.Controls.Add(box);
			}
			tips.SetToolTip(TravelerName, "Empty: read it from the game");
		}

		private void BuildRightColumn()
		{
			scan.Text = "Scan Genshin";
			scan.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
			scan.SetBounds(Right_, 34, RightWidth, 40);
			scan.FlatStyle = FlatStyle.Flat;
			scan.FlatAppearance.BorderSize = 0;
			scan.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 117, 197);
			scan.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 166);
			scan.EnabledChanged += (s, e) => StyleScanButton();
			StyleScanButton();
			scan.Click += Scan_Click;
			Controls.Add(scan);
			Controls.Add(new Label { Text = "Press Enter in the game to stop a scan", ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(Right_, 76, RightWidth, 18) });

			Status.SetBounds(Right_, 98, RightWidth, 20);
			Status.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
			Status.LinkColor = Status.ActiveLinkColor = Status.VisitedLinkColor = UpdatesForm.Blue;
			Status.LinkClicked += (s, e) => UpdatesForm.ShowFor(this);
			Controls.Add(Status);
			GameData.TextAlign = ContentAlignment.MiddleLeft;
			Controls.Add(GameData);
			GameData.Beside(Status, atRowEnd: true);   // right of "Ready" (1.7.1)

			void Row(string text, int y, Label count, Label max)
			{
				Controls.Add(new Label { Text = text, Bounds = new Rectangle(Right_, y, 80, 18) });
				count.SetBounds(Right_ + 80, y, 52, 18);
				count.TextAlign = ContentAlignment.TopRight;
				count.Text = "0";
				Controls.Add(count);
				if (max == null) return;
				Controls.Add(new Label { Text = "/", Bounds = new Rectangle(Right_ + 134, y, 12, 18) });
				max.SetBounds(Right_ + 146, y, 60, 18);
				max.Text = "?";
				Controls.Add(max);
			}
			Row("Weapons", 124, weaponCount, weaponMax);
			Row("Artifacts", 142, artifactCount, artifactMax);
			Row("Characters", 160, characterCount, null);

			Caption(this, "Weapon / artifact", Right_, 186);
			gearPicture.SetBounds(Right_, 204, 140, 90);
			gearPicture.SizeMode = PictureBoxSizeMode.Zoom;
			gearPicture.BorderStyle = BorderStyle.FixedSingle;
			Controls.Add(gearPicture);
			OutputBox(gearText, new Rectangle(Right_, 298, 140, 100), "Weapon or artifact being read");
			Caption(this, "Character", Right_ + 148, 186);
			characterPicture.SetBounds(Right_ + 148, 204, 140, 30);
			characterPicture.SizeMode = PictureBoxSizeMode.Zoom;
			characterPicture.BorderStyle = BorderStyle.FixedSingle;
			Controls.Add(characterPicture);
			OutputBox(characterText, new Rectangle(Right_ + 148, 238, 140, 160), "Character being read");

			ErrorTitle.SetBounds(Right_, 406, 140, 18);
			ErrorTitle.Text = AdvancedText.ErrorLog(0);
			Controls.Add(ErrorTitle);
			var issues = new LinkLabel { Text = "Got errors? Issue page", TextAlign = ContentAlignment.TopRight, Bounds = new Rectangle(Right_ + 140, 406, 148, 18) };
			issues.LinkClicked += (s, e) => Process.Start(ForkRepository.IssuesUrl);
			Controls.Add(issues);
			OutputBox(ErrorLog, new Rectangle(Right_, 424, RightWidth, 92), "Error log");
			ErrorLog.ForeColor = UserInterface.ErrorColor;

			var openFolder = new Button { Text = "Open export folder", Bounds = new Rectangle(Right_, 524, 126, 28) };
			openFolder.Click += (s, e) => ScanWindows.OpenFolder(OutputPath.Text);
			Controls.Add(openFolder);
			OpenOptimizer.Text = "Open Genshin Optimizer";
			OpenOptimizer.SetBounds(Right_ + 132, 524, 156, 28);
			OpenOptimizer.Enabled = ScanSession.Data?.HasData == true;
			tips.SetToolTip(OpenOptimizer, "Copies the last scan and opens Genshin Optimizer. Available after a scan.");
			OpenOptimizer.Click += (s, e) => ScanWindows.OpenOptimizer(new GOOD(ScanSession.Data));
			Controls.Add(OpenOptimizer);
		}

		private void OutputBox(TextBox box, Rectangle bounds, string name)
		{
			box.Multiline = true;
			box.ReadOnly = true;
			box.ScrollBars = ScrollBars.Vertical;
			box.Bounds = bounds;
			box.AccessibleName = name;
			box.BackColor = SystemColors.Window;
			Controls.Add(box);
		}

		#endregion Building the window

		#region Highlight (Simple's "Change" links)

		// Which box Simple's "Change" link was under.
		internal enum Section { Weapons, Artifacts, Characters }

		// Mockup pick C: pale Windows selection blue inside and a 2 px accent-blue outline, for 1.5 s, then
		// straight off (no fade, so it's the same with reduce motion on).
		internal static readonly Color HighlightFill = Color.FromArgb(229, 240, 251), HighlightOutline = Color.FromArgb(0, 103, 192);
		internal const int HighlightMs = 1500;

		private readonly Timer highlightTimer = new Timer { Interval = HighlightMs };

		internal GroupBox Highlighted { get; private set; }

		internal GroupBox GroupFor(Section section) =>
			section == Section.Weapons ? weaponsGroup : section == Section.Artifacts ? artifactsGroup : charactersGroup;

		// Lights up that box and puts the keyboard focus on its first option, which stays there.
		internal void Highlight(Section section)
		{
			EndHighlight();
			Highlighted = GroupFor(section);
			Highlighted.BackColor = HighlightFill;
			Highlighted.Invalidate();
			ActiveControl = section == Section.Weapons ? WeaponRarity
				: section == Section.Artifacts ? (Control)ArtifactRarity
				: CharacterCount.OnlyChoice.Checked ? CharacterCount.OnlyChoice : CharacterCount.AllChoice;
			highlightTimer.Tick -= HighlightTimerTick;
			highlightTimer.Tick += HighlightTimerTick;
			highlightTimer.Start();
		}

		private void HighlightTimerTick(object sender, EventArgs e) => EndHighlight();

		internal void EndHighlight()
		{
			highlightTimer.Stop();
			if (Highlighted == null) return;
			Highlighted.BackColor = Color.Empty;   // back to the window's own colour
			Highlighted.Invalidate();
			Highlighted = null;
		}

		private void PaintHighlightOutline(object sender, PaintEventArgs e)
		{
			if (sender != Highlighted) return;
			var box = (GroupBox)sender;
			int top = box.Font.Height / 2, right = box.Width - 2, bottom = box.Height - 2;
			// A gap behind the title, as in the box's own border.
			int titleStart = 6, titleEnd = titleStart + TextRenderer.MeasureText(box.Text, box.Font).Width + 2;
			using (var pen = new Pen(HighlightOutline, 2))
			{
				e.Graphics.DrawLine(pen, 1, top, titleStart, top);
				e.Graphics.DrawLine(pen, titleEnd, top, right, top);
				e.Graphics.DrawLine(pen, 1, top, 1, bottom);
				e.Graphics.DrawLine(pen, right, top, right, bottom);
				e.Graphics.DrawLine(pen, 1, bottom, right, bottom);
			}
		}

		#endregion Highlight (Simple's "Change" links)

		// White on blue (5.6:1); greyed while a scan runs.
		private void StyleScanButton()
		{
			scan.BackColor = scan.Enabled ? Color.FromArgb(0, 103, 192) : Color.FromArgb(220, 220, 220);
			scan.ForeColor = scan.Enabled ? Color.White : SystemColors.GrayText;
		}

		// The scan speed isn't a plain binding (the list is in choice order, Auto first).
		private void ShowScanSpeed()
		{
			int choice = ScanSpeeds.ChoiceOf(ScanSpeeds.Valid(Settings.ScanSpeed));
			if (ScanSpeed.SelectedIndex != choice) ScanSpeed.SelectedIndex = choice;
		}

		private void SettingChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(Properties.Settings.ScanSpeed) && !IsDisposed) ShowScanSpeed();
		}

		// Yellow when a name is another character's (as in Classic); its own default and an empty box stay white.
		private void CheckName(TextBox box, string key)
		{
			bool clash = !string.IsNullOrWhiteSpace(box.Text) && GenshinProcesor.NameBelongsToAnotherCharacter(box.Text, key);
			box.BackColor = clash ? Color.Yellow : SystemColors.Window;
			tips.SetToolTip(box, clash
				? $"{box.Text} already exists as a character's name.\nThis may affect equipping items to characters and is not fully supported yet."
				: key == "traveler" ? "Empty: read it from the game" : null);
		}

		#region The scan

		private void StartListening()
		{
			UserInterface.Init(gearPicture, gearText, characterPicture, null, null, characterText,
				weaponCount, weaponMax, artifactCount, artifactMax, characterCount, null, ErrorLog, null);
			if (listening) return;
			listening = true;
			UserInterface.StatusChanged += OnScanChanged;
			UserInterface.ErrorsChanged += OnScanChanged;
		}

		internal void StopListening()
		{
			if (!listening) return;
			listening = false;
			UserInterface.StatusChanged -= OnScanChanged;
			UserInterface.ErrorsChanged -= OnScanChanged;
		}

		private void OnScanChanged()
		{
			if (!listening || IsDisposed) return;
			if (!InvokeRequired) { ShowScan(); return; }
			try { BeginInvoke((MethodInvoker)ShowScan); }
			catch (InvalidOperationException) { }   // closing: nothing left to show
		}

		// The counters, preview and error log are filled by UserInterface itself; the status line and the
		// error count come from its events.
		internal void ShowScan()
		{
			if (IsDisposed) return;
			string status = UserInterface.Status == Looks.HintText ? "" : UserInterface.Status;
			Status.Text = string.IsNullOrEmpty(status) ? "Ready" : status;
			// "Updates available · Open Updates": the link opens the Updates window (mockup 2A).
			if (UpdateNotice.IsNoticeText(status))
			{
				Status.Text = status + " · " + Updates.OpenLink;
				Status.LinkArea = new LinkArea(status.Length + 3, Updates.OpenLink.Length);
			}
			else Status.LinkArea = new LinkArea(0, 0);
			Status.ForeColor = string.IsNullOrEmpty(status) || UserInterface.StatusColor.IsEmpty ? SystemColors.ControlText : UserInterface.StatusColor;
			ErrorTitle.Text = AdvancedText.ErrorLog(UserInterface.ErrorCount);
			weaponCount.Text = UserInterface.Weapons.ToString();
			weaponMax.Text = UserInterface.WeaponsMax < 0 ? "?" : UserInterface.WeaponsMax.ToString();
			artifactCount.Text = UserInterface.Artifacts.ToString();
			artifactMax.Text = UserInterface.ArtifactsMax < 0 ? "?" : UserInterface.ArtifactsMax.ToString();
			characterCount.Text = UserInterface.Characters.ToString();
		}

		private void Scan_Click(object sender, EventArgs e)
		{
			ScanSession.Start(OutputPath.Text, this);
			if (!ScanSession.IsRunning) { ShowScan(); return; }   // not started: the status or error log says why
			scan.Enabled = false;
			scan.Text = "Scanning…";
			OpenOptimizer.Enabled = false;
			ShowScan();
		}

		#endregion The scan

		// ILookWindow
		public void LookupTablesReloaded()
		{
			foreach (var (box, _, key) in Names) CheckName(box, key);
			GameData.ShowData();
		}

		public void BecameShown()
		{
			StartListening();
			GameData.ShowData();
			ShowScanSpeed();
			ShowScan();   // catches up with anything that happened while hidden
			OpenOptimizer.Enabled = ScanWindows.CanOpenOptimizer;
		}

		public void BecameHidden() => StopListening();

		// IScanWindow
		public Control Invoker => this;

		public void ScanEnded(string title, string message, MessageBoxIcon icon) =>
			ScanWindows.ShowScanEnded(this, title, message, icon);

		public void ScanStopped(bool hasData)
		{
			BeginInvoke((MethodInvoker)delegate
			{
				scan.Enabled = true;
				scan.Text = "Scan Genshin";
				OpenOptimizer.Enabled = hasData;
				ShowScan();
				Activate();
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				StopListening();
				Settings.PropertyChanged -= SettingChanged;
				highlightTimer.Dispose();
				SpeedTips?.Dispose();
				tips.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
