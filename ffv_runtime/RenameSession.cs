// Original rename overlay session for the verified Tule talk event.
using System;
using Last.Interpreter;
using Last.Management;
using NativeCalls = Last.Interpreter.Instructions.SystemCall.Current;

namespace ProjectBlues.Runtime
{
    internal static class RenameSession
    {
        private static Integrator interpreter;
        private static Core core;
        private static IntPtr interpreterPointer;
        private static IntPtr corePointer;
        private static IntPtr programPointer;
        private static bool active;
        private static bool ownsPause;
        private static bool failed;
        private static bool uiAbortReported;

        internal static bool IsActive => active;

        // The caller has matched the full verified NPC program after Execute.
        internal static bool TryBegin(Integrator candidate)
        {
            if (candidate == null || !SetupView.RenameAvailable) return false;
            try
            {
                Core candidateCore = candidate.mainCore;
                if (candidateCore == null || !candidate.working) return false;
                var candidateProgram = candidateCore.mnemonics;
                if (candidateProgram == null) return false;
                if (active)
                {
                    if (candidate.Pointer == interpreterPointer && candidateCore.Pointer == corePointer)
                        return true;
                    Fail(new InvalidOperationException("A second rename event began before the first finished."));
                    return false;
                }
                // Never take ownership of, or later resume, another pause.
                if (candidate.isPauseScript)
                {
                    Plugin.Logger.LogWarning("Tule rename skipped: the matched event was already paused.");
                    return false;
                }
                interpreter = candidate;
                core = candidateCore; // Keep both native objects rooted while held.
                interpreterPointer = candidate.Pointer;
                corePointer = candidateCore.Pointer;
                programPointer = candidateProgram.Pointer;
                active = true;
                failed = false;
                uiAbortReported = false;
                candidate.PauseScript();
                ownsPause = true;
                if (!candidate.isPauseScript)
                    throw new InvalidOperationException("The native talk event did not pause.");
                SetupView.OpenRename(RunSession.Active?.BartzName ?? "Bartz");
                Plugin.Logger.LogInfo("Verified Tule rename event paused for name selection.");
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        internal static void Tick()
        {
            if (!active || failed) return;
            try
            {
                if (!MatchesHeldContext() || !interpreter.working || !ownsPause || !interpreter.isPauseScript)
                    throw new InvalidOperationException("The held rename event changed before selection finished.");
                if (SetupView.RenameCancelled)
                {
                    ReleasePause();
                    SetupView.CloseRename();
                    Clear();
                    Plugin.Logger.LogInfo("Tule rename cancelled; original talk event resumed.");
                    return;
                }
                if (!SetupView.RenameConfirmed) return;
                string selected = SetupView.SelectedName;
                if (String.IsNullOrWhiteSpace(selected))
                    throw new InvalidOperationException("The confirmed name was empty.");
                RunMetadata.ValidateName(selected);
                new OwnedCharacterClient().UpdateName(NativeCalls.GetCharacterIdFromStatusId(1), selected);
                RunSession.RenameRecorded(selected);
                ReleasePause();
                SetupView.CloseRename();
                Clear();
                SetupView.Notify("Bartz renamed to " + selected);
                Plugin.Logger.LogInfo("Tule rename applied; original talk event resumed.");
            }
            catch (Exception exception) { Fail(exception); }
        }

        // Rendering/input failures must not depend on rendering a Cancel button.
        // This path never applies a pending name or changes run metadata.
        internal static void AbortUi(Exception exception)
        {
            if (!active) return;
            if (!uiAbortReported)
            {
                uiAbortReported = true;
                Plugin.Logger.LogError(exception);
            }
            // Fail already retained ownership and blocked saving. Do not retry
            // native cleanup or emit the same recovery failure every callback.
            if (failed) return;
            try
            {
                // Cancel the pending selection before touching native ownership.
                // CloseRename deliberately preserves any independent FatalError.
                SetupView.CloseRename();
                if (!MatchesHeldContext())
                    throw new InvalidOperationException("Refusing UI recovery of a different rename event.");
                if (ownsPause) ReleasePause();
                Clear();
                Plugin.Logger.LogWarning("Tule rename aborted after a UI error; original talk event resumed without changing the name.");
            }
            catch (Exception recoveryException) { Fail(recoveryException); }
        }

        internal static void Reset()
        {
            try
            {
                // Pause belongs to the integrator, and native core Reset does
                // not clear it. An explicit context reset restores our prior
                // unpaused state even if the integrator now has a new program.
                if (active && ownsPause)
                {
                    if (interpreter == null || interpreter.Pointer != interpreterPointer)
                        throw new InvalidOperationException("The owned rename interpreter changed during reset.");
                    if (interpreter.isPauseScript) interpreter.ResumeScript();
                    if (interpreter.isPauseScript)
                        throw new InvalidOperationException("The owned rename interpreter did not resume during reset.");
                    ownsPause = false;
                }
                Clear();
                SetupView.CloseRename();
            }
            catch (Exception exception)
            {
                // Callers must not clear the error view if IsActive stays true.
                Fail(exception);
            }
        }

        private static bool MatchesHeldContext()
        {
            if (interpreter == null || core == null || interpreter.Pointer != interpreterPointer
                || core.Pointer != corePointer) return false;
            Core current = interpreter.mainCore;
            if (current == null || current.Pointer != corePointer) return false;
            var program = current.mnemonics;
            return program != null && program.Pointer == programPointer;
        }

        private static void ReleasePause()
        {
            if (!ownsPause) return;
            if (!MatchesHeldContext())
                throw new InvalidOperationException("Refusing to resume a different event.");
            if (interpreter.isPauseScript) interpreter.ResumeScript();
            if (interpreter.isPauseScript)
                throw new InvalidOperationException("The native talk event did not resume.");
            ownsPause = false;
        }

        private static void Clear()
        {
            active = false;
            ownsPause = false;
            failed = false;
            uiAbortReported = false;
            interpreter = null;
            core = null;
            interpreterPointer = IntPtr.Zero;
            corePointer = IntPtr.Zero;
            programPointer = IntPtr.Zero;
        }

        private static void Fail(Exception exception)
        {
            active = true; // Keep SaveGuard closed even if startup failed to pause.
            failed = true;
            Plugin.Logger.LogError(exception);
            SetupView.Fail("Bartz's name could not be changed safely. Saving is blocked. Please close FFV and report the error.");
        }
    }
}
