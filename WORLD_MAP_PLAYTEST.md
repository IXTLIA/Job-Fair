# World-map refresh playtest — 0.9.5

Launch FFV through Steam yourself. Use your existing Remixed save with both
world keys. No new run or seed Apply is needed.

1. Travel to **World 2**, then **World 1**. Open the map: it should show World 1
   rather than retaining World 2. Close and reopen it to confirm.
2. Travel to **World 2**, then **World 3**. Check the map shows the merged world.
3. In **World 3**, dive in the submarine. Check the underwater map. Surface and
   check that the above-water map returns. Repeat the dive/surface cycle once.
4. Travel back to World 1, then World 2. Check the displayed map each time.
5. If you have a save made while the wrong map was visible, load it. Once field
   control resumes on the overworld, the map should correct itself automatically.

If convenient, also check World 2's dive/surface transition, the small overlay
and full map, and that transport/player markers remain in the correct positions.

Valid native map variants for changed terrain or landmarks are preserved. The
repair changes the selected map only when it belongs to another world/layer.
Normal keyboard/controller controls are unchanged. Warp Shard: **View + Y** or
**F6**; D-pad/left stick/LB/RB choose, **A** travels, **B** cancels.

Offline adapter, travel and controller checks pass. **Native visual acceptance
remains pending.** If it still displays the wrong map, close FFV normally and
preserve `BepInEx/LogOutput.log`; successful corrections log `World map refreshed`.
