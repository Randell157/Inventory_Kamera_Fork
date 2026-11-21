namespace InventoryKamera
{
    // The custom name boxes (Wanderer, the two mannequins) fall back to their setting's own
    // default when left empty, so an empty name never reaches the scan. The Traveler is not
    // one of them: an empty Traveler name means "read it from the game" (AssignTravelerName).
    internal static class CustomNames
    {
        internal static readonly string[] FilledWhenEmpty = { "WandererName", "Manequin1Name", "Manequin2Name" };

        internal static string DefaultFor(string settingName) =>
            Properties.Settings.Default.Properties[settingName]?.DefaultValue as string ?? "";

        internal static string OrDefault(string settingName, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
            return System.Array.IndexOf(FilledWhenEmpty, settingName) >= 0 ? DefaultFor(settingName) : value;
        }
    }
}
