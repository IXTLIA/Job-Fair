// Boss EXP includes named native bosses outside the identity-shuffle roster.
// The budget is one destination encounter's effective HP, before native boosts.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Battle;

namespace ProjectBlues.Runtime
{
    internal static class EnemyLootExperience
    {
        internal const string Policy = "destination-hp-v1";
        private sealed class Encounter
        {
            internal InstantiateManager Manager;
            internal string RunId, Hash;
            internal int Formation, Budget;
            internal bool Victory;
        }
        private static readonly Dictionary<IntPtr, Encounter> encounters = new Dictionary<IntPtr, Encounter>();
        internal static bool Applies(RunMetadata run) => CampaignContract.UsesExpandedRewards(run)
            && run.Settings.BossExperience == Policy;
        internal static void Reset() => encounters.Clear();
        internal static void RevokeVictory()
        { foreach (var encounter in encounters.Values) encounter.Victory = false; }
        internal static void Track(InstantiateManager manager, int formation)
        {
            if (manager == null) return;
            var run = CampaignRuntime.EventRun;
            if (!Applies(run)) { encounters.Remove(manager.Pointer); return; }
            if (encounters.TryGetValue(manager.Pointer, out var previous)
                && previous.RunId == run.RunId && previous.Hash == run.PlacementHash
                && previous.Formation == formation) return;
            encounters.Remove(manager.Pointer);
            foreach (var row in EnemyLootCatalog.Shared.SupplementalBossExperience.EnumerateArray())
                if (row.GetProperty("formation_id").GetInt32() == formation)
                {
                    encounters[manager.Pointer] = new Encounter { Manager = manager, RunId = run.RunId,
                        Hash = run.PlacementHash, Formation = formation,
                        Budget = row.GetProperty("effective_hp_budget").GetInt32() };
                    return;
                }
        }
        private static Encounter Current()
        {
            var run = CampaignRuntime.EventRun;
            if (!Applies(run)) return null;
            var plug = BattlePlugManager.Instance();
            var manager = plug == null ? null : plug.InstantiateManager;
            if (manager == null || !encounters.TryGetValue(manager.Pointer, out var encounter)) return null;
            if (encounter.RunId != run.RunId || encounter.Hash != run.PlacementHash)
            { encounters.Remove(manager.Pointer); return null; }
            return encounter;
        }
        internal static bool HasEncounter => Current() != null;
        internal static void PrepareVictory()
        {
            var encounter = Current();
            if (encounter == null) return;
            if (SetupView.FatalError != null || encounter.Budget <= 0)
                throw new InvalidOperationException("Supplemental boss EXP is unavailable.");
            encounter.Victory = true;
        }
        internal static bool TryBudget(out int budget)
        {
            budget = 0;
            var encounter = Current();
            if (encounter == null) return false;
            if (!encounter.Victory || SetupView.FatalError != null)
                throw new InvalidOperationException("Boss EXP requested outside an accepted victory.");
            budget = encounter.Budget;
            return true;
        }
    }
    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.SetMonstarParty))]
    internal static class EnemyLootExperienceFormationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(InstantiateManager __instance, int __0)
        {
            try { EnemyLootExperience.Track(__instance, __0); return true; }
            catch (Exception error) { EnemyLootRuntime.Fail(error); return false; }
        }
    }
    [HarmonyPatch(typeof(Last.Scene.Battle), nameof(Last.Scene.Battle.DestroyInstance))]
    internal static class EnemyLootExperienceExitPatch
    {
        private static void Postfix() => EnemyLootExperience.Reset();
    }
}
