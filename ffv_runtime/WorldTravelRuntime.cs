// Owned Remixed travel through the native map service; commit only after arrival.
using System;
using HarmonyLib;
using Last.Interpreter;
using Last.Management;
using Last.Map;
using UnityEngine;
using VehicleInstruction = Last.Interpreter.Instructions.External.Vehicle;

namespace ProjectBlues.Runtime
{
    internal static class WorldTravelRuntime
    {
        private static FieldMap field;
        private static WorldTravelTransaction pending;
        private static LoadData dispatchedLoad;
        private static bool dispatching;
        internal static bool IsBusy => pending != null;
        internal static bool IsMenuOpen => pending != null && pending.Phase == WorldTravelTransaction.Stage.Menu;
        internal static int CurrentWorld => RunSession.Active?.WorldState?.ActiveWorld ?? 0;
        internal static string Status => pending == null ? "Warp Shard" : IsMenuOpen ? "Choose a world" : "Travelling...";
        // Entry scripts compile before the destination is committed to the save.
        // Only this owned transition may supply that temporary world context.
        internal static int EventWorld(RunMetadata run)
        {
            if (!CampaignContract.UsesWorldTravel(run)) throw new InvalidOperationException("No owned world context.");
            if (pending == null || pending.Phase == WorldTravelTransaction.Stage.Menu) return run.WorldState.ActiveWorld;
            RequireOwnership();
            if (!System.Object.ReferenceEquals(run, pending.Run) || pending.Target == null)
                throw new InvalidOperationException("A destination event belongs to another run.");
            return pending.Target.World;
        }
        internal static bool IsActionVisible
        {
            get
            {
                try { return !IsBusy && CampaignContract.UsesWorldTravel(RunSession.Active) && field != null
                    && field.isActiveAndEnabled && field.gameObject.activeInHierarchy
                    && field.CheckCurrentState(SubSceneManagerMainGame.State.Player) && field.IsPlayable(); }
                catch { return false; }
            }
        }
        internal static bool TryOpen(out string reason)
        {
            reason = null;
            try
            {
                if (IsBusy) throw new InvalidOperationException("World travel is already open or in progress.");
                RequireSafeField();
                pending = new WorldTravelTransaction(RunSession.Active, DataStorage.Instance.currentMapId, field.Pointer.ToInt64(), Environment.TickCount64);
                WorldTravelController.GuardRelease();
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
        }
        internal static void CancelMenu()
        {
            if (pending == null) return;
            pending.Cancel(); pending = null;
        }
        internal static void RequireIdleForLoad()
        {
            // A menu has no native effects and can be dismissed safely. An
            // in-flight map/transport mutation cannot be cleared on load/title.
            if (IsMenuOpen) CancelMenu();
            if (IsBusy) throw new InvalidOperationException("World travel has not completed; close FFV after a failed transition.");
        }
        internal static void RequireIdleForReward()
        {
            if (IsBusy) throw new InvalidOperationException("A reward cannot begin during world travel.");
        }
        internal static void SelectWorld(int target)
        {
            if (!IsMenuOpen) throw new InvalidOperationException("Open the Warp Shard first.");
            pending.RequireOwner(RunSession.Active, field.Pointer.ToInt64());
            RequireSafeField(true);
            pending.Dispatch(target, Environment.TickCount64);
            try
            {
                dispatchedLoad = new LoadData();
                dispatchedLoad.NextMapData(pending.Target.Map, pending.Target.Point, ViewType.Normal);
                if (dispatchedLoad.MapId != pending.Target.Map || dispatchedLoad.Point != pending.Target.Point)
                    throw new InvalidOperationException("Native destination construction changed.");
                dispatching = true;
                field.fieldController.eventHandle.EventGotoMap(dispatchedLoad, false, false);
                Plugin.Logger.LogInfo("Warp Shard dispatched: world " + target + ", run " + pending.Run.RunId);
            }
            catch (Exception error) { Fail(error); throw; }
            finally { dispatching = false; }
        }
        private static void RequireSafeField(bool ownedMenu = false)
        {
            var run = RunSession.Active;
            if (!CampaignContract.UsesWorldTravel(run) || RunSession.Loading != null || RunSession.Pending != null
                || SetupView.FatalError != null || RemixedJobRuntime.IsBusy || RenameSession.IsActive
                || (!ownedMenu && !RunSession.CanSave)) throw new InvalidOperationException("The current run is not ready for world travel.");
            if (field == null || !field.isActiveAndEnabled || !field.gameObject.activeInHierarchy || field.fieldController == null || field.mapManager == null
                || !field.CheckCurrentState(SubSceneManagerMainGame.State.Player) || !field.IsPlayable() || !field.IsGameUiOperable())
                throw new InvalidOperationException("Use the Warp Shard while standing still in the field.");
            var c = field.fieldController; var model = field.mapManager.CurrentMapModel;
            int map = DataStorage.Instance.currentMapId;
            if (!WorldTravelAnchor.IsSurfaceMap(map) || WorldTravelAnchor.WorldFromMap(map) != run.WorldState.ActiveWorld
                || model == null || model.mapId != map || !model.IsAreaTypeWorld)
                throw new InvalidOperationException("Use the Warp Shard on the overworld.");
            if (!Stable(c) || Integrator.Instance.IsMainCoreAlive() || Integrator.Instance.IsPuppetCoreAlive()
                || !String.IsNullOrEmpty(field.requestItemScriptName))
                throw new InvalidOperationException("Wait until movement, dialogue and vehicle changes finish.");
        }
        private static bool Stable(FieldController c) => c != null && c.player != null && c.eventHandle != null
            && !c.eventHandle.IsRunningAction && !c.isPlayerFootProcessing && !c.player.IsMoving && !c.player.IsAutoMoving
            && !c.IsDuringTakeoffAndLanding && !c.IsDuringSubmarineDivingAndSurface;

        internal static bool CanOpenController()
        {
            try { RequireSafeField(); return true; }
            catch { return false; }
        }

        // Called before native player input, also retaining a current FieldMap
        // without unsafe global-object searches or stale title-screen assumptions.
        internal static bool BeforePlayerUpdate(FieldMap instance)
        {
            field = instance;
            // Runs before player input, including ordinary dive/surface changes
            // and loading an existing save with a stale GPS selection.
            WorldMapOverlayRuntime.Refresh(instance);
            AdvanceArrivalTransport();
            Tick();
            bool inputReleased = WorldTravelController.BeforePlayerInput();
            return !IsBusy && inputReleased;
        }
        private static void AdvanceArrivalTransport()
        {
            if (pending == null || pending.Phase != WorldTravelTransaction.Stage.AwaitingControl) return;
            try
            {
                RequireOwnership();
                RequireMap(pending.Target.Map);
                if (!field.CheckCurrentState(SubSceneManagerMainGame.State.Player)) return;
                var c = field.fieldController;
                // The suppressed native player update normally advances these
                // animations through FieldController.UpdateController. Call only
                // their native visual/state updates, never movement or input.
                // Keep this outside Tick: the UI also calls Tick each frame.
                if (c.MapViewType == ViewType.Bird && c.birdViewController != null
                    && c.birdViewController.IsDuringTakeoffAndLanding)
                    c.birdViewController.UpdateController();
                if (c.player.IsDuringTakeoffAndLanding)
                    c.player.UpdateTransformLowFlying();
            }
            catch (Exception error) { Fail(error); }
        }
        internal static bool BeforePlayerReady(FieldMap instance)
        {
            field = instance;
            if (pending == null) return true;
            try
            {
                if (pending.Phase != WorldTravelTransaction.Stage.Dispatched)
                    throw new InvalidOperationException("Unexpected return to player control during travel.");
                RequireOwnership();
                RequireMap(pending.Target.Map);
                pending.Arrive(DataStorage.Instance.currentMapId);
                var c = field.fieldController; var a = pending.Target;
                var position = c.WorldPositionToCellPosition(c.player.transform.position);
                if (!WorldTravelTransaction.Near(position.x, a.X) || !WorldTravelTransaction.Near(position.y, a.Y))
                    throw new InvalidOperationException("Native spawn point differs from the fixed arrival.");
                var directions = VehicleInstruction.directionList;
                if (directions == null || directions.Length <= a.DirectionIndex)
                    throw new InvalidOperationException("Native vehicle direction table is unavailable.");
                c.TransportationEnabled(a.Vehicle, true, a.Map,
                    new Il2CppSystem.Nullable<Vector2>(new Vector2(a.X, a.Y)),
                    new Il2CppSystem.Nullable<LayerSetting.Layers>((LayerSetting.Layers)c.player.gameObject.layer),
                    new Il2CppSystem.Nullable<int>(directions[a.DirectionIndex]), true);
                c.ChangeTransportation(a.Vehicle, true, a.Background, ViewType.Normal);
                Plugin.Logger.LogInfo("Warp Shard boarding: world " + a.World + ", map " + a.Map
                    + ", vehicle " + a.Vehicle + ", view " + c.MapViewType);
                return true; // Native initialization restores its own control/UI.
            }
            catch (Exception error) { Fail(error); return false; }
        }
        internal static void Tick()
        {
            if (pending == null || IsMenuOpen || pending.Phase == WorldTravelTransaction.Stage.Failed) return;
            try
            {
                RequireOwnership();
                if (Environment.TickCount64 - pending.StartedAt > 120000)
                    throw new InvalidOperationException("Native world travel did not complete in time. " + ArrivalDiagnostic());
                if (pending.Phase != WorldTravelTransaction.Stage.AwaitingControl) return;
                RequireMap(pending.Target.Map);
                var c = field.fieldController;
                if (!field.CheckCurrentState(SubSceneManagerMainGame.State.Player) || !field.IsPlayable()
                    || !field.IsGameUiOperable() || !Stable(c) || Integrator.Instance.IsMainCoreAlive() || Integrator.Instance.IsPuppetCoreAlive()) return;
                // The UI may Tick before the native player prefix this frame.
                // Refresh before the arrival snapshot in either update order.
                WorldMapOverlayRuntime.Refresh(field);
                var transport = c.transportation?.CurrentTransportation;
                if (transport == null || !transport.Enable) throw new InvalidOperationException("Arrival transport is unavailable.");
                var p = c.WorldPositionToCellPosition(c.player.transform.position);
                var arrived = pending.Commit(DataStorage.Instance.currentMapId, transport.Id, transport.StayMapId,
                    (int)c.MapViewType, p.x, p.y, true);
                // Refresh the normal native save snapshot before publishing the
                // owned world. This must still run while saves remain blocked.
                c.SaveSetupParameters(false);
                var saved = SaveMapManager.Instance.SaveData;
                if (saved == null || saved.MapId != pending.Target.Map || saved.TransportationId != pending.Target.Vehicle)
                    throw new InvalidOperationException("Native saved arrival does not match the verified transport.");
                RunSession.Active = arrived;
                Plugin.Logger.LogInfo("Warp Shard arrival committed: world " + arrived.WorldState.ActiveWorld + ", run " + arrived.RunId);
                pending = null; dispatchedLoad = null;
                SetupView.Notify("World " + arrived.WorldState.ActiveWorld);
            }
            catch (Exception error) { Fail(error); }
        }
        private static void RequireOwnership()
        {
            if (pending == null || field == null || !field.isActiveAndEnabled || !field.gameObject.activeInHierarchy
                || RunSession.Loading != null || RunSession.Pending != null || SetupView.FatalError != null)
                throw new InvalidOperationException("World travel lost its native run context.");
            pending.RequireOwner(RunSession.Active, field.Pointer.ToInt64());
        }
        private static string ArrivalDiagnostic()
        {
            try
            {
                var c = field?.fieldController;
                return "map=" + DataStorage.Instance.currentMapId + ", view=" + c?.MapViewType
                    + ", playable=" + field?.IsPlayable() + ", UI=" + field?.IsGameUiOperable()
                    + ", stable=" + Stable(c);
            }
            catch { return "Arrival diagnostics unavailable."; }
        }
        private static void RequireMap(int map)
        {
            if (field.fieldController == null || field.fieldController.player == null || field.mapManager?.CurrentMapModel == null
                || DataStorage.Instance.currentMapId != map || field.mapManager.CurrentMapModel.mapId != map
                || !field.mapManager.CurrentMapModel.IsAreaTypeWorld)
                throw new InvalidOperationException("Native map model and arrival identity disagree.");
        }
        internal static bool BeforeGotoMap(LoadData load)
        {
            var run = CampaignRuntime.EventRun;
            if (!CampaignContract.UsesWorldTravel(run)) return true;
            if (load == null) throw new InvalidOperationException("Missing native destination.");
            if (IsBusy)
            {
                RequireOwnership();
                if (!dispatching || dispatchedLoad == null || load.Pointer != dispatchedLoad.Pointer
                    || pending.Phase != WorldTravelTransaction.Stage.Dispatched || load.MapId != pending.Target.Map || load.Point != pending.Target.Point)
                    throw new InvalidOperationException("An unrelated map transition overlapped world travel.");
                return true;
            }
            int destinationWorld = WorldTravelAnchor.WorldFromMap(load.MapId);
            // Map 591 is also the native W1 Ronka surface cutscene. Only its
            // W2 merge use bypasses owned travel; preserve the W1 boss route.
            if (load.MapId == 584 || (load.MapId == 591 && run.WorldState.ActiveWorld == 2)
                || (destinationWorld != 0 && destinationWorld != run.WorldState.ActiveWorld))
                throw new InvalidOperationException("A native story transition would bypass owned world travel. "
                    + "run=" + run.RunId + ", world=" + run.WorldState.ActiveWorld
                    + ", sourceMap=" + DataStorage.Instance.currentMapId + ", targetMap=" + load.MapId
                    + ", targetPoint=" + load.Point + ", script=" + Integrator.Instance?.scriptName);
            return true;
        }
        internal static void Fail(Exception error)
        {
            pending?.Fail();
            Plugin.Logger.LogError(error);
            SetupView.Fail("Job Fair stopped an incomplete world transition. Close FFV and report the log.");
        }
    }
    [HarmonyPatch(typeof(FieldMap), nameof(FieldMap.UpdatePlayerStatePlay), new Type[] { })]
    internal static class WorldTravelPlayerPatch
    {
        private static bool Prefix(FieldMap __instance) => WorldTravelRuntime.BeforePlayerUpdate(__instance);
    }
    [HarmonyPatch(typeof(FieldMap), nameof(FieldMap.InitPlayerStatePlay), new Type[] { })]
    internal static class WorldTravelArrivalPatch
    {
        private static bool Prefix(FieldMap __instance) => WorldTravelRuntime.BeforePlayerReady(__instance);
        private static Exception Finalizer(Exception __exception)
        { if (__exception != null && WorldTravelRuntime.IsBusy) WorldTravelRuntime.Fail(__exception); return __exception; }
    }
    [HarmonyPatch(typeof(EventProcedure), nameof(EventProcedure.EventGotoMap), new Type[] { typeof(LoadData), typeof(bool), typeof(bool) })]
    internal static class WorldTravelGatePatch
    {
        private static bool Prefix(LoadData __0)
        { try { return WorldTravelRuntime.BeforeGotoMap(__0); } catch (Exception error) { WorldTravelRuntime.Fail(error); return false; } }
        private static Exception Finalizer(Exception __exception)
        { if (__exception != null && WorldTravelRuntime.IsBusy) WorldTravelRuntime.Fail(__exception); return __exception; }
    }
}
