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
		internal static async Task RunChecksAsync(ILookWindow window)
		{
			CheckForKameraUpdates();
			if (Database.TablesMissing)
				await DownloadMissingTablesAsync(window);   // a new install: the data is the newest there is
			else
				CheckForGenshinUpdates(window);
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

		// Options → Update Lookup Tables.
		internal static async Task UpdateLookupTablesAsync(ILookWindow window)
		{
			if (Database.TablesMissing)
			{
				await DownloadMissingTablesAsync(window);   // the retry the "not downloaded" message points to
				return;
			}

			var status = Database.UpdateGameData();
			switch (status)
			{
				case UpdateStatus.Fail:
					MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
					break;
				case UpdateStatus.Success:
					GenshinProcesor.ReloadData();
					window.LookupTablesReloaded();
					MessageBox.Show($"Update for game version {Database.LocalVersion.ToString(2)} successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
					Logger.Info("Updated game date to {0}", Database.LocalVersion.ToString(2));
					break;
				case UpdateStatus.Skipped:
					if (MessageBox.Show($"No update necessary! You are already using the latest game data ({Database.LocalVersion.ToString(2)})." +
						$" Would you like to force an update?",
						"Already Up to Date",
						buttons: MessageBoxButtons.YesNo,
						icon: MessageBoxIcon.Information) == DialogResult.Yes)
					{
						status = Database.UpdateGameData(force: true);
						switch (status)
						{
							case UpdateStatus.Fail:
								MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
								break;
							default:
								GenshinProcesor.ReloadData();
								window.LookupTablesReloaded();
								MessageBox.Show($"Update for game version {Database.LocalVersion.ToString(2)} successful.", "Update success", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
								Logger.Info("Successfully updated game data to {0}", new DatabaseManager().LocalVersion.ToString(2));
								break;
						}
					}
					break;
				default:
					break;
			}
		}

		private static async void CheckForKameraUpdates()
		{
			var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("Inventory_Kamera"));
			try
			{
				// Latest published, non-prerelease release of this fork.
				var latest = await client.Repository.Release.GetLatest(ForkRepository.Owner, ForkRepository.Name);
				Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
				if (ForkRepository.IsNewerRelease(latest.TagName, currentVersion))
				{
					var message = $"A new version of Inventory Kamera is available.\n\n" +
						$"Current Version: {currentVersion.ToString(3)}\nLatest Version: {latest.TagName}\n\n" +
						$"Would you like to download the update?";
					var result = MessageBox.Show(message, "Inventory Kamera Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
					if (result == DialogResult.Yes)
					{
						Process.Start(new ProcessStartInfo(latest.HtmlUrl) { UseShellExecute = true });
					}
				}
			}
			catch (Octokit.RateLimitExceededException) { Logger.Warn("Rate limit exceeded checking for Kamera Update!!!! This warning should be resolved in an hour."); }
			catch (Octokit.NotFoundException) { Logger.Info("No published release of {0}/{1} yet; skipping the update check.", ForkRepository.Owner, ForkRepository.Name); }
			// Offline, GitHub down, etc.: an update check must never stop the app from starting.
			catch (Exception ex) { Logger.Warn(ex, "Could not check for Kamera updates."); }
		}

		private static void CheckForGenshinUpdates(ILookWindow window)
		{
			var databaseManager = new DatabaseManager();
			try
			{
				var updatesAvailable = databaseManager.UpdateAvailable();
				if (updatesAvailable)
				{
					var message = "A new version for Genshin Impact has been found. Would you like to update Kamera's lookup tables? (Recommended)";
					var result = MessageBox.Show(message, "Game Version Update", MessageBoxButtons.YesNo);
					if (result == DialogResult.Yes)
					{
						switch (databaseManager.UpdateGameData())
						{
							case UpdateStatus.Fail:
								MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
								break;
							case UpdateStatus.Success:
								Database = new DatabaseManager();
								GenshinProcesor.ReloadData();
								window.LookupTablesReloaded();
								MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
								Logger.Info("Updated game data to {0}", databaseManager.LocalVersion.ToString(2));
								break;
							default:
								break;
						}
					}
					else if (result == DialogResult.No)
					{
						MessageBox.Show("Update skipped. Please know that skipping this update will likely result in incorrect scans.\n" +
							"\nYou may check for updates again on restarting this application or by using the update manager found" +
							" under 'options'", "Update declined", MessageBoxButtons.OK);
					}
				}
				else
					Logger.Info("Current game data is up to date with data for {0}", databaseManager.LocalVersion.ToString(2));
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "Could not check for list updates");
				MessageBox.Show("Could not check for updates. Consider trying again in an hour or so.", "Game Version Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			Properties.Settings.Default.LastUpdateCheck = DateTime.Now;
		}
	}
}
