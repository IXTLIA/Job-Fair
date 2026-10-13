# Job Fair 0.9.5 — tester release

An unofficial Final Fantasy V Pixel Remaster randomizer for **Windows x64,
Steam app 1173810, build 20265087**. You need your own installed copy of FFV.
This is a development playtest release; full campaign completion and all
randomized boss matchups have not been validated in the game.

## Install and start

1. Back up your FFV saves. They are normally under your Windows Documents
   folder, `My Games\FINAL FANTASY V PR\Steam`. Documents may be redirected to
   OneDrive. Use separate save slots for testing; Steam Cloud and autosaves
   remain the game's responsibility.
2. Close FFV. Extract **the entire ZIP** to a permanent folder outside the game
   directory. Do not run the programs from inside the ZIP.
3. Open `Setup\JobFairSetup.exe`. Select the FFV folder inside your Steam
   library's `steamapps\common`, then click **Validate and install**.
   Steam's Manage → Browse local files can help find it.
4. Setup checks the game build and native file hashes, downloads the pinned
   BepInEx and Magicite releases from their official GitHub repositories, and
   verifies their exact size and SHA-256. Internet access is required; about
   35 MB is downloaded. It generates the startup overrides from your own
   game files and installs Job Fair. Replaced files are backed up and verified.
   No Python or developer tools need to be installed.
5. Open `Launcher\JobFairLauncher.exe`. Choose the same game folder, mode and
   seed. Click **Apply for next New Game** while FFV is closed.
6. Launch FFV **through Steam yourself**, then start a New Game. First launch
   may take several minutes while BepInEx downloads Unity libraries and
   generates local interop assemblies. Keep an internet connection available.

Keep each EXE with its `_internal` folder. Setup and the launcher never launch
FFV. If Windows blocks writing into the chosen game folder, stop and resolve
that folder's permissions before retrying; setup does not elevate itself.

Use a clean game installation, or the same pinned BepInEx/Magicite versions.
Different loader versions and other Magicite override folders are rejected.
Other mods are not part of this release's compatibility testing.

## Choose a mode

| Mode | Jobs and progression |
| --- | --- |
| Mixed Crystals | Full Freelancer party; follow the story from Wind Shrine. All 21 jobs shuffle among the crystals. Bosses shuffle within Simple/Complex groups. |
| Remixed Crystals | Open World 1 exploration, four starting jobs, 17 more hidden in treasure. Two independent world keys unlock Worlds 2 and 3. All 49 bosses use one shuffle pool. |
| Mixed-Fiesta | Mixed progression, with only four random jobs unlocked at Wind Shrine. |
| Remixed-Fiesta | Remixed progression, with only the four starting jobs and no hidden job crystals. |

Fiesta keeps Freelancer available and allows duplicate jobs across characters.
Bosses keep their native tactics with stats scaled to their destination.
Boss base EXP uses the destination's effective HP budget, before native boosts
and distribution. Regular enemies stay in place, with shuffled drop/steal
contents and their original rates and quantities.

Rewards shuffle by world: 245 treasures and 35 finite quest/event rewards.
The 15 timed Karnak Castle rewards remain fixed. Items, equipment, gil, spells,
songs and summons participate. Original payments and prerequisites still apply.
Sealed Weapons and the Brave Blade/Chicken Knife choice keep their native rules.
Shops retain world/type pools and ordinary prices.

Newly learning Barehanded at Monk level 2 also teaches **!Kick**, usable on other
jobs. It is not automatically granted to characters who already knew Barehanded.
Equip Bells remains included.

## Warp Shard controls

On a safe Remixed overworld, stand still, hold **View**, press **Y**, then
release both. Use **D-pad / left stick / LB / RB** to choose, **A** to travel
and **B** to cancel. Keyboard **F6** and mouse controls remain available.
Warp Shard supports unlocked worlds and transport recall.

## Test priorities and known limits

- **Map refresh:** World 2 → World 1; World 2 → World 3; World 3 submarine dive
  → surface. Check the overlay, full map and markers. The 0.9.5 repair has passed
  offline checks; native visual acceptance is still pending.
- **Boss repair:** fresh Remixed seed 2 should place Halicarnassus at Wind
  Shrine, with 250 HP, 1 physical defense, 2 magic defense and 250 base EXP.
  Check normal-form attacks, Zeninage, victory EXP, original ABP and progression.
  Explicit in-game acceptance of the defense/EXP repair remains pending.
- **Controller:** open, select, cancel and travel; test reconnecting and
  switching window focus. Full hardware acceptance remains pending.
- **Jobs/rewards:** compare with the launcher's spoiler, check Fiesta job
  limits and finite event rewards. Test save, full quit and reload.
- **!Kick:** learning and cross-job use were reported working; full-quit reload
  remains to be checked.
- **Progression:** Remixed skips selected early story scenes, but later story
  removal and a complete nonlinear campaign still need work. Dungeon bosses
  remain required. Dungeon keys, the shuffled four-Tablet goal and in-game
  hints are not included yet. Weapon behavior customization and Advance jobs
  are not included.

Changing the launcher's mode/seed prepares the next New Game; it does not
convert existing saves. Older runs retain their embedded rules. Use a fresh
run to test the current reward, boss and EXP settings.

## Report a problem

Record version **0.9.5**, mode, seed, location, expected/actual behavior and
steps to reproduce. Include a screenshot if useful. After closing FFV, copy
`BepInEx\LogOutput.log` before the next launch overwrites it. For early loader
failures also check `BepInEx\ErrorLog.log` and game-root `preloader_*.log`.
Send these to the person who supplied this tester ZIP. Review logs for local
paths before sharing them publicly. Preserve the affected save separately.

## Updates, backups and removal

Setup prints a backup journal path under
`%LOCALAPPDATA%\JobFair\Installer\backups\<installation-id>\restore.json`.
Keep that folder. Setup backs up every file it replaces; it does not make a
save backup or modify saves, seed configuration, Steam settings or the registry.

Close FFV, open setup, choose **Restore a backup / remove**, then select that
`restore.json`. Restore newer installations first, working back to the original
installation if removing everything. Replaced files return to their previous
bytes and installer-added files are removed. A same-version no-op reinstall
has an empty journal; use the original installation's journal for removal.

Restore refuses missing/damaged backups and files changed by a later update.
Generated loader configurations, cache, interop assemblies, logs and saves are
retained. On a clean installation, removing `winhttp.dll` disables the loader.
On an existing loader installation, restoration returns to the prior mod state.
If setup stops partway through, keep its reported journal and use Restore.
If a file changed externally, preserve it and resolve the conflict before retrying.

## Package contents and third-party components

The runtime DLL and launcher match the installed 0.9.5 release. The installer
contains startup patches, not extracted startup assets or the game's DLLs.
It does not include personal profiles, seeds, saves, logs or game-generated
interop assemblies. Loader releases are downloaded directly by setup.

Component license texts and notices are retained under each program's
`_internal\notices`; setup also installs the loader license snapshots.
Python, Tcl/Tk, PyInstaller and UnityPy dependency notices accompany setup.
These third-party notices do not assign a license to Job Fair's original code.

Pinned loader sources:

- https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2
- https://github.com/Silvris/Magicite/releases/tag/v2.2.0

Job Fair original code is licensed under MIT; see LICENSE.
Download releases and report issues at https://github.com/IXTLIA/Job-Fair.
