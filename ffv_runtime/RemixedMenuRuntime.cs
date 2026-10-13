// The native predicate gates only Job and Ability on Wind-complete flag16.
// Override that UI predicate for owned playable Remixed saves; retain flag16
// for the native Wind dungeon, Wing Raptor and crystal-event progression.
using System;
using HarmonyLib;
using Last.Defaine;

namespace ProjectBlues.Runtime
{
    internal static class RemixedMenuRuntime
    {
        internal static bool ShouldEnable(MenuCommandId command)
        {
            var run = RunSession.Active;
            return run != null && run.SchemaVersion == 3 && CampaignContract.IsRemixedMode(run.Mode)
                && CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)
                && RunSession.Loading == null && RunSession.Pending == null && !RunSession.LoadFailed
                && SetupView.FatalError == null
                && (command == MenuCommandId.Job || command == MenuCommandId.Ability);
        }
    }

    [HarmonyPatch(typeof(Serial.FF5.Management.SpcialFlagData), nameof(Serial.FF5.Management.SpcialFlagData.IsUsableCommand),
        new Type[] { typeof(MenuCommandId) })]
    internal static class RemixedMenuPatch
    {
        private static void Postfix(MenuCommandId __0, ref bool __result)
        {
            if (RemixedMenuRuntime.ShouldEnable(__0)) __result = true;
        }
    }
}
