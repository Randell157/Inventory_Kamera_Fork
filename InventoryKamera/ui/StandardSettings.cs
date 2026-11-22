using System;
using System.ComponentModel;
using System.Configuration;
using System.Linq;

namespace InventoryKamera
{
	// "Use standard settings" (Simple, 1.7): what to scan and how much, put back to the app's own defaults
	// (the values a new install gets, read from the settings themselves so they can't drift apart):
	// all five items, weapons 3★ and up, artifacts 4★ and up, any level, every page and character, equip on.
	// Names, the export folder, the scan speed and the keys are left alone.
	internal static class StandardSettings
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static readonly string[] Names = {
			"ScanWeapons", "ScanArtifacts", "ScanCharacters", "ScanCharDevItems", "ScanMaterials",
			"MinimumWeaponRarity", "MinimumWeaponLevel", "EquipWeapons",
			"MinimumArtifactRarity", "MinimumArtifactLevel", "SortByObtained", "EquipArtifacts",
			"NumOfCharToScan",
		};

		internal const string Tip = "Scan everything: weapons 3★ and up, artifacts 4★ and up, any level, every page and character.";

		internal static object Default(ApplicationSettingsBase settings, string name)
		{
			var property = settings.Properties[name];
			return TypeDescriptor.GetConverter(property.PropertyType).ConvertFromInvariantString((string)property.DefaultValue);
		}

		internal static bool IsStandard(ApplicationSettingsBase settings) =>
			Names.All(name => Equals(settings[name], Default(settings, name)));

		internal static void Apply(ApplicationSettingsBase settings)
		{
			var changed = Names.Where(name => !Equals(settings[name], Default(settings, name))).ToList();
			foreach (var name in changed) settings[name] = Default(settings, name);
			settings.Save();
			Logger.Info("Standard settings restored ({0})", string.Join(", ", changed));
		}
	}
}
