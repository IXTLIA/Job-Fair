> Historical test route. Use [TESTER_GUIDE.md](TESTER_GUIDE.md) for current 0.9.5 installation and acceptance status.

# Warp Shard controller playtest — 0.9.4

Launch FFV through Steam yourself. Use **Remixed Crystals** or **Remixed-Fiesta**
and an Xbox-style controller. An existing Remixed save works; no new seed Apply
is needed. These controls are for Warp Shard; the launcher still uses mouse and
keyboard.

| Action | Xbox-style controller |
| --- | --- |
| Open Warp Shard | Stand still on an overworld; hold **View**, then press **Y** |
| Select world | **D-pad Up/Down**, **left stick Up/Down**, or **LB/RB** |
| Travel / return to arrival point | **A** |
| Cancel | **B** |

View is the small button with two overlapping rectangles. Release the opening
buttons before choosing. Holding a direction repeats after a short delay.
Keyboard **F6**, **1–3 / arrows + Enter**, **Escape**, and mouse clicks still work.

1. Open and cancel while stationary outside Tule. Check the party stays still
   and FFV's normal menu/map does not open behind Warp Shard.
2. Reopen, release the buttons, and try the D-pad, stick and LB/RB. Selection
   wraps across the three worlds. Small stick drift should not move it.
3. Press A on a locked world: the menu should stay open without travelling.
4. Choose World 1's **Return to arrival point**, press A, and keep holding it
   briefly. You should arrive outside Tule with transport, without a stray
   field action. Release the controls; ordinary movement should resume.
5. Open again and press B. Hold B briefly, then release it. Nothing underneath
   the overlay should activate, and field controls should resume after release.
6. If convenient, disconnect/reconnect the controller or switch away from FFV
   while the menu is open. Held buttons on return must not confirm a destination;
   release them before continuing. F6 and Escape remain fallback controls.

World 1 recall is available immediately. After collecting a world key, also try
travelling there and back. Remixed seed 2 has the World 3 key in Tule's former
Ether chest and World 2 key in Wind Shrine 4F's former Staff chest.

The shortcut only claims native field input in a safe Remixed overworld context.
Y alone keeps its native behavior. Steam must expose the controller as a gamepad;
a custom layout that converts every button into keyboard keys uses the keyboard
controls instead. Prompts describe the physical Xbox layout.

Offline checks cover simulated Unity/native input and travel transactions.
**Physical controller/Steam acceptance remains pending.** The 0.9.3 boss defense
repair is retained; its damage/EXP retest is still pending separately. If a
control fails, close FFV normally and preserve `BepInEx/LogOutput.log` for review.
