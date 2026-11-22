using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InventoryKamera
{
	// Options → Updates… (1.7, mockup 1A): the app and the game data in one window. Inventory Kamera on top
	// (your version, the latest release and its notes; Skip this version / Remind me later / Update…), the
	// game data below (your tables' game version and the newest; Remind me later / Update game data, or
	// "Download again"); at the bottom when it last checked, "Check once a day by itself", Check now, Close.
	public sealed class UpdatesForm : Form
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		private static Properties.Settings Settings => Properties.Settings.Default;

		internal static readonly Color Blue = Color.FromArgb(0, 103, 192), Green = Color.FromArgb(0, 110, 0), Red = Color.FromArgb(192, 0, 0);

		private readonly Func<Task<UpdateResult>> check;
		private readonly Func<bool, Task<(bool Ok, string Message)>> updateGameData;
		private readonly Func<DateTime> now;
		private readonly Func<UpdateResult, IProgress<string>, Task> install;
		private readonly Func<string, bool> confirm;
		private readonly Action restart;

		// Inventory Kamera
		internal readonly Label AppBadge = Badge(), AppYouHave = new Label(), AppLatest = new Label(), AppMessage = new Label();
		internal readonly LinkLabel AppHint = new LinkLabel();   // under the buttons: what Update does, then its progress or why it stopped
		internal readonly TextBox Notes = new TextBox();
		internal readonly Button Skip = new Button(), AppLater = new Button(), UpdateApp = new Button();
		// Game data
		internal readonly Label DataBadge = Badge(), DataHave = new Label(), DataNewest = new Label(), DataMessage = new Label();
		internal readonly Button DataLater = new Button(), UpdateData = new Button();
		internal readonly LinkLabel DownloadAgain = new LinkLabel();
		// Footer
		internal readonly Label LastChecked = new Label();
		internal readonly CheckBox Daily = new CheckBox();
		internal readonly Button CheckNow = new Button(), Close_ = new Button();

		internal UpdateResult Result { get; private set; }
		internal bool Busy { get; private set; }

		public UpdatesForm() : this(() => UpdateNotice.CheckNowAsync(), AppStartup.UpdateGameDataAsync, () => DateTime.Now,
			(release, progress) => SelfUpdate.InstallAsync(release, Application.StartupPath, progress), null, RestartApp) { }

		internal UpdatesForm(Func<Task<UpdateResult>> check, Func<bool, Task<(bool Ok, string Message)>> updateGameData, Func<DateTime> now,
			Func<UpdateResult, IProgress<string>, Task> install = null, Func<string, bool> confirm = null, Action restart = null)
		{
			this.check = check;
			this.updateGameData = updateGameData;
			this.now = now;
			this.install = install;
			this.confirm = confirm ?? (text => MessageBox.Show(this, text, "Update Inventory Kamera", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK);
			this.restart = restart;

			SuspendLayout();
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Segoe UI", 9F);
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(504, 488);
			Text = "Updates";

			BuildApp();
			BuildData();
			BuildFooter();
			ResumeLayout(true);

			Shown += async (s, e) => { if (Result == null) await RunCheckAsync(); };
			ShowResult(UpdateNotice.Last);
		}

		// Opens the window over the open look (not during a scan), at the same zoom.
		internal static void ShowFor(Form owner)
		{
			if (ScanSession.IsRunning) return;
			using (var window = new UpdatesForm())
			{
				Zoom.Apply(window, Zoom.Current(owner));
				window.ShowDialog(owner);
			}
		}

		#region Building

		private static Label Badge() => new Label { Text = "New", AutoSize = true, BackColor = Blue, ForeColor = Color.White, Padding = new Padding(4, 0, 4, 1), Visible = false };

		private Panel Section(string title, Label badge, Rectangle bounds)
		{
			var panel = new Panel { Bounds = bounds, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
			var heading = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(31, 58, 110), Location = new Point(10, 8) };
			panel.Controls.Add(heading);
			badge.Location = new Point(10 + TextRenderer.MeasureText(title, heading.Font).Width + 4, 11);
			panel.Controls.Add(badge);
			Controls.Add(panel);
			return panel;
		}

		private static void Pair(Control parent, string caption, Label value, int y)
		{
			parent.Controls.Add(new Label { Text = caption, Bounds = new Rectangle(10, y, 100, 18) });
			value.SetBounds(112, y, 370, 18);
			parent.Controls.Add(value);
		}

		private static void Primary(Button button)
		{
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.BackColor = Blue;
			button.ForeColor = Color.White;
			button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
			button.EnabledChanged += (s, e) =>
			{
				button.BackColor = button.Enabled ? Blue : Color.FromArgb(220, 220, 220);
				button.ForeColor = button.Enabled ? Color.White : SystemColors.GrayText;
			};
		}

		private void BuildApp()
		{
			var app = Section("Inventory Kamera", AppBadge, new Rectangle(10, 10, 484, 270));
			Pair(app, "You have", AppYouHave, 36);
			Pair(app, "Latest", AppLatest, 56);
			Notes.Multiline = true;
			Notes.ReadOnly = true;
			Notes.ScrollBars = ScrollBars.Vertical;
			Notes.BackColor = Color.FromArgb(251, 251, 251);
			Notes.SetBounds(10, 80, 462, 110);
			Notes.AccessibleName = "Release notes";
			app.Controls.Add(Notes);
			AppMessage.SetBounds(10, 80, 462, 40);
			app.Controls.Add(AppMessage);

			Skip.Text = "Skip this version";
			Skip.SetBounds(150, 198, 120, 28);
			Skip.Click += (s, e) => SkipVersion();
			AppLater.Text = "Remind me later";
			AppLater.SetBounds(276, 198, 110, 28);
			AppLater.Click += (s, e) => RemindApp();
			UpdateApp.Text = "Update…";
			UpdateApp.SetBounds(392, 198, 80, 28);
			Primary(UpdateApp);
			UpdateApp.Click += async (s, e) => await InstallAppAsync();
			app.Controls.AddRange(new Control[] { Skip, AppLater, UpdateApp });
			AppHint.SetBounds(10, 230, 462, 34);
			AppHint.LinkColor = AppHint.ActiveLinkColor = AppHint.VisitedLinkColor = Blue;
			AppHint.LinkClicked += (s, e) => OpenRelease();
			Hint(UpdateHint, SystemColors.GrayText);
			app.Controls.Add(AppHint);
		}

		private void BuildData()
		{
			var data = Section("Game data", DataBadge, new Rectangle(10, 290, 484, 146));
			Pair(data, "Lookup tables", DataHave, 36);
			Pair(data, "Newest", DataNewest, 56);
			DataMessage.SetBounds(10, 80, 462, 34);
			data.Controls.Add(DataMessage);
			DownloadAgain.Text = "Download again";
			DownloadAgain.AutoSize = true;
			DownloadAgain.Location = new Point(10, 114);
			DownloadAgain.LinkClicked += async (s, e) => await UpdateGameDataAsync(force: true);
			data.Controls.Add(DownloadAgain);
			DataLater.Text = "Remind me later";
			DataLater.SetBounds(232, 110, 110, 28);
			DataLater.Click += (s, e) => RemindData();
			UpdateData.Text = "Update game data";
			UpdateData.SetBounds(348, 110, 124, 28);
			Primary(UpdateData);
			UpdateData.Click += async (s, e) => await UpdateGameDataAsync(force: false);
			data.Controls.AddRange(new Control[] { DataLater, UpdateData });
		}

		private void BuildFooter()
		{
			LastChecked.SetBounds(10, 448, 170, 18);
			LastChecked.ForeColor = SystemColors.GrayText;
			Controls.Add(LastChecked);
			Daily.Text = "Check once a day by itself";
			Daily.AutoSize = true;
			Daily.Location = new Point(10, 466);
			Daily.DataBindings.Add(new Binding("Checked", Settings, "AutoCheckUpdates", true, DataSourceUpdateMode.OnPropertyChanged));
			Controls.Add(Daily);
			CheckNow.Text = "Check now";
			CheckNow.SetBounds(324, 450, 90, 28);
			CheckNow.Click += async (s, e) => await RunCheckAsync();
			Controls.Add(CheckNow);
			Close_.Text = "Close";
			Close_.SetBounds(420, 450, 74, 28);
			Close_.DialogResult = DialogResult.Cancel;
			Controls.Add(Close_);
			CancelButton = Close_;
		}

		#endregion Building

		// Fills both parts from a check's result (null: not checked yet this launch).
		internal void ShowResult(UpdateResult result)
		{
			Result = result;
			var current = result?.Current ?? new OnlineUpdateSource().Current;
			AppYouHave.Text = current.ToString(3);
			LastChecked.Text = Updates.LastCheckedText(Settings.LastUpdateCheck, now());

			bool appNew = result != null && result.AppNewer;
			bool skipped = appNew && Equals(Updates.TagVersion(Settings.SkippedAppVersion), result.Latest);
			AppBadge.Visible = appNew && !skipped;
			AppLatest.Text = result?.LatestTag == null ? "" : $"{result.Latest?.ToString(3) ?? result.LatestTag}" + (result.Published == null ? "" : " · released " + Updates.DateText(result.Published));
			AppLatest.Font = new Font(Font, appNew ? FontStyle.Bold : FontStyle.Regular);
			Notes.Text = appNew ? result.Notes : "";
			Notes.Visible = appNew;
			Skip.Visible = AppLater.Visible = UpdateApp.Visible = AppHint.Visible = appNew;
			if (appNew) Hint(UpdateHint, SystemColors.GrayText);
			Skip.Enabled = !skipped;
			AppMessage.Visible = !appNew;
			if (result == null) Message(AppMessage, "Checking…", SystemColors.GrayText);
			else if (result.AppError != null) Message(AppMessage, result.AppError, Red);
			else if (!appNew) Message(AppMessage, "✓ This is the latest version.", Green);

			var local = result?.DataLocal ?? AppStartup.Database?.LocalVersion;
			DataHave.Text = local == null ? "None yet" : "Genshin Impact " + local.ToString(2);
			bool dataNew = result != null && result.DataNewer;
			DataBadge.Visible = dataNew;
			DataNewest.Text = result?.DataRemote == null ? "" : result.DataRemote.ToString(2);
			DataNewest.Font = new Font(Font, dataNew ? FontStyle.Bold : FontStyle.Regular);
			DataLater.Visible = UpdateData.Visible = dataNew;
			DownloadAgain.Visible = result != null && !dataNew && result.DataError == null;
			if (result == null) Message(DataMessage, "Checking…", SystemColors.GrayText);
			else if (result.DataError != null) Message(DataMessage, result.DataError, Red);
			else if (dataNew) Message(DataMessage, $"New characters, weapons and artifact sets for {result.DataRemote.ToString(2)}. Scans need it to name them. Downloaded from GitLab.", SystemColors.ControlText);
			else Message(DataMessage, "✓ Up to date.", Green);
		}

		private static void Message(Label label, string text, Color color)
		{
			label.Text = text;
			label.ForeColor = color;
		}

		private void SetBusy(bool busy)
		{
			Busy = busy;
			CheckNow.Enabled = Close_.Enabled = UpdateData.Enabled = DataLater.Enabled = DownloadAgain.Enabled = !busy;
			ControlBox = !busy;
		}

		internal async Task RunCheckAsync()
		{
			SetBusy(true);
			CheckNow.Text = "Checking…";
			try { ShowResult(await check()); }
			catch (Exception ex) { Logger.Warn(ex, "Update check failed"); }
			finally
			{
				CheckNow.Text = "Check now";
				SetBusy(false);
			}
		}

		internal void SkipVersion()
		{
			if (Result?.LatestTag == null) return;
			Settings.SkippedAppVersion = Result.LatestTag;
			Settings.Save();
			Logger.Info("Skipped app version {0}", Result.LatestTag);
			ShowResult(Result);
			Message(AppMessage, $"Skipped {Result.Latest?.ToString(3)}. You'll be told about the next version.", SystemColors.ControlText);
			AfterChoice(app: true);
			UpdateNotice.Refresh();
		}

		internal void RemindApp()
		{
			Settings.AppRemindAfter = now() + Updates.Daily;
			Settings.Save();
			Logger.Info("App update: remind me later (after {0})", Settings.AppRemindAfter);
			Message(AppMessage, "OK, it'll be mentioned again tomorrow.", SystemColors.ControlText);
			AfterChoice(app: true);
			UpdateNotice.Refresh();
		}

		internal void RemindData()
		{
			Settings.DataRemindAfter = now() + Updates.Daily;
			Settings.Save();
			Logger.Info("Game data update: remind me later (after {0})", Settings.DataRemindAfter);
			Message(DataMessage, "OK, it'll be mentioned again tomorrow. Scans may not name the newest items until then.", SystemColors.ControlText);
			DataLater.Visible = UpdateData.Visible = false;
			UpdateNotice.Refresh();
		}

		// After Skip or Remind me later the notes and buttons give way to a one-line answer.
		private void AfterChoice(bool app)
		{
			if (!app) return;
			Notes.Visible = Skip.Visible = AppLater.Visible = AppHint.Visible = false;
			AppMessage.Visible = true;
			AppBadge.Visible = false;
		}

		#region One-click update (step 6)

		internal const string UpdateHint = "Downloads it from GitHub, checks it, installs it and restarts.";
		internal const string ReleasePageLink = "Open the release page";

		internal static string ConfirmText(UpdateResult release) =>
			$"Download {release.Latest?.ToString(3) ?? release.LatestTag} ({SelfUpdate.SizeText(release.AssetSize)}), check it, replace this version and restart Inventory Kamera?" +
			Environment.NewLine + Environment.NewLine + "Your settings, game data, logs and exports are kept.";

		// The line under the buttons; a refusal ends with a link to the release page (manual download).
		private void Hint(string text, Color color, bool withReleaseLink = false)
		{
			AppHint.TextAlign = withReleaseLink ? ContentAlignment.TopLeft : ContentAlignment.TopRight;
			AppHint.Text = withReleaseLink ? text + " " + ReleasePageLink : text;
			AppHint.LinkArea = withReleaseLink ? new LinkArea(text.Length + 1, ReleasePageLink.Length) : new LinkArea(0, 0);
			AppHint.ForeColor = color;
		}

		internal bool Restarting { get; private set; }

		// Update…: confirm, then download, check, install and restart; or say why not (nothing replaced).
		internal async Task InstallAppAsync()
		{
			if (Result == null || ScanSession.IsRunning) return;
			try { SelfUpdate.CheckRelease(Result); }
			catch (UpdateRefused refused) { Refuse(refused.Message); return; }
			if (!confirm(ConfirmText(Result))) return;

			SetBusy(true);
			UpdateApp.Enabled = Skip.Enabled = AppLater.Enabled = false;
			Hint("Starting the download…", Color.FromArgb(146, 64, 14));
			var progress = new OnWindow(this, text => Hint(text, Color.FromArgb(146, 64, 14)));
			try
			{
				await install(Result, progress);
				Hint("Installed. Restarting…", Green);
				Logger.Info("Update to {0} installed; restarting", Result.LatestTag);
				Restarting = true;
				restart?.Invoke();
			}
			catch (UpdateRefused refused) { Refuse(refused.Message); }
			catch (Exception ex)
			{
				Logger.Error(ex, "Update failed");
				Refuse("The update didn't work (" + ex.Message + "). Nothing was changed.");
			}
			finally
			{
				if (!Restarting)
				{
					SetBusy(false);
					UpdateApp.Enabled = AppLater.Enabled = true;
					Skip.Enabled = !Equals(Updates.TagVersion(Settings.SkippedAppVersion), Result.Latest);
				}
			}
		}

		private void Refuse(string why)
		{
			Logger.Warn("Update not installed: {0}", why);
			Hint(why, Red, withReleaseLink: true);
		}

		// Progress reported from the download, shown on the window's thread.
		private sealed class OnWindow : IProgress<string>
		{
			private readonly Control window;
			private readonly Action<string> show;
			internal OnWindow(Control window, Action<string> show) { this.window = window; this.show = show; }
			public void Report(string value)
			{
				if (window.IsDisposed) return;
				if (window.InvokeRequired) window.BeginInvoke((MethodInvoker)(() => show(value)));
				else show(value);
			}
		}

		// Starts the new exe (it inherits this one's administrator rights, so no second prompt) and closes this one.
		private static void RestartApp()
		{
			Process.Start(new ProcessStartInfo(Path.Combine(Application.StartupPath, SelfUpdate.ExeName)) { WorkingDirectory = Application.StartupPath, UseShellExecute = false });
			Application.Exit();
		}

		#endregion One-click update (step 6)

		// GitHub's link when it's this repo's own page, otherwise the repo's releases list.
		internal static string ReleasePageUrl(string fromGitHub) =>
			ForkRepository.IsOwnPage(fromGitHub) ? fromGitHub : ForkRepository.ReleasesUrl;

		private void OpenRelease()
		{
			if (string.IsNullOrEmpty(Result?.ReleaseUrl)) return;
			string url = ReleasePageUrl(Result.ReleaseUrl);
			Logger.Info("Opening the release page for {0}: {1}", Result.LatestTag, url);
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception ex) { Logger.Warn(ex, "Couldn't open the release page"); }
		}

		internal async Task UpdateGameDataAsync(bool force)
		{
			SetBusy(true);
			Message(DataMessage, "Downloading game data… (this can take a minute)", Color.FromArgb(146, 64, 14));
			DownloadAgain.Visible = DataLater.Visible = UpdateData.Visible = false;
			try
			{
				var (ok, message) = await updateGameData(force);
				if (Result != null && ok) Result.DataLocal = AppStartup.Database?.LocalVersion ?? Result.DataRemote;
				ShowResult(Result);
				Message(DataMessage, message, ok ? Green : Red);
				UpdateNotice.Refresh();
			}
			finally { SetBusy(false); }
		}
	}
}
