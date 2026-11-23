using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7.1 (your pick D in .mockups/genshin-version.html): the game data version next to the Scan button.
	[TestClass]
	public sealed class GameDataLineTests
	{
		private static readonly Version V72 = new Version(7, 2, 0);
		private static UpdateResult Remote(Version v, string error = null) => new UpdateResult { DataRemote = v, DataError = error };

		[TestMethod]
		public void WordsAndLink()
		{
			var plain = GameDataText.For(false, V72, null);
			Assert.AreEqual("Game data: Genshin 7.2", plain.Text);
			Assert.AreEqual(0, plain.LinkLength);
			Assert.IsFalse(plain.Missing);

			var newer = GameDataText.For(false, V72, Remote(new Version(7, 3)));
			Assert.AreEqual("Game data: Genshin 7.2 · 7.3 available", newer.Text);
			Assert.AreEqual("7.3 available", newer.Text.Substring(newer.LinkStart, newer.LinkLength));

			var twoLines = GameDataText.For(false, V72, Remote(new Version(7, 3)), twoLines: true);
			Assert.AreEqual("Game data: Genshin 7.2\n7.3 available", twoLines.Text);
			Assert.AreEqual("7.3 available", twoLines.Text.Substring(twoLines.LinkStart, twoLines.LinkLength));

			Assert.AreEqual(plain.Text, GameDataText.For(false, V72, Remote(new Version(7, 2))).Text, "same version");
			Assert.AreEqual(plain.Text, GameDataText.For(false, V72, Remote(new Version(7, 1))).Text, "older online");
			Assert.AreEqual(plain.Text, GameDataText.For(false, V72, Remote(new Version(7, 3), "429")).Text, "the check failed");

			var missing = GameDataText.For(true, null, Remote(new Version(7, 3)));
			Assert.AreEqual("Game data missing · Get it", missing.Text);
			Assert.AreEqual("Get it", missing.Text.Substring(missing.LinkStart, missing.LinkLength));
			Assert.IsTrue(missing.Missing);
			Assert.AreEqual("Game data missing\nGet it", GameDataText.For(true, null, null, twoLines: true).Text);
		}

		// In each look, at every zoom, the longest line ("… · 7.3 available") sits by the Scan button inside the
		// window, clear of the status text; a long status hides it and a short one brings it back.
		[TestMethod]
		public void PlacedNextToScanInEveryLookAtEveryZoom()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var simple = new SimpleForm())
				using (var advanced = new AdvancedForm())
				using (var classic = new MainForm())
				{
					T Field<T>(string name) => (T)typeof(MainForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(classic);
					var classicStatus = Field<Label>("ProgramStatus_Label");
					var classicPanel = Field<Panel>("ScannerOutput_Panel");
					var classicScan = Field<Button>("StartScan_Button");
					string longest = GameDataText.For(false, V72, Remote(new Version(7, 3))).Text;

					foreach (var (name, form, line, status) in new (string, Form, GameDataLine, Control)[] {
						("Simple", simple, simple.GameData, simple.Status), ("Advanced", advanced, advanced.GameData, advanced.Status), ("Classic", classic, classic.GameData, classicStatus) })
					{
						SimpleLookTests.Bind(form);
						bool classicLook = form == classic;
						line.Text = classicLook ? longest.Replace(" · ", "\n") : longest;
						string idle = classicLook ? "" : "Ready";
						foreach (int step in Zoom.Steps.Concat(new[] { 150, 100 }))
						{
							Zoom.Set(form, step);
							string where = $"{name} at {step} %";
							status.Text = idle;
							Assert.IsTrue(line.Showing, where + ": shown while idle");
							var span = line.TextSpan;
							var text = line.GetPreferredSize(Size.Empty);   // as the LinkLabel itself lays it out
							Assert.IsTrue(text.Width <= line.Width, $"{where}: text {text.Width} px in a {line.Width} px line (would wrap)");
							Assert.IsTrue(text.Height <= line.Height + 1, $"{where}: {text.Height} px of text in a {line.Height} px line");
							Assert.IsTrue(span.Left >= 0 && span.Right <= form.ClientSize.Width, where + ": inside the window");
							if (classicLook)
							{
								Assert.IsTrue(span.Right <= classicPanel.Left, $"{where}: ends at {span.Right}, dark panel at {classicPanel.Left}");
								Assert.IsTrue(line.Bottom <= classicScan.Top, $"{where}: bottom {line.Bottom}, Scan button at {classicScan.Top}");
								Assert.IsTrue(line.Top >= Field<Control>("ScanSpeed_TrackBar").Bottom, where + ": below the speed slider");
							}
							else
							{
								Assert.AreEqual(status.Right, line.Right, where + ": at the end of the status row");
								Assert.AreEqual(status.Top, line.Top, where + ": on the status row");
								int statusEnd = status.Left + TextRenderer.MeasureText(status.Text, status.Font).Width;
								Assert.IsTrue(statusEnd < span.Left, $"{where}: \"Ready\" ends at {statusEnd}, the line starts at {span.Left}");
							}

							status.Text = "Updates available · Open Updates";
							Assert.IsFalse(line.Showing, where + ": steps aside for a long status");
							status.Text = idle;
							Assert.IsTrue(line.Showing, where + ": back after it");
						}
					}
				}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}
	}
}
