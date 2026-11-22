using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
			public Task<(string Tag, DateTime? Published, string Notes, string Url)> LatestReleaseAsync() =>
				AppFails != null ? throw AppFails
				: Task.FromResult((Tag, (DateTime?)new DateTime(2026, 12, 3, 18, 0, 0, DateTimeKind.Utc), "## What's new\n- **Simple** shows what it scans\n- See [the issue](https://example.invalid/1)", "https://github.com/x/y/releases/tag/" + Tag));
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
		public void StatusLinkAndOptionsDotInSimple()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				AppStartup.PrepareSettings();
				UserInterface.SetProgramStatus("");
				using (var form = new SimpleForm())
				{
					SimpleLookTests.Bind(form);
					form.BecameShown();
					var options = (ToolStripMenuItem)form.MainMenuStrip.Items[2];
					var updates = (ToolStripMenuItem)options.DropDownItems[0];
					Assert.AreEqual("Updates…", updates.Text);
					Assert.IsNull(updates.Image, "no dot before a check");

					UpdateNotice.Set(Result());   // what the launch check does
					Assert.AreEqual("Updates available · Open Updates", form.Status.Text);
					Assert.AreEqual("Open Updates", form.Status.Text.Substring(form.Status.LinkArea.Start, form.Status.LinkArea.Length), "only Open Updates is the link");
					Assert.AreEqual(true, options.Tag, "dot on Options");
					Assert.IsNotNull(updates.Image, "dot on Updates…");

					Properties.Settings.Default.SkippedAppVersion = "v1.7.1";
					Properties.Settings.Default.DataRemindAfter = DateTime.Now.AddDays(1);
					UpdateNotice.Refresh();
					Assert.AreEqual("Ready", form.Status.Text, "nothing left to mention");
					Assert.AreEqual(0, form.Status.LinkArea.Length);
					Assert.AreEqual(false, options.Tag);
					Assert.IsNull(updates.Image);
					form.BecameHidden();
				}
			}
			finally
			{
				UpdateNotice.Reset();
				UserInterface.SetProgramStatus("");
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}
	}
}
