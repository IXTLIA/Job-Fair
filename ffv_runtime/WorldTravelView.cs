// Warp Shard field action. Uses the native-backed input and label/box
// APIs already used by the accepted renamer; no stripped GUI button controls.
using System;
using UnityEngine;

namespace ProjectBlues.Runtime
{
    public sealed class WorldTravelView : MonoBehaviour
    {
        private bool armed, disabled, menuSeen;
        private int selected = 1;
        public WorldTravelView(IntPtr pointer) : base(pointer) { }

        // Update may stop completely in the background. Explicit callbacks also
        // disarm the menu when no unfocused frame was sampled.
        public void OnApplicationFocus(bool focused) { armed = false; WorldTravelController.ResetInput(); }
        public void OnApplicationPause(bool paused) { armed = false; WorldTravelController.ResetInput(); }

        private static bool Visible => CampaignContract.UsesWorldTravel(RunSession.Active)
            && RunSession.Loading == null && SetupView.FatalError == null && !RenameSession.IsActive
            && (WorldTravelRuntime.IsMenuOpen || WorldTravelRuntime.IsActionVisible);

        public void Update()
        {
            try
            {
                WorldTravelRuntime.Tick();
                WorldTravelController.Tick(); // Keep edges fresh through battles, menus and focus changes.
                if (disabled || !Visible) { menuSeen = false; return; }
                if (!WorldTravelRuntime.IsMenuOpen)
                {
                    menuSeen = false;
                    if (!Application.isFocused) return;
                    bool request = Input.GetKeyDown(KeyCode.F6);
                    if (Input.GetMouseButtonDown(0)) request |= Hit(ActionRect());
                    if (request)
                    {
                        if (WorldTravelRuntime.TryOpen(out string reason))
                        { armed = false; selected = RunSession.Active.WorldState.ActiveWorld; }
                        else SetupView.Notify(reason);
                    }
                    return;
                }
                if (!menuSeen)
                { menuSeen = true; armed = false; selected = RunSession.Active.WorldState.ActiveWorld; }
                if (!Application.isFocused) { armed = false; return; }
                if (Input.GetKeyDown(KeyCode.Escape) || WorldTravelController.Cancel)
                { WorldTravelRuntime.CancelMenu(); return; }
                if (!armed) { if (!Input.anyKey && !Input.GetMouseButton(0) && WorldTravelController.Neutral) armed = true; return; }
                if (Input.GetKeyDown(KeyCode.UpArrow) || WorldTravelController.Step < 0) selected = selected == 1 ? 3 : selected - 1;
                if (Input.GetKeyDown(KeyCode.DownArrow) || WorldTravelController.Step > 0) selected = selected == 3 ? 1 : selected + 1;
                bool choose = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                    || WorldTravelController.Confirm;
                if (Input.GetKeyDown(KeyCode.Alpha1)) { selected = 1; choose = true; }
                if (Input.GetKeyDown(KeyCode.Alpha2)) { selected = 2; choose = true; }
                if (Input.GetKeyDown(KeyCode.Alpha3)) { selected = 3; choose = true; }
                if (Input.GetMouseButtonDown(0))
                {
                    for (int world = 1; world <= 3; world++)
                        if (Hit(WorldRect(world))) { selected = world; choose = true; }
                    if (Hit(CancelRect())) { WorldTravelRuntime.CancelMenu(); return; }
                }
                if (choose && WorldTravelState.CanEnter(RunSession.Active, selected))
                {
                    try { WorldTravelRuntime.SelectWorld(selected); }
                    catch (InvalidOperationException error)
                    {
                        if (!WorldTravelRuntime.IsMenuOpen) throw;
                        SetupView.Notify(error.Message);
                    }
                }
            }
            catch (Exception error) { Disable(error); }
        }

        private void Disable(Exception error)
        {
            disabled = true;
            Plugin.Logger.LogError("Warp Shard interface stopped: " + error);
            // The runtime owns control and any in-flight transaction. Cancelling
            // only an open menu must never discard a dispatched travel operation.
            try { if (WorldTravelRuntime.IsMenuOpen) WorldTravelRuntime.CancelMenu(); }
            catch (Exception cancel) { Plugin.Logger.LogError(cancel); }
            SetupView.Fail("Warp Shard could not finish safely. Close FFV and report the log.");
        }

        public void OnGUI()
        {
            if (disabled || !Visible) return;
            try
            {
                GUIStyle style = GUI.skin.label;
                int size = style.fontSize, depth = GUI.depth;
                bool wrap = style.wordWrap;
                FontStyle font = style.fontStyle;
                try
                {
                    GUI.depth = -9999; style.fontSize = 18; style.wordWrap = true; style.fontStyle = FontStyle.Normal;
                    if (!WorldTravelRuntime.IsMenuOpen)
                    {
                        var action = ActionRect(); GUI.Box(action, "");
                        GUI.Label(new Rect(action.x + 10, action.y + 7, action.width - 20, 30), "Warp Shard: View + Y / F6", style);
                        return;
                    }
                    var panel = Panel(); GUI.Box(panel, "");
                    style.fontSize = 25;
                    GUI.Label(new Rect(panel.x + 24, panel.y + 20, panel.width - 48, 40), "Warp Shard", style);
                    style.fontSize = 18;
                    GUI.Label(new Rect(panel.x + 24, panel.y + 66, panel.width - 48, 56),
                        "Travel to an unlocked world. Your keys are reusable.", style);
                    var run = RunSession.Active;
                    for (int world = 1; world <= 3; world++)
                    {
                        var row = WorldRect(world); GUI.Box(row, "");
                        string status = world == run.WorldState.ActiveWorld
                            ? (CampaignContract.IsPlayableRemixedCatalog(run.CatalogId) ? " — Return to arrival point" : " — Current world")
                            : WorldTravelState.CanEnter(run, world) ? " — Travel" : " — Key required";
                        GUI.Label(new Rect(row.x + 12, row.y + 10, row.width - 24, 32),
                            (selected == world ? "> " : "  ") + world + ": World " + world + status, style);
                    }
                    var cancel = CancelRect(); GUI.Box(cancel, "");
                    GUI.Label(new Rect(cancel.x + 12, cancel.y + 7, cancel.width - 24, 30), "B / Esc: Cancel", style);
                    style.fontSize = 16;
                    GUI.Label(new Rect(panel.x + 24, panel.y + panel.height - 60, panel.width - 48, 48),
                        armed ? "D-pad / left stick / LB / RB: Choose    A: Travel\nKeyboard: 1–3 or arrows + Enter. Mouse also supported." : "Release buttons and center the stick to choose.", style);
                }
                finally { style.fontSize = size; style.wordWrap = wrap; style.fontStyle = font; GUI.depth = depth; }
            }
            catch (Exception error) { Disable(error); }
        }
        private static Rect ActionRect() => new Rect(Screen.width - 330, 15, 315, 44);
        private static Rect Panel() => new Rect((Screen.width - 560) / 2f, (Screen.height - 430) / 2f, 560, 430);
        private static Rect WorldRect(int world) { var p = Panel(); return new Rect(p.x + 24, p.y + 124 + (world - 1) * 57, p.width - 48, 50); }
        private static Rect CancelRect() { var p = Panel(); return new Rect(p.x + 24, p.y + 307, p.width - 48, 43); }
        private static bool Hit(Rect rect)
        { var mouse = Input.mousePosition; float y = Screen.height - mouse.y; return mouse.x >= rect.x && mouse.x < rect.x + rect.width && y >= rect.y && y < rect.y + rect.height; }
    }
}
