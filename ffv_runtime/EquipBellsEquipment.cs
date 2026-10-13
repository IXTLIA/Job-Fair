// Original Job Fair extension for the FFV-specific active passive slots.
using System;
using HarmonyLib;
using Last.Data.Master;
using Last.Data.User;
using Ff5Equipment = Serial.FF5.Management.EquipmentAbilityData;
using Ff5Jobs = Serial.FF5.Management.JobInfomationData;

namespace ProjectBlues.Runtime
{
    internal static class EquipBellsEquipment
    {
        // FFV's menu SetJobAbility writes Content IDs into this active-slot list.
        // LearningAbilitys holds learned abilities and must never confer permission.
        internal static bool IsAssigned(OwnedCharacterData character)
        {
            if (character == null || character.EquipmentAbilitys == null) return false;
            return character.EquipmentAbilitys.Contains(EquipBellsData.ContentId);
        }

        internal static bool GrantsPermission(OwnedCharacterData character, int itemContentId)
        {
            if (EquipBellsData.Failed || !IsAssigned(character)) return false;
            EquipBellsData.RequireReady();
            // These are the pinned item Content IDs, distinct from weapon IDs.
            if (itemContentId < 168 || itemContentId > 171) return false;
            var manager = MasterManager.Instance;
            if (manager == null) throw new InvalidOperationException("Equipment master manager unavailable.");
            var contents = manager.GetList<Content>();
            var weapons = manager.GetList<Weapon>();
            if (contents == null || weapons == null || !contents.ContainsKey(itemContentId))
                throw new InvalidOperationException("Bell item master data unavailable.");
            var content = contents[itemContentId];
            int expectedWeaponId = itemContentId - 58;
            if (content == null || content.TypeId != 2 || content.TypeValue != expectedWeaponId
                || !weapons.ContainsKey(expectedWeaponId))
                throw new InvalidOperationException("Pinned bell content mapping changed.");
            var weapon = weapons[expectedWeaponId];
            if (weapon == null || weapon.Id != expectedWeaponId || weapon.CategoryType != 17)
                throw new InvalidOperationException("Pinned bell weapon category changed.");
            return true;
        }
    }

    // The generic EquipUtility.CanEquippedByAbility is a shared native false stub.
    // This concrete FFV override is reached by equipment filters and shop previews.
    [HarmonyPatch(typeof(Ff5Jobs), nameof(Ff5Jobs.CanEquipped), new Type[] { typeof(OwnedCharacterData), typeof(int) })]
    internal static class EquipBellsPermissionPatch
    {
        private static void Postfix(OwnedCharacterData __0, int __1, ref bool __result)
        {
            if (__result) return;
            try { if (EquipBellsEquipment.GrantsPermission(__0, __1)) __result = true; }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }

    // Both native keyboard/controller and touch menus use this classifier when
    // comparing the old/new assigned abilities before equipment revalidation.
    [HarmonyPatch(typeof(Ff5Equipment), nameof(Ff5Equipment.IsChangeEquipmentAfterChangeAbility), new Type[] { typeof(int) })]
    internal static class EquipBellsChangedEquipmentPatch
    {
        private static void Postfix(int __0, ref bool __result)
        {
            if (__0 != EquipBellsData.ContentId || EquipBellsData.Failed) return;
            try { EquipBellsData.RequireReady(); __result = true; }
            catch (Exception exception) { EquipBellsData.Fail(exception); }
        }
    }
}
