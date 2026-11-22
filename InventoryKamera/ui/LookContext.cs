using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace InventoryKamera
{
	// Runs the app with one window on screen at a time, in the chosen look. Each look's window is built the
	// first time it's needed and then kept (hidden) for the rest of the launch, so switching back is instant.
	// View → Zoom resizes the open window in place. Closing the window ends the app.
	internal sealed class LookContext : ApplicationContext
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static LookContext Current { get; private set; }

		private readonly WindowSet windows = new WindowSet(CreateWindow);
		private Form window;
		private bool checksDone;

		internal Look Shown { get; private set; }

		// The window on screen.
		internal Form Window => window;

		// The zoom of the open window (the saved one, or less when it doesn't fit the screen).
		internal int ShownZoom => window == null ? Zoom.Normal : Zoom.Current(window);

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
			Show(Looks.Shown((Look)Properties.Settings.Default.Look));
		}

		private static Form CreateWindow(Look look)
		{
			switch (look)
			{
				case Look.Simple: return new SimpleForm();
				case Look.Advanced: return new AdvancedForm();
				default: return new MainForm();
			}
		}

		private void Show(Look look)
		{
			var watch = Stopwatch.StartNew();
			var old = window;
			var screen = old != null ? Screen.FromControl(old) : Screen.FromPoint(Cursor.Position);
			var area = screen.WorkingArea;
			int saved = Zoom.Valid(Properties.Settings.Default.Zoom);

			var next = windows.Get(look, out bool built);
			Size size;
			if (built)
			{
				next.FormClosed += WindowClosed;
				next.Shown += WindowShown;
				next.StartPosition = FormStartPosition.Manual;
				var measured = Zoom.Measure(next);   // at 100 %, before it's scaled
				int opening = Zoom.Usable(saved, measured, area);
				Zoom.Apply(next, opening);
				size = Zoom.SizeAt(measured, opening);   // in screen pixels
			}
			else
			{
				Zoom.Set(next, Zoom.Usable(saved, Zoom.Measure(next), area));   // zoom may have changed while it was hidden
				size = next.Size;
			}
			next.Location = old != null ? Placement.KeepTopLeft(old.Location, size, area) : Placement.Centered(size, area);

			Shown = look;
			window = next;
			UserInterface.StatusZoom = ShownZoom / 100f;
			next.Show();       // the new window appears first, then the old one goes, so there's no empty moment
			Zoom.MarkShown(next);
			next.Activate();
			(next as ILookWindow)?.BecameShown();
			if (old != null && old != next)
			{
				old.Hide();
				(old as ILookWindow)?.BecameHidden();
			}

			int zoom = ShownZoom;
			Logger.Info("{0} the {1} look at {2}{3} in {4} ms", built ? "Opened" : "Showed", look, Zoom.MenuText(zoom),
				zoom == saved ? "" : $" ({Zoom.MenuText(saved)} doesn't fit this screen)", watch.ElapsedMilliseconds);
		}

		private async void WindowShown(object sender, EventArgs e)
		{
			if (checksDone) return;   // once per launch, not again after switching looks
			checksDone = true;
			if (sender is ILookWindow lookWindow) await AppStartup.RunChecksAsync(lookWindow);
		}

		// Saves the look and shows its window. Not while a scan runs (the scan reports to its window).
		internal void SwitchTo(Look look)
		{
			if (ScanSession.IsRunning) return;
			var settings = Properties.Settings.Default;
			settings.Look = (int)look;
			settings.Save();
			Logger.Info("Look chosen: {0}", look);

			var shown = Looks.Shown(look);
			if (shown != Shown) Show(shown);   // already open (or not built yet: stays on Classic)
		}

		// View → Zoom: saves it and resizes the open window in place, keeping its top-left corner.
		internal void SetZoom(int percent)
		{
			if (ScanSession.IsRunning || window == null) return;
			var watch = Stopwatch.StartNew();
			percent = Zoom.Valid(percent);
			var settings = Properties.Settings.Default;
			settings.Zoom = percent;
			settings.Save();

			var area = Screen.FromControl(window).WorkingArea;
			var topLeft = window.Location;
			Zoom.Set(window, Zoom.Usable(percent, Zoom.Measure(window), area));
			window.Location = Placement.KeepTopLeft(topLeft, window.Size, area);
			UserInterface.StatusZoom = ShownZoom / 100f;
			(window as ILookWindow)?.BecameShown();   // Classic re-fits its status line
			Logger.Info("Zoom chosen: {0}; the {1} look is now at {2} ({3} ms)", Zoom.MenuText(percent), Shown, Zoom.MenuText(ShownZoom), watch.ElapsedMilliseconds);
		}

		// The window on screen was closed (its X, or File → Quit): close the kept ones too, then end the app.
		private void WindowClosed(object sender, FormClosedEventArgs e)
		{
			windows.Forget((Form)sender);
			if (sender != window) return;
			foreach (var kept in windows.All.ToList()) kept.Close();   // each saves the settings as it closes
			ExitThread();
		}
	}

	// One window per look, built on first use and handed back after that.
	internal sealed class WindowSet
	{
		private readonly Func<Look, Form> create;
		private readonly Dictionary<Look, Form> windows = new Dictionary<Look, Form>();

		internal WindowSet(Func<Look, Form> create) { this.create = create; }

		internal Form Get(Look look, out bool built)
		{
			built = !windows.TryGetValue(look, out var window) || window.IsDisposed;
			if (built) windows[look] = window = create(look);
			return window;
		}

		internal IEnumerable<Form> All => windows.Values.Where(w => !w.IsDisposed);

		internal void Forget(Form window)
		{
			foreach (var look in windows.Where(p => p.Value == window).Select(p => p.Key).ToList()) windows.Remove(look);
		}
	}
}
