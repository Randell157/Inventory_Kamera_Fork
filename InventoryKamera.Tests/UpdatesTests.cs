using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7 step 5: the update rules and the Updates window. Offline: every answer from GitHub or GitLab is
	// canned here; nothing is downloaded.
	[TestClass]
	public sealed class UpdatesTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 22, 15, 12, 0);

		private sealed class Canned : IUpdateSource
		{
			internal string Tag = "v1.7.1";
			internal Exception AppFails, DataFails;
			internal Version Remote = new Version(7, 3);
			public Version Current => new Version(1, 7, 0, 6734);
			public Task<ReleaseInfo> LatestReleaseAsync() =>
				AppFails != null ? throw AppFails
				: Task.FromResult(new ReleaseInfo { Tag = Tag, Published = new DateTime(2026, 12, 3, 18, 0, 0, DateTimeKind.Utc), Notes = "## What's new\n- **Simple** shows what it scans\n- See [the issue](https://example.invalid/1)", Url = "https://github.com/x/y/releases/tag/" + Tag });
			public Version LocalGameData => new Version(7, 2);
			public Task<Version> RemoteGameDataAsync() => DataFails != null ? throw DataFails : Task.FromResult(Remote);
		}

		[TestMethod]
		public void VersionsFromTags()
		{
			Assert.AreEqual(new Version(1, 7, 1), Updates.TagVersion("v1.7.1"));
			Assert.AreEqual(new Version(1, 7, 0), Updates.TagVersion("Inventory Kamera 1.7"));
			Assert.IsNull(Updates.TagVersion("latest"));
			Assert.IsNull(Updates.TagVersion(""));
			Assert.IsTrue(Updates.IsNewer(new Version(1, 7, 1), new Version(1, 7, 0, 6734)));
			Assert.IsFalse(Updates.IsNewer(new Version(1, 7, 0), new Version(1, 7, 0, 6734)), "the build number doesn't count");
			Assert.IsFalse(Updates.IsNewer(new Version(1, 6, 1), new Version(1, 7, 0)));
			Assert.IsFalse(Updates.IsNewer(null, new Version(1, 7, 0)));
		}

		[TestMethod]
		public void OnceADay()
		{
			Assert.IsTrue(Updates.CheckDue(true, new DateTime(1000, 1, 1), Now), "never checked");
			Assert.IsTrue(Updates.CheckDue(true, Now.AddHours(-25), Now));
			Assert.IsFalse(Updates.CheckDue(true, Now.AddHours(-23), Now), "checked earlier today");
			Assert.IsTrue(Updates.CheckDue(true, Now.AddDays(3), Now), "a check in the future: the clock was changed");
			Assert.IsFalse(Updates.CheckDue(false, new DateTime(1000, 1, 1), Now), "switched off");
		}

		// Your plan (2026-11-22): "Remind me later" means the next launch after 24 hours. Walks the launch
		// logic through the clock: CheckDue decides whether a launch checks; a check moves LastUpdateCheck;
		// ShowApp / ShowData decide whether the notice (button and popup) shows.
		[TestMethod]
		public void RemindMeLaterComesBackAtTheNextLaunchAfterItsTime()
		{
			var day1 = new DateTime(2026, 11, 22);
			DateTime lastCheck = day1.AddHours(17);              // the launch check at 17:00
			DateTime remind = day1.AddHours(18) + Updates.Daily;  // Remind me later clicked at 18:00
			var never = DateTime.MinValue;
			var result = Result();

			bool Launch(DateTime at, bool auto = true, DateTime? dataRemind = null)
			{
				if (!Updates.CheckDue(auto, lastCheck, at, remind, dataRemind ?? never)) return false;
				lastCheck = at;   // the check runs
				return Updates.ShowApp(result, "", remind, at);
			}

			Assert.IsFalse(Launch(day1.AddDays(1).AddHours(17.5)), "next day 17:30: it checks (a day passed) but the reminder hides it");
			Assert.AreEqual(day1.AddDays(1).AddHours(17.5), lastCheck, "that launch did check");
			Assert.IsTrue(Launch(day1.AddDays(1).AddHours(18) + TimeSpan.FromMinutes(5)), "18:05: the reminder has passed, so it checks again and shows");
			Assert.IsFalse(Updates.CheckDue(true, lastCheck, lastCheck.AddHours(2), remind, never), "after that check the reminder no longer forces checks");

			// Game data's own reminder works the same way.
			lastCheck = day1.AddHours(17);
			var dataReminder = day1.AddHours(18) + Updates.Daily;
			Assert.IsTrue(Updates.CheckDue(true, day1.AddDays(1).AddHours(17.5), day1.AddDays(1).AddHours(18.1), never, dataReminder), "game data reminder passed: due");
		}

		[TestMethod]
		public void WithoutAReminderStillOnceADayAndOffMeansOff()
		{
			var never = DateTime.MinValue;
			var lastCheck = Now.AddHours(-3);
			Assert.IsFalse(Updates.CheckDue(true, lastCheck, Now, never, never), "no reminder: not again within a day");
			Assert.IsFalse(Updates.CheckDue(true, lastCheck, Now, Now.AddHours(-5), never), "a reminder that ended before the last check isn't waiting");
			Assert.IsFalse(Updates.CheckDue(true, lastCheck, Now, Now.AddHours(2), never), "a reminder still in the future: not yet");
			Assert.IsTrue(Updates.CheckDue(true, Now.AddHours(-25), Now, never, never), "a day later: due as before");
			Assert.IsFalse(Updates.CheckDue(false, lastCheck, Now, Now.AddHours(-1), never), "Check once a day off: no launch check, reminder or not");
			Assert.IsFalse(Updates.CheckDue(false, Now.AddDays(-5), Now, never, never), "off");
		}

		// Skip this version sets no reminder, so it changes nothing about when checks run, and the skipped
		// version never shows while the next one does.
		[TestMethod]
		public void SkipIsUnaffectedByTheReminderFix()
		{
			var lastCheck = Now.AddHours(-3);
			Assert.IsFalse(Updates.CheckDue(true, lastCheck, Now, DateTime.MinValue, DateTime.MinValue));
			Assert.IsFalse(Updates.ShowApp(Result("v1.7.1"), "v1.7.1", DateTime.MinValue, Now.AddDays(10)), "skipped: never comes back");
			Assert.IsTrue(Updates.ShowApp(Result("v1.7.2"), "v1.7.1", DateTime.MinValue, Now), "the next version does");
		}

		// Live 2026-11-22: a launch on a day the check had already run showed no button. Each check's result is
		// now saved, and a launch without a check brings the button back from it.
		[TestMethod]
		public void SavedCheckRoundTripsAndComparesWithThisPc()
		{
			var r = Result();
			r.AssetUrl = "https://github.com/Randell157/Inventory_Kamera_Fork/releases/download/v1.7.1/Inventory.Kamera.v1.7.1.zip";
			r.AssetDigest = "sha256:" + new string('a', 64);
			r.AssetSize = 24421178;
			string json = UpdateNotice.Serialize(r);

			var back = UpdateNotice.Deserialize(json, new Version(1, 7, 0, 39012), new Version(7, 2));
			Assert.AreEqual(r.LatestTag, back.LatestTag);
			Assert.AreEqual(r.AssetUrl, back.AssetUrl, "Update… can download from the saved result");
			Assert.AreEqual(r.AssetDigest, back.AssetDigest);
			Assert.AreEqual(r.AssetSize, back.AssetSize);
			Assert.AreEqual(r.Notes, back.Notes);
			Assert.AreEqual(r.Published, back.Published);
			Assert.AreEqual(new Version(7, 3), back.DataRemote);
			Assert.IsTrue(Updates.ShowApp(back, "", DateTime.MinValue, Now), "still 1.7.0 here: the button comes back");
			Assert.IsTrue(Updates.ShowData(back, DateTime.MinValue, Now));

			var updated = UpdateNotice.Deserialize(json, new Version(1, 7, 1, 1), new Version(7, 3));
			Assert.IsFalse(Updates.ShowApp(updated, "", DateTime.MinValue, Now), "after updating to that version: nothing");
			Assert.IsFalse(Updates.ShowData(updated, DateTime.MinValue, Now), "game data updated since: nothing");

			Assert.IsFalse(Updates.ShowApp(back, "v1.7.1", DateTime.MinValue, Now), "Skip still hides it");
			Assert.IsFalse(Updates.ShowApp(back, "", Now.AddHours(3), Now), "Remind me later still hides it");

			foreach (var damaged in new[] { null, "", "   ", "{not json", "null", "{}", "[1,2]" })
				Assert.IsNull(UpdateNotice.Deserialize(damaged, new Version(1, 7, 0), new Version(7, 2)), $"damaged: {damaged ?? "null"}");
		}

		// A check saves its result; a launch on a day the check isn't due restores it, sets the button and asks
		// nothing (the popup is only called after a real check, which this launch doesn't run).
		[TestMethod]
		public async Task LaunchWithoutACheckBringsTheButtonBack()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				settings.AutoCheckUpdates = true;
				await UpdateNotice.CheckNowAsync(new Canned());   // yesterday's check, say
				Assert.IsFalse(string.IsNullOrEmpty(settings.LastUpdateResult), "the check was saved");
				Assert.AreEqual("v1.7.1", UpdateNotice.Deserialize(settings.LastUpdateResult, new Version(1, 7, 0), new Version(7, 2)).LatestTag);

				UpdateNotice.Reset();   // the app closes
				Assert.IsFalse(UpdateNotice.Any);
				var earlier = DateTime.Now.AddHours(-2);
				settings.LastUpdateCheck = earlier;   // checked earlier today: the launch won't check
				if (AppStartup.Database == null || AppStartup.Database.TablesMissing)
					Assert.Inconclusive("the sandbox has no lookup tables; the launch would try to download them");
				await AppStartup.RunChecksAsync(new CountingWindow());
				Assert.AreEqual(earlier, settings.LastUpdateCheck, "no new check ran");
				Assert.IsNotNull(UpdateNotice.Last, "restored at launch");
				Assert.AreEqual("v1.7.1", UpdateNotice.Last.LatestTag);
				Assert.AreEqual(AppStartup.Database.LocalVersion, UpdateNotice.Last.DataLocal, "compared with this PC's game data");
			}
			finally
			{
				UpdateNotice.Reset();
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
			}
		}

		// Not a Form: even if the launch wrongly tried the popup, it couldn't block the test.
		private sealed class CountingWindow : ILookWindow
		{
			public void LookupTablesReloaded() { }
			public void BecameShown() { }
			public void BecameHidden() { }
		}

		private static UpdateResult Result(string tag = "v1.7.1", Version remote = null) => new UpdateResult
		{
			CheckedAt = Now, Current = new Version(1, 7, 0, 6734), LatestTag = tag, Notes = "What's new", ReleaseUrl = "https://example.invalid/release",
			Published = new DateTime(2026, 12, 3, 18, 0, 0, DateTimeKind.Utc),
			DataLocal = new Version(7, 2), DataRemote = remote ?? new Version(7, 3),
		};

		[TestMethod]
		public void RemindAndSkip()
		{
			var never = new DateTime(1000, 1, 1);
			Assert.IsTrue(Updates.ShowApp(Result(), "", never, Now));
			Assert.IsFalse(Updates.ShowApp(Result(), "1.7.1", never, Now), "skipped (with or without the v)");
			Assert.IsTrue(Updates.ShowApp(Result("v1.7.2"), "v1.7.1", never, Now), "a newer one than the skipped one is shown");
			Assert.IsFalse(Updates.ShowApp(Result(), "", Now.AddHours(20), Now), "remind me later: not until tomorrow");
			Assert.IsTrue(Updates.ShowApp(Result(), "", Now.AddHours(-1), Now));
			Assert.IsFalse(Updates.ShowApp(Result("v1.7.0"), "", never, Now), "same version");
			Assert.IsFalse(Updates.ShowApp(new UpdateResult { Current = new Version(1, 7, 0), LatestTag = "v1.8.0", AppError = "offline" }, "", never, Now), "a failed check shows nothing");

			Assert.IsTrue(Updates.ShowData(Result(), never, Now));
			Assert.IsFalse(Updates.ShowData(Result(remote: new Version(7, 2)), never, Now), "same game version");
			Assert.IsFalse(Updates.ShowData(Result(), Now.AddHours(5), Now));
		}

		[TestMethod]
		public void Texts()
		{
			Assert.AreEqual("Updates available", Updates.StatusText(true, true));
			Assert.AreEqual("App update available", Updates.StatusText(true, false));
			Assert.AreEqual("New game data available", Updates.StatusText(false, true));
			Assert.AreEqual("", Updates.StatusText(false, false));
			Assert.IsTrue(UpdateNotice.IsNoticeText("New game data available"));
			Assert.IsFalse(UpdateNotice.IsNoticeText("Scanning"));

			string nl = Environment.NewLine;
			Assert.AreEqual("What's new" + nl + "• Simple shows what it scans" + nl + "• See the issue",
				Updates.PlainNotes("## What's new\r\n- **Simple** shows what it scans\r\n* See [the issue](https://example.invalid/1)\r\n![shot](x.png)"));
			Assert.AreEqual("No release notes.", Updates.PlainNotes(null));

			Assert.AreEqual("Last checked: today, 3:12 PM", Updates.LastCheckedText(Now, Now.AddMinutes(30)));
			Assert.AreEqual("Last checked: yesterday, 3:12 PM", Updates.LastCheckedText(Now, Now.AddDays(1)));
			Assert.AreEqual("Last checked: Nov 22, 3:12 PM", Updates.LastCheckedText(Now, Now.AddDays(5)));
			Assert.AreEqual("Not checked yet", Updates.LastCheckedText(new DateTime(1000, 1, 1), Now));
		}

		[TestMethod]
		public async Task ACheckNeverThrows()
		{
			var ok = await Updates.CheckAsync(new Canned(), Now);
			Assert.IsTrue(ok.AppNewer && ok.DataNewer);
			Assert.AreEqual(new Version(1, 7, 1), ok.Latest);
			StringAssert.StartsWith(ok.Notes, "What's new");

			var offline = await Updates.CheckAsync(new Canned { AppFails = new System.Net.Http.HttpRequestException("x"), DataFails = new System.Net.WebException("x") }, Now);
			Assert.AreEqual("Couldn't reach GitHub (no internet?). Try Check now later.", offline.AppError);
			Assert.AreEqual("Couldn't reach GitLab (no internet?). Try Check now later.", offline.DataError);
			Assert.IsFalse(offline.AppNewer || offline.DataNewer);
			Assert.AreEqual(new Version(7, 2), offline.DataLocal, "what you have is still shown");
		}

		private static bool Shown(Control control) =>
			(bool)typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { 2 });

		private static async Task WithWindow(Func<UpdatesForm, Properties.Settings, Task> test, UpdateResult canned, (bool, string) dataAnswer = default)
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				var settings = Properties.Settings.Default;
				settings.LastUpdateCheck = Now;
				using (var window = new UpdatesForm(() => Task.FromResult(canned), force => Task.FromResult(dataAnswer), () => Now))
				{
					SimpleLookTests.Bind(window);
					await test(window, settings);
				}
			}
			finally
			{
				UpdateNotice.Reset();
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
			}
		}

		[TestMethod]
		public Task WindowShowsBothUpdatesAndRemembersChoices() => WithWindow(async (w, s) =>
		{
			Assert.AreEqual("Checking…", w.AppMessage.Text, "before the first check");
			await w.RunCheckAsync();
			Assert.IsTrue(Shown(w.AppBadge) && Shown(w.DataBadge), "New on both");
			Assert.AreEqual("1.7.0", w.AppYouHave.Text);
			StringAssert.StartsWith(w.AppLatest.Text, "1.7.1 · released Dec 3, 2026");
			Assert.IsTrue(Shown(w.UpdateApp) && Shown(w.Skip) && Shown(w.AppLater) && Shown(w.Notes));
			Assert.AreEqual("Genshin Impact 7.2", w.DataHave.Text);
			Assert.AreEqual("7.3", w.DataNewest.Text);
			Assert.IsTrue(Shown(w.UpdateData) && Shown(w.DataLater));
			Assert.IsFalse(Shown(w.DownloadAgain));
			Assert.AreEqual("Last checked: today, 3:12 PM", w.LastChecked.Text);
			Assert.IsTrue(w.Daily.Checked, "once a day is on by default");

			w.SkipVersion();
			Assert.AreEqual("v1.7.1", s.SkippedAppVersion);
			StringAssert.StartsWith(w.AppMessage.Text, "Skipped 1.7.1.");
			Assert.IsFalse(Shown(w.Notes) || Shown(w.Skip));

			w.RemindData();
			Assert.AreEqual(Now + Updates.Daily, s.DataRemindAfter);
			Assert.IsFalse(Shown(w.UpdateData));
		}, Result());

		[TestMethod]
		public Task RemindMeLaterForTheApp() => WithWindow(async (w, s) =>
		{
			await w.RunCheckAsync();
			w.RemindApp();
			Assert.AreEqual(Now + Updates.Daily, s.AppRemindAfter);
			Assert.AreEqual("OK, it'll be mentioned again tomorrow.", w.AppMessage.Text);
			Assert.AreEqual("", s.SkippedAppVersion, "not skipped");
		}, Result());

		[TestMethod]
		public Task UpToDateAndGameDataUpdate() => WithWindow(async (w, s) =>
		{
			await w.RunCheckAsync();
			Assert.AreEqual("✓ This is the latest version.", w.AppMessage.Text);
			Assert.IsFalse(Shown(w.UpdateApp) || Shown(w.AppBadge));
			Assert.AreEqual("✓ Up to date.", w.DataMessage.Text);
			Assert.IsTrue(Shown(w.DownloadAgain));

			await w.UpdateGameDataAsync(force: true);   // Download again, with a canned answer
			Assert.AreEqual("✓ Updated to game data 7.2.", w.DataMessage.Text);
			Assert.IsFalse(w.Busy);
			Assert.IsTrue(w.CheckNow.Enabled && w.Close_.Enabled);
		}, Result("v1.7.0", new Version(7, 2)), (true, "✓ Updated to game data 7.2."));

		[TestMethod]
		public Task CouldntCheckIsShownNotPopped() => WithWindow(async (w, s) =>
		{
			await w.RunCheckAsync();
			StringAssert.StartsWith(w.AppMessage.Text, "Couldn't reach GitHub");
			Assert.AreEqual(UpdatesForm.Red, w.AppMessage.ForeColor);
			StringAssert.StartsWith(w.DataMessage.Text, "Couldn't reach GitLab");
			Assert.IsFalse(Shown(w.UpdateApp) || Shown(w.UpdateData) || Shown(w.DownloadAgain));
		}, new UpdateResult { CheckedAt = Now, Current = new Version(1, 7, 0), DataLocal = new Version(7, 2),
			AppError = "Couldn't reach GitHub (no internet?). Try Check now later.", DataError = "Couldn't reach GitLab (no internet?). Try Check now later." });

		[TestMethod]
		public void MenuBarButtonWords()
		{
			var r = Result();
			Assert.AreEqual("⬆ Update to 1.7.1", UpdateNotice.ButtonText(r, true, false));
			Assert.AreEqual("⬆ New game data 7.3", UpdateNotice.ButtonText(r, false, true));
			Assert.AreEqual("⬆ 2 updates", UpdateNotice.ButtonText(r, true, true));
			Assert.AreEqual("", UpdateNotice.ButtonText(r, false, false));
		}

		// 1.7.2 (mockup pick C): a blue button at the right end of the menu bar in every look, instead of the
		// status-line notice and the dot on Options. It fits beside the menus at every zoom.
		[TestMethod]
		public void MenuBarButtonInEveryLook()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				AppStartup.PrepareSettings();
				UserInterface.SetProgramStatus("");
				using (var simple = new SimpleForm())
				using (var advanced = new AdvancedForm())
				using (var classic = new MainForm())
				{
					foreach (var form in new Form[] { simple, advanced, classic })
					{
						SimpleLookTests.Bind(form);
						(form as ILookWindow).BecameShown();
						string name = form.GetType().Name;
						var menu = form.Controls.OfType<MenuStrip>().Single();
						var button = (ToolStripMenuItem)menu.Items[UpdateMark.ButtonName];
						var options = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Options");
						var updates = options.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Updates…");
						Assert.IsFalse(button.Available, name + ": no button before a check");
						Assert.IsNull(updates.Image, name + ": no dot before a check");

						UpdateNotice.Set(Result());   // what the launch check does
						Assert.IsTrue(button.Available, name + ": button after a check found something");
						Assert.AreEqual("⬆ 2 updates", button.Text, name);
						Assert.AreEqual(ToolStripItemAlignment.Right, button.Alignment, name);
						Assert.AreEqual("Options", options.Text, name + ": Options keeps its name (no dot room)");
						Assert.IsNotNull(updates.Image, name + ": dot on Updates…");
						if (form is SimpleForm s) Assert.AreEqual("Ready", s.Status.Text, "the status line isn't used any more");

						foreach (int step in Zoom.Steps.Concat(new[] { 100 }))
						{
							Zoom.Set(form, step);
							menu.PerformLayout();
							Assert.IsTrue(button.Bounds.Left >= options.Bounds.Right + 4, $"{name} at {step} %: button starts at {button.Bounds.Left}, Options ends at {options.Bounds.Right}");
							Assert.IsTrue(button.Bounds.Right <= menu.DisplayRectangle.Right, $"{name} at {step} %: button inside the menu bar");
							using (var bold = new Font(button.Font, FontStyle.Bold))
								Assert.IsTrue(TextRenderer.MeasureText(button.Text, bold).Width <= button.Width - 4, $"{name} at {step} %: bold text fits the button");
						}

						Properties.Settings.Default.SkippedAppVersion = "v1.7.1";
						Properties.Settings.Default.DataRemindAfter = DateTime.Now.AddDays(1);
						UpdateNotice.Refresh();
						Assert.IsFalse(button.Available, name + ": nothing left to mention");
						Assert.IsNull(updates.Image, name);
						Properties.Settings.Default.SkippedAppVersion = "";
						Properties.Settings.Default.DataRemindAfter = DateTime.MinValue;
						UpdateNotice.Set(null);   // no check yet (Reset would also drop the other windows' listeners)
						(form as ILookWindow).BecameHidden();
					}
				}
			}
			finally
			{
				UpdateNotice.Reset();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);   // Classic pointed the status line at its label
				UserInterface.SetProgramStatus("");
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
			}
		}

		// An old "Updates available" left on the status line (1.7.1 put it there) is cleared, never set again.
		[TestMethod]
		public void StatusLineNoLongerCarriesTheNotice()
		{
			UpdateNotice.Reset();
			try
			{
				UserInterface.SetProgramStatus("");
				UpdateNotice.Set(Result());
				Assert.AreEqual("", UserInterface.Status);
				UserInterface.SetProgramStatus("Updates available");
				UpdateNotice.Refresh();
				Assert.AreEqual("", UserInterface.Status);
			}
			finally { UpdateNotice.Reset(); UserInterface.SetProgramStatus(""); }
		}

		[TestMethod]
		public void PopupWordsAndButtons()
		{
			var r = Result();
			r.Notes = "Zoom fixes.\n\n## Zoom\n- **Checkbox squares grow with the zoom** in every look, drawn from Windows' own images so they stay sharp at any size you pick.\n- Classic: see the [README](https://example.invalid).\n* `Select` lines up\n- fourth bullet";
			r.AssetSize = 24421178;
			var notes = UpdatePromptText.NotesPreview(r.Notes);
			CollectionAssert.AreEqual(new[] {
				"Checkbox squares grow with the zoom in every look, drawn from Windows' own images so they…",
				"Classic: see the README.",
				"Select lines up" }, notes);
			Assert.IsTrue(notes.All(n => n.Length <= 90));

			var app = UpdatePromptText.Parts(r, true, false).Single();
			Assert.AreEqual("Inventory Kamera 1.7.1 is available", app.Heading);
			Assert.AreEqual("You have 1.7.0. What's new:", app.Text);
			Assert.AreEqual(3, app.Notes.Count);
			Assert.AreEqual("New game data for Genshin 7.3", UpdatePromptText.Parts(r, false, true).Single().Heading);
			Assert.AreEqual(2, UpdatePromptText.Parts(r, true, true).Count);
			StringAssert.Contains(UpdatePromptText.AppHint(r), "(23 MB)");

			CollectionAssert.AreEqual(new[] { "Skip this version", "Remind me later", "Update now" }, UpdatePromptText.Buttons(true, false));
			CollectionAssert.AreEqual(new[] { "Remind me later", "Update game data" }, UpdatePromptText.Buttons(false, true));
			CollectionAssert.AreEqual(new[] { "Skip this version", "Remind me later", "Open Updates" }, UpdatePromptText.Buttons(true, true));
			Assert.AreEqual("You have 1.7.0.", UpdatePromptText.Parts(new UpdateResult { Current = new Version(1, 7, 0), LatestTag = "v1.7.1", Notes = "" }, true, false)[0].Text, "no notes: no \"What's new\"");

			// Live 2026-11-22: real notes reach the popup already made plain by Updates.PlainNotes ("• " bullets),
			// so the popup showed none. Through the same path as a real check, with 1.7.1's real wording:
			string real = "Zoom fixes and the Genshin version on the main window.\r\n\r\n## Zoom\r\n- **Checkbox squares grow with the zoom** in every look.\r\n- **Classic:** the Weapons checkboxes now match Materials.\r\n\r\n## Game data version\r\n- Next to the Scan button, every look now shows which Genshin version the game data is for.\r\n";
			var plain = UpdatePromptText.NotesPreview(Updates.PlainNotes(real));
			Assert.AreEqual(3, plain.Count, string.Join(" | ", plain));
			Assert.AreEqual("Checkbox squares grow with the zoom in every look.", plain[0]);
			Assert.AreEqual("Classic: the Weapons checkboxes now match Materials.", plain[1]);
		}

		// Skip and Remind me later save what the Updates window's buttons save; Update now opens Updates and
		// starts the one-click update there without asking again; ✕ changes nothing.
		[TestMethod]
		public void PopupChoices()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				UpdateNotice.Set(Result());
				var opened = new List<Func<UpdatesForm, System.Threading.Tasks.Task>>();
				void Act(string choice, bool app, bool data) => UpdatePrompt.Act(null, choice, app, data, Now, (owner, start) => opened.Add(start));

				Act(null, true, true);
				Assert.AreEqual("", settings.SkippedAppVersion ?? "", "✕ changes nothing");
				Assert.AreEqual(0, opened.Count);

				Act("Remind me later", true, true);
				Assert.AreEqual(Now + Updates.Daily, settings.AppRemindAfter);
				Assert.AreEqual(Now + Updates.Daily, settings.DataRemindAfter);

				Act("Skip this version", true, false);
				Assert.AreEqual("v1.7.1", settings.SkippedAppVersion);

				Act("Update now", true, false);
				Act("Update game data", false, true);
				Act("Open Updates", true, true);
				Assert.AreEqual(3, opened.Count);
				Assert.IsNotNull(opened[0], "Update now starts something");
				Assert.IsNotNull(opened[1], "Update game data starts something");
				Assert.IsNull(opened[2], "Open Updates only opens it");

				// Update now's start marks the window as already confirmed (no second question).
				bool asked = false;
				using (var window = new UpdatesForm(() => System.Threading.Tasks.Task.FromResult(Result()), f => System.Threading.Tasks.Task.FromResult((true, "")), () => Now,
					(release, progress) => System.Threading.Tasks.Task.CompletedTask, text => { asked = true; return false; }, () => { }))
				{
					window.ShowResult(Result());
					opened[0](window).Wait(5000);
					Assert.IsTrue(window.AlreadyConfirmed);
					Assert.IsFalse(asked, "not asked again");
				}
			}
			finally
			{
				UpdateNotice.Reset();
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
			}
		}

		// The popup fits its text and buttons at 100 % and zoomed, with the main button last and blue.
		[TestMethod]
		public void PopupLayoutFits()
		{
			var r = Result();
			r.Notes = "- Checkbox squares grow with the zoom in every look\n- Classic: the Select button lines up with the path box\n- Every look shows the Genshin version";
			r.AssetSize = 24421178;
			foreach (var (app, data) in new[] { (true, false), (false, true), (true, true) })
				foreach (int step in new[] { 100, 175 })
					using (var prompt = new UpdatePrompt(r, app, data))
					{
						Zoom.Apply(prompt, step);
						string where = $"{(app ? "app" : "")}{(data ? "+data" : "")} at {step} %";
						var footer = prompt.Choices[0].Parent;
						foreach (Control c in prompt.Controls)
							Assert.IsTrue(c.Right <= prompt.ClientSize.Width && c.Bottom <= prompt.ClientSize.Height && c.Left >= 0, $"{where}: {c.Text} inside");
						for (int i = 0; i < prompt.Choices.Count; i++)
						{
							var b = prompt.Choices[i];
							Assert.IsTrue(b.Left >= 0 && b.Right <= footer.ClientSize.Width && b.Bottom <= footer.ClientSize.Height, $"{where}: {b.Text} inside the footer");
							Assert.IsTrue(TextRenderer.MeasureText(b.Text, b.Font).Width <= b.Width, $"{where}: {b.Text} text fits");
							if (i > 0) Assert.IsTrue(prompt.Choices[i - 1].Right < b.Left, $"{where}: {b.Text} after {prompt.Choices[i - 1].Text}");
						}
						Assert.AreEqual(UpdatesForm.Blue, prompt.Choices.Last().BackColor, where + ": main button blue");
						var labels = prompt.Controls.OfType<Label>().ToList();
						for (int i = 1; i < labels.Count; i++)
							Assert.IsTrue(labels[i - 1].Bottom <= labels[i].Top, $"{where}: \"{labels[i - 1].Text}\" overlaps the next line");
					}
		}
	}
}
