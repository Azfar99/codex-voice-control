using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexVoice {
static class Product {
 public const string Version="0.4.1";
 public const string Name="CODEX Voice Control";
 public const string PackageFamily="OpenAI.Codex_2p2nqsd0c76g0";
 public const string AppId=PackageFamily+"!App";
 public const string DownloadUrl="https://learn.chatgpt.com/docs/app/windows";
}
static class AppDiscovery {
 public static string FindExecutable(){
  uint count=0,chars=0;
  int status=GetPackagesByPackageFamily(Product.PackageFamily,ref count,IntPtr.Zero,ref chars,IntPtr.Zero);
  if((status!=122&&status!=0)||count==0||count>128||chars>131072)return "";
  IntPtr names=Marshal.AllocHGlobal((int)count*IntPtr.Size),buffer=Marshal.AllocHGlobal((int)chars*2);
  try {
   status=GetPackagesByPackageFamily(Product.PackageFamily,ref count,names,ref chars,buffer);
   if(status!=0)return "";
   for(int i=(int)count-1;i>=0;i--){
    string full=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));
    uint length=0;GetPackagePathByFullName(full,ref length,null);if(length<2||length>32768)continue;
    var path=new StringBuilder((int)length);if(GetPackagePathByFullName(full,ref length,path)!=0)continue;
    string executable=Path.Combine(path.ToString(),"app","ChatGPT.exe");if(CodexBridge.AllowedPath(executable)&&File.Exists(executable))return executable;
   }
  }finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);}
  return "";
 }
 public static string Check(){string path=FindExecutable();return path.Length>0?"Installed OpenAI Codex package detected.\nExecutable="+path:"Codex app not detected for this Windows user. Install Codex and sign in first.\n"+Product.DownloadUrl;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint chars,IntPtr buffer);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder path);
}
static class InstanceControl {
 const string Prefix="Local\\CodexVoiceCompanion.";
 public static bool Signal(string action){try{using(var e=EventWaitHandle.OpenExisting(Prefix+action)){return e.Set();}}catch(WaitHandleCannotBeOpenedException){return false;}}
 public static bool SignalThisCopy(string action){int current=Process.GetCurrentProcess().Id;string executable=Path.Combine(Program.Root,"CODEX.exe");foreach(var p in Process.GetProcessesByName("CODEX")){try{if(p.Id!=current&&String.Equals(p.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase))return Signal(action);}catch{}finally{p.Dispose();}}return false;}
 public static IDisposable Listen(string action,Action callback){return new Registration(action,callback);}
 sealed class Registration:IDisposable {
  readonly EventWaitHandle handle;readonly RegisteredWaitHandle wait;
  public Registration(string action,Action callback){handle=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+action);wait=ThreadPool.RegisterWaitForSingleObject(handle,delegate(object state,bool timedOut){if(!timedOut)callback();},null,Timeout.Infinite,false);}
  public void Dispose(){wait.Unregister(null);handle.Dispose();}
 }
}
static class DesktopShortcut {
 public static string Create(){
  string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product.Name+".lnk");
  string target=Path.Combine(Program.Root,"CODEX.exe");
  if(File.Exists(file)){string existing=ReadTarget(file);if(!String.Equals(existing,target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A different shortcut already uses this name: "+file);}
  Type type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)throw new InvalidOperationException("Windows shortcut service is unavailable.");
  object shell=Activator.CreateInstance(type),link=null;
  try {
   link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});
   Set(link,"TargetPath",target);Set(link,"WorkingDirectory",Program.Root);Set(link,"Description","Start CODEX Voice Control");Set(link,"IconLocation",target+",0");Set(link,"WindowStyle",1);
   link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);return file;
  }finally{if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
 }
 public static string ReadTarget(string path){
  Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type),link=null;
  try{link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});return Convert.ToString(link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));}
  finally{if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
 }
 static void Set(object obj,string name,object value){obj.GetType().InvokeMember(name,BindingFlags.SetProperty,null,obj,new[]{value});}
}
}
