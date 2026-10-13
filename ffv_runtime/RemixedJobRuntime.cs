// Owned job/key bonuses for Remixed. Historical draft admission remains disabled.
// Own a native treasure coroutine from preparation until native flag + job commit.
using System;
using System.Collections.Generic;
using System.Text.Json;
using HarmonyLib;
using Last.Entity.Field;
using Last.Interpreter;
using Last.Management;
using Last.Map;
using NativeCalls = Last.Interpreter.Instructions.SystemCall.Current;
using TreasureRoutine = Last.Map.EventActionTreasure._EventOpenTresureBox_d__4;

namespace ProjectBlues.Runtime
{
    internal static class RemixedJobRuntime
    {
        private sealed class Transaction
        {
            internal RunMetadata Run, Claimed;
            internal FieldTresureBox Box;
            internal PropertyTresureBox Property;
            internal RewardPlacement Ordinary, Bonus;
            internal int Map, Flag, Before, ExpectedAfter;
            internal int Content, Quantity;
            internal int Entity, Object, Type, ActionId, Link, Sound;
            internal string Message;
            internal HashSet<int> Released;
            internal TreasureRoutine Routine;
            internal IntPtr Action;
        }
        private sealed class Authorization
        {
            internal RunMetadata Run;
            internal int Job;
            internal bool Startup, Used;
            internal HashSet<int> Before;
        }
        private static Transaction pending;
        private static Authorization authorized;
        internal static bool IsBusy => pending != null || authorized != null;
        internal static bool Applies(RunMetadata run) => run != null && CampaignContract.IsRemixedMode(run.Mode)
            && ((run.SchemaVersion == 2 && run.CatalogId == CampaignContract.RemixedJobSliceCatalog)
                || CampaignContract.UsesWorldTravel(run));

        internal static void RequireIdleForLoad()
        {
            if (IsBusy) throw new InvalidOperationException("A Remixed job reward is still in progress; loading would discard its ownership.");
        }
        internal static void RequireIdleForContainer()
        {
            if (IsBusy) throw new InvalidOperationException("Overlapping treasure or job grants are not allowed.");
        }

        internal static int ReleaseStartingJob(RunMetadata run, int job)
        {
            RequireIdleForContainer(); RequireRun(run, true);
            int index = -1;
            for (int i = 0; i < run.StartingJobs.Count; i++) if (run.StartingJobs[i] == job) index = i;
            if (index < 0) throw new InvalidOperationException("Starter job is outside this saved run.");
            var before = Released();
            var expected = new HashSet<int> { 1 };
            for (int i = 0; i < index; i++) expected.Add(run.StartingJobs[i]);
            if (!before.SetEquals(expected)) throw new InvalidOperationException("Native starter jobs disagree with the ordered saved setup.");
            ReleaseOwned(run, job, before, true);
            return Core.Next;
        }

        internal static void PrepareContainer(FieldTresureBox box, string ordinaryId, JsonElement binding, bool after, bool message)
        {
            var run = CampaignRuntime.EventRun;
            if (!Applies(run)) return;
            RequireRun(run, false);
            var bonus = run.FindPlacement("remixed_job/" + ordinaryId);
            var keyBonus = CampaignContract.UsesWorldTravel(run) ? run.FindPlacement("remixed_key/" + ordinaryId) : null;
            if (bonus != null && keyBonus != null) throw new InvalidOperationException("A container cannot hold both a crystal and world key.");
            bonus = bonus ?? keyBonus;
            if (bonus == null) return;
            bool isKey = bonus.RewardKind == "key" && CampaignContract.UsesWorldTravel(run)
                && (bonus.RewardId == WorldTravelState.World2Key || bonus.RewardId == WorldTravelState.World3Key);
            if ((!isKey && bonus.RewardKind != "job") || bonus.Quantity != 1 || after || !message)
                throw new InvalidOperationException("Unsupported Remixed bonus reward or scripted container invocation.");
            bool eligible = false;
            if (RemixedJobCatalog.Shared.TryGetCandidate(ordinaryId, out var candidate)
                && (isKey ? "remixed_key/" + ordinaryId : candidate.GetProperty("id").GetString()) == bonus.LocationId)
                foreach (var native in candidate.GetProperty("native_bindings").EnumerateArray())
                    if (native.GetProperty("map_id").GetInt32() == binding.GetProperty("map_id").GetInt32()
                        && native.GetProperty("entity_id").GetInt32() == binding.GetProperty("entity_id").GetInt32()
                        && native.GetProperty("flag_id").GetInt32() == binding.GetProperty("flag_id").GetInt32()) eligible = true;
            var p = box.tresureBoxProperty;
            if (!eligible || p.ScriptId != 0 || p.PickUp || p.ObjectType != 7 || p.ActionId != 6
                || p.FlagId != binding.GetProperty("flag_id").GetInt32()
                || DataStorage.Instance.currentMapId != binding.GetProperty("map_id").GetInt32())
                throw new InvalidOperationException("Bonus container differs from the bounded persistent candidate.");
            bool nativeClaim = Claimed(p.FlagId), savedClaim = run.IsClaimed(bonus.LocationId);
            if (nativeClaim != savedClaim) throw new InvalidOperationException("Native container and saved bonus claims disagree.");
            var released = Released();
            if (!released.SetEquals(ExpectedReleased(run))) throw new InvalidOperationException("Native jobs disagree with starter and claimed crystals.");
            if (nativeClaim) return; // Native empty-message path remains untouched.
            var ordinary = run.FindPlacement(ordinaryId);
            if (!CampaignContract.IsOrdinaryReward(ordinary)
                || p.ContentId != CampaignCatalog.NativeContentId(ordinary) || p.ContentNum != ordinary.Quantity
                || (!isKey && released.Contains(bonus.NumericRewardId)))
                throw new InvalidOperationException("Remixed ordinary reward or unclaimed job identity changed.");
            int before = Count(ordinary), limit = Limit(ordinary);
            if (before < 0 || before > limit) throw new InvalidOperationException("Native ordinary inventory count is invalid.");
            pending = new Transaction { Run = run, Claimed = run.WithClaim(bonus.LocationId), Box = box, Property = p,
                Ordinary = ordinary, Bonus = bonus, Map = DataStorage.Instance.currentMapId, Flag = p.FlagId,
                Content = p.ContentId, Quantity = p.ContentNum, Message = p.MessageKey,
                Entity = p.EntityId, Object = p.ObjectId, Type = p.ObjectType, ActionId = p.ActionId,
                Link = p.LinkObjectId, Sound = p.SoundId,
                Before = before, ExpectedAfter = Math.Min(limit, checked(before + ordinary.Quantity)), Released = released };
            Plugin.Logger.LogInfo("Remixed job container prepared: " + bonus.LocationId + ", seed " + run.Seed);
        }

        internal static bool BeforeMoveNext(TreasureRoutine routine)
        {
            if (pending == null) return false;
            if (routine == null || routine.tresureBoxEntity == null
                || routine.tresureBoxEntity.Pointer != pending.Box.Pointer)
                throw new InvalidOperationException("Another treasure coroutine ran during an owned bonus.");
            RequireContext(routine);
            if (pending.Routine == null)
            {
                if (routine.__1__state != 0 || routine.__4__this == null || Claimed(pending.Flag)
                    || Count(pending.Ordinary) != pending.Before || !Released().SetEquals(pending.Released))
                    throw new InvalidOperationException("Bonus coroutine did not begin in its verified unclaimed state.");
                pending.Routine = routine;
                pending.Action = routine.__4__this.Pointer;
            }
            else if (routine.Pointer != pending.Routine.Pointer || routine.__4__this == null
                || routine.__4__this.Pointer != pending.Action)
                throw new InvalidOperationException("Bonus coroutine ownership changed.");
            return true;
        }

        internal static void AfterMoveNext(TreasureRoutine routine, bool advanced)
        {
            RequireContext(routine);
            if (pending.Routine == null || routine.Pointer != pending.Routine.Pointer
                || routine.__4__this == null || routine.__4__this.Pointer != pending.Action)
                throw new InvalidOperationException("Unknown bonus coroutine completion.");
            bool claimed = Claimed(pending.Flag);
            if (!claimed)
            {
                if (!advanced) throw new InvalidOperationException("Bonus coroutine ended before native claim completion.");
                return;
            }
            // Native MoveNext writes the claim then yields in state5. Commit in
            // this same call, before its next yield or any save can run.
            if (!advanced || routine.__1__state != 5 || Count(pending.Ordinary) != pending.ExpectedAfter
                || !Released().SetEquals(pending.Released))
                throw new InvalidOperationException("Native treasure did not complete its expected ordinary reward and claim.");
            var transaction = pending;
            if (transaction.Bonus.RewardKind == "job")
                ReleaseOwned(transaction.Run, transaction.Bonus.NumericRewardId, transaction.Released, false);
            RunSession.Active = transaction.Claimed;
            pending = null;
            Plugin.Logger.LogInfo("Remixed bonus claimed: " + transaction.Bonus.LocationId + " -> " + transaction.Bonus.RewardId);
            SetupView.Notify(transaction.Bonus.RewardKind == "job"
                ? "Crystal found: " + CampaignCatalog.JobName(transaction.Bonus.NumericRewardId)
                : (transaction.Bonus.RewardId == WorldTravelState.World2Key ? "World 2 key found!" : "World 3 key found!")
                    + " Use Warp Shard to travel.");
        }

        private static void RequireContext(TreasureRoutine routine)
        {
            if (pending == null) throw new InvalidOperationException("No owned Remixed bonus exists.");
            RequireRun(pending.Run, false);
            var p = pending.Property;
            if (routine == null || routine.tresureBoxEntity == null || routine.tresureBoxEntity.Pointer != pending.Box.Pointer
                || routine.tresureBoxEntity.tresureBoxProperty == null
                || routine.tresureBoxEntity.tresureBoxProperty.Pointer != p.Pointer
                || DataStorage.Instance.currentMapId != pending.Map || routine.after || !routine.message
                || p.FlagId != pending.Flag || p.ContentId != pending.Content || p.ContentNum != pending.Quantity
                || p.MessageKey != pending.Message || p.ScriptId != 0 || p.PickUp
                || p.EntityId != pending.Entity || p.ObjectId != pending.Object || p.ObjectType != pending.Type
                || p.ActionId != pending.ActionId || p.LinkObjectId != pending.Link || p.SoundId != pending.Sound
                || (routine._propertyTresureBox_5__2 != null && routine._propertyTresureBox_5__2.Pointer != p.Pointer))
                throw new InvalidOperationException("Owned Remixed container context changed.");
        }

        internal static bool BeforeAddReleaseJob(int job)
        {
            if (authorized != null)
            {
                RequireRun(authorized.Run, authorized.Startup);
                if (authorized.Used || authorized.Job != job || !Released().SetEquals(authorized.Before))
                    throw new InvalidOperationException("Unexpected nested native job release.");
                authorized.Used = true;
                return true;
            }
            // Wrappers inline the Common method. AddReleaseJob is their actual
            // common native callee, including ChangeScript/queued dispatch.
            var run = RunSession.Pending ?? CampaignRuntime.EventRun;
            if (run != null && run.Mode == CampaignContract.MixedFiesta)
                return FiestaRuntime.BeforeAddReleaseJob(run, job);
            if (!Applies(run)) return true;
            if (job == 1) return true; // Freelancer remains universally available.
            if (job < 2 || job > 22) throw new InvalidOperationException("Unknown native job release in Remixed.");
            Plugin.Logger.LogInfo("Suppressed native Remixed story job: " + job);
            return false;
        }

        private static void ReleaseOwned(RunMetadata run, int job, HashSet<int> before, bool startup)
        {
            if (authorized != null || before.Contains(job)) throw new InvalidOperationException("Repeated authorized job release.");
            var scope = new Authorization { Run = run, Job = job, Startup = startup, Before = before };
            authorized = scope;
            try
            {
                int result = NativeCalls.ReleaseJobCommon((NativeCalls.JobId)job);
                RequireRun(run, startup);
                var expected = new HashSet<int>(before) { job };
                if (!scope.Used || result != Core.Next || !Released().SetEquals(expected))
                    throw new InvalidOperationException("Native job release failed its exact unlocked-state check.");
            }
            finally { authorized = null; }
        }

        private static void RequireRun(RunMetadata run, bool startup)
        {
            if (!Applies(run) || SetupView.FatalError != null || RunSession.Loading != null
                || !Object.ReferenceEquals(startup ? RunSession.Pending : RunSession.Active, run))
                throw new InvalidOperationException("Remixed reward ownership changed or is not ready.");
        }
        private static HashSet<int> Released()
        {
            var manager = UserDataManager.Instance();
            if (manager == null) throw new InvalidOperationException("Native job list is unavailable.");
            var jobs = manager.GetReleasedJobsClone();
            if (jobs == null) throw new InvalidOperationException("Native job list is unavailable.");
            var result = new HashSet<int>();
            for (int i = 0; i < jobs.Count; i++)
                if (jobs[i] == null || jobs[i].Id < 1 || jobs[i].Id > 22 || !result.Add(jobs[i].Id))
                    throw new InvalidOperationException("Native released-job list is invalid.");
            return result;
        }
        private static HashSet<int> ExpectedReleased(RunMetadata run)
        {
            var expected = new HashSet<int>(run.StartingJobs) { 1 };
            foreach (string claim in run.ClaimedLocationIds)
            {
                var reward = run.FindPlacement(claim);
                if (reward != null && reward.RewardKind == "job") expected.Add(reward.NumericRewardId);
            }
            return expected;
        }
        private static bool Claimed(int flag) => EventProcedure.GetFlag(EventProcedure.DataStorageType.Treasure, flag);
        // The frozen world pool's five magic rewards use the native owned-magic
        // set instead of item stacks. Native AddOwnedItem passes Content.Id here.
        private static bool Magic(int id) => id == 379 || id == 385 || id == 410 || id == 415 || id == 418;
        private static int Count(RewardPlacement ordinary)
        {
            var manager = UserDataManager.Instance();
            if (manager == null) throw new InvalidOperationException("Native inventory is unavailable.");
            if (ordinary.RewardKind == "gil") return manager.OwendGil;
            if (ordinary.RewardKind == "magic" || Magic(ordinary.NumericRewardId))
            {
                if (manager.OwnedMagicList == null) throw new InvalidOperationException("Native magic ownership is unavailable.");
                return manager.OwnedMagicList.Contains(ordinary.NumericRewardId) ? 1 : 0;
            }
            var items = manager.GetOwnedItemsClone();
            if (items == null) throw new InvalidOperationException("Native items are unavailable.");
            int count = 0; bool found = false;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].ContentId == ordinary.NumericRewardId)
                {
                    if (found) throw new InvalidOperationException("Duplicate native ordinary item stack.");
                    count = items[i].Count; found = true;
                }
            return count;
        }
        private static int Limit(RewardPlacement ordinary)
        {
            var config = SystemConfig.Instance();
            if (config == null) throw new InvalidOperationException("Native inventory limit is unavailable.");
            if (ordinary.RewardKind == "gil") return config.GetGilMaxCount();
            return ordinary.RewardKind == "magic" || Magic(ordinary.NumericRewardId) ? 1 : config.GetItemMaxCount();
        }
        internal static void Fail(Exception error)
        {
            Plugin.Logger.LogError(error);
            SetupView.Fail("Job Fair stopped an incomplete crystal or world-key reward. Close FFV and report the log.");
            // Ownership deliberately remains busy after any possible native side
            // effect. Never clear it on load/new game/error and save a half-grant.
        }
    }

    [HarmonyPatch(typeof(TreasureRoutine), nameof(TreasureRoutine.MoveNext), new Type[] { })]
    internal static class RemixedTreasureRoutinePatch
    {
        private static bool Prefix(TreasureRoutine __instance, ref bool __result, out bool __state)
        {
            __state = false;
            try { __state = RemixedJobRuntime.BeforeMoveNext(__instance); return true; }
            catch (Exception error) { RemixedJobRuntime.Fail(error); __result = false; return false; }
        }
        private static void Postfix(TreasureRoutine __instance, ref bool __result, bool __state)
        {
            if (!__state) return;
            try { RemixedJobRuntime.AfterMoveNext(__instance, __result); }
            catch (Exception error) { RemixedJobRuntime.Fail(error); __result = false; }
        }
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__exception != null && __state) RemixedJobRuntime.Fail(__exception);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(UserDataClient), nameof(UserDataClient.AddReleaseJob), new Type[] { typeof(int) })]
    internal static class RemixedNativeJobPatch
    {
        private static bool Prefix(int __0)
        {
            try { return RemixedJobRuntime.BeforeAddReleaseJob(__0); }
            catch (Exception error) { RemixedJobRuntime.Fail(error); return false; }
        }
    }
}
