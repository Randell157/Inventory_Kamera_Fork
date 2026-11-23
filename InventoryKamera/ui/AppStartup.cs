using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsInput.Native;

namespace InventoryKamera
{
	// A window of any look (Simple, Advanced, Classic) that the shared startup steps report back to.
	internal interface ILookWindow
	{
		// The lookup tables were just downloaded or updated and reloaded: re-check anything that uses them
		// (the custom-name boxes).
		void LookupTablesReloaded();

		// Each look keeps its window for the whole launch (LookContext hides the one you switch away from).
		// Shown: this window now shows the status, counts and errors (also after a zoom). Hidden: it stops.
		void BecameShown();
		void BecameHidden();
	}

	// What runs once per launch, whatever look is open: settings repairs before the first window is built,
	// and the update checks after it appears. Moved out of MainForm for 1.7 so every look shares it.
	internal static class AppStartup
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static DatabaseManager Database { get; private set; }

		// Before any window: reading the settings here first also records whether a settings file
		// existed (JsonUserSettingsProvider.HadSettingsBeforeThisLaunch), which the defaults below use.
		internal static void PrepareSettings()
		{
			var settings = Properties.Settings.Default;
			if (settings.UpgradeNeeded)
			{
				try
				{
					settings.Upgrade();
					Logger.Info("Application settings loaded from previous version");
				}
				catch (Exception) { }
				settings.UpgradeNeeded = false;
				settings.Save();
			}

			// A blank Wanderer or mannequin name saved earlier gets its default back before the boxes bind.
			foreach (var setting in CustomNames.FilledWhenEmpty)
				settings[setting] = CustomNames.OrDefault(setting, settings[setting] as string);

			RepairKeySettings();
			Navigation.inventoryKey = (VirtualKeyCode)settings.InventoryKey;
			Navigation.characterKey = (VirtualKeyCode)settings.CharacterKey;
			Navigation.slotOneKey = (VirtualKeyCode)settings.Slot1Key;

			bool hadSettings = Properties.JsonUserSettingsProvider.HadSettingsBeforeThisLaunch;
			int step = ScanSpeeds.Stored(settings.ScanSpeed, settings.ScannerDelay, hadSettings);
			if (settings.ScanSpeed != step)
			{
				if (hadSettings)
					Logger.Info("Scan speed set from the old Scanner Delay setting {0}: {1}", settings.ScannerDelay, ScanSpeeds.LogText(step));
				else
					Logger.Info("New install: scan speed set to {0}", ScanSpeeds.LogText(step));
				settings.ScanSpeed = step;
				settings.Save();
			}

			var look = Looks.Stored(settings.Look, hadSettings);
			if (settings.Look != (int)look)
			{
				Logger.Info("{0}: look set to {1}", hadSettings ? "Existing settings" : "New install", look);
				settings.Look = (int)look;
				settings.Save();
			}
			if (!hadSettings && !settings.LooksHintShown)
			{
				settings.LooksHintShown = true;   // a new install starts on Simple: nothing to point out
				settings.Save();
			}

			// The export folder, which Classic used to fill in when its window loaded.
			if (string.IsNullOrWhiteSpace(settings.OutputPath))
				settings.OutputPath = System.IO.Directory.GetCurrentDirectory() + @"\GenshinData";

			Database = new DatabaseManager();
		}

		// Key codes 0-6 are "none" and the mouse buttons, which the key boxes can't record. Older
		// settings stored the slot 1 key as 1 (the left mouse button) by mistake, so the scan never
		// switched to party slot 1. Replace such values with the default key.
		internal static int KeyOrDefault(int stored, Keys fallback) => stored > 6 ? stored : (int)fallback;

		private static void RepairKeySettings()
		{
			var settings = Properties.Settings.Default;
			int inventory = KeyOrDefault(settings.InventoryKey, Keys.B);
			int character = KeyOrDefault(settings.CharacterKey, Keys.C);
			int slot1 = KeyOrDefault(settings.Slot1Key, Keys.D1);

			if (inventory == settings.InventoryKey && character == settings.CharacterKey && slot1 == settings.Slot1Key) return;

			Logger.Info("Repaired key settings (inventory {0}->{1}, character {2}->{3}, slot 1 {4}->{5})",
				settings.InventoryKey, inventory, settings.CharacterKey, character, settings.Slot1Key, slot1);
			settings.InventoryKey = inventory;
			settings.CharacterKey = character;
			settings.Slot1Key = slot1;
			settings.Save();
		}

		// After the first window appears (not again when switching looks).
		// Once per launch, after the first window appears. A new install downloads its lookup tables; then,
		// once a day (Options → Updates… → "Check once a day by itself"), a background check for a newer app
		// and newer game data. It never pops up: the status line and a dot on Options say what it found.
		internal static async Task RunChecksAsync(ILookWindow window)
		{
			if (Database.TablesMissing)
				await DownloadMissingTablesAsync(window);   // a new install: the data is the newest there is

			var settings = Properties.Settings.Default;
			var now = DateTime.Now;
			if (!Updates.CheckDue(settings.AutoCheckUpdates, settings.LastUpdateCheck, now, settings.AppRemindAfter, settings.DataRemindAfter))
			{
				Logger.Info(settings.AutoCheckUpdates ? "Update check: last one {0:g}, next after a day" : "Update check: off (checked only with Check now)", settings.LastUpdateCheck);
				UpdateNotice.Restore(new OnlineUpdateSource().Current, Database?.TablesMissing == false ? Database.LocalVersion : null);   // the button, from the last check
				return;
			}
			if (now - settings.LastUpdateCheck < Updates.Daily && Updates.ReminderPassed(settings.LastUpdateCheck, now, settings.AppRemindAfter, settings.DataRemindAfter))
				Logger.Info("Update check: a Remind me later time has passed, checking again");
			await UpdateNotice.CheckNowAsync();
			UpdatePrompt.ShowAfterCheck(window as Form);   // 1.7.2: asks once when something new turned up
		}

		// A new install (or one whose download failed) has no lookup tables. Download them with the window
		// open and the step named on the status line, then say what arrived, or why nothing did.
		internal static async Task<bool> DownloadMissingTablesAsync(ILookWindow window)
		{
			UserInterface.SetProgramStatus(FirstDownload.Status);
			Logger.Info("Lookup tables missing: downloading them");
			DatabaseManager.RetryAfterUtc = null;
			UpdateStatus status;
			try
			{
				status = await Task.Run(() => new DatabaseManager().UpdateGameData(force: true));
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Could not download the lookup tables");
				status = UpdateStatus.Fail;
			}

			Database = new DatabaseManager();   // picks up the new version.txt
			if (status != UpdateStatus.Fail && !Database.TablesMissing)
			{
				GenshinProcesor.ReloadData();
				window.LookupTablesReloaded();
				string version = Database.LocalVersion.ToString(2);
				Logger.Info("Downloaded the lookup tables for game version {0}", version);
				UserInterface.SetProgramStatus("");
				MessageBox.Show(FirstDownload.SuccessText(version, Database.TableCounts()), FirstDownload.SuccessTitle,
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return true;
			}

			Logger.Error("Lookup tables are still missing after the download");
			UserInterface.SetProgramStatus(FirstDownload.MissingStatus, ok: false);
			MessageBox.Show(FirstDownload.FailTextFor(DatabaseManager.RetryAfterUtc), FirstDownload.FailTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
			return false;
		}

		// The Updates window's "Update game data" / "Download again". Runs the download off the window's thread
		// and reloads the lists; returns what to show instead of a popup.
		internal static async Task<(bool Ok, string Message)> UpdateGameDataAsync(bool force)
		{
			if (Database.TablesMissing)
			{
				bool got = await DownloadMissingTablesAsync(LookContext.Current?.Window as ILookWindow ?? NoWindow.Instance);
				return got ? (true, $"✓ Downloaded the game data for {Database.LocalVersion.ToString(2)}.") : (false, "The download didn't finish. See the message for when to try again.");
			}

			UpdateStatus status;
			DatabaseManager.RetryAfterUtc = null;
			try { status = await Task.Run(() => Database.UpdateGameData(force)); }
			catch (Exception ex)
			{
				Logger.Error(ex, "Game data update failed");
				status = UpdateStatus.Fail;
			}
			if (status == UpdateStatus.Fail)
				return (false, DatabaseManager.RetryAfterUtc == null
					? "Couldn't update the game data. The log has the details."
					: FirstDownload.FailTextFor(DatabaseManager.RetryAfterUtc));

			Database = new DatabaseManager();   // picks up the new version.txt
			GenshinProcesor.ReloadData();
			(LookContext.Current?.Window as ILookWindow)?.LookupTablesReloaded();
			string version = Database.LocalVersion.ToString(2);
			Logger.Info("Game data {0}: {1}", status == UpdateStatus.Skipped ? "already up to date" : "updated", version);
			return (true, status == UpdateStatus.Skipped ? $"✓ Already up to date ({version})." : $"✓ Updated to game data {version}.");
		}

		// For the rare download with no look window to tell (tests).
		private sealed class NoWindow : ILookWindow
		{
			internal static readonly NoWindow Instance = new NoWindow();
			public void LookupTablesReloaded() { }
			public void BecameShown() { }
			public void BecameHidden() { }
		}
	}
}
