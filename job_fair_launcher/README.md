# Job Fair launcher

Current package: **0.9.5 world-map refresh repair**, requiring its matching runtime.
Remixed map selection now follows world changes and submarine surfacing,
while preserving valid native map variants. See `WORLD_MAP_PLAYTEST.md`.
On a Remixed overworld, hold **View** and press **Y**, then release both.
Use **D-pad / left stick / LB / RB** to choose, **A** to travel, **B** to cancel.
F6, keyboard and mouse still work. Controller hardware acceptance is pending.
Physical and magic defenses scale with the square root of the encounter HP
budget ratio. This preserves the donor's armor patterns without turning a
destination's temporary defensive phase into permanent armor. Native acceptance
is pending; see the latest project checkpoint for installation status.

Open `JobFairLauncher.exe` from the complete packaged launcher folder, or run `LaunchJobFair.ps1` from a source checkout with Python and Tcl/Tk installed.

Choose your **FFV installation folder** with **Browse…**, or use **Find Steam game**. Steam libraries are detected automatically; if several copies are found, choose one from the folder list. The selected game folder is remembered. Job Fair's matching runtime and startup assets must already be installed in that copy of FFV.

Choose **Mixed Crystals**, **Remixed Crystals**, **Mixed-Fiesta** or **Remixed-Fiesta**, enter a seed, and press **Apply for next New Game** while FFV is closed. Then launch FFV through Steam yourself. Applying never starts the game or edits a save.

**Mixed Crystals:** follow the story from Wind Shrine with a full party of Freelancers. All 21 jobs are shuffled among the crystals. Jobs and Abilities unlock after the Wind Crystal; story and key-item progression remain normal. The 49 bosses shuffle across worlds within their Simple or Complex tier, with stats scaled to the destination encounter.

**Remixed Crystals:** explore with Bartz, Lenna, Faris and Krile, starting as Freelancers with four distinct jobs unlocked. Find the remaining 17 jobs in treasure, split 6 / 6 / 5 across the worlds. World 1 starts open; two permanent keys unlock Worlds 2 and 3 independently. Use Warp Shard (View + Y or F6 on the overworld) for return trips and transport recall. All 49 bosses shuffle across worlds in one pool, with stats scaled to the destination encounter. Dungeon bosses remain required.

Remixed skips the reviewed early story scenes in Tule, Tycoon, Tycoon Meteor, Pirate Cave/Hideout and Wind Shrine's summit. The Chancellor waits in the treasure room; Karnak shops and Fire-Powered Ship boarding are available without arrest or Cid's jail scenes. The boss encounters at Wind Shrine, Fire-Powered Ship and Walse Tower, the timed castle escape, and Water Crystal events remain required. Later story removal is still in progress.

**Mixed-Fiesta** follows Mixed but unlocks only four distinct random jobs at Wind Shrine. **Remixed-Fiesta** follows Remixed but keeps only its four starter jobs, with no hidden job crystals. Freelancer remains available; multiple characters may use the same job.

Newly learning **Barehanded at Monk job level 2** also teaches **!Kick**, which can be assigned on other jobs and keeps its native battle behavior. Characters who already learned Barehanded receive no automatic or retroactive Kick grant. The owner reports learning and cross-job use working in 0.9.1; save/full-quit/reload remains pending. See `KICK_PLAYTEST.md`.

Bosses retain their attacks and tactics, including forms and companions. Mixed and Mixed-Fiesta use 27 Simple and 22 Complex encounters; Remixed and Remixed-Fiesta allow crossings between those groups. Each run saves its complete boss assignment. Boss base EXP equals the destination's effective HP budget, before native boosts and distribution. Native playtesting remains required.

The first 0.9.3 repair check is **Remixed, seed 2**: **Halicarnassus at Wind Shrine, 250 HP, 1 physical defense, 2 magic defense and 250 base encounter EXP**, before native boosts and distribution. Test normal-form physical attacks and Zeninage, victory, original destination ABP and progression. Follow `BOSS_SHUFFLE_PLAYTEST.md`.

All four modes keep the 15 timed Karnak Castle rewards fixed and pool the other 245 treasures with 35 finite quest/event rewards in their original worlds. The pool includes items, equipment, gil, spells, songs and summons. Original payments, prerequisites and completion flags remain. Regular enemies stay in place; their occupied drop/steal contents shuffle with native rates and quantities preserved. Shops retain world/type pools and normal prices. Job and world-key bonuses are additional rewards and cannot appear in the fixed chests. Brave Blade/Chicken Knife retain their native choice, and the twelve Sealed Weapons stay fixed tablet rewards.

Launcher preferences, seed profiles, receipts and spoilers live under `%LOCALAPPDATA%\JobFair`. **Import profile…** copies an existing profile into that location without replacing a different profile with the same name. Development profiles previously stored in `job_fair_launcher/data/profiles` can be imported this way.

Apply writes the selected game's `BepInEx/config/JobFair.next-run.json`. The launcher checks Steam app `1173810`, supported build `20265087`, and the matching Job Fair 0.9.5 runtime before writing, and checks again before committing the file. An unsupported game or runtime produces an error. Its bundled seed catalogs do not require extracted game files or the original development checkout.

Current rules are automatic. Reopening the launcher or loading a profile keeps its mode and seed and regenerates placements using the current rules. Saving a profile or applying a seed also uses current rules. Old starter-only prototype profiles become Remixed with the same seed. Loading a profile does not write configuration; **Apply for next New Game** prepares the game. In-game saves keep their existing run until you start a New Game.

This is a development playtest. Use `KICK_PLAYTEST.md` for the new learning rule and `FIESTA_REWARDS_PLAYTEST.md` for modes and rewards. Retain the special fights in `BOSS_SHUFFLE_PLAYTEST.md`, and use `REMIXED_PLAYTEST.md` for the exploration route. Include Tule renaming and Equip Bells in regression testing.

See [SHARING.md](SHARING.md) for the standalone Windows build and curated source export. The standalone launcher requires an already-modded game. The full tester release includes the guided installer.

Offline launcher checks use temporary fixtures and hidden Tk windows:

```powershell
& .\ffv_randomizer\.venv\Scripts\python.exe -m unittest discover -s .\job_fair_launcher\tests -v
```
