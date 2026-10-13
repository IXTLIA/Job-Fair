// Actual adapter + ownership protocol against native fakes. No game execution.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using ProjectBlues.Runtime;
using Last.Map;
using Last.Interpreter;
using Last.Management;
using UnityEngine;
[assembly: TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = ".NET 6.0")]
internal static class WorldTravelRuntimeProbe
{
    private static int checks;
    private static void Check(bool yes, string why) { checks++; if (!yes) throw new Exception(why); }
    private static void Reject(Action action) { checks++; try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Unsafe transition accepted."); }
    internal static RunMetadata Read(string path)
    {
        using var d = JsonDocument.Parse(File.ReadAllText(path)); var r = d.RootElement;
        if(CampaignContract.IsPlayableRemixedCatalog(r.GetProperty("catalog_id").GetString()))
            return NextRunConfig.Parse(File.ReadAllText(path)).CreateRun("Bartz");
        if(r.GetProperty("catalog_id").GetString()!=CampaignContract.WorldTravelCatalog)
            throw new ArgumentException("Unknown world-travel adapter fixture.");
        Reject(()=>NextRunConfig.Parse(File.ReadAllText(path)));
        return new RunMetadata(3, r.GetProperty("seed").GetString(),r.GetProperty("generator").GetString(),Guid.NewGuid().ToString("N"),"Bartz",
            r.GetProperty("mode").GetString(),SaveSeedCodec.ReadSettings(r.GetProperty("settings")),r.GetProperty("catalog_id").GetString(),
            SaveSeedCodec.ReadJobs(r.GetProperty("starting_job_ids")),SaveSeedCodec.ReadPlacements(r.GetProperty("placements")),r.GetProperty("placement_hash").GetString(),
            Array.Empty<string>(),Array.Empty<string>(),WorldTravelState.Initial);
    }
    internal static RunMetadata Key(RunMetadata run,string key)
    { foreach(var p in run.Placements) if(p.RewardKind=="key" && p.RewardId==key) return run.WithClaim(p.LocationId); throw new Exception("Missing key."); }
    internal static FieldMap Reset(RunMetadata run)
    {
        foreach(string name in new[]{"field","pending","dispatchedLoad"}) typeof(WorldTravelRuntime).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null);
        RunSession.Active=run;RunSession.Pending=null;RunSession.Loading=null;SetupView.FatalError=null;SetupView.Notification=null;
        RenameSession.IsActive=false;RemixedJobRuntime.IsBusy=false;Integrator.Instance.Main=false;Integrator.Instance.Puppet=false;
        FakeFaults.Dispatch=false;FakeFaults.Transport=false;FakeFaults.Snapshot=false;
        FakeFaults.AnimateTransport=false;FakeFaults.BirdUpdate=false;FakeFaults.PlayerUpdate=false;
        int map=run==null?1:WorldTravelAnchor.ForWorld(run.WorldState.ActiveWorld).Map;
        var f=new FieldMap();f.mapManager.CurrentMapModel.mapId=map;DataStorage.Instance.currentMapId=map;
        Check(WorldTravelRuntime.BeforePlayerUpdate(f),"Unowned field update blocked.");return f;
    }
    private static void Open(FieldMap f)
    { Check(WorldTravelRuntime.IsActionVisible,"Warp action hidden on idle field.");Check(WorldTravelRuntime.TryOpen(out var why),why);Check(WorldTravelRuntime.IsMenuOpen&&!RunSession.CanSave,"Menu did not own input/save.");Check(!WorldTravelRuntime.BeforePlayerUpdate(f),"Native field input ran beneath menu."); }
    private static void Arrive(FieldMap f,WorldTravelAnchor a)
    {
        f.State=SubSceneManagerMainGame.State.Player;f.Ready=false;
        DataStorage.Instance.currentMapId=a.Map;f.mapManager.CurrentMapModel.mapId=a.Map;f.fieldController.player.transform.position=new Vector3(a.X,a.Y,0);
        Check(WorldTravelRuntime.BeforePlayerReady(f),"Pinned arrival was rejected.");
        Check(WorldTravelRuntime.IsBusy&&!RunSession.CanSave,"Arrival published before native control.");
        var source=RunSession.Active;WorldTravelRuntime.Tick();
        Check(WorldTravelRuntime.IsBusy&&!RunSession.CanSave&&Object.ReferenceEquals(source,RunSession.Active),"Tick committed before native control restoration.");
        f.Ready=true;
    }
    private static void RoundTrips(RunMetadata baseRun)
    {
        var thirdOnly=Key(baseRun,WorldTravelState.World3Key);var f=Reset(thirdOnly);Open(f);
        Check(WorldTravelRuntime.EventWorld(thirdOnly)==1,"Menu changed event world before dispatch.");
        Reject(()=>WorldTravelRuntime.SelectWorld(2));Check(WorldTravelRuntime.IsMenuOpen,"Locked destination discarded menu.");
        foreach(int target in new[]{3,1,3,1})
        {
            if(!WorldTravelRuntime.IsMenuOpen)Open(f);
            var source=RunSession.Active;WorldTravelRuntime.SelectWorld(target);
            Check(WorldTravelRuntime.IsBusy&&!WorldTravelRuntime.IsMenuOpen&&Object.ReferenceEquals(source,RunSession.Active),"Dispatch prematurely committed world.");
            Check(WorldTravelRuntime.CurrentWorld==source.WorldState.ActiveWorld&&WorldTravelRuntime.EventWorld(source)==target,"Pending entry used saved source world instead of owned destination.");
            Reject(()=>WorldTravelRuntime.EventWorld(source.WithBartzName("Other")));
            Reject(WorldTravelRuntime.RequireIdleForLoad);Reject(WorldTravelRuntime.RequireIdleForReward);
            Check(!WorldTravelRuntime.TryOpen(out _),"Reentrant menu accepted.");
            var a=WorldTravelAnchor.ForWorld(target);Arrive(f,a);WorldTravelRuntime.Tick();
            Check(SetupView.FatalError==null&&!WorldTravelRuntime.IsBusy&&RunSession.Active.WorldState.ActiveWorld==target,"Independent World3 travel failed.");
            Check(!RunSession.Active.HasKey(WorldTravelState.World2Key)&&RunSession.Active.HasKey(WorldTravelState.World3Key),"Free return consumed/invented key.");
            Check(f.fieldController.Direction==8,"Native direction table was bypassed.");
            Check(f.fieldController.PlacedLayer==9,"Native current player layer was not preserved.");
            Check(WorldTravelRuntime.EventWorld(RunSession.Active)==target,"Completed event world differs from saved destination.");
        }
        var both=Key(RunSession.Active,WorldTravelState.World2Key);f=Reset(both);
        foreach(int target in new[]{2,3,2,1})
        {Open(f);WorldTravelRuntime.SelectWorld(target);Arrive(f,WorldTravelAnchor.ForWorld(target));WorldTravelRuntime.Tick();Check(!WorldTravelRuntime.IsBusy&&RunSession.Active.WorldState.ActiveWorld==target,"All-world reversible path failed.");}
        string save=SaveSeedCodec.Attach("{\"native\":17}",RunSession.Active);SaveSeedCodec.Extract(save,out var loaded);
        Check(loaded.WorldState.ActiveWorld==1&&loaded.AcquiredKeyIds.Count==2&&loaded.PlacementHash==baseRun.PlacementHash,"Travel changed saved placement/key identity.");
        if(CampaignContract.IsPlayableRemixedCatalog(baseRun.CatalogId))
        {CampaignContract.ValidatePlayable(loaded);Check(true,"New playtest remains loadable after travel.");}
        else Reject(()=>CampaignContract.ValidatePlayable(loaded));
    }
    private static void Guards(RunMetadata run)
    {
        run=Key(run,WorldTravelState.World3Key);
        Action<FieldMap>[] unavailable={f=>f.State=SubSceneManagerMainGame.State.Battle,f=>f.isActiveAndEnabled=false,f=>f.gameObject.activeInHierarchy=false,f=>f.Ready=false,f=>f.fieldController.player.IsMoving=true,
            f=>f.fieldController.eventHandle.IsRunningAction=true,f=>f.fieldController.IsDuringTakeoffAndLanding=true,
            f=>f.fieldController.IsDuringSubmarineDivingAndSurface=true,f=>f.fieldController.isPlayerFootProcessing=true,
            f=>Integrator.Instance.Main=true,f=>Integrator.Instance.Puppet=true,f=>f.requestItemScriptName="native_item",
            f=>RunSession.Loading=new object(),f=>RemixedJobRuntime.IsBusy=true,f=>RenameSession.IsActive=true,
            f=>f.mapManager.CurrentMapModel.mapId=19,f=>DataStorage.Instance.currentMapId=30};
        foreach(var change in unavailable){var f=Reset(run);change(f);Check(!WorldTravelRuntime.TryOpen(out var reason)&&reason!=null&&!WorldTravelRuntime.IsBusy,"Unsafe field context opened travel.");}
        var menu=Reset(run);Open(menu);WorldTravelRuntime.RequireIdleForLoad();Check(!WorldTravelRuntime.IsBusy,"Effect-free menu could not close for load.");
        foreach(int bad in CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)?new[]{0,2,4}:new[]{0,1,2,4}){var f=Reset(run);Open(f);Reject(()=>WorldTravelRuntime.SelectWorld(bad));Check(WorldTravelRuntime.IsMenuOpen,"Invalid target changed menu ownership.");WorldTravelRuntime.CancelMenu();}
        for(int fault=0;fault<8;fault++)
        {
            var f=Reset(run);Open(f);var before=RunSession.Active;
            if(fault==0){FakeFaults.Dispatch=true;Reject(()=>WorldTravelRuntime.SelectWorld(3));}
            else
            {
                WorldTravelRuntime.SelectWorld(3);var a=WorldTravelAnchor.ForWorld(3);
                if(fault==1)RunSession.Active=run.WithBartzName("Changed");
                if(fault==2){var other=new FieldMap();WorldTravelRuntime.BeforePlayerUpdate(other);}
                if(fault==3)FakeFaults.Transport=true;
                if(fault==4)FakeFaults.Snapshot=true;
                if(fault==5)RunSession.Loading=new object();
                if(fault==6){f.mapManager.CurrentMapModel.mapId=19;DataStorage.Instance.currentMapId=19;f.fieldController.player.transform.position=new Vector3(131,90,0);Check(!WorldTravelRuntime.BeforePlayerReady(f),"Wrong native arrival coordinate accepted.");}
                else if(SetupView.FatalError==null)
                {
                    f.State=SubSceneManagerMainGame.State.Player;f.Ready=false;DataStorage.Instance.currentMapId=a.Map;
                    f.mapManager.CurrentMapModel.mapId=a.Map;f.fieldController.player.transform.position=new Vector3(a.X,a.Y,0);
                    WorldTravelRuntime.BeforePlayerReady(f);f.Ready=true;
                    if(fault==7)f.fieldController.MapViewType=ViewType.Normal;
                    WorldTravelRuntime.Tick();
                }
            }
            Check(SetupView.FatalError!=null&&WorldTravelRuntime.IsBusy&&!RunSession.CanSave,"Failed native transition became saveable.");
            Check(RunSession.Active.WorldState.ActiveWorld==before.WorldState.ActiveWorld,"Failed transition committed destination world.");
            Reject(()=>WorldTravelRuntime.EventWorld(before));
            Reject(WorldTravelRuntime.CancelMenu);Reject(WorldTravelRuntime.RequireIdleForLoad);
        }
        var home=Reset(run);foreach(int map in new[]{12,18,19,29,584}){var load=new LoadData();load.NextMapData(map,1,ViewType.Normal);Reject(()=>WorldTravelRuntime.BeforeGotoMap(load));}
        foreach(int map in new[]{1,30,203,591}){var load=new LoadData();load.NextMapData(map,1,ViewType.Normal);Check(WorldTravelRuntime.BeforeGotoMap(load),"Same-world normal map or W1 Ronka scene blocked.");}
        foreach(int world in new[]{2,3})
        {
            var owned=Key(Key(run,WorldTravelState.World2Key),WorldTravelState.World3Key).WithActiveWorld(world);
            var f=Reset(owned);int sea=world==2?18:29;var load=new LoadData();load.NextMapData(sea,1,ViewType.Normal);
            Check(WorldTravelRuntime.BeforeGotoMap(load),"Same-world underwater route blocked.");
            if(world==2){var merge=new LoadData();merge.NextMapData(591,1,ViewType.Normal);Reject(()=>WorldTravelRuntime.BeforeGotoMap(merge));}
            DataStorage.Instance.currentMapId=sea;f.mapManager.CurrentMapModel.mapId=sea;
            Check(!WorldTravelRuntime.TryOpen(out _)&&!WorldTravelRuntime.IsBusy,"Surface-only Warp Shard opened underwater.");
            Reject(()=>new WorldTravelTransaction(owned,sea,1,0));
        }
        Reset(null);var legacy=new LoadData();legacy.NextMapData(591,1,ViewType.Normal);Check(WorldTravelRuntime.BeforeGotoMap(legacy),"Legacy story travel blocked.");
        Reject(()=>WorldTravelRuntime.EventWorld(null));Reject(()=>WorldTravelRuntime.EventWorld(RunMetadata.Create(8,"Bartz")));
    }
    private static void SameWorldRecall(RunMetadata run)
    {
        if(!CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)) return;
        var unlocked=Key(Key(run,WorldTravelState.World2Key),WorldTravelState.World3Key);
        foreach(int world in new[]{1,2,3})
        {
            // The initial World1 recall needs no key; other worlds retain both.
            var before=world==1?run:unlocked.WithActiveWorld(world);var field=Reset(before);
            Check(WorldTravelRuntime.EventWorld(before)==world,"Idle event world ignored owned state.");
            field.fieldController.player.transform.position=new Vector3(0,0,0);Open(field);
            WorldTravelRuntime.SelectWorld(world);
            Check(WorldTravelRuntime.IsBusy&&Object.ReferenceEquals(before,RunSession.Active),"Recall bypassed native transaction.");
            Check(WorldTravelRuntime.EventWorld(before)==world,"Recall event world changed.");
            Arrive(field,WorldTravelAnchor.ForWorld(world));WorldTravelRuntime.Tick();
            var after=RunSession.Active;
            Check(!WorldTravelRuntime.IsBusy&&after.WorldState.ActiveWorld==world,"Same-world recall failed.");
            Check(after.AcquiredKeyIds.Count==before.AcquiredKeyIds.Count&&after.ClaimedLocationIds.Count==before.ClaimedLocationIds.Count
                &&after.RunId==before.RunId&&after.PlacementHash==before.PlacementHash,"Recall changed reward or key state.");
            Check(SaveSeedCodec.Attach("{}",after)==SaveSeedCodec.Attach("{}",before),"Recall changed owned save metadata.");
        }
    }
    private static void Protocol(RunMetadata run)
    {
        run=Key(run,WorldTravelState.World3Key);var p=new WorldTravelTransaction(run,1,1,0);p.Dispatch(3,300000);
        Check(p.StartedAt==300000,"Menu dwell incorrectly counted as travel timeout.");Reject(p.Cancel);Reject(()=>p.RequireOwner(run.WithBartzName("Other"),1));Reject(()=>p.RequireOwner(run,2));
        Reject(()=>p.Commit(19,19,19,1,130,90,true));Reject(()=>p.Arrive(12));p.Arrive(19);
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,130.1f})Reject(()=>p.Commit(19,19,19,1,bad,90,true));
        Reject(()=>p.Commit(19,19,19,1,130,90,false));Reject(()=>p.Commit(19,19,1,1,130,90,true));
        var next=p.Commit(19,19,19,1,130,90,true);Check(next.WorldState.ActiveWorld==3&&run.WorldState.ActiveWorld==1,"Protocol mutated source world.");
        Reject(()=>p.Commit(19,19,19,1,130,90,true));
    }
    private static void AnimationProgress(RunMetadata run)
    {
        if(!CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)) return;
        run=Key(Key(run,WorldTravelState.World2Key),WorldTravelState.World3Key);
        foreach(int world in new[]{1,2,3})
        {
            var f=Reset(run);Open(f);FakeFaults.AnimateTransport=true;WorldTravelRuntime.SelectWorld(world);
            var c=f.fieldController;var source=RunSession.Active;Arrive(f,WorldTravelAnchor.ForWorld(world));
            int birdFrames=c.birdViewController.Frames,playerFrames=c.player.Frames;
            Check(birdFrames+playerFrames>0,"Animation fixture skipped boarding.");
            for(int i=0;i<4;i++)WorldTravelRuntime.Tick();
            Check(c.birdViewController.Updates==0&&c.player.Updates==0&&WorldTravelRuntime.IsBusy&&!RunSession.CanSave,"UI Tick advanced boarding or released ownership.");
            int total=Math.Max(birdFrames,playerFrames);
            for(int frame=1;frame<=total;frame++)
            {
                bool nativeInput=WorldTravelRuntime.BeforePlayerUpdate(f);
                Check(c.birdViewController.Updates==Math.Min(frame,birdFrames)&&c.player.Updates==Math.Min(frame,playerFrames),"Arrival animation advanced more than once per player update.");
                if(frame<total)
                    Check(!nativeInput&&WorldTravelRuntime.IsBusy&&!RunSession.CanSave&&Object.ReferenceEquals(source,RunSession.Active),"Input/save/world released during boarding.");
                else Check(nativeInput&&!WorldTravelRuntime.IsBusy&&RunSession.CanSave&&RunSession.Active.WorldState.ActiveWorld==world,"Completed animation did not restore verified arrival.");
                WorldTravelRuntime.Tick();WorldTravelRuntime.Tick();
                Check(c.birdViewController.Updates==Math.Min(frame,birdFrames)&&c.player.Updates==Math.Min(frame,playerFrames),"Repeated UI Tick doubled animation advancement.");
            }
            Check(WorldTravelRuntime.BeforePlayerUpdate(f)&&c.birdViewController.Updates==birdFrames&&c.player.Updates==playerFrames,"Idle native update manually advanced animations.");
        }
        // An animation cannot begin before native arrival or outside player state.
        var paused=Reset(run);Open(paused);FakeFaults.AnimateTransport=true;WorldTravelRuntime.SelectWorld(2);
        Check(!WorldTravelRuntime.BeforePlayerUpdate(paused)&&paused.fieldController.player.Updates==0,"Dispatched transition advanced a stale player.");
        Arrive(paused,WorldTravelAnchor.ForWorld(2));paused.State=SubSceneManagerMainGame.State.ChangeMap;
        Check(!WorldTravelRuntime.BeforePlayerUpdate(paused)&&paused.fieldController.player.Updates==0&&!RunSession.CanSave,"Arrival animation ran outside native player state.");
        paused.State=SubSceneManagerMainGame.State.Player;
        RunSession.Active=run.WithBartzName("Changed");
        Check(!WorldTravelRuntime.BeforePlayerUpdate(paused)&&paused.fieldController.player.Updates==0&&SetupView.FatalError!=null&&!RunSession.CanSave,"Animation ran after its run owner changed.");
        foreach(int world in new[]{1,2})
        {
            var f=Reset(run);Open(f);FakeFaults.AnimateTransport=true;WorldTravelRuntime.SelectWorld(world);Arrive(f,WorldTravelAnchor.ForWorld(world));
            FakeFaults.BirdUpdate=world==1;FakeFaults.PlayerUpdate=world==2;
            Check(!WorldTravelRuntime.BeforePlayerUpdate(f)&&SetupView.FatalError!=null&&WorldTravelRuntime.IsBusy&&!RunSession.CanSave,"Native animation failure released input/save.");
            Check(Object.ReferenceEquals(run,RunSession.Active),"Animation failure published destination metadata.");
            int updates=f.fieldController.player.Updates+f.fieldController.birdViewController.Updates;
            WorldTravelRuntime.Tick();WorldTravelRuntime.BeforePlayerUpdate(f);
            Check(f.fieldController.player.Updates+f.fieldController.birdViewController.Updates==updates,"Failed travel continued advancing native animation.");
        }
    }
    internal static int Main(string[] args)
    {foreach(string path in args){var run=Read(path);RoundTrips(run);Guards(run);Protocol(run);SameWorldRecall(run);AnimationProgress(run);}Console.WriteLine("World travel adapter/protocol checks passed: "+checks);Console.WriteLine("Fake native only: playable/draft catalogs, independent keys, free returns/recall, destination-event context, animation-only arrival progress, native arrival/control/vehicle/snapshot ownership, unsafe context rejection, partial failure save blocking. No in-game travel acceptance.");return 0;}
}
internal static class FakeFaults {internal static bool Dispatch,Transport,Snapshot,AnimateTransport,BirdUpdate,PlayerUpdate;}
internal static class FakeIdentity {private static int count;internal static IntPtr New()=>new IntPtr(++count);}
public sealed class FieldMap
{
    public readonly IntPtr Pointer=FakeIdentity.New();public bool isActiveAndEnabled=true;public GameObject gameObject=new GameObject();public FieldController fieldController=new FieldController();public MapManager mapManager=new MapManager();
    public SubSceneManagerMainGame.State State=SubSceneManagerMainGame.State.Player;public bool Ready=true;public string requestItemScriptName;
    public bool CheckCurrentState(SubSceneManagerMainGame.State s)=>State==s;public bool IsPlayable()=>Ready;
    public bool IsGameUiOperable()=>Ready&&State==SubSceneManagerMainGame.State.Player;
    public void UpdatePlayerStatePlay(){}public void InitPlayerStatePlay(){}
}
namespace UnityEngine
{
    public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public sealed class Transform{public Vector3 position;}
    public sealed class GameObject{public int layer=9;public bool activeInHierarchy=true;}
}
public static class LayerSetting{public enum Layers{Walk=9}}
namespace Il2CppSystem{public struct Nullable<T> where T:struct {public T Value;public Nullable(T value){Value=value;}}}
namespace Last.Management{public static class SubSceneManagerMainGame{public enum State{ChangeMap=1,Player=3,Battle=13}}}
namespace Last.Interpreter
{
    public sealed class DataStorage{public static DataStorage Instance=new DataStorage();public int currentMapId;}
    public sealed class Integrator{public static Integrator Instance=new Integrator();public string scriptName="probe";public bool Main,Puppet;public bool IsMainCoreAlive()=>Main;public bool IsPuppetCoreAlive()=>Puppet;}
}
namespace Last.Interpreter.Instructions
{public static class External{public static class Vehicle{public static int[] directionList=new[]{1,2,4,8};}}}
namespace Last.Map
{
    public enum ViewType{Normal,Bird}
    public sealed class LoadData{public readonly IntPtr Pointer=FakeIdentity.New();public int MapId,Point;public void NextMapData(int map,int point,ViewType view){MapId=map;Point=point;}}
    public sealed class EventProcedure{public bool IsRunningAction;public void EventGotoMap(LoadData load,bool skipFade,bool skipBgm){WorldTravelRuntime.BeforeGotoMap(load);if(FakeFaults.Dispatch)throw new InvalidOperationException("Injected dispatch failure.");}}
    public sealed class MapModel{public int mapId;public bool IsAreaTypeWorld=true;}
    public sealed class MapManager{public MapModel CurrentMapModel=new MapModel();}
    public sealed class FieldPlayer
    {
        public Transform transform=new Transform();public GameObject gameObject=new GameObject();public bool IsMoving,IsAutoMoving;public int Frames,Updates;
        public bool IsDuringTakeoffAndLanding=>Frames>0;
        public void UpdateTransformLowFlying(){Updates++;if(FakeFaults.PlayerUpdate)throw new InvalidOperationException("Injected player animation failure.");if(Frames>0)Frames--;}
    }
    public sealed class BirdViewController
    {
        public int Frames,Updates;public bool IsDuringTakeoffAndLanding=>Frames>0;
        public void UpdateController(){Updates++;if(FakeFaults.BirdUpdate)throw new InvalidOperationException("Injected bird animation failure.");if(Frames>0)Frames--;}
    }
    public sealed class TransportationInfo{public int Id,StayMapId;public bool Enable;}
    public sealed class TransportationController{public TransportationInfo CurrentTransportation=new TransportationInfo();}
    public sealed class FieldController
    {
#if WORLD_MAP_PROBE
        public int GpsLoads, GpsAtSnapshot;
        public void Last_Map_IMapAccessor_SetGpsId(int id)
        {
            var common = MapUIManager.Instance.GetGpsCommonData();
            if (common.GpsId == id) return;
            var row = Last.Data.Master.MasterManager.Instance.Rows[id];
            common.GpsId = id; common.MapId = row.MapId; GpsLoads++;
        }
#endif
        public FieldPlayer player=new FieldPlayer();public BirdViewController birdViewController=new BirdViewController();public EventProcedure eventHandle=new EventProcedure();public TransportationController transportation=new TransportationController();
        private bool forcedTakeoff;public bool IsDuringTakeoffAndLanding{get=>forcedTakeoff||(MapViewType==ViewType.Bird&&birdViewController.IsDuringTakeoffAndLanding)||player.IsDuringTakeoffAndLanding;set=>forcedTakeoff=value;}
        public bool isPlayerFootProcessing,IsDuringSubmarineDivingAndSurface;public ViewType MapViewType;public int Direction,PlacedLayer;
        public Vector3 WorldPositionToCellPosition(Vector3 p)=>p;
        public void TransportationEnabled(int id,bool enabled,int map,Il2CppSystem.Nullable<Vector2> pos,Il2CppSystem.Nullable<LayerSetting.Layers> layer,Il2CppSystem.Nullable<int> direction,bool script)
        {transportation.CurrentTransportation=new TransportationInfo{Id=id,StayMapId=map,Enable=enabled};Direction=direction.Value;PlacedLayer=(int)layer.Value;}
        public void ChangeTransportation(int id,bool collision,bool background,ViewType view){transportation.CurrentTransportation.Id=FakeFaults.Transport?1:id;MapViewType=id==4?ViewType.Normal:ViewType.Bird;if(FakeFaults.AnimateTransport){birdViewController.Frames=MapViewType==ViewType.Bird?2:0;player.Frames=background?0:3;}}
        public void SaveSetupParameters(bool value){SaveMapManager.Instance.SaveData=new SaveMapData{MapId=DataStorage.Instance.currentMapId,TransportationId=FakeFaults.Snapshot?1:transportation.CurrentTransportation.Id};
#if WORLD_MAP_PROBE
            GpsAtSnapshot=MapUIManager.Instance?.GetGpsCommonData()?.GpsId??0;
#endif
        }
    }
    public sealed class SaveMapData{public int MapId,TransportationId;}
    public sealed class SaveMapManager{public static SaveMapManager Instance=new SaveMapManager();public SaveMapData SaveData;}
}
namespace HarmonyLib{[AttributeUsage(AttributeTargets.Class)]public sealed class HarmonyPatch:Attribute{public HarmonyPatch(Type type,string method,Type[] args){}}}
namespace ProjectBlues.Runtime
{
#if !WORLD_MAP_PROBE
    // Overlay behavior is exercised with the real travel adapter in its own probe.
    internal static class WorldMapOverlayRuntime { internal static void Refresh(FieldMap field) {} }
#endif
#if !CONTROLLER_PROBE
    // The travel protocol probe isolates transaction behavior. The separate
    // controller probe compiles the real bridge, view and protocol together.
    internal static class WorldTravelController
    { internal static void GuardRelease() {} internal static bool BeforePlayerInput() => true; }
#endif
    internal static class RunSession{internal static RunMetadata Active,Pending;internal static object Loading;internal static bool CanSave=>Loading==null&&SetupView.FatalError==null&&!WorldTravelRuntime.IsBusy;}
    internal static class CampaignRuntime{internal static RunMetadata EventRun=>RunSession.Active;}
    internal static class RemixedJobRuntime{internal static bool IsBusy;}
    internal static class RenameSession{internal static bool IsActive;}
    internal static class SetupView{internal static string FatalError,Notification;internal static void Fail(string message){FatalError=message;}internal static void Notify(string message){Notification=message;}}
    internal sealed class FakeLog{internal void LogInfo(object value){}internal void LogError(object value){}}
    internal static class Plugin{internal static FakeLog Logger=new FakeLog();}
}
