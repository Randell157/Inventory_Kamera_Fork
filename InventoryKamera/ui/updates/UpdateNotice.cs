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
			Settings.LastUpdateResult = Serialize(result);   // the button comes back at the next launch (1.7.2)
			Settings.Save();
			Logger.Info("Update check: app {0} (latest {1}{2}), game data {3} (newest {4}{5})",
				result.Current?.ToString(3), result.LatestTag ?? "?", result.AppError == null ? "" : ", " + result.AppError,
				result.DataLocal?.ToString(2), result.DataRemote?.ToString(2) ?? "?", result.DataError == null ? "" : ", " + result.DataError);
			Set(result);
			return result;
		}

		// After a check, Skip, Remind me later or a game data update: the menu-bar button and the dot on
		// Updates… follow. (1.7.1 and earlier also put "Updates available · Open Updates" on the status line;
		// the button replaced it, so an old notice there is only cleared.)
		internal static void Refresh()
		{
			if (!ScanSession.IsRunning && IsNoticeText(UserInterface.Status))
				UserInterface.SetProgramStatus("");
			Changed?.Invoke();
		}

		// The menu-bar button's words (mockup .mockups/update-notice.html, pick C).
		internal static string ButtonText(UpdateResult result, bool app, bool data) =>
			app && data ? "⬆ 2 updates"
			: app ? "⬆ Update to " + (result?.Latest?.ToString(3) ?? result?.LatestTag)
			: data ? "⬆ New game data " + result?.DataRemote?.ToString(2)
			: "";

		// The launch popup's choices: the same settings the Updates window's buttons save.
		internal static void SkipApp()
		{
			if (Last?.LatestTag == null) return;
			Settings.SkippedAppVersion = Last.LatestTag;
			Settings.Save();
			Logger.Info("Skipped app version {0}", Last.LatestTag);
		}

		internal static void RemindApp(DateTime now)
		{
			Settings.AppRemindAfter = now + Updates.Daily;
			Settings.Save();
			Logger.Info("App update: remind me later (after {0})", Settings.AppRemindAfter);
		}

		internal static void RemindData(DateTime now)
		{
			Settings.DataRemindAfter = now + Updates.Daily;
			Settings.Save();
			Logger.Info("Game data update: remind me later (after {0})", Settings.DataRemindAfter);
		}

		internal static bool IsNoticeText(string status) =>
			status == Updates.StatusText(true, true) || status == Updates.StatusText(true, false) || status == Updates.StatusText(false, true);

		// For tests: forget the last check.
		internal static void Reset() { Last = null; Changed = null; }

		#region The last check, kept across launches (1.7.2)

		// Live 2026-11-22: a launch on a day the check had already run showed no update button, because the
		// result lived only in memory. Each check's result is saved; a launch without a check shows it again,
		// compared with the version and the game data on this PC now. Only a real check opens the popup.
		private sealed class Saved
		{
			public DateTime CheckedAt;
			public string LatestTag, Notes, ReleaseUrl, AppError, AssetUrl, AssetDigest, DataRemote, DataError;
			public DateTime? Published;
			public long AssetSize;
		}

		internal static string Serialize(UpdateResult result) => result == null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(new Saved
		{
			CheckedAt = result.CheckedAt, LatestTag = result.LatestTag, Notes = result.Notes, ReleaseUrl = result.ReleaseUrl, AppError = result.AppError,
			AssetUrl = result.AssetUrl, AssetDigest = result.AssetDigest, AssetSize = result.AssetSize, Published = result.Published,
			DataRemote = result.DataRemote?.ToString(), DataError = result.DataError,
		});

		// current / dataLocal: what this PC has now (after an update the saved "latest" may be what's running).
		// An empty or damaged value gives null.
		internal static UpdateResult Deserialize(string json, Version current, Version dataLocal)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<Saved>(json);
				if (saved == null || (saved.LatestTag == null && saved.DataRemote == null)) return null;
				return new UpdateResult
				{
					CheckedAt = saved.CheckedAt, Current = current, LatestTag = saved.LatestTag, Notes = saved.Notes ?? "", ReleaseUrl = saved.ReleaseUrl,
					AppError = saved.AppError, AssetUrl = saved.AssetUrl, AssetDigest = saved.AssetDigest, AssetSize = saved.AssetSize, Published = saved.Published,
					DataLocal = dataLocal, DataRemote = Version.TryParse(saved.DataRemote ?? "", out var remote) ? remote : null, DataError = saved.DataError,
				};
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "Ignored the saved update check (damaged)");
				return null;
			}
		}

		// A launch without a check: the saved result, if any, sets the button again. No popup.
		internal static void Restore(Version current, Version dataLocal)
		{
			var result = Deserialize(Settings.LastUpdateResult, current, dataLocal);
			if (result == null) return;
			Set(result);
			Logger.Info("Update notice from the check of {0:g}: {1}", result.CheckedAt, Any ? StatusText : "nothing new");
		}

		#endregion
	}

	// While there's something new: a blue "⬆ Update to 1.7.2" button at the right end of the menu bar
	// (1.7.2, mockup pick C; it replaced 1.7's dot on Options and the status-line notice) and a small blue
	// dot on Options → Updates…. A scan doesn't hide the button; clicking it opens Updates (not during a scan).
	internal static class UpdateMark
	{
		internal static readonly Color Dot = Color.FromArgb(0, 103, 192);
		private static readonly Color Hover = Color.FromArgb(25, 117, 197), Down = Color.FromArgb(0, 84, 166);

		internal const string ButtonName = "UpdateButton";

		internal static ToolStripMenuItem Attach(Form window, ToolStrip menu, ToolStripMenuItem updates)
		{
			updates.ImageScaling = ToolStripItemImageScaling.None;
			var button = new ToolStripMenuItem
			{
				Name = ButtonName,
				Alignment = ToolStripItemAlignment.Right,
				Padding = new Padding(8, 0, 8, 0),
				Visible = false,
				AccessibleName = "Update available",
			};
			button.Click += (s, e) => UpdatesForm.ShowFor(window);
			button.Paint += (s, e) => PaintButton(button, e.Graphics);
			menu.Items.Add(button);

			void Show()
			{
				if (window.IsDisposed) return;
				bool any = UpdateNotice.Any;
				button.Text = UpdateNotice.ButtonText(UpdateNotice.Last, UpdateNotice.App, UpdateNotice.Data);
				button.ToolTipText = any ? UpdateNotice.StatusText + " · open Updates" : null;
				button.Visible = any;
				updates.Image = any ? DotImage(updates.Font.Height / 2) : null;
				updates.ToolTipText = any ? UpdateNotice.StatusText : null;
			}
			Action changed = () => { if (window.InvokeRequired) window.BeginInvoke((MethodInvoker)Show); else Show(); };   // the check finishes on another thread
			UpdateNotice.Changed += changed;
			window.FormClosed += (s, e) => UpdateNotice.Changed -= changed;
			Show();
			return button;
		}

		// White bold text on Windows accent blue (5.6:1), drawn over whatever the menu drew for the item.
		private static void PaintButton(ToolStripMenuItem button, Graphics g)
		{
			var box = new Rectangle(2, 2, button.Width - 4, button.Height - 4);
			var back = button.Pressed ? Down : button.Selected ? Hover : Dot;
			using (var parent = new SolidBrush(button.Owner?.BackColor ?? SystemColors.Menu)) g.FillRectangle(parent, 0, 0, button.Width, button.Height);
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			using (var path = Rounded(box, Math.Max(3, button.Height / 6)))
			using (var brush = new SolidBrush(back))
				g.FillPath(brush, path);
			using (var bold = new Font(button.Font, FontStyle.Bold))
			{
				var font = TextRenderer.MeasureText(button.Text, bold).Width <= box.Width ? bold : button.Font;
				TextRenderer.DrawText(g, button.Text, font, box, Color.White,
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
			}
		}

		private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
		{
			var path = new System.Drawing.Drawing2D.GraphicsPath();
			int d = radius * 2;
			path.AddArc(r.Left, r.Top, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
			path.CloseFigure();
			return path;
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
