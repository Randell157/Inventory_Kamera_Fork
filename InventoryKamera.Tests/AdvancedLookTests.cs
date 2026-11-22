using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7 step 4: the Advanced window, its "All / Only N" choices and the star rating. Offline; windows are
	// created but never shown.
	[TestClass]
	public sealed class AdvancedLookTests
	{
		[TestMethod]
		public void LevelAndRarityNotes()
		{
			Assert.AreEqual("= any level", AdvancedText.WeaponLevel(1));
			Assert.AreEqual("= level 40 and up", AdvancedText.WeaponLevel(40));
			Assert.AreEqual("= any level", AdvancedText.ArtifactLevel(0));
			Assert.AreEqual("= +16 and up", AdvancedText.ArtifactLevel(16));
			Assert.AreEqual("3★ and up", AdvancedText.Rarity(3));
			Assert.AreEqual("Error log (2)", AdvancedText.ErrorLog(2));
		}

		[TestMethod]
		public void HowManyMapsAllAndOnlyN()
		{
			Assert.AreEqual(0, HowMany.Value(only: false, 5, 10), "All is 0");
			Assert.AreEqual(5, HowMany.Value(only: true, 5, 10));
			Assert.AreEqual(10, HowMany.Value(only: true, 15, 10), "clamped to the limit");
			Assert.AreEqual(1, HowMany.Value(only: true, 0, 10), "Only means at least 1");
			Assert.AreEqual(4, HowMany.Shown(4, 7, 10), "the setting's own number");
			Assert.AreEqual(7, HowMany.Shown(0, 7, 10), "All: the last number used");
			Assert.AreEqual(1, HowMany.Shown(0, 0, 10), "1 at first");

			using (var pages = new HowManyControl("All artifact pages", "pages", 10))
			{
				int changes = 0;
				pages.ValueChanged += (s, e) => changes++;
				Assert.IsTrue(pages.AllChoice.Checked);
				Assert.AreEqual(1, pages.Number.Value);

				pages.Value = 4;
				Assert.IsTrue(pages.OnlyChoice.Checked);
				Assert.AreEqual(4, pages.Number.Value);
				pages.Value = 0;
				Assert.IsTrue(pages.AllChoice.Checked);
				Assert.AreEqual(4, pages.Number.Value, "All keeps the last N beside Only");

				pages.OnlyChoice.Checked = true;
				Assert.AreEqual(4, pages.Value, "back to Only: the last N");
				pages.Number.Value = 7;
				Assert.AreEqual(7, pages.Value);
				pages.AllChoice.Checked = true;
				Assert.AreEqual(0, pages.Value);
				pages.Number.Value = 3;
				Assert.IsTrue(pages.OnlyChoice.Checked, "typing a number picks Only");
				Assert.AreEqual(3, pages.Value);
				pages.Value = 50;
				Assert.AreEqual(10, pages.Value, "pages go up to 10");
				Assert.IsTrue(changes >= 6, $"ValueChanged raised {changes} times");
			}
			using (var characters = new HowManyControl("All characters", "characters", 100))
			{
				characters.Value = 100;
				Assert.AreEqual(100, characters.Value);
			}
		}

		private static void Press(StarRating stars, Keys key) =>
			typeof(StarRating).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(stars, new object[] { new KeyEventArgs(key) });

		private static void Click(StarRating stars, Point at) =>
			typeof(StarRating).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(stars, new object[] { new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0) });

		[TestMethod]
		public void StarsSetByClickAndKeys()
		{
			using (var form = new Form())
			using (var stars = new StarRating { Font = new Font("Segoe UI", 9F) })
			{
				form.Controls.Add(stars);
				Assert.IsTrue(stars.TabStop, "reachable with Tab");
				Assert.AreEqual(1, stars.Value);
				Assert.AreEqual("1★ and up", stars.AccessibleDescription, "a screen reader hears the value");
				Assert.AreEqual(AccessibleRole.Slider, stars.AccessibleRole);

				var third = stars.StarBounds(2);
				Click(stars, new Point(third.Left + third.Width / 2, third.Top + third.Height / 2));
				Assert.AreEqual(3, stars.Value);
				Assert.AreEqual("3★ and up", stars.AccessibleDescription);

				Press(stars, Keys.Right);
				Assert.AreEqual(4, stars.Value);
				Press(stars, Keys.End);
				Press(stars, Keys.Right);
				Assert.AreEqual(5, stars.Value, "no more than 5");
				Press(stars, Keys.Home);
				Press(stars, Keys.Left);
				Assert.AreEqual(1, stars.Value, "no less than 1");
				Press(stars, Keys.D4);
				Assert.AreEqual(4, stars.Value, "number keys");
				stars.Value = 9;
				Assert.AreEqual(5, stars.Value);

				int before = stars.StarBounds(4).Right;
				stars.Font = new Font("Segoe UI", 18F);
				Assert.IsTrue(stars.StarBounds(4).Right > before * 1.8, "stars grow with the zoom");
			}
		}

		private static void WithAdvanced(Action<AdvancedForm, Properties.Settings> test)
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var form = new AdvancedForm())
				{
					SimpleLookTests.Bind(form);
					test(form, Properties.Settings.Default);
				}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		[TestMethod]
		public void OpeningAdvancedChangesNoSetting()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				var settings = Properties.Settings.Default;
				settings.MinimumWeaponRarity = 4;
				settings.MinimumArtifactLevel = 12;
				settings.SortByObtained = 3;
				settings.NumOfCharToScan = 0;
				settings.ScanSpeed = 2;
				var before = SimpleLookTests.AllSettings();
				using (var form = new AdvancedForm())
				{
					SimpleLookTests.Bind(form);
					form.BecameShown();
					form.BecameHidden();
					form.Close();
				}
				var after = SimpleLookTests.AllSettings();
				foreach (var name in before.Keys) Assert.AreEqual(before[name], after[name], name);
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		[TestMethod]
		public void EveryAdvancedControlIsBoundBothWays()
		{
			WithAdvanced((form, s) =>
			{
				// Setting → control
				s.ScanWeapons = false; s.ScanArtifacts = true; s.ScanCharacters = false; s.ScanCharDevItems = true; s.ScanMaterials = false;
				s.LogScreenshots = true; s.EquipWeapons = false; s.EquipArtifacts = true;
				s.MinimumWeaponRarity = 4; s.MinimumWeaponLevel = 40; s.MinimumArtifactRarity = 5; s.MinimumArtifactLevel = 16;
				s.SortByObtained = 3; s.NumOfCharToScan = 0; s.ScanSpeed = ScanSpeeds.Auto;
				s.OutputPath = @"C:\Exports"; s.TravelerName = "Lumine"; s.WandererName = "Kunikuzushi"; s.Manequin1Name = "Mona2"; s.Manequin2Name = "Kaeya2";
				Assert.AreEqual((false, true, false, true, false), (form.Weapons.Checked, form.Artifacts.Checked, form.Characters.Checked, form.DevItems.Checked, form.Materials.Checked));
				Assert.AreEqual((true, false, true), (form.LogScreenshots.Checked, form.EquipWeapons.Checked, form.EquipArtifacts.Checked));
				Assert.AreEqual((4, 40m, 5, 16m), (form.WeaponRarity.Value, form.WeaponLevel.Value, form.ArtifactRarity.Value, form.ArtifactLevel.Value));
				Assert.AreEqual("= level 40 and up", form.WeaponLevelNote.Text);
				Assert.AreEqual("= +16 and up", form.ArtifactLevelNote.Text);
				Assert.AreEqual((3, true), (form.ArtifactPages.Value, form.ArtifactPages.OnlyChoice.Checked));
				Assert.AreEqual((0, true), (form.CharacterCount.Value, form.CharacterCount.AllChoice.Checked));
				Assert.AreEqual(0, form.ScanSpeed.SelectedIndex, "Auto is first");
				Assert.AreEqual((@"C:\Exports", "Lumine", "Kunikuzushi", "Mona2", "Kaeya2"),
					(form.OutputPath.Text, form.TravelerName.Text, form.WandererName.Text, form.Mannequin1Name.Text, form.Mannequin2Name.Text));

				// Control → setting
				form.Weapons.Checked = true; form.Materials.Checked = true; form.LogScreenshots.Checked = false; form.EquipWeapons.Checked = true;
				form.WeaponRarity.Value = 3; form.WeaponLevel.Value = 1; form.ArtifactRarity.Value = 4; form.ArtifactLevel.Value = 0;
				form.ArtifactPages.AllChoice.Checked = true;
				form.CharacterCount.OnlyChoice.Checked = true;
				form.CharacterCount.Number.Value = 8;
				form.ScanSpeed.SelectedIndex = ScanSpeeds.ChoiceOf(1);
				form.OutputPath.Text = @"D:\Genshin"; form.TravelerName.Text = ""; form.WandererName.Text = "Wanderer";
				Assert.AreEqual((true, true, false, true), (s.ScanWeapons, s.ScanMaterials, s.LogScreenshots, s.EquipWeapons));
				Assert.AreEqual((3, 1, 4, 0), (s.MinimumWeaponRarity, s.MinimumWeaponLevel, s.MinimumArtifactRarity, s.MinimumArtifactLevel));
				Assert.AreEqual((0, 8), (s.SortByObtained, s.NumOfCharToScan));
				Assert.AreEqual(1, s.ScanSpeed);
				Assert.AreEqual((@"D:\Genshin", "", "Wanderer"), (s.OutputPath, s.TravelerName, s.WandererName));
				Assert.AreEqual("= any level", form.WeaponLevelNote.Text);
			});
		}

		[TestMethod]
		public void SpeedDropDownHasClassicsTooltips()
		{
			WithAdvanced((form, s) =>
			{
				Assert.AreEqual(DrawMode.OwnerDrawFixed, form.ScanSpeed.DrawMode, "drawn so the open list can explain each choice");
				foreach (int step in new[] { ScanSpeeds.Auto, 0, 2, 4 })
				{
					s.ScanSpeed = step;
					Assert.AreEqual(ScanSpeeds.TooltipText(step), form.SpeedTips.CurrentTip(form.ScanSpeed), "the chosen speed, as in Classic");
				}
				form.ScanSpeed.SelectedIndex = ScanSpeeds.ChoiceOf(1);
				Assert.AreEqual(ScanSpeeds.TooltipText(1), form.SpeedTips.CurrentTip(form.ScanSpeed));
			});
		}

		[TestMethod]
		public void ChangeLightsUpItsBoxAndFocusesIt()
		{
			WithAdvanced((form, s) =>
			{
				var timer = (Timer)typeof(AdvancedForm).GetField("highlightTimer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
				Assert.AreEqual(1500, timer.Interval, "about a second and a half");
				var normal = form.GroupFor(AdvancedForm.Section.Weapons).BackColor;

				form.Highlight(AdvancedForm.Section.Weapons);
				Assert.AreSame(form.GroupFor(AdvancedForm.Section.Weapons), form.Highlighted);
				Assert.AreEqual(AdvancedForm.HighlightFill, form.Highlighted.BackColor);
				Assert.AreEqual(normal, form.GroupFor(AdvancedForm.Section.Artifacts).BackColor, "only that box");
				Assert.AreSame(form.WeaponRarity, form.ActiveControl, "focus on the weapon stars");
				Assert.IsTrue(timer.Enabled);

				form.Highlight(AdvancedForm.Section.Artifacts);   // another Change before it ended
				Assert.AreEqual(normal, form.GroupFor(AdvancedForm.Section.Weapons).BackColor, "the first one is off again");
				Assert.AreSame(form.ArtifactRarity, form.ActiveControl);

				s.NumOfCharToScan = 5;
				form.Highlight(AdvancedForm.Section.Characters);
				Assert.AreSame(form.CharacterCount, form.ActiveControl, "the characters choice…");
				Assert.AreSame(form.CharacterCount.OnlyChoice, form.CharacterCount.ActiveControl, "…on the option that's chosen");

				form.EndHighlight();   // what the timer does after 1.5 s
				Assert.IsNull(form.Highlighted);
				Assert.AreEqual(normal, form.GroupFor(AdvancedForm.Section.Characters).BackColor, "looks as before");
				Assert.IsFalse(timer.Enabled);
				Assert.AreSame(form.CharacterCount.OnlyChoice, form.CharacterCount.ActiveControl, "the focus stays");
			});
		}

		[TestMethod]
		public void AdvancedListensOnlyWhileShownAndZoomsBackExactly()
		{
			int before = UserInterface.Listeners;
			WithAdvanced((form, s) =>
			{
				Assert.AreEqual(before, UserInterface.Listeners, "not listening until shown");
				form.BecameShown();
				Assert.AreEqual(before + 2, UserInterface.Listeners, "status and errors (counts go straight to its labels)");
				UserInterface.ResetErrors();
				UserInterface.AddError("test error");
				Assert.AreEqual("Error log (1)", form.ErrorTitle.Text);
				StringAssert.Contains(form.ErrorLog.Text, "test error", "the full log is in the window");
				UserInterface.ResetErrors();
				form.BecameHidden();
				Assert.AreEqual(before, UserInterface.Listeners);

				var original = SimpleLookTests.Layout(form);
				foreach (int step in new[] { 150, 125, 200, 100 }) Zoom.Set(form, step);
				CollectionAssert.AreEqual(original, SimpleLookTests.Layout(form));
				Assert.IsTrue(form.Size.Width <= 900 && form.Size.Height <= 620, $"about 880 × 600: {form.Size}");
				Assert.AreEqual(175, Zoom.Usable(300, Zoom.Measure(form), new Rectangle(0, 0, 1920, 1032)), "up to 175 % on a 1080p screen");
			});
		}
	}
}
