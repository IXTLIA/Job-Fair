// Actual view + controller + travel runtime with fake Unity/native objects.
using System;
using System.Collections.Generic;
using System.Reflection;
using ProjectBlues.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

internal static class WorldTravelControllerProbe
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static void Set(string name, object value) => typeof(WorldTravelController).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
    private static TravelPad Pad(bool view=false, bool y=false, bool a=false, bool b=false, bool up=false, bool down=false, bool focus=true, int device=1)
        => new TravelPad(device, focus, view, y, a, b, up, down, view||y||a||b||up||down);
    private static void Policy()
    {
        var c = new WorldTravelControls();
        c.Update(Pad(a:true,view:true,y:true,up:true),0);
        Check(!c.Open&&!c.Confirm&&c.Step==0,"Startup held buttons acted");
        c.Update(Pad(),1); c.Update(Pad(y:true),2); Check(!c.Open,"Y alone opened");
        c.Update(Pad(view:true,y:true),3); Check(!c.Open,"Modifier added after Y opened");
        c.Update(Pad(view:true),4); c.Update(Pad(view:true,y:true),5); Check(c.Open,"View then Y failed");
        c.Update(Pad(view:true,y:true),6); Check(!c.Open,"Held chord repeated");
        c.Update(Pad(up:true),10); Check(c.Step==-1,"Initial stick movement missed");
        c.Update(Pad(up:true),359); Check(c.Step==0,"Repeat started early");
        c.Update(Pad(up:true),360); Check(c.Step==-1,"Initial repeat missed");
        c.Update(Pad(up:true),499); Check(c.Step==0,"Repeat interval too short");
        c.Update(Pad(up:true),500); Check(c.Step==-1,"Held repeat missed");
        c.Update(Pad(up:true),9999); Check(c.Step==-1,"Slow frame should produce one step");
        c.Update(Pad(up:true),10000); Check(c.Step==0,"Slow frame caused repeat burst");
        c.Update(Pad(up:true,down:true),10001); Check(c.Step==0,"Opposite directions did not cancel");
        c.Update(Pad(down:true),10002); Check(c.Step==1,"Direction reversal failed");
        c.Update(Pad(a:true,b:true),10003); Check(c.Confirm&&c.Cancel,"Button edges missing");
        c.Update(Pad(a:true,b:true),10004); Check(!c.Confirm&&!c.Cancel,"Held buttons repeated");
        c.Update(Pad(focus:false),10005); c.Update(Pad(a:true,view:true,y:true),10006);
        Check(!c.Open&&!c.Confirm,"Focus regain accepted held buttons");
        c.Update(Pad(),10007); c.Update(Pad(a:true),10008); Check(c.Confirm,"Focus neutral did not rearm");
        c.Update(Pad(device:0),10009); c.Update(Pad(a:true,view:true,y:true),10010);
        Check(!c.Open&&!c.Confirm,"Reconnect accepted held buttons");
        c.Update(Pad(),10011); c.Update(Pad(a:true,device:2),10012); Check(!c.Confirm,"Device switch confirmed");
        c.Update(Pad(device:2),10013); c.Update(Pad(a:true,device:2),10014); Check(c.Confirm,"New pad failed after neutral");
    }
    private static void Integration(RunMetadata run, bool viewFirst)
    {
        Set("releaseGuard",false); Set("sampledFrame",-1); Set("openFrame",-1); Set("neutralFrame",-1); Set("unavailable",false);
        Application.isFocused=true; Input.Clear(); Gamepad.current=null;
        Time.frameCount++;
        var field=WorldTravelRuntimeProbe.Reset(run);
        var view=new WorldTravelView(IntPtr.Zero);
        bool Frame(Action<Gamepad> press=null, bool focus=true, KeyCode? key=null, bool connected=true, bool click=false)
        {
            Time.frameCount++; Application.isFocused=focus; Input.Clear();
            if(key.HasValue) Input.Keys.Add(key.Value);
            Input.Mouse=click;
            Gamepad.current=connected?new Gamepad():null; if(connected)press?.Invoke(Gamepad.current);
            bool allowed;
            if(viewFirst) {view.Update(); allowed=WorldTravelRuntime.BeforePlayerUpdate(field);}
            else {allowed=WorldTravelRuntime.BeforePlayerUpdate(field); view.Update();}
            Check(SetupView.FatalError==null,"View failed: "+SetupView.FatalError);
            return allowed;
        }
        int Selection() => (int)typeof(WorldTravelView).GetField("selected",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);
        void Open()
        {
            Frame(); Frame();
            Check(!Frame(p=>p.selectButton.isPressed=true),"Modifier leaked to native field");
            Check(!Frame(p=>{p.selectButton.isPressed=true;p.buttonNorth.isPressed=true;}),"Open chord leaked to field");
            Check(WorldTravelRuntime.IsMenuOpen,"Controller chord did not open");
            Frame(); Frame();
        }
        Check(Frame(),"Idle input blocked");
        Check(Frame(p=>p.buttonNorth.isPressed=true)&&!WorldTravelRuntime.IsMenuOpen,"Native Y was stolen");
        Frame();
        // Unsafe contexts must not claim the modifier or create a transaction.
        field.mapManager.CurrentMapModel.IsAreaTypeWorld=false;
        Check(Frame(p=>p.selectButton.isPressed=true),"Dungeon modifier stolen");
        Check(Frame(p=>{p.selectButton.isPressed=true;p.buttonNorth.isPressed=true;})&&!WorldTravelRuntime.IsMenuOpen,"Dungeon shortcut opened");
        field.mapManager.CurrentMapModel.IsAreaTypeWorld=true;
        field.fieldController.player.IsMoving=true; Frame();
        Check(Frame(p=>{p.selectButton.isPressed=true;p.buttonNorth.isPressed=true;})&&!WorldTravelRuntime.IsMenuOpen,"Moving shortcut opened");
        field.fieldController.player.IsMoving=false;
        Open(); Check(Selection()==1,"Menu did not start on current world");
        GUI.Labels.Clear(); view.OnGUI();
        Check(GUI.Labels.Contains("B / Esc: Cancel"),"Controller cancel prompt missing");
        Check(GUI.Labels.Exists(s=>s.Contains("A: Travel")&&s.Contains("left stick")),"Controller navigation prompt missing");
        Frame(p=>p.leftStick.y=0.05f); Check(Selection()==1,"Stick drift moved selection");
        Frame(p=>p.leftStick.y=0.54f); Check(Selection()==1,"Stick moved below navigation threshold");
        Frame();
        Check(!Frame(p=>p.dpad.down.isPressed=true)&&Selection()==2,"D-pad failed or leaked movement");
        Frame(); Frame(p=>p.buttonSouth.isPressed=true);
        Check(WorldTravelRuntime.IsMenuOpen&&WorldTravelRuntime.IsBusy,"Locked world dismissed menu");
        Frame(); Frame(p=>p.leftStick.down.isPressed=true); Check(Selection()==3,"Left stick navigation failed");
        Frame(); Frame(p=>p.rightShoulder.isPressed=true); Check(Selection()==1,"RB wrap failed");
        Frame(); Frame(p=>p.leftShoulder.isPressed=true); Check(Selection()==3,"LB wrap failed");
        Frame(); Frame(p=>{p.dpad.up.isPressed=true;p.leftStick.down.isPressed=true;}); Check(Selection()==3,"Conflicting devices moved selection");
        Frame(); Check(!Frame(p=>p.buttonEast.isPressed=true),"Cancel frame leaked");
        Check(!WorldTravelRuntime.IsMenuOpen,"B did not cancel");
        Check(!Frame(p=>p.buttonEast.isPressed=true),"Held B leaked after cancel");
        Check(!Frame(p=>p.leftStick.x=0.3f),"Partially held stick leaked after cancel");
        Check(!Frame(),"Release frame leaked after cancel"); Check(Frame(),"Input did not restore after release");
        // Held shortcut at reconnect and focus return must be released first.
        Frame(connected:false);
        Frame(p=>{p.selectButton.isPressed=true;p.buttonNorth.isPressed=true;}); Check(!WorldTravelRuntime.IsMenuOpen,"Held reconnect opened");
        Frame(); Frame(); Open();
        view.OnApplicationFocus(false); view.OnApplicationFocus(true);
        Frame(p=>p.buttonSouth.isPressed=true);
        Check(WorldTravelRuntime.IsMenuOpen,"Focus callbacks without background Update confirmed a warp");
        Frame(); Frame();
        view.OnApplicationPause(true); view.OnApplicationPause(false);
        Frame(p=>p.buttonSouth.isPressed=true);
        Check(WorldTravelRuntime.IsMenuOpen,"Pause callbacks without background Update confirmed a warp");
        Frame(); Frame();
        int before=Selection(); Frame(focus:false);
        Frame(p=>{p.buttonSouth.isPressed=true;p.dpad.down.isPressed=true;});
        Check(WorldTravelRuntime.IsMenuOpen&&Selection()==before,"Focus regain navigated/confirmed");
        Frame(); Frame();
        Frame(p=>{p.buttonEast.isPressed=true;p.buttonSouth.isPressed=true;});
        Check(!WorldTravelRuntime.IsBusy,"A beat B when pressed together");
        Frame(); Frame();
        // Existing keyboard and mouse behavior remains usable with no pad.
        Frame(connected:false,key:KeyCode.F6); Check(WorldTravelRuntime.IsMenuOpen,"F6 regression");
        Frame(connected:false); Frame(connected:false);
        Frame(connected:false,key:KeyCode.DownArrow); Check(Selection()==2,"Keyboard navigation regression");
        Frame(connected:false,key:KeyCode.Escape); Check(!WorldTravelRuntime.IsBusy,"Escape regression");
        Frame(connected:false); Frame(connected:false);
        Input.mousePosition=new Vector3(Screen.width-180,Screen.height-30,0);
        Frame(connected:false,click:true); Check(WorldTravelRuntime.IsMenuOpen,"Mouse opening regression");
        Frame(connected:false); Frame(connected:false);
        Input.mousePosition=new Vector3(Screen.width/2,Screen.height/2-105,0);
        Frame(connected:false,click:true); Check(!WorldTravelRuntime.IsBusy,"Mouse cancellation regression");
        Frame(connected:false); Frame(connected:false);
        Open(); Frame(p=>p.buttonSouth.isPressed=true);
        Check(WorldTravelRuntime.IsBusy&&!WorldTravelRuntime.IsMenuOpen,"A did not dispatch unlocked recall");
        Check(RunSession.Active.WorldState.ActiveWorld==1,"Dispatch committed before arrival");
        Check(!Frame(p=>p.buttonSouth.isPressed=true),"Travel allowed held field action");
        view.OnGUI(); Check(SetupView.FatalError==null,"GUI threw");
    }
    private static int Main(string[] args)
    {
        Policy(); var run=WorldTravelRuntimeProbe.Read(args[0]);
        Integration(run,false); Integration(run,true);
        Console.WriteLine("World travel controller/view/runtime checks passed: "+checks);
        Console.WriteLine("Fake Unity/native input only; physical controller and Steam gameplay acceptance pending."); return 0;
    }
}
namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr p){} }
    public static class Time { public static int frameCount; }
    public static class Application { public static bool isFocused=true; }
    public enum KeyCode {F6,Escape,UpArrow,DownArrow,Return,KeypadEnter,Alpha1,Alpha2,Alpha3}
    public static class Input
    {
        public static HashSet<KeyCode> Keys=new HashSet<KeyCode>();
        public static bool anyKey=>Keys.Count>0; public static Vector3 mousePosition;public static bool Mouse;
        public static bool GetKeyDown(KeyCode key)=>Keys.Contains(key);
        public static bool GetMouseButton(int i)=>Mouse; public static bool GetMouseButtonDown(int i)=>Mouse;
        public static void Clear(){Keys.Clear();Mouse=false;}
    }
    public struct Rect {public float x,y,width,height; public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}}
    public enum FontStyle{Normal}
    public class GUIStyle{public int fontSize;public bool wordWrap;public FontStyle fontStyle;}
    public class GUISkin{public GUIStyle label=new GUIStyle();}
    public static class GUI{public static int depth;public static GUISkin skin=new GUISkin();public static List<string> Labels=new List<string>();public static void Box(Rect r,string s){}public static void Label(Rect r,string s,GUIStyle st){Labels.Add(s);}}
    public static class Screen{public static int width=1280,height=720;}
}
namespace UnityEngine.InputSystem
{
    public class Button {public bool isPressed;}
    public class Directions {public Button up=new Button(),down=new Button(),left=new Button(),right=new Button();public float x,y;public Vector2 ReadValue()=>new Vector2(x,y+(up.isPressed?1:0)-(down.isPressed?1:0));}
    public class Gamepad
    {
        public static Gamepad current;public bool added=true;public int deviceId=1;
        public Directions dpad=new Directions(),leftStick=new Directions(),rightStick=new Directions();
        public Button selectButton=new Button(),startButton=new Button(),buttonNorth=new Button(),buttonSouth=new Button(),buttonEast=new Button(),buttonWest=new Button(),leftShoulder=new Button(),rightShoulder=new Button(),leftStickButton=new Button(),rightStickButton=new Button(),leftTrigger=new Button(),rightTrigger=new Button();
    }
}
