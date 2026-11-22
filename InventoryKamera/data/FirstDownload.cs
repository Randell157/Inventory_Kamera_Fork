using System;
using System.Globalization;

namespace InventoryKamera
{
	// The messages for a new install's first lookup-table download (1.6.1). The main window downloads
	// the tables after it appears, with "Downloading lookup tables…" on the status line, then shows one
	// of these.
	internal static class FirstDownload
	{
		internal const string Status = "Downloading lookup tables…";
		internal const string MissingStatus = "Lookup tables missing";

		internal const string SuccessTitle = "New game data";

		// gameVersion is "7.2"; an unknown version ("0.0" or empty) leaves it out.
		internal static string SuccessText(string gameVersion, (int Characters, int Weapons, int ArtifactSets, int Materials) counts)
		{
			bool known = !string.IsNullOrWhiteSpace(gameVersion) && gameVersion != "0.0";
			return "Inventory Kamera downloaded the " + (known ? "game data for Genshin Impact " + gameVersion : "latest game data") + ": " +
				$"{counts.Characters} characters, {counts.Weapons} weapons, {counts.ArtifactSets} artifact sets and {counts.Materials} materials.\n\n" +
				"You can start scanning.";
		}

		internal const string FailTitle = "Lookup tables not downloaded";

		private const string FailHead =
			"Inventory Kamera couldn't download the game data it needs to recognize your characters, weapons, artifacts and materials.\n\n";

		// Offline, or GitLab gave no time.
		internal const string FailText = FailHead +
			"GitLab may be busy (it limits how often it can be asked) or you may be offline. Scans won't work until the data is downloaded.\n\n" +
			"Try again in a few minutes: Options → Update Lookup Tables.";

		// With the time GitLab asked to wait until (DatabaseManager.RetryAfterUtc), when it's still ahead.
		internal static string FailTextFor(DateTime? retryAfterUtc) =>
			retryAfterUtc == null ? FailText : FailTextFor(retryAfterUtc.Value.ToLocalTime(), DateTime.Now, CultureInfo.CurrentCulture);

		// 18:59:50 → 19:00, so the popup and the log never name a time before GitLab's.
		internal static DateTime RoundUpToMinute(DateTime t)
		{
			var at = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
			return at < t ? at.AddMinutes(1) : at;
		}

		// The time is rounded up to the next minute ("7:00 PM"); on another day it includes the date.
		internal static string FailTextFor(DateTime retryLocal, DateTime nowLocal, CultureInfo culture)
		{
			if (retryLocal <= nowLocal) return FailText;
			var at = RoundUpToMinute(retryLocal);
			string when = at.ToString(at.Date == nowLocal.Date ? "t" : "g", culture);
			return FailHead +
				"GitLab limits how much can be downloaded without an account and asked Inventory Kamera to wait. Scans won't work until the data is downloaded.\n\n" +
				"Try again after " + when + ": Options → Update Lookup Tables.";
		}
	}
}
