namespace InventoryKamera
{
	// 1.7: the window comes in three looks, picked from the View menu and saved in the "Look" setting.
	internal enum Look { Classic = 0, Simple = 1, Advanced = 2 }

	internal static class Looks
	{
		internal const int Unset = -1;   // the setting's default: not chosen yet

		// The look to open: the saved one once chosen. Until then, a new install starts on Simple and
		// someone upgrading (settings already there) keeps the window they know, Classic.
		internal static Look Stored(int saved, bool hadSettings) =>
			saved >= (int)Look.Classic && saved <= (int)Look.Advanced ? (Look)saved : hadSettings ? Look.Classic : Look.Simple;

		// Simple and Advanced are built in the next steps of 1.7; until then they open as Classic.
		internal static bool IsBuilt(Look look) => look == Look.Classic;

		internal static Look Shown(Look look) => IsBuilt(look) ? look : Look.Classic;

		internal static string MenuText(Look look) => look.ToString();

		internal static string NotBuiltTip(Look look) => $"{look} is coming in Inventory Kamera 1.7.";
	}
}
