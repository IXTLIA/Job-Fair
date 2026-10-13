> Historical test route. Use [TESTER_GUIDE.md](TESTER_GUIDE.md) for current 0.9.5 installation and acceptance status.

# Job Fair 0.9.2 Kick regression playtest

Use a matching 0.9.2 runtime and launcher after a separately authorized
installation. 0.9.1 remains installed while the 0.9.2 boss repair is a candidate.
Launch FFV through Steam yourself and use disposable save slots. Offline checks
are not gameplay acceptance.

The owner reports that **learning Kick and using it on other jobs work in
0.9.1**. Save/full-quit/reload persistence remains pending. Recheck the learning
and cross-job behavior after the boss repair and complete the pending checks.

**Rule:** a character who newly learns **Barehanded at Monk job level 2** also
learns **!Kick**. The learned command can be assigned on other jobs. Kick keeps
its native battle behavior. Existing characters who already learned Barehanded
receive no automatic or retroactive Kick grant.

1. Use a fresh character below Monk job level 2 with Monk available. Check that
   !Kick is not yet in that character's learned commands for other jobs. Monk's
   innate Kick should still work normally.
2. Earn the ABP that teaches Barehanded at Monk job level 2. Check the learning
   messages and ability list: **both Barehanded and !Kick** must be learned.
   Repeat with another eligible character to check character-specific progress.
3. Change to another job, assign !Kick in an ability slot and use it in battle.
   Confirm the native Kick action, targeting, animation and damage rules. Remove
   and reassign the command; it should remain learned and selectable.
4. Save, quit FFV fully and reload. Confirm the learned commands, equipped !Kick
   and battle use persist. Another battle or reload must not announce or grant
   Kick again.
5. Separately load an existing disposable save whose character already has
   Barehanded but has never learned !Kick. Check after load, a normal battle and
   another save/full-quit/reload: **no automatic Kick grant**. Do not edit job
   progress or saves to force the condition.
6. Recheck **Equip Bells**: its learning rule, selection on another job, native
   bell equipment permission, removal and save/full-quit/reload persistence must
   retain the accepted behavior.

Stop at the first mismatch. Retain the BepInEx log, mode, seed, character,
Monk level/ABP before and after, learned-ability list and save-slot details.
Keep the broader checks in `FIESTA_REWARDS_PLAYTEST.md`,
`BOSS_SHUFFLE_PLAYTEST.md` and `REMIXED_PLAYTEST.md` separate from Kick acceptance.
