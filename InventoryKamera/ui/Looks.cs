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

		// Advanced is built in the next step of 1.7; until then it opens as Classic.
		internal static bool IsBuilt(Look look) => look == Look.Classic || look == Look.Simple;

		internal static Look Shown(Look look) => IsBuilt(look) ? look : Look.Classic;

		internal static string MenuText(Look look) => look.ToString();

		internal static string NotBuiltTip(Look look) => $"{look} is coming in Inventory Kamera 1.7.";

		// Someone upgrading opens on Classic and may not know the new looks exist: Classic says so once in
		// its status line. A new install opens on Simple and is never told (PrepareSettings marks it shown).
		internal const string HintText = "New: View → Simple";

		internal static bool ShowHint(bool hintShown, Look shown) => !hintShown && shown == Look.Classic;

		// Simple's pointer to the other options: Advanced once it's built, Classic until then.
		internal static string MoreOptionsText => "More options: View → " + (IsBuilt(Look.Advanced) ? Look.Advanced : Look.Classic);
	}
}
