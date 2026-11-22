# Inventory Kamera - A Genshin Data Scanner

Fan-made Genshin Impact tool that scans characters, weapons, artifacts, materials and character development items in your inventory using the OCR technique.

This scanner exports in `.GOOD`, a JSON-based exporting format, which allows you to use it with compatible online Genshin Impact tools. These tools include artifact optimizing tools including [Genshin Optimizer](https://frzyc.github.io/genshin-optimizer/#/), [SEELIE.me](https://seelie.me/) and [Aspirine's Genshin Impact Calculator](https://genshin.aspirine.su/).

> **AI disclosure:** this fork's changes were written with [Claude Code](https://claude.com/claude-code) using Claude Opus 5.5. See [AI-assisted development](#ai-assisted-development).

## Download
[LATEST DOWNLOAD HERE](https://github.com/Randell157/Inventory_Kamera_Fork/releases/latest) <- CLICK ME NOT THE "CODE" BUTTON NEAR THE TOP OF THIS PAGE

## DISCORD
The Discord server of the original Inventory Kamera project (not run by this fork): https://discord.gg/zh56aVWe3U



## Table of Contents
> **Note**
>
> Please **read the following instructions carefully** and setup before using the scanner.
- Getting Started
  - [Installing Inventory Kamera](#installing-inventory-kamera)
  - [Setting up Genshin Impact](#setting-up-genshin-impact)
  - [Settings and configurations](#how-to-configure-inventory-kamera)
  - [Running Inventory Kamera](#how-to-run-inventory-kamera)
- Scanner
  - [Updating the database](#updating-for-new-game-versions)
- Repository
  - [Reporting a bug or a scanning issue](#reporting-an-issue)
  - [Requesting a new feature](#requesting-a-new-feature)
  - [Asking a question](#asking-a-question)
  - [Frequently Asked Questions (FAQ)](#frequently-asked-questions-faq)
  - [AI-assisted development](#ai-assisted-development)
  - [Credits](#credits)
  - [License](#license)

## Installing Inventory Kamera

Before installing Inventory Kamera, please have **the following things installed on your device**:

- [GenshinImpact.exe](https://genshin.hoyoverse.com/) or [YuanShen.exe](https://ys.mihoyo.com/) launcher
- [Microsoft Visual C++ Redistributable for Visual Studio 2015-2022 (x86 or x64)](https://docs.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170#visual-studio-2015-2017-2019-and-2022), _**A computer restart may be required after installing this package for the first time**_

[Download the latest version of Inventory Kamera here](https://github.com/Randell157/Inventory_Kamera_Fork/releases/latest) and unzip its files into a folder of your choosing, then launch the extracted .exe file. It will likely prompt you for Security and/or User Access. This access is required to interact with the game.

To upgrade from a previous version, download a newer release and unzip its files into the current Inventory Kamera location (allow it to replace old files when prompted) or a new folder of your choosing.

## Setting up Genshin Impact

1. Log in to Genshin Impact and click start.
2. Open [Paimon menu](https://genshin-impact.fandom.com/wiki/Paimon_Menu) (Default shortcut: `ESC`).
3. Go to `Settings` (Cog Icon ⚙) and set these settings:
   - Under `Languages`, set _Game Language_ to English.
   - The game resolution should be _16:9_ or _16:10_. If your screen has a matching native resolution, you may set _Display Mode_ under `Graphics` to fullscreen. Otherwise, you'll have to switch it to any _windowed_ resolution that matches one of these aspect ratios.
     - Some examples of good resolutions are: 1920x1080 (Full HD), 1920x1200, 2560x1440 (QHD), 3840x2160 (4K), etc.
     - Don't know if a resolution is 16:9 or 16:10? [Find out here](https://andrew.hedges.name/experiments/aspect_ratio/).
        > **Warning**
        >
        > If you have an ultrawide screen, please see [this thread in the original project](https://github.com/Andrewthe13th/Inventory_Kamera/issues/40)
        > Additionally, HDR is not currently supported. If you experience scanning issues, please make sure this is disabled in Windows Settings
   - Under `Controls`, set _Control Type_ to Keyboard.
     - If you rebound keys (inventory (default: B), character screen (default: C), or selecting character 1 (default: 1)) revert your binding to default or set up the new key binding in Inventory Kamera.

## How to configure Inventory Kamera

Before starting the scanner, you can (optionally) edit the following options:

- Select which categories (weapons, artifacts, characters, items) you want to scan.
- Configure minimum weapon and artifact rarity to be scanned.
- "Delay per item" sets the scan speed. **Auto** (recommended) waits for each weapon and artifact card, and each character screen, to finish loading before reading it; it is usually both the fastest and the most reliable choice. The fixed speeds (Fastest 100 ms to Slowest 300 ms) also check the card; pick a slower one if menus or characters are misread. Hover over it to see every wait and the trade-offs. Prefer a slider? Choose Options → Scan Speed Control → Slider.
- Constellations and talent levels are read straight from the lists on the character screen, at every speed; anything unclear is clicked and read the old way.
- Set the file export destination in the File Directory.
- Inventory Kamera uses the default keybinds to open the inventory (B) and character screens (C). These settings may be customized within Inventory Kamera's options.

## How to run Inventory Kamera

1. Start the Inventory Kamera scan by **leaving the game with the Paimon Menu open** and clicking 'Scan'.
> **Warning**
>
> While scanning, **do not use your mouse or keyboard**. The scanner uses keyboard and mouse input to automate scanning.
>
> If you want to terminate the scan early, you can press `ENTER` button at any time. This will cancel the scan, and the application will not automatically output any scanned results. You may press the 'Export Scanned Data' button to force the export of the most recently collected data (whether it's complete or incomplete).

2. Wait for Inventory Kamera to complete its scan for the `Scan Items` selections.

3. Press the `Open Export Folder` button to open a File Explorer window. This is where Inventory Kamera exports its scanned data.




## Updating for new game versions

Inventory Kamera uses lists of valid items and characters to assist with text recognition. These lists are kept locally in the `inventorylists` folder. These lists should be updated every time a new version of the game is released, and it can be done either automatically or manually.

### Updating automatically

A notification window will appear when starting the application and a new version of Genshin game data is found. Inventory Kamera syncs with [Dimbreath's Anime Game Data Repo](https://gitlab.com/Dimbreath/AnimeGameData) for new game versions. Big thanks for all the hard work done there.

If the update window does not show up when the applications starts, you can select `Update Lookup Tables` under `Options` to run the automatic updater. You may optionally force the updater to run if it does not detect a new Genshin Impact version. A normal update also refreshes characters already in your lists, so corrected game data reaches older installs too.

### Updating manually

> **Warning**
> 
> This option should only be necessary if Inventory Kamera's `Update Lookup Tables` reports an error or the generated ouputs are invalid.

The materials and weapons lists are kept in a simple key:value JSON-readable format. 'value' is the name of an item in [PascalCase](<https://en.wikipedia.org/wiki/Naming_convention_(programming)#Examples_of_multiple-word_identifier_formats>) and 'key' is whatever 'value' is but in lowercase. 

The format for manually updating characters and artifacts is slightly different. The key for both json files is still the lowercase version of the character/artifact set name in PascalCase, and the value is in the following example formats:

characters.json
```json
"albedo": {
    "GOOD": "Albedo",
    "ConstellationName": [
      "Princeps Cretaceus"
    ],
    "ConstellationOrder": [
      "skill",
      "burst"
    ],
    "Element": [
      "geo"
    ],
    "WeaponType": 0
  },


"traveler": {
    "GOOD": "Traveler",
    "Element": [
      "electro",
      "pyro",
      "dendro",
      "geo",
      "hydro",
      "anemo"
    ],
    "ConstellationOrder": {
      "electro": [
        "burst",
        "skill"
      ],
      "dendro": [
        "skill",
        "burst"
      ],
      "geo": [
        "burst",
        "skill"
      ],
      "hydro": [
        "skill",
        "burst"
      ],
      "anemo": [
        "burst",
        "skill"
      ]
    },
    "WeaponType": 0
  },
```
Note: The ConstellationName value is formatted as an array. This primarily applies to only the traveler as the male and female versions of the characters have different constellation names.
Note: The Traveler is a unique case in which constellation skill orders are be provided for each currently available element.

The character's name is as it appears in the party menu on the right side of the in-world UI or the character's menu screen in the top left corner. The constellation order depends on which talent the third constellation upgrades for each character.

Weapon type values are as follows:

0 = Sword, 1 = Claymore, 2 = Polearm, 3 = Bow, 4 = Catalyst

artifacts.json
```json
"adventurer": {
    "setName": "Adventurer",
    "GOOD": "Adventurer",
    "normalizedName": "adventurer",
    "artifacts": {
      "goblet": {
        "artifactName": "Adventurer's Golden Goblet",
        "GOOD": "AdventurersGoldenGoblet",
        "normalizedName": "adventurersgoldengoblet"
      },
      "plume": {
        "artifactName": "Adventurer's Tail Feather",
        "GOOD": "AdventurersTailFeather",
        "normalizedName": "adventurerstailfeather"
      },
      "circlet": {
        "artifactName": "Adventurer's Bandana",
        "GOOD": "AdventurersBandana",
        "normalizedName": "adventurersbandana"
      },
      "flower": {
        "artifactName": "Adventurer's Flower",
        "GOOD": "AdventurersFlower",
        "normalizedName": "adventurersflower"
      },
      "sands": {
        "artifactName": "Adventurer's Pocket Watch",
        "GOOD": "AdventurersPocketWatch",
        "normalizedName": "adventurerspocketwatch"
      }
    }
  },
```

Consider using [a JSON text validator](https://jsonlint.com/) after following this manual method. Support may or may not be provided if you choose this route.

### Creating a new issue

Open issues for this fork at [github.com/Randell157/Inventory_Kamera_Fork/issues](https://github.com/Randell157/Inventory_Kamera_Fork/issues). We would **love to have Screenshots (especially video recordings!) and Error Logs as evidence**. These can be very helpful for debugging your problem. Add it to the issue via drag-and-drop or by attaching the file to the template. Inventory Kamera may place screenshots in the `logging` folder (divided into categories) when it thinks it encountered an issue. Attaching a zipped copy of your `logging` folder is the best way to submit logs. You may check the 'Log All Screenshots' box to force this behavior in most areas that may concern the devs.

## Frequently Asked Questions (FAQ)

#### Can Inventory Kamera get me banned?

According to [HoYoverse's response to Script, Plug-In, and Third-Party Software](https://genshin.hoyoverse.com/en/news/detail/5763), we believe, no.

The scanner does not provide any exploits or game progression. It only takes screenshots of a portion of the game window, processes the image and exports the data. The account will still be yours to keep. We do not provide any account exchanges or primogem top-ups. Game interaction is limited to only menu navigation.

In addition, we have not received any warnings about the application development.
However, that does not mean it will stay that way forever! We are at the mercy of HoYoverse.

#### What Languages does Inventory Kamera support?

Inventory Kamera only supports _**English**_ currently. This limitation is due to only having a trained dataset available for the English language. The in-game "Game Langugae" option must be set to Engish. Scanning efficiency is not guaranteed for other languages. More languages may be implemented in the future but there are no current plans.

## AI-assisted development

The changes in this fork, from version 1.5.0 on, were made with [Claude Code](https://claude.com/claude-code), Anthropic's AI coding tool, using the Claude Opus 5.5 model. Claude Code wrote the code, tests and documentation changes. The maintainer directed the work, reviewed it, and checked each fix with real in-game scans before it was committed. Commits written this way carry a `Co-Authored-By: Claude` line.

The code inherited from Andrewthe13th, taiwenlee and dmitry-shishkin (see [Credits](#credits)) is their own work and is not covered by this note.

## Credits

This is a maintained fork. The source code lives at [Randell157/Inventory_Kamera_Fork](https://github.com/Randell157/Inventory_Kamera_Fork).

- [Andrewthe13th/Inventory_Kamera](https://github.com/Andrewthe13th/Inventory_Kamera): the original Inventory Kamera.
- [taiwenlee/Inventory_Kamera](https://github.com/taiwenlee/Inventory_Kamera): the fork this one is based on (version 1.4.5).
- [dmitry-shishkin/Inventory_Kamera](https://github.com/dmitry-shishkin/Inventory_Kamera): the SDK-style build setup, the Cryo Traveler lookup fix and the material quantity reader (with their offline tests and recorded game data) are adapted from its 1.4.11 release (MIT).

## License

- This project is distributed under the [MIT license](LICENSE).
- This project uses third-party libraries and other resources that may be distributed under other licenses.

---

All rights reserved by © Cognosphere Pte. Ltd. This project is not affiliated with nor endorsed by HoYoverse. Genshin Impact™ and other properties belong to their respective owners.
