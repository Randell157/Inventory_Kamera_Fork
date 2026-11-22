namespace InventoryKamera
{
	// The words and numbers Simple shows while scanning, kept apart from the window so they can be tested.
	internal static class SimpleText
	{
		// "Weapons 12 / 306   Artifacts 0 / ?   Characters 3", only for what's being scanned.
		// A total that isn't known yet shows as "?" (the scan counts it when it opens that inventory).
		internal static string Counts(bool weapons, int weaponCount, int weaponMax, bool artifacts, int artifactCount, int artifactMax, bool characters, int characterCount)
		{
			var parts = new System.Collections.Generic.List<string>();
			string Of(int max) => max < 0 ? "?" : max.ToString("N0");
			if (weapons) parts.Add($"Weapons {weaponCount:N0} / {Of(weaponMax)}");
			if (artifacts) parts.Add($"Artifacts {artifactCount:N0} / {Of(artifactMax)}");
			if (characters) parts.Add($"Characters {characterCount:N0}");
			return string.Join("   ", parts);
		}

		// The bar's percent, or -1 for a moving bar: while a total being scanned isn't known yet, and once
		// weapons and artifacts are done (characters, dev items and materials have no total).
		internal static int Percent(bool weapons, int weaponCount, int weaponMax, bool artifacts, int artifactCount, int artifactMax)
		{
			if ((weapons && weaponMax < 0) || (artifacts && artifactMax < 0) || (!weapons && !artifacts)) return -1;
			long done = (weapons ? System.Math.Min(weaponCount, weaponMax) : 0) + (artifacts ? System.Math.Min(artifactCount, artifactMax) : 0);
			long total = (weapons ? weaponMax : 0) + (artifacts ? artifactMax : 0);
			if (total <= 0 || done >= total) return -1;
			return (int)(100 * done / total);
		}

		// "1 error · View log", "3 errors · View log"; nothing when all went well.
		internal static string ErrorLink(int errors) =>
			errors <= 0 ? "" : $"{errors:N0} error{(errors == 1 ? "" : "s")} · View log";
	}
}
