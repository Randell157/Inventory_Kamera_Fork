using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InventoryKamera
{
	// A step that can't go on, with the words to show in the Updates window. Nothing has been replaced.
	internal sealed class UpdateRefused : Exception
	{
		internal UpdateRefused(string message) : base(message) { }
	}

	// 1.7 step 6: one-click update in place (picks 2026-11-22). Download this repo's release zip, check its
	// SHA-256 against GitHub's digest (refuse when it's missing or different), unzip it next to the app,
	// rename every file it replaces to *.old (Windows allows that even for the running exe and its DLLs),
	// copy the new files in and restart. Any failure before the restart puts the .old files back. The next
	// launch deletes the .old files (listed in update-old.txt) and the update folder. Settings, lookup tables,
	// logs and exports are never in the zip, so they're never touched.
	internal static class SelfUpdate
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal const string ExeName = "InventoryKamera.exe";
		internal const string WorkFolder = "update";
		internal const string OldList = "update-old.txt";
		internal static readonly string AllowedPrefix = $"https://github.com/{ForkRepository.Owner}/{ForkRepository.Name}/releases/download/";

		// Only this repo's own release downloads.
		internal static bool AllowedUrl(string url) =>
			!string.IsNullOrEmpty(url) && url.StartsWith(AllowedPrefix, StringComparison.Ordinal) &&
			!url.Contains("..") && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

		// GitHub's asset digest: "sha256:<64 hex>" → the hex, lower case; anything else → null.
		internal static string Sha256FromDigest(string digest)
		{
			var match = Regex.Match(digest ?? "", @"^sha256:([0-9a-fA-F]{64})$");
			return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
		}

		internal static string Sha256Of(string path)
		{
			using (var sha = SHA256.Create())
			using (var stream = File.OpenRead(path))
				return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
		}

		// Everything checked before anything is downloaded: there is a zip, from here, with a digest.
		internal static void CheckRelease(UpdateResult release)
		{
			if (release == null || string.IsNullOrEmpty(release.AssetUrl))
				throw new UpdateRefused("This release has no zip to download.");
			if (!AllowedUrl(release.AssetUrl))
				throw new UpdateRefused("The download isn't from this app's own release page, so it wasn't used.");
			if (Sha256FromDigest(release.AssetDigest) == null)
				throw new UpdateRefused("GitHub gives no checksum for this download, so it can't be checked.");
		}

		internal static void Verify(string zip, string digest)
		{
			string expected = Sha256FromDigest(digest), actual = Sha256Of(zip);
			if (actual != expected)
			{
				Logger.Warn("Update refused: SHA-256 {0} doesn't match GitHub's {1}", actual, expected);
				throw new UpdateRefused("The download doesn't match GitHub's checksum (damaged or changed), so nothing was replaced.");
			}
			Logger.Info("Update zip checked: SHA-256 {0} matches", actual);
		}

		// Unzips into an empty folder. Refuses an entry that would land outside it and a zip without the app.
		internal static void Extract(string zip, string into)
		{
			if (Directory.Exists(into)) Directory.Delete(into, recursive: true);
			Directory.CreateDirectory(into);
			string root = Path.GetFullPath(into).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
			{
				foreach (var entry in archive.Entries)
				{
					string target = Path.GetFullPath(Path.Combine(into, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
					if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
						throw new UpdateRefused($"The download contains an unsafe path ({entry.FullName}), so it wasn't used.");
					if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					using (var from = entry.Open())
					using (var to = new FileStream(target, FileMode.CreateNew))
						from.CopyTo(to);
				}
			}
			if (!File.Exists(Path.Combine(into, ExeName)))
				throw new UpdateRefused("The download doesn't contain Inventory Kamera, so it wasn't used.");
		}

		// Puts the unzipped files into the app folder. Each file it replaces is renamed to *.old first; on any
		// failure the new copies are removed and the .old files renamed back. Returns the .old files.
		internal static List<string> Swap(string newFiles, string appDir)
		{
			var moved = new List<(string Original, string Old)>();
			var added = new List<string>();
			try
			{
				foreach (string source in Directory.GetFiles(newFiles, "*", SearchOption.AllDirectories))
				{
					string relative = source.Substring(newFiles.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
					string target = Path.Combine(appDir, relative);
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					if (File.Exists(target))
					{
						string old = target + ".old";
						if (File.Exists(old)) File.Delete(old);
						File.Move(target, old);
						moved.Add((target, old));
					}
					File.Copy(source, target);
					added.Add(target);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Update failed while replacing files; putting the old ones back");
				foreach (string file in added) TryDelete(file);
				foreach (var (original, old) in moved)
					try { File.Move(old, original); } catch (Exception back) { Logger.Error(back, "Couldn't put back {0}", original); }
				throw new UpdateRefused("Couldn't replace the app's files (" + ex.Message + "). Nothing was changed.");
			}
			var olds = moved.Select(m => m.Old).ToList();
			File.WriteAllLines(Path.Combine(appDir, OldList), olds);
			Logger.Info("Update installed: {0} files, {1} replaced", added.Count, olds.Count);
			return olds;
		}

		// At launch: delete the .old files of the last update and its work folder. Files still in use (the old
		// app hasn't quite closed) are kept for the next launch.
		internal static void CleanUp(string appDir)
		{
			string list = Path.Combine(appDir, OldList);
			if (!File.Exists(list)) return;
			var left = File.ReadAllLines(list).Where(f => f.EndsWith(".old", StringComparison.OrdinalIgnoreCase) && !TryDelete(f)).ToList();
			if (left.Count == 0) File.Delete(list); else File.WriteAllLines(list, left);
			try { if (Directory.Exists(Path.Combine(appDir, WorkFolder))) Directory.Delete(Path.Combine(appDir, WorkFolder), recursive: true); }
			catch (Exception ex) { Logger.Warn(ex, "Couldn't remove the update folder yet"); }
			Logger.Info("Cleaned up after the last update ({0} old files left for later)", left.Count);
		}

		private static bool TryDelete(string file)
		{
			try { if (File.Exists(file)) File.Delete(file); return true; }
			catch { return false; }
		}

		// The whole update up to the restart: check, download (with progress), verify, unzip, swap.
		internal static async Task InstallAsync(UpdateResult release, string appDir, IProgress<string> progress, HttpClient http = null)
		{
			CheckRelease(release);
			string work = Path.Combine(appDir, WorkFolder);
			Directory.CreateDirectory(work);
			string zip = Path.Combine(work, Path.GetFileName(new Uri(release.AssetUrl).LocalPath));
			string version = release.Latest?.ToString(3) ?? release.LatestTag;

			Logger.Info("Downloading update {0} from {1}", version, release.AssetUrl);
			var client = http ?? new HttpClient();
			try
			{
				using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
				{
					response.EnsureSuccessStatusCode();
					long total = response.Content.Headers.ContentLength ?? release.AssetSize;
					using (var input = await response.Content.ReadAsStreamAsync())
					using (var output = File.Create(zip))
					{
						var buffer = new byte[81920];
						long done = 0;
						int read, lastMb = -1;
						while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
						{
							await output.WriteAsync(buffer, 0, read);
							done += read;
							int mb = (int)(done / 1048576);
							if (mb != lastMb) { lastMb = mb; progress?.Report(ProgressText(version, done, total)); }
						}
					}
				}
			}
			catch (HttpRequestException ex) { throw new UpdateRefused("The download didn't work (" + ex.Message + "). Nothing was changed."); }
			finally { if (http == null) client.Dispose(); }

			progress?.Report("Checking the download…");
			Verify(zip, release.AssetDigest);
			progress?.Report("Unpacking…");
			Extract(zip, Path.Combine(work, "new"));
			progress?.Report("Replacing files…");
			Swap(Path.Combine(work, "new"), appDir);
		}

		internal static string ProgressText(string version, long done, long total) =>
			total > 0
				? $"Downloading {version}… {done / 1048576.0:0} of {total / 1048576.0:0} MB"
				: $"Downloading {version}… {done / 1048576.0:0} MB";

		internal static string SizeText(long bytes) => $"{Math.Max(1, (int)Math.Round(bytes / 1048576.0))} MB";
	}
}
