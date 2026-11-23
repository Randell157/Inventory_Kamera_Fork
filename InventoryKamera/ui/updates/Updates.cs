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

		// The release's zip (step 6: one-click update).
		internal string AssetUrl;
		internal long AssetSize;
		internal string AssetDigest;   // GitHub's "sha256:…"

		// The game data (lookup tables).
		internal Version DataLocal;
		internal Version DataRemote;
		internal string DataError;

		internal Version Latest => Updates.TagVersion(LatestTag);
		internal bool AppNewer => AppError == null && Updates.IsNewer(Latest, Current);
		internal bool DataNewer => DataError == null && DataRemote != null && DataLocal != null && DataRemote > DataLocal;
	}

	// The latest release as GitHub describes it.
	internal sealed class ReleaseInfo
	{
		internal string Tag, Notes, Url, AssetUrl, AssetDigest;
		internal DateTime? Published;
		internal long AssetSize;
	}

	// Where a check gets its answers; the real one asks GitHub and GitLab, tests use canned answers.
	internal interface IUpdateSource
	{
		Version Current { get; }
		Task<ReleaseInfo> LatestReleaseAsync();
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
		// remindAfter: the Remind me later times (app, game data). A reminder that ends after the last check is
		// still waiting; once it has passed, the launch check is due even if the last one was under a day ago,
		// so "tomorrow" doesn't turn into two days (check 17:00, remind 18:00, launch 17:30 next day: checked
		// but hidden; launch 18:05: checked again and shown). The check that follows ends the wait.
		internal static bool CheckDue(bool auto, DateTime lastCheck, DateTime now, params DateTime[] remindAfter) =>
			auto && (now - lastCheck >= Daily || lastCheck > now || ReminderPassed(lastCheck, now, remindAfter));

		internal static bool ReminderPassed(DateTime lastCheck, DateTime now, params DateTime[] remindAfter)
		{
			foreach (var time in remindAfter ?? new DateTime[0])
				if (time > lastCheck && now >= time) return true;
			return false;
		}

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
				var release = await source.LatestReleaseAsync();
				result.LatestTag = release.Tag;
				result.Published = release.Published;
				result.Notes = PlainNotes(release.Notes);
				result.ReleaseUrl = release.Url;
				result.AssetUrl = release.AssetUrl;
				result.AssetSize = release.AssetSize;
				result.AssetDigest = release.AssetDigest;
			}
			catch (Exception ex) { result.AppError = AppErrorText(ex); }
			try { result.DataRemote = await source.RemoteGameDataAsync(); }
			catch (Exception ex) { result.DataError = "Couldn't reach GitLab (" + Short(ex) + "). Try Check now later."; }
			return result;
		}

		internal static string AppErrorText(Exception ex) =>
			ex is GitHubAnswer answer && (answer.Status == 403 || answer.Status == 429) ? "GitHub asked to wait (too many checks from this network). Try again in an hour."
			: ex is GitHubAnswer missing && missing.Status == 404 ? "No release published yet."
			: "Couldn't reach GitHub (" + Short(ex) + "). Try Check now later.";

		private static string Short(Exception ex) =>
			ex is System.Net.Http.HttpRequestException || ex is System.Net.WebException ? "no internet?" : ex.GetType().Name.Replace("Exception", "").ToLowerInvariant();
	}

	// GitHub answered, but not with a release (403 rate limit, 404 none yet...).
	internal sealed class GitHubAnswer : Exception
	{
		internal readonly int Status;
		internal GitHubAnswer(int status) : base("GitHub answered " + status) { Status = status; }
	}

	// The real source: this fork's latest GitHub release and the GitLab game data. One small request each.
	internal sealed class OnlineUpdateSource : IUpdateSource
	{
		public Version Current => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

		// GitHub's own JSON (the Octokit version here predates the assets' "digest" field).
		public async Task<ReleaseInfo> LatestReleaseAsync()
		{
			using (var http = new System.Net.Http.HttpClient())
			{
				http.DefaultRequestHeaders.UserAgent.ParseAdd("Inventory_Kamera");
				http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
				var response = await http.GetAsync($"https://api.github.com/repos/{ForkRepository.Owner}/{ForkRepository.Name}/releases/latest");
				if (!response.IsSuccessStatusCode) throw new GitHubAnswer((int)response.StatusCode);
				return Parse(await response.Content.ReadAsStringAsync());
			}
		}

		// The release and its first .zip asset.
		internal static ReleaseInfo Parse(string json)
		{
			var release = Newtonsoft.Json.Linq.JObject.Parse(json);
			var info = new ReleaseInfo
			{
				Tag = (string)release["tag_name"],
				Notes = (string)release["body"],
				Url = (string)release["html_url"],
				Published = (DateTime?)release["published_at"],
			};
			if (info.Published != null) info.Published = DateTime.SpecifyKind(info.Published.Value.ToUniversalTime(), DateTimeKind.Utc);
			foreach (var asset in release["assets"] ?? new Newtonsoft.Json.Linq.JArray())
			{
				string name = (string)asset["name"] ?? "";
				if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
				info.AssetUrl = (string)asset["browser_download_url"];
				info.AssetSize = (long?)asset["size"] ?? 0;
				info.AssetDigest = (string)asset["digest"];
				break;
			}
			return info;
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
