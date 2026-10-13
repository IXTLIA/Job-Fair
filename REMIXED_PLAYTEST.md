> Historical test route. Use [TESTER_GUIDE.md](TESTER_GUIDE.md) for current 0.9.5 installation and acceptance status.

# Job Fair 0.8.0 — Remixed test guide

New runs prepared with the current launcher also shuffle all 49 bosses with
destination-scaled stats. Mixed uses Simple/Complex pools; Remixed uses one
pool. Start with the separate **BOSS_SHUFFLE_PLAYTEST.md** checks. Existing saves
keep their saved configuration and do not gain boss randomization automatically.
The boss runtime has passed offline checks; native combat acceptance is pending.

The story skips work with your existing disposable Remixed save. **Fixed Karnak
treasures apply only to new runs in either mode**; existing saves retain their
saved rewards. Both world keys and return travel worked in the owner's 0.7.0
test. These story fixes and full campaign completion still need native testing.

## Wind and Karnak test

1. In Remixed, defeat the boss at Wind Shrine and enter the summit. The crystal
   collection and associated scenes should be skipped, with no extra jobs granted.
   Check the stairs, optional exit warp, re-entry and save/quit/reload.
2. Enter Karnak and use its magic, weapon and armor shops. No arrest should occur.
   Visit the castle/jail if desired; the route no longer needs the Cid scenes.
   The lower fire barrier must still block pre-boss access to the crystal room.
3. Board the Fire-Powered Ship from the dock without freeing Cid. Traverse its
   puzzles and defeat the ship's assigned boss. Continue through the retained native crystal
   sequence into the ten-minute castle escape.
4. Check a gil chest and a guarded chest, then complete the escape encounter.
   The timer, escape and castle destruction remain required. Save/quit/reload
   and revisit Karnak to check party, progress, shops and ruins.

For a **fresh run prepared with current rules**, all 15 castle chests retain
their original rewards, including their battles and claims. They never hold
hidden job crystals or world keys. The other 245 treasures still shuffle by
world: **91 / 54 / 100**. Town and Fire-Powered Ship treasures remain randomized.
Older saves retain their original 260-reward shuffle.

Water Crystal/Walse Tower scenes and the tower sinking remain required. A new
boss-enabled run replaces Garula's fight while retaining its victory event.

## Existing-save story repair test

1. Launch FFV through Steam and load the existing Remixed test slot.
2. In World 3, enter Tule and leave by its south exit. The full party should
   stay together, and the exit should return to World 3. Repeat in World 1.
   If the old save already had Faris away, leaving Tule restores her and the
   Pendant once, without the reunion scene. This uses the original native
   rejoin behavior, including its healing/equipment handling.
3. Try Tule's renamer, shops, and piano. Visit Zok: speaking to him gives the
   Canal Key once without an overnight scene; repeat visits must not give it again.
4. In World 1, enter Tycoon Castle and reach the treasure room. The Chancellor
   should already be there unless his gift was claimed earlier. His gift,
   treasure claims and wall-switch puzzle still work normally. Revisit after
   taking the gift to check it does not return.
5. Revisit Tycoon Meteor and Pirate Cave/Hideout in World 1. Opening rescues,
   Boko introductions, pirate capture and ship tutorials should not play. Check
   the Meteor chest, cave chests, healing spring and manual door/treasury switches.
   Check the cave's north exit in World 3 returns to World 3.
6. Save, fully quit, relaunch and reload. Confirm party, jobs, keys, current
   world and previously claimed treasures persist.

Remixed now skips the reviewed Tule pirate/pub/shop/Zok scenes and Tycoon
entrance/rest/banquet/king-memory scenes, and reviewed pre-Wind Meteor/pirate
scenes. Ordinary NPC dialogue, services,
puzzles and required bosses remain. Other story chains are still being adapted;
this release does not claim to remove every FFV cutscene. Mixed keeps its story.

## Start the test

1. Close FFV. Close and reopen your usual Job Fair launcher so it loads the update.
   Select the FFV folder containing `FINAL FANTASY V.exe` with **Browse…** if it
   was not detected. The launcher remembers it under `%LOCALAPPDATA%\JobFair`.
2. Select **Remixed Crystals**, enter seed **2**, then **Apply for next New Game**.
   Current rules, including fixed Karnak chests, are automatic. Loading a saved
   profile keeps its mode and seed and regenerates placements with current rules.
3. Launch FFV yourself through Steam and select **New Game**.
4. Expect **Bartz, Lenna, Faris and Krile**, all Freelancers, at Wind Shrine.
   Jobs and Abilities should already work.
   The available jobs are Freelancer, **Ninja, Red Mage, Samurai and Monk**.
5. Exit the shrine. An airship should be parked immediately outside.
6. Confirm Wing Raptor still requires a fight. After saving, fully quitting and
   reloading, Krile, all four characters' jobs and their equipment should persist.

Krile is used only in new Remixed runs. Existing saves keep their current party,
including Galuf. Retained later story scenes may still display Galuf as a story
actor; changing the starting party does not complete their story flags.

Apply the selection before starting the new test. Opening the launcher or loading
a profile alone does not write game configuration. Loading an older in-game save
continues that save's own run.

## Warp Shard and independent keys

Version 0.9.5 repairs stale world-map overlays after travel and submarine
surfacing. See `WORLD_MAP_PLAYTEST.md` for the focused return-path test.

While standing still on an overworld, hold **View** and press **Y** on an
Xbox-style controller, or press **F6** / click **Warp Shard**. Release the opening
buttons. Choose with **D-pad / left stick / LB / RB**, **A** to travel, **B** to
cancel. Keyboard 1–3, arrows and Enter, mouse, and Escape remain available.
See `CONTROLLER_PLAYTEST.md` for the focused 0.9.4 controller check. The current world
also offers **Return to arrival point**, which recalls its transport.

For a focused test of World 3 unlocking independently:

1. Before collecting all Wind Shrine chests, visit Tule. You can use World 1's
   arrival-point option immediately to arrive outside Tule.
2. Open Tule's chest that normally contains an **Ether** (`native_treasure_005`).
   Seed 2 adds the **World 3 key** to its ordinary randomized reward.
3. Return outside. World 3 should be available even while World 2 remains locked.
   Visit World 3, then return freely to World 1.
4. Complete the Wind Shrine chest circuit. The chest that normally contains a
   **Staff**, on 4F (`native_treasure_062`), adds the **World 2 key**.
5. Visit World 2, then return to Worlds 1 and 3. Keys are permanent and never spent.

Arrival points are outside Tule in Worlds 1 and 3, and outside Surgate in World 2.
Each arrival supplies transport. World 1 starts open; dungeon bosses still gate
their dungeon progress. Warp Shard is an overworld field action in this build.

## Hidden jobs, treasures and shops

All three worlds have hidden job bonuses: **6 / 6 / 5**, with four starting jobs
making all 21 unlockable jobs available. Job/key containers also give their normal
randomized reward. Native crystal events do not add extra jobs in Remixed.
This first build chooses job bonuses from 24 persistent container candidates;
current treasure rules shuffle 245 rewards and keep the 15 Karnak Castle rewards fixed.

For seed 2, the first three Wind Shrine chests (normally Tent, Leather Cap and
Broadsword) add **Chemist, Dragoon and Ranger**, respectively. Tule's three
outside containers add **Knight, Bard and Beastmaster**. Check that each award
happens once, survives re-entry and stays unlocked after save/quit/reload.

Ordinary treasures shuffle across their entire original world's pool, with
quantities preserved. Shops shuffle within each world and shop type, preserving
stock slots and normal prices. Returning World 3 shops have their own inventory.
Previously opened physical containers remain empty across worlds.

The twelve Sealed Weapons retain their tablet requirements and fixed rewards.
Brave Blade and Chicken Knife retain their original either/or choice.

## Save and regression checks

Save in the disposable slot, fully quit, relaunch through Steam, and reload.
Check jobs, keys, current world, chest claims, transport and shop stock. After
changing the launcher's seed, reload the same slot and verify that nothing rerolls.
An existing Mixed/legacy save should retain its own progression and configuration.
Tule renaming and Equip Bells remain available.

Stop at the first mismatch and report the location, action, seed and result.
The runtime log is `BepInEx/LogOutput.log` inside your selected FFV installation.
If the game reports an incomplete transition, close it and retain that log.

## Remaining acceptance

Native arrival/boarding/control, purchase transactions, reward notifications and
save round trips need your test. Broader dungeon/story interactions and an entire
end-to-end campaign remain unverified. World 2 Exdeath retains his battle and
returns to World 2 after victory; this test build does not yet prevent a rematch.
