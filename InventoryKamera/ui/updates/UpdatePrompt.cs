using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace InventoryKamera
{
	// 1.7.2: what the launch popup says and offers (mockup .mockups/update-notice.html, pick 2 A).
	internal static class UpdatePromptText
	{
		internal const string Title = "Update available";
		internal const string Skip = "Skip this version", Later = "Remind me later", UpdateNow = "Update now", UpdateData = "Update game data", Open = "Open Updates";

		internal sealed class Part
		{
			internal string Heading, Text;
			internal List<string> Notes = new List<string>();
		}

		internal static List<Part> Parts(UpdateResult result, bool app, bool data)
		{
			var parts = new List<Part>();
			if (app)
			{
				var part = new Part
				{
					Heading = $"Inventory Kamera {result.Latest?.ToString(3) ?? result.LatestTag} is available",
					Text = $"You have {result.Current?.ToString(3)}." + (NotesPreview(result.Notes).Count > 0 ? " What's new:" : ""),
				};
				part.Notes.AddRange(NotesPreview(result.Notes));
				parts.Add(part);
			}
			if (data)
				parts.Add(new Part
				{
					Heading = $"New game data for Genshin {result.DataRemote?.ToString(2)}",
					Text = "Scans need it to name new characters, weapons and artifact sets.",
				});
			return parts;
		}

		// Left to right; the last one is the main button.
		internal static string[] Buttons(bool app, bool data) =>
			app && data ? new[] { Skip, Later, Open }
			: app ? new[] { Skip, Later, UpdateNow }
			: new[] { Later, UpdateData };

		internal static string AppHint(UpdateResult result) =>
			$"Update now downloads it ({SelfUpdate.SizeText(result.AssetSize)}) from GitHub, checks it, installs it and restarts. Your settings, game data and exports are kept.";

		// The first few bullets of the release notes, as plain text, each cut to one short line. The notes
		// arrive already plain (Updates.PlainNotes turns "- " into "• "); Markdown bullets work too.
		internal static List<string> NotesPreview(string notes, int max = 3, int length = 90)
		{
			var lines = new List<string>();
			foreach (var raw in (notes ?? "").Split('\n'))
			{
				string line = raw.Trim();
				if (!line.StartsWith("- ") && !line.StartsWith("* ") && !line.StartsWith("• ")) continue;
				line = line.Substring(2);
				line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");   // [text](link) → text
				line = line.Replace("**", "").Replace("`", "").Replace("__", "");
				line = Regex.Replace(line, @"\s+", " ").Trim();
				if (line.Length > length)
				{
					int cut = line.LastIndexOf(' ', length - 1);
					line = line.Substring(0, cut > length / 2 ? cut : length - 1).TrimEnd(',', ';', ':', '.', ' ') + "…";
				}
				if (line.Length > 0) lines.Add(line);
				if (lines.Count == max) break;
			}
			return lines;
		}
	}

	// The popup itself: after the launch check found something new that wasn't skipped or put off.
	internal sealed class UpdatePrompt : Form, IZoomed
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		private const int Width_ = 380, Edge = 16;

		internal readonly List<Button> Choices = new List<Button>();
		internal string Chosen { get; private set; }

		// The text lines from the top: each with its space before and its indent, in 100 % pixels.
		private readonly List<(Label Label, int Before, int Indent)> lines = new List<(Label, int, int)>();
		private readonly Panel footer = new Panel { BackColor = Color.FromArgb(240, 240, 240) };

		internal UpdatePrompt(UpdateResult result, bool app, bool data)
		{
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Segoe UI", 9F);
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			Text = UpdatePromptText.Title;
			BackColor = Color.White;

			void Add(string text, Font font, Color color, int before, int indent = 0)
			{
				var label = new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, UseMnemonic = false };
				Controls.Add(label);
				lines.Add((label, before, indent));
			}
			var heading = new Font("Segoe UI", 11.5F);
			foreach (var part in UpdatePromptText.Parts(result, app, data))
			{
				Add(part.Heading, heading, Color.FromArgb(0, 51, 153), lines.Count == 0 ? 12 : 14);
				Add(part.Text, Font, SystemColors.ControlText, 4);
				foreach (var note in part.Notes) Add("•  " + note, Font, Color.FromArgb(51, 51, 51), 2, indent: 6);
			}
			if (app) Add(UpdatePromptText.AppHint(result), Font, SystemColors.GrayText, 10);

			// Grey footer with the buttons on the right, the main one last and blue.
			Controls.Add(footer);
			var names = UpdatePromptText.Buttons(app, data);
			for (int i = 0; i < names.Length; i++)
			{
				var button = new Button { Text = names[i], AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0), MinimumSize = new Size(0, 28) };
				if (i == names.Length - 1)
				{
					button.FlatStyle = FlatStyle.Flat;
					button.FlatAppearance.BorderSize = 0;
					button.BackColor = UpdatesForm.Blue;
					button.ForeColor = Color.White;
					button.Font = new Font(Font, FontStyle.Bold);
					AcceptButton = button;
				}
				string name = names[i];
				button.Click += (s, e) => { Chosen = name; DialogResult = DialogResult.OK; };
				footer.Controls.Add(button);
				Choices.Add(button);
			}
			Arrange(1f);
			ResumeLayout(true);
		}

		// Lays the lines out top-down and the buttons right to left. Again after each zoom: zoomed text
		// grows a little faster than the layout, so a line may wrap differently.
		private void Arrange(float f)
		{
			int S(int px) => (int)Math.Round(px * f);
			int width = S(Width_), y = 0;
			foreach (var (label, before, indent) in lines)
			{
				y += S(before);
				label.MaximumSize = new Size(width - 2 * S(Edge) - S(indent), 0);
				label.Location = new Point(S(Edge) + S(indent), y);
				label.Size = label.PreferredSize;
				y += label.Height;
			}
			y += S(18);
			int x = width - S(12), top = S(10), height = 0;
			for (int i = Choices.Count - 1; i >= 0; i--)
			{
				var button = Choices[i];
				button.MinimumSize = new Size(0, S(28));
				button.Size = button.PreferredSize;
				x -= button.Width;
				button.Location = new Point(x, top);
				x -= S(8);
				height = Math.Max(height, button.Height);
			}
			footer.Bounds = new Rectangle(0, y, width, top + height + S(10));
			ClientSize = new Size(width, footer.Bottom);
		}

		void IZoomed.Zoomed(int percent) => Arrange(percent / (float)Zoom.Normal);

		// After the launch check: asks once (Remind me later: again tomorrow; Skip: never for that version).
		internal static void ShowAfterCheck(Form owner)
		{
			if (owner == null || owner.IsDisposed || ScanSession.IsRunning || !UpdateNotice.Any) return;
			var result = UpdateNotice.Last;
			bool app = UpdateNotice.App, data = UpdateNotice.Data;
			string chosen;
			using (var prompt = new UpdatePrompt(result, app, data))
			{
				Zoom.Apply(prompt, Zoom.Current(owner));
				Logger.Info("Update popup: {0}", UpdateNotice.StatusText);
				prompt.ShowDialog(owner);
				chosen = prompt.Chosen;
			}
			Logger.Info("Update popup answer: {0}", chosen ?? "closed");
			Act(owner, chosen, app, data, DateTime.Now);
		}

		internal static void Act(Form owner, string chosen, bool app, bool data, DateTime now, Action<Form, Func<UpdatesForm, System.Threading.Tasks.Task>> showUpdates = null)
		{
			showUpdates = showUpdates ?? UpdatesForm.ShowFor;
			switch (chosen)
			{
				case UpdatePromptText.Skip:
					UpdateNotice.SkipApp();
					break;
				case UpdatePromptText.Later:
					if (app) UpdateNotice.RemindApp(now);
					if (data) UpdateNotice.RemindData(now);
					break;
				case UpdatePromptText.UpdateNow:
					showUpdates(owner, window => { window.AlreadyConfirmed = true; return window.InstallAppAsync(); });
					break;
				case UpdatePromptText.UpdateData:
					showUpdates(owner, window => window.UpdateGameDataAsync(force: false));
					break;
				case UpdatePromptText.Open:
					showUpdates(owner, null);
					break;
				default:
					return;   // closed with ✕: nothing changes; the menu-bar button stays
			}
			UpdateNotice.Refresh();
		}
	}
}
