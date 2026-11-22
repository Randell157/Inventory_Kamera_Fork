using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace InventoryKamera
{
	// Runs the app with one window at a time in the chosen look. View → Simple / Advanced / Classic saves
	// the choice and swaps the window; View → Zoom reopens it bigger. The app ends when the window is
	// closed any other way.
	internal sealed class LookContext : ApplicationContext
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static LookContext Current { get; private set; }

		private Form window;
		private bool switching;
		private bool checksDone;

		internal Look Shown { get; private set; }

		// The zoom the open window was made with (the saved one, or less when it doesn't fit the screen).
		internal int ShownZoom { get; private set; } = Zoom.Normal;

		internal static string VersionText
		{
			get
			{
#if DEBUG
				return Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
#else
				return Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
#endif
			}
		}

		public LookContext()
		{
			Current = this;
			Logger.Info("Inventory Kamera version {0}", VersionText);
			AppStartup.PrepareSettings();
			Open(Looks.Shown((Look)Properties.Settings.Default.Look), Screen.FromPoint(Cursor.Position));
		}

		private void Open(Look look, Screen screen)
		{
			Shown = look;
			window = CreateWindow(look);

			var area = screen.WorkingArea;
			int saved = Zoom.Valid(Properties.Settings.Default.Zoom);
			ShownZoom = Zoom.Usable(saved, Zoom.BaseSize(window), area);
			Zoom.Apply(window, ShownZoom);
			UserInterface.StatusZoom = ShownZoom / 100f;

			// Centered on the screen it was on, so a zoomed window doesn't hang off the bottom.
			window.StartPosition = FormStartPosition.Manual;
			var size = Zoom.BaseSize(window);   // the scaled size, in screen pixels
			window.Location = new Point(area.Left + Math.Max(0, (area.Width - size.Width) / 2), area.Top + Math.Max(0, (area.Height - size.Height) / 2));

			window.FormClosed += WindowClosed;
			window.Shown += WindowShown;
			window.Show();
			Logger.Info("Opened the {0} look at {1}", look, Zoom.MenuText(ShownZoom) + (ShownZoom == saved ? "" : $" ({Zoom.MenuText(saved)} doesn't fit this screen)"));
		}

		// Advanced gets its own window in step 4 of 1.7; until then Looks.Shown opens Classic instead.
		private static Form CreateWindow(Look look) => look == Look.Simple ? (Form)new SimpleForm() : new MainForm();

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
			Reopen(shown);
		}

		// View → Zoom: saves it and reopens the window at that size (scaling a window once, from 100 %,
		// keeps it sharp; scaling it again and again would drift).
		internal void SetZoom(int percent)
		{
			if (ScanSession.IsRunning) return;
			percent = Zoom.Valid(percent);
			var settings = Properties.Settings.Default;
			settings.Zoom = percent;
			settings.Save();
			Logger.Info("Zoom chosen: {0}", Zoom.MenuText(percent));
			if (percent != ShownZoom) Reopen(Shown);
		}

		private void Reopen(Look look)
		{
			var old = window;
			switching = true;
			old.Hide();
			Open(look, Screen.FromControl(old));
			old.Close();
			switching = false;
		}

		private void WindowClosed(object sender, FormClosedEventArgs e)
		{
			if (!switching && sender == window) ExitThread();
		}
	}
}
