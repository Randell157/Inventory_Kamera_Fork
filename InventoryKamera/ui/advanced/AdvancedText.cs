namespace InventoryKamera
{
	// The plain-words notes beside Advanced's numbers, kept apart from the window so they can be tested.
	internal static class AdvancedText
	{
		// Weapons: 1 is the lowest level there is.
		internal static string WeaponLevel(int level) => level <= 1 ? "= any level" : $"= level {level} and up";

		// Artifacts are leveled +0 to +20 in the game.
		internal static string ArtifactLevel(int level) => level <= 0 ? "= any level" : $"= +{level} and up";

		internal static string Rarity(int stars) => $"{stars}★ and up";

		internal static string ErrorLog(int errors) => $"Error log ({errors:N0})";
	}

	// "All" or "Only the first N" for a setting where 0 means all (characters to scan, artifact pages).
	internal static class HowMany
	{
		internal const int All = 0;

		// The setting's value from the two choices. Only clamps N into its range.
		internal static int Value(bool only, int n, int max) => only ? System.Math.Max(1, System.Math.Min(n, max)) : All;

		// The number shown beside "Only the first": the setting's own number, or the last one used (1 at first).
		internal static int Shown(int value, int lastOnly, int max) =>
			value > All ? System.Math.Min(value, max) : System.Math.Max(1, System.Math.Min(lastOnly, max));
	}
}
