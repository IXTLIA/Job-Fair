// The three pinned native selectors read only these loot fields and never retain
// the supplied Monster. A throwaway view avoids global master mutation/caches.
using System;
using HarmonyLib;
using Last.Battle;
using Last.Data;
using Last.Data.Master;

namespace ProjectBlues.Runtime
{
    internal static class EnemyLootRuntime
    {
        internal static void Initialize() { _ = EnemyLootCatalog.Shared; EnemyLootExperience.Reset(); }
        internal static Monster Prepare(Monster original)
        {
            var run = CampaignRuntime.EventRun;
            if (original == null || !EnemyLootCatalog.Applies(run)
                || !EnemyLootCatalog.Shared.TryMonster(original.Id, out var native)) return original;
            if (SetupView.FatalError != null || RunSession.Loading != null)
                throw new InvalidOperationException("Enemy loot requested during an unavailable run.");
            int[] contents = { original.DropContentId1, original.DropContentId2, original.DropContentId3, original.DropContentId4,
                               original.DropContentId5, original.DropContentId6, original.DropContentId7, original.DropContentId8 };
            int[] quantities = { original.DropContentId1Value, original.DropContentId2Value, original.DropContentId3Value, original.DropContentId4Value,
                                 original.DropContentId5Value, original.DropContentId6Value, original.DropContentId7Value, original.DropContentId8Value };
            int[] steals = { original.StealContentId1, original.StealContentId2, original.StealContentId3, original.StealContentId4 };
            if (original.DropRate != native.GetProperty("drop_rate").GetInt32())
                throw new InvalidOperationException("Native regular-enemy drop rate changed.");
            for (int index = 0; index < 8; index++)
            {
                var slot = native.GetProperty("drops")[index];
                if (contents[index] != slot[0].GetInt32() || quantities[index] != slot[1].GetInt32())
                    throw new InvalidOperationException("Native regular-enemy drop slot changed.");
                contents[index] = EnemyLootCatalog.Resolve(run, original.Id, "drop", index + 1, contents[index]);
            }
            for (int index = 0; index < 4; index++)
            {
                if (steals[index] != native.GetProperty("steals")[index].GetInt32())
                    throw new InvalidOperationException("Native regular-enemy steal slot changed.");
                steals[index] = EnemyLootCatalog.Resolve(run, original.Id, "steal", index + 1, steals[index]);
            }
            // The complete projection is validated before constructing a native
            // object. No battle entity, source row or static cache receives it.
            return new Monster {
                Id = original.Id, DropRate = original.DropRate,
                DropContentId1 = contents[0], DropContentId1Value = quantities[0],
                DropContentId2 = contents[1], DropContentId2Value = quantities[1],
                DropContentId3 = contents[2], DropContentId3Value = quantities[2],
                DropContentId4 = contents[3], DropContentId4Value = quantities[3],
                DropContentId5 = contents[4], DropContentId5Value = quantities[4],
                DropContentId6 = contents[5], DropContentId6Value = quantities[5],
                DropContentId7 = contents[6], DropContentId7Value = quantities[6],
                DropContentId8 = contents[7], DropContentId8Value = quantities[7],
                StealContentId1 = steals[0], StealContentId2 = steals[1],
                StealContentId3 = steals[2], StealContentId4 = steals[3]
            };
        }
        internal static void Fail(Exception error)
        {
            Plugin.Logger.LogError("Enemy loot stopped: " + error);
            SetupView.Fail("Job Fair stopped an invalid enemy reward. Close FFV and report the log.");
        }
    }
    [HarmonyPatch(typeof(BattleItemDropPlug), nameof(BattleItemDropPlug.Lot), new Type[] { typeof(Monster) })]
    internal static class EnemyLootDropPatch
    {
        private static bool Prefix(ref Monster __0, ref DropItemData __result)
        {
            try { __0 = EnemyLootRuntime.Prepare(__0); return true; }
            catch (Exception error) { __result = new DropItemData(0, 0); EnemyLootRuntime.Fail(error); return false; }
        }
    }
    [HarmonyPatch(typeof(BattleStealItemPlug), nameof(BattleStealItemPlug.StealContentId), new Type[] { typeof(Monster), typeof(int) })]
    internal static class EnemyLootStealPatch
    {
        private static bool Prefix(ref Monster __0, ref int __result)
        {
            try { __0 = EnemyLootRuntime.Prepare(__0); return true; }
            catch (Exception error) { __result = 0; EnemyLootRuntime.Fail(error); return false; }
        }
    }
    [HarmonyPatch(typeof(BattleStealItemPlug), nameof(BattleStealItemPlug.GetStealContentIds), new Type[] { typeof(Monster), typeof(int) })]
    internal static class EnemyLootStealListPatch
    {
        private static bool Prefix(ref Monster __0, ref Il2CppSystem.Collections.Generic.List<int> __result)
        {
            try { __0 = EnemyLootRuntime.Prepare(__0); return true; }
            catch (Exception error) { __result = new Il2CppSystem.Collections.Generic.List<int>(); EnemyLootRuntime.Fail(error); return false; }
        }
    }
}
