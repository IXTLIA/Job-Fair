// Production Kick and Equip Bells learning adapters with disposable managed
// stand-ins. This does not invoke Harmony, native FFV code, or native save I/O.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProjectBlues.Runtime;
using Last.Data.User;
using NativeIds = Il2CppSystem.Collections.Generic.IEnumerable<int>;
using Ids = Il2CppSystem.Collections.Generic.List<int>;

internal static class KickLearningProbe
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        checks++;
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe operation accepted: " + message);
    }
    private static Ids List(params int[] ids)
    { var list = new Ids(); foreach (int id in ids) list.Add(id); return list; }
    private static int[] Values(NativeIds ids) => ids.Values.ToArray();
    private static OwnedCharacterData Character(int id = 1, int job = 3, int abp = 45, params int[] learned)
    {
        var character = new OwnedCharacterData { Id = id, JobId = job, CurrentExp = 1234,
            LearningAbilitys = List(learned), EquipmentAbilitys = List(882, 333) };
        character.OwnedJobDataList.Add(new OwnedJobData { Id = job, Level = 1, CurrentProficiency = abp });
        return character;
    }
    private static OwnedCharacterData Clone(OwnedCharacterData original)
    {
        var clone = Character(original.Id, original.JobId, 0, Values(original.LearningAbilitys));
        clone.CurrentExp = original.CurrentExp;
        clone.EquipmentAbilitys = List(Values(original.EquipmentAbilitys));
        clone.OwnedJobDataList.Values.Clear();
        foreach (var job in original.OwnedJobDataList.Values)
            clone.OwnedJobDataList.Add(job == null ? null : new OwnedJobData {
                Id = job.Id, Level = job.Level, CurrentProficiency = job.CurrentProficiency });
        return clone;
    }
    private static string Protected(OwnedCharacterData character)
        => character.Id + "/" + character.JobId + "/" + character.CurrentExp + "/"
            + String.Join(",", Values(character.EquipmentAbilitys)) + "/"
            + String.Join(";", character.OwnedJobDataList.Values.Select(j => j == null ? "null"
                : j.Id + ":" + j.Level + ":" + j.CurrentProficiency));
    private static void ResetReadiness()
    {
        KickData.Ready = EquipBellsData.Ready = true;
        KickData.Failed = EquipBellsData.Failed = false;
        KickData.Failures = EquipBellsData.Failures = 0;
    }
    private static void Transition()
    {
        // Proficiency has already been applied when native Execution is called.
        // Membership transition, not this field's magnitude, owns the bonus.
        foreach (int abp in new[] { 0, 1, 44, 45, 46, 150, 700, int.MaxValue })
        {
            var before = Character(abp: abp, learned: new[] { 0, 882, 882 });
            string original = Protected(before);
            var oldList = before.LearningAbilitys;
            var snapshot = KickLearning.Capture(before);
            Check(snapshot.Eligible && snapshot.CharacterId == before.Id, "Eligible Monk snapshot lost identity.");
            Check(Protected(before) == original && ReferenceEquals(oldList, before.LearningAbilitys)
                && Values(oldList).SequenceEqual(new[] { 0, 882, 882 }), "Capture mutated the old character.");
            Check(!KickLearning.GrantNewlyLearned(before, snapshot), "Unchanged learned list granted Kick.");
            var after = Clone(before);
            // Simulate native learning several levels in one result. The probe
            // verifies preservation of those outputs, not the native algorithm.
            after.LearningAbilitys.Add(333); after.LearningAbilitys.Add(883); after.LearningAbilitys.Add(338);
            after.OwnedJobDataList[0].Level = 4;
            var newList = after.LearningAbilitys; string nativeState = Protected(after);
            Check(KickLearning.GrantNewlyLearned(after, snapshot), "New Barehanded did not grant Kick.");
            Check(ReferenceEquals(newList, after.LearningAbilitys), "Grant replaced the native learned list.");
            Check(Values(newList).SequenceEqual(new[] { 0, 882, 882, 333, 883, 338, 881 }),
                "Kick did not preserve all native grants and append native command content 881.");
            Check(Protected(after) == nativeState, "Kick modified native levels, ABP, experience or equipment.");
            Check(Protected(before) == original && Values(oldList).SequenceEqual(new[] { 0, 882, 882 }),
                "Post-battle grant mutated the pre-battle clone.");
            Check(!KickLearning.GrantNewlyLearned(after, snapshot) && newList.Count == 7,
                "Repeated postfix added another Kick.");
            Check(!KickLearning.Capture(after).Eligible, "Already learned Kick became eligible again.");
            var next = Clone(after);
            Check(!KickLearning.GrantNewlyLearned(next, KickLearning.Capture(after)), "Later battle granted again.");
        }
        var aliased = Character(learned: new[] { 882 });
        var aliasState = KickLearning.Capture(aliased);
        aliased.LearningAbilitys.Add(333);
        Check(KickLearning.GrantNewlyLearned(aliased, aliasState), "Same-object native return lost prefix state.");
        Check(Values(aliased.LearningAbilitys).SequenceEqual(new[] { 882, 333, 881 }), "Same-object grant changed native output.");
        var nativeKick = Character(); var nativeKickState = KickLearning.Capture(nativeKick);
        nativeKick.LearningAbilitys.Add(333); nativeKick.LearningAbilitys.Add(881);
        Check(!KickLearning.GrantNewlyLearned(nativeKick, nativeKickState) && nativeKick.LearningAbilitys.Count == 2,
            "An existing native Kick grant was duplicated.");
    }
    private static void Nonretroactivity()
    {
        foreach (int abp in new[] { 0, 44, 45, 700, int.MaxValue })
        foreach (int job in new[] { 1, 2, 3, 10, 22 })
        {
            var old = Character(job: job, abp: abp, learned: new[] { 882, 333, 333 });
            var saved = Values(old.LearningAbilitys); string state = Protected(old);
            KickLearnAfterBattlePatch.Prefix(old, out var snapshot);
            Check(!snapshot.Eligible, "Existing Barehanded acquired retrospective eligibility.");
            // Both zero-AP and later positive-AP execution keep Barehanded owned.
            KickLearnAfterBattlePatch.Postfix(old, snapshot);
            old.OwnedJobDataList[0].CurrentProficiency = int.MaxValue;
            KickLearnAfterBattlePatch.Prefix(old, out snapshot);
            KickLearnAfterBattlePatch.Postfix(old, snapshot);
            Check(Values(old.LearningAbilitys).SequenceEqual(saved) && !KickLearning.HasLearned(old),
                "Old Barehanded was upgraded by an ABP/battle check.");
            old.OwnedJobDataList[0].CurrentProficiency = abp;
            Check(Protected(old) == state, "Nonretroactive battle changed another field.");
            NativeIds allowed = List(0, 882, 333); var original = allowed;
            KickLearnedListPatch.Postfix(old, ref allowed);
            KickLearnedCatalogPatch.Postfix(old, ref allowed);
            Check(ReferenceEquals(allowed, original) && Values(old.LearningAbilitys).SequenceEqual(saved),
                "Menu query learned Kick or changed unearned native result.");
        }
        var manager = new Last.Management.UserDataManager();
        Last.Management.UserDataManager.Current = manager;
        var mastered = Character(abp: 700, learned: new[] { 882, 333 });
        mastered.OwnedJobDataList[0].Level = 7;
        mastered.OwnedJobDataList.Add(new OwnedJobData { Id = 10, Level = 3, CurrentProficiency = 175 });
        var future = Character(id: 2, abp: 0); manager.OwnedCharacterList.Add(mastered); manager.OwnedCharacterList.Add(future);
        manager.OwnedCharacterList.Add(null);
        var before = Protected(mastered);
        EquipBellsLearning.ReconcileLoaded(); EquipBellsLearning.ReconcileLoaded();
        Check(Values(mastered.LearningAbilitys).SequenceEqual(new[] { 882, 333, 1691 }),
            "Existing load should reconcile Bells only, never Kick.");
        Check(!KickLearning.HasLearned(future) && Protected(mastered) == before, "Load modified jobs or another character.");
        var owned = Character(learned: new[] { 881 });
        Check(!KickLearning.Capture(owned).Eligible, "Existing Kick without Barehanded became eligible.");
        Check(KickLearning.HasLearned(owned), "Persisted Kick was revoked.");
        Check(!KickLearning.HasLearned(null), "Null character has Kick.");
    }
    private static void IdentityAndFailures()
    {
        var first = Character(id: 1); var second = Character(id: 2);
        KickLearnAfterBattlePatch.Prefix(first, out var firstState);
        KickLearnAfterBattlePatch.Prefix(second, out var secondState);
        first.LearningAbilitys.Add(333); second.LearningAbilitys.Add(333);
        KickLearnAfterBattlePatch.Postfix(second, secondState);
        Check(KickLearning.HasLearned(second) && !KickLearning.HasLearned(first), "Nested call leaked character state.");
        KickLearnAfterBattlePatch.Postfix(first, firstState);
        Check(KickLearning.HasLearned(first), "Outer call lost its snapshot.");
        var candidate = Character(); var captured = KickLearning.Capture(candidate);
        var wrong = Character(id: 2, learned: new[] { 333 });
        Reject(() => KickLearning.GrantNewlyLearned(wrong, captured), "different result character");
        Check(Values(wrong.LearningAbilitys).SequenceEqual(new[] { 333 }), "Wrong-character failure wrote learning.");
        wrong = Character(job: 10, learned: new[] { 333 });
        Reject(() => KickLearning.GrantNewlyLearned(wrong, captured), "different result job");
        Check(!KickLearning.HasLearned(wrong), "Wrong-job failure wrote learning.");
        Check(!KickLearning.Capture(null).Eligible && !KickLearning.Capture(Character(job: 1)).Eligible,
            "Null/non-Monk capture became eligible.");
        Check(!KickLearning.GrantNewlyLearned(null, captured), "Null native result granted Kick.");
        Check(!KickLearning.GrantNewlyLearned(Character(learned: new[] { 333 }), default), "Missing prefix state granted Kick.");
        candidate.LearningAbilitys = null;
        Reject(() => KickLearning.Capture(candidate), "missing input list");
        Reject(() => KickLearning.GrantNewlyLearned(candidate, captured), "missing result list");
        Check(!KickLearning.HasLearned(candidate), "Null learned list has Kick.");
        ResetReadiness(); KickLearnAfterBattlePatch.Prefix(candidate, out var failedState);
        Check(!failedState.Eligible && KickData.Failures == 1 && KickData.Failed, "Prefix error did not fail closed.");
        candidate.LearningAbilitys = List(333); KickLearnAfterBattlePatch.Postfix(candidate, failedState);
        Check(!KickLearning.HasLearned(candidate), "Failed prefix leaked a grant.");
        ResetReadiness(); KickLearnAfterBattlePatch.Postfix(wrong, captured);
        Check(KickData.Failures == 1 && !KickLearning.HasLearned(wrong), "Postfix identity error was swallowed without blocking.");
        foreach (bool failed in new[] { false, true })
        {
            ResetReadiness(); KickData.Ready = failed; KickData.Failed = failed;
            // Exercise each readiness axis separately, not only their usual pair.
            if (!failed) KickData.Ready = false;
            Reject(() => KickLearning.Capture(Character()), "unready/failed capture");
            var target = Character(learned: new[] { 333 });
            Reject(() => KickLearning.GrantNewlyLearned(target, captured), "unready/failed completion");
            Check(!KickLearning.HasLearned(target), "Readiness error mutated learned list.");
        }
        ResetReadiness();
        // Prefix/postfix intentionally have no seed/AP scalar argument. Native
        // Execution's int is a RNG seed, so it cannot be treated as ABP here.
        Check(typeof(KickLearnAfterBattlePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
            .GetParameters().All(p => p.ParameterType != typeof(int)), "Prefix incorrectly consumes native RNG seed.");
    }
    private static NativeIds Select(OwnedCharacterData character, NativeIds original, bool catalog, bool kickFirst)
    {
        NativeIds result = original;
        for (int i = 0; i < 2; i++)
        {
            bool kick = i == 0 ? kickFirst : !kickFirst;
            if (catalog && kick) KickLearnedCatalogPatch.Postfix(character, ref result);
            else if (catalog) EquipBellsLearnedCatalogPatch.Postfix(character, ref result);
            else if (kick) KickLearnedListPatch.Postfix(character, ref result);
            else EquipBellsLearnedListPatch.Postfix(character, ref result);
        }
        return result;
    }
    private static void Selectors()
    {
        foreach (bool hasKick in new[] { false, true }) foreach (bool hasBells in new[] { false, true })
        foreach (bool kickFirst in new[] { false, true }) foreach (bool catalog in new[] { false, true })
        foreach (bool nativeKick in new[] { false, true }) foreach (bool nativeBells in new[] { false, true })
        {
            var character = Character(job: 7, learned: new[] { 333 });
            if (hasKick) character.LearningAbilitys.Add(881);
            if (hasBells) character.LearningAbilitys.Add(1691);
            var native = List(0, 882, 882, 333);
            if (nativeKick) { native.Add(881); native.Add(881); }
            if (nativeBells) { native.Add(1691); native.Add(1691); }
            var original = Values(native); var saved = Values(character.LearningAbilitys);
            NativeIds result = Select(character, native, catalog, kickFirst);
            var expected = new List<int>(original);
            foreach (int id in kickFirst ? new[] { 881, 1691 } : new[] { 1691, 881 })
                if ((id == 881 ? hasKick : hasBells) && !expected.Contains(id)) expected.Add(id);
            Check(Values(result).SequenceEqual(expected), "Selector composition lost native order, duplicates, placeholders or earned bonus.");
            Check(Values(native).SequenceEqual(original) && Values(character.LearningAbilitys).SequenceEqual(saved),
                "Selector mutated native input or saved learned list.");
            Check(native.GetEnumeratorCalls == 0 && result.GetEnumeratorCalls == 0, "Selector used broken native enumeration.");
            Check(ReferenceEquals(result, Select(character, result, catalog, !kickFirst)), "Reapplied selectors are not idempotent.");
            if (expected.SequenceEqual(original)) Check(ReferenceEquals(native, result), "Unchanged selection lost exact pass-through.");
        }
        var earned = Character(job: 1, learned: new[] { 881 });
        Check(Values(KickLearning.IncludeLearned(earned, List())).SequenceEqual(new[] { 881 }), "Earned Kick unavailable after job change.");
        var persisted = Clone(earned); // Models native saved-list restoration only.
        Check(Values(KickLearning.IncludeLearned(persisted, List())).SequenceEqual(new[] { 881 }), "Restored saved ID lost visibility.");
        var unearned = Character(learned: new[] { 333 });
        Check(KickLearning.IncludeLearned(unearned, null) == null && KickLearning.IncludeLearned(null, null) == null,
            "Unearned/null selector changed null native result.");
        Reject(() => KickLearning.IncludeLearned(earned, null), "earned null selector");
        var unexpected = new NativeIds(); unexpected.Values.Add(333);
        Check(ReferenceEquals(unexpected, KickLearning.IncludeLearned(unearned, unexpected)), "Unearned non-list was changed.");
        Reject(() => KickLearning.IncludeLearned(earned, unexpected), "earned non-list selector");
        Check(unexpected.GetEnumeratorCalls == 0 && Values(unexpected).SequenceEqual(new[] { 333 }), "Rejected native shape mutated or enumerated.");
        foreach (bool catalog in new[] { false, true })
        {
            ResetReadiness(); NativeIds result = unexpected;
            if (catalog) KickLearnedCatalogPatch.Postfix(earned, ref result); else KickLearnedListPatch.Postfix(earned, ref result);
            Check(KickData.Failures == 1 && KickData.Failed && ReferenceEquals(result, unexpected),
                "Selector error did not retain original output and fail closed.");
        }
        var brokenCursor = List(333);
        Reject(() => brokenCursor.GetEnumerator(), "fixture native-enumerator failure");
        Check(brokenCursor.GetEnumeratorCalls == 1, "Broken native-enumerator fixture was not exercised.");
        foreach (bool catalog in new[] { false, true }) foreach (bool kickFirst in new[] { false, true })
        {
            ResetReadiness(); KickData.Ready = false;
            var both = Character(learned: new[] { 333, 881, 1691 });
            var native = List(0, 882, 333);
            var result = Select(both, native, catalog, kickFirst);
            Check(KickData.Failed && KickData.Failures == 1 && !EquipBellsData.Failed,
                "Kick selector error affected the independent Bells readiness state.");
            Check(Values(result).SequenceEqual(new[] { 0, 882, 333, 1691 })
                && Values(native).SequenceEqual(new[] { 0, 882, 333 }),
                "Failed Kick selector prevented Bells composition or changed native input.");
            Check(Values(both.LearningAbilitys).SequenceEqual(new[] { 333, 881, 1691 }),
                "Failed selector revoked persisted abilities.");
        }
        ResetReadiness();
    }
    private static void BellComposition()
    {
        foreach (bool kickFirst in new[] { false, true })
        {
            var before = Character(learned: new[] { 882 });
            before.OwnedJobDataList.Add(new OwnedJobData { Id = 10, CurrentProficiency = 5, Level = 0 });
            var state = KickLearning.Capture(before); var after = Clone(before); after.LearningAbilitys.Add(333);
            var originalList = after.LearningAbilitys; string nativeState = Protected(after);
            if (kickFirst) { KickLearnAfterBattlePatch.Postfix(after, state); EquipBellsLearnAfterBattlePatch.Postfix(after); }
            else { EquipBellsLearnAfterBattlePatch.Postfix(after); KickLearnAfterBattlePatch.Postfix(after, state); }
            Check(Values(after.LearningAbilitys).SequenceEqual(kickFirst ? new[] { 882, 333, 881, 1691 } : new[] { 882, 333, 1691, 881 }),
                "Battle hook order lost either bonus or native learning.");
            Check(Protected(after) == nativeState && ReferenceEquals(originalList, after.LearningAbilitys), "Combined grants altered protected fields/list identity.");
            Check(Values(before.LearningAbilitys).SequenceEqual(new[] { 882 }), "Combined grants mutated the old character.");
            KickLearnAfterBattlePatch.Postfix(after, state); EquipBellsLearnAfterBattlePatch.Postfix(after);
            Check(after.LearningAbilitys.Count == 4, "Repeated composed hooks duplicate a bonus.");
            var allowed = Select(after, List(0, 882, 333), false, kickFirst);
            var catalog = Select(after, List(0, 882, 333), true, !kickFirst);
            Check(Values(allowed).Intersect(Values(catalog)).Contains(881) && Values(allowed).Intersect(Values(catalog)).Contains(1691),
                "Earned bonuses missing from visible catalog or allowed set.");
        }
        ResetReadiness(); KickData.Failed = true; KickData.Ready = false;
        var bellsOnly = Character(job: 10, abp: 5, learned: new[] { 333 });
        KickLearnAfterBattlePatch.Prefix(bellsOnly, out var ineligible);
        EquipBellsLearnAfterBattlePatch.Postfix(bellsOnly); KickLearnAfterBattlePatch.Postfix(bellsOnly, ineligible);
        Check(Values(bellsOnly.LearningAbilitys).SequenceEqual(new[] { 333, 1691 }),
            "Independent Bells battle learning changed under a Kick failure.");
        Check(!EquipBellsData.Failed && KickData.Failures == 0, "Ineligible Kick hook caused an unrelated failure.");
        ResetReadiness();
    }
    private static void LabelsAndCounts()
    {
        foreach (int id in new[] { 0, 8, 333, 881, 882, 1691, 9999 })
        foreach (bool kickFirst in new[] { false, true })
        {
            int job = 99; bool runNative = true;
            if (kickFirst) { runNative &= KickLearningJobPatch.Prefix(id, ref job); runNative &= EquipBellsLearningJobPatch.Prefix(id, ref job); }
            else { runNative &= EquipBellsLearningJobPatch.Prefix(id, ref job); runNative &= KickLearningJobPatch.Prefix(id, ref job); }
            Check(job == (id == 881 ? 3 : id == 1691 ? 10 : 99) && runNative == (id != 881 && id != 1691),
                "Composed learning-job labels changed unrelated native content.");
            int level = 99; bool nativeLevel = KickLearningLevelPatch.Prefix(id, ref level);
            Check(level == (id == 881 ? 2 : 99) && nativeLevel == (id != 881), "Kick level label modified unrelated content.");
        }
        foreach (bool kickReady in new[] { false, true }) foreach (bool kickFailed in new[] { false, true })
        foreach (bool bellsReady in new[] { false, true }) foreach (bool bellsFailed in new[] { false, true })
        foreach (bool kickFirst in new[] { false, true })
        {
            KickData.Ready = kickReady; KickData.Failed = kickFailed;
            EquipBellsData.Ready = bellsReady; EquipBellsData.Failed = bellsFailed;
            int expected = 65 + (kickReady && !kickFailed ? 1 : 0) + (bellsReady && !bellsFailed ? 1 : 0);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                int count = 65;
                if (kickFirst) { KickLearningMaxCountPatch.Postfix(ref count); EquipBellsLearningMaxCountPatch.Postfix(ref count); }
                else { EquipBellsLearningMaxCountPatch.Postfix(ref count); KickLearningMaxCountPatch.Postfix(ref count); }
                Check(count == expected, "Completion denominator ignored readiness or accumulated between native calls.");
            }
        }
        ResetReadiness();
    }
    public static void Main()
    {
        ResetReadiness(); Transition(); Nonretroactivity(); IdentityAndFailures(); Selectors(); BellComposition(); LabelsAndCounts();
        Console.WriteLine("Kick learning/composed Equip Bells assertions passed: " + checks);
        Console.WriteLine("New Barehanded transitions only; old-save/ABP/menu/load nonretroactivity; per-call identity and clone isolation; exact native-list preservation; 64 selector combinations; job/level/count composition; indexed-enumeration and fail-closed guards.");
        Console.WriteLine("Actual KickLearning.cs and EquipBellsLearning.cs with managed stand-ins. No native execution, save I/O, game launch or installation.");
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string name, Type[] args) { } }
}
namespace Il2CppSystem
{
    public class Object
    {
        public T Cast<T>() where T : class => this as T ?? throw new InvalidOperationException("adapter cast");
        public T TryCast<T>() where T : class => this as T;
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public class IEnumerable<T> : Il2CppSystem.Object
    {
        public readonly System.Collections.Generic.List<T> Values = new System.Collections.Generic.List<T>();
        public int GetEnumeratorCalls { get; private set; }
        public System.Collections.Generic.IEnumerator<T> GetEnumerator()
        { GetEnumeratorCalls++; throw new InvalidOperationException("Native interface enumeration must not be used."); }
    }
    public sealed class List<T> : IEnumerable<T>
    {
        public int Count => Values.Count; public T this[int index] => Values[index];
        public void Add(T value) => Values.Add(value); public bool Contains(T value) => Values.Contains(value);
    }
}
namespace Last.Data.User
{
    public sealed class OwnedJobData { public int Id, Level, CurrentProficiency; }
    public sealed class OwnedCharacterData
    {
        public int Id, JobId, CurrentExp;
        public Ids LearningAbilitys = new Ids(), EquipmentAbilitys = new Ids();
        public Il2CppSystem.Collections.Generic.List<OwnedJobData> OwnedJobDataList = new Il2CppSystem.Collections.Generic.List<OwnedJobData>();
    }
}
namespace Last.Battle { public sealed class BattleSkillUpInformation { public int AP; } }
namespace Last.Systems
{
    public sealed class StatusUpProvider
    { public OwnedCharacterData Execution(OwnedCharacterData target, int seed, Last.Battle.BattleSkillUpInformation information) => target; }
}
namespace Last.Management
{
    public sealed class UserDataManager
    {
        public static UserDataManager Current; public static UserDataManager Instance() => Current;
        public readonly Il2CppSystem.Collections.Generic.List<OwnedCharacterData> OwnedCharacterList = new Il2CppSystem.Collections.Generic.List<OwnedCharacterData>();
    }
}
namespace Serial.FF5.Management
{
    public sealed class JobInfomationData
    {
        public void GetLearnedJobAbilitys() { } public void GetLearnedAbilityAllIds() { }
        public void GetAbilityLearningJob() { } public void GetAbilityLearningLevel() { }
        public void GetLearnedAbilityMaxCount() { }
    }
}
namespace ProjectBlues.Runtime
{
    internal static class KickData
    {
        internal const int ContentId = 881, BarehandedContentId = 333, MonkJobId = 3, LearningLevel = 2;
        internal static bool Ready, Failed; internal static int Failures;
        internal static void RequireReady() { if (!Ready || Failed) throw new InvalidOperationException("Kick not ready"); }
        internal static void Fail(Exception error) { if (Failed) return; Failures++; Failed = true; Ready = false; }
    }
    internal static class EquipBellsData
    {
        internal const int ContentId = 1691, GeomancerJobId = 10, Threshold = 5;
        internal static bool Ready, Failed; internal static int Failures;
        internal static void RequireReady() { if (!Ready || Failed) throw new InvalidOperationException("Bells not ready"); }
        internal static void Fail(Exception error) { if (Failed) return; Failures++; Failed = true; Ready = false; }
    }
    internal static class Plugin { internal static readonly ProbeLogger Logger = new ProbeLogger(); }
    internal sealed class ProbeLogger { internal void LogInfo(object value) { } }
}
