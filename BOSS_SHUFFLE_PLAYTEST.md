> Historical test route. Use [TESTER_GUIDE.md](TESTER_GUIDE.md) for current 0.9.5 installation and acceptance status.

# Job Fair boss shuffle playtest

Use the current launcher with the matching **0.9.3 runtime**. See the newest
checkpoint for installation status. The owner confirmed 0.9.2 replaced Wing
Raptor with Halicarnassus, but ordinary physical attacks and Zeninage did zero
damage in both frog and normal forms. Version 0.9.3 repairs defense scaling;
native damage, EXP and broader combat/progression acceptance remain pending.
Launch FFV through Steam yourself.

## Rules

- Mixed: all 49 bosses shuffle across worlds within the approved 27 Simple and
  22 Complex groups. Remixed: all 49 share one pool.
- Both modes scale the assigned boss's health, offensive stats, damage multiplier,
  defenses and speed to the destination. Attacks and encounter mechanics remain
  associated with the assigned boss.
- Physical and magic defense use the square root of the destination/source HP
  budget ratio, with exact integer rounding. A destination's temporary armored
  phase is no longer copied onto an unrelated boss. Native zeroes and immunity
  values remain; ordinary armor cannot become a new 255 immunity sentinel.
- Victory progression, gil and ABP remain at the destination. New 0.9.0 runs
  award EXP from the destination's effective HP budget and randomize the three
  summon-item rewards in the expanded event pool. Ordinary boss drops and steals
  follow the assigned boss. Older 0.8.0 saves retain their original reward rules.
- Repeated native Gargoyle gates use the same assignment. Transformations,
  companions and internal phases belong to their assigned encounter.
- Existing saves retain their embedded configuration. A launcher profile uses
  current rules for the next New Game; applying it does not change a loaded save.

## First checks

Use fresh disposable slots. Apply with FFV closed, then launch through Steam.
Stop at the first error and retain the BepInEx log.

1. **Remixed, seed 2 — first repair check:** the current request uses
   **`remixed-crystals-rewards-v1`**. The Wind Shrine boss must be
   **Halicarnassus**, with **250 HP, 1 physical defense, 2 magic defense** and
   **250 base encounter EXP** before native EXP boosts and distribution. Check
   normal-form physical damage and Zeninage; the log now reports effective
   physical/magic defenses. Also check actual battle HP, the EXP
   result and original Wind-destination ABP and progression. The summit must
   still skip its crystal cinematic without granting extra jobs. Save, fully
   quit and reload. The prior failure occurred with this current catalog;
   switching away from a legacy catalog is not the repair.
2. **Mixed, seed 2:** the Wind Shrine boss should be **Karlabos**. Check that the
   party remains intact, the fight ends normally, and the Wind Crystal event
   unlocks the saved jobs and menus. Ordinary Wind Shrine chests should still
   give their saved rewards. Save, fully quit and reload.
3. **Remixed, seed 3:** the Wind Shrine boss should be **Magissa and Forza**.
   Forza's appearance follows Magissa's native low-health condition; defeating
   her quickly may skip it. Confirm the Wind summit still skips its crystal
   cinematic and does not grant extra jobs. Save, fully quit and reload.
4. Load one of those slots after applying a different seed. The save must keep
   its original jobs, treasures, shops and boss assignments. The new seed applies
   only to a subsequent New Game.

## Special encounter checks

Use the launcher's spoiler to locate each assigned boss. The repair must retain
the full 49-boss assignment and mechanics rules. The native combat checks below
cannot be established by an offline catalog or compiler test.

- Multi-form and multi-enemy encounters: Liquid Flame, Archeoaevis, Soul Cannon,
  Gargoyles, Wendigo and the Crystals. Check transitions, target selection,
  revival and actual victory.
- Defense patterns: Wing Raptor's folded wings, Siren's forms, Carbuncle's
  vulnerable window, Melusine's changing defense and Twintania's charging form
  should retain their own patterns. Check Titan and Odin at early destinations
  too: their native levels (1 and 2) do not express their normal difficulty.
- Golem: protect him for success; a failed rescue must not clear the destination.
  The three summon-item locations must grant their saved event reward once per
  successful encounter, regardless of the replacement boss. Older 0.8.0 saves
  must still grant their original summon.
- Odin: his own one-minute limit follows him. The boss replacing Odin must not
  inherit that limit. Karnak's escape and the underwater tower's field countdowns
  must still matter when their encounters are shuffled.
- Gilgamesh and Necrophobe: scripted retreats/endings must finish the current
  destination encounter without activating unrelated story progression.
- Fork Tower: keep the native party split and the replacement boss's gameplay
  restrictions, and confirm both destination victory events complete.
- Lose, retry, flee where permitted, then enter another battle. There must be no
  leftover stat scaling, timers, rewards or altered AI in the next encounter.

Also retain the established Tule renaming and Equip Bells checks, including
unequipping, saving, fully quitting and reloading.
