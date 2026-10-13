// Original Job Fair runtime integration for the source-pinned startup trial.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Last.Interpreter;
using Last.Management;
using Cysharp.Threading.Tasks;
using FileResult = Il2CppSystems.Files.FileOperationUtility.ResultCode;
using NativeCalls = Last.Interpreter.Instructions.SystemCall.Current;

[assembly: TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = ".NET 6.0")]

namespace ProjectBlues.Runtime
{
    [BepInPlugin("jobfair.runtime", "Job Fair", "0.9.5")]
    [BepInProcess("FINAL FANTASY V.exe")]
    public sealed class Plugin : BasePlugin
    {
        internal static ManualLogSource Logger;
        internal static OpeningScript StartupScript;
        internal static ModeOpening ModeStartup;
        internal static RemixedOpening RemixedStartup;
        public override void Load()
        {
            Logger = Log;
            string startup = Path.Combine(Paths.GameRootPath,
                "FINAL FANTASY V_Data/StreamingAssets/Magicite/ProjectBluesStartupTrial/map_20250/Assets/GameAssets/Serial/Res/Map/Map_20250/Map_20250/sc_e_0001.json");
            const string expected = "6b507e3ff62f9d78b2708bac5f2f74a706de8237ad5ea1fd25c546abbdf75e70";
            if (!File.Exists(startup) || HashFile(startup) != expected)
                throw new InvalidOperationException("Job Fair requires the verified revision 2 startup package.");
            RequireHash("GameAssembly.dll", "355bb0eccd3cf6a2cb70d48f2329379eb98d4b5085971d46985154a882dcfb17");
            RequireHash("FINAL FANTASY V_Data/il2cpp_data/Metadata/global-metadata.dat",
                "65df1ceec31f0f40bc4dc881606708aa65fd9cd4e3aa4237d4d4fcf498cf91e3");
            StartupScript = new OpeningScript(File.ReadAllText(startup));
            ModeStartup = new ModeOpening(File.ReadAllText(startup));
            RemixedStartup = new RemixedOpening(File.ReadAllText(startup));
            CampaignRuntime.Initialize();
            ShopRuntime.Initialize();
            BossRuntime.Initialize();
            EnemyLootRuntime.Initialize();
            AddComponent<SetupView>();
            AddComponent<WorldTravelView>();
            var harmony = new Harmony("jobfair.runtime");
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
                int patched = 0;
                foreach (MethodBase method in harmony.GetPatchedMethods())
                {
                    Log.LogInfo("Patched " + method.DeclaringType.Name + "." + method.Name);
                    patched++;
                }
                if (patched != 51) throw new InvalidOperationException("Expected 51 Job Fair hook targets, found " + patched);
            }
            catch
            {
                harmony.UnpatchSelf();
                SetupView.Fail("Job Fair could not initialize. Please close FFV and report the error.");
                throw;
            }
            Log.LogInfo("Launcher seed, Tule renamer, embedded metadata, Equip Bells and Kick learning hooks installed. Algorithm: " + SeedLogic.AlgorithmVersion);
            SetupView.Notify("Job Fair 0.9.5 — world map refresh repair");
        }

        private static void RequireHash(string relative, string expected)
        {
            string path = Path.Combine(Paths.GameRootPath, relative);
            if (!File.Exists(path) || HashFile(path) != expected)
                throw new InvalidOperationException("Job Fair requires verified FFV build 20265087: " + relative);
        }

        private static string HashFile(string path)
        {
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path))
                return Convert.ToHexString(hash.ComputeHash(input)).ToLowerInvariant();
        }
    }

    internal static class RunSession
    {
        internal static IntPtr OpeningCore;
        internal static RunMetadata Active;
        internal static RunMetadata Pending;
        internal static int GrantIndex;
        internal static bool SetupApplied;
        internal static LoadBridge Loading;
        internal static bool LoadFailed;
        internal static bool CanSave => EquipBellsData.Ready && !EquipBellsData.Failed
            && KickData.Ready && !KickData.Failed
            && Loading == null && !LoadFailed && !RenameSession.IsActive
            && SetupView.FatalError == null && !RemixedJobRuntime.IsBusy && !WorldTravelRuntime.IsBusy
            && (OpeningCore == IntPtr.Zero || Active != null);
        private static readonly string[] FixedMethods = {
            "ReleaseJobPharmacist", "ReleaseJobSummoner", "ReleaseJobMime", "ReleaseJobBard"
        };

        internal static void BeginOpening(IntPtr core, RunMetadata selected = null)
        {
            RemixedJobRuntime.RequireIdleForLoad();
            WorldTravelRuntime.RequireIdleForLoad();
            try { EquipBellsData.RequireReady(); }
            catch (Exception exception) { EquipBellsData.Fail(exception); return; }
            if (Loading != null)
            {
                SetupView.Fail("A save is still loading. Please close FFV and report the error.");
                return;
            }
            OpeningCore = core;
            BossRuntime.Reset();
            EnemyLootExperience.Reset();
            FiestaRuntime.Reset();
            Active = null;
            Pending = selected;
            GrantIndex = 0;
            SetupApplied = selected != null;
            LoadFailed = false;
            RenameSession.Reset();
            if (RenameSession.IsActive) return;
            SetupView.ResetSetup();
            Plugin.Logger.LogInfo("Verified startup began; mode " + (selected == null ? "legacy pending" : selected.Mode));
        }

        internal static bool MatchesOpeningProgram(Core core)
            => MatchesProgram(core, Plugin.StartupScript)
                || MatchesProgram(core, Plugin.ModeStartup.MixedScript)
                || MatchesProgram(core, Plugin.RemixedStartup.Script);

        internal static bool MatchesProgram(Core core, OpeningScript expected)
        {
            if (core == null || expected == null) return false;
            var program = core.mnemonics;
            if (program == null || program.Length != expected.InstructionCount) return false;
            for (int index = 0; index < program.Length; index++)
            {
                var instruction = program[index];
                if (instruction == null || instruction.operands == null) return false;
                var operands = instruction.operands;
                if (operands.iValues == null || operands.rValues == null || operands.sValues == null) return false;
                var integers = new int[operands.iValues.Length];
                var reals = new double[operands.rValues.Length];
                var strings = new string[operands.sValues.Length];
                for (int slot = 0; slot < integers.Length; slot++) integers[slot] = operands.iValues[slot];
                for (int slot = 0; slot < reals.Length; slot++) reals[slot] = operands.rValues[slot];
                for (int slot = 0; slot < strings.Length; slot++) strings[slot] = operands.sValues[slot];
                if (!expected.MatchesInstruction(index, instruction.label, instruction.mnemonic,
                    instruction.type, integers, reals, strings)) return false;
            }
            return true;
        }

        internal static RunMetadata ReadLauncherRun()
        {
            string path = Path.Combine(Paths.ConfigPath, "JobFair.next-run.json");
            if (!File.Exists(path) || new FileInfo(path).Length > NextRunConfig.MaximumLength)
                throw new InvalidOperationException("Apply a seed with the Job Fair launcher before starting New Game.");
            RunMetadata selected = NextRunConfig.Parse(File.ReadAllText(path)).CreateRun("Bartz");
            CampaignRuntime.ValidateRun(selected);
            Plugin.Logger.LogInfo("Launcher run selected: seed " + selected.Seed + ", mode " + selected.Mode + ", run " + selected.RunId);
            return selected;
        }

        private static bool ReadLauncherSetup()
        {
            try
            {
                Pending = ReadLauncherRun();
                // Campaign startup must have been selected before native compilation.
                if (Pending.SchemaVersion != 1)
                    throw new InvalidOperationException("Campaign opening was not transformed before execution.");
                SetupApplied = true;
                Plugin.Logger.LogInfo("Launcher run selected: seed " + Pending.Seed + ", run " + Pending.RunId);
                return true;
            }
            catch (Exception exception)
            {
                SetupView.Fail("Close FFV, open the Job Fair launcher, and apply a seed for your next New Game. Then launch FFV through Steam again.");
                Plugin.Logger.LogError(exception);
            }
            return false;
        }

        internal static bool GrantPrefix(MainCore core, MethodBase method, ref int result)
        {
            // Seed setup uses this confirmed native hook, independent of naming.
            if (OpeningCore == IntPtr.Zero && MatchesOpeningProgram(core))
                BeginOpening(core.Pointer);
            if (core == null || core.Pointer != OpeningCore) return true;
            if (core != null && core.Pointer == OpeningCore && SetupView.FatalError != null)
            {
                result = Core.Blank;
                return false;
            }
            if (!SetupApplied)
            {
                if (method.Name != FixedMethods[0])
                {
                    SetupView.Fail("The starting-job sequence was unexpected. Please close FFV and report the error.");
                    result = Core.Blank;
                    return false;
                }
                if (!ReadLauncherSetup()) { result = Core.Blank; return false; }
            }
            result = Core.Blank;
            if (SetupView.FatalError != null) return false;
            if (Pending == null || GrantIndex >= FixedMethods.Length || method.Name != FixedMethods[GrantIndex])
            {
                SetupView.Fail("The startup job sequence did not match this version. Please close FFV and report the error.");
                Plugin.Logger.LogError("Refusing unexpected startup grant: " + method.Name + " at index " + GrantIndex);
                return false;
            }
            try
            {
                int selected = Pending.StartingJobs.Count == 0 ? 1 : Pending.StartingJobs[GrantIndex];
                result = Pending.StartingJobs.Count == 0 ? Core.Next
                    : RemixedJobRuntime.Applies(Pending)
                        ? RemixedJobRuntime.ReleaseStartingJob(Pending, selected)
                        : NativeCalls.ReleaseJobCommon((NativeCalls.JobId)selected);
                if (result != Core.Next) throw new InvalidOperationException("Native job release did not advance.");
                Plugin.Logger.LogInfo(Pending.StartingJobs.Count == 0 ? "Mixed startup marker " + (GrantIndex + 1) + ": Freelancer only"
                    : "Starting job " + (GrantIndex + 1) + ": " + selected);
                GrantIndex++;
                if (GrantIndex == FixedMethods.Length)
                {
                    Active = Pending;
                    Pending = null;
                    OpeningCore = IntPtr.Zero;
                    SetupView.Notify("Seed " + Active.Seed);
                    Plugin.Logger.LogInfo("Seeded startup complete; future saves will carry run " + Active.RunId);
                }
            }
            catch (Exception exception)
            {
                result = Core.Blank;
                SetupView.Fail("The starting jobs could not be applied. Please close FFV and report the error.");
                Plugin.Logger.LogError(exception);
            }
            return false;
        }

        internal static void AdoptLoaded(RunMetadata metadata)
        {
            RemixedJobRuntime.RequireIdleForLoad();
            WorldTravelRuntime.RequireIdleForLoad();
            CampaignRuntime.ValidateRun(metadata);
            BossRuntime.Reset();
            EnemyLootExperience.Reset();
            FiestaRuntime.Reset();
            RenameSession.Reset();
            if (RenameSession.IsActive) throw new InvalidOperationException("The prior rename event could not be released.");
            Active = metadata;
            Pending = null;
            OpeningCore = IntPtr.Zero;
            SetupApplied = false;
            GrantIndex = 0;
            SetupView.ResetSetup();
            if (metadata == null)
                Plugin.Logger.LogInfo("Loaded save has no Job Fair seed metadata; no run seed adopted.");
            else
            {
                Plugin.Logger.LogInfo("Restored embedded seed " + metadata.Seed + ", run " + metadata.RunId);
                SetupView.Notify("Loaded seed " + metadata.Seed);
            }
        }

        internal static void RenameRecorded(string name)
        {
            if (Active != null) Active = Active.WithBartzName(name);
        }

        internal static LoadBridge StageLoad(RunMetadata metadata)
        {
            RemixedJobRuntime.RequireIdleForLoad();
            WorldTravelRuntime.RequireIdleForLoad();
            if (Loading != null) throw new InvalidOperationException("Overlapping game-data loads are not supported.");
            BossRuntime.Reset();
            EnemyLootExperience.Reset();
            FiestaRuntime.Reset();
            Loading = new LoadBridge(metadata);
            return Loading;
        }

        internal static void CompleteLoad(LoadBridge bridge)
        {
            if (Loading != bridge) throw new InvalidOperationException("The active load changed before completion.");
            EquipBellsLearning.ReconcileLoaded();
            FiestaRuntime.ValidateLoaded(bridge.Metadata);
            Loading = null;
            LoadFailed = false;
            AdoptLoaded(bridge.Metadata);
        }

        internal static void FailLoad(string message)
        {
            BossRuntime.Reset();
            EnemyLootExperience.Reset();
            FiestaRuntime.Reset();
            Loading = null;
            Active = null;
            Pending = null;
            LoadFailed = true;
            SetupView.Fail(message);
        }
    }

    [HarmonyPatch(typeof(Integrator), "Execute", new Type[] { typeof(string), typeof(ICallbacks), typeof(bool) })]
    internal static class OpeningPatch
    {
        private static bool Prefix(ref string __0, out RunMetadata __state, ref bool __result)
        {
            __state = null;
            try
            {
                if (Plugin.StartupScript.Matches(__0))
                {
                    __state = RunSession.ReadLauncherRun();
                    string selected = OpeningSelection.Select(__state, Plugin.ModeStartup, Plugin.RemixedStartup);
                    if (selected != null) __0 = selected;
                }
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(exception);
                SetupView.Fail("Job Fair could not prepare this event. Close FFV and report the log.");
                __result = false;
                return false;
            }
        }

        private static void Postfix(Integrator __instance, string __0, bool __result, RunMetadata __state)
        {
            if (!__result || __instance.mainCore == null) return;
            try
            {
                // Execute(null) loads a queued program. Its commands have not
                // dispatched yet; replace an isolated copy after exact matching.
                if (__state == null && RunSession.MatchesProgram(__instance.mainCore, Plugin.StartupScript))
                {
                    __state = RunSession.ReadLauncherRun();
                    string selected = OpeningSelection.Select(__state, Plugin.ModeStartup, Plugin.RemixedStartup);
                    if (selected != null)
                        NativeProgram.ReplaceBeforeDispatch(__instance, Plugin.StartupScript, selected);
                }
                bool opening = __state != null || RunSession.MatchesOpeningProgram(__instance.mainCore);
                RuntimeDiagnostics.Execute(__instance, __result, opening);
                if (opening)
                {
                    RunSession.BeginOpening(__instance.mainCore.Pointer, __state);
                    if (RunSession.OpeningCore != __instance.mainCore.Pointer || SetupView.FatalError != null)
                        throw new InvalidOperationException("Opening initialization did not complete.");
                }
                else if (RunSession.MatchesProgram(__instance.mainCore, TuleNpcDefinition.Script))
                    RenameSession.TryBegin(__instance);
                else CampaignRuntime.PrepareQueuedReward(__instance, __0 == null);
            }
            catch (Exception exception)
            {
                // Never let a failed transformation proceed with vanilla rewards.
                Plugin.Logger.LogError(exception);
                SetupView.Fail("Job Fair stopped an event whose setup did not match. Close FFV and report the log.");
                try
                {
                    __instance.PauseScript();
                    if (!__instance.isPauseScript) throw new InvalidOperationException("Failed to pause mismatched native event.");
                }
                catch (Exception pauseError) { Plugin.Logger.LogError(pauseError); }
            }
        }
    }

    [HarmonyPatch(typeof(Integrator), nameof(Integrator.ChangeScript), new Type[] { typeof(string), typeof(bool) })]
    internal static class NestedStoryPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (SetupView.FatalError == null) return true;
            __result = false;
            return false;
        }

        private static void Postfix(Integrator __instance, string __0, bool __result)
        {
            if (!__result) return;
            try { CampaignRuntime.PrepareChangedScript(__instance, __0, __result); }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(exception);
                SetupView.Fail("Job Fair stopped a story event whose setup did not match. Close FFV and report the log.");
                try
                {
                    __instance.PauseScript();
                    if (!__instance.isPauseScript) throw new InvalidOperationException("Failed to pause mismatched nested story event.");
                }
                catch (Exception pauseError) { Plugin.Logger.LogError(pauseError); }
            }
        }
    }

    internal static class RuntimeDiagnostics
    {
        private static int executions;
        internal static void Execute(Integrator interpreter, bool success, bool opening)
        {
            if (++executions > 8 && !opening) return;
            Plugin.Logger.LogInfo("Interpreter Execute: success=" + success + ", opening=" + opening
                + ", scriptName=" + interpreter.scriptName + ", map="
                + (DataStorage.Instance == null ? -1 : DataStorage.Instance.currentMapId));
        }
    }

    [HarmonyPatch]
    internal static class StartingJobsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "ReleaseJobPharmacist", "ReleaseJobSummoner", "ReleaseJobMime", "ReleaseJobBard" })
                yield return AccessTools.Method(typeof(NativeCalls), name, new[] { typeof(MainCore) })
                    ?? throw new MissingMethodException(name);
        }
        private static bool Prefix(MainCore __0, MethodBase __originalMethod, ref int __result)
            => RunSession.GrantPrefix(__0, __originalMethod, ref __result);
    }

    [HarmonyPatch(typeof(UserDataManager), "ToJSON", new Type[] { typeof(bool) })]
    internal static class SaveSeedPatch
    {
        private static void Postfix(ref string __result)
        {
            if (RunSession.Active == null || !RunSession.CanSave) return;
            __result = SaveSeedCodec.Attach(__result, RunSession.Active);
            Plugin.Logger.LogInfo("Embedded seed " + RunSession.Active.Seed + " in save data, run " + RunSession.Active.RunId);
        }
    }

    [HarmonyPatch(typeof(UserDataManager), "FromJsonAsync", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
    internal static class LoadSeedPatch
    {
        private static bool Prefix(ref string __0, bool __2, ref UniTask __result, out LoadBridge __state)
        {
            __state = null;
            try
            {
                if (!__2) EquipBellsData.RequireReady();
                RunMetadata metadata;
                string nativeJson = SaveSeedCodec.Extract(__0, out metadata);
                CampaignRuntime.ValidateRun(metadata);
                __0 = nativeJson;
                if (!__2) __state = RunSession.StageLoad(metadata); // library-only import must not switch runs
                return true;
            }
            catch (Exception exception)
            {
                const string message = "This save's randomizer data is unsupported or damaged. It was not loaded. Please close FFV and report the error.";
                Plugin.Logger.LogError(exception);
                if (!__2) RunSession.FailLoad(message);
                else SetupView.Fail(message);
                __result = UniTask.FromException(new Il2CppSystem.Exception(message));
                return false;
            }
        }

        private static void Postfix(ref UniTask __result, LoadBridge __state)
        {
            if (__state == null) return;
            UniTask original = __result;
            __result = __state.Proxy;
            __state.Start(original);
        }
    }

    [HarmonyPatch(typeof(SaveSlotManager), "Save", new Type[] { typeof(int), typeof(Il2CppSystem.Action<bool>), typeof(bool) })]
    internal static class SaveGuardPatch
    {
        private static bool Prefix(Il2CppSystem.Action<bool> __1, ref FileResult __result)
        {
            if (RunSession.CanSave) return true;
            Plugin.Logger.LogWarning("Save blocked while startup/load is incomplete or failed.");
            __result = FileResult.Unknown;
            if (__1 != null) __1.Invoke(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveSlotManager), "Save", new Type[] { typeof(int), typeof(SaveSlotData) })]
    internal static class SaveCopyGuardPatch
    {
        private static bool Prefix(ref FileResult __result)
        {
            if (RunSession.CanSave) return true;
            Plugin.Logger.LogWarning("Save copy blocked while startup/load is incomplete or failed.");
            __result = FileResult.Unknown;
            return false;
        }
    }
}
