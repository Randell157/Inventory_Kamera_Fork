using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryKamera
{
    public class InventoryKamera
	{

		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		[JsonProperty]
		public List<Character> Characters;

		[JsonProperty]
		public Inventory Inventory;

		private List<Artifact> equippedArtifacts;
		private List<Weapon> equippedWeapons;
		public static Queue<OCRImageCollection> workerQueue;
		private List<Thread> ImageProcessors;

		private WeaponScraper weaponScraper;
		private ArtifactScraper artifactScraper;
		private CharacterScraper characterScraper;
		private MaterialScraper materialScraper;

		private volatile bool b_threadCancel;
		private readonly int NumWorkers;

		public bool HasData
        {
			get { return Characters.Count > 0 || Inventory.Size > 0; }
        }

		public InventoryKamera()
		{
			Characters = new List<Character>();
			Inventory = new Inventory();
			equippedArtifacts = new List<Artifact>();
			equippedWeapons = new List<Weapon>();
			ImageProcessors = new List<Thread>();
			workerQueue = new Queue<OCRImageCollection>();

			weaponScraper = new WeaponScraper();
			artifactScraper = new ArtifactScraper();
			characterScraper = new CharacterScraper();
			materialScraper = new MaterialScraper();

			b_threadCancel = false;

			NumWorkers = ScanSpeeds.Workers(Properties.Settings.Default.ScanSpeed);
			Logger.Info("Kamera initialized");
		}

		public void ResetLogging()
		{
			// Name the step while it runs: it's usually milliseconds, but it once took 25 s,
			// and "Scanning" with nothing happening in the game looked like the app was stuck.
			UserInterface.SetProgramStatus("Clearing files…", UserInterface.PreparingColor);
			ResetLoggingFolders("./logging");
			Logger.Info("Logging directory reset");
			UserInterface.SetProgramStatus("Scanning");
		}

		private static readonly string[] ScreenshotFolders = { "weapons", "artifacts", "characters", "materials" };

		// Empties the previous scan's screenshot folders without making the scan wait. A full scan
		// leaves 20,000+ files, and deleting them took long enough that the scan looked stuck.
		// Each folder is renamed aside (instant) and deleted in the background; folders left
		// from an earlier run are deleted too. Even counting the old files took seconds, so that
		// happens in the background as well. Returns the background deletion, which callers
		// don't need to wait for; its result is the number of old files it found.
		internal static Task<int> ResetLoggingFolders(string root)
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			Directory.CreateDirectory(root);

			foreach (string name in ScreenshotFolders)
			{
				string folder = Path.Combine(root, name);
				if (Directory.Exists(folder))
				{
					try
					{
						Directory.Move(folder, Path.Combine(root, $"old-{name}-{Guid.NewGuid():N}"));
					}
					catch (Exception ex)
					{
						// A file held open elsewhere can block the rename. Delete what can be deleted,
						// so this folder starts as empty as possible and the other folders still reset.
						Logger.Warn("Could not move {0} aside ({1}); deleting its files instead", folder, ex.Message);
						DeleteContents(folder);
					}
				}
				Directory.CreateDirectory(folder);
			}

			string[] old = Directory.GetDirectories(root, "old-*");
			Logger.Info("Previous screenshots moved aside in {0} ms; deleting them in the background", watch.ElapsedMilliseconds);

			return Task.Run(() =>
			{
				int oldFiles = 0;
				foreach (string folder in old)
				{
					try { oldFiles += Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count(); }
					catch (Exception) { }
					try { Directory.Delete(folder, true); }
					catch (Exception ex) { Logger.Debug("Old screenshots in {0} not fully deleted: {1}", folder, ex.Message); }
				}
				Logger.Info("Cleared previous screenshots ({0} files)", oldFiles);
				return oldFiles;
			});
		}

		private static void DeleteContents(string folder)
		{
			foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
			{
				try { File.Delete(file); } catch (Exception) { }
			}
			foreach (string dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
			{
				try { Directory.Delete(dir, false); } catch (Exception) { }
			}
		}

		public void StopImageProcessorWorkers()
		{
			b_threadCancel = true;
			AwaitProcessors();
			workerQueue = new Queue<OCRImageCollection>();
		}

		public void GatherData()
		{

			ResetLogging();

			GenshinProcesor.ReloadData();

			// Initize Image Processors
			for (int i = 0; i < NumWorkers; i++)
			{
				Thread processor = new Thread(ImageProcessorWorker){ IsBackground = true };
				processor.Start();
				ImageProcessors.Add(processor);
			}
			Logger.Debug("Added {ImageProcessors.Count} workers", ImageProcessors.Count);

			GenshinProcesor.RestartEngines();


			// Assign Traveler's custom name
			GenshinProcesor.AssignTravelerName(Properties.Settings.Default.TravelerName);

            // Assign Wanderer's custom name
            GenshinProcesor.UpdateCharacterName("wanderer", CustomNames.OrDefault("WandererName", Properties.Settings.Default.WandererName));

			// Mannequins are skipped by the character scan, but their custom names still need
			// matching. GenshinProcesor.ReloadData adds their entries, so these can't fail.
			GenshinProcesor.UpdateCharacterName("manequin1", CustomNames.OrDefault("Manequin1Name", Properties.Settings.Default.Manequin1Name));
			GenshinProcesor.UpdateCharacterName("manequin2", CustomNames.OrDefault("Manequin2Name", Properties.Settings.Default.Manequin2Name));

            if (Properties.Settings.Default.ScanWeapons)
			{
				Logger.Info("Scanning weapons...");
				// Get Weapons
				if (OpenTabOrReport(InventoryPage.Weapons, "Weapons"))
				try
				{
                    weaponScraper.ScanWeapons();
				}
				catch (FormatException ex) { UserInterface.AddError(ex.Message); }
				catch (ThreadAbortException) { }
				catch (Exception ex)
				{
					UserInterface.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info(weaponScraper.CardSettleSummary("weapons"));
				Logger.Info("Done scanning weapons");
			}

			if (Properties.Settings.Default.ScanArtifacts)
			{
				Logger.Info("Scanning artifacts...");

				// Get Artifacts
				if (OpenTabOrReport(InventoryPage.Artifacts, "Artifacts"))
				try
				{
					artifactScraper.ScanArtifacts();
				}
				catch (FormatException ex) { UserInterface.AddError(ex.Message); }
				catch (ThreadAbortException) { }
				catch (Exception ex)
				{
					UserInterface.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info(artifactScraper.CardSettleSummary("artifacts"));
				Logger.Info("Done scanning artifacts");
			}

			workerQueue.Enqueue(new OCRImageCollection(null, "END", 0));

			if (Properties.Settings.Default.ScanCharacters)
			{
				Logger.Info("Scanning characters...");
				Logger.Info("Switching to party slot 1 (slot 1 key: {0})", Navigation.slotOneKey);
				// Get characters
				Navigation.CharacterScreen();
				ScreenSettle.Reset();
				try
				{
					characterScraper.ScanCharacters(ref Characters);
				}
				catch (ThreadAbortException) { }
				catch (Exception ex)
				{
					UserInterface.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info(ScreenSettle.Summary());
				Logger.Info("Done scanning characters");
			}

			// Wait for Image Processors to finish
			AwaitProcessors();

			if (Properties.Settings.Default.ScanCharacters)
			{
				// Assign Artifacts to Characters
				if (Properties.Settings.Default.ScanArtifacts)
					AssignArtifacts();
				if (Properties.Settings.Default.ScanWeapons)
					AssignWeapons();
			}

			// Scan Character Development Items
			if (Properties.Settings.Default.ScanCharDevItems)
			{
				Logger.Info("Scanning character development materials...");
				// Get Materials
				HashSet<Material> devItems = new HashSet<Material>();
				if (OpenTabOrReport(InventoryPage.CharacterDevelopmentItems, "Character Development Items"))
				try
				{
					materialScraper.SetInventoryPage(InventoryPage.CharacterDevelopmentItems);
					materialScraper.Scan_Materials(ref Inventory);
				}
				catch (FormatException ex) { UserInterface.AddError(ex.Message); }
				catch (ThreadAbortException) { }
				catch (Exception ex)
				{
					UserInterface.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning character development materials");
			}

			// Scan Materials
			if (Properties.Settings.Default.ScanMaterials)
			{
				Logger.Info("Scanning materials...");
				// Get Materials
				HashSet<Material> materials = new HashSet<Material>();
				if (OpenTabOrReport(InventoryPage.Materials, "Materials"))
				try
				{
					materialScraper.SetInventoryPage(InventoryPage.Materials);
					materialScraper.Scan_Materials(ref Inventory);
				}
				catch (FormatException ex) { UserInterface.AddError(ex.Message); }
				catch (ThreadAbortException) { }
				catch (Exception ex)
				{
					UserInterface.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning materials");
			}
		}

		/// <summary>
		/// Opens an inventory tab and confirms it by name. If the tab can't be confirmed, reports an error
		/// and returns false so the caller skips that section instead of scanning the wrong tab.
		/// </summary>
		private static bool OpenTabOrReport(InventoryPage page, string tabName)
		{
			if (Navigation.OpenInventoryTab(page)) return true;
			UserInterface.AddError($"Couldn't switch to the {tabName} tab, so it was skipped. See logging\\tab-check-failed-{page}.png");
			return false;
		}

		private void AwaitProcessors()
		{
			while (ImageProcessors.Count > 0)
			{
				ImageProcessors.RemoveAll(process => !process.IsAlive);
			}
			b_threadCancel = false;
		}

		public void ImageProcessorWorker()
		{
			Logger.Debug("Thread #{0} priority: {1}", Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Priority);
			while (true)
			{
				if (b_threadCancel)
				{
					workerQueue.Clear();
					break;
				}

				if (workerQueue.TryDequeue(out OCRImageCollection imageCollection))
				{
					switch (imageCollection.Type)
					{
						case "weapon":
							if (weaponScraper.IsEnhancementMaterial(imageCollection.Bitmaps.First()))
							{
								Logger.Debug("Enhancement Material found for weapon #{weaponID}", imageCollection.Id);
								weaponScraper.StopScanning = true;
								break;
							}

							UserInterface.SetGearPictureBox(imageCollection.Bitmaps.Last());

							// Scan as weapon
							Weapon weapon = weaponScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id).Result;
							UserInterface.SetGear(imageCollection.Bitmaps.Last(), weapon);

							string weaponPath = $"./logging/weapons/weapon{weapon.Id}/";

							if (Properties.Settings.Default.LogScreenshots) Directory.CreateDirectory(weaponPath);

							if (weapon.IsValid())
							{
								UserInterface.IncrementWeaponCount();
								Inventory.Add(weapon);
								if (!string.IsNullOrWhiteSpace(weapon.EquippedCharacter))
									equippedWeapons.Add(weapon);
							}
							else
							{
								UserInterface.AddError($"Unable to validate information for weapon ID#{weapon.Id}");
								string error = "";
								if (!weapon.HasValidWeaponName()) error += "Invalid weapon name\n"; 
								if (!weapon.HasValidRarity()) error += "Invalid weapon rarity\n";
								if (!weapon.HasValidLevel()) error += "Invalid weapon level\n";
								if (!weapon.HasValidRefinementLevel()) error += "Invalid refinement level\n";
								if (!weapon.HasValidEquippedCharacter()) error += "Inavlid equipped character\n";
								UserInterface.AddError(error + weapon.ToString());
								Directory.CreateDirectory(weaponPath);
								using (var writer = File.CreateText(weaponPath + "log.txt"))
								{
									writer.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!weapon.IsValid() || Properties.Settings.Default.LogScreenshots)
                            {
                                Directory.CreateDirectory(weaponPath + "name");
                                imageCollection.Bitmaps[0].Save(weaponPath + "name/name.png");
                                Directory.CreateDirectory(weaponPath + "rarity");
                                imageCollection.Bitmaps[0].Save(weaponPath + "rarity/rarity.png");
                                Directory.CreateDirectory(weaponPath + "level");
                                imageCollection.Bitmaps[1].Save(weaponPath + "level/level.png");
                                Directory.CreateDirectory(weaponPath + "refinement");
                                imageCollection.Bitmaps[2].Save(weaponPath + "refinement/refinement.png");
                                Directory.CreateDirectory(weaponPath + "equipped");
                                imageCollection.Bitmaps[4].Save(weaponPath + "equipped/equipped.png");

                                imageCollection.Bitmaps.Last().Save(weaponPath + "card.png");
								Task.Run(() => LogObject(weapon, weaponPath + "weapon.json"));
                            }

                            // Dispose of everything
                            imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						case "artifact":
							if (ArtifactScraper.IsEnhancementMaterial(imageCollection.Bitmaps.Last()))
							{
								Logger.Debug("Enhancement Material found for artifact #{artifactID}", imageCollection.Id);
								artifactScraper.StopScanning = true;
								break;
							}

							UserInterface.SetGearPictureBox(imageCollection.Bitmaps.Last());
							// Scan as artifact
							Artifact artifact = ArtifactScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id).Result;
							UserInterface.SetGear(imageCollection.Bitmaps.Last(), artifact);

							string artifactPath = $"./logging/artifacts/artifact{artifact.Id}/";

                            if (Properties.Settings.Default.LogScreenshots) Directory.CreateDirectory(artifactPath);

							if (artifact.IsValid())
							{
								UserInterface.IncrementArtifactCount();
								Inventory.Add(artifact);
								if (!string.IsNullOrWhiteSpace(artifact.EquippedCharacter))
									equippedArtifacts.Add(artifact);
							}
							else
							{
								UserInterface.AddError($"Unable to validate information for artifact ID#{artifact.Id}");
								string error = "";
								if (!artifact.HasValidSlot()) error += "Invalid artifact gear slot\n";
								if (!artifact.HasValidSetName()) error += "Invalid artifact set name\n";
								if (!artifact.HasValidRarity()) error += "Invalid artifact rarity\n";
								if (!artifact.HasValidLevel()) error += "Invalid artifact level\n";
								if (!artifact.HasValidMainStat()) error += "Invalid artifact main stat\n";
								if (!artifact.HasValidSubStats()) error += "Invalid artifact sub stats\n";
								if (!artifact.HasValidEquippedCharacter()) error += "Invalid equipped character\n";
								UserInterface.AddError(error + artifact.ToString());
								Directory.CreateDirectory(artifactPath);
								using (var writer = File.CreateText(artifactPath + "log.txt"))
								{
									writer.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error Log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!artifact.IsValid() || Properties.Settings.Default.LogScreenshots)
                            {
                                Directory.CreateDirectory(artifactPath + "name");
                                imageCollection.Bitmaps[0].Save(artifactPath + "name/name.png");
                                Directory.CreateDirectory(artifactPath + "slot");
								imageCollection.Bitmaps[1].Save(artifactPath + "slot/slot.png");
                                Directory.CreateDirectory(artifactPath + "mainstat");
                                imageCollection.Bitmaps[2].Save(artifactPath + "mainstat/mainstat.png");
								Directory.CreateDirectory(artifactPath + "level");
								imageCollection.Bitmaps[3].Save(artifactPath + "level/level.png");
								Directory.CreateDirectory(artifactPath + "substats");
								imageCollection.Bitmaps[4].Save(artifactPath + "substats/substats.png");
								Directory.CreateDirectory(artifactPath + "equipped");
								imageCollection.Bitmaps[5].Save(artifactPath + "equipped/equipped.png");
								Directory.CreateDirectory(artifactPath + "locked");
								imageCollection.Bitmaps[6].Save(artifactPath + "locked/locked.png");
                                Directory.CreateDirectory(artifactPath + "sanctify");
                                imageCollection.Bitmaps[7].Save(artifactPath + "sanctify/sanctify.png");


                                imageCollection.Bitmaps.Last().Save(artifactPath + "card.png");

								Task.Run(()=>LogObject(artifact, artifactPath + "artifact.json"));
							}

							// Dispose of everything
							imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						case "END":
							b_threadCancel = true;
							break;

						default:
							ScanSession.UnexpectedError("Unknown Image type for Image Processor");
							break;
					}
				}
				else
				{
					// Wait for more images to process
					Thread.Sleep(250);
				}
			}
			Logger.Debug("Thread {threadId} exit", Thread.CurrentThread.ManagedThreadId);
		}

        private static void LogObject(object obj, string path)
        {
            using (var file = new StreamWriter(path))
            {
                var serializer = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };
                serializer.Serialize(file, obj);
            }
        }

        public void AssignArtifacts()
		{
			foreach (Artifact artifact in equippedArtifacts)
			{
				foreach (Character character in Characters)
				{
					if (artifact.EquippedCharacter == character.LocationGOOD)
					{
						character.AssignArtifact(artifact); // Do we even need to do this?
						Logger.Debug("Assigned {fearSlot} to {character}", artifact.GearSlot, character.NameGOOD);
						break;
					}
				}
			}
		}

		public void AssignWeapons()
		{
			var defaults = AssignWeapons(Characters, equippedWeapons, Inventory.Weapons, character => character.WeaponType);
			foreach (Weapon weapon in defaults) Inventory.Add(weapon);
		}

		// Gives each character its scanned weapon. A character whose weapon wasn't scanned
		// (filtered out by rarity or level) gets a starter weapon instead, numbered after the
		// scanned weapons so every weapon id stays unique. Returns the starter weapons.
		internal static List<Weapon> AssignWeapons(IEnumerable<Character> characters, IEnumerable<Weapon> equippedWeapons,
			IEnumerable<Weapon> scannedWeapons, Func<Character, WeaponType> weaponTypeOf)
		{
			int nextId = scannedWeapons.Select(w => w.Id).DefaultIfEmpty(-1).Max() + 1;
			var defaults = new List<Weapon>();

			foreach (Character character in characters)
			{
				Weapon weapon = equippedWeapons.FirstOrDefault(w => w.EquippedCharacter == character.LocationGOOD);
				if (weapon != null)
				{
					character.AssignWeapon(weapon);
					Logger.Debug("Assigned {weapon} to {character}", weapon.Name, character.NameGOOD);
				}
				else
				{
					defaults.Add(new Weapon(weaponTypeOf(character), character.LocationGOOD, nextId++));
					Logger.Info("Default weapon assigned to {character}", character.NameGOOD);
				}
			}
			return defaults;
		}
	}
}