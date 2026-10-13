// A battle-local ownership transition, never an ABP or existing-save migration.
using System;
using HarmonyLib;
using Last.Battle;
using Last.Data.User;
using Last.Systems;
using JobInformation = Serial.FF5.Management.JobInfomationData;
using NativeIds = Il2CppSystem.Collections.Generic.IEnumerable<int>;
using NativeIdList = Il2CppSystem.Collections.Generic.List<int>;

namespace ProjectBlues.Runtime
{
    internal static class KickLearning
    {
        internal readonly struct Snapshot
        {
            internal readonly bool Eligible;
            internal readonly int CharacterId;
            internal Snapshot(int characterId) { Eligible = true; CharacterId = characterId; }
        }

        internal static bool HasLearned(OwnedCharacterData character)
            => character != null && character.LearningAbilitys != null
                && character.LearningAbilitys.Contains(KickData.ContentId);

        internal static Snapshot Capture(OwnedCharacterData character)
        {
            if (character == null || character.JobId != KickData.MonkJobId) return default;
            KickData.RequireReady();
            var learned = character.LearningAbilitys;
            if (learned == null)
                throw new InvalidOperationException("Kick learning could not snapshot the native learned-ability list.");
            return learned.Contains(KickData.BarehandedContentId) || learned.Contains(KickData.ContentId)
                ? default : new Snapshot(character.Id);
        }

        internal static bool GrantNewlyLearned(OwnedCharacterData character, Snapshot before)
        {
            if (!before.Eligible || character == null) return false;
            KickData.RequireReady();
            if (character.Id != before.CharacterId || character.JobId != KickData.MonkJobId)
                throw new InvalidOperationException("Kick learning received a different post-battle character or job.");
            var learned = character.LearningAbilitys;
            if (learned == null)
                throw new InvalidOperationException("Kick learning received an unavailable post-battle ability list.");
            if (!learned.Contains(KickData.BarehandedContentId) || learned.Contains(KickData.ContentId)) return false;
            learned.Add(KickData.ContentId);
            Plugin.Logger.LogInfo("Learned Kick with Barehanded: native command content 881, Monk level 2.");
            return true;
        }

        internal static NativeIds IncludeLearned(OwnedCharacterData character, NativeIds nativeResult)
        {
            if (!HasLearned(character)) return nativeResult;
            KickData.RequireReady();
            if (nativeResult == null)
                throw new InvalidOperationException("Kick learning received an unavailable native ability list.");
            // The pinned selectors return a List<int>. Native interface enumeration
            // previously failed in this game; preserve the proven indexed adapter.
            var nativeList = nativeResult.TryCast<NativeIdList>();
            if (nativeList == null)
                throw new InvalidOperationException("Kick learning received an unexpected native ability list type.");
            if (nativeList.Contains(KickData.ContentId)) return nativeResult;
            var result = new NativeIdList();
            for (int index = 0; index < nativeList.Count; index++) result.Add(nativeList[index]);
            result.Add(KickData.ContentId);
            return result.Cast<NativeIds>();
        }
    }

    [HarmonyPatch(typeof(StatusUpProvider), nameof(StatusUpProvider.Execution),
        new Type[] { typeof(OwnedCharacterData), typeof(int), typeof(BattleSkillUpInformation) })]
    internal static class KickLearnAfterBattlePatch
    {
        internal static void Prefix(OwnedCharacterData __0, out KickLearning.Snapshot __state)
        {
            __state = default;
            try { __state = KickLearning.Capture(__0); }
            catch (Exception exception) { KickData.Fail(exception); }
        }

        internal static void Postfix(OwnedCharacterData __result, KickLearning.Snapshot __state)
        {
            try { KickLearning.GrantNewlyLearned(__result, __state); }
            catch (Exception exception) { KickData.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedJobAbilitys),
        new Type[] { typeof(OwnedCharacterData) })]
    internal static class KickLearnedListPatch
    {
        internal static void Postfix(OwnedCharacterData __0, ref NativeIds __result)
        {
            try { __result = KickLearning.IncludeLearned(__0, __result); }
            catch (Exception exception) { KickData.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedAbilityAllIds),
        new Type[] { typeof(OwnedCharacterData) })]
    internal static class KickLearnedCatalogPatch
    {
        internal static void Postfix(OwnedCharacterData __0, ref NativeIds __result)
        {
            try { __result = KickLearning.IncludeLearned(__0, __result); }
            catch (Exception exception) { KickData.Fail(exception); }
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetAbilityLearningJob), new Type[] { typeof(int) })]
    internal static class KickLearningJobPatch
    {
        internal static bool Prefix(int __0, ref int __result)
        {
            if (__0 != KickData.ContentId) return true;
            __result = KickData.MonkJobId;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetAbilityLearningLevel), new Type[] { typeof(int) })]
    internal static class KickLearningLevelPatch
    {
        internal static bool Prefix(int __0, ref int __result)
        {
            if (__0 != KickData.ContentId) return true;
            __result = KickData.LearningLevel;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobInformation), nameof(JobInformation.GetLearnedAbilityMaxCount), new Type[] { })]
    internal static class KickLearningMaxCountPatch
    {
        internal static void Postfix(ref int __result)
        {
            if (KickData.Ready && !KickData.Failed) __result++;
        }
    }
}
