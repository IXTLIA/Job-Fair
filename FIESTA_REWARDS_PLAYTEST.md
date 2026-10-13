> Historical test route. Use [TESTER_GUIDE.md](TESTER_GUIDE.md) for current 0.9.5 installation and acceptance status.

# Job Fair 0.9.2 Fiesta and rewards playtest

Use a matching 0.9.2 runtime and launcher after a separately authorized
installation, and fresh disposable save slots. The 0.9.2 boss repair remains a
candidate; 0.9.1 is installed.
Apply with FFV closed, then launch through Steam yourself. Existing saves keep
their embedded rules; selecting a new mode in the launcher does not convert them.
Offline verification does not establish native gameplay acceptance.
Begin boss repair acceptance with Remixed seed 2 in `BOSS_SHUFFLE_PLAYTEST.md`:
Halicarnassus at Wind, 250 HP and 250 base encounter EXP before native boosts
and distribution, with the destination's original ABP and progression retained.

## Rules

- **Mixed-Fiesta:** begin with Freelancer. Wind Shrine unlocks four distinct
  random jobs. Later crystals unlock no additional jobs.
- **Remixed-Fiesta:** begin with four distinct random jobs plus Freelancer.
  No hidden job crystals exist. The two independent world keys remain available.
- Multiple characters may use the same available job. Normal Mixed and Remixed
  retain their 21-job rules.
- All four modes pool 35 finite event rewards with the 260 ordinary rewards by
  original world. The 15 timed Karnak rewards remain fixed. Items, equipment,
  gil, spells, songs and summons can appear in the eligible pool.
- Native payments, prerequisites and completion flags still apply. Sealed
  Weapons, Brave Blade/Chicken Knife, progression keys and repeatable rewards
  retain their existing rules.
- Bosses award base EXP equal to the destination encounter's effective HP
  budget. Native EXP boosts and distribution still apply. Ordinary enemies stay
  in place; their 555 occupied drop/steal slots shuffle contents, preserving
  native chances and quantities. Boss loot otherwise follows existing rules.

## First route

1. Start **Mixed-Fiesta**. Verify Freelancer is the only opening job. Defeat the
   Wind Shrine boss and check that the announced four jobs match the launcher's
   preview. Verify that all party members can choose any of those jobs.
2. Save, quit fully and reload. Check jobs and the seed display. Continue through
   the next crystal event: there should be no new jobs or false job announcement.
3. Start **Remixed-Fiesta** in another slot. Verify its four starter jobs and
   Freelancer. Open several eligible chests and confirm ordinary rewards, with
   no job bonuses. Find and use a world key, then return to World 1.
4. Load each slot after applying a different seed. Its original jobs, rewards,
   loot and boss assignments must remain intact. Also load an older 0.8.0 slot;
   it must keep its older reward and EXP rules.

## Rewards and battles

- Check a chest and a finite NPC gift against the saved spoiler. Check both the
  message and actual inventory, gil or learned-magic entry.
- Exercise a spell/song event and a summon event. Return after completion and
  reload nearby: the native one-time reward must not be awarded twice.
- Check Kelb's three meals separately. Each original eight-Potion reward becomes
  one saved reward. The normal paid inn should still work after the meals end.
- Check the Holy/Flare sequence, Crescent song events, and the three boss summon
  reward locations. Messages and inventory must agree; destination completion
  must occur even if the reward is no longer the original summon.
- With encounters disabled, win early and later bosses. Record displayed EXP
  and actual level progress, accounting for native boost settings. A loss, retry
  or escape must not award EXP or leak that boss's EXP into an ordinary fight.
- Steal from and defeat a regular enemy repeatedly. Compare obtained contents
  to its saved loot slots. Reload another seeded slot and check its own table.
  Failed steals, empty slots and native quantities should remain normal.

Retain the special-fight checks in `BOSS_SHUFFLE_PLAYTEST.md`, the exploration
checks in `REMIXED_PLAYTEST.md`, Tule renaming and Equip Bells. Stop at the first
mismatch and retain the BepInEx log, mode, seed, location and save-slot details.
Dungeon keys, the shuffled four-Tablet goal, hints, weapon customization and
Advance jobs are later milestones.
