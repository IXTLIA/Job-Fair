// The bonus uses FFV's saved content-ID list; existing ABP and job levels are untouched.
using System;
using HarmonyLib;
using Last.Battle;
using Last.Data.User;
using Last.Management;
using Last.Systems;
using JobInformation = Serial.FF5.Management.JobInfomationData;
using NativeIds = Il2CppSystem.Collections.Generic.IEnumerable<int>;
using NativeIdList = Il2CppSystem.Collections.Generic.List<int>;

namespace ProjectBlues.Runtime
{
    internal static class EquipBellsLearning
    {
        internal static bool HasLearned(OwnedCharacterData character)
            => character != null && character.LearningAbilitys != null
                && character.LearningAbilitys.Contains(EquipBellsData.ContentId);

        internal static bool Reconcile(OwnedCharacterData character)
        {
            if (character == null) return false;
            EquipBellsData.RequireReady();
            if (HasLearned(character)) return false;
            var jobs = character.OwnedJobDataList;
            if (jobs == null) return false;
            for (int index = 0; index < jobs.Count; index++)
            {
                var job = jobs[index];
                if (job == null || job.Id != EquipBellsData.GeomancerJobId
                    || job.CurrentProficiency < EquipBellsData.Threshold) continue;
                var learned = character.LearningAbilitys;
                if (learned == null)
                    throw new InvalidOperationException("Equip Bells found an unavailable learned-ability list for an eligible character.");
                learned.Add(EquipBellsData.ContentId);
                Plugin.Logger.LogInfo("Learned Equip Bells: content 1691, Geomancer ABP " + job.CurrentProficiency);
                return true;
            }
            return false;
        }

        // Call only after a successful normal save load, never for the extra library.
        // Exceptions deliberately reach the existing load-failure path.
        internal static void ReconcileLoaded()
        {
            EquipBellsData.RequireReady();
            var manager = UserDataManager.Instance();
            var characters = manager == null ? null : manager.OwnedCharacterList;
            if (characters == null)
                throw new InvalidOperationException("Equip Bells could not read loaded character data.");
            for (int index = 0; index < characters.Count; index++) Reconcile(characters[index]);
        }

        internal static NativeIds IncludeLearned(OwnedCharacterData character, NativeIds nativeResult)
        {
            if (!HasLearned(character)) return nativeResult;
            EquipBellsData.RequireReady();
            if (nativeResult == null)
                throw new InvalidOperationException("Equip Bells received an unavailable native ability list.");
            // Both pinned FFV selectors return a fresh List<int>. Avoid the
            // enumerator interface path that failed in the observed build;
            // read the concrete list through native Count/indexer bindings.
            var nativeList = nativeResult.TryCast<NativeIdList>();
            if (nativeList == null)
                throw new InvalidOperationException("Equip Bells received an unexpected native ability list type.");
            if (nativeList.Contains(EquipBellsData.ContentId)) return nativeResult;
            var result = new NativeIdList();
            int count = nativeList.Count;
            for (int index = 0; index < count; index++) result.Add(nativeList[index]);
            result.Add(EquipBellsData.ContentId);
            return result.Cast<NativeIds>();
        }
    }

    [HarmonyPatch(typeof(StatusUpProvider), nameof(StatusUpProvider.Execution),
        new Type[] { typeof(OwnedCharacterData), typeof(int), typeof(BattleSkillUpInformation) })]
    internal static class EquipBellsLearnAfterBattlePatch
    {
        internal static void Postfix(OwnedCharacterData __result)
        {
            try { EquipBellsLearning.Reconcile(__result); }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedJobAbilitys),
        new Type[] { typeof(OwnedCharacterData) })]
    internal static class EquipBellsLearnedListPatch
    {
        internal static void Postfix(OwnedCharacterData __0, ref NativeIds __result)
        {
            try { __result = EquipBellsLearning.IncludeLearned(__0, __result); }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }

    // GetAllAbility builds visible rows from this catalog before consulting the
    // separate GetLearnedJobAbilitys allowed set. Both need the saved bonus ID.
    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedAbilityAllIds),
        new Type[] { typeof(OwnedCharacterData) })]
    internal static class EquipBellsLearnedCatalogPatch
    {
        internal static void Postfix(OwnedCharacterData __0, ref NativeIds __result)
        {
            try { __result = EquipBellsLearning.IncludeLearned(__0, __result); }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetAbilityLearningJob),
        new Type[] { typeof(int) })]
    internal static class EquipBellsLearningJobPatch
    {
        internal static bool Prefix(int __0, ref int __result)
        {
            if (__0 != EquipBellsData.ContentId) return true;
            __result = EquipBellsData.GeomancerJobId;
            return false;
        }
    }

    // Native current/party counts consume the patched learned list. Include the
    // one new ability in their denominator without changing stock category data.
    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedAbilityMaxCount),
        new Type[] { })]
    internal static class EquipBellsLearningMaxCountPatch
    {
        internal static void Postfix(ref int __result)
        {
            if (EquipBellsData.Ready && !EquipBellsData.Failed) __result++;
        }
    }
}
