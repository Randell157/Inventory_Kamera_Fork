using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InventoryKamera
{
	// The result of the last update check and what it means for the open window: the status line
	// ("Updates available · Open Updates") and the blue dot on Options and Updates… (mockup 2A, 3A).
	internal static class UpdateNotice
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		private static Properties.Settings Settings => Properties.Settings.Default;

		internal static UpdateResult Last { get; private set; }

		internal static event Action Changed;

		internal static bool App => Updates.ShowApp(Last, Settings.SkippedAppVersion, Settings.AppRemindAfter, DateTime.Now);
		internal static bool Data => Updates.ShowData(Last, Settings.DataRemindAfter, DateTime.Now);
		internal static bool Any => App || Data;
		internal static string StatusText => Updates.StatusText(App, Data);

		internal static void Set(UpdateResult result)
		{
			Last = result;
			Refresh();
		}

		// Runs a check (the launch check, or Check now), remembers when, and tells the windows.
		internal static async Task<UpdateResult> CheckNowAsync(IUpdateSource source = null)
		{
			var result = await Updates.CheckAsync(source ?? new OnlineUpdateSource(), DateTime.Now);
			Settings.LastUpdateCheck = result.CheckedAt;
			Settings.Save();
			Logger.Info("Update check: app {0} (latest {1}{2}), game data {3} (newest {4}{5})",
				result.Current?.ToString(3), result.LatestTag ?? "?", result.AppError == null ? "" : ", " + result.AppError,
				result.DataLocal?.ToString(2), result.DataRemote?.ToString(2) ?? "?", result.DataError == null ? "" : ", " + result.DataError);
			Set(result);
			return result;
		}

		// After a check, Skip, Remind me later or a game data update: the status line says so (unless a scan
		// is using it), or stops saying so; the dots follow.
		internal static void Refresh()
		{
			if (!ScanSession.IsRunning)
			{
				bool showingNotice = IsNoticeText(UserInterface.Status);
				if (Any && (showingNotice || string.IsNullOrEmpty(UserInterface.Status)))
					UserInterface.SetProgramStatus(StatusText, UserInterface.HintColor);
				else if (!Any && showingNotice)
					UserInterface.SetProgramStatus("");
			}
			Changed?.Invoke();
		}

		internal static bool IsNoticeText(string status) =>
			status == Updates.StatusText(true, true) || status == Updates.StatusText(true, false) || status == Updates.StatusText(false, true);

		// For tests: forget the last check.
		internal static void Reset() { Last = null; Changed = null; }
	}

	// Options and Options → Updates… get a small blue dot while there's something new (mockup 3A).
	internal static class UpdateMark
	{
		internal static readonly Color Dot = Color.FromArgb(0, 103, 192);

		internal static void Attach(Form window, ToolStripMenuItem options, ToolStripMenuItem updates)
		{
			string optionsText = options.Text;
			updates.ImageScaling = ToolStripItemImageScaling.None;
			void Show()
			{
				if (window.IsDisposed) return;
				bool any = UpdateNotice.Any;
				options.Text = any ? optionsText + "    " : optionsText;   // room for the dot
				options.Tag = any;
				updates.Image = any ? DotImage(updates.Font.Height / 2) : null;
				updates.ToolTipText = any ? UpdateNotice.StatusText : null;
				options.Invalidate();
			}
			options.Paint += (s, e) =>
			{
				if (!(options.Tag is bool on) || !on) return;
				int size = Math.Max(6, options.Font.Height / 2 - 1);
				var text = TextRenderer.MeasureText(optionsText, options.Font);
				int x = (options.Width + text.Width) / 2 - 2, y = (options.Height - size) / 2;
				e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				using (var brush = new SolidBrush(Dot)) e.Graphics.FillEllipse(brush, x, y, size, size);
			};
			Action changed = () => { if (window.InvokeRequired) window.BeginInvoke((MethodInvoker)Show); else Show(); };   // the check finishes on another thread
			UpdateNotice.Changed += changed;
			window.FormClosed += (s, e) => UpdateNotice.Changed -= changed;
			Show();
		}

		private static Bitmap DotImage(int size)
		{
			size = Math.Max(6, size);
			var bitmap = new Bitmap(size + 2, size + 2);
			using (var g = Graphics.FromImage(bitmap))
			using (var brush = new SolidBrush(Dot))
			{
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				g.FillEllipse(brush, 1, 1, size, size);
			}
			return bitmap;
		}
	}
}
