// Original Job Fair extension. Stock assets and job-level tables stay on disk unchanged.
using System;
using HarmonyLib;
using Last.Data.Master;
using Last.Management;

namespace ProjectBlues.Runtime
{
    internal static class EquipBellsData
    {
        internal const int ContentId = 1691;
        internal const int AbilityId = 1176;
        internal const int GeomancerJobId = 10;
        internal const int Threshold = 5;
        internal const string NameKey = "JOB_FAIR_EQUIP_BELLS_NAME";
        internal const string DescriptionKey = "JOB_FAIR_EQUIP_BELLS_DESCRIPTION";
        internal static bool Ready { get; private set; }
        internal static bool Failed { get; private set; }
        private static IntPtr ownedContentList;
        private static IntPtr ownedAbilityList;

        internal static void RequireReady()
        {
            if (!Ready || Failed)
                throw new InvalidOperationException("Equip Bells master data was not initialized. Close FFV and report the log.");
        }

        // UpdateParse calls this cache builder after every master table has parsed.
        // SetupMasterAccessCache itself is inlined at that call site in build 20265087.
        internal static void Install()
        {
            Ready = false;
            if (Failed) return;
            var manager = MasterManager.Instance;
            if (manager == null) throw new InvalidOperationException("Master manager unavailable.");
            var content = manager.GetList<Content>();
            var abilities = manager.GetList<Ability>();
            if (content == null || abilities == null)
                throw new InvalidOperationException("Ability master tables unavailable.");

            bool owned = ownedContentList == content.Pointer && ownedAbilityList == abilities.Pointer;
            if (owned && content.ContainsKey(ContentId) && abilities.ContainsKey(AbilityId))
            {
                Validate(content[ContentId], abilities[AbilityId]);
                return;
            }
            if (content.ContainsKey(ContentId) || abilities.ContainsKey(AbilityId))
                throw new InvalidOperationException("Equip Bells IDs collide with another data modification.");
            var reference = abilities[60]; // Native Equip Harps passive shape.
            if (content[326].TypeId != 4 || content[326].TypeValue != 60 ||
                reference.TypeId != 13 || reference.SystemId != 2 || reference.UseJobGroupId != 1)
                throw new InvalidOperationException("Native equipment ability template changed.");

            var ability = new Ability("1176,1176,0,0,13,0,0,2,0,0,0,0,0,0,100,0,1,0,5,5,5,0,1,0,0,0,0,0,0,0,0,0,0,0,None,0,0,0");
            var entry = new Content("1691,JOB_FAIR_EQUIP_BELLS_NAME,None,JOB_FAIR_EQUIP_BELLS_DESCRIPTION,0,4,1176");
            Validate(entry, ability);
            // Both records are constructed and checked before either dictionary changes.
            abilities.Add(AbilityId, ability);
            try { content.Add(ContentId, entry); }
            catch { abilities.Remove(AbilityId); throw; }
            ownedContentList = content.Pointer;
            ownedAbilityList = abilities.Pointer;
        }

        internal static void VerifyCache(MasterAccessCache cache)
        {
            if (Failed) return;
            var byType = cache.ContentMasterTypeAndValueList;
            if (byType == null || !byType.ContainsKey(4) || !byType[4].ContainsKey(AbilityId) ||
                byType[4][AbilityId].Id != ContentId)
                throw new InvalidOperationException("Equip Bells is missing from the native content cache.");
            Ready = true;
            Plugin.Logger.LogInfo("Equip Bells registered: content 1691 / ability 1176; bonus at 5 Geomancer ABP.");
        }

        private static void Validate(Content content, Ability ability)
        {
            if (content.Id != ContentId || content.TypeId != 4 || content.TypeValue != AbilityId ||
                content.MesIdName != NameKey || content.MesIdDescription != DescriptionKey ||
                ability.Id != AbilityId || ability.TypeId != 13 || ability.SystemId != 2)
                throw new InvalidOperationException("Equip Bells master record validation failed.");
        }

        internal static void Fail(Exception exception)
        {
            if (Failed) return; // Retain the original error instead of cascading readiness errors.
            Failed = true;
            Ready = false;
            Plugin.Logger.LogError(exception);
            SetupView.Fail("Equip Bells encountered an error. Please close FFV and report the log.");
        }

        internal static string Message(string key)
        {
            if (key == NameKey) return "Equip Bells";
            if (key == DescriptionKey) return "Allows bells to be equipped.";
            return null;
        }
    }

    [HarmonyPatch(typeof(MasterAccessCache), nameof(MasterAccessCache.CreateContentMasterTypeAndValueList))]
    internal static class EquipBellsMasterPatch
    {
        private static void Prefix()
        {
            try { EquipBellsData.Install(); }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
        private static void Postfix(MasterAccessCache __instance)
        {
            try { EquipBellsData.VerifyCache(__instance); }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }

    // These two unique keys use English fallback in every language. No stock text changes.
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetMessage), new Type[] { typeof(string), typeof(bool) })]
    internal static class EquipBellsMessagePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            string message = EquipBellsData.Message(__0);
            if (message == null) return true;
            __result = message;
            return false;
        }
    }
}
