Adapted from dmitry-shishkin/Inventory_Kamera 1.4.11 (MIT). Recorded public game data only; no account data.

# Character compatibility fixture

Source: AnimeGameData2, released 7.1.0 snapshot, commit `792978e5503ecfba73dcb3562ed44a0d35a2abe2`.

These JSON files preserve source values and source array order. All avatar, skill depot, skill, talent and fetter rows remain, including both Traveler genders, alternate avatars and more than 50 playable characters. Only unrelated fields and unused text mappings are removed. `Reduce-Fixtures.ps1` recreates the files from the original local `.tools/game-data` downloads. English character strings are in `TextMap_MediumEN`; the full text map has no mappings referenced by these retained fields.

The tests intercept source downloads in-process and supply these files. Unrelated equipment/material source arrays are empty and their existing cache files are preseeded. Each test runs in a separate temporary directory and never touches the game or the real account. Tests are explicitly nonparallel because the production API uses the process working directory. The reversal test changes only array order. The error test removes the actual `Player_Ice_Constellation_3` source row.
