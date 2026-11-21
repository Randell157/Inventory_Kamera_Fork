# Inventory Kamera (Randell157 fork)

Windows Forms app (.NET Framework 4.7.2, C# 7.3, one project: `InventoryKamera/InventoryKamera.csproj`) that OCRs Genshin Impact screens with Tesseract and exports GOOD-format JSON. Forked from taiwenlee/Inventory_Kamera 1.4.5 (`upstream` remote), which is itself a fork of Andrewthe13th/Inventory_Kamera.

## Build

Both projects are SDK-style and still target .NET Framework 4.7.2 (`net472`), so the `dotnet` CLI builds them without Visual Studio. The .NET 7 SDK is installed. The 4.7.2 reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package, because this PC has no 4.7.2 Developer Pack.

```powershell
dotnet build InventoryKamera.sln -c Release -p:Platform=x64
dotnet test InventoryKamera.Tests\InventoryKamera.Tests.csproj -c Release -p:Platform=x64
```

- App output: `InventoryKamera\bin\x64\Release\`, the same file set as the 1.4.5 release plus `System.Resources.Extensions.dll`. Run the exe from that folder, because logs and lookup tables are relative to the working directory. Copy `inventorylists\` from the 1.4.5 install into it to skip the first-launch download.
- The exe asks for Administrator rights (UAC) on launch, so the agent can't run it. The user launches it.
- The dev build shares `%LOCALAPPDATA%\InventoryKamera\settings.json` with the user's 1.4.5 install.
- Tests (`InventoryKamera.Tests`, MSTest) are offline only. They never launch Genshin or send input. Run them in **both** Debug and Release: test order differs, and a test that touches `GenshinProcesor` without the `MaterialQuantityTests` isolation sandbox breaks its static initializer for the whole run.
- Some tests use the user's private screenshots, logs and exports from the folder in `IK_TEST_IMAGES` (the vault's `Inventory Kamera Fork\Test images`). Without it they are skipped. Never copy those files into the repo.
- If the user has the app open, the Release exe is locked and the build fails with MSB3027. Build Debug instead, or ask them to close it.
- `.resx` images are embedded with `GenerateResourceUsePreserializedResources`, which needs `System.Resources.Extensions` at runtime. `SmokeTests.EmbeddedResourcesLoad` guards this.

## Where things are

- Scrapers: `InventoryKamera/scraping/` (`CharacterScraper`, `WeaponScraper`, `ArtifactScraper`, `MaterialScraper`; OCR helpers in `GenshinProcesor`).
- GOOD export: `InventoryKamera/data/GOOD.cs` writes `genshinData_GOOD_yyyy_MM_dd_HH_mm.json` to the Output Path setting (default `<exe dir>\GenshinData`).
- Lookup tables: `inventorylists/{characters,weapons,artifacts,materials}.json` + `version.txt` next to the exe. These files are not in git. `data/DatabaseManager.cs` generates them from Dimbreath/AnimeGameData2 on GitLab (TextMap + ExcelBinOutput). The updater regenerates them, so fix the generator, not the JSON. Non-force updates rebuild every character from the game data and replace a saved entry only when it differs; entries not in the game data (mannequins, custom) are kept.
- Logs: `<exe dir>\logging\InventoryKamera.log` and `InventoryKamera.debug.log`, plus failed-OCR screenshots in `logging\{characters,weapons,artifacts,materials}\`. These folders are wiped at the start of each scan.
- The user's 1.4.5 evidence: logs in `C:\Users\rande\Documents\Inventory.Kamera.v1.4.5\logging\`, exports in `C:\Users\rande\Documents\InventoryKamera\Player\`. Logs are large, so grep them instead of reading them whole. Logs and exports are account data, so never commit them.

## Testing

The agent can't run Genshin. Live verification means the user runs a scan and shares the logs and exports. Prefer logic that can be tested offline, such as parsing a saved PNG or a recorded data file.

## Rules

- Keep the MIT `LICENSE` and its copyright notice intact.
- The README must credit Andrewthe13th (original) and taiwenlee (the fork this is based on). Credit any adapted code, such as dmitry-shishkin's or rogerioth's (both MIT), in the commit message and README.
- Commit only when the user asks. Push only when the user asks.
- Show a plan before changing code. Give each change "finished when" checks and verify them one by one.
- Commit identity is the GitHub no-reply address, already set in the repo config. Never commit a personal email.
