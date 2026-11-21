// Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT), InventoryKamera.Tests/CharacterLookupCompatibilityTests.cs.
// Runs the real lookup-table updater offline: GitLab downloads are intercepted and served
// from Fixtures/ (recorded AnimeGameData2 7.1.0 game data). Any other network request fails the test.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: DoNotParallelize]

namespace InventoryKamera.Tests
{
    [TestClass]
    public sealed class CharacterLookupCompatibilityTests
    {
        private static readonly string[] Elements = { "anemo", "geo", "electro", "dendro", "hydro", "pyro", "cryo" };
        private string previousDirectory;
        private string sandbox;

        [ClassInitialize]
        public static void RegisterOfflineSource(TestContext context)
        {
            Assert.IsTrue(WebRequest.RegisterPrefix("https://gitlab.com/Dimbreath/AnimeGameData2/-/raw/main/", new FixtureRequestFactory()));
            // Unexpected WebRequest URLs fail locally rather than reaching the network.
            WebRequest.RegisterPrefix("https:", new RejectNetworkFactory());
            WebRequest.RegisterPrefix("http:", new RejectNetworkFactory());
        }

        [TestInitialize]
        public void CreateIsolatedCache()
        {
            previousDirectory = Environment.CurrentDirectory;
            sandbox = Path.Combine(Path.GetTempPath(), "InventoryKamera.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandbox, "inventorylists"));
            Environment.CurrentDirectory = sandbox;
            foreach (var name in new[] { "characters", "weapons", "artifacts", "materials" })
                File.WriteAllText(Path.Combine("inventorylists", name + ".json"), "{}");
            File.WriteAllText(Path.Combine("inventorylists", "version.txt"), "7.0.0");
            FixtureRequestFactory.Documents = LoadFixture();
        }

        [TestCleanup]
        public void RemoveIsolatedCache()
        {
            Environment.CurrentDirectory = previousDirectory;
            if (sandbox != null && Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }

        [TestMethod]
        [Description("AC-1: generate one seven-element Traveler with source-derived Cryo talents")]
        public void AC1_EmptyCacheGeneratesCompleteTraveler()
        {
            var records = UpdateAndRead();
            Assert.AreEqual(1, records.Values.Count(record => (string)record["GOOD"] == "Traveler"));
            AssertTraveler(records["traveler"]);
        }

        [TestMethod]
        [Description("AC-2: non-forced update refreshes cached Traveler and retains custom records")]
        public void AC2_NonForcedUpdateRefreshesTravelerAndPreservesCustomRecord()
        {
            var custom = JObject.Parse("{ 'GOOD':'Custom', 'Element':['anemo'], 'ConstellationOrder':['skill','burst'], 'WeaponType':0, 'ConstellationName':['Custom Constellation'] }");
            var traveler = SixElementTraveler();
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), new JObject { ["traveler"] = traveler, ["custom"] = custom }.ToString());

            var records = UpdateAndRead();

            Assert.AreEqual(custom.ToString(Formatting.None), records["custom"].ToString(Formatting.None));
            AssertTraveler(records["traveler"]);
        }

        [TestMethod]
        [Description("AC-3: generate Vesna and Vodyanitsa with exact GOOD names, weapons and talents")]
        public void AC3_GeneratesVesnaAndVodyanitsa()
        {
            var records = UpdateAndRead();
            AssertCharacter(records["vesna"], "Vesna", "anemo", 0, "Amentum Vernum");
            AssertCharacter(records["vodyanitsa"], "Vodyanitsa", "hydro", 4, "Piscicula Aurea");
        }

        [TestMethod]
        [Description("EC-1: source row order and alternate Traveler rows do not change character records")]
        public void EC1_ReversedSourceRowsProduceTheSameCompleteRecords()
        {
            var original = UpdateAndRead();
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), "{}");
            foreach (var file in new[] { "AvatarExcelConfigData.json", "AvatarSkillExcelConfigData.json", "AvatarTalentExcelConfigData.json", "AvatarSkillDepotExcelConfigData.json", "FetterInfoExcelConfigData.json" })
                FixtureRequestFactory.Documents[file] = new JArray(JArray.Parse(FixtureRequestFactory.Documents[file]).Reverse()).ToString(Formatting.None);

            var reversed = UpdateAndRead();

            foreach (var key in new[] { "traveler", "vesna", "vodyanitsa" })
                Assert.AreEqual(Canonical(original[key]), Canonical(reversed[key]), key + " differs after reversing source rows");
            AssertTraveler(reversed["traveler"]);
            AssertCharacter(reversed["vesna"], "Vesna", "anemo", 0, "Amentum Vernum");
            AssertCharacter(reversed["vodyanitsa"], "Vodyanitsa", "hydro", 4, "Piscicula Aurea");
        }

        [TestMethod]
        [Description("ERR-1: missing Cryo C3 fails without modifying the previously saved character cache")]
        public void ERR1_MissingCryoConstellationFailsAndPreservesCacheBytes()
        {
            var path = Path.Combine("inventorylists", "characters.json");
            File.WriteAllText(path, new JObject { ["traveler"] = SixElementTraveler() }.ToString());
            var before = File.ReadAllBytes(path);
            var talents = JArray.Parse(FixtureRequestFactory.Documents["AvatarTalentExcelConfigData.json"]);
            var cryoC3 = talents.Single(row => (string)row["openConfig"] == "Player_Ice_Constellation_3");
            cryoC3.Remove();
            FixtureRequestFactory.Documents["AvatarTalentExcelConfigData.json"] = talents.ToString(Formatting.None);
            var database = new DatabaseManager();

            var status = database.UpdateGameData(force: false);

            CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "A failed source update must preserve the exact saved bytes.");
            Assert.AreEqual("Fail", status.ToString());
        }

        [TestMethod]
        [Description("Item 5b: a constellation raising a renamed Normal Attack (Sandrone's C3) reads as auto")]
        public void ConstellationOrderFollowsTheSkillLink()
        {
            var records = UpdateAndRead();
            Assert.IsTrue(records.ContainsKey("sandrone"), "Sandrone is in the recorded game data");
            CollectionAssert.AreEqual(new[] { "auto", "burst" }, records["sandrone"]["ConstellationOrder"].Values<string>().ToArray());
        }

        [TestMethod]
        [Description("Item 5: a normal update rebuilds a stale saved character and keeps custom records")]
        public void NonForcedUpdateRefreshesStaleCharacter()
        {
            var fresh = UpdateAndRead()["vesna"];
            // Like Kamisato Ayaka's old ["burst","burst"]: a wrong order and a field missing.
            var stale = (JObject)fresh.DeepClone();
            stale["ConstellationOrder"] = new JArray("burst", "burst");
            stale.Remove("ConstellationName");
            var custom = JObject.Parse("{ 'GOOD':'Custom', 'Element':['anemo'], 'ConstellationOrder':['burst','skills'], 'WeaponType':0, 'ConstellationName':['Custom Constellation'] }");
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), new JObject { ["vesna"] = stale, ["custom"] = custom }.ToString());

            var records = UpdateAndRead();

            Assert.AreEqual(Canonical(fresh), Canonical(records["vesna"]));
            Assert.AreEqual(custom.ToString(Formatting.None), records["custom"].ToString(Formatting.None));
        }

        [TestMethod]
        [Description("Item 5: an update with nothing new reports Skipped and leaves the saved table alone")]
        public void SecondUpdateChangesNothing()
        {
            UpdateAndRead();
            var path = Path.Combine("inventorylists", "characters.json");
            var before = File.ReadAllBytes(path);

            var status = new DatabaseManager().UpdateGameData(force: false);

            Assert.AreEqual("Skipped", status.ToString());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [TestMethod]
        [Description("Item 5: a character whose source rows are broken keeps its saved entry")]
        public void BrokenSourceKeepsTheSavedEntry()
        {
            var saved = (JObject)UpdateAndRead()["vesna"].DeepClone();
            saved["ConstellationOrder"] = new JArray("burst", "skill");
            File.WriteAllText(Path.Combine("inventorylists", "characters.json"), new JObject { ["vesna"] = saved }.ToString());
            var talents = JArray.Parse(FixtureRequestFactory.Documents["AvatarTalentExcelConfigData.json"]);
            foreach (var row in talents.Where(row => ((string)row["icon"])?.Contains("Vesna") == true).ToList()) row.Remove();
            FixtureRequestFactory.Documents["AvatarTalentExcelConfigData.json"] = talents.ToString(Formatting.None);

            var records = UpdateAndRead();

            Assert.AreEqual(Canonical(saved), Canonical(records["vesna"]));
        }

        private static Dictionary<string, JObject> UpdateAndRead()
        {
            var database = new DatabaseManager();
            Assert.AreEqual("Success", database.UpdateGameData(force: false).ToString(), "The valid fixture must update successfully.");
            return database.LoadCharacters();
        }

        private static void AssertTraveler(JObject record)
        {
            Assert.AreEqual("Traveler", (string)record["GOOD"]);
            Assert.AreEqual(0, (int)record["WeaponType"]);
            CollectionAssert.AreEquivalent(Elements, record["Element"].Values<string>().ToArray());
            CollectionAssert.AreEquivalent(Elements, ((JObject)record["ConstellationOrder"]).Properties().Select(p => p.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "burst", "skill" }, record["ConstellationOrder"]["cryo"].Values<string>().ToArray());
            CollectionAssert.AreEqual(new[] { "Viator", "Viatrix" }, record["ConstellationName"].Values<string>().ToArray());
        }

        private static void AssertCharacter(JObject record, string good, string element, int weapon, string constellation)
        {
            Assert.AreEqual(good, (string)record["GOOD"]);
            Assert.AreEqual(weapon, (int)record["WeaponType"]);
            CollectionAssert.AreEqual(new[] { element }, record["Element"].Values<string>().ToArray());
            CollectionAssert.AreEqual(new[] { "skill", "burst" }, record["ConstellationOrder"].Values<string>().ToArray());
            CollectionAssert.AreEqual(new[] { constellation }, record["ConstellationName"].Values<string>().ToArray());
        }

        private static JObject SixElementTraveler()
        {
            return JObject.Parse("{ 'GOOD':'Traveler', 'Element':['anemo','geo','electro','dendro','hydro','pyro'], 'ConstellationOrder':{'anemo':['skill','burst'],'geo':['burst','skill'],'electro':['burst','skill'],'dendro':['skill','burst'],'hydro':['skill','burst'],'pyro':['skill','burst']}, 'WeaponType':0, 'ConstellationName':['Viator','Viatrix'] }");
        }

        private static string Canonical(JToken token)
        {
            if (token is JObject obj)
                return "{" + string.Join(",", obj.Properties().OrderBy(p => p.Name).Select(p => JsonConvert.SerializeObject(p.Name) + ":" + Canonical(p.Value))) + "}";
            if (token is JArray array) return "[" + string.Join(",", array.Select(Canonical)) + "]";
            return token.ToString(Formatting.None);
        }

        private static Dictionary<string, string> LoadFixture()
        {
            var result = Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures"), "*.json")
                .ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.Ordinal);
            foreach (var file in new[] { "DisplayItemExcelConfigData.json", "ReliquaryCodexExcelConfigData.json", "ReliquaryExcelConfigData.json", "WeaponExcelConfigData.json", "MaterialExcelConfigData.json" }) result[file] = "[]";
            return result;
        }

        private sealed class FixtureRequestFactory : IWebRequestCreate
        {
            public static Dictionary<string, string> Documents;
            public WebRequest Create(Uri uri)
            {
                var name = Path.GetFileName(uri.AbsolutePath);
                if (!Documents.TryGetValue(name, out var document)) throw new InvalidOperationException("Unexpected fixture URL: " + uri);
                return new FixtureRequest(uri, document);
            }
        }

        private sealed class RejectNetworkFactory : IWebRequestCreate
        {
            public WebRequest Create(Uri uri) { throw new InvalidOperationException("Live network forbidden in integration tests: " + uri); }
        }

        private sealed class FixtureRequest : WebRequest
        {
            private readonly Uri uri;
            private readonly string document;
            public FixtureRequest(Uri uri, string document) { this.uri = uri; this.document = document; }
            public override Uri RequestUri => uri;
            public override WebHeaderCollection Headers { get; set; } = new WebHeaderCollection();
            public override ICredentials Credentials { get; set; }
            public override IWebProxy Proxy { get; set; }
            public override string Method { get; set; } = "GET";
            public override WebResponse GetResponse() { return new FixtureResponse(uri, document); }
        }

        private sealed class FixtureResponse : WebResponse
        {
            private readonly Uri uri;
            private readonly byte[] bytes;
            public FixtureResponse(Uri uri, string document) { this.uri = uri; bytes = Encoding.UTF8.GetBytes(document); }
            public override long ContentLength { get => bytes.Length; set => throw new NotSupportedException(); }
            public override string ContentType { get => "application/json; charset=utf-8"; set => throw new NotSupportedException(); }
            public override Uri ResponseUri => uri;
            public override WebHeaderCollection Headers => new WebHeaderCollection();
            public override Stream GetResponseStream() { return new MemoryStream(bytes, false); }
        }
    }
}
