// Remixed can revisit worlds/layers in orders the native story never expects.
// Keep the GPS selection on the loaded overworld without changing story flags.
using System;
using Last.Data.Master;
using Last.Interpreter;
using Last.Management;
using Last.Map;

namespace ProjectBlues.Runtime
{
    internal static class WorldMapOverlayRuntime
    {
        private static bool disabled;
        internal static int DefaultGps(int map) => map == 1 ? 1 : map == 12 ? 2
            : map == 18 ? 3 : map == 19 ? 4 : map == 29 ? 5 : 0;

        internal static void Refresh(FieldMap field)
        {
            var run = CampaignRuntime.EventRun;
            if (disabled || !CampaignContract.UsesWorldTravel(run) || RunSession.Loading != null
                || RunSession.Pending != null || SetupView.FatalError != null) return;
            try
            {
                if (field == null || !field.isActiveAndEnabled || !field.gameObject.activeInHierarchy
                    || !field.CheckCurrentState(SubSceneManagerMainGame.State.Player) || !field.IsPlayable()
                    || field.fieldController == null || field.mapManager?.CurrentMapModel == null) return;
                var model = field.mapManager.CurrentMapModel;
                int map = model.mapId, target = DefaultGps(map);
                if (target == 0 || !model.IsAreaTypeWorld || DataStorage.Instance.currentMapId != map
                    || WorldTravelAnchor.WorldFromMap(map) != WorldTravelRuntime.EventWorld(run)) return;
                var ui = MapUIManager.Instance;
                var common = ui?.GetGpsCommonData();
                if (common == null) return; // Native UI initialization has not completed yet.
                var manager = MasterManager.Instance;
                var rows = manager?.GetList<Gps>();
                if (rows == null) return;
                int previous = common.GpsId;
                // Preserve all valid native variants (changed terrain, landmarks).
                if (rows.ContainsKey(previous) && rows[previous] != null && rows[previous].MapId == map) return;
                if (!rows.ContainsKey(target) || rows[target] == null || rows[target].Id != target || rows[target].MapId != map)
                    throw new InvalidOperationException("Native world-map GPS destination differs from the reviewed table.");
                // Native SetGpsId caches the target identity/size and reloads the
                // overlay, full map and icons. Never replace textures or save data.
                field.fieldController.Last_Map_IMapAccessor_SetGpsId(target);
                if (common.GpsId != target) throw new InvalidOperationException("Native world-map GPS selection did not change.");
                Plugin.Logger.LogInfo("World map refreshed: map " + map + ", GPS " + previous + " -> " + target);
            }
            catch (Exception error)
            {
                disabled = true;
                Plugin.Logger.LogError("World map refresh stopped: " + error);
                SetupView.Notify("World map could not refresh. Please report the log after closing FFV.");
            }
        }
    }
}
