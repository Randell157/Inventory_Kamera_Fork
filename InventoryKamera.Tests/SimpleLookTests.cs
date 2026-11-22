using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7 step 3: the Simple window, View → Zoom and the one-time hint for upgraders. Offline; the
	// windows are created but never shown, and nothing touches the game.
	[TestClass]
	public sealed class SimpleLookTests
	{
		private static readonly Rectangle FullHd = new Rectangle(0, 0, 1920, 1032);   // 1080p minus the taskbar

		[TestMethod]
		public void ZoomStepsAndScreenFit()
		{
			Assert.AreEqual(150, Zoom.Valid(150));
			Assert.AreEqual(100, Zoom.Valid(130), "not a step");
			Assert.AreEqual(100, Zoom.Valid(0), "unset or broken");
			Assert.AreEqual("175%", Zoom.MenuText(175));

			var simple = new Size(360, 550);
			Assert.IsTrue(Zoom.Fits(simple, 175, FullHd), "962 px tall");
			Assert.IsFalse(Zoom.Fits(simple, 200, FullHd), "1100 px tall");
			Assert.IsTrue(Zoom.Fits(new Size(5000, 5000), 100, FullHd), "100 % is always allowed");
			Assert.AreEqual(175, Zoom.Usable(300, simple, FullHd), "the biggest step that fits");
			Assert.AreEqual(150, Zoom.Usable(150, simple, FullHd), "a saved step that fits is used");
			Assert.AreEqual(300, Zoom.Usable(300, simple, new Rectangle(0, 0, 3840, 2100)), "4K");
		}

		[TestMethod]
		public void ZoomScalesTextAndLayout()
		{
			using (var form = new Form { ClientSize = new Size(300, 200), Font = new Font("Segoe UI", 9F) })
			{
				var menu = new MenuStrip();
				var plain = new Label { Bounds = new Rectangle(10, 30, 100, 20) };
				var bold = new Label { Bounds = new Rectangle(10, 60, 100, 20), Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
				form.Controls.AddRange(new Control[] { plain, bold, menu });
				float menuSize = menu.Font.Size;

				Zoom.Apply(form, 200);

				Assert.AreEqual(new Size(600, 400), form.ClientSize);
				Assert.AreEqual(new Rectangle(20, 60, 200, 40), plain.Bounds);
				Assert.AreEqual(18F, plain.Font.Size, 0.01, "inherits the form's scaled font");
				Assert.AreEqual(22F, bold.Font.Size, 0.01, "its own font is scaled too");
				Assert.AreEqual(FontStyle.Bold, bold.Font.Style);
				Assert.AreEqual(menuSize * 2, menu.Font.Size, 0.01, "menus don't inherit the form's font");
			}
			using (var form = new Form { ClientSize = new Size(300, 200) })
			{
				Zoom.Apply(form, 100);
				Assert.AreEqual(new Size(300, 200), form.ClientSize, "100 % changes nothing");
			}
		}

		[TestMethod]
		public void UpgradersSeeTheHintOnceInClassic()
		{
			Assert.IsTrue(Looks.ShowHint(hintShown: false, Look.Classic));
			Assert.IsFalse(Looks.ShowHint(hintShown: true, Look.Classic), "only once");
			Assert.IsFalse(Looks.ShowHint(hintShown: false, Look.Simple), "Simple is the new look itself");

			var isolation = new MaterialQuantityTests();   // in-memory settings: a new install
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				Assert.IsTrue(settings.LooksHintShown, "a new install is never told");
				Assert.AreEqual(100, settings.Zoom);
				StringAssert.EndsWith(settings.OutputPath, @"\GenshinData", "the export folder is filled in before any window");
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		[TestMethod]
		public void ProgressAndErrorText()
		{
			var culture = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
			try
			{
				Assert.AreEqual("Weapons 12 / 306   Artifacts 0 / ?   Characters 3", SimpleText.Counts(true, 12, 306, true, 0, -1, true, 3));
				Assert.AreEqual("Artifacts 1,500 / 2,187", SimpleText.Counts(false, 0, -1, true, 1500, 2187, false, 0), "only what's scanned");
				Assert.AreEqual("", SimpleText.Counts(false, 0, -1, false, 0, -1, false, 0), "materials only: no counts");

				Assert.AreEqual(-1, SimpleText.Percent(true, 12, 306, true, 0, -1), "artifact total not known yet: moving bar");
				Assert.AreEqual(50, SimpleText.Percent(true, 153, 306, false, 0, -1));
				Assert.AreEqual(25, SimpleText.Percent(true, 100, 100, true, 0, 300));
				Assert.AreEqual(-1, SimpleText.Percent(true, 306, 306, false, 0, -1), "weapons done, characters or materials may follow");
				Assert.AreEqual(-1, SimpleText.Percent(false, 0, -1, false, 0, -1), "nothing with a total");

				Assert.AreEqual("", SimpleText.ErrorLink(0));
				Assert.AreEqual("1 error · View log", SimpleText.ErrorLink(1));
				Assert.AreEqual("3 errors · View log", SimpleText.ErrorLink(3));
			}
			finally { Thread.CurrentThread.CurrentCulture = culture; }
		}

		// What showing the window does, without showing it: create every control so its bindings read the settings.
		// (Control.CreateControl(ignoreVisible: true) is internal; it's what Show runs.)
		private static void Bind(Form form) =>
			typeof(Control).GetMethod("CreateControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(bool) }, null)
				.Invoke(form, new object[] { true });

		private static Dictionary<string, object> AllSettings()
		{
			var settings = Properties.Settings.Default;
			return settings.Properties.Cast<SettingsProperty>().ToDictionary(p => p.Name, p => settings[p.Name]);
		}

		[TestMethod]
		public void SimpleSharesClassicsSettingsAndLeavesTheHiddenOnesAlone()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			int listenersBefore = UserInterface.Listeners;
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				// Options Simple doesn't show, set away from their defaults.
				settings.MinimumWeaponRarity = 4;
				settings.MinimumWeaponLevel = 20;
				settings.MinimumArtifactRarity = 5;
				settings.MinimumArtifactLevel = 16;
				settings.NumOfCharToScan = 3;
				settings.SortByObtained = 2;
				settings.EquipWeapons = true;
				settings.WandererName = "Scaramouche";
				settings.ScanWeapons = true;
				settings.ScanMaterials = false;
				settings.TravelerName = "Aether";
				var before = AllSettings();
				UserInterface.SetProgramStatus("");   // other tests leave "Scanning" behind

				using (var form = new SimpleForm())
				{
					Bind(form);
					Assert.IsTrue(form.Weapons.Checked);
					Assert.IsFalse(form.Materials.Checked);
					Assert.AreEqual("Aether", form.TravelerName.Text);
					Assert.AreEqual(settings.OutputPath, form.OutputPath.Text);
					Assert.AreEqual(listenersBefore + 3, UserInterface.Listeners, "counts, status and errors");

					form.Weapons.Checked = false;   // a change in Simple is the setting Classic shows
					Assert.IsFalse(settings.ScanWeapons);
					settings.ScanMaterials = true;  // and the other way round
					Assert.IsTrue(form.Materials.Checked);

					Assert.AreEqual("Ready", form.Status.Text);
					UserInterface.SetProgramStatus(Looks.HintText, UserInterface.HintColor);
					Assert.AreEqual("Ready", form.Status.Text, "Classic's hint isn't carried into Simple");
					UserInterface.SetProgramStatus("");
					UserInterface.AddError("test error");
					Assert.AreEqual("1 error · View log", form.Errors.Text);
					UserInterface.ResetErrors();
					Assert.AreEqual("", form.Errors.Text);
					form.Close();
				}
				Assert.AreEqual(listenersBefore, UserInterface.Listeners, "a closed window stops listening");

				var after = AllSettings();
				foreach (var name in before.Keys.Where(n => n != "ScanWeapons" && n != "ScanMaterials"))
					Assert.AreEqual(before[name], after[name], name + " is left as it was");
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		[TestMethod]
		public void SimpleZoomsWholeAndFitsAFullHdScreenAt175()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var form = new SimpleForm())
				{
					var normal = form.Size;
					Assert.IsTrue(normal.Width <= 370 && normal.Height <= 560, $"about 360 × 550 at 100 %: {normal}");
					Assert.AreEqual(175, Zoom.Usable(300, normal, FullHd));

					int scanWidth = form.Controls.OfType<Button>().Max(b => b.Width);
					Zoom.Apply(form, 200);
					Assert.AreEqual(scanWidth * 2, form.Controls.OfType<Button>().Max(b => b.Width), 1);
					Assert.AreEqual(18F, form.Font.Size, 0.01);
				}
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}
	}
}
