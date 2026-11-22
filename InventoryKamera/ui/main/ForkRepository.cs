using System;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
	// The GitHub repository this build comes from. The update check and the GitHub / Releases / Issues
	// links all use it, so they never point at a different fork.
	internal static class ForkRepository
	{
		public const string Owner = "Randell157";
		public const string Name = "Inventory_Kamera_Fork";

		public static string Url => $"https://github.com/{Owner}/{Name}/";
		public static string ReleasesUrl => Url + "releases";
		public static string IssuesUrl => Url + "issues";

		// True only for a page of this repository on GitHub, so a link from GitHub's reply is never opened
		// as something else (a local program, another site).
		public static bool IsOwnPage(string url) =>
			!string.IsNullOrEmpty(url) && url.StartsWith(Url, StringComparison.Ordinal) && !url.Contains("..") &&
			Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com";

		// True when a release tag such as "v1.5.1" is newer than the running version. Only
		// major.minor.patch are compared: the build's fourth number is a random wildcard, so 1.5.0.x
		// must count as equal to a "v1.5.0" release. Tags that are not a version give false.
		public static bool IsNewerRelease(string tagName, Version current)
		{
			if (string.IsNullOrWhiteSpace(tagName) || current == null) return false;
			var match = Regex.Match(tagName, @"\d+(?:\.\d+){1,2}");
			if (!match.Success || !Version.TryParse(match.Value, out var tag)) return false;

			var latest = new Version(tag.Major, tag.Minor, Math.Max(tag.Build, 0));
			var running = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
			return latest > running;
		}
	}
}
