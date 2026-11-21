using Accord.Imaging;
using Accord.Imaging.Filters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    [Serializable]
	public struct Material : ISerializable
	{
		public string name;
		public int count;

		public Material(string _name, int _count)
		{
			name = _name;
			count = _count;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) => info.AddValue(name, count);

		public override int GetHashCode()
		{
			return name.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			return obj is Material material && name == material.name;
		}
	}

	internal class MaterialScraper : InventoryScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();


		public MaterialScraper()
		{
			inventoryPage = InventoryPage.CharacterDevelopmentItems;
		}

		public MaterialScraper(InventoryPage section) : base()
		{
			inventoryPage = section;
		}

		internal void SetInventoryPage(InventoryPage page)
		{
			if (materialPages.Contains(page)) inventoryPage = page;
		}

		public void Scan_Materials(ref Inventory inventory)
		{
			if (!inventory.Materials.Contains(new Material("Mora", 0)))
			{
				inventory.Materials.Add(new Material("Mora", ScanMora()));
			}

			int scrollCount = 0;

			Material material = new Material(null, 0);
			Material previousMaterial = new Material(null, -1);

			List<Rectangle> rectangles;
			int page = 1;

			// Keep scanning while not repeating any items names
			while (true)
			{
				int rows, cols;
				// Find all items on the screen
				(rectangles, cols, rows) = GetPageOfItems(page, acceptLess: true);

				// Remove last row. Sometimes the bottom of a page of items is caught which results
				// in a faded quantity that can't be parsed. Removing slightly increases the number of pages that
				// need to be scrolled but it's fine.
				var r = rectangles.Take(rectangles.Count() - cols).ToList();

				Logger.Debug("Scanning material page {0}", page);
				Logger.Debug("Located {0} possible item locations on page.", rectangles.Count);

				foreach (var rectangle in r)
				{
					// Select Material
					Navigation.SetCursor(rectangle.Center().X, rectangle.Center().Y);
					Navigation.Click();
					Navigation.SystemWait(Navigation.Speed.SelectNextInventoryItem);

					material.name = ScanMaterialName(out Bitmap nameplate);
					material.count = 0;

					// Check if new material has been found
					if (inventory.Materials.Contains(material))
					{
						Logger.Debug("Repeat material found. Scrolling until end");
						goto LastPage;
					}
					else
					{
						if (!string.IsNullOrEmpty(material.name))
						{
							// Scan Material Quantity
							material.count = ScanMaterialCount(rectangle, out Bitmap quantity);
							if (material.count == 0)
							{
								UserInterface.AddError($"Failed to parse quantity for {material.name}");
								SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
							}
							else if (Properties.Settings.Default.LogScreenshots)
							{
								SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
							}
							inventory.Materials.Add(material);
							UserInterface.ResetCharacterDisplay();
							UserInterface.SetMaterial(nameplate, quantity, material.name, material.count);
							quantity.Dispose();   // the UI shows its own copy

							previousMaterial.name = material.name;
						}
					}
					nameplate.Dispose();
					Navigation.Wait(150);
				}

				Navigation.SetCursor(r.Last().Center().X, r.Last().Center().Y);
				Navigation.Click();
				Navigation.Wait(150);

				Logger.Debug("Finished page of materials. Scrolling...");

				// Scroll to next page
				for (int i = 0; i < rows - 1; i++)
				{
					scrollCount++;

					// scroll down
					for (int k = 0; k < 10; k++)
					{
						Navigation.sim.Mouse.VerticalScroll(-1);
						// skip a scroll
						if (( k == 7 ) && ( ( scrollCount % 3 ) == 0 ))
						{
							k++;
							if (scrollCount % 9 == 0)
							{
								if (scrollCount == 18)
								{
									scrollCount = 0;
								}
								else
								{
									Navigation.sim.Mouse.VerticalScroll(-1);
								}
							}
						}
						Navigation.SystemWait(Navigation.Speed.InventoryScroll);
					}
				}
				Navigation.SystemWait(Navigation.Speed.Normal);
				++page;
			}

		LastPage:
			// scroll down as much as possible
			for (int i = 0; i < 20; i++)
			{
				Navigation.sim.Mouse.VerticalScroll(-1);
				Navigation.SystemWait(Navigation.Speed.InventoryScroll);
			}

			Navigation.Wait(500);

			(rectangles, _, _) = GetPageOfItems(page, acceptLess: true);
			bool passby = true;
			for (int i = rectangles.Count - 1; i >= 0; i--) // Click through but backwards to short-circuit after new materials
			{
				// Select Material
				Rectangle rectangle = rectangles[i];
				Navigation.SetCursor(rectangle.Center().X, rectangle.Center().Y);
				Navigation.Click();
				Navigation.SystemWait(Navigation.Speed.SelectNextInventoryItem);

				material.name = ScanMaterialName(out Bitmap nameplate);
				material.count = 0;

				if (inventory.Materials.Contains(material) && passby) continue;

				if (!inventory.Materials.Contains(material))
				{
					if (!string.IsNullOrEmpty(material.name))
					{
						// Scan Material Number
						material.count = ScanMaterialCount(rectangle, out Bitmap quantity);
						if (material.count == 0)
						{
							UserInterface.AddError($"Failed to parse quantity for {material.name}");
							SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
						}
						else if (Properties.Settings.Default.LogScreenshots)
						{
							SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
						}
						inventory.Materials.Add(material);
						UserInterface.ResetCharacterDisplay();
						UserInterface.SetMaterial(nameplate, quantity, material.name, material.count);
						passby = false; // New material found so break on next old material
						quantity.Dispose();
					}
				}
				else
				{
					Logger.Debug("Last material scanned, {0}.", inventory.Materials.Last().name);
					nameplate.Dispose();
					break;
				}
				Navigation.Wait(150);
			}
		}

		private int ScanMora()
		{
			var region = new Rectangle(
				x: (int)(125 / 1280.0 * Navigation.GetWidth()),
				y: (int)(665 / 720.0 * Navigation.GetHeight()),
				width: (int)(300 / 1280.0 * Navigation.GetWidth()),
				height: (int)(30 / 720.0 * Navigation.GetHeight()));

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				region.Y = (int)( 740 / 800.0 * Navigation.GetHeight() );
			}

			using (var screenshot = Navigation.CaptureRegion(region))
			{
				string mora = ParseMoraFromScreenshot(screenshot);

				if (int.TryParse(mora, out int count))
				{
					UserInterface.ResetCharacterDisplay();
					UserInterface.SetMora(screenshot, count);
                    if (Properties.Settings.Default.LogScreenshots) 
						SaveInventoryBitmap(screenshot, "mora.png");
				}
				else
				{
					UserInterface.SetNavigation_Image(screenshot);
					UserInterface.AddError("Unable to parse mora count");
					SaveInventoryBitmap(screenshot, "mora.png");
				}
				return count;
			}
		}

		public static string ParseMoraFromScreenshot(Bitmap screenshot)
		{
			using (var gray = GenshinProcesor.ConvertToGrayscale(screenshot))
			{
				var invert = (Bitmap)gray.Clone();
				GenshinProcesor.SetInvert(ref invert);
				var input = GenshinProcesor.AnalyzeText(invert).Split(' ').ToList();
				Logger.Debug("Scanned mora input: {0}", input.ToString());
				input.RemoveAll(e => Regex.IsMatch(e.Trim(), @"[^0-9]") || string.IsNullOrWhiteSpace(e.Trim()));
				var mora = input.LastOrDefault();
				Logger.Debug("Parsed mora input: {0}", mora);
				return mora;
			}
		}

		public string ScanMaterialName(out Bitmap nameplate)
		{
			// Grab item name on right
			var refWidth = 1280.0;
			var refHeight = Navigation.GetAspectRatio() == new Size(16,9) ? 720.0 : 800.0;

			var width = Navigation.GetWidth();
			var height = Navigation.GetHeight();

			var reference = new Rectangle(872, 80, 327, 37);

			// Nameplate is in the same place in 16:9 and 16:10
			var region= new RECT(
				Left:   (int)( reference.Left   / refWidth  * width),
				Top:    (int)( reference.Top    / refHeight * height),
				Right:  (int)( reference.Right  / refWidth  * width),
				Bottom: (int)( reference.Bottom / refHeight * height));

			Bitmap bm = Navigation.CaptureRegion(region);
			nameplate = (Bitmap)bm.Clone();

			// Alter Image
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = GenshinProcesor.ConvertToGrayscale(bm);
			GenshinProcesor.SetInvert(ref n);

			string text = GenshinProcesor.AnalyzeText(n,Tesseract.PageSegMode.Auto);
			text = Regex.Replace(text, @"[\W\s]", string.Empty).ToLower();

			//UI
			n.Dispose();
			bm.Dispose();

			if (inventoryPage == InventoryPage.CharacterDevelopmentItems)
				return GenshinProcesor.FindClosestDevelopmentName(text);

			if (inventoryPage == InventoryPage.Materials)
				return GenshinProcesor.FindClosestMaterialName(text);

			return null;
		}

		// Quantity reading adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT): find the pale
		// quantity strip, keep only dark-blue digit ink (drops gold rarity stars), reject digits clipped
		// at the bottom edge, OCR at several sizes and require a clear majority. 0 means "unknown".
		public static int ScanMaterialCount(Rectangle rectangle, out Bitmap quantity)
		{
            var region = GetMaterialQuantityRegion(rectangle);
            using (Bitmap bm = Navigation.CaptureRegion(region))
            {
                quantity = (Bitmap)bm.Clone();
                return ParseMaterialCount(bm);
            }
        }

        public static RECT GetMaterialQuantityRegion(Rectangle rectangle)
        {
            double referenceHeight = Navigation.GetAspectRatio() == new Size(8, 5) ? 800.0 : 720.0;
            return new RECT(
                Left: rectangle.X,
                Top: (int)(rectangle.Y + (0.8 * rectangle.Height)),
                Right: rectangle.Right,
                Bottom: rectangle.Bottom + (int)Math.Round(20 * Navigation.GetHeight() / referenceHeight));
        }

        public static int ParseMaterialCount(Bitmap bm, Func<Bitmap, string> analyzeText = null)
        {
            if (analyzeText == null)
                analyzeText = image =>
                {
                    string text = GenshinProcesor.AnalyzeText(image, Tesseract.PageSegMode.SingleLine, true);
                    return string.IsNullOrWhiteSpace(text)
                        ? GenshinProcesor.AnalyzeText(image, Tesseract.PageSegMode.SingleWord, true)
                        : text;
                };
            var counts = new Dictionary<int, int>();

            // Locate the pale quantity strip, independently of capture height and display scale.
            int top = 0;
            int bottom = bm.Height;
            for (int y = 0; y < bm.Height; y++)
            {
                int light = 0;
                for (int x = 0; x < bm.Width; x++)
                {
                    Color color = bm.GetPixel(x, y);
                    if (color.R > 180 && color.G > 180 && color.B > 160) light++;
                }
                if (light >= bm.Width * 0.85)
                {
                    top = y;
                    // The blank side margins locate the lower edge even on dense digit rows.
                    int margin = Math.Max(1, bm.Width / 8);
                    for (int row = top + 1; row < bm.Height; row++)
                    {
                        int paleMargins = 0;
                        for (int x = 0; x < margin; x++)
                        {
                            Color left = bm.GetPixel(x, row);
                            Color right = bm.GetPixel(bm.Width - 1 - x, row);
                            if (left.R > 180 && left.G > 180 && left.B > 160) paleMargins++;
                            if (right.R > 180 && right.G > 180 && right.B > 160) paleMargins++;
                        }
                        // Require a strict pale majority: a half-pale transition row can contain dark frame fragments.
                        if (paleMargins <= margin)
                        {
                            bottom = row;
                            break;
                        }
                    }
                    break;
                }
            }

            using (var digits = new Bitmap(bm.Width, bottom - top))
            {
                // Keep dark blue number ink (and neutral black), excluding gold rarity stars.
                for (int y = top; y < bottom; y++)
                    for (int x = 0; x < bm.Width; x++)
                    {
                        Color color = bm.GetPixel(x, y);
                        bool ink = color.R < 160 && color.B >= color.R && color.G >= color.R;
                        digits.SetPixel(x, y - top, ink ? color : Color.White);
                    }

                // A central ink stroke at the bottom may be a clipped glyph, even if OCR agrees.
                // Runs attached to an image edge belong to the selected tile's frame.
                int runStart = -1;
                for (int x = 0; x < digits.Width; x++)
                {
                    if (digits.GetPixel(x, digits.Height - 1).R < 160)
                    {
                        if (runStart < 0) runStart = x;
                    }
                    else
                    {
                        if (runStart > 0) return 0;
                        runStart = -1;
                    }
                }

                for (double scale = 2; scale <= 4; scale += 0.5)
                {
                    using (var resized = GenshinProcesor.ResizeImage(digits,
                        (int)(digits.Width * scale), (int)(digits.Height * scale)))
                    using (var padded = new Bitmap(resized.Width + 30, resized.Height + 30))
                    {
                        using (var graphics = Graphics.FromImage(padded))
                        {
                            graphics.Clear(Color.White);
                            graphics.DrawImageUnscaled(resized, 15, 15);
                        }
                        string original = analyzeText(padded).Trim();
                        string cleaned = Regex.Replace(original, @"[^0-9]", string.Empty);
                        int.TryParse(cleaned, out int value);
                        Logger.Debug($"Scanned: {original} -> Regex: {cleaned} -> Parsed: {value}");
                        if (value > 0)
                            counts[value] = counts.TryGetValue(value, out int votes) ? votes + 1 : 1;
                    }
                }
            }

            var winners = counts.OrderByDescending(entry => entry.Value).ToList();
            if (winners.Count == 0 || winners[0].Value < 2
                || (winners.Count > 1 && winners[0].Value == winners[1].Value)) return 0;
            return winners[0].Key;
        }
    }
}
