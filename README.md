# Job Fair

A randomizer mod for **Final Fantasy V Pixel Remaster** on Windows / Steam,
inspired by Career Day. **0.9.5 is a tester prerelease.**

## Download and play

**[Download the latest tester release](https://github.com/IXTLIA/Job-Fair/releases)**

Download `JobFair-0.9.5-Tester-Windows-x64.zip`, extract the whole archive, run
`Setup/JobFairSetup.exe`, then use `Launcher/JobFairLauncher.exe` to choose your
mode and seed. Launch FFV through Steam yourself. Use the release ZIP for playing;
GitHub's automatically generated source archives are for development.

Requires your own Windows x64 Steam FFV installation, app **1173810**, build
**20265087**, and internet access for pinned loader downloads and first-launch
setup. The installer validates the game, backs up replaced files, and supports
restoration. Back up saves separately and use disposable test slots.

See **[the tester guide](TESTER_GUIDE.md)** for installation, controls, removal,
known limitations, and bug-report details.

## Features

- **Mixed Crystals:** story progression with 21 randomized crystal jobs.
- **Remixed Crystals:** open World 1, four starting jobs, 17 hidden jobs, and
  independent keys for Worlds 2 and 3.
- **Mixed-Fiesta / Remixed-Fiesta:** four jobs plus Freelancer; duplicates allowed.
- 49 randomized boss encounters, destination-scaled stats and boss EXP.
- Treasure and finite quest/event rewards, shop inventories, regular-enemy
  drops and steals, with preserved rates and quantities.
- Equip Bells and learnable **!Kick** alongside Monk's Barehanded.
- Warp Shard world travel and transport recall, keyboard/mouse and Xbox-style
  controller support; world-map refresh repair in 0.9.5.

This release still needs native acceptance of map refresh, controller behavior,
boss defense/EXP and complete campaign progression. Dungeon keys, a shuffled
four-Tablet goal and in-game hints remain planned. Weapon behavior customization
and Advance jobs are not included.

## Contribute

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, build/test instructions and
contribution scope. The repository includes the runtime C#, Python seed generator
and launcher, installer, startup patch recipes, and portable tests. Native game
DLLs, interop assemblies, saves and extracted asset bundles are not included.

- [Report a bug](https://github.com/IXTLIA/Job-Fair/issues/new?template=bug_report.md)
- [Browse source](https://github.com/IXTLIA/Job-Fair)
- [Third-party notices](THIRD_PARTY.md)

Job Fair's original code is **[MIT licensed](LICENSE)**. Third-party software and
underlying game material retain their respective rights. This is an unofficial
fan project and requires a separately obtained copy of Final Fantasy V.
