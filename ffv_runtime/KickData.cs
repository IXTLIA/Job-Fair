// Reuse the native Kick command; no master rows, costs or combat formulas change.
using System;
using HarmonyLib;
using Last.Data.Master;
using Last.Management;

namespace ProjectBlues.Runtime
{
    internal static class KickData
    {
        internal const int ContentId = 881;
        internal const int CommandId = 8;
        internal const int AbilityId = 7;
        internal const int BarehandedContentId = 333;
        internal const int MonkJobId = 3;
        internal const int LearningLevel = 2;
        internal static bool Ready { get; private set; }
        internal static bool Failed { get; private set; }

        internal static void RequireReady()
        {
            if (!Ready || Failed)
                throw new InvalidOperationException("Kick learning data was not initialized. Close FFV and report the log.");
        }

        internal static void Verify(MasterAccessCache cache)
        {
            Ready = false;
            if (Failed) return;
            var manager = MasterManager.Instance;
            if (manager == null || cache == null)
                throw new InvalidOperationException("Kick learning master data unavailable.");
            var contents = manager.GetList<Content>();
            var commands = manager.GetList<Command>();
            var abilities = manager.GetList<Ability>();
            var learning = manager.GetList<Learning>();
            if (contents == null || commands == null || abilities == null || learning == null
                || !contents.ContainsKey(ContentId) || !contents.ContainsKey(BarehandedContentId)
                || !commands.ContainsKey(CommandId) || !abilities.ContainsKey(AbilityId)
                || !abilities.ContainsKey(67) || !learning.ContainsKey(9))
                throw new InvalidOperationException("Native Kick/Barehanded records are missing.");
            var kick = contents[ContentId];
            var command = commands[CommandId];
            var attack = abilities[AbilityId];
            var barehanded = contents[BarehandedContentId];
            var unlock = learning[9];
            if (kick.Id != ContentId || kick.TypeId != 7 || kick.TypeValue != CommandId
                || command.Id != CommandId || command.AbilityId != AbilityId || command.JobGroupId != 1
                || command.CommandType != 1 || command.ExecutionType != 1
                || attack.Id != AbilityId || attack.TypeId != 28 || attack.SystemId != 3 || attack.UseJobGroupId != 1
                || barehanded.Id != BarehandedContentId || barehanded.TypeId != 4 || barehanded.TypeValue != 67
                || unlock.TypeId != 5 || unlock.JobId != MonkJobId || unlock.Value1 != LearningLevel
                || unlock.ContentId != BarehandedContentId)
                throw new InvalidOperationException("Native Kick/Barehanded learning contract changed.");
            var byType = cache.ContentMasterTypeAndValueList;
            if (byType == null || !byType.ContainsKey(7) || !byType[7].ContainsKey(CommandId)
                || byType[7][CommandId].Id != ContentId)
                throw new InvalidOperationException("Native Kick command is missing from the content cache.");
            Ready = true;
            Plugin.Logger.LogInfo("Kick learning ready: native command content 881, alongside new Barehanded at Monk level 2.");
        }

        internal static void Fail(Exception exception)
        {
            if (Failed) return;
            Failed = true;
            Ready = false;
            Plugin.Logger.LogError(exception);
            SetupView.Fail("Kick learning encountered an error. Please close FFV and report the log.");
        }
    }

    [HarmonyPatch(typeof(MasterAccessCache), nameof(MasterAccessCache.CreateContentMasterTypeAndValueList))]
    internal static class KickMasterPatch
    {
        internal static void Postfix(MasterAccessCache __instance)
        {
            try { KickData.Verify(__instance); }
            catch (Exception exception) { KickData.Fail(exception); }
        }
    }
}
