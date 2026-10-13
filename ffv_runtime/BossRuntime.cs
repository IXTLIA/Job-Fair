// Encounter-local boss replacement. No master table rows are mutated.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Data.Master;
using UnityEngine;

namespace ProjectBlues.Runtime
{
    internal static class BossRuntime
    {
        private sealed class Encounter
        {
            internal InstantiateManager Manager;
            internal string RunId, PlacementHash;
            internal JsonElement Source, Destination;
            internal int DestinationFormation, SourceFormation;
            internal readonly Dictionary<int, TextAsset> Scripts = new Dictionary<int, TextAsset>();
            // Retain parameters as well as pointers: native address reuse must
            // never cause a new enemy or a retry to skip scaling.
            internal readonly Dictionary<IntPtr, EnemyCharacterParameter> Parameters = new Dictionary<IntPtr, EnemyCharacterParameter>();
        }
        private static readonly Dictionary<IntPtr, Encounter> encounters = new Dictionary<IntPtr, Encounter>();
        internal static void Initialize() { _ = BossCatalog.Shared; Reset(); }
        private static void Release(Encounter encounter)
        {
            foreach (var script in encounter.Scripts.Values) UnityEngine.Object.Destroy(script);
            encounter.Scripts.Clear(); encounter.Parameters.Clear();
        }
        private static void Remove(InstantiateManager manager)
        {
            if (manager != null && encounters.TryGetValue(manager.Pointer, out var encounter))
            { Release(encounter); encounters.Remove(manager.Pointer); }
        }
        internal static void Reset()
        {
            BossMechanics.End();
            foreach (var encounter in encounters.Values) Release(encounter);
            encounters.Clear();
        }
        private static bool MatchesRun(Encounter encounter)
        {
            var run = CampaignRuntime.EventRun;
            return BossCatalog.Applies(run) && run.RunId == encounter.RunId && run.PlacementHash == encounter.PlacementHash;
        }
        private static Encounter ForManager(InstantiateManager manager)
        {
            if (manager == null || !encounters.TryGetValue(manager.Pointer, out var encounter)) return null;
            if (!MatchesRun(encounter)) { Remove(manager); return null; }
            return encounter;
        }
        private static Encounter Current()
        {
            if (!BossCatalog.Applies(CampaignRuntime.EventRun)) return null;
            var plug = BattlePlugManager.Instance();
            return plug == null ? null : ForManager(plug.InstantiateManager);
        }
        internal static bool TryCurrent(out JsonElement source, out JsonElement destination)
        {
            var encounter = Current();
            source = encounter == null ? default : encounter.Source;
            destination = encounter == null ? default : encounter.Destination;
            return encounter != null;
        }
        internal static object CurrentToken => Current();
        internal static bool IsScaledParameter(CharacterParameterBase parameter)
        {
            var encounter = Current();
            return parameter != null && encounter != null && encounter.Parameters.ContainsKey(parameter.Pointer);
        }

        internal static void PrepareInitial(InstantiateManager manager)
        {
            if (manager == null) throw new InvalidOperationException("Boss initialization has no native manager.");
            var run = CampaignRuntime.EventRun;
            if (!BossCatalog.Applies(run) && !EnemyLootExperience.Applies(run))
            {
                EnemyLootExperience.Track(manager, 0);
                Remove(manager);
                return;
            }
            // The native constructor builds the first party directly and never
            // calls SetMonstarParty. Init is the first non-constructor boundary
            // before enemy/AI/battle assets are loaded, with a live manager.
            var party = manager.GetMonsterParty();
            if (party == null) throw new InvalidOperationException("Boss initialization has no native formation.");
            int original = party.Id, formation = original;
            EnemyLootExperience.Track(manager, original);
            Prepare(manager, ref formation);
            // The existing setter patches see a retry of SourceFormation and
            // retain the original destination instead of randomizing twice.
            if (formation != original) manager.SetMonstarParty(formation);
        }

        internal static void Prepare(InstantiateManager manager, ref int formation)
        {
            var run = CampaignRuntime.EventRun;
            if (!BossCatalog.Applies(run)) { Remove(manager); return; }
            if (RunSession.Loading != null) throw new InvalidOperationException("Boss encounter requested during an incomplete load.");
            var prior = ForManager(manager);
            if (prior != null && formation == prior.SourceFormation) return; // Same native loader retry.
            // Some internal forms (Gargoyles 601) also appear as public aliases.
            // An active donor's phase must win over public location lookup.
            if (prior != null && prior.Source.TryGetProperty("internal_formation_ids", out var internalIds))
                foreach (var id in internalIds.EnumerateArray()) if (id.GetInt32() == formation) return;
            if (!BossCatalog.Shared.TryFormation(formation, out var destination))
            {
                // A source encounter may change the formation internally, for
                // example the end of the Golem rescue. Such changes are not new
                // randomized locations and retain their current stat context.
                Remove(manager); return;
            }
            BossCatalog.Validate(run);
            var placement = run.FindPlacement("native_boss/" + destination.GetProperty("boss_id").GetString());
            var source = BossCatalog.Shared.Get(placement.RewardId);
            var encounter = new Encounter { Manager = manager, RunId = run.RunId, PlacementHash = run.PlacementHash,
                Source = source, Destination = destination, DestinationFormation = formation,
                SourceFormation = source.GetProperty("formation_id").GetInt32() };
            Remove(manager);
            encounters[manager.Pointer] = encounter;
            formation = encounter.SourceFormation;
            Plugin.Logger.LogInfo("Boss encounter " + destination.GetProperty("name").GetString() + " -> "
                + source.GetProperty("name").GetString() + "; destination " + encounter.DestinationFormation
                + ", source " + formation + ", seed " + run.Seed);
        }

        internal static void Scale(EnemyCharacterParameter parameter, Monster monster, int? preservedHp = null)
        {
            var encounter = Current();
            if (encounter == null) return;
            int id = monster.Id;
            if (!BossCatalog.HasMonster(encounter.Source, id))
                throw new InvalidOperationException("Assigned boss created an unaudited monster component: " + id);
            if (encounter.Parameters.ContainsKey(parameter.Pointer)) throw new InvalidOperationException("Boss parameter scaled twice.");
            var native = BossCatalog.Monster(encounter.Source, id);
            if (parameter.BaseMaxHp != native.GetProperty("hp").GetInt32()
                || parameter.BaseLevel != native.GetProperty("lv").GetInt32()
                || parameter.BaseAttack != native.GetProperty("attack").GetInt32()
                || parameter.BaseMagic != native.GetProperty("magic").GetInt32()
                || parameter.BaseDamageDiameter != native.GetProperty("attack_count").GetInt32()
                || parameter.BaseDefense != native.GetProperty("defense").GetInt32()
                || parameter.BaseAbilityDefense != native.GetProperty("ability_defense").GetInt32()
                || parameter.BaseAgility != native.GetProperty("agility").GetInt32())
                throw new InvalidOperationException("Native boss parameters differ from the pinned catalog.");
            // All calculations finish before any native field changes. Master
            // Monster rows and their status/element/AI properties remain native.
            int hp = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "hp");
            int level = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "lv");
            int attack = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "attack");
            int magic = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "magic");
            int multiplier = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "attack_count");
            int defense = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "defense");
            int abilityDefense = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "ability_defense");
            int agility = BossCatalog.ScaleStat(encounter.Source, encounter.Destination, id, "agility");
            encounter.Parameters.Add(parameter.Pointer, parameter);
            parameter.BaseMaxHp = hp; parameter.CurrentHP = preservedHp ?? hp;
            parameter.BaseLevel = level; parameter.BaseAttack = attack; parameter.BaseMagic = magic;
            parameter.BaseDamageDiameter = multiplier;
            parameter.BaseDefense = defense; parameter.BaseAbilityDefense = abilityDefense; parameter.BaseAgility = agility;
            BossMechanics.SawMonster(id);
            Plugin.Logger.LogInfo("Scaled boss component " + id + " HP " + hp + ", level " + level
                + ", defense " + parameter.ConfirmedDefense() + ", magic defense " + parameter.ConfirmedAbilityDefense());
        }

        internal static void ScaleUnit(BattleEnemyData unit, int? preservedHp = null)
        {
            if (Current() == null) return;
            var monster = unit == null ? null : unit.GetMonster();
            var parameter = unit?.BattleUnitDataInfo?.Parameter?.TryCast<EnemyCharacterParameter>();
            if (monster == null || parameter == null)
                throw new InvalidOperationException("Boss construction produced an unavailable enemy parameter.");
            // Transformations capture already-scaled encounter HP before the
            // native constructor resets their maximum. Reapply that snapshot
            // after scaling: the earlier native setter may have clamped it.
            // Write HP once: the native setter also reconciles HP-related state.
            Scale(parameter, monster, preservedHp);
        }

        internal static void ScaleTransformation(DeformationEnemyFunction function)
        {
            if (Current() == null) return;
            var targets = function?.battleActData?.TargetUnitDatas;
            if (targets == null)
                throw new InvalidOperationException("Boss transformation has no native target list.");
            // Native PrevCalc captures the first target's HP and UpdateParameter
            // applies that same snapshot to every target. Preserve that rule.
            for (int index = 0; index < targets.Count; index++)
                ScaleUnit(targets[index]?.TryCast<BattleEnemyData>(), function.tempHp);
        }

        internal static TextAsset PrepareAi(InstantiateManager manager, int monsterId, TextAsset original)
        {
            var encounter = ForManager(manager);
            if (encounter == null) return original;
            if (original == null) throw new InvalidOperationException("Assigned boss AI asset missing.");
            if (!BossCatalog.HasMonster(encounter.Source, monsterId))
                throw new InvalidOperationException("Assigned boss requested an unaudited AI component.");
            if (encounter.Scripts.TryGetValue(monsterId, out var cached)) return cached;
            string text = BossScripts.RewriteAi(original.text, encounter.Source, encounter.Destination, monsterId);
            var replacement = new TextAsset(text);
            encounter.Scripts.Add(monsterId, replacement);
            return replacement;
        }

        internal static void VerifyBattleScript(InstantiateManager manager, TextAsset script)
        {
            var encounter = ForManager(manager);
            if (encounter == null) return;
            string expected = encounter.Source.GetProperty("battle_script").GetProperty("sha256").GetString();
            if (script != null)
            {
                string actual = BossScripts.Hash(script.text);
                if (actual == expected) return;
                if (encounter.Source.TryGetProperty("internal_battle_scripts", out var scripts))
                    foreach (var item in scripts.EnumerateArray()) if (actual == item.GetProperty("sha256").GetString()) return;
            }
            throw new InvalidOperationException("Assigned boss battle script differs from the pinned catalog.");
        }

        internal static void ClearBattle()
        {
            BossMechanics.End();
            var encounter = Current();
            if (encounter != null) Remove(encounter.Manager);
        }
        internal static void Fail(Exception exception)
        {
            Plugin.Logger.LogError("Boss shuffler stopped: " + exception);
            SetupView.Fail("Job Fair stopped an incomplete boss encounter. Close FFV and report the log.");
        }
    }

    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.Init), new Type[] { })]
    internal static class BossInitialFormationPatch
    {
        internal static bool Prefix(InstantiateManager __instance)
        {
            try { BossRuntime.PrepareInitial(__instance); return true; }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
    }
    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.SetMonstarParty))]
    internal static class BossFormationPatch
    {
        private static bool Prefix(InstantiateManager __instance, ref int __0)
        {
            try { BossRuntime.Prepare(__instance, ref __0); return true; }
            catch (Exception exception) { BossRuntime.Fail(exception); return false; }
        }
    }
    [HarmonyPatch(typeof(BattleStatusControl), nameof(BattleStatusControl.CreateBattleEnemyData))]
    internal static class BossEnemyConstructionPatch
    {
        internal static void Postfix(BattleEnemyData __result)
        {
            try { BossRuntime.ScaleUnit(__result); }
            catch (Exception exception) { BossRuntime.Fail(exception); }
        }
    }
    [HarmonyPatch(typeof(DeformationEnemyFunction), nameof(DeformationEnemyFunction.UpdateParameter))]
    internal static class BossFormConstructionPatch
    {
        internal static void Postfix(DeformationEnemyFunction __instance)
        {
            try { BossRuntime.ScaleTransformation(__instance); }
            catch (Exception exception) { BossRuntime.Fail(exception); }
        }
    }
    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.GetEnemyAIScript))]
    internal static class BossAiPatch
    {
        private static void Postfix(InstantiateManager __instance, int __0, ref TextAsset __result)
        {
            try { __result = BossRuntime.PrepareAi(__instance, __0, __result); }
            catch (Exception exception) { __result = null; BossRuntime.Fail(exception); }
        }
    }
    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.GetScript))]
    internal static class BossBattleScriptPatch
    {
        private static void Postfix(InstantiateManager __instance, TextAsset __result)
        {
            try { BossRuntime.VerifyBattleScript(__instance, __result); }
            catch (Exception exception) { BossRuntime.Fail(exception); }
        }
    }
    // BattleController.OnDestroy is a shared native no-op RVA; never detour it.
    [HarmonyPatch(typeof(Last.Scene.Battle), nameof(Last.Scene.Battle.DestroyInstance))]
    internal static class BossExitPatch
    {
        private static void Prefix() { BossRuntime.ClearBattle(); }
    }
}
