using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7 step 2: the look setting, the shared startup, and a status helper that works with whatever
	// controls a look has. Offline; nothing here opens a window or touches the game.
	[TestClass]
	public sealed class LookTests
	{
		[TestMethod]
		public void NewInstallsStartOnSimpleUpgradersOnClassic()
		{
			Assert.AreEqual(Look.Simple, Looks.Stored(Looks.Unset, hadSettings: false), "new install");
			Assert.AreEqual(Look.Classic, Looks.Stored(Looks.Unset, hadSettings: true), "upgrading from 1.6");
			foreach (bool had in new[] { true, false })
			{
				Assert.AreEqual(Look.Advanced, Looks.Stored((int)Look.Advanced, had), "a saved look always wins");
				Assert.AreEqual(Look.Classic, Looks.Stored((int)Look.Classic, had));
				Assert.AreEqual(Look.Simple, Looks.Stored((int)Look.Simple, had));
			}
			Assert.AreEqual(Look.Classic, Looks.Stored(7, hadSettings: true), "a broken value counts as unset");
			Assert.AreEqual(Look.Simple, Looks.Stored(7, hadSettings: false));
		}

		[TestMethod]
		public void LooksNotBuiltYetOpenAsClassic()
		{
			Assert.AreEqual(Look.Classic, Looks.Shown(Look.Classic));
			Assert.AreEqual(Look.Classic, Looks.Shown(Look.Simple), "Simple arrives in step 3");
			Assert.AreEqual(Look.Classic, Looks.Shown(Look.Advanced), "Advanced arrives in step 4");
			Assert.AreEqual("Simple is coming in Inventory Kamera 1.7.", Looks.NotBuiltTip(Look.Simple));
		}

		[TestMethod]
		public void PrepareSettingsGivesANewInstallSimpleAndKeepsASavedLook()
		{
			var isolation = new MaterialQuantityTests();   // in-memory settings: no settings file, like a new install
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				Assert.AreEqual((int)Look.Simple, settings.Look);
				Assert.AreEqual(ScanSpeeds.Auto, settings.ScanSpeed, "and Auto, as in 1.6.1");
				Assert.IsNotNull(AppStartup.Database);

				settings.Look = (int)Look.Classic;
				AppStartup.PrepareSettings();
				Assert.AreEqual((int)Look.Classic, settings.Look, "a saved look is kept");
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		private static T WithHandle<T>(T control) where T : Control
		{
			var _ = control.Handle;   // Invoke needs a handle; this test thread owns it
			return control;
		}

		[TestCleanup]
		public void ForgetControls() =>
			UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);

		[TestMethod]
		public void StatusAndCountsWorkWithOnlySimplesControls()
		{
			// Simple has a status line but no item images, text output, counters or error box.
			var status = WithHandle(new Label());
			UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, status, null, null);
			int counts = 0, statuses = 0, errors = 0;
			void Counts() => counts++;
			void Statuses() => statuses++;
			void Errors() => errors++;
			UserInterface.CountsChanged += Counts;
			UserInterface.StatusChanged += Statuses;
			UserInterface.ErrorsChanged += Errors;
			try
			{
				UserInterface.ResetAll();
				using (var bm = new Bitmap(4, 4))
				{
					UserInterface.SetGearPictureBox(bm);
					UserInterface.SetGearTextBox("Gladiator's Finale");
					UserInterface.SetCharacter_NameAndElement(bm, "Ganyu", "Cryo");
					UserInterface.SetCharacter_Level(bm, 90, 90);
					UserInterface.SetCharacter_Talent(bm, "10", 0);
					UserInterface.SetCharacter_Constellation(1);
					UserInterface.SetMaterial(bm, bm, "Mora", 1);
					UserInterface.SetMora(bm, 1);
					UserInterface.SetNavigation_Image(bm);
				}
				UserInterface.SetWeapon_Max(306);
				UserInterface.SetArtifact_Max(2187);
				for (int i = 0; i < 3; i++) UserInterface.IncrementWeaponCount();
				UserInterface.IncrementArtifactCount();
				UserInterface.IncrementCharacterCount();
				UserInterface.AddError("test error");
				UserInterface.SetProgramStatus("Scanning");

				Assert.AreEqual((3, 306, 1, 2187, 1), (UserInterface.Weapons, UserInterface.WeaponsMax, UserInterface.Artifacts, UserInterface.ArtifactsMax, UserInterface.Characters));
				Assert.AreEqual(1, UserInterface.ErrorCount);
				Assert.AreEqual("Scanning", UserInterface.Status);
				Assert.AreEqual("Scanning", status.Text, "the status line is still shown");
				Assert.IsTrue(counts >= 6 && statuses >= 1 && errors >= 2, $"events: counts {counts}, status {statuses}, errors {errors}");

				UserInterface.ResetAll();
				Assert.AreEqual((0, -1, 0, -1, 0), (UserInterface.Weapons, UserInterface.WeaponsMax, UserInterface.Artifacts, UserInterface.ArtifactsMax, UserInterface.Characters));
				Assert.AreEqual(0, UserInterface.ErrorCount);
			}
			finally
			{
				UserInterface.CountsChanged -= Counts;
				UserInterface.StatusChanged -= Statuses;
				UserInterface.ErrorsChanged -= Errors;
			}
		}

		[TestMethod]
		public void ClassicCounterLabelsShowWhatTheyDidBefore()
		{
			Label L() => WithHandle(new Label());
			Label weapons = L(), weaponsMax = L(), artifacts = L(), artifactsMax = L(), characters = L();
			var errors = WithHandle(new TextBox { Multiline = true });
			UserInterface.Init(null, null, null, null, null, null, weapons, weaponsMax, artifacts, artifactsMax, characters, null, errors, null);

			UserInterface.ResetAll();
			Assert.AreEqual(("0", "?", "0", "?", "0"), (weapons.Text, weaponsMax.Text, artifacts.Text, artifactsMax.Text, characters.Text));

			UserInterface.SetWeapon_Max(306);
			UserInterface.SetArtifact_Max(2187);
			for (int i = 0; i < 12; i++) UserInterface.IncrementWeaponCount();
			UserInterface.IncrementArtifactCount();
			UserInterface.IncrementCharacterCount();
			UserInterface.IncrementCharacterCount();
			Assert.AreEqual(("12", "306", "1", "2187", "2"), (weapons.Text, weaponsMax.Text, artifacts.Text, artifactsMax.Text, characters.Text));

			UserInterface.AddError("first\nsecond");
			StringAssert.Contains(errors.Text, "first");
			StringAssert.Contains(errors.Text, "second");
			UserInterface.ResetAll();
			Assert.AreEqual("", errors.Text);
		}
	}
}
