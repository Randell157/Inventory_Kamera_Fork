using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7 step 6: one-click update in place. Offline: temporary folders, zips made here, a fake download.
	[TestClass]
	public sealed class SelfUpdateTests
	{
		private string root;
		private const string RealUrl = "https://github.com/Randell157/Inventory_Kamera_Fork/releases/download/v1.7.1/Inventory.Kamera.v1.7.1.zip";

		[TestInitialize]
		public void MakeTemp() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "ik-selfupdate-" + Guid.NewGuid().ToString("N")));

		[TestCleanup]
		public void RemoveTemp() { try { Directory.Delete(root, true); } catch { } }

		private string Dir(string name) { string d = Path.Combine(root, name); Directory.CreateDirectory(d); return d; }

		private static void Write(string folder, string relative, string text)
		{
			string path = Path.Combine(folder, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
		}

		private static string Read(string folder, string relative) => File.ReadAllText(Path.Combine(folder, relative));

		private string Zip(params (string Name, string Text)[] entries)
		{
			string path = Path.Combine(root, "release-" + Guid.NewGuid().ToString("N") + ".zip");
			using (var stream = File.Create(path))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
				foreach (var (name, text) in entries)
					using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(text);
			return path;
		}

		private static string Digest(string file) => "sha256:" + SelfUpdate.Sha256Of(file);

		private static readonly (string, string)[] NewRelease = { ("InventoryKamera.exe", "new exe"), ("a.dll", "new a"), ("b.dll", "new b"), ("tessdata/eng.traineddata", "new eng") };

		// An installed app with things the update must not touch.
		private string App()
		{
			string app = Dir("app");
			Write(app, "InventoryKamera.exe", "old exe");
			Write(app, "a.dll", "old a");
			Write(app, @"inventorylists\characters.json", "my tables");
			Write(app, @"logging\InventoryKamera.log", "my log");
			Write(app, @"GenshinData\genshinData_GOOD.json", "my export");
			return app;
		}

		[TestMethod]
		public void OnlyThisReposReleaseDownloads()
		{
			Assert.IsTrue(SelfUpdate.AllowedUrl(RealUrl));
			Assert.IsFalse(SelfUpdate.AllowedUrl("https://github.com/someone/Inventory_Kamera/releases/download/v9/x.zip"), "another repo");
			Assert.IsFalse(SelfUpdate.AllowedUrl(RealUrl.Replace("https:", "http:")), "not https");
			Assert.IsFalse(SelfUpdate.AllowedUrl(RealUrl.Replace("v1.7.1/", "../../other/")), "climbing out");
			Assert.IsFalse(SelfUpdate.AllowedUrl("https://objects.githubusercontent.com/x.zip"));
			Assert.IsFalse(SelfUpdate.AllowedUrl(null));
		}

		[TestMethod]
		public void ReleasePageLinkOpensOnlyThisRepo()
		{
			const string page = "https://github.com/Randell157/Inventory_Kamera_Fork/releases/tag/v1.7.0";
			Assert.AreEqual(page, UpdatesForm.ReleasePageUrl(page));
			foreach (string bad in new[] { "calc.exe", @"C:\Windows\System32\cmd.exe", "file:///C:/Windows/System32/cmd.exe",
				"https://github.com/someone/Inventory_Kamera/releases/tag/v9", "http://github.com/Randell157/Inventory_Kamera_Fork/releases",
				"https://github.com/Randell157/Inventory_Kamera_Fork/../../evil", "https://github.com/Randell157/Inventory_Kamera_Fork.evil.com/", "", null })
				Assert.AreEqual(ForkRepository.ReleasesUrl, UpdatesForm.ReleasePageUrl(bad), bad ?? "null");
		}

		[TestMethod]
		public void DigestsAndReleaseJson()
		{
			string hex = new string('a', 64);
			Assert.AreEqual(hex, SelfUpdate.Sha256FromDigest("sha256:" + hex.ToUpperInvariant()));
			Assert.IsNull(SelfUpdate.Sha256FromDigest("sha1:" + hex));
			Assert.IsNull(SelfUpdate.Sha256FromDigest("sha256:abc"));
			Assert.IsNull(SelfUpdate.Sha256FromDigest(null));

			// Shaped like GitHub's answer for 1.6.1.
			var info = OnlineUpdateSource.Parse(@"{""tag_name"":""v1.6.1"",""html_url"":""https://github.com/Randell157/Inventory_Kamera_Fork/releases/tag/v1.6.1"",
				""published_at"":""2026-11-21T08:00:00Z"",""body"":""notes"",""assets"":[{""name"":""Inventory.Kamera.v1.6.1.zip"",""size"":24381230,
				""digest"":""sha256:92bcee28668db37acee2363ba367939b5b91d06ca282b1817d25effc0e841705"",
				""browser_download_url"":""https://github.com/Randell157/Inventory_Kamera_Fork/releases/download/v1.6.1/Inventory.Kamera.v1.6.1.zip""}]}");
			Assert.AreEqual("v1.6.1", info.Tag);
			Assert.AreEqual(24381230, info.AssetSize);
			Assert.AreEqual("sha256:92bcee28668db37acee2363ba367939b5b91d06ca282b1817d25effc0e841705", info.AssetDigest);
			Assert.IsTrue(SelfUpdate.AllowedUrl(info.AssetUrl));
			Assert.AreEqual(new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc), info.Published);
			Assert.IsNull(OnlineUpdateSource.Parse(@"{""tag_name"":""v1"",""assets"":[]}").AssetUrl, "no zip");

			string Refusal(UpdateResult r) { try { SelfUpdate.CheckRelease(r); return null; } catch (UpdateRefused x) { return x.Message; } }
			Assert.IsNull(Refusal(new UpdateResult { AssetUrl = RealUrl, AssetDigest = "sha256:" + hex }));
			StringAssert.Contains(Refusal(new UpdateResult { AssetUrl = RealUrl }), "no checksum");
			StringAssert.Contains(Refusal(new UpdateResult { AssetUrl = "https://example.invalid/x.zip", AssetDigest = "sha256:" + hex }), "isn't from this app's own release page");
			StringAssert.Contains(Refusal(new UpdateResult()), "no zip");
		}

		[TestMethod]
		public void AChangedDownloadIsRefused()
		{
			string zip = Zip(NewRelease);
			SelfUpdate.Verify(zip, Digest(zip));
			var refused = Assert.ThrowsException<UpdateRefused>(() => SelfUpdate.Verify(zip, "sha256:" + new string('0', 64)));
			StringAssert.Contains(refused.Message, "doesn't match GitHub's checksum");
		}

		[TestMethod]
		public void UnsafeOrIncompleteZipsAreRefused()
		{
			string into = Path.Combine(root, "new");
			StringAssert.Contains(Assert.ThrowsException<UpdateRefused>(() => SelfUpdate.Extract(Zip(("InventoryKamera.exe", "x"), ("../evil.dll", "x")), into)).Message, "unsafe path");
			Assert.IsFalse(File.Exists(Path.Combine(root, "evil.dll")), "nothing written outside");
			StringAssert.Contains(Assert.ThrowsException<UpdateRefused>(() => SelfUpdate.Extract(Zip(("a.dll", "x")), into)).Message, "doesn't contain Inventory Kamera");

			SelfUpdate.Extract(Zip(NewRelease), into);
			Assert.AreEqual("new eng", Read(into, @"tessdata\eng.traineddata"));
		}

		[TestMethod]
		public void SwapKeepsOldFilesAsDotOldAndLeavesYourDataAlone()
		{
			string app = App(), into = Path.Combine(root, "new");
			SelfUpdate.Extract(Zip(NewRelease), into);
			var olds = SelfUpdate.Swap(into, app);

			Assert.AreEqual("new exe", Read(app, "InventoryKamera.exe"));
			Assert.AreEqual("old exe", Read(app, "InventoryKamera.exe.old"));
			Assert.AreEqual("old a", Read(app, "a.dll.old"));
			Assert.AreEqual("new b", Read(app, "b.dll"));
			Assert.AreEqual("new eng", Read(app, @"tessdata\eng.traineddata"));
			Assert.AreEqual(2, olds.Count);
			Assert.AreEqual(("my tables", "my log", "my export"),
				(Read(app, @"inventorylists\characters.json"), Read(app, @"logging\InventoryKamera.log"), Read(app, @"GenshinData\genshinData_GOOD.json")));

			SelfUpdate.CleanUp(app);   // the next launch
			Assert.IsFalse(File.Exists(Path.Combine(app, "InventoryKamera.exe.old")) || File.Exists(Path.Combine(app, "a.dll.old")));
			Assert.IsFalse(File.Exists(Path.Combine(app, SelfUpdate.OldList)));
			Assert.AreEqual("new exe", Read(app, "InventoryKamera.exe"));
			Assert.AreEqual("my tables", Read(app, @"inventorylists\characters.json"));
		}

		[TestMethod]
		public void AFailurePartWayPutsEverythingBack()
		{
			string app = App(), into = Path.Combine(root, "new");
			SelfUpdate.Extract(Zip(NewRelease), into);
			// a.dll is held open without sharing, so renaming it fails part-way through.
			using (new FileStream(Path.Combine(app, "a.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
				StringAssert.Contains(Assert.ThrowsException<UpdateRefused>(() => SelfUpdate.Swap(into, app)).Message, "Nothing was changed");

			Assert.AreEqual("old exe", Read(app, "InventoryKamera.exe"));
			Assert.AreEqual("old a", Read(app, "a.dll"));
			Assert.AreEqual(0, Directory.GetFiles(app, "*.old", SearchOption.AllDirectories).Length, "no .old left");
			Assert.IsFalse(File.Exists(Path.Combine(app, "b.dll")), "new files taken out again");
			Assert.IsFalse(File.Exists(Path.Combine(app, SelfUpdate.OldList)));
		}

		[TestMethod]
		public void CleanUpKeepsFilesStillInUseForNextTime()
		{
			string app = App(), into = Path.Combine(root, "new");
			SelfUpdate.Extract(Zip(NewRelease), into);
			SelfUpdate.Swap(into, app);
			using (new FileStream(Path.Combine(app, "InventoryKamera.exe.old"), FileMode.Open, FileAccess.Read, FileShare.None))
				SelfUpdate.CleanUp(app);   // the old app hasn't closed yet
			Assert.IsTrue(File.Exists(Path.Combine(app, "InventoryKamera.exe.old")));
			CollectionAssert.AreEqual(new[] { Path.Combine(app, "InventoryKamera.exe.old") }, File.ReadAllLines(Path.Combine(app, SelfUpdate.OldList)));
			SelfUpdate.CleanUp(app);
			Assert.IsFalse(File.Exists(Path.Combine(app, "InventoryKamera.exe.old")));
			SelfUpdate.CleanUp(app);   // nothing to do: no list
		}

		// Serves the zip for the download, as GitHub would (after its redirect).
		private sealed class FakeGitHub : HttpMessageHandler
		{
			private readonly byte[] zip;
			internal int Requests;
			internal FakeGitHub(byte[] zip) { this.zip = zip; }
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests++;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
			}
		}

		private UpdateResult Release(string zip, string digest = null) => new UpdateResult
		{
			Current = new Version(1, 7, 0), LatestTag = "v1.7.1", AssetUrl = RealUrl, AssetSize = new FileInfo(zip).Length, AssetDigest = digest ?? Digest(zip),
		};

		[TestMethod]
		public async Task InstallEndToEndWithAFakeDownload()
		{
			string app = App(), zip = Zip(NewRelease);
			var github = new FakeGitHub(File.ReadAllBytes(zip));
			var steps = new List<string>();
			using (var http = new HttpClient(github))
				await SelfUpdate.InstallAsync(Release(zip), app, new Recorder(steps), http);
			Assert.AreEqual(1, github.Requests, "one download");
			Assert.AreEqual("new exe", Read(app, "InventoryKamera.exe"));
			Assert.IsTrue(steps.Any(s => s.StartsWith("Downloading 1.7.1…")));
			CollectionAssert.IsSubsetOf(new[] { "Checking the download…", "Unpacking…", "Replacing files…" }, steps);
		}

		[TestMethod]
		public async Task AWrongChecksumChangesNothing()
		{
			string app = App(), zip = Zip(NewRelease);
			using (var http = new HttpClient(new FakeGitHub(File.ReadAllBytes(zip))))
				await Assert.ThrowsExceptionAsync<UpdateRefused>(() => SelfUpdate.InstallAsync(Release(zip, "sha256:" + new string('1', 64)), app, null, http));
			Assert.AreEqual("old exe", Read(app, "InventoryKamera.exe"));
			Assert.AreEqual(0, Directory.GetFiles(app, "*.old").Length);
		}

		private sealed class Recorder : IProgress<string>
		{
			private readonly List<string> list;
			internal Recorder(List<string> list) { this.list = list; }
			public void Report(string value) => list.Add(value);
		}

		[TestMethod]
		public void ProgressTexts()
		{
			Assert.AreEqual("Downloading 1.7.1… 12 of 23 MB", SelfUpdate.ProgressText("1.7.1", 12 * 1048576L, 23 * 1048576L));
			Assert.AreEqual("23 MB", SelfUpdate.SizeText(24381230));
			StringAssert.StartsWith(UpdatesForm.ConfirmText(new UpdateResult { LatestTag = "v1.7.1", AssetSize = 24381230 }),
				"Download 1.7.1 (23 MB), check it, replace this version and restart Inventory Kamera?");
		}

		// The window's Update… flow with everything canned: the question, the install and the restart.
		private static async Task Window(Func<UpdatesForm, Task> test, UpdateResult release, bool answer, Func<UpdateResult, IProgress<string>, Task> install, Action restart)
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			UpdateNotice.Reset();
			try
			{
				using (var w = new UpdatesForm(() => Task.FromResult(release), f => Task.FromResult((true, "")), () => DateTime.Now, install, text => answer, restart))
				{
					SimpleLookTests.Bind(w);
					await w.RunCheckAsync();
					await test(w);
				}
			}
			finally { UpdateNotice.Reset(); isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		private static UpdateResult Offer(string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") => new UpdateResult
		{
			CheckedAt = DateTime.Now, Current = new Version(1, 7, 0), LatestTag = "v1.7.1", ReleaseUrl = "https://example.invalid/r",
			AssetUrl = RealUrl, AssetSize = 24381230, AssetDigest = digest, DataLocal = new Version(7, 2), DataRemote = new Version(7, 2),
		};

		[TestMethod]
		public Task WindowAsksThenInstallsAndRestarts()
		{
			int installs = 0, restarts = 0;
			return Window(async w =>
			{
				Assert.AreEqual(UpdatesForm.UpdateHint, w.AppHint.Text);
				await w.InstallAppAsync();
				Assert.AreEqual((1, 1), (installs, restarts));
				Assert.AreEqual("Installed. Restarting…", w.AppHint.Text);
				Assert.IsTrue(w.Restarting);
			}, Offer(), answer: true, install: (r, p) => { installs++; p.Report("Downloading 1.7.1… 5 of 23 MB"); return Task.CompletedTask; }, restart: () => restarts++);
		}

		[TestMethod]
		public Task CancelDoesNothing()
		{
			int installs = 0;
			return Window(async w =>
			{
				await w.InstallAppAsync();
				Assert.AreEqual(0, installs);
				Assert.AreEqual(UpdatesForm.UpdateHint, w.AppHint.Text);
				Assert.IsTrue(w.UpdateApp.Enabled);
			}, Offer(), answer: false, install: (r, p) => { installs++; return Task.CompletedTask; }, restart: null);
		}

		[TestMethod]
		public Task ARefusalSaysWhyAndLinksTheReleasePage() => Window(async w =>
		{
			await w.InstallAppAsync();
			StringAssert.StartsWith(w.AppHint.Text, "The download doesn't match GitHub's checksum");
			StringAssert.EndsWith(w.AppHint.Text, UpdatesForm.ReleasePageLink);
			Assert.AreEqual(UpdatesForm.ReleasePageLink, w.AppHint.Text.Substring(w.AppHint.LinkArea.Start, w.AppHint.LinkArea.Length));
			Assert.AreEqual(UpdatesForm.Red, w.AppHint.ForeColor);
			Assert.IsFalse(w.Restarting);
			Assert.IsTrue(w.UpdateApp.Enabled && w.CheckNow.Enabled, "you can try again");
		}, Offer(), answer: true, install: (r, p) => throw new UpdateRefused("The download doesn't match GitHub's checksum (damaged or changed), so nothing was replaced."), restart: () => Assert.Fail("no restart"));

		[TestMethod]
		public Task NoChecksumIsRefusedBeforeAsking() => Window(async w =>
		{
			await w.InstallAppAsync();
			StringAssert.StartsWith(w.AppHint.Text, "GitHub gives no checksum");
		}, Offer(digest: null), answer: true, install: (r, p) => { Assert.Fail("not downloaded"); return Task.CompletedTask; }, restart: null);
	}
}
