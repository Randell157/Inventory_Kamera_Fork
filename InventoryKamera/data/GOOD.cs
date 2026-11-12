using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    public class GOOD
    {
        [JsonProperty("format")]
        public string Format { get; private set; }

        [JsonProperty("version")]
        public int Version { get; private set; }

        [JsonProperty("kamera_version")]
        public string AppVersion { get; private set; }

        [JsonProperty("source")]
        public string Source { get; private set; }

        [JsonProperty("weapons", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Weapon> Weapons { get; private set; }

        [JsonProperty("artifacts", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Artifact> Artifacts { get; private set; }

        [JsonProperty("characters", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Character> Characters { get; private set; }

        [JsonProperty("materials", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, int> Materials { get; private set; }

        public GOOD()
        {
            Format = "EMPTY";
            Version = 0;
            Source = "NOT FILLED";
        }

        public GOOD(InventoryKamera genshinData) : this()
        {
            // Get rid of VS warning since we are converting this class to JSON
            Format = "GOOD";
            Version = 3;
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Source = "Inventory_Kamera";

            // Assign Characters
            if (genshinData.Characters.Count > 0)
            {
                Characters = new List<Character>(genshinData.Characters);

                if (!Properties.Settings.Default.EquipWeapons)
                {
                    foreach (Character character in Characters) character.Weapon = null;
                }
            }



            // Assign Weapons
            if (genshinData.Inventory.Weapons.Count > 0)
            {
                Weapons = new List<Weapon>(genshinData.Inventory.Weapons);

                if (!Properties.Settings.Default.EquipWeapons)
                {
                    foreach (Weapon weapon in Weapons) weapon.EquippedCharacter = "";
                }
            }

            // Assign Artifacts
            if (genshinData.Inventory.Artifacts.Count > 0)
            {
                Artifacts = new List<Artifact>(genshinData.Inventory.Artifacts);

                if (!Properties.Settings.Default.EquipArtifacts)
                {
                    foreach (Artifact artifact in Artifacts) artifact.EquippedCharacter = "";
                }
            }

            // Assign materials
            if (genshinData.Inventory.AllMaterials.Count > 0) Materials = new Dictionary<string, int>();
            // A count of 0 means the quantity couldn't be read (owned items are never 0 in the inventory).
            // Leave it out rather than tell importers the item is gone. The scan log still reports it.
            // (Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11, MIT.)
            genshinData.Inventory.AllMaterials.Where(material => material.count > 0).ToList()
                .ForEach(material => Materials.Add(material.name, material.count));
        }

        /// <summary>
        /// Text for the scan-complete popup: what was exported, where, and how many errors were reported.
        /// </summary>
        internal string CompletionSummary(string outputFile, int errorCount)
        {
            var parts = new List<string>();
            void Add(int count, string one, string many) { if (count > 0) parts.Add($"{count} {(count == 1 ? one : many)}"); }
            Add(Characters?.Count ?? 0, "character", "characters");
            Add(Weapons?.Count ?? 0, "weapon", "weapons");
            Add(Artifacts?.Count ?? 0, "artifact", "artifacts");
            Add(Materials?.Count ?? 0, "material", "materials");

            var lines = new List<string>
            {
                parts.Count > 0 ? "Exported: " + string.Join(", ", parts) : "Nothing was exported.",
                "",
                "File: " + Path.GetFileName(outputFile),
                "Folder: " + Path.GetDirectoryName(outputFile),
                "",
                errorCount == 0 ? "No errors."
                    : $"{errorCount} {(errorCount == 1 ? "error" : "errors")}: see the error log in the window.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Writes the export and returns the full path of the file.</summary>
        internal string WriteToJSON(string outputDirectory)
        {
            // Creates directory if doesn't exist
            Directory.CreateDirectory(outputDirectory);

            // Create file with timestamp in name
            string fileName = $"genshinData_GOOD_{DateTime.Now.ToString("yyyy_MM_dd_HH_mm")}.json";
            string outputFile = Path.Combine(outputDirectory, fileName);

            // Write file
            WriteToJson(outputFile);


            if (!File.Exists(outputFile)) // did not make file
            {
                UserInterface.AddError($"Failed to output at : {outputDirectory}");
            }
            return outputFile;
        }

        private void WriteToJson(string outputFile)
        {
            using (var streamWriter = new StreamWriter(outputFile))
            {
                streamWriter.WriteLine(ToString());
            }
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this).ToString();
        }
    }
}