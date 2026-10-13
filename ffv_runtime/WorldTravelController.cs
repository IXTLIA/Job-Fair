// Physical Xbox layout through FFV's installed Unity Input System (including
// Steam's virtual gamepad). No guessed legacy axis names or joystick indices.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectBlues.Runtime
{
    internal static class WorldTravelController
    {
        private static readonly WorldTravelControls controls = new WorldTravelControls();
        private static TravelPad pad;
        private static int sampledFrame = -1, openFrame = -1, neutralFrame = -1;
        private static bool releaseGuard, unavailable;
        internal static bool Neutral { get { Poll(); return pad.Focus && !pad.Any; } }
        internal static bool Confirm { get { Poll(); return controls.Confirm; } }
        internal static bool Cancel { get { Poll(); return controls.Cancel; } }
        internal static int Step { get { Poll(); return controls.Step; } }
        internal static void GuardRelease() { releaseGuard = true; neutralFrame = -1; }
        internal static void Tick() => Poll();
        internal static void ResetInput() { controls.Reset(); sampledFrame = -1; }

        private static void Poll()
        {
            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            pad = new TravelPad(0, Application.isFocused, false, false, false, false, false, false, false);
            if (!unavailable)
            {
                try
                {
                    var p = Gamepad.current;
                    if (p != null && p.added)
                    {
                        var left = p.leftStick.ReadValue(); var right = p.rightStick.ReadValue();
                        bool up = p.dpad.up.isPressed || left.y >= 0.55f || p.leftShoulder.isPressed;
                        bool down = p.dpad.down.isPressed || left.y <= -0.55f || p.rightShoulder.isPressed;
                        bool any = up || down || p.selectButton.isPressed || p.startButton.isPressed
                            || p.buttonNorth.isPressed || p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed
                            || p.leftStickButton.isPressed || p.rightStickButton.isPressed
                            || p.dpad.left.isPressed || p.dpad.right.isPressed
                            || Math.Abs(left.x) > 0.2f || Math.Abs(left.y) > 0.2f || Math.Abs(right.x) > 0.2f || Math.Abs(right.y) > 0.2f
                            || p.leftTrigger.isPressed || p.rightTrigger.isPressed;
                        pad = new TravelPad(p.deviceId, Application.isFocused, p.selectButton.isPressed,
                            p.buttonNorth.isPressed, p.buttonSouth.isPressed, p.buttonEast.isPressed, up, down, any);
                    }
                }
                catch (Exception error)
                {
                    unavailable = true;
                    Plugin.Logger.LogError("Warp Shard controller input unavailable; keyboard/mouse remain available: " + error);
                    SetupView.Notify("Warp Shard controller input unavailable. Use F6 and report the log.");
                }
            }
            controls.Update(pad, Environment.TickCount64);
        }

        // Called by the existing FieldMap prefix, before native player actions.
        // Claim the modifier only on a safe Remixed overworld; all other scenes
        // retain their normal bindings. Keep input owned through button release.
        internal static bool BeforePlayerInput()
        {
            Poll();
            if (!WorldTravelRuntime.IsBusy && pad.Focus && pad.View && WorldTravelRuntime.CanOpenController())
            {
                GuardRelease();
                if (controls.Open && openFrame != Time.frameCount)
                {
                    openFrame = Time.frameCount;
                    if (!WorldTravelRuntime.TryOpen(out string reason)) SetupView.Notify(reason);
                }
            }
            if (!releaseGuard) return true;
            if (WorldTravelRuntime.IsBusy || !Neutral || Input.anyKey || Input.GetMouseButton(0))
            { neutralFrame = -1; return false; }
            if (neutralFrame < 0) { neutralFrame = Time.frameCount; return false; }
            if (neutralFrame == Time.frameCount) return false;
            releaseGuard = false; return true;
        }
    }
}
