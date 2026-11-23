using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
	// 1.7.3: Classic's GitHub links ("Github Mainpage", "Latest Releases", "Issue Page") as a Help menu in
	// Simple and Advanced (mockup pick A).
	[TestClass]
	public sealed class HelpMenuTests
	{
		[TestMethod]
		public void SimpleAndAdvancedHaveHelpAfterOptionsWithTheThreeLinks()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				foreach (var make in new Func<Form>[] { () => new SimpleForm(), () => new AdvancedForm() })
					using (var form = make())
					{
						string look = form.GetType().Name;
						var menu = form.MainMenuStrip;
						var top = menu.Items.Cast<ToolStripItem>().Where(i => i.Name != UpdateMark.ButtonName).Select(i => i.Text).ToArray();
						CollectionAssert.AreEqual(new[] { "File", "View", "Options", "Help" }, top, $"{look}: {string.Join(", ", top)}");

						var help = (ToolStripMenuItem)menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "Help");
						var links = help.DropDownItems.OfType<ToolStripMenuItem>().Select(i => (i.Text, i.Tag as string)).ToArray();
						CollectionAssert.AreEqual(new[]
						{
							("GitHub page", ForkRepository.Url),
							("Latest releases", ForkRepository.ReleasesUrl),
							("Report a problem (Issue page)", ForkRepository.IssuesUrl),
						}, links, $"{look}: {string.Join(", ", links)}");
						foreach (var (_, url) in links) Assert.IsTrue(ForkRepository.IsOwnPage(url), $"{look}: {url} is this fork's GitHub page");
					}
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		// Simple is narrow, and the blue update button shares the menu bar: nothing may drop into the overflow.
		[TestMethod]
		public void MenusStillFitNextToTheLongestUpdateButton()
		{
			var isolation = new MaterialQuantityTests();
			isolation.IsolateSettingsDataAndLogs();
			try
			{
				AppStartup.PrepareSettings();
				foreach (var make in new Func<Form>[] { () => new SimpleForm(), () => new AdvancedForm() })
					foreach (int percent in new[] { 100, 175 })
						using (var form = make())
						{
							SimpleLookTests.Bind(form);
							Zoom.Set(form, percent);
							var menu = form.MainMenuStrip;
							var button = menu.Items[UpdateMark.ButtonName];
							foreach (var text in new[] { "⬆ New game data 10.10", "⬆ Update to 10.10.10", "⬆ 2 updates" })
							{
								button.Text = text;
								button.Visible = true;
								menu.PerformLayout();
								var hidden = menu.Items.Cast<ToolStripItem>().Where(i => i.Placement != ToolStripItemPlacement.Main).Select(i => i.Text).ToArray();
								Assert.AreEqual(0, hidden.Length, $"{form.GetType().Name} at {percent} % with \"{text}\": {string.Join(", ", hidden)} pushed off the menu bar");
							}
						}
			}
			finally { isolation.RestoreGlobalsAndRemoveOwnSandbox(); }
		}

		[TestMethod]
		public void HelpLinksAreTheSamePagesAsClassics()
		{
			Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/", ForkRepository.Url);
			Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/releases", ForkRepository.ReleasesUrl);
			Assert.AreEqual("https://github.com/Randell157/Inventory_Kamera_Fork/issues", ForkRepository.IssuesUrl);
		}
	}
}
