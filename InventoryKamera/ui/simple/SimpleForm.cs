using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The Simple look (1.7, mockup "Simple A, column"): what to scan, the Traveler's name, the export
	// folder, one Scan button and the progress. Every other option keeps its saved value and is still
	// used by the scan (ScanSession reads the settings); View → Classic shows them.
	public sealed class SimpleForm : Form, ILookWindow, IScanWindow
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		// Layout at 100 % (96 DPI): a 344 px wide client area with 12 px margins.
		private const int Edge = 12, Inner = 320;

		private readonly MenuStrip menu = new MenuStrip();
		internal readonly CheckBox Weapons = new CheckBox(), Artifacts = new CheckBox(), Characters = new CheckBox(), DevItems = new CheckBox(), Materials = new CheckBox();
		internal readonly TextBox TravelerName = new TextBox(), OutputPath = new TextBox();
		private readonly Button browse = new Button(), scan = new Button(), openFolder = new Button();
		internal readonly Button OpenOptimizer = new Button();
		internal readonly LinkLabel Status = new LinkLabel();
		internal readonly Label Counts = new Label();
		internal readonly ProgressBar Progress = new ProgressBar();
		internal readonly LinkLabel Errors = new LinkLabel();
		internal readonly LinkLabel WeaponsLine = new LinkLabel(), ArtifactsLine = new LinkLabel(), CharactersLine = new LinkLabel(), UseStandard = new LinkLabel();
		private readonly ToolTip tips = new ToolTip();
		private bool listening;

		public SimpleForm()
		{
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Segoe UI", 9F);
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			ClientSize = new Size(344, 556);
			Text = $"Inventory Kamera V{LookContext.VersionText}";
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

			BuildMenu();

			BuildWhatToScan();

			var game = Group("Your game", 236, 104);
			game.Controls.Add(new Label { Text = "Traveler's name", AutoSize = true, Location = new Point(10, 25) });
			TravelerName.SetBounds(112, 22, 196, 23);
			TravelerName.DataBindings.Add(new Binding("Text", Properties.Settings.Default, "TravelerName", true, DataSourceUpdateMode.OnPropertyChanged));
			TravelerName.TextChanged += (s, e) => CheckTravelerName();
			game.Controls.Add(TravelerName);
			game.Controls.Add(new Label { Text = "Empty: read it from the game", AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(110, 47) });
			game.Controls.Add(new Label { Text = "Save exports to", AutoSize = true, Location = new Point(10, 74) });
			OutputPath.SetBounds(112, 71, 120, 23);
			OutputPath.DataBindings.Add(new Binding("Text", Properties.Settings.Default, "OutputPath", true, DataSourceUpdateMode.OnPropertyChanged));
			game.Controls.Add(OutputPath);
			browse.Text = "Browse…";
			browse.SetBounds(236, 70, 72, 25);
			browse.Click += (s, e) => { var folder = ScanWindows.PickFolder(OutputPath.Text); if (folder != null) OutputPath.Text = folder; };
			game.Controls.Add(browse);

			scan.Text = "Scan Genshin";
			scan.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
			scan.SetBounds(Edge, 350, Inner, 40);
			scan.FlatStyle = FlatStyle.Flat;   // a plain button can't be colored; flat can (mockup: Windows accent blue)
			scan.FlatAppearance.BorderSize = 0;
			scan.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 117, 197);
			scan.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 166);
			scan.EnabledChanged += (s, e) => StyleScanButton();
			StyleScanButton();
			scan.Click += Scan_Click;
			Controls.Add(scan);
			Controls.Add(new Label { Text = "Press Enter in the game to stop a scan", ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(Edge, 392, Inner, 18) });

			Status.SetBounds(Edge, 416, Inner, 20);
			Status.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
			Status.LinkColor = Status.ActiveLinkColor = Status.VisitedLinkColor = UpdatesForm.Blue;
			Status.LinkClicked += (s, e) => UpdatesForm.ShowFor(this);
			Controls.Add(Status);
			Progress.SetBounds(Edge, 438, Inner, 14);
			Progress.Visible = false;
			Controls.Add(Progress);
			Counts.SetBounds(Edge, 454, Inner, 18);
			Counts.ForeColor = SystemColors.GrayText;
			Counts.Visible = false;
			Controls.Add(Counts);
			Errors.SetBounds(Edge, 474, Inner, 18);
			Errors.LinkColor = Errors.ActiveLinkColor = Errors.VisitedLinkColor = UserInterface.ErrorColor;
			Errors.Visible = false;
			Errors.LinkClicked += (s, e) => OpenLog();
			Controls.Add(Errors);

			Controls.Add(new Label { Text = Looks.MoreOptionsText, ForeColor = SystemColors.GrayText, Bounds = new Rectangle(Edge, 496, Inner, 18) });
			openFolder.Text = "Open export folder";
			openFolder.SetBounds(Edge, 518, 155, 28);
			openFolder.Click += (s, e) => ScanWindows.OpenFolder(OutputPath.Text);
			Controls.Add(openFolder);
			OpenOptimizer.Text = "Open Genshin Optimizer";
			OpenOptimizer.SetBounds(Edge + Inner - 160, 518, 160, 28);
			OpenOptimizer.Enabled = ScanSession.Data?.HasData == true;
			tips.SetToolTip(OpenOptimizer, "Copies the last scan and opens Genshin Optimizer. Available after a scan.");
			OpenOptimizer.Click += (s, e) => ScanWindows.OpenOptimizer(new GOOD(ScanSession.Data));
			Controls.Add(OpenOptimizer);

			ResumeLayout(true);

			// It listens for the scan only while it's on screen (BecameShown / BecameHidden).
			FormClosed += (s, e) => StopListening();
			FormClosing += (s, e) => { Properties.Settings.Default.Save(); ScanSession.RemoveHotkey(); };

			ShowScan();
		}

		private void BuildMenu()
		{
			var file = new ToolStripMenuItem("File");
			file.DropDownItems.Add("Quit", null, (s, e) => Application.Exit());
			var options = new ToolStripMenuItem("Options");
			var update = new ToolStripMenuItem("Updates…");
			update.Click += (s, e) => UpdatesForm.ShowFor(this);
			options.DropDownItems.Add(update);
			UpdateMark.Attach(this, options, update);
			options.DropDownItems.Add("Open Export Folder", null, (s, e) => ScanWindows.OpenFolder(OutputPath.Text));
			options.DropDownOpening += (s, e) => update.Enabled = !ScanSession.IsRunning;
			menu.Items.AddRange(new ToolStripItem[] { file, LookMenu.Build(Look.Simple, this), options });
			MainMenuStrip = menu;
			Controls.Add(menu);
		}

		// What to scan: the five checkboxes, a grey line under Weapons, Artifacts and Characters saying what
		// the hidden options will do ("4★ and up, any level · Change"), and "Use standard settings" when
		// something differs from the app's defaults (1.7, mockup 1A / 2A / 3A). The lines sit in a top-down
		// flow, so an unticked item's line takes no room.
		private void BuildWhatToScan()
		{
			var what = Group("What to scan", 32, 196);
			var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Bounds = new Rectangle(4, 18, 312, 156) };
			what.Controls.Add(flow);
			var lineFont = new Font("Segoe UI", 8.25F);
			foreach (var (box, text, setting, line, section) in new[] {
				(Weapons, "Weapons", "ScanWeapons", WeaponsLine, AdvancedForm.Section.Weapons),
				(Artifacts, "Artifacts", "ScanArtifacts", ArtifactsLine, AdvancedForm.Section.Artifacts),
				(Characters, "Characters", "ScanCharacters", CharactersLine, AdvancedForm.Section.Characters),
				(DevItems, "Character development items", "ScanCharDevItems", null, AdvancedForm.Section.Weapons),
				(Materials, "Materials", "ScanMaterials", null, AdvancedForm.Section.Weapons) })
			{
				box.Text = text;
				box.AutoSize = false;   // fixed rows: five checkboxes and three lines fit the box exactly
				box.Size = new Size(296, 20);
				box.Margin = new Padding(8, 1, 3, 0);
				box.DataBindings.Add(new Binding("Checked", Properties.Settings.Default, setting, true, DataSourceUpdateMode.OnPropertyChanged));
				flow.Controls.Add(box);
				if (line == null) continue;
				line.AutoSize = false;
				line.Size = new Size(280, 15);
				line.Font = lineFont;
				line.Margin = new Padding(26, 0, 3, 1);
				line.ForeColor = line.DisabledLinkColor = SystemColors.GrayText;
				line.LinkColor = line.ActiveLinkColor = line.VisitedLinkColor = Color.FromArgb(0, 103, 192);
				line.LinkClicked += (s, e) => OpenAdvancedAt(section);
				flow.Controls.Add(line);
			}

			UseStandard.Text = "Use standard settings";
			UseStandard.Font = lineFont;
			UseStandard.TextAlign = ContentAlignment.TopRight;
			UseStandard.Bounds = new Rectangle(10, 175, 300, 16);
			UseStandard.LinkColor = UseStandard.ActiveLinkColor = UseStandard.VisitedLinkColor = Color.FromArgb(0, 103, 192);
			tips.SetToolTip(UseStandard, StandardSettings.Tip);
			UseStandard.LinkClicked += (s, e) => StandardSettings.Apply(Properties.Settings.Default);
			what.Controls.Add(UseStandard);

			Properties.Settings.Default.PropertyChanged += SettingChanged;
			ShowSummaries();
		}

		// "Change": Advanced, with that box lit for a moment and the focus in it.
		private static void OpenAdvancedAt(AdvancedForm.Section section)
		{
			var context = LookContext.Current;
			if (context == null) return;
			context.SwitchTo(Look.Advanced);
			(context.Window as AdvancedForm)?.Highlight(section);
		}

		private void SettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (IsDisposed) return;
			if (InvokeRequired) BeginInvoke((MethodInvoker)ShowSummaries);
			else ShowSummaries();
		}

		// The lines and the reset link, from the settings as they are now (changed in any look).
		internal void ShowSummaries()
		{
			var settings = Properties.Settings.Default;
			bool idle = !ScanSession.IsRunning;
			void Line(LinkLabel line, bool on, string text)
			{
				line.Visible = on;
				line.Text = text + " · Change";
				line.LinkArea = new LinkArea(text.Length + 3, "Change".Length);
				line.Enabled = idle;
			}
			Line(WeaponsLine, settings.ScanWeapons, SimpleText.WeaponsSummary((int)settings.MinimumWeaponRarity, (int)settings.MinimumWeaponLevel));
			Line(ArtifactsLine, settings.ScanArtifacts, SimpleText.ArtifactsSummary((int)settings.MinimumArtifactRarity, (int)settings.MinimumArtifactLevel, (int)settings.SortByObtained));
			Line(CharactersLine, settings.ScanCharacters, SimpleText.CharactersSummary((int)settings.NumOfCharToScan));
			UseStandard.Visible = !StandardSettings.IsStandard(settings);
			UseStandard.Enabled = idle;
		}

		private GroupBox Group(string text, int top, int height)
		{
			var box = new GroupBox { Text = text, Bounds = new Rectangle(Edge, top, Inner, height) };
			Controls.Add(box);
			return box;
		}

		// This look shows no item images, text output, counters or error box: the status, counts and
		// errors come from UserInterface's events instead.
		private void StartListening()
		{
			UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			if (listening) return;
			listening = true;
			UserInterface.CountsChanged += OnScanChanged;
			UserInterface.StatusChanged += OnScanChanged;
			UserInterface.ErrorsChanged += OnScanChanged;
		}

		internal void StopListening()
		{
			if (!listening) return;
			listening = false;
			UserInterface.CountsChanged -= OnScanChanged;
			UserInterface.StatusChanged -= OnScanChanged;
			UserInterface.ErrorsChanged -= OnScanChanged;
		}

		// Raised on the scan thread: redraw on this window's thread.
		private void OnScanChanged()
		{
			if (!listening || IsDisposed) return;
			if (!InvokeRequired) { ShowScan(); return; }
			try { BeginInvoke((MethodInvoker)ShowScan); }
			catch (InvalidOperationException) { }   // closing: nothing left to show
		}

		private bool scanStarted;

		// Status always; the bar and counts once a scan has started from this window; the error link only with errors.
		internal void ShowScan()
		{
			if (IsDisposed) return;
			// Classic's one-time "New: View → Simple" hint means nothing here.
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

			var settings = Properties.Settings.Default;
			bool running = ScanSession.IsRunning;
			Progress.Visible = Counts.Visible = scanStarted;
			if (scanStarted)
			{
				Counts.Text = SimpleText.Counts(settings.ScanWeapons, UserInterface.Weapons, UserInterface.WeaponsMax,
					settings.ScanArtifacts, UserInterface.Artifacts, UserInterface.ArtifactsMax, settings.ScanCharacters, UserInterface.Characters);
				int percent = SimpleText.Percent(settings.ScanWeapons, UserInterface.Weapons, UserInterface.WeaponsMax,
					settings.ScanArtifacts, UserInterface.Artifacts, UserInterface.ArtifactsMax);
				if (!running) { Progress.Style = ProgressBarStyle.Continuous; Progress.Value = UserInterface.Status == "Finished" ? 100 : Progress.Value; }
				else if (percent < 0) Progress.Style = ProgressBarStyle.Marquee;
				else { Progress.Style = ProgressBarStyle.Continuous; Progress.Value = percent; }
			}

			string errors = SimpleText.ErrorLink(UserInterface.ErrorCount);
			Errors.Text = errors;
			Errors.Visible = errors.Length > 0;
		}

		// White on blue (5.6:1); greyed while a scan runs. Windows draws a disabled button's text gray itself.
		private void StyleScanButton()
		{
			scan.BackColor = scan.Enabled ? Color.FromArgb(0, 103, 192) : Color.FromArgb(220, 220, 220);
			scan.ForeColor = scan.Enabled ? Color.White : SystemColors.GrayText;
		}

		private void Scan_Click(object sender, EventArgs e)
		{
			ScanSession.Start(OutputPath.Text, this);
			if (!ScanSession.IsRunning) { ShowScan(); return; }   // not started: the status or error link says why
			scanStarted = true;
			Progress.Value = 0;
			scan.Enabled = false;
			scan.Text = "Scanning…";
			OpenOptimizer.Enabled = false;
			ShowScan();
			ShowSummaries();   // Change and "Use standard settings" wait until the scan ends
		}

		private void OpenLog()
		{
			var log = Path.Combine("logging", "InventoryKamera.log");
			try { Process.Start(File.Exists(log) ? log : "logging"); }
			catch (Exception ex) { Logger.Warn(ex, "Couldn't open the log"); }
		}

		// Yellow when the Traveler's name is another character's (as in Classic); empty is fine.
		private void CheckTravelerName()
		{
			bool clash = !string.IsNullOrWhiteSpace(TravelerName.Text) && GenshinProcesor.NameBelongsToAnotherCharacter(TravelerName.Text, "traveler");
			TravelerName.BackColor = clash ? Color.Yellow : SystemColors.Window;
			tips.SetToolTip(TravelerName, clash
				? $"{TravelerName.Text} already exists as a character's name.\nThis may affect equipping items to characters and is not fully supported yet."
				: null);
		}

		// ILookWindow
		public void LookupTablesReloaded() => CheckTravelerName();

		public void BecameShown()
		{
			StartListening();
			ShowScan();   // catches up with anything that happened while hidden
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
				ShowSummaries();
				Activate();
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				StopListening();
				Properties.Settings.Default.PropertyChanged -= SettingChanged;
				tips.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
