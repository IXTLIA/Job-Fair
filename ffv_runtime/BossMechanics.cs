// Narrow adapters for mechanics stored outside the donor's numeric monster row.
using System;
using System.Text.Json;
using HarmonyLib;
using Last.Battle;
using Last.Battle.Calc;
using Last.Data;
using Last.Interpreter;
using Last.Management;
using Last.Timer;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NativeCalls = Last.Interpreter.Instructions.SystemCall.Current;

namespace ProjectBlues.Runtime
{
    internal static class BossMechanics
    {
        private static object token;
        private static bool golemRescued;
        private static GrobalTimer ownedTimer, previousTimer;
        private static bool replacedTimer, continuePreviousTimer;
        internal static bool GolemRescued => token != null && Object.ReferenceEquals(token, BossRuntime.CurrentToken) && golemRescued;
        internal static int ScaleFixedDamage(CharacterParameterBase user, CharacterParameterBase target, int damage)
        {
            if (!BossRuntime.IsScaledParameter(user) || BossRuntime.IsScaledParameter(target)
                || !BossRuntime.TryCurrent(out var source, out var destination)) return damage;
            return BossCatalog.ScaleFixedDamage(source, destination, damage);
        }
        internal static void End()
        {
            BossRewards.Reset();
            if (replacedTimer)
            {
                var manager = TimerInstanceManager.Instance;
                // Only restore the exact timer suspended by this encounter.
                // FinishTimer may already have removed the owned Odin timer.
                if (manager != null && (manager.GrobalTimer == null || (ownedTimer != null && manager.GrobalTimer.Pointer == ownedTimer.Pointer)))
                    manager.GrobalTimer = previousTimer;
            }
            token = null; golemRescued = false; ownedTimer = null; previousTimer = null;
            replacedTimer = false; continuePreviousTimer = false;
        }
        internal static void Begin()
        {
            if (token != null && Object.ReferenceEquals(token, BossRuntime.CurrentToken)) return;
            End();
            if (!BossRuntime.TryCurrent(out var source, out var destination)) return;
            token = BossRuntime.CurrentToken;
            bool sourceOdin = source.GetProperty("boss_id").GetString() == "odin";
            bool destinationOdin = destination.GetProperty("boss_id").GetString() == "odin";
            if (!sourceOdin && !destinationOdin) return;
            var manager = TimerInstanceManager.Instance;
            if (manager == null) throw new InvalidOperationException("Relocated Odin requires the native timer manager.");
            previousTimer = manager.GrobalTimer;
            // Odin's deadline is part of his fight; it follows the donor. The
            // Karnak escape and underwater tower clocks belong to the location.
            continuePreviousTimer = !destinationOdin;
            replacedTimer = true;
            if (!sourceOdin) { manager.GrobalTimer = null; return; }
            var states = new Il2CppSystem.Collections.Generic.List<SubSceneManagerMainGame.State>();
            states.Add(SubSceneManagerMainGame.State.Battle);
            // Native GetRandomScript handles null as an empty script name but
            // does not bounds-check an empty array before indexing it.
            ownedTimer = new GrobalTimer(new Timer(60), states, null, true);
            manager.GrobalTimer = ownedTimer;
            Plugin.Logger.LogInfo("Relocated Odin: attached native 60-second battle timer.");
        }
        internal static void SawMonster(int monsterId)
        {
            if (token != null && Object.ReferenceEquals(token, BossRuntime.CurrentToken) && monsterId == 230) golemRescued = true;
        }
        internal sealed class TimerScope
        {
            internal Timer Timer;
            internal bool WasPaused;
            internal void Restore() { if (Timer != null) { Timer.isPause = WasPaused; Timer = null; } }
        }
        internal static TimerScope UpdateTimers(SubSceneManagerMainGame.State state)
        {
            if (ownedTimer == null || !Object.ReferenceEquals(token, BossRuntime.CurrentToken)) return null;
            // The destination timer (Karnak/underwater tower) continues while
            // Odin's separate displayed deadline runs, preserving both clocks.
            if (continuePreviousTimer && previousTimer != null && previousTimer.ContainsState(state))
                previousTimer.Timer.Update(UnityEngine.Time.deltaTime);
            if (!ownedTimer.ContainsState(state)) return null;
            // Update our deadline once, then let the native manager update all
            // other timers without dispatching an empty field-event callback.
            ownedTimer.Timer.Update(UnityEngine.Time.deltaTime);
            var scope = new TimerScope { Timer = ownedTimer.Timer, WasPaused = ownedTimer.Timer.isPause };
            ownedTimer.Timer.isPause = true;
            return scope;
        }
        internal static BattleEndJugment.ResultType Result(BattleEndJugment.ResultType original)
        {
            if (!BossRuntime.TryCurrent(out var source, out var destination)) return original;
            bool current = Object.ReferenceEquals(token, BossRuntime.CurrentToken);
            string sourceId = source.GetProperty("boss_id").GetString();
            string destinationId = destination.GetProperty("boss_id").GetString();
            var manager = TimerInstanceManager.Instance;
            bool expired = current && ((ownedTimer != null && ownedTimer.Timer.IsFinished)
                || (continuePreviousTimer && previousTimer != null && previousTimer.Timer.IsFinished));
            if (destinationId == "iron_claw" || destinationId == "gogo")
                expired |= manager != null && manager.GrobalTimer != null && manager.GrobalTimer.Timer.IsFinished;
            if (expired && original != BattleEndJugment.ResultType.Restart)
                return BattleEndJugment.ResultType.Lose;
            if (current && sourceId == "golem" && original == BattleEndJugment.ResultType.Win && !golemRescued)
                return BattleEndJugment.ResultType.Lose;
            if (original == BattleEndJugment.ResultType.Win && current)
            {
                // sc_b_timer pauses the deadline on victory before the result
                // screen. An untimed donor must preserve that location behavior.
                if (ownedTimer != null) ownedTimer.Timer.isPause = true;
                if (continuePreviousTimer && previousTimer != null) previousTimer.Timer.isPause = true;
                if ((destinationId == "iron_claw" || destinationId == "gogo")
                    && manager != null && manager.GrobalTimer != null) manager.GrobalTimer.Timer.isPause = true;
            }
            return original;
        }

        internal sealed class HpScope
        {
            internal CharacterParameterBase Parameter;
            internal int Original;
            internal void Restore() { if (Parameter != null) { Parameter.currentHP = Original; Parameter = null; } }
        }
        internal static HpScope NormalizeNecrophobeThreshold()
        {
            if (!BossRuntime.TryCurrent(out var source, out var destination) || source.GetProperty("boss_id").GetString() != "necrophobe") return null;
            var plug = BattlePlugManager.Instance();
            var units = plug.BattleStatusControl.BattleStatusControlInfo.BattleEnemyDatas;
            foreach (var unit in units)
            {
                if (unit == null || unit.GetMonster() == null || unit.GetMonster().Id != 477) continue;
                var parameter = unit.BattleUnitDataInfo.Parameter;
                int cutoff = BossCatalog.ScaleHpThreshold(source, destination, 477, 9999);
                var state = new HpScope { Parameter = parameter, Original = parameter.currentHP };
                // Pinned native NecrophobeFinalAttack only reads this field for
                // its <9999 test before enqueueing a future action. It does not
                // deal damage or alter HP; restore even if that call throws.
                parameter.currentHP = parameter.currentHP < cutoff ? 9998 : 9999;
                return state;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(AddCalcStatic), nameof(AddCalcStatic.execute))]
    internal static class BossFixedDamagePatch
    {
        private static void Postfix(AddCalcStatic __instance, AddStatusCalcResultData __result)
        {
            try
            {
                // This native method creates a fresh DamageConfirm result from
                // DataB + (DataC << 8); percentages and stat-based spells use other
                // calculators. Keep enemy-to-enemy scripted finishers intact.
                if (__result != null) __result.Fixeddamage = BossMechanics.ScaleFixedDamage(__instance.user, __instance.target, __result.Fixeddamage);
            }
            catch (Exception exception) { BossRuntime.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(BattleController), nameof(BattleController.ReStart))]
    internal static class BossRestartMechanicsPatch
    {
        private static void Prefix()
        { try { BossMechanics.End(); } catch (Exception exception) { BossRuntime.Fail(exception); } }
    }

    [HarmonyPatch(typeof(BattleController), nameof(BattleController.InitEnemyStatus))]
    internal static class BossBeginMechanicsPatch
    {
        private static void Postfix()
        { try { BossMechanics.Begin(); } catch (Exception exception) { BossRuntime.Fail(exception); } }
    }
    [HarmonyPatch(typeof(TimerInstanceManager), nameof(TimerInstanceManager.UpdateTimer))]
    internal static class BossOuterTimerPatch
    {
        private static void Prefix(SubSceneManagerMainGame.State __0, out BossMechanics.TimerScope __state)
        {
            __state = null;
            try { __state = BossMechanics.UpdateTimers(__0); } catch (Exception exception) { BossRuntime.Fail(exception); }
        }
        private static void Postfix(BossMechanics.TimerScope __state) { __state?.Restore(); }
        private static Exception Finalizer(Exception __exception, BossMechanics.TimerScope __state)
        { __state?.Restore(); return __exception; }
    }
    // GetEndJugment shares its getter RVA with unrelated native classes. Execute
    // is unique and owns the stored result read by both scripts and controller.
    [HarmonyPatch(typeof(BattleEndJugment), nameof(BattleEndJugment.Execute))]
    internal static class BossObjectivePatch
    {
        private static void Postfix(BattleEndJugment __instance)
        { try { __instance.resultType = BossMechanics.Result(__instance.resultType); } catch (Exception exception) { BossRuntime.Fail(exception); } }
    }
    [HarmonyPatch(typeof(NativeCalls), nameof(NativeCalls.NecrophobeFinalAttack))]
    internal static class BossNecrophobeThresholdPatch
    {
        private static bool Prefix(out BossMechanics.HpScope __state)
        {
            __state = null;
            try { __state = BossMechanics.NormalizeNecrophobeThreshold(); return true; }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
        private static void Postfix(BossMechanics.HpScope __state) { __state?.Restore(); }
        private static Exception Finalizer(Exception __exception, BossMechanics.HpScope __state)
        { __state?.Restore(); return __exception; }
    }
}
