using System;
using System.Windows.Forms;

namespace InventoryKamera
{
	// Runs the app with one window at a time in the chosen look. View → Simple / Advanced / Classic saves
	// the choice and swaps the window; the app ends when the window is closed any other way.
	internal sealed class LookContext : ApplicationContext
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static LookContext Current { get; private set; }

		private Form window;
		private bool switching;
		private bool checksDone;

		internal Look Shown { get; private set; }

		public LookContext()
		{
			Current = this;
			AppStartup.PrepareSettings();
			Open(Looks.Shown((Look)Properties.Settings.Default.Look));
		}

		private void Open(Look look)
		{
			Shown = look;
			window = CreateWindow(look);
			window.FormClosed += WindowClosed;
			window.Shown += WindowShown;
			window.Show();
			Logger.Info("Opened the {0} look", look);
		}

		// Simple and Advanced get their own windows in the next 1.7 steps.
		private static Form CreateWindow(Look look) => new MainForm();

		private async void WindowShown(object sender, EventArgs e)
		{
			if (checksDone) return;   // once per launch, not again after switching looks
			checksDone = true;
			if (sender is ILookWindow lookWindow) await AppStartup.RunChecksAsync(lookWindow);
		}

		// Saves the look and swaps the window. Not while a scan runs (the scan reports to its window).
		internal void SwitchTo(Look look)
		{
			if (ScanSession.IsRunning) return;
			var settings = Properties.Settings.Default;
			settings.Look = (int)look;
			settings.Save();
			Logger.Info("Look chosen: {0}", look);

			var shown = Looks.Shown(look);
			if (shown == Shown) return;   // already open (or not built yet: stays on Classic)

			var old = window;
			switching = true;
			Open(shown);
			old.Close();
			switching = false;
		}

		private void WindowClosed(object sender, FormClosedEventArgs e)
		{
			if (!switching && sender == window) ExitThread();
		}
	}
}
