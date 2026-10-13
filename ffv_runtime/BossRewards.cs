// Location rewards are applied before native inventory/character commits.
// Ordinary item drops and steals retain the assigned boss's native behavior.
// Expanded runs award one destination HP budget of EXP before native boosts.
using System;
using System.Collections.Generic;
using System.Text.Json;
using HarmonyLib;
using Last.Battle;
using Last.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ProjectBlues.Runtime
{
    public readonly struct BossRewardItem
    {
        public readonly int ContentId, Quantity;
        public BossRewardItem(int contentId, int quantity) { ContentId = contentId; Quantity = quantity; }
    }

    // Pure policy is shared by native adapters and the offline probes. Rebuilding
    // an already projected result is idempotent, including duplicate source drops.
    public static class BossRewardPolicy
    {
        public const string Version = "destination-rewards-v1";
        public static bool IsProgressionSummon(int contentId) => contentId == 33 || contentId == 34 || contentId == 35;
        public static List<BossRewardItem> ProjectDrops(IEnumerable<BossRewardItem> native, int destinationSummon)
        {
            if (destinationSummon != 0 && !IsProgressionSummon(destinationSummon))
                throw new ArgumentException("Unknown boss destination summon reward.");
            var result = new List<BossRewardItem>();
            if (native != null)
                foreach (var item in native)
                {
                    if (item.ContentId <= 0 || item.Quantity <= 0)
                        throw new ArgumentException("Invalid native boss drop.");
                    if (!IsProgressionSummon(item.ContentId)) result.Add(item);
                }
            if (destinationSummon != 0) result.Add(new BossRewardItem(destinationSummon, 1));
            return result;
        }
        public static int Gil(int destinationGil, int stolenGil, bool onlyDefeatGil)
        {
            if (destinationGil < 0 || stolenGil < 0) throw new ArgumentException("Invalid boss gil reward.");
            return checked(destinationGil + (onlyDefeatGil ? 0 : stolenGil));
        }
        public static int CompletionMonster(int summonItem)
        {
            switch (summonItem)
            {
                case 0: return 0;
                case 33: return 40;
                case 34: return 115;
                case 35: return 230;
                default: throw new ArgumentException("Unknown native summon completion mapping.");
            }
        }
        public static List<int> ProjectCompletionIds(IEnumerable<int> native, int summonItem)
        {
            int completion = CompletionMonster(summonItem);
            var result = new List<int>();
            if (native != null)
                foreach (int id in native)
                    if (id != 40 && id != 115 && id != 230) result.Add(id);
            if (completion != 0) result.Add(completion);
            return result;
        }
        public static int DestinationValue(JsonElement destination, string kind)
        {
            int value = destination.GetProperty("destination_rewards").GetProperty(kind).GetInt32();
            if (value < 0) throw new ArgumentException("Invalid boss destination reward.");
            return value;
        }
        public static int Experience(JsonElement destination, bool destinationHp)
        {
            if (!destinationHp) return DestinationValue(destination, "exp");
            int value = destination.GetProperty("effective_hp_budget").GetInt32();
            if (value <= 0) throw new ArgumentException("Invalid destination boss EXP budget.");
            return value;
        }
    }

    internal static class BossRewards
    {
        private static object preparedToken, winnerToken;
        private static BattleItemDropPlug preparedPlug;
        private static Il2CppSystem.Collections.Generic.List<DropItemData> preparedDrops;
        private static int destinationSummon;
        private static object projectedToken;
        private static BattleItemDropPlug projectedPlug;
        private static Il2CppSystem.Collections.Generic.List<DropItemData> projectedDrops;
        internal static void Reset()
        {
            EnemyLootExperience.RevokeVictory();
            preparedToken = null; winnerToken = null; preparedPlug = null; preparedDrops = null; destinationSummon = 0;
            projectedToken = null; projectedPlug = null; projectedDrops = null;
        }
        internal static bool CanCommit => SetupView.FatalError == null
            || (!CampaignContract.UsesExpandedRewards(CampaignRuntime.EventRun)
                && !BossRuntime.TryCurrent(out _, out _) && !EnemyLootExperience.HasEncounter);
        private static RewardPlacement EventReward(JsonElement destination)
        {
            var run = CampaignRuntime.EventRun;
            int summon = BossRewardPolicy.DestinationValue(destination, "summon_item");
            if (!CampaignContract.UsesExpandedRewards(run) || summon == 0) return null;
            var reward = EventRewardCatalog.BossReward(run, summon);
            if (reward == null || (reward.RewardKind != "item" && reward.RewardKind != "magic" && reward.RewardKind != "gil")
                || reward.Quantity <= 0 || (reward.RewardKind != "gil" && reward.NumericRewardId <= 0))
                throw new InvalidOperationException("Invalid randomized boss event reward.");
            return reward;
        }
        private static int DestinationGil(JsonElement destination)
        {
            int native = BossRewardPolicy.DestinationValue(destination, "gil");
            var reward = EventReward(destination);
            return checked(native + (reward != null && reward.RewardKind == "gil" ? reward.Quantity : 0));
        }
        internal static bool CanStartVictory()
        {
            if (!BossRuntime.TryCurrent(out var source, out var destination)) return CanCommit;
            if (SetupView.FatalError != null) return false;
            foreach (string value in new[] { "exp", "gil", "abp", "summon_item" })
                BossRewardPolicy.DestinationValue(destination, value);
            int summon = BossRewardPolicy.DestinationValue(destination, "summon_item");
            if (summon != 0 && !BossRewardPolicy.IsProgressionSummon(summon))
                throw new InvalidOperationException("Unknown boss destination summon reward.");
            BossRewardPolicy.Experience(destination, EnemyLootExperience.Applies(CampaignRuntime.EventRun));
            _ = EventReward(destination);
            RequireObjective(source);
            return true;
        }
        internal static bool PrepareVictory()
        {
            Reset();
            if (!CanStartVictory()) return false;
            if (!BossRuntime.TryCurrent(out _, out var destination))
            { EnemyLootExperience.PrepareVictory(); return true; }
            var manager = BattlePlugManager.Instance();
            if (manager == null || manager.BattleItemDropPlug == null)
                throw new InvalidOperationException("Boss victory has no native drop plug.");
            var plug = manager.BattleItemDropPlug;
            // The original StartWinResult begins with exactly this lottery.
            // Execute and validate it before that method can write scenario,
            // bestiary, achievement, victory-count or character state.
            plug.LotGilIndividualDropItem();
            if (SetupView.FatalError != null) return false;
            if (plug.dropItemList == null) throw new InvalidOperationException("Boss lottery produced no drop list.");
            destinationSummon = BossRewardPolicy.DestinationValue(destination, "summon_item");
            preparedToken = winnerToken = BossRuntime.CurrentToken;
            preparedPlug = plug; preparedDrops = plug.dropItemList;
            return true;
        }
        internal static bool SkipPreparedLottery(BattleItemDropPlug plug)
        {
            if (preparedToken == null) return false;
            if (!Object.ReferenceEquals(preparedToken, BossRuntime.CurrentToken))
            { Reset(); return false; }
            if (plug == null || preparedPlug == null || plug.Pointer != preparedPlug.Pointer
                || plug.dropItemList == null || preparedDrops == null || plug.dropItemList.Pointer != preparedDrops.Pointer)
                throw new InvalidOperationException("Prepared boss lottery changed before result construction.");
            preparedToken = null; preparedPlug = null; preparedDrops = null;
            return true;
        }
        internal static bool ProjectCompletion(ref Il2CppStructArray<int> monsterIds)
        {
            if (!BossRuntime.TryCurrent(out var source, out _)) return true;
            // Only the accepted win chain may clear a randomized location.
            if (SetupView.FatalError != null || winnerToken == null
                || !Object.ReferenceEquals(winnerToken, BossRuntime.CurrentToken)) return false;
            RequireObjective(source);
            var ids = new List<int>();
            if (monsterIds != null) for (int i = 0; i < monsterIds.Length; i++) ids.Add(monsterIds[i]);
            var projected = BossRewardPolicy.ProjectCompletionIds(ids, destinationSummon);
            monsterIds = new Il2CppStructArray<int>(projected.ToArray());
            return true;
        }
        private static void RequireObjective(JsonElement source)
        {
            if (source.GetProperty("boss_id").GetString() == "golem" && !BossMechanics.GolemRescued)
                throw new InvalidOperationException("Golem rescue reached victory without its native completion form.");
        }
        internal static void ClearDrops(BattleItemDropPlug plug)
        { plug.dropItemList = new Il2CppSystem.Collections.Generic.List<DropItemData>(); }
        internal static void ProjectDrops(BattleItemDropPlug plug)
        {
            if (!BossRuntime.TryCurrent(out var source, out var destination)) return;
            if (SetupView.FatalError != null) { ClearDrops(plug); return; }
            bool expanded = CampaignContract.UsesExpandedRewards(CampaignRuntime.EventRun);
            if (expanded && Object.ReferenceEquals(projectedToken, BossRuntime.CurrentToken)
                && projectedPlug != null && projectedPlug.Pointer == plug.Pointer
                && projectedDrops != null && plug.dropItemList != null && projectedDrops.Pointer == plug.dropItemList.Pointer) return;
            var native = new List<BossRewardItem>();
            if (plug.dropItemList != null)
                for (int i = 0; i < plug.dropItemList.Count; i++)
                {
                    var item = plug.dropItemList[i];
                    if (item == null) continue;
                    native.Add(new BossRewardItem(item.ContentId, item.DropValue));
                }
            // Runtime's encounter judgment rejects a failed rescue before the
            // win phase. Use its encounter objective marker, not a drop that a
            // previous projection may already have removed from this list.
            RequireObjective(source);
            var items = BossRewardPolicy.ProjectDrops(native, expanded ? 0 : BossRewardPolicy.DestinationValue(destination, "summon_item"));
            var eventReward = EventReward(destination);
            if (eventReward != null && eventReward.RewardKind != "gil")
                items.Add(new BossRewardItem(eventReward.NumericRewardId, eventReward.Quantity));
            var replacement = new Il2CppSystem.Collections.Generic.List<DropItemData>();
            foreach (var item in items) replacement.Add(new DropItemData(item.ContentId, item.Quantity));
            // An encounter-local list replaces an encounter-local list. Original
            // master monsters and drop entries are never modified.
            plug.dropItemList = replacement;
            if (expanded) { projectedToken = BossRuntime.CurrentToken; projectedPlug = plug; projectedDrops = replacement; }
        }
        internal static int ProjectGil(BattleAddExpPlug plug, bool onlyDefeatGil, int nativeResult)
        {
            if (!BossRuntime.TryCurrent(out _, out var destination)) return nativeResult;
            if (SetupView.FatalError != null) return 0;
            return BossRewardPolicy.Gil(DestinationGil(destination), plug.stealGil, onlyDefeatGil);
        }
        internal static void ProjectExperience(ref int exp, ref int gil, ref BattleSkillUpInformation information)
        {
            if (!BossRuntime.TryCurrent(out _, out var destination))
            {
                if (EnemyLootExperience.TryBudget(out int supplemental)) exp = supplemental;
                return;
            }
            if (information == null) throw new InvalidOperationException("Boss result has no skill information.");
            int destinationExp = BossRewardPolicy.Experience(destination, EnemyLootExperience.Applies(CampaignRuntime.EventRun));
            int destinationGil = DestinationGil(destination);
            int destinationAp = BossRewardPolicy.DestinationValue(destination, "abp");
            var replacement = new BattleSkillUpInformation(information.BattleActLogDataList,
                information.MonsterAverageRank, destinationAp, information.MagicPoint);
            exp = destinationExp; gil = destinationGil; information = replacement;
        }
    }

    [HarmonyPatch(typeof(BattleController), nameof(BattleController.StartWinResult))]
    internal static class BossRewardStartPatch
    {
        private static bool Prefix()
        {
            try { return BossRewards.PrepareVictory(); }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
    }
    [HarmonyPatch(typeof(BattleItemDropPlug), nameof(BattleItemDropPlug.LotGilIndividualDropItem))]
    internal static class BossRewardDropsPatch
    {
        private static bool Prefix(BattleItemDropPlug __instance, out bool __state)
        {
            __state = false;
            try { __state = BossRewards.SkipPreparedLottery(__instance); return !__state; }
            catch (Exception exception) { __state = true; BossRuntime.Fail(exception); BossRewards.ClearDrops(__instance); return false; }
        }
        private static void Postfix(BattleItemDropPlug __instance, bool __state)
        {
            if (__state) return;
            try { BossRewards.ProjectDrops(__instance); }
            catch (Exception exception) { BossRuntime.Fail(exception); BossRewards.ClearDrops(__instance); }
        }
    }
    [HarmonyPatch(typeof(Serial.FF5.Management.SpcialFlagData), nameof(Serial.FF5.Management.SpcialFlagData.SetSpecialEncountFlag))]
    internal static class BossSummonCompletionPatch
    {
        private static bool Prefix(ref Il2CppStructArray<int> __0)
        {
            try { return BossRewards.ProjectCompletion(ref __0); }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
    }
    [HarmonyPatch(typeof(BattleController), nameof(BattleController.SaveGil))]
    internal static class BossRewardWalletPatch
    {
        private static bool Prefix() => BossRewards.CanCommit;
    }
    [HarmonyPatch(typeof(BattleAddExpPlug), nameof(BattleAddExpPlug.GetGil))]
    internal static class BossRewardGilPatch
    {
        private static void Postfix(BattleAddExpPlug __instance, bool __0, ref int __result)
        {
            try { __result = BossRewards.ProjectGil(__instance, __0, __result); }
            catch (Exception exception) { __result = 0; BossRuntime.Fail(exception); }
        }
    }
    [HarmonyPatch(typeof(Serial.FF5.Management.BattleResultProvider), nameof(Serial.FF5.Management.BattleResultProvider.Genelate))]
    internal static class BossRewardExperiencePatch
    {
        private static bool Prefix(ref int __1, ref int __2, ref BattleSkillUpInformation __4)
        {
            if (!BossRewards.CanCommit) return false;
            try { BossRewards.ProjectExperience(ref __1, ref __2, ref __4); return true; }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
    }
}
