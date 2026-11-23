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

			// Simple: 344 × 512 inside, plus a title bar and borders (16 × 39) that don't zoom.
			var simple = new Zoom.WindowSize(new Size(344, 512), new Size(16, 39));
			Assert.AreEqual(new Size(618, 935), Zoom.SizeAt(simple, 175));
			Assert.IsTrue(Zoom.Fits(simple, 175, FullHd), "935 px tall");
			Assert.IsFalse(Zoom.Fits(simple, 200, FullHd), "1,063 px tall");
			Assert.IsTrue(Zoom.Fits(new Zoom.WindowSize(new Size(5000, 5000), Size.Empty), 100, FullHd), "100 % is always allowed");
			// Advanced: 864 × 561 inside. 175 % is 1,021 px tall with the title bar: it fits (scaling the
			// title bar too said 1,050 and wrongly refused it, while the menu allowed it).
			var advanced = new Zoom.WindowSize(new Size(864, 561), new Size(16, 39));
			Assert.AreEqual(175, Zoom.Usable(300, advanced, FullHd));
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
		internal static void Bind(Form form) =>
			typeof(Control).GetMethod("CreateControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(bool) }, null)
				.Invoke(form, new object[] { true });

		internal static Dictionary<string, object> AllSettings()
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
					Assert.AreEqual(listenersBefore, UserInterface.Listeners, "not listening until it's on screen");
					form.BecameShown();
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
					form.BecameHidden();
					Assert.AreEqual(listenersBefore, UserInterface.Listeners, "a hidden window stops listening");
					UserInterface.SetProgramStatus("Scanning");
					Assert.AreEqual("Ready", form.Status.Text, "not updated while hidden");
					form.BecameShown();
					form.BecameShown();   // twice (a zoom) doesn't listen twice
					Assert.AreEqual(listenersBefore + 3, UserInterface.Listeners);
					Assert.AreEqual("Scanning", form.Status.Text, "catches up when shown again");
					UserInterface.SetProgramStatus("");
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
					Assert.IsTrue(normal.Width <= 370 && normal.Height <= 600, $"about 360 × 595 at 100 % (with the summary lines): {normal}");
					Assert.AreEqual(175, Zoom.Usable(300, Zoom.Measure(form), FullHd));

					int scanWidth = form.Controls.OfType<Button>().Max(b => b.Width);
					Zoom.Apply(form, 200);
					Assert.AreEqual(scanWidth * 2, form.Controls.OfType<Button>().Max(b => b.Width), 1);
					Assert.AreEqual(18F, form.Font.Size, 0.01);
				}
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		// Every control's position, size and font, to compare a window before and after zooming.
		internal static List<string> Layout(Control root)
		{
			var all = new List<string> { $"{root.Name} {root.ClientSize} {root.Font.Size}" };
			void Walk(Control parent)
			{
				foreach (Control c in parent.Controls)
				{
					all.Add($"{c.GetType().Name} {c.Text} {c.Bounds} {c.Font.Name} {c.Font.Size} {c.Font.Style}");
					Walk(c);
				}
			}
			Walk(root);
			return all;
		}

		private static Form TestForm()
		{
			var form = new Form { ClientSize = new Size(333, 217), Font = new Font("Segoe UI", 9F) };
			var group = new GroupBox { Text = "g", Bounds = new Rectangle(7, 31, 151, 97) };
			group.Controls.Add(new CheckBox { Text = "c", AutoSize = true, Location = new Point(11, 23) });
			group.Controls.Add(new TextBox { Bounds = new Rectangle(13, 53, 117, 23) });
			form.Controls.AddRange(new Control[] { group, new Label { Text = "b", Bounds = new Rectangle(171, 41, 97, 19), Font = new Font("Segoe UI", 11F, FontStyle.Bold) }, new Button { Text = "x", Bounds = new Rectangle(9, 151, 311, 37) }, new MenuStrip() });
			return form;
		}

		[TestMethod]
		public void ZoomingInPlaceAndBackNeverDrifts()
		{
			using (var form = TestForm())
			{
				var original = Layout(form);
				foreach (int step in new[] { 200, 125, 175, 300, 150 })
				{
					Zoom.Set(form, step);
					Assert.AreEqual(step, Zoom.Current(form));
				}
				Zoom.Set(form, 175);
				int buttonWidth = form.Controls.OfType<Button>().Single().Width;
				Assert.AreEqual(311 * 1.75, buttonWidth, 1.5, "175 % is 175 % of the original, however it got there");
				Zoom.Set(form, 100);
				CollectionAssert.AreEqual(original, Layout(form), "back at 100 %: exactly as built");
			}

			// A window opened at a saved zoom (scaled once before it's shown) comes back to that exact layout too.
			using (var form = TestForm())
			{
				Zoom.Apply(form, 150);
				var opened = Layout(form);
				Zoom.Set(form, 100);
				Zoom.Set(form, 250);
				Zoom.Set(form, 150);
				CollectionAssert.AreEqual(opened, Layout(form));
			}
		}

		[TestMethod]
		public void SimpleZoomsInPlaceAndBackExactly()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var form = new SimpleForm())
				{
					Bind(form);
					var original = Layout(form);
					foreach (int step in new[] { 200, 125, 175, 100 }) Zoom.Set(form, step);
					CollectionAssert.AreEqual(original, Layout(form), "menus, margins and fonts all back as built");
				}
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		[TestMethod]
		public void ClassicZoomsInPlaceAndBackExactly()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var form = new MainForm())
				{
					Bind(form);
					var original = Layout(form);
					foreach (int step in new[] { 150, 200, 125, 100 }) Zoom.Set(form, step);
					CollectionAssert.AreEqual(original, Layout(form));
				}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		// Live 2026-11-22 (1.7.1 check): opened at the saved 175 %, then View → Zoom → 100 %: Classic's
		// Select button came back shorter than designed, with its text cut off. A window opened zoomed and then
		// set to 100 % must look exactly like one opened at 100 %, in every look.
		[TestMethod]
		public void OpenedZoomedThenSetTo100MatchesOpenedAt100()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				foreach (var make in new System.Func<Form>[] { () => new SimpleForm(), () => new AdvancedForm(), () => new MainForm() })
					foreach (int saved in new[] { 125, 175, 300 })
						using (var fresh = make())
						using (var zoomed = make())
						{
							Bind(fresh); Bind(zoomed);
							// The app opens at the biggest step that fits the screen (a taller window would be cut by Windows).
							int opened = Zoom.Usable(saved, Zoom.Measure(zoomed), Screen.PrimaryScreen.WorkingArea);
							Zoom.Apply(zoomed, opened);   // how a launch at a saved zoom opens the window
							Zoom.Set(zoomed, 150);
							Zoom.Set(zoomed, 100);
							var want = Layout(fresh); var got = Layout(zoomed);
							var differ = want.Zip(got, (w, g) => (w, g)).Where(p => p.w != p.g).Select(p => $"\n  want {p.w}\n  got  {p.g}").Take(6);
							Assert.IsTrue(want.Count == got.Count && !differ.Any(), $"{fresh.GetType().Name} opened at {opened} %, then 100 %:{string.Concat(differ)}");
						}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		// Live 2026-11-22 (1.7.0, Classic at 150–175 %): four scan checkboxes looked smaller than Materials, the
		// slider's "Speed: Auto (recommended)" ran into the dark panel, and the Select button stuck out below
		// the path box with its right edge covered.
		[TestMethod]
		public void ClassicZoomKeepsCheckboxesHeadingAndSelectButtonInPlace()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				using (var form = new MainForm())
				{
					Bind(form);
					T Field<T>(string name) => (T)typeof(MainForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(form);
					var boxes = new[] { "Weapons_CheckBox", "Artifacts_Checkbox", "Characters_CheckBox", "CharDevItems_CheckBox", "Materials_CheckBox" }.Select(Field<CheckBox>).ToArray();
					var heading = Field<Label>("ScannerDelay_Label");
					var panel = Field<Panel>("ScannerOutput_Panel");
					var select = Field<Button>("FileSelectButton");
					var path = Field<TextBox>("OutputPath_TextBox");
					var speeds = Field<ComboBox>("ScanSpeed_ComboBox");

					// The user chose the normal font at 100 % too: the boxes may touch, but each one's text stays clear of the next.
					for (int i = 1; i < boxes.Length; i++)
						Assert.IsTrue(boxes[i].Top - boxes[i - 1].Top >= boxes[i - 1].Font.Height, $"{boxes[i - 1].Text} and {boxes[i].Text} at 100 %");
					var selectAt100 = select.Bounds;
					var headingFontAt100 = heading.Font;

					typeof(MainForm).GetMethod("LoadScanSpeed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(form, null);   // normally on Load
					typeof(MainForm).GetMethod("ShowScanSpeedControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(form, new object[] { ScanSpeeds.Slider });
					speeds.SelectedIndex = 0;
					// An unshown form's slider counts as hidden, so the heading is set as the shown slider would set it.
					heading.Text = ScanSpeeds.SliderHeading(ScanSpeeds.StepAt(0));
					StringAssert.Contains(heading.Text, "recommended");   // Auto: the longest heading

					foreach (int step in Zoom.Steps.Concat(new[] { 150, 100 }))
					{
						Zoom.Set(form, step);
						foreach (var box in boxes) Assert.AreEqual(boxes[4].Font.Size, box.Font.Size, $"{box.Text} at {step} %");
						Assert.IsTrue(heading.Right <= panel.Left, $"heading ends at {heading.Right}, panel starts at {panel.Left} ({step} %)");
						Assert.IsTrue(TextRenderer.MeasureText(heading.Text, heading.Font).Width <= heading.Width, $"heading text fits its box at {step} %");
						Assert.IsTrue(heading.Font.Height <= heading.Height + 1, $"heading text isn't cut off at the bottom at {step} %");
						Assert.IsTrue(speeds.ItemHeight >= speeds.Font.Height, $"speed drop-down items {speeds.ItemHeight} px for {speeds.Font.Height} px text at {step} %");
						if (step == Zoom.Normal) Assert.AreEqual(15, speeds.ItemHeight, "drop-down as designed at 100 %");
						foreach (Control c in form.Controls)
							// The status line's designer text is a placeholder; SetProgramStatus fits the real text (StatusWidth).
							if (c != panel && c.Name != "ProgramStatus_Label" && c.Left < panel.Left && c.Top < panel.Bottom && c.Bottom > panel.Top && (c is Label || c is CheckBox))
								Assert.IsTrue(c.Right <= panel.Left, $"{c.Name} runs into the dark panel at {step} %");
						if (step == Zoom.Normal)
						{
							Assert.AreEqual(selectAt100, select.Bounds, "Select button as built at 100 %");
							Assert.AreEqual(headingFontAt100, heading.Font, "heading font as designed at 100 %");
						}
						Assert.AreEqual(path.Top, select.Top, $"Select top at {step} %");
						Assert.AreEqual(path.Bottom, select.Bottom, $"Select bottom at {step} %");
						Assert.AreEqual(path.Left, select.Right, $"Select ends where the path box starts at {step} %");
						var selectText = TextRenderer.MeasureText(select.Text, select.Font);
						Assert.IsTrue(selectText.Width <= select.Width && selectText.Height <= select.Height + 2, $"Select text {selectText} fits {select.Size} at {step} %");
					}
				}
			}
			finally
			{
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		// Live 2026-11-22: a scan in Advanced, then View → Simple: Simple's Optimizer button stayed grey.
		[TestMethod]
		public void OptimizerButtonCatchesUpWhenALookIsShownAgain()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			var data = typeof(ScanSession).GetProperty("Data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			try
			{
				AppStartup.PrepareSettings();
				using (var simple = new SimpleForm())
				using (var advanced = new AdvancedForm())
				using (var classic = new MainForm())
				{
					Bind(simple); Bind(advanced); Bind(classic);
					var classicButton = (Button)typeof(MainForm).GetField("ManualExportButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(classic);
					Assert.IsFalse(simple.OpenOptimizer.Enabled || advanced.OpenOptimizer.Enabled || classicButton.Enabled, "no scan yet");

					var scanned = new InventoryKamera();   // what a scan in another look left behind
					scanned.Characters.Add(new Character());
					data.SetValue(null, scanned);
					simple.BecameShown(); advanced.BecameShown(); classic.BecameShown();
					Assert.IsTrue(simple.OpenOptimizer.Enabled, "Simple");
					Assert.IsTrue(advanced.OpenOptimizer.Enabled, "Advanced");
					Assert.IsTrue(classicButton.Enabled, "Classic");

					data.SetValue(null, new InventoryKamera());   // a scan with nothing found
					simple.BecameShown(); advanced.BecameShown(); classic.BecameShown();
					Assert.IsFalse(simple.OpenOptimizer.Enabled || advanced.OpenOptimizer.Enabled || classicButton.Enabled, "nothing to copy");
				}
			}
			finally
			{
				data.SetValue(null, new InventoryKamera());
				isolation.RestoreGlobalsAndRemoveOwnSandbox();
				UserInterface.Init(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
			}
		}

		[TestMethod]
		public void WindowsKeepTheirTopLeftCornerOnScreen()
		{
			var size = new Size(630, 960);
			Assert.AreEqual(new Point(100, 50), Placement.KeepTopLeft(new Point(100, 50), size, FullHd), "fits: stays put");
			Assert.AreEqual(new Point(1290, 50), Placement.KeepTopLeft(new Point(1500, 50), size, FullHd), "would run off the right: moved left just enough");
			Assert.AreEqual(new Point(100, 72), Placement.KeepTopLeft(new Point(100, 400), size, FullHd), "would run off the bottom: moved up just enough");
			Assert.AreEqual(new Point(0, 0), Placement.KeepTopLeft(new Point(-30, -10), size, FullHd), "never above or left of the screen");
			Assert.AreEqual(new Point(0, 0), Placement.KeepTopLeft(new Point(500, 500), new Size(2500, 1200), FullHd), "too big anyway: the top-left stays visible");
			var second = new Rectangle(1920, 0, 1920, 1032);
			Assert.AreEqual(new Point(2000, 50), Placement.KeepTopLeft(new Point(2000, 50), size, second), "a second screen");
			Assert.AreEqual(new Point(645, 36), Placement.Centered(size, FullHd));
		}

		[TestMethod]
		public void EachLooksWindowIsBuiltOnceAndKept()
		{
			int builds = 0;
			var set = new WindowSet(look => { builds++; return new Form { Text = look.ToString() }; });
			var simple = set.Get(Look.Simple, out bool built);
			Assert.IsTrue(built);
			var classic = set.Get(Look.Classic, out built);
			Assert.IsTrue(built);
			Assert.AreSame(simple, set.Get(Look.Simple, out built), "switching back: the same window");
			Assert.IsFalse(built);
			Assert.AreEqual(2, builds);
			Assert.AreEqual(2, set.All.Count());

			classic.Dispose();
			Assert.AreNotSame(classic, set.Get(Look.Classic, out built), "a closed window is built again");
			Assert.IsTrue(built);
			set.Forget(simple);
			Assert.AreEqual(1, set.All.Count());
			foreach (var form in set.All.ToList()) form.Dispose();
			simple.Dispose();
		}
	}
}
