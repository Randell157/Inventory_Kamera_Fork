using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InventoryKamera
{
	// What one update check found. Filled by UpdateCheck from GitHub (the app) and GitLab (the game data).
	internal sealed class UpdateResult
	{
		internal DateTime CheckedAt;

		// The app (this fork's latest GitHub release).
		internal Version Current;
		internal string LatestTag;
		internal DateTime? Published;
		internal string Notes = "";
		internal string ReleaseUrl;
		internal string AppError;   // null when the check worked

		// The game data (lookup tables).
		internal Version DataLocal;
		internal Version DataRemote;
		internal string DataError;

		internal Version Latest => Updates.TagVersion(LatestTag);
		internal bool AppNewer => AppError == null && Updates.IsNewer(Latest, Current);
		internal bool DataNewer => DataError == null && DataRemote != null && DataLocal != null && DataRemote > DataLocal;
	}

	// Where a check gets its answers; the real one asks GitHub and GitLab, tests use canned answers.
	internal interface IUpdateSource
	{
		Version Current { get; }
		Task<(string Tag, DateTime? Published, string Notes, string Url)> LatestReleaseAsync();
		Version LocalGameData { get; }
		Task<Version> RemoteGameDataAsync();
	}

	// The rules of 1.7's update checks (picks 2026-11-22): once a day at launch, in the background, never a
	// popup; "Remind me later" waits until the next day; "Skip this version" ignores that version only;
	// game data is downloaded only when asked.
	internal static class Updates
	{
		internal static readonly TimeSpan Daily = TimeSpan.FromDays(1);

		// "v1.7.1", "1.7.1" or "Inventory Kamera 1.7" → 1.7.1 / 1.7.0; null when there's no version in it.
		internal static Version TagVersion(string tag)
		{
			if (string.IsNullOrWhiteSpace(tag)) return null;
			var match = Regex.Match(tag, @"\d+(?:\.\d+){1,2}");
			if (!match.Success || !Version.TryParse(match.Value, out var v)) return null;
			return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
		}

		// Compares major.minor.build only (the running app's revision is a build number).
		internal static bool IsNewer(Version latest, Version current) =>
			latest != null && current != null && latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

		// The launch check runs when it's switched on and the last one was a day ago or more (or in the future:
		// the clock was changed).
		internal static bool CheckDue(bool auto, DateTime lastCheck, DateTime now) =>
			auto && (now - lastCheck >= Daily || lastCheck > now);

		internal static bool ShowApp(UpdateResult result, string skipped, DateTime remindAfter, DateTime now) =>
			result != null && result.AppNewer && now >= remindAfter &&
			!Equals(TagVersion(skipped), result.Latest);

		internal static bool ShowData(UpdateResult result, DateTime remindAfter, DateTime now) =>
			result != null && result.DataNewer && now >= remindAfter;

		// The status line: one or both.
		internal static string StatusText(bool app, bool data) =>
			app && data ? "Updates available" : app ? "App update available" : data ? "New game data available" : "";

		internal const string OpenLink = "Open Updates";

		// GitHub release notes are Markdown; the window shows plain text.
		internal static string PlainNotes(string markdown)
		{
			if (string.IsNullOrWhiteSpace(markdown)) return "No release notes.";
			string text = markdown.Replace("\r\n", "\n");
			text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");                  // images
			text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");                // links → their text
			text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline); // headings
			text = Regex.Replace(text, @"^(\s*)[-*+]\s+", "$1• ", RegexOptions.Multiline); // bullets
			text = Regex.Replace(text, @"(\*\*|__)(.+?)\1", "$2");                     // bold
			text = Regex.Replace(text, @"`([^`]+)`", "$1");                            // code
			text = Regex.Replace(text, @"\n{3,}", "\n\n");
			return text.Trim().Replace("\n", Environment.NewLine);
		}

		internal static string DateText(DateTime? published) =>
			published == null ? "" : published.Value.ToLocalTime().ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

		internal static string LastCheckedText(DateTime lastCheck, DateTime now)
		{
			if (lastCheck.Year < 2000) return "Not checked yet";
			string time = lastCheck.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
			if (lastCheck.Date == now.Date) return "Last checked: today, " + time;
			if (lastCheck.Date == now.Date.AddDays(-1)) return "Last checked: yesterday, " + time;
			return "Last checked: " + lastCheck.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture) + ", " + time;
		}

		// Asks both sources; a failure in one is written into the result, never thrown.
		internal static async Task<UpdateResult> CheckAsync(IUpdateSource source, DateTime now)
		{
			var result = new UpdateResult { CheckedAt = now, Current = source.Current, DataLocal = source.LocalGameData };
			try
			{
				var (tag, published, notes, url) = await source.LatestReleaseAsync();
				result.LatestTag = tag;
				result.Published = published;
				result.Notes = PlainNotes(notes);
				result.ReleaseUrl = url;
			}
			catch (Exception ex) { result.AppError = AppErrorText(ex); }
			try { result.DataRemote = await source.RemoteGameDataAsync(); }
			catch (Exception ex) { result.DataError = "Couldn't reach GitLab (" + Short(ex) + "). Try Check now later."; }
			return result;
		}

		internal static string AppErrorText(Exception ex) =>
			ex is Octokit.RateLimitExceededException ? "GitHub asked to wait (too many checks from this network). Try again in an hour."
			: ex is Octokit.NotFoundException ? "No release published yet."
			: "Couldn't reach GitHub (" + Short(ex) + "). Try Check now later.";

		private static string Short(Exception ex) =>
			ex is System.Net.Http.HttpRequestException || ex is System.Net.WebException ? "no internet?" : ex.GetType().Name.Replace("Exception", "").ToLowerInvariant();
	}

	// The real source: this fork's latest GitHub release and the GitLab game data. One small request each.
	internal sealed class OnlineUpdateSource : IUpdateSource
	{
		public Version Current => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

		public async Task<(string Tag, DateTime? Published, string Notes, string Url)> LatestReleaseAsync()
		{
			var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("Inventory_Kamera"));
			var latest = await client.Repository.Release.GetLatest(ForkRepository.Owner, ForkRepository.Name);
			return (latest.TagName, latest.PublishedAt?.UtcDateTime, latest.Body, latest.HtmlUrl);
		}

		public Version LocalGameData => AppStartup.Database?.LocalVersion;

		public Task<Version> RemoteGameDataAsync() => Task.Run(() =>
		{
			var database = new DatabaseManager();
			database.UpdateAvailable();   // fills RemoteVersion (the commits API, not a table download)
			return database.RemoteVersion;
		});
	}
}
