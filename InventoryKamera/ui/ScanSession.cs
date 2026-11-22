using NHotkey;
using NHotkey.WindowsForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The window a scan was started from, whatever its look. Called from the scan thread, so each
	// method marshals to the window's own thread.
	internal interface IScanWindow
	{
		// The window's control to marshal onto (the form itself).
		Control Invoker { get; }

		// Flash the taskbar and show the end-of-scan summary (OK only).
		void ScanEnded(string title, string message, MessageBoxIcon icon);

		// The scan thread has finished, however it ended: re-enable what a scan disables.
		void ScanStopped(bool hasData);
	}

	// Starting and stopping a scan, shared by every look. Moved out of MainForm for 1.7; the steps,
	// messages and log lines are unchanged.
	internal static class ScanSession
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		private static Thread scannerThread;
		private static volatile bool running;

		// The last scan's data (empty until a scan has run). The "Open Genshin Optimizer" button exports it.
		internal static InventoryKamera Data { get; private set; } = new InventoryKamera();

		internal static bool IsRunning => running;

		public static void UnexpectedError(string error)
		{
			if (scannerThread != null && scannerThread.IsAlive)
			{
				UserInterface.AddError(error);
			}
		}

		// Called on the window's thread (the Scan button).
		internal static void Start(string outputPath, IScanWindow window)
		{
			GC.Collect();

			if (AppStartup.Database.TablesMissing)
			{
				Logger.Warn("Scan not started: the lookup tables are missing");
				UserInterface.SetProgramStatus(FirstDownload.MissingStatus, ok: false);
				MessageBox.Show(FirstDownload.FailTextFor(DatabaseManager.RetryAfterUtc), FirstDownload.FailTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			UserInterface.ResetAll();

			UserInterface.SetProgramStatus("Scanning");
			Logger.Info("Starting scan");

			if (string.IsNullOrWhiteSpace(outputPath))
			{
				UserInterface.AddError("Please set an output directory");
				return;
			}
			if (!(Directory.Exists(outputPath) || Directory.CreateDirectory(outputPath).Exists))
			{
				UserInterface.AddError($"{outputPath} is not a valid directory");
				return;
			}

			if (running)
			{
				Logger.Debug("Already running");
				return;
			}
			running = true;

			HotkeyManager.Current.AddOrReplace("Stop", Keys.Enter, Hotkey_Pressed);
			Logger.Info("Hotkey registered");
			var settings = Properties.Settings.Default;
			var gameVersion = new DatabaseManager().LocalVersion.ToString(2);
			var options =
				$"\n\tGame Version Data:\t\t\t {gameVersion}\n" +
				$"\tWeapons:\t\t\t\t {settings.ScanWeapons}\n" +
				$"\tArtifacts:\t\t\t\t {settings.ScanArtifacts}\n" +
				$"\tCharacters:\t\t\t\t {settings.ScanCharacters}\n" +
				$"\tDev Items:\t\t\t\t {settings.ScanCharDevItems}\n" +
				$"\tMaterials:\t\t\t\t {settings.ScanMaterials}\n" +
				$"\tMin Weapon Rarity:\t\t {settings.MinimumWeaponRarity}\n" +
				$"\tMin Weapon Level:\t\t {settings.MinimumWeaponLevel}\n" +
				$"\tEquip Weapons:\t\t\t {settings.EquipWeapons}\n" +
				$"\tMin Artifact Rarity:\t {settings.MinimumArtifactRarity}\n" +
				$"\tMin Artifact Level:\t\t {settings.MinimumArtifactLevel}\n" +
				$"\tEquip Artifacts:\t\t {settings.EquipArtifacts}\n" +
				$"\tScan speed:\t\t\t\t {ScanSpeeds.LogText(settings.ScanSpeed)}";

			Logger.Info("Scan settings: {0}", options);

			scannerThread = new Thread(() =>
			{
				try
				{
					// Get Screen Location and Size
					Navigation.Initialize();

					List<Size> sizes = new List<Size>
					{
						new Size(16,9),
						new Size(8,5),
					};

					if (!sizes.Contains(Navigation.GetAspectRatio()))
					{
						throw new NotImplementedException($"{Navigation.GetSize().Width}x{Navigation.GetSize().Height} is an unsupported resolution.");
					}

					if (Navigation.GetSize() != Navigation.CaptureWindow().Size) throw new FormatException("Window size and screenshot size mismatch. Please make sure the game is not in a fullscreen mode.");

					Data = new InventoryKamera();

					Logger.Info("Resolution: {0}x{1}", Navigation.GetSize().Width, Navigation.GetSize().Height);

					// Add navigation delay
					Navigation.SetDelay(ScanSpeeds.Factor(Properties.Settings.Default.ScanSpeed));
					Navigation.AutoItemWait = ScanSpeeds.IsAuto(Properties.Settings.Default.ScanSpeed);

					// The Data object of json object
					Data.GatherData();

					// Covert to GOOD
					GOOD good = new GOOD(Data);
					Logger.Info("Data converted to GOOD");

					// Make Json File
					string outputFile = good.WriteToJSON(outputPath);
					Logger.Info("Exported data");

					UserInterface.SetProgramStatus("Finished");
					// No "upload to Genshin Optimizer?" prompt after a scan. The
					// "Open Genshin Optimizer" button still does it on request.
					window.ScanEnded("Scan complete",
						good.CompletionSummary(outputFile, UserInterface.ErrorCount), MessageBoxIcon.Information);
				}
				catch (ThreadAbortException)
				{
					// Workers can get stuck if the thread is aborted or an exception is raised
					Data?.StopImageProcessorWorkers();
					UserInterface.SetProgramStatus("Scan stopped");
				}
				catch (NotImplementedException ex)
				{
					UserInterface.AddError(ex.ToString());
				}
				catch (Exception ex)
				{
					// Workers can get stuck if the thread is aborted or an exception is raised
					Data?.StopImageProcessorWorkers();
					while (ex.InnerException != null) ex = ex.InnerException;
					UserInterface.AddError(ex.ToString());
					UserInterface.SetProgramStatus("Scan aborted", ok: false);
					window.ScanEnded("Scan stopped",
						"The scan stopped because of an error:" + Environment.NewLine + ex.Message +
						Environment.NewLine + Environment.NewLine + "See the error log in the window for details.",
						MessageBoxIcon.Error);
				}
				finally
				{
					Navigation.Reset();
					// Need to invoke method from the UI's handle, not the worker thread
					window.Invoker.BeginInvoke((MethodInvoker)RemoveHotkey);
					Logger.Info("Hotkey removed");
					running = false;
					window.ScanStopped(Data.HasData);
				}
			})
			{
				IsBackground = true
			};
			scannerThread.Start();
		}

		private static void Hotkey_Pressed(object sender, HotkeyEventArgs e)
		{
			Logger.Info("Hotkey pressed");
			e.Handled = true;
			// Check if scanner is running
			if (scannerThread != null && scannerThread.IsAlive)
			{
				// Stop navigating weapons/artifacts
				scannerThread.Abort();

				UserInterface.SetProgramStatus("Scan Stopped");

				Navigation.Reset();
			}
		}

		internal static void RemoveHotkey()
		{
			HotkeyManager.Current.Remove("Stop");
		}
	}
}
