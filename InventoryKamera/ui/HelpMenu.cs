using System.Diagnostics;
using System.Windows.Forms;

namespace InventoryKamera
{
	// 1.7.3: the Help menu in Simple and Advanced (mockup pick A): the links Classic shows on its window
	// ("Github Mainpage", "Latest Releases", "Issue Page"), in the new looks' words. Each item's Tag is the
	// page it opens.
	internal static class HelpMenu
	{
		internal const string GitHubPage = "GitHub page", LatestReleases = "Latest releases", ReportProblem = "Report a problem (Issue page)";

		internal static ToolStripMenuItem Build()
		{
			var help = new ToolStripMenuItem("Help");
			help.DropDownItems.Add(Link(GitHubPage, ForkRepository.Url));
			help.DropDownItems.Add(Link(LatestReleases, ForkRepository.ReleasesUrl));
			help.DropDownItems.Add(new ToolStripSeparator());
			help.DropDownItems.Add(Link(ReportProblem, ForkRepository.IssuesUrl));
			return help;
		}

		private static ToolStripMenuItem Link(string text, string url)
		{
			var item = new ToolStripMenuItem(text) { Tag = url };
			item.Click += (s, e) => Process.Start(url);
			return item;
		}
	}
}
