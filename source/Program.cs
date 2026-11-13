using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;
namespace CodexVoice {
static class Program {
 public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
 public static string Data {get{return Path.Combine(Root,"data");}}
 public static Settings Config;
 [STAThread] static int Main(string[] args) {
  Directory.CreateDirectory(Data); Directory.CreateDirectory(Path.Combine(Root,"build"));Config=Settings.Load();if(Config.Migrate())Config.Save();
  try {
   if(args.Length>0) {
    if(args[0]=="--shutdown"){InstanceControl.SignalThisCopy("Exit");return 0;}
    if(args[0]=="--shutdown-permanently"){if(!InstanceControl.SignalThisCopy("Stop")){Startup.Set(false);Config.StoppedByUser=true;Config.ListenerEnabled=false;Config.Save();}return 0;}
    if(args[0]=="--desktop-shortcut"){File.WriteAllText(Path.Combine(Root,"build","desktop-shortcut.txt"),DesktopShortcut.Create());return 0;}
    if(args[0]=="--detect-codex"){File.WriteAllText(Path.Combine(Root,"build","codex-detection.txt"),AppDiscovery.Check());return 0;}
    if(args[0]=="--probe-prerequisites"){int result=AppDiscovery.FindExecutable().Length==0?1:0;bool speech=false;foreach(var r in System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers())if(r.Culture.Name=="en-US")speech=true;if(!speech)result|=2;return result;}
    if(args[0]=="--startup-on"||args[0]=="--startup-off"){Startup.Set(args[0]=="--startup-on");return 0;}
    if(args[0]=="--configure-install"){Config.AutoStart=args.Length>1&&args[1]=="1";Config.ListenerEnabled=true;Config.StoppedByUser=false;Config.Hidden=false;Config.AppPath=AppDiscovery.FindExecutable();Config.Save();Startup.Set(Config.AutoStart);return 0;}
    if(args[0]=="--export-icon"){Directory.CreateDirectory(Path.Combine(Root,"assets"));using(var bitmap=new System.Drawing.Bitmap(64,64)){using(var g=System.Drawing.Graphics.FromImage(bitmap)){var visual=new OrbVisual();visual.Tick(SessionState.Idle,0,.1);visual.Draw(g,32,32,60);}IntPtr h=bitmap.GetHicon();try{using(var icon=(System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone())using(var f=File.Create(Path.Combine(Root,"assets","codex-voice.ico")))icon.Save(f);}finally{Native.DestroyIcon(h);}}return 0;}
    if(args[0]=="--inspect") {File.WriteAllText(Path.Combine(Root,"build","native-inspection.txt"),NativeProbe.Inspect());return 0;}
    if(args[0]=="--self-test") {SelfTests.Run(false);return 0;}
    if(args[0]=="--voice-self-test") {SelfTests.Run(true);return 0;}
    if(args[0]=="--bridge-check") {File.WriteAllText(Path.Combine(Root,"build","bridge-check.txt"),new CodexBridge().Check());return 0;}
    if(args[0]=="--bridge-benchmark"){File.WriteAllText(Path.Combine(Root,"build","bridge-benchmark.txt"),System.Threading.Tasks.Task.Run(()=>new CodexBridge().Benchmark()).GetAwaiter().GetResult());return 0;}
    if(args[0]=="--background-record-test"){File.WriteAllText(Path.Combine(Root,"build","background-record-test.txt"),System.Threading.Tasks.Task.Run(()=>new CodexBridge().CheckBackgroundRecording()).GetAwaiter().GetResult());return 0;}
    if(args[0]=="--background-open-record-test"){File.WriteAllText(Path.Combine(Root,"build","background-open-record-test.txt"),System.Threading.Tasks.Task.Run(()=>new CodexBridge().CheckBackgroundRecording(false)).GetAwaiter().GetResult());return 0;}
    if(args[0]=="--record-smoke-test") {var bridge=new CodexBridge();var report=bridge.Check();try{report+=bridge.Start()+"\n";Thread.Sleep(1100);report+="Stop result characters="+bridge.Stop().Length+"\n";report+=bridge.Check();File.WriteAllText(Path.Combine(Root,"build","record-smoke-test.txt"),report);}finally{bridge.CancelOwned();}return 0;}
    if(args[0]=="--render-preview"){Application.EnableVisualStyles();using(var form=new Companion(true))form.CaptureDesign();return 0;}
   }
   bool first;using(var mutex=new Mutex(true,"Local\\CodexVoiceCompanion2",out first)) {
    bool atStartup=Array.IndexOf(args,"--startup")>=0;
    if(!first){if(!atStartup)InstanceControl.Signal("Show");return 0;}
    if(atStartup&&Config.StoppedByUser)return 0;
    if(!atStartup&&Config.StoppedByUser){Config.StoppedByUser=false;Config.ListenerEnabled=true;Config.Hidden=false;Config.Save();}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    Application.ThreadException+=delegate(object s,ThreadExceptionEventArgs e){Log(e.Exception);};
    AppDomain.CurrentDomain.UnhandledException+=delegate(object s,UnhandledExceptionEventArgs e){Log(e.ExceptionObject as Exception);};
    using(var form=new Companion())using(var show=InstanceControl.Listen("Show",()=>form.RequestAction("show")))using(var stop=InstanceControl.Listen("Stop",()=>form.RequestAction("stop")))using(var exit=InstanceControl.Listen("Exit",()=>form.RequestAction("exit")))Application.Run(form);
   }
   return 0;
  }catch(Exception ex){Log(ex);File.WriteAllText(Path.Combine(Root,"build","last-check-error.txt"),ex.ToString());return 1;}
 }
 public static void Log(Exception ex){try{string p=Path.Combine(Data,"diagnostics.log");File.AppendAllText(p,DateTime.Now.ToString("s")+" "+(ex==null?"Unknown error":ex.GetType().Name+": "+ex.Message)+Environment.NewLine);if(new FileInfo(p).Length>65536)File.WriteAllText(p,"Log rotated\n");}catch{}}
}
public class Settings {
 public int Version=4,Size=76,X=-1,Y=-1,SilenceLevel=4;
 public bool AutoStart=false,ListenerEnabled=true,Hidden=false,StoppedByUser=false;
 public float WakeConfidence=.67f,ControlConfidence=.70f;
 public double CommandPauseSeconds=2;
 public string AppPath="";
 static readonly XmlSerializer Serializer=new XmlSerializer(typeof(Settings));static readonly object SaveLock=new object();
 public bool Migrate(){if(Version>=4)return false;if(Version<3)ControlConfidence=.70f;Size=Math.Max(56,Math.Min(104,Size+4));Version=4;return true;}
 public static Settings Load(){try{using(var r=File.OpenRead(Path.Combine(Program.Data,"settings.xml")))return (Settings)Serializer.Deserialize(r);}catch{return new Settings();}}
 public void Save(){lock(SaveLock)try{string path=Path.Combine(Program.Data,"settings.xml");using(var w=File.Create(path+".new"))Serializer.Serialize(w,this);if(File.Exists(path))File.Replace(path+".new",path,null);else File.Move(path+".new",path);}catch(Exception e){Program.Log(e);}}
}
static class Startup {
 const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static void Set(bool enabled){using(var k=Registry.CurrentUser.CreateSubKey(Key)){string own=System.IO.Path.Combine(Program.Root,"CODEX.exe");if(enabled)k.SetValue("CodexVoiceCompanion","\""+own+"\" --startup");else{string current=Convert.ToString(k.GetValue("CodexVoiceCompanion"));if(current.IndexOf(own,StringComparison.OrdinalIgnoreCase)>=0)k.DeleteValue("CodexVoiceCompanion",false);}}Program.Config.AutoStart=enabled;Program.Config.Save();}
}
}
