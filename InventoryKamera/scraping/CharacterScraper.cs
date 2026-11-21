using Accord.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    internal class CharacterScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		protected int NumOfCharToScan;

        public CharacterScraper()
		{
			NumOfCharToScan = Properties.Settings.Default.NumOfCharToScan;
		}

		public void ScanCharacters(ref List<Character> Characters)
		{
			int viewed = 0;
			int counter = 0;
			string first = null;
			HashSet<string> scanned = new HashSet<string>();

			UserInterface.ResetCharacterDisplay();

			while (true)
			{
				var character = ScanCharacter(first);
				if (Character.IsMannequin(character.NameGOOD))
				{
					Logger.Info("Skipped {0}: Genshin Optimizer doesn't support mannequins", character.NameGOOD);
				}
				else
				{
                    if (Characters.Count > 0 && character.NameGOOD == Characters.ElementAt(0).NameGOOD) break;
                    if (character.IsValid())
                    {
                        if (!scanned.Contains(character.NameGOOD))
                        {
                            Characters.Add(character);
                            UserInterface.IncrementCharacterCount();
                            counter++;
                            Logger.Info("Scanned {0} successfully", character.NameGOOD);
                            if (Characters.Count == 1) first = character.NameGOOD;
                        }
                        else
                        {
                            Logger.Info("Prevented {0} duplicate scan", character.NameGOOD);
                        }
                    }
                    else
                    {
                        string error = "";
                        if (!character.HasValidName()) error += "Invalid character name\n";
                        if (!character.HasValidLevel()) error += "Invalid level\n";
                        if (!character.HasValidElement()) error += "Invalid element\n";
                        if (!character.HasValidConstellation()) error += "Invalid constellation\n";
                        if (!character.HasValidTalents()) error += "Invalid talents\n";
                        Logger.Error("Failed to scan character\n" + error + character);
                    }
                }

                Navigation.SelectNextCharacter();
				UserInterface.ResetCharacterDisplay();

				if ((++viewed > 3 && Characters.Count < 1) || (NumOfCharToScan !=0 && counter >= NumOfCharToScan)) break;
			}

			// Childe passive buff fix
			for (int i = 0; i < Characters.Count; i++)
			{
				if (Characters[i].NameGOOD.ToLower() == "tartaglia" && Characters[i].Ascension >= 4)
				{
					Logger.Info("Ascension 4+ Tartaglia found at position {0}.", i);
					if (i < 4)
					{
						for (int j = 0; j < 4; j++)
						{
							Characters[j].Talents["auto"] -= 1;
							Logger.Info("Applied Tartaglia auto attack fix to {0} at position {1}.", Characters[j].NameGOOD, j);
						}
						break;
					}
					else
					{
						Characters[i].Talents["auto"] -= 1;
						Logger.Info("Applied Tartaglia auto attack fix to self only.");
						break;
					}
				}
				else if (Characters[i].NameGOOD.ToLower() == "tartaglia") break;
            }
		}

		private static Character ScanCharacter(string firstCharacter)
		{
			var character = new Character();
			Navigation.SelectCharacterAttributes();
			string name = null;
			string element = null;

			// Scan the Name and element of Character. Attempt 75 times max.
			ScanNameAndElement(ref name, ref element);

			if(name == "Manequin1" || name == "Manequin2")
			{
                character.NameGOOD = name;
				return character;
            }

            if (string.IsNullOrWhiteSpace(name))
			{
				if (string.IsNullOrWhiteSpace(name)) UserInterface.AddError("Could not determine character's name");
				if (string.IsNullOrWhiteSpace(element)) UserInterface.AddError("Could not determine character's element");
				return character;
			}

			character.NameGOOD = name;
			character.Element = element;

			// Check if character was first scanned
			if (character.NameGOOD != firstCharacter)
			{
				bool ascended = false;
				// Scan Level and ascension
				int level = ScanLevel(ref ascended, out bool capUncertain);
				if (level == -1)
				{
					UserInterface.AddError($"Could not determine {character.NameGOOD}'s level. Setting to 1.");
					level = 1;
					ascended = false;
				}
				else if (capUncertain)
				{
					UserInterface.AddError($"Could not read {character.NameGOOD}'s level cap. Exported level {level} as not ascended; please check their ascension.");
				}
				character.Level = level;
				character.Ascended = ascended;

				Logger.Info("{0} Level: {1}", character.NameGOOD, character.Level);
				Logger.Info("{0} Ascended: {1}", character.NameGOOD, character.Ascended);

				// Scan Experience
				//experience = ScanExperience();
				//Navigation.SystemRandomWait(Navigation.Speed.Normal);

				// Scan Constellation
				if (!Navigation.SelectCharacterConstellation())
				{
					// Clicking nodes on another screen read Chiori and Navia as C6 (2026-11-21); leave them out instead.
					UserInterface.AddError($"Couldn't open {character.NameGOOD}'s constellations; skipped them. Try a slower scan speed if this repeats.");
					Logger.Error("Couldn't open {0}'s constellation screen; character skipped", character.NameGOOD);
					// Keep what the game showed instead, so a failure can be checked rather than guessed.
					try
					{
						Directory.CreateDirectory($"./logging/characters/{character.NameGOOD}");
						using (var screen = Navigation.CaptureWindow())
							screen.Save($"./logging/characters/{character.NameGOOD}/open-constellation-failed.png");
					}
					catch (Exception e) { Logger.Debug("Couldn't save the failure screenshot: {0}", e.Message); }
					character.Constellation = -1;
					return character;
				}
				character.Constellation = ScanConstellations(character);
				Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);

				// Scan Talents
				Navigation.SelectCharacterTalents();
				character.Talents = ScanTalents(character, BoostedTalents(character, name));
				Logger.Info("{0} Talents: {1}", character.NameGOOD, "{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");

				// Scale down talents due to constellations
				if (character.Constellation >= 3)
				{
					if (GenshinProcesor.Characters.ContainsKey(name.ToLower()))
					{
						string talentLeveledAtConst3 = character.NameGOOD.Contains("Traveler")
                            ? (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][character.Element.ToLower()][0]
                            : (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][0];
                        string talentLeveledAtConst5 = character.NameGOOD.Contains("Traveler")
							? (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][character.Element.ToLower()][1]
							: (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][1];

						// Scale down talents
						if (character.Constellation >= 3)
						{
							Logger.Info("{0} constellation 3+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst3);
							character.Talents[talentLeveledAtConst3] -= 3;
						}

						if (character.Constellation >= 5)
						{
                            Logger.Info("{0} constellation 5+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst5);
                            character.Talents[talentLeveledAtConst5] -= 3;
						}
					}
					else
						return character;
				}


				return character;
			}
			Logger.Info("Repeat character {0} detected. Finishing character scan...", name);
			return character;
		}

		public static string ScanMainCharacterName()
		{
			var xReference = 1280.0;
			var yReference = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			RECT region = new RECT(
				Left:   (int)(185 / xReference * Navigation.GetWidth()),
				Top:    (int)(26  / yReference * Navigation.GetHeight()),
				Right:  (int)(460 / xReference * Navigation.GetWidth()),
				Bottom: (int)(60  / yReference * Navigation.GetHeight()));

			Bitmap nameBitmap = Navigation.CaptureRegion(region);

			//Image Operations
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameBitmap);
			GenshinProcesor.SetInvert(ref nameBitmap);
			Bitmap n = GenshinProcesor.ConvertToGrayscale(nameBitmap);

			UserInterface.SetNavigation_Image(nameBitmap);

			string text = GenshinProcesor.AnalyzeText(n).Trim();
			if (text != "")
			{
				// Only keep a-Z and 0-9
				text = Regex.Replace(text, @"[\W_]", string.Empty).ToLower();

				// Only keep text up until first space
				text = Regex.Replace(text, @"\s+\w*", string.Empty);

			}
			else
			{
				UserInterface.AddError(text);
			}
			n.Dispose();
			nameBitmap.Dispose();
			return text;
		}

		private static void ScanNameAndElement(ref string name, ref string element)
		{
			int attempts = 0;
			int maxAttempts = 75;
			Rectangle region = new RECT(
				Left:   (int)( 85  / 1280.0 * Navigation.GetWidth() ),
				Top:    (int)( 10  / 720.0 * Navigation.GetHeight() ),
				// Wide enough for "Anemo / Kaedehara Kazuha", ending in the gap before the first portrait.
				// The Q key badge right after the name is cut off in ReadNameAndElement.
				Right:  (int)( 355 / 1280.0 * Navigation.GetWidth() ),
				Bottom: (int)( 55  / 720.0 * Navigation.GetHeight() ));

			do
			{
				Navigation.SystemWait(Navigation.Speed.Fast);
				using (Bitmap bm = Navigation.CaptureRegion(region))
				{
					ReadNameAndElement(bm, out name, out element);

					if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element))
					{
						Logger.Debug("Scanned character name as {0} with element {1}", name, element);
                        UserInterface.SetCharacter_NameAndElement(bm, name, element);
						return;
					}
					else
                    {
                        Logger.Debug("Could not parse character name/element (Atempt {0}/{1}). Retrying...", attempts+1, maxAttempts);
                        bm.Save($"./logging/characters/{bm.GetHashCode()}.png");
                    }
				}
				attempts++;
				Navigation.SystemWait(Navigation.Speed.Fast);
			} while ( attempts < maxAttempts );
			name = null;
			element = null;
		}

		// Reads "<Element> / <Name>" from the character screen's name area. Leaves name and element
		// empty when they can't be read or don't belong together.
		internal static void ReadNameAndElement(Bitmap bm, out string name, out string element)
		{
			ParseNameAndElement(ReadNameText(bm), out name, out element);
		}

		// "<element> / <name>" as read by OCR -> character and element, or both empty.
		internal static void ParseNameAndElement(string nameAndElement, out string name, out string element)
		{
			name = ""; element = "";
			if (nameAndElement.Contains("/"))
			{
				var split = nameAndElement.Split('/');

				// Long name characters might look like
				// <Element>   <First Name>
				// /           <Last Name>
				// so the element is the first word. Anything that isn't a letter is dropped first: the edge
				// of the element emblem is often read as "»" or ">" ("» cryo / skirk", 2026-11-21).
				var words = Regex.Replace(split[0], @"[^\p{L}\s]", string.Empty)
					.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				element = words.Length > 0 ? GenshinProcesor.FindElementByName(words[0]) : "";

				// Find character based on string after /
				// Long name characters might search by their last name only but it'll still work.
				name = GenshinProcesor.FindClosestCharacterName(Regex.Replace(split[1], @"[\W]", string.Empty));

				if (!GenshinProcesor.CharacterMatchesElement(name, element)) { name = ""; element = ""; }
			}
		}

		// OCR of the name area, lower case, cut off before the Q key badge.
		internal static string ReadNameText(Bitmap bm)
		{
			int badge = KeyBadgeColumn(bm);
			Bitmap cut = badge > 0 ? bm.Clone(new Rectangle(0, 0, badge, bm.Height), bm.PixelFormat) : (Bitmap)bm.Clone();

			Bitmap n = GenshinProcesor.ConvertToGrayscale(cut);
			cut.Dispose();
			GenshinProcesor.SetThreshold(110, ref n);
			GenshinProcesor.SetInvert(ref n);

			n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
			string block = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.Auto).ToLower().Trim();
			string line = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleLine).ToLower().Trim();
			n.Dispose();

			// OCR sometimes puts a "/" before the element ("/anemo / kaedehara kazuha"), which would
			// split off an empty element. Nothing is drawn there, so drop it.
			block = block.TrimStart('/', ' ', '\t', '\n', '\r');
			line = line.TrimStart('/', ' ', '\t');

			// Characters with wrapped names will not have a slash
			return line.Contains("/") ? line : block;
		}

		/// <summary>
		/// The first column of the white Q key badge that follows the character's name, or -1 if there is none.
		/// The badge is pure white; the name text is beige and never brighter than about 216, so a column
		/// with many pixels above 235 in every channel is the badge. Everything from there on is not the name.
		/// </summary>
		internal static int KeyBadgeColumn(Bitmap bm)
		{
			var whole = new Rectangle(0, 0, bm.Width, bm.Height);
			var data = bm.LockBits(whole, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
			try
			{
				var px = new byte[data.Stride * bm.Height];
				System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
				int needed = Math.Max(4, bm.Height * 15 / 100);
				for (int x = 0; x < bm.Width; x++)
				{
					int white = 0;
					for (int y = 0; y < bm.Height; y++)
					{
						int i = y * data.Stride + x * 3;
						if (px[i] > 235 && px[i + 1] > 235 && px[i + 2] > 235) white++;
					}
					if (white >= needed) return x;
				}
				return -1;
			}
			finally
			{
				bm.UnlockBits(data);
			}
		}

		/// <param name="capUncertain">True when the level was read but no valid cap was found in any attempt.
		/// The level is then returned and the character is treated as not ascended.</param>
		private static int ScanLevel(ref bool ascended, out bool capUncertain)
		{
            int attempt = 0;
			int levelWithoutCap = 0;
			capUncertain = false;

            var xRef = 1280.0;
			var yRef = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yRef = 800.0;
			}

			Rectangle region =  new RECT(
				Left:   (int)( 960  / xRef * Navigation.GetWidth() ),
				Top:    (int)( 135  / yRef * Navigation.GetHeight() ),
				Right:  (int)( 1125 / xRef * Navigation.GetWidth() ),
				Bottom: (int)( 163  / yRef * Navigation.GetHeight() ));

			do
			{
				Bitmap bm = Navigation.CaptureRegion(region);

				bm = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = GenshinProcesor.ConvertToGrayscale(bm);
				GenshinProcesor.SetInvert(ref n);
				GenshinProcesor.SetContrast(30.0, ref bm);

				string text = GenshinProcesor.AnalyzeText(n).Trim();
				Logger.Debug("Scanned character level as {0}", text);

				// Accept only a real cap that fits the level (e.g. "80 / 9C" -> 80/90), never a guessed one.
				var result = LevelReading.Read(text, LevelReading.CharacterCaps, out int level, out int maxLevel);
				if (result == LevelReadResult.Valid)
				{
					ascended = LevelReading.IsAscended(level, maxLevel);
					UserInterface.SetCharacter_Level(bm, level, maxLevel);
					n.Dispose();
					bm.Dispose();
					Logger.Debug("Parsed character level as {0} / {1}", level, maxLevel);
					return level;
				}
				if (result == LevelReadResult.LevelOnly) levelWithoutCap = level;
				Logger.Debug("Failed to parse character level and ascension from {0} (text), retrying", text);

				attempt++;

                n.Dispose();
                bm.Dispose();
                Navigation.SystemWait(Navigation.Speed.Fast);
			} while (attempt < 50);

			if (levelWithoutCap > 0)
			{
				// Keep the level we could read rather than dropping to level 1; the caller reports it.
				capUncertain = true;
				ascended = false;
				return levelWithoutCap;
			}
			return -1;
		}

		private static int ScanExperience()
		{
			int experience = 0;

			int xOffset = 1117;
			int yOffset = 151;
			Bitmap bm = new Bitmap(90, 10);
			Graphics g = Graphics.FromImage(bm);
			int screenLocation_X = Navigation.GetPosition().Left + xOffset;
			int screenLocation_Y = Navigation.GetPosition().Top + yOffset;
			g.CopyFromScreen(screenLocation_X, screenLocation_Y, 0, 0, bm.Size);

			//Image Operations
			bm = GenshinProcesor.ResizeImage(bm, bm.Width * 6, bm.Height * 6);
			//Scraper.ConvertToGrayscale(ref bm);
			//Scraper.SetInvert(ref bm);
			GenshinProcesor.SetContrast(30.0, ref bm);

			string text = GenshinProcesor.AnalyzeText(bm);
			text = text.Trim();
			text = Regex.Replace(text, @"(?![0-9\s/]).", string.Empty);

			if (Regex.IsMatch(text, "/"))
			{
				string[] temp = text.Split('/');
				experience = Convert.ToInt32(temp[0]);
			}
			else
			{
				Debug.Print("Error: Found " + experience + " instead of experience");
				UserInterface.AddError("Found " + experience + " instead of experience");
			}

			return experience;
		}

		// How long to look for a readable, steady list before clicking instead.
		private const int ListReadMs = 1500;

		/// <summary>
		/// The constellation level from the six node rings on the just-opened constellation screen, or
		/// null (then the nodes are clicked). The same reading twice in a row is needed, so a list that is
		/// still fading in isn't read.
		/// </summary>
		private static int? ReadConstellationFromList(Character character)
		{
			var size = new Size(Navigation.GetWidth(), Navigation.GetHeight());
			if (!CharacterLists.Supported(size)) return null;
			var rect = CharacterLists.NodeListArea(size);
			var clock = Stopwatch.StartNew();
			int? previous = null;
			CharacterLists.NodeState[] nodes = null;
			while (clock.ElapsedMilliseconds < ListReadMs)
			{
				int? now;
				using (Bitmap list = Navigation.CaptureRegion(rect))
				{
					nodes = CharacterLists.ReadNodes(list);
					now = CharacterLists.Constellation(nodes);
				}
				if (now != null && now == previous) return now;
				previous = now;
				Navigation.Wait(ScreenSettle.PollMs);
			}
			Logger.Debug("{0}: constellation node list unclear ({1}); clicking the nodes", character.NameGOOD,
				nodes == null ? "not read" : string.Join(" ", nodes));
			SaveScreenshot(character, "constellation-list-unclear");
			return null;
		}

		private static void SaveScreenshot(Character character, string name)
		{
			using (var screen = Navigation.CaptureWindow())
				SaveImage(character, name, screen);
		}

		private static void SaveImage(Character character, string name, Bitmap image)
		{
			try
			{
				Directory.CreateDirectory($"./logging/characters/{character.NameGOOD}");
				image.Save($"./logging/characters/{character.NameGOOD}/{name}.png");
			}
			catch (Exception e) { Logger.Debug("Couldn't save the screenshot {0}: {1}", name, e.Message); }
		}

		private static int ScanConstellations(Character character)
		{
			int? listed = ReadConstellationFromList(character);
			if (listed != null)
			{
				if (Properties.Settings.Default.LogScreenshots) SaveScreenshot(character, "constellation_list");
				Logger.Debug("{0}: constellation {1} read from the node list", character.NameGOOD, listed.Value);
				UserInterface.SetCharacter_Constellation(listed.Value);
				return listed.Value;   // no node was clicked, so no Esc: the menu is still there for Talents
			}

			double yReference = 720.0;
			int constellation;

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			Rectangle constActivate =  new RECT(
				Left:   (int)( 70 / 1280.0 * Navigation.GetWidth() ),
				Top:    (int)( 665 / 720.0 * Navigation.GetHeight() ),
				Right:  (int)( 100 / 1280.0 * Navigation.GetWidth() ),
				Bottom: (int)( 695 / 720.0 * Navigation.GetHeight() ));

			for (constellation = 0; constellation < 6; constellation++)
			{
				// Select Constellation
				int yOffset = (int)( ( 180 + ( constellation * 75 ) ) / yReference * Navigation.GetHeight() );

				if (Navigation.GetAspectRatio() == new Size(8, 5))
				{
					yOffset = (int)( ( 225 + ( constellation * 75 ) ) / yReference * Navigation.GetHeight() );
				}

				Navigation.SetCursor((int)( 1130 / 1280.0 * Navigation.GetWidth() ), yOffset);
				var pause = constellation == 0 ? 700 : 550;
				ScreenSettle.Run("constellation node", ScreenSettle.Area.ConstellationDetail, Navigation.Click, pause, clickAgain: true);

				if (Properties.Settings.Default.LogScreenshots)
				{
					var screenshot = Navigation.CaptureWindow();
					Directory.CreateDirectory($"./logging/characters/{character.NameGOOD}");
					screenshot.Save($"./logging/characters/{character.NameGOOD}/constellation_{constellation + 1}.png");
				}

				// Grab Color
				using (Bitmap region = Navigation.CaptureRegion(constActivate))
				{
					// Check a small region next to the text "Activate"
					// for a mostly white backround
					ImageStatistics statistics = new ImageStatistics(region);
					if (statistics.Red.Mean >= 190 && statistics.Green.Mean >= 190 && statistics.Blue.Mean >= 190) 
						break;
					
				}
			}

			ScreenSettle.Run("close constellation", ScreenSettle.Area.ConstellationDetail,
				() => Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE), Navigation.BaseWait(Navigation.Speed.Normal));
			UserInterface.SetCharacter_Constellation(constellation);
			return constellation;
		}

		/// <summary>The talents raised by 3 on screen by C3 and C5 ("skill", "burst"); empty below C3 or if unknown.</summary>
		private static HashSet<string> BoostedTalents(Character character, string name)
		{
			var boosted = new HashSet<string>();
			try
			{
				if (character.Constellation < 3 || !GenshinProcesor.Characters.ContainsKey(name.ToLower())) return boosted;
				var order = character.NameGOOD.Contains("Traveler")
					? GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][character.Element.ToLower()]
					: GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"];
				boosted.Add((string)order[0]);
				if (character.Constellation >= 5) boosted.Add((string)order[1]);
			}
			catch (Exception e) { Logger.Debug("{0}: boosted talents unknown: {1}", character.NameGOOD, e.Message); }
			return boosted;
		}

		private static Dictionary<string, int> ScanTalents(Character character, HashSet<string> boosted)
		{
			var talents = new Dictionary<string, int>
			{
				{ "auto" , -1 },
				{ "skill", -1 },
				{ "burst", -1 }
			};

			int specialOffset = 0;

			// Check if character has a movement talent like
			// Mona or Ayaka
			if (character.NameGOOD.Contains("Mona") || character.NameGOOD.Contains("Ayaka")) specialOffset = 1;

			var xRef = 1280.0;
			var yRef = 720.0;

			if (Navigation.GetAspectRatio() == new Size(8, 5)) yRef = 800.0;

			Rectangle region =  new Rectangle(
				x:		(int)((Navigation.IsNormal ? 0.0003 : 0.0003) * Navigation.GetWidth() ),
				y:		(int)((Navigation.IsNormal ? 0.1278 : 0.1078) * Navigation.GetHeight() ),
				width:	(int)((Navigation.IsNormal ? 0.2913 : 0.2913) * Navigation.GetWidth() ),
				height:	(int)((Navigation.IsNormal ? 0.0711 : 0.0711) * Navigation.GetHeight() ));

			// First read the levels from the list on the right; only talents that can't be read are clicked.
			var listed = ReadTalentsFromList(character, specialOffset, boosted);
			bool clicked = false;

			for (int i = 0; i < 3; i++)
			{
				string talent;
				string key = i == 0 ? "auto" : i == 1 ? "skill" : "burst";
				if (listed[i] != null)
				{
					talents[key] = listed[i].Value;
					UserInterface.SetCharacter_Talent(listedCrops[i], listed[i].Value.ToString(), i);
					continue;
				}
				clicked = true;
				// Change y-offset for talent clicking
				int yOffset = (int)( 110 / yRef * Navigation.GetHeight() ) + ( i + ( ( i == 2 ) ? specialOffset : 0 ) ) * (int)(60 / yRef * Navigation.GetHeight() );

				Navigation.SetCursor((int)(1130 / xRef * Navigation.GetWidth()), yOffset);
				int pause = i == 0 ? 700 : 550;
				ScreenSettle.Run("talent", ScreenSettle.Area.TalentDetail, Navigation.Click, pause, clickAgain: true);
                switch (i)
                {
					default:
						talent = "auto";
						break;
					case 1:
						talent = "skill";
						break;
					case 2:
						talent = "burst";
						break;
                }

                while (talents[talent] < 1 || talents[talent] > 15)
				{
					Bitmap talentLevel = Navigation.CaptureRegion(region);

					talentLevel = GenshinProcesor.ResizeImage(talentLevel, talentLevel.Width * 2, talentLevel.Height * 2);

					Bitmap n = GenshinProcesor.ConvertToGrayscale(talentLevel);
					GenshinProcesor.SetContrast(60, ref n);
					GenshinProcesor.SetInvert(ref n);

					var text = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim().Split('\n').ToList();

					if (int.TryParse(Regex.Replace(text.Last(), @"\D", string.Empty), out int level))
					{
						if (level >= 1 && level <= 15)
						{
							talents[talent] = level;
							UserInterface.SetCharacter_Talent(talentLevel, level.ToString(), i);
						}
					}

					n.Dispose();
					talentLevel.Dispose();
				}
			}

			// Only a clicked talent opens its detail; read from the list, the Talents list is still showing.
			if (clicked)
				ScreenSettle.Run("close talents", ScreenSettle.Area.TalentDetail,
					() => Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE), Navigation.BaseWait(Navigation.Speed.Normal));
			for (int i = 0; i < 3; i++) { listedCrops[i]?.Dispose(); listedCrops[i] = null; }
			return talents;
		}

		// The entry crops shown in the window for talents read from the list.
		private static readonly Bitmap[] listedCrops = new Bitmap[3];

		/// <summary>
		/// Normal Attack, Skill and Burst levels read from the Talents list (null where unreadable).
		/// The list must hold still for two captures before it is read, so a list still fading in isn't.
		/// </summary>
		private static int?[] ReadTalentsFromList(Character character, int specialOffset, HashSet<string> boosted)
		{
			var levels = new int?[3];
			for (int i = 0; i < 3; i++) { listedCrops[i]?.Dispose(); listedCrops[i] = null; }
			var size = new Size(Navigation.GetWidth(), Navigation.GetHeight());
			if (!CharacterLists.Supported(size)) return levels;
			var rect = CharacterLists.TalentListArea(size);
			var whole = new[] { new Rectangle(Point.Empty, rect.Size) };
			var clock = Stopwatch.StartNew();
			while (clock.ElapsedMilliseconds < ListReadMs && levels.Any(l => l == null))
			{
				using (Bitmap a = Navigation.CaptureRegion(rect))
				{
					Navigation.Wait(ScreenSettle.PollMs);
					using (Bitmap b = Navigation.CaptureRegion(rect))
					{
						if (!CardSettle.Matches(a, b, whole)) continue;   // still changing
						SaveImage(character, "talent_list", b);   // always, so a wrong read can be checked afterwards
						for (int i = 0; i < 3; i++)
						{
							if (levels[i] != null) continue;
							int entry = i == 2 ? 2 + specialOffset : i;   // Mona and Ayaka: the sprint comes before the burst
							levels[i] = CharacterLists.ReadTalentLevel(b, entry);
							string key = i == 0 ? "auto" : i == 1 ? "skill" : "burst";
							if (levels[i] < 4 && boosted.Contains(key))
							{
								// A C3/C5 boosted talent shows at least 4: a misread (Rosaria's 11 as 1), so click it
								Logger.Debug("{0}: boosted {1} read as {2}; not trusted", character.NameGOOD, key, levels[i]);
								levels[i] = null;
								continue;
							}
							if (levels[i] != null)
								listedCrops[i] = b.Clone(CharacterLists.TalentEntry(entry, b.Size), b.PixelFormat);
						}
					}
				}
			}
			if (levels.Any(l => l == null))
			{
				Logger.Debug("{0}: talent list unclear ({1}); clicking those talents", character.NameGOOD,
					string.Join(", ", levels.Select(l => l?.ToString() ?? "?")));
				SaveScreenshot(character, "talent-list-unclear");
			}
			else Logger.Debug("{0}: talents read from the list", character.NameGOOD);
			return levels;
		}
	}
}