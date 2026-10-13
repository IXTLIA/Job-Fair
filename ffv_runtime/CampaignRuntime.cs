// Native integration for versioned Mixed and Remixed reward catalogs.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Entity.Field;
using Last.Interpreter;
using Last.Management;
using Last.Map;

namespace ProjectBlues.Runtime
{
    internal static class CampaignRuntime
    {
        internal static CampaignCatalog Catalog;
        private static CampaignCatalog worldCatalog;
        private static CampaignCatalog shopCatalog;
        private static CampaignCatalog remixedJobCatalog;
        private static CampaignCatalog travelCatalog;
        private static CampaignCatalog playtestCatalog;
        private static CampaignCatalog fixedKarnakShopCatalog;
        private static CampaignCatalog fixedKarnakRemixedCatalog;
        private static CampaignCatalog bossMixedCatalog;
        private static CampaignCatalog bossRemixedCatalog;
        private static readonly Dictionary<string, CampaignCatalog> expanded = new Dictionary<string, CampaignCatalog>(StringComparer.Ordinal);
        private static CampaignCatalog TravelCatalog(string id)
        {
            if (id == CampaignContract.RemixedBossCatalog)
                return bossRemixedCatalog ?? (bossRemixedCatalog = new CampaignCatalog(id));
            if (id == CampaignContract.FixedKarnakRemixedCatalog)
                return fixedKarnakRemixedCatalog ?? (fixedKarnakRemixedCatalog = new CampaignCatalog(id));
            if (id == CampaignContract.RemixedPlaytestCatalog)
                return playtestCatalog ?? (playtestCatalog = new CampaignCatalog(id));
            return travelCatalog ?? (travelCatalog = new CampaignCatalog(id));
        }
        // A load may compile map events before its async task finishes. Use that
        // save's staged table from the start, never the previously active run.
        internal static RunMetadata EventRun => RunSession.Loading != null ? RunSession.Loading.Metadata : RunSession.Active;
        internal static CampaignCatalog ForRun(RunMetadata run)
        {
            if (CampaignContract.UsesExpandedRewards(run))
            {
                if (!expanded.TryGetValue(run.CatalogId, out var catalog))
                    expanded.Add(run.CatalogId, catalog = new CampaignCatalog(run.CatalogId));
                return catalog;
            }
            if (CampaignContract.UsesWorldTravel(run))
                return TravelCatalog(run.CatalogId);
            if (CampaignContract.UsesBossShuffle(run) && run.Mode == CampaignContract.MixedCrystals)
                return bossMixedCatalog ?? (bossMixedCatalog = new CampaignCatalog(run.CatalogId));
            if (RemixedJobRuntime.Applies(run))
                return remixedJobCatalog ?? (remixedJobCatalog = new CampaignCatalog(CampaignContract.RemixedJobSliceCatalog));
            if (run != null && run.SchemaVersion == 2 && CampaignContract.IsMixedShopCatalog(run.CatalogId))
                return run.CatalogId == CampaignContract.FixedKarnakMixedCatalog
                    ? fixedKarnakShopCatalog ?? (fixedKarnakShopCatalog = new CampaignCatalog(run.CatalogId))
                    : shopCatalog ?? (shopCatalog = new CampaignCatalog(CampaignContract.ShopCatalog));
            if (run != null && run.SchemaVersion == 2 && run.CatalogId == CampaignContract.WorldCatalog)
                return worldCatalog ?? (worldCatalog = new CampaignCatalog(CampaignContract.WorldCatalog));
            return Catalog;
        }
        internal static void Initialize()
        {
            Catalog = new CampaignCatalog();
            worldCatalog = new CampaignCatalog(CampaignContract.WorldCatalog);
            shopCatalog = new CampaignCatalog(CampaignContract.ShopCatalog);
        }
        internal static void ValidateRun(RunMetadata run)
        {
            if (run != null && run.SchemaVersion != 1) CampaignContract.ValidatePlayable(run);
            ForRun(run).ValidateRun(run);
        }

        internal static string Rewrite(string json, RunMetadata run)
        {
            if (run == null || run.SchemaVersion == 1 || json == null) return json;
            string eventRewrite = EventRewardRuntime.Rewrite(json, run);
            if (eventRewrite != json) return eventRewrite;
            if (CampaignContract.UsesWorldTravel(run))
            {
                var travel = WorldTravelScripts.Shared;
                for (int i = 0; i < travel.Scripts.Length; i++)
                    if (travel.HandlesScript(i) && travel.Scripts[i].Matches(json))
                    {
                        string rewritten = travel.RewriteScript(i, DataStorage.Instance.currentMapId, WorldTravelRuntime.EventWorld(run));
                        string reason = travel.BlockedReason(i);
                        if (reason != null) SetupView.Notify(reason);
                        return rewritten;
                    }
            }
            var selectedCatalog = ForRun(run);
            for (int i = 0; i < selectedCatalog.Scripts.Length; i++)
                if (selectedCatalog.Scripts[i].Matches(json))
                {
                    Plugin.Logger.LogInfo("Mixed crystal script prepared: " + i + ", seed " + run.Seed);
                    return selectedCatalog.RewriteScript(i, run);
                }
            return json;
        }

        internal static void PrepareQueuedReward(Integrator interpreter, bool namedQueuedScript = true)
            => PrepareRewardScript(interpreter, namedQueuedScript ? interpreter.scriptName : null, false);

        private static void PrepareRewardScript(Integrator interpreter, string scriptName, bool changedScript)
        {
            var run = EventRun;
            if (run == null || run.SchemaVersion == 1) return;
            if (EventRewardRuntime.TryPrepare(interpreter, run, changedScript, scriptName)) return;
            if (PrepareTravelScript(interpreter, scriptName, changedScript)) return;
            var selectedCatalog = ForRun(run);
            string context = run.Mode == CampaignContract.MixedFiesta ? FiestaRuntime.Context(run)
                : "crystals|" + run.RunId + "|" + run.PlacementHash;
            if (scriptName != null && selectedCatalog.IsKnownScriptName(scriptName)
                && NativeProgram.IsOwnedReplacement(interpreter, context)) return;
            for (int i = 0; i < selectedCatalog.Scripts.Length; i++)
                if (NativeProgram.Matches(interpreter.mainCore, selectedCatalog.Scripts[i]))
                {
                    string rewritten = selectedCatalog.RewriteScript(i, run);
                    if (changedScript) NativeProgram.ReplaceAfterChangeScript(interpreter, selectedCatalog.Scripts[i], rewritten, context);
                    else NativeProgram.ReplaceBeforeDispatch(interpreter, selectedCatalog.Scripts[i], rewritten, context);
                    FiestaRuntime.RegisterWind(interpreter, run, i);
                    Plugin.Logger.LogInfo("Mixed queued crystal script prepared: " + i + ", seed " + run.Seed);
                    return;
                }
            // A changed known event must not silently leak a vanilla job grant.
            // Execute(nonnull JSON) leaves scriptName unchanged, so that value
            // identifies a queued script only on the native null-input path.
            if (scriptName != null && selectedCatalog.IsKnownScriptName(scriptName))
                throw new InvalidOperationException("Known crystal script no longer matches its pinned native source.");
            var program = interpreter.mainCore.mnemonics;
            if (program != null)
                for (int i = 0; i < program.Length; i++)
                {
                    var instruction = program[i];
                    if (instruction == null || instruction.mnemonic != "SysCall" || instruction.operands == null
                        || instruction.operands.sValues == null || instruction.operands.sValues.Length == 0) continue;
                    string token = instruction.operands.sValues[0];
                    if (token != null && token.StartsWith("ジョブ開放：", StringComparison.Ordinal))
                        throw new InvalidOperationException("An uncatalogued job-grant program was stopped.");
                }
        }

        internal static void PrepareChangedScript(Integrator interpreter, string scriptName, bool success)
        {
            // A failed ChangeScript did not load a new program. Never inspect or
            // transform its still-active caller. Expanded rewards include nested
            // Mixed events as well as the existing Remixed travel scripts.
            if (!success) return;
            if (CampaignContract.UsesExpandedRewards(EventRun)) PrepareRewardScript(interpreter, scriptName, true);
            else if (CampaignContract.UsesWorldTravel(EventRun)) PrepareTravelScript(interpreter, scriptName, true);
        }

        private static bool PrepareTravelScript(Integrator interpreter, string scriptName, bool changedScript)
        {
            var run = EventRun;
            if (!CampaignContract.UsesWorldTravel(run)) return false;
            var travel = WorldTravelScripts.Shared;
            bool knownName = scriptName != null && travel.IsKnownScriptName(scriptName);
            int map = DataStorage.Instance.currentMapId, world = WorldTravelRuntime.EventWorld(run);
            string details = "script=" + (scriptName ?? "<explicit JSON>") + ", map=" + map + ", world=" + world
                + ", entry=" + (changedScript ? "ChangeScript" : "Execute");
            if (interpreter == null || interpreter.mainCore == null)
            {
                if (knownName) throw new InvalidOperationException("Known story program has no native core: " + details);
                return false;
            }
            if (changedScript && knownName && interpreter.scriptName != scriptName)
                throw new InvalidOperationException("Changed story program name does not match its request: " + details);
            string context = run.RunId + "|" + run.PlacementHash + "|" + map + "|" + world;
            if (knownName && NativeProgram.IsOwnedReplacement(interpreter, context)) return true;
            for (int i = 0; i < travel.Scripts.Length; i++)
                if (travel.HandlesScript(i) && NativeProgram.Matches(interpreter.mainCore, travel.Scripts[i]))
                {
                    string rewritten = travel.RewriteScript(i, map, world);
                    if (changedScript) NativeProgram.ReplaceAfterChangeScript(interpreter, travel.Scripts[i], rewritten, context);
                    else NativeProgram.ReplaceBeforeDispatch(interpreter, travel.Scripts[i], rewritten, context);
                    string reason = travel.BlockedReason(i);
                    if (reason != null) SetupView.Notify(reason);
                    return true;
                }
            if (knownName)
                throw new InvalidOperationException("Known world-travel/story event differs from its pinned source: " + details);
            return false;
        }

        private sealed class OwnedProperty
        {
            internal PropertyTresureBox Property;
            internal int OriginalId, OriginalQuantity, LastId, LastQuantity;
            internal string OriginalMessage, LastMessage;
        }
        private static readonly Dictionary<IntPtr, OwnedProperty> Changed = new Dictionary<IntPtr, OwnedProperty>();

        internal static void PrepareTreasure(FieldTresureBox box, bool after = false, bool message = true)
        {
            WorldTravelRuntime.RequireIdleForReward();
            RemixedJobRuntime.RequireIdleForContainer();
            if (box == null || box.tresureBoxProperty == null) return;
            var property = box.tresureBoxProperty;
            OwnedProperty owned;
            if (Changed.TryGetValue(property.Pointer, out owned))
            {
                if (property.ContentId != owned.LastId || property.ContentNum != owned.LastQuantity
                    || property.MessageKey != owned.LastMessage)
                    throw new InvalidOperationException("Previously owned treasure property changed unexpectedly.");
                property.ContentId = owned.OriginalId;
                property.ContentNum = owned.OriginalQuantity;
                property.MessageKey = owned.OriginalMessage;
                owned.LastId = owned.OriginalId;
                owned.LastQuantity = owned.OriginalQuantity;
                owned.LastMessage = owned.OriginalMessage;
            }
            var run = EventRun;
            if (run == null || run.SchemaVersion == 1) return;
            if (RunSession.Loading != null) throw new InvalidOperationException("Treasure interaction during unfinished load.");
            int map = DataStorage.Instance.currentMapId;
            var selectedCatalog = ForRun(run);
            bool knownEntity = false;
            foreach (var chest in selectedCatalog.Chests.EnumerateArray())
            {
                if (map != chest.GetProperty("map_id").GetInt32()
                    || property.EntityId != chest.GetProperty("entity_id").GetInt32()) continue;
                knownEntity = true;
                if (selectedCatalog.WorldPools && property.FlagId != chest.GetProperty("flag_id").GetInt32()) continue;
                if (selectedCatalog.WorldPools)
                {
                    var pinned = chest.GetProperty("native_properties");
                    if (property.ObjectId != pinned.GetProperty("object_id").GetInt32()
                        || property.ObjectType != pinned.GetProperty("object_type").GetInt32()
                        || property.ActionId != pinned.GetProperty("action_id").GetInt32()
                        || property.ScriptId != pinned.GetProperty("script_id").GetInt32()
                        || property.LinkObjectId != pinned.GetProperty("link_object_id").GetInt32()
                        || property.SoundId != pinned.GetProperty("se_id").GetInt32()
                        || property.MessageKey != pinned.GetProperty("message_key").GetString()
                        || property.ContentId != pinned.GetProperty("content_id").GetInt32()
                        || property.ContentNum != pinned.GetProperty("content_num").GetInt32()
                        || property.PickUp != pinned.GetProperty("pick_up").GetBoolean())
                        throw new InvalidOperationException("Native world treasure binding changed.");
                }
                else if (property.ObjectId != 0 || property.ObjectType != 7
                    || property.ActionId != 6 || property.ScriptId != 0
                    || property.LinkObjectId != 100 || property.SoundId != 909
                    || property.MessageKey != "T0001_01_01"
                    || property.FlagId != chest.GetProperty("flag_id").GetInt32()
                    || property.ContentId != chest.GetProperty("content_id").GetInt32()
                    || property.ContentNum != chest.GetProperty("quantity").GetInt32()
                    || property.PickUp)
                    throw new InvalidOperationException("Native Wind treasure binding changed.");
                var reward = CampaignCatalog.Find(run, chest.GetProperty("id").GetString());
                if (owned == null)
                {
                    owned = new OwnedProperty { Property = property, OriginalId = property.ContentId,
                        OriginalQuantity = property.ContentNum, OriginalMessage = property.MessageKey };
                    Changed.Add(property.Pointer, owned);
                }
                property.ContentId = CampaignCatalog.NativeContentId(reward);
                property.ContentNum = reward.Quantity;
                if (selectedCatalog.WorldPools)
                {
                    string family = property.MessageKey.StartsWith("T0001", StringComparison.Ordinal) ? "T0001" : "T0003";
                    property.MessageKey = family + (reward.RewardKind == "gil" ? "_03_01" : "_01_01");
                }
                owned.LastId = property.ContentId;
                owned.LastQuantity = reward.Quantity;
                owned.LastMessage = property.MessageKey;
                Plugin.Logger.LogInfo("Mixed treasure prepared: " + reward.LocationId + " -> " + reward.RewardKind + " " + reward.RewardId + " x" + reward.Quantity);
                RemixedJobRuntime.PrepareContainer(box, reward.LocationId, chest, after, message);
                return;
            }
            if (knownEntity) throw new InvalidOperationException("Native treasure claim identity changed.");
        }
    }

    [HarmonyPatch(typeof(EventActionTreasure), nameof(EventActionTreasure.CreateTask),
        new Type[] { typeof(FieldTresureBox), typeof(bool), typeof(bool) })]
    internal static class CampaignTreasurePatch
    {
        private static bool Prefix(FieldTresureBox __0, bool __1, bool __2)
        {
            if (SetupView.FatalError != null) return false;
            try { CampaignRuntime.PrepareTreasure(__0, __1, __2); return true; }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(exception);
                SetupView.Fail("Job Fair could not prepare this treasure. Close FFV and report the log.");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetMessage), new Type[] { typeof(string), typeof(bool) })]
    internal static class CampaignMessagePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            var run = CampaignRuntime.EventRun;
            string message = EventRewardCatalog.Shared.Message(__0, run)
                ?? CampaignRuntime.ForRun(run).Message(__0, run);
            if (message == null) return true;
            __result = message;
            return false;
        }
    }
}
