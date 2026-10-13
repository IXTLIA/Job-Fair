using System;
using UnityEngine;

namespace ProjectBlues.Runtime
{
    public sealed class SetupView : MonoBehaviour
    {
        private static bool open;
        private static bool inputArmed;
        private static bool renameUnavailable;
        private static bool drawingDisabled;
        private static RenameInput editor;
        private static string notification;
        private string shownNotification;
        private float showUntil;
        private bool firstGui = true;
        internal static bool RenameConfirmed { get; private set; }
        internal static bool RenameCancelled { get; private set; }
        internal static string SelectedName { get; private set; }
        internal static string FatalError { get; private set; }
        internal static bool RenameAvailable => !renameUnavailable && FatalError == null;

        public SetupView(IntPtr pointer) : base(pointer) { }

        internal static void ResetSetup()
        {
            CloseRename();
            FatalError = null;
            notification = null;
            // An unavailable input/render API stays disabled until game restart.
        }

        internal static void OpenRename(string initialName)
        {
            if (!RenameAvailable) throw new InvalidOperationException("Renaming is unavailable in this session.");
            editor = new RenameInput(initialName ?? "Bartz");
            RenameConfirmed = false;
            RenameCancelled = false;
            SelectedName = null;
            inputArmed = false;
            open = true;
            Plugin.Logger.LogInfo("Tule rename window opened; release the talk button, then type. Escape cancels.");
        }

        internal static void CloseRename()
        {
            open = false;
            inputArmed = false;
            RenameConfirmed = false;
            RenameCancelled = false;
            SelectedName = null;
            editor = null;
        }

        internal static void Fail(string message) { FatalError = message; open = false; }
        internal static void Notify(string message) { notification = message; }

        public void Update()
        {
            try
            {
                if (open && FatalError == null) ReadRenameInput();
                RenameSession.Tick();
            }
            catch (Exception exception) { DisableRename(exception); }
        }

        private static void DisableRename(Exception exception)
        {
            if (renameUnavailable) return;
            renameUnavailable = true;
            Plugin.Logger.LogError("Rename interface disabled for this session: " + exception);
            // A display/input failure happens before any name commit. Release only
            // our verified talk-event pause, retaining save guards if release fails.
            RenameSession.AbortUi(exception);
        }

        private static void ReadRenameInput()
        {
            // These native-backed Input calls do not use GUI.TextField/TextEditor.
            // Cancel is deliberately available before arming and outside OnGUI.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
            {
                RenameCancelled = true;
                return;
            }
            if (!inputArmed)
            {
                if (!Input.anyKey) inputArmed = true;
                return; // Never treat the NPC talk press as text or confirmation.
            }
            if (Input.GetKeyDown(KeyCode.Delete)) editor.Clear();
            else if (Input.GetKeyDown(KeyCode.Backspace)) editor.Backspace();
            editor.Insert(Input.inputString);
            bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

            if (Input.GetMouseButtonDown(0))
            {
                float left, top, width, height;
                GetPanel(out left, out top, out width, out height);
                Vector3 mouse = Input.mousePosition;
                float y = Screen.height - mouse.y;
                float buttonWidth = (width - 60) / 2;
                if (Hits(mouse.x, y, left + 24, top + height - 65, buttonWidth, 40))
                {
                    RenameCancelled = true;
                    return;
                }
                confirm |= Hits(mouse.x, y, left + 36 + buttonWidth, top + height - 65, buttonWidth, 40);
            }
            if (confirm)
            {
                string selected;
                if (editor.TryConfirm(out selected))
                {
                    SelectedName = selected;
                    RenameConfirmed = true;
                }
            }
        }

        private static bool Hits(float x, float y, float left, float top, float width, float height)
            => x >= left && x < left + width && y >= top && y < top + height;

        public void OnGUI()
        {
            if (drawingDisabled) return;
            try
            {
                if (firstGui)
                {
                    firstGui = false;
                    Plugin.Logger.LogInfo("Job Fair overlay is receiving OnGUI callbacks.");
                }
                if (!open && FatalError == null && notification == null) return;
                DrawWithRestoredStyle();
            }
            catch (Exception exception)
            {
                drawingDisabled = true; // Avoid an exception flood on every GUI event.
                DisableRename(exception);
            }
        }

        private void DrawWithRestoredStyle()
        {
            // Only already-supported Box/Label drawing is used. Interactive GUI
            // controls can reach stripped Unity methods even when they compile.
            GUIStyle style = GUI.skin.label;
            int size = style.fontSize;
            FontStyle font = style.fontStyle;
            bool wrapping = style.wordWrap;
            int depth = GUI.depth;
            try
            {
                style.fontSize = 18;
                style.fontStyle = FontStyle.Normal;
                style.wordWrap = true;
                GUI.depth = -10000;
                Draw(style);
            }
            finally
            {
                style.fontSize = size;
                style.fontStyle = font;
                style.wordWrap = wrapping;
                GUI.depth = depth;
            }
        }

        private static void GetPanel(out float left, out float top, out float width, out float height)
        {
            width = Math.Min(680f, Screen.width - 24f);
            height = Math.Min(435f, Screen.height - 24f);
            left = (Screen.width - width) / 2f;
            top = (Screen.height - height) / 2f;
        }

        private void Draw(GUIStyle style)
        {
            if (notification != shownNotification)
            {
                shownNotification = notification;
                showUntil = Time.realtimeSinceStartup + 8f;
            }
            if (!open && FatalError == null)
            {
                if (Time.realtimeSinceStartup < showUntil)
                {
                    GUI.Box(new Rect(15, 15, Math.Min(Screen.width - 30, 610), 46), "");
                    GUI.Label(new Rect(25, 24, Math.Min(Screen.width - 50, 590), 30), notification, style);
                }
                return;
            }
            float left, top, width, height;
            GetPanel(out left, out top, out width, out height);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Box(new Rect(left, top, width, height), "");
            style.fontSize = 26;
            style.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(left + 24, top + 20, width - 48, 40),
                FatalError == null ? "Job Fair — Rename Bartz" : "Job Fair", style);
            style.fontSize = 18;
            style.fontStyle = FontStyle.Normal;
            if (FatalError != null)
            {
                GUI.Label(new Rect(left + 24, top + 85, width - 48, height - 110), FatalError, style);
                return;
            }
            GUI.Label(new Rect(left + 24, top + 80, width - 48, 50),
                "Choose a name for Bartz. Your seed and starting jobs stay the same.", style);
            GUI.Label(new Rect(left + 24, top + 140, width - 48, 27), "New name (up to 6 characters)", style);
            GUI.Box(new Rect(left + 24, top + 174, width - 48, 42), "");
            style.fontSize = 22;
            GUI.Label(new Rect(left + 34, top + 181, width - 68, 31),
                editor.Text.Length == 0 ? "_" : editor.Text, style);
            style.fontSize = 18;
            GUI.Label(new Rect(left + 24, top + 230, width - 48, 55),
                inputArmed ? "Type to replace the name. Backspace edits; Delete clears.\nEnter saves the name. Escape cancels."
                           : "Release the talk button, then type a name. Escape cancels.", style);
            if (editor.Validation.Length > 0)
                GUI.Label(new Rect(left + 24, top + 294, width - 48, 45), editor.Validation, style);
            float buttonWidth = (width - 60) / 2;
            GUI.Box(new Rect(left + 24, top + height - 65, buttonWidth, 40), "");
            GUI.Label(new Rect(left + 34, top + height - 57, buttonWidth - 20, 27), "Esc: Keep current name", style);
            GUI.Box(new Rect(left + 36 + buttonWidth, top + height - 65, buttonWidth, 40), "");
            GUI.Label(new Rect(left + 46 + buttonWidth, top + height - 57, buttonWidth - 20, 27), "Enter: Rename", style);
        }
    }
}
