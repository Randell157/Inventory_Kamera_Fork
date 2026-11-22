using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7: Simple says what its hidden options will scan, and "Use standard settings" puts them back.
	// Offline; the window is created but never shown.
	[TestClass]
	public sealed class SimpleSummaryTests
	{
		[TestMethod]
		public void SummaryText()
		{
			Assert.AreEqual("3★ and up, any level", SimpleText.WeaponsSummary(3, 1));
			Assert.AreEqual("4★ and up, level 40 and up", SimpleText.WeaponsSummary(4, 40));
			Assert.AreEqual("4★ and up, any level, every page", SimpleText.ArtifactsSummary(4, 0, 0));
			Assert.AreEqual("5★ and up, +16 and up, newest 2 pages", SimpleText.ArtifactsSummary(5, 16, 2));
			Assert.AreEqual("5★ and up, any level, newest page", SimpleText.ArtifactsSummary(5, 0, 1));
			Assert.AreEqual("every character", SimpleText.CharactersSummary(0));
			Assert.AreEqual("first character", SimpleText.CharactersSummary(1));
			Assert.AreEqual("first 5 characters", SimpleText.CharactersSummary(5));
		}

		private static void Isolated(Action<Properties.Settings> test)
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				test(Properties.Settings.Default);
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		private static void Leave(Properties.Settings s)
		{
			// What the Advanced test scan left behind, plus things "standard" must not touch.
			s.MinimumWeaponRarity = 4; s.MinimumArtifactRarity = 5; s.SortByObtained = 2; s.NumOfCharToScan = 5; s.ScanCharDevItems = false;
			s.TravelerName = "Aether"; s.OutputPath = @"D:\Exports"; s.ScanSpeed = 1; s.WandererName = "Kunikuzushi";
		}

		[TestMethod]
		public void StandardIsTheAppsOwnDefaultsAndLeavesTheRestAlone()
		{
			Isolated(s =>
			{
				Assert.IsTrue(StandardSettings.IsStandard(s), "a new install is standard");
				Assert.AreEqual(3m, StandardSettings.Default(s, "MinimumWeaponRarity"));
				Assert.AreEqual(4m, StandardSettings.Default(s, "MinimumArtifactRarity"));
				Assert.AreEqual(0, StandardSettings.Default(s, "NumOfCharToScan"));
				Assert.AreEqual(true, StandardSettings.Default(s, "ScanCharDevItems"));

				Leave(s);
				Assert.IsFalse(StandardSettings.IsStandard(s));
				StandardSettings.Apply(s);
				Assert.IsTrue(StandardSettings.IsStandard(s));
				Assert.AreEqual((3m, 4m, 0, 0, true), (s.MinimumWeaponRarity, s.MinimumArtifactRarity, s.SortByObtained, s.NumOfCharToScan, s.ScanCharDevItems));
				Assert.AreEqual(("Aether", @"D:\Exports", 1, "Kunikuzushi"), (s.TravelerName, s.OutputPath, s.ScanSpeed, s.WandererName), "names, folder and speed untouched");

				s.TravelerName = "Lumine";
				Assert.IsTrue(StandardSettings.IsStandard(s), "names don't count");
			});
		}

		// Whether a control is set to show (Visible reads false for every control of a window that isn't on screen).
		private static bool Shown(Control control) =>
			(bool)typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { 2 });

		[TestMethod]
		public void SimpleShowsWhatItWillScanAndCanReset()
		{
			Isolated(s =>
			{
				using (var form = new SimpleForm())
				{
					SimpleLookTests.Bind(form);
					Assert.AreEqual("3★ and up, any level · Change", form.WeaponsLine.Text);
					Assert.AreEqual("4★ and up, any level, every page · Change", form.ArtifactsLine.Text);
					Assert.AreEqual("every character · Change", form.CharactersLine.Text);
					Assert.IsFalse(Shown(form.UseStandard), "standard: no reset link");

					Leave(s);   // as if changed in Advanced
					Assert.AreEqual("4★ and up, any level · Change", form.WeaponsLine.Text);
					Assert.AreEqual("5★ and up, any level, newest 2 pages · Change", form.ArtifactsLine.Text);
					Assert.AreEqual("first 5 characters · Change", form.CharactersLine.Text);
					Assert.IsTrue(Shown(form.UseStandard));
					Assert.AreEqual("Change", form.WeaponsLine.Text.Substring(form.WeaponsLine.LinkArea.Start, form.WeaponsLine.LinkArea.Length), "only Change is the link");

					s.ScanArtifacts = false;
					Assert.IsFalse(Shown(form.ArtifactsLine), "an unticked item has no line");
					form.Artifacts.Checked = true;
					Assert.IsTrue(Shown(form.ArtifactsLine));

					StandardSettings.Apply(s);   // what the link does
					Assert.AreEqual("3★ and up, any level · Change", form.WeaponsLine.Text);
					Assert.AreEqual("every character · Change", form.CharactersLine.Text);
					Assert.IsFalse(Shown(form.UseStandard));
					Assert.IsTrue(form.DevItems.Checked);
					Assert.AreEqual("Aether", form.TravelerName.Text);
				}
			});
		}
	}
}
