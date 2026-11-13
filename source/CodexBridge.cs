using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;
namespace CodexVoice {
sealed class CodexSnapshot {
 public AutomationElement Root,Surface,Editor;
 public AutomationElement[] Buttons;
 public string[] ButtonNames;
 public string Text,Chat,SurfaceId;
 public bool Recording {get{return Button("Stop dictation")!=null;}}
 public AutomationElement Button(string name){AutomationElement found=null;for(int i=0;i<Buttons.Length;i++)if(ButtonNames[i]==name){if(found!=null)throw new InvalidOperationException("Ambiguous Codex "+name+" control. Nothing clicked.");found=Buttons[i];}return found;}
}
sealed class CodexBridge {
 IntPtr hwnd;string chat;bool ownedRecording;AutomationElement savedSurface;
 readonly BackgroundCodex backgroundHost;
 readonly bool backgroundOnly;
 bool onBackgroundDesktop;
 uint hostPid;
 AutomationElement cachedMode,cachedDocument;CodexSnapshot snapshot;int snapshotAt;
 public CodexBridge():this(new BackgroundCodex(Path.Combine(Program.Data,"background-codex")),false){}
 CodexBridge(BackgroundCodex host,bool isolated){backgroundHost=host;backgroundOnly=isolated;}
 public bool Bound {get{return hwnd!=IntPtr.Zero;}}
 public void RebindIfSafe(){if(!ownedRecording)Forget();}
 public static bool AllowedPath(string path){return !String.IsNullOrEmpty(path)&&path.IndexOf(@"\WindowsApps\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0&&String.Equals(Path.GetFileName(path),"ChatGPT.exe",StringComparison.OrdinalIgnoreCase);}
 internal static bool IsCodexProcess(uint pid){
  if(pid==0)return false;
  try{using(var p=Process.GetProcessById((int)pid)){return p.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)&&AllowedPath(p.MainModule.FileName);}}
  catch(ArgumentException){return false;}catch(System.ComponentModel.Win32Exception){return false;}catch(InvalidOperationException){return false;}
 }
 static List<IntPtr> AppWindows(){
  return BackgroundCodex.CurrentDesktopWindows(delegate(IntPtr window){
   if(!Native.IsWindowVisible(window)||Native.GetWindow(window,4)!=IntPtr.Zero)return false;
   uint pid;
   Native.GetWindowThreadProcessId(window,out pid);
   return IsCodexProcess(pid);
  });
 }
 static string Id(AutomationElement e){return String.Join(".",e.GetRuntimeId().Select(i=>i.ToString()).ToArray());}
 CodexSnapshot Bind(bool allowLaunch=false,Func<bool> cancelled=null){
  var apps=backgroundOnly?new List<IntPtr>():AppWindows();
  if(apps.Count!=0){
   IntPtr selected=apps.FirstOrDefault(window=>window==Native.GetForegroundWindow());
   if(selected==IntPtr.Zero&&apps.Count==1)selected=apps[0];
   if(selected==IntPtr.Zero)throw new InvalidOperationException("Several Codex windows are open. Focus the intended chat, then wake again.");
   return BindWindow(selected,false);
  }
  if(!allowLaunch){
   var existing=backgroundHost.Windows();
   if(existing.Count==1)return BindWindow(existing[0],true);
   throw new InvalidOperationException("No Codex chat is ready. Say Hello Codex to start one.");
  }
  CheckCancelled(cancelled);
  backgroundHost.EnsureStarted();
  var timer=Stopwatch.StartNew();
  Exception readinessError=null;
  while(timer.ElapsedMilliseconds<35000){
   CheckCancelled(cancelled);
   if(backgroundHost.StartedProcessExited)
    throw new InvalidOperationException("Codex closed while starting in the background. Open Codex normally and check that you are signed in.");
   var candidates=backgroundHost.Windows();
   if(candidates.Count==1){
    try{
     var selected=BindWindow(candidates[0],true);
     var dictate=selected.Button("Dictate");
     if(selected.Recording||(dictate!=null&&dictate.Current.IsEnabled))return selected;
     Forget();
    }
    catch(InvalidOperationException error){readinessError=error;}
    catch(ElementNotAvailableException error){readinessError=error;}
   }
   Thread.Sleep(180);
  }
  throw new InvalidOperationException("Background Codex could not open its composer. Open Codex normally, sign in, then try again.",readinessError);
 }
 CodexSnapshot BindWindow(IntPtr window,bool background){
  hwnd=window;onBackgroundDesktop=background;
  Native.GetWindowThreadProcessId(hwnd,out hostPid);
  try{
   var selectedChat=Read(false,true);
   chat=selectedChat.Chat;
   return selectedChat;
  }catch{Forget();throw;}
 }
 bool IsBoundWindowAlive(){
  uint pid;
  return Bound&&Native.GetWindowThreadProcessId(hwnd,out pid)!=0&&pid==hostPid;
 }
 internal static bool ShowExistingCodex(){
  var windows=AppWindows();
  if(windows.Count!=1)return false;
  Native.ShowWindow(windows[0],9);
  Native.SetForegroundWindow(windows[0]);
  return true;
 }
 void VerifyProcess(){
  if(!IsBoundWindowAlive())throw new InvalidOperationException("The Codex window closed. Nothing sent.");
  if(!IsCodexProcess(hostPid))throw new InvalidOperationException("Destination is not the installed Codex app. Nothing clicked.");
 }
 static void CheckCancelled(Func<bool> cancelled){
  if(cancelled!=null&&cancelled())throw new OperationCanceledException("Pending dictation cancelled.");
 }
 IDisposable HideDuringOperation(){return onBackgroundDesktop?null:new BackgroundWindow(hwnd);}
 CodexSnapshot Read(bool pinned,bool fresh=false){
  if(fresh)snapshot=null;
  for(int retry=0;;retry++){try{if(snapshot!=null&&CacheFresh(Environment.TickCount,snapshotAt))return ReadCached(pinned);return ReadOnce(pinned);}catch(ElementNotAvailableException){snapshot=null;if(retry>=7)throw;Thread.Sleep(40);}catch(InvalidOperationException ex){snapshot=null;if(retry>=7||ex.Message.IndexOf("composer not uniquely available",StringComparison.Ordinal)<0)throw;Thread.Sleep(40);}}
 }
 internal static bool CacheFresh(int now,int saved){return unchecked((uint)(now-saved))<1000;}
 CodexSnapshot ReadCached(bool pinned){
  VerifyProcess();if(cachedMode.Current.Name!="Switch mode, current mode: Codex")throw new InvalidOperationException("Codex mode changed. No controls clicked.");
  if(pinned&&cachedDocument.Current.Name!=chat)throw new InvalidOperationException("Codex chat changed. Nothing sent; wake again in the intended chat.");
  if(!(snapshot.Surface.Current.ClassName??"").StartsWith("ComposerLayoutBody-",StringComparison.Ordinal)){snapshot=null;return ReadOnce(pinned);}
  // Cached element identities avoid scanning the whole conversation each time.
  // Their Current properties and the native process/chat identity are still checked.
  snapshot.ButtonNames=snapshot.Buttons.Select(b=>b.Current.Name).ToArray();
  snapshot.Text=snapshot.Editor==null?"":ReadText(snapshot.Editor);return snapshot;
 }
 CodexSnapshot ReadOnce(bool pinned){
  if(hwnd==IntPtr.Zero)throw new InvalidOperationException("No Codex session selected.");VerifyProcess();
  var root=AutomationElement.FromHandle(hwnd);
  var mode=root.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,"Switch mode, current mode: Codex")));
  if(mode==null)throw new InvalidOperationException("Codex mode cannot be verified. Select Codex in the app.");
  var doc=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"RootWebArea"));
  if(doc==null)throw new InvalidOperationException("Codex chat is not accessible yet.");
  var candidates=new List<AutomationElement>();
  var anchors=root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"Stop dictation"),new PropertyCondition(AutomationElement.NameProperty,"Cancel dictation"),new PropertyCondition(AutomationElement.NameProperty,"Dictate")))));
  foreach(AutomationElement anchor in anchors){
   if(anchor.Current.ControlType==ControlType.Edit&&!IsComposerEditor(anchor))continue;
   var ancestor=anchor;
   for(int depth=0;depth<6;depth++){ancestor=TreeWalker.RawViewWalker.GetParent(ancestor);if(ancestor==null)break;if((ancestor.Current.ClassName??"").StartsWith("ComposerLayoutBody-",StringComparison.Ordinal)){if(!candidates.Any(c=>Id(c)==Id(ancestor)))candidates.Add(ancestor);break;}}
  }
  if(candidates.Count==0&&pinned&&savedSurface!=null&&(savedSurface.Current.ClassName??"").StartsWith("ComposerLayoutBody-",StringComparison.Ordinal))candidates.Add(savedSurface);
  if(candidates.Count!=1)throw new InvalidOperationException("Main Codex composer not uniquely available. No controls clicked.");
  var surface=candidates[0];savedSurface=surface;var s=new CodexSnapshot{Root=root,Surface=surface,Chat=doc.Current.Name,SurfaceId=Id(surface)};
  if(pinned&&s.Chat!=chat)throw new InvalidOperationException("Codex chat changed. Nothing sent; wake again in the intended chat.");
  var edits=new List<AutomationElement>();
  foreach(AutomationElement e in anchors){
   if(!IsComposerEditor(e))continue;
   var parent=TreeWalker.RawViewWalker.GetParent(e);
   for(int depth=0;parent!=null&&depth<6;depth++,parent=TreeWalker.RawViewWalker.GetParent(parent)){
    if(parent.Equals(surface)){edits.Add(e);break;}
   }
  }
  if(edits.Count==0){
   // Chromium can omit a contenteditable from a cross-process FindAll while
   // still exposing it in the raw tree. Only inspect the known composer.
   var walker=TreeWalker.RawViewWalker;
   for(var child=walker.GetFirstChild(surface);child!=null;child=walker.GetNextSibling(child))
    if(IsComposerEditor(child))edits.Add(child);
  }
  if(edits.Count>1)throw new InvalidOperationException("More than one composer editor. Nothing sent.");s.Editor=edits.FirstOrDefault();
  s.Text=s.Editor==null?"":ReadText(s.Editor);
  s.Buttons=surface.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)).Cast<AutomationElement>().ToArray();s.ButtonNames=s.Buttons.Select(b=>b.Current.Name).ToArray();cachedMode=mode;cachedDocument=doc;snapshot=s;snapshotAt=Environment.TickCount;return s;
 }
 static bool IsComposerEditor(AutomationElement element){
  var info=element.Current;
  if(info.ControlType!=ControlType.Edit||info.IsPassword)return false;
  return info.Name=="Work with Codex"||info.Name=="Ask Codex"||
   (info.ClassName??"").Split(' ').Contains("ProseMirror");
 }
 static string ReadText(AutomationElement editor){
  object p;string text="";
  if(editor.TryGetCurrentPattern(ValuePattern.Pattern,out p))text=((ValuePattern)p).Current.Value;
  else if(editor.TryGetCurrentPattern(TextPattern.Pattern,out p))text=((TextPattern)p).DocumentRange.GetText(-1);
  else throw new InvalidOperationException("Codex composer text is not readable.");
  text=(text??"").TrimEnd('\r','\n');if(text=="Work with Codex"||text=="Ask Codex")return "";return text;
 }
 static void Invoke(AutomationElement button){if(button==null||!button.Current.IsEnabled)throw new InvalidOperationException("Codex control is not ready. Nothing clicked.");object pattern;if(!button.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))throw new InvalidOperationException("Codex control cannot be invoked reliably. No fallback click used.");((InvokePattern)pattern).Invoke();}
 public string Start(Func<bool> cancelled=null){
  var timer=Stopwatch.StartNew();
  if(Bound&&!IsBoundWindowAlive())Forget();
  if(!Bound)Bind(true,cancelled);
  using(var background=HideDuringOperation()){var s=Read(true,true);
  if(s.Recording){if(!ownedRecording)throw new InvalidOperationException("Codex is already recording outside this control. Stop it first.");return "Codex dictation is already recording.";}
  var start=s.Button("Dictate");if(start==null)throw new InvalidOperationException("Codex Dictate button is unavailable. Finish the current dialog first.");
  CheckCancelled(cancelled);Invoke(start);ownedRecording=true;snapshot=null;
  var trace=new StringBuilder();
  for(int i=0;i<100;i++){CheckCancelled(cancelled);Thread.Sleep(75);var after=Read(true,true);if(i%10==0)trace.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff")+" recording="+after.Recording);if(after.Recording){Audit("recordConfirmedMs="+timer.ElapsedMilliseconds);return "Recording in Codex · say Stop, Send or Run";}}
  File.WriteAllText(Path.Combine(Program.Root,"build","record-control-trace.txt"),trace.ToString());
  throw new InvalidOperationException("Codex recording did not confirm. Check its microphone permission; no second click was made.");
  }
 }
 public string Stop(){
  if(!Bound)throw new InvalidOperationException("No recording started from this control.");using(var background=HideDuringOperation()){var s=Read(true,true);
  if(s.Recording){if(!ownedRecording)throw new InvalidOperationException("This recording was started elsewhere. Nothing clicked.");Invoke(s.Button("Stop dictation"));snapshot=null;}
  var timer=Stopwatch.StartNew();
  string stable=null;int same=0;
  while(timer.ElapsedMilliseconds<36000){
   Thread.Sleep(100);s=Read(true,true);var dictate=s.Button("Dictate");
   if(!s.Recording&&s.Editor!=null&&dictate!=null&&dictate.Current.IsEnabled&&s.Button("Cancel dictation")==null){
    if(stable==s.Text)same++;else{same=0;stable=s.Text;}
    if(same>=2){ownedRecording=false;return s.Text;}
   }else{stable=null;same=0;}
  }
  ownedRecording=false;throw new InvalidOperationException("Codex is still transcribing. Draft was retained; try Send after it finishes.");
  }
 }
 public string Preview(){if(!Bound)Bind();return Read(true).Text;}
 public bool IsRecording(){return Bound&&Read(true,true).Recording;}
 public void CancelOwned(){if(!ownedRecording||!Bound)return;if(!IsBoundWindowAlive()){Forget();return;}using(var background=HideDuringOperation()){var s=Read(true,true);if(s.Recording){Invoke(s.Button("Cancel dictation")??s.Button("Stop dictation"));for(int i=0;i<30;i++){Thread.Sleep(75);if(!Read(true,true).Recording)break;}}ownedRecording=false;snapshot=null;}}
 public void Forget(){hwnd=IntPtr.Zero;hostPid=0;onBackgroundDesktop=false;chat=null;ownedRecording=false;savedSurface=null;snapshot=null;cachedMode=cachedDocument=null;}
 public void ReleaseBackground(){backgroundHost.Dispose();}
 public bool CloseBackground(){if(onBackgroundDesktop)Forget();return backgroundHost.CloseWindows();}
 public string Send(Func<bool> cancelled=null){
  using(var background=HideDuringOperation()){
  var s=Read(true,true);if(s.Recording)throw new InvalidOperationException("Recording must finish before sending.");
  if(String.IsNullOrWhiteSpace(s.Text))throw new InvalidOperationException("Draft is empty. Nothing sent.");
  // Never invoke generic 'Stop' or the recording row's 'Finish dictation and submit'.
  AutomationElement send=null;for(int i=0;i<50;i++){if(cancelled!=null&&cancelled())throw new OperationCanceledException("Pending send cancelled.");s=Read(true,i>0);send=s.Button("Send message")??s.Button("Send")??s.Button("Steer");if(send!=null&&send.Current.IsEnabled)break;Thread.Sleep(60);}
  if(send==null||!send.Current.IsEnabled)throw new InvalidOperationException("Codex Send button unavailable. Draft retained.");
  if(cancelled!=null&&cancelled())throw new OperationCanceledException("Pending send cancelled.");Audit("action="+send.Current.Name+" draftChars="+s.Text.Length+" invokedOnce=true");Invoke(send);snapshot=null;
  for(int i=0;i<80;i++){Thread.Sleep(75);var after=Read(true,true);if(String.IsNullOrWhiteSpace(after.Text)){Audit("submittedDraftCleared=true");Forget();return "Submitted to Codex · waiting for Hello Codex";}}
  Forget();throw new InvalidOperationException("Send was clicked once; result is uncertain. Inspect Codex before sending again.");
  }
 }
 public string Check(){Bind();var s=Read(true);return "Verified installed OpenAI Codex app\nCodex mode confirmed\nMain composer isolated from question popups\nChat identity pinned\nEditorReadable="+(s.Editor!=null)+"\nComposer characters="+s.Text.Length+"\nRecording="+s.Recording+"\nButtons="+String.Join(", ",s.ButtonNames)+"\nNo UI action performed.\n";}
 internal string Benchmark(){var timer=Stopwatch.StartNew();Bind();long bind=timer.ElapsedMilliseconds;var times=new List<long>();for(int i=0;i<8;i++){timer.Restart();Read(true);times.Add(timer.ElapsedMilliseconds);}times.Sort();return "BindMs="+bind+"\nReadMedianMs="+times[times.Count/2]+"\nReadMaxMs="+times[times.Count-1]+"\n";}
 internal static string CheckColdStart(){
  string profile=Path.Combine(Program.Root,"build","cold-start-test-"+Guid.NewGuid().ToString("N"));
  var host=new BackgroundCodex(profile);
  var bridge=new CodexBridge(host,true);
  var report=new StringBuilder();
  var timer=Stopwatch.StartNew();
  int inputDesktopSamples=0;
  IntPtr foreground=Native.GetForegroundWindow();
  int foregroundChanges=0;
  using(var finished=new ManualResetEvent(false)){
   var watcher=new Thread(delegate(){
    while(!finished.WaitOne(25)){
     int pid=host.StartedProcessId;
     if(pid==0)continue;
     if(Native.GetForegroundWindow()!=foreground)Interlocked.Increment(ref foregroundChanges);
     try{if(BackgroundCodex.HasProcessOnInputDesktop(pid))Interlocked.Increment(ref inputDesktopSamples);}
     catch(System.ComponentModel.Win32Exception){Interlocked.Increment(ref inputDesktopSamples);}
    }
   }){IsBackground=true};
   if(host.OpenExisting())throw new InvalidOperationException("The scratch background desktop already exists.");
   watcher.Start();
   try{
    using(var recognized=new ManualResetEvent(false))
    using(var speech=new System.Speech.Synthesis.SpeechSynthesizer())
    using(var audio=new MemoryStream())
    using(var listener=new CommandListener()){
     var format=new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,System.Speech.AudioFormat.AudioChannel.Mono);
     speech.SetOutputToAudioStream(audio,format);
     speech.Speak("Hello Codex");
     speech.SetOutputToNull();
     byte[] silence=new byte[32000];
     audio.Write(silence,0,silence.Length);
     audio.Position=0;
     listener.Command+=delegate(VoiceAction action,string phrase,float confidence){if(action==VoiceAction.Wake)recognized.Set();};
     listener.Start(false,audio);
     if(!recognized.WaitOne(8000))throw new InvalidOperationException("The command listener did not recognize the synthetic Hello Codex wake.");
     report.AppendLine("PASS Hello Codex accepted by the production command listener");
    }
    timer.Restart();
    report.AppendLine(bridge.Start());
    int pid=host.StartedProcessId;
    if(pid==0||!bridge.IsRecording())throw new InvalidOperationException("The newly started process did not begin native dictation.");
    report.AppendLine("ColdRecordMs="+timer.ElapsedMilliseconds);
    if(BackgroundCodex.HasProcessOnInputDesktop(pid))throw new InvalidOperationException("Codex created a window on the input desktop.");
    Thread.Sleep(800);
    timer.Restart();
    bridge.Stop();
    if(bridge.IsRecording())throw new InvalidOperationException("The native recording did not stop.");
    report.AppendLine("NativeStopMs="+timer.ElapsedMilliseconds);
    bridge.Forget();
    bridge.Start();
    if(host.StartedProcessId!=pid)throw new InvalidOperationException("A second wake did not reuse the background host.");
    bridge.CancelOwned();
    if(bridge.IsRecording())throw new InvalidOperationException("The second recording did not cancel.");
    if(inputDesktopSamples!=0||BackgroundCodex.HasProcessOnInputDesktop(pid))
     throw new InvalidOperationException("The background host was detected on the input desktop.");
    report.AppendLine("PASS native cold Record, Stop and repeated wake");
    report.AppendLine("PASS zero windows from the background host on the input desktop");
    report.AppendLine("InputForegroundUnchanged="+(foregroundChanges==0&&Native.GetForegroundWindow()==foreground));
    report.AppendLine("No draft was submitted.");
    return report.ToString();
   }finally{
    finished.Set();
    watcher.Join();
    try{bridge.CancelOwned();}finally{host.FinishSmokeTest();}
   }
  }
 }
 internal string CheckBackgroundRecording(bool minimized=true){
  Bind();var before=Read(true,true);if(before.Recording)throw new InvalidOperationException("Another recording is active. Test skipped.");
  Native.ShowWindow(hwnd,6);
  Thread.Sleep(200);
  if(!minimized){
   Native.SetWindowPos(hwnd,new IntPtr(1),0,0,0,0,0x0001|0x0002|0x0010);
   Native.ShowWindow(hwnd,4);
   Native.SetWindowPos(hwnd,new IntPtr(1),0,0,0,0,0x0001|0x0002|0x0010);
  }
  Thread.Sleep(650);
  var foreground=Native.GetForegroundWindow();
  if(foreground==hwnd)throw new InvalidOperationException("The test needs a different foreground app.");
  var report=new StringBuilder();report.AppendLine("InitialMinimized="+minimized);var timer=Stopwatch.StartNew();
  int visibleHostForegroundSamples=0;var desktop=System.Windows.Forms.SystemInformation.VirtualScreen;string firstPopup=null;int stage=0;
  using(var done=new ManualResetEvent(false)){
   var watcher=new Thread(()=>{while(!done.WaitOne(2)){Native.Rect rect;if(Native.GetForegroundWindow()==hwnd&&!Native.IsIconic(hwnd)&&Native.IsWindowVisible(hwnd)&&Native.GetWindowRect(hwnd,out rect)&&System.Drawing.Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom).IntersectsWith(desktop)&&!Native.HasZeroOpacity(hwnd)){if(Interlocked.Increment(ref visibleHostForegroundSamples)==1)firstPopup="stage="+Volatile.Read(ref stage)+" ms="+timer.ElapsedMilliseconds+" rect="+rect.Left+","+rect.Top+","+rect.Right+","+rect.Bottom;}}});watcher.IsBackground=true;watcher.Start();
   try{
    report.AppendLine(Start());report.AppendLine("StartMs="+timer.ElapsedMilliseconds);
    if(Native.IsIconic(hwnd)!=minimized)throw new Exception("Record changed the original window state.");
    report.AppendLine("ForegroundRetainedAfterRecord="+(Native.GetForegroundWindow()==foreground));
    report.AppendLine("PASS native Record kept the original window state");Volatile.Write(ref stage,1);Thread.Sleep(700);timer.Restart();Volatile.Write(ref stage,2);string text=Stop();report.AppendLine("StopMs="+timer.ElapsedMilliseconds);
    if(Native.IsIconic(hwnd)!=minimized)throw new Exception("Stop changed the original window state.");
    report.AppendLine("ForegroundRetainedAfterStop="+(Native.GetForegroundWindow()==foreground));
    report.AppendLine("PASS native Stop/preview kept the original window state");
    Thread.Sleep(500);
    if(!text.StartsWith(before.Text,StringComparison.Ordinal))throw new Exception("Original draft changed during native test.");
    if(visibleHostForegroundSamples!=0)throw new Exception("Codex appeared on the desktop foreground. Samples="+visibleHostForegroundSamples+" "+firstPopup+"\n"+report);
    report.AppendLine("PASS foreground watcher: no visible Codex popup observed");report.AppendLine("OriginalDraftRetained=True");report.AppendLine("Nothing sent; test did not type or erase draft text.");return report.ToString();
   }finally{done.Set();watcher.Join();CancelOwned();if(!minimized)Native.ShowWindow(hwnd,7);}
  }
 }
 static void Audit(string message){try{string path=Path.Combine(Program.Data,"ui-actions.log");if(File.Exists(path)&&new FileInfo(path).Length>32768)File.WriteAllText(path,"");File.AppendAllText(path,DateTime.Now.ToString("s")+" "+message+"\n");}catch{}}
}
// A minimized WebView can defer InvokePattern. Restore it without activation
// only while its opacity is zero, then return it to its previous show state.
sealed class BackgroundWindow:IDisposable {
 readonly IntPtr window,foreground;
 readonly bool minimized;
 WindowOpacity opacity;

 public BackgroundWindow(IntPtr handle){
  window=handle;
  foreground=Native.GetForegroundWindow();
  minimized=Native.IsIconic(window);
  if(window==foreground&&!minimized)return;

  opacity=new WindowOpacity(window);
  try {
   if(minimized)Native.ShowWindow(window,4); // SW_SHOWNOACTIVATE
   if(!Native.HasZeroOpacity(window))
    throw new InvalidOperationException("Codex did not stay hidden. No dictation control was clicked.");
  }catch{Dispose();throw;}
 }

 public void Dispose(){
  if(opacity==null)return;
  var originalOpacity=opacity;
  opacity=null;
  try {
   // The native composer can focus the host after the accessibility action
   // has returned. Keep it transparent until this transition settles.
   if(!minimized)Thread.Sleep(550);
   RestoreForegroundIfNeeded();
   if(minimized||Native.GetForegroundWindow()==window){
    // If Codex took focus, minimize it and let Windows activate the next app.
    Native.ShowWindow(window,Native.GetForegroundWindow()==window?6:7);
    for(int attempt=0;attempt<20&&!Native.IsIconic(window);attempt++)Thread.Sleep(25);
    if(!Native.IsIconic(window))Native.ShowWindow(window,0); // SW_HIDE
   }
   RestoreForegroundIfNeeded();
   if(!minimized&&Native.GetForegroundWindow()!=window)
    Native.SetWindowPos(window,new IntPtr(1),0,0,0,0,0x0001|0x0002|0x0010);
  }finally{originalOpacity.Dispose();}
 }
 void RestoreForegroundIfNeeded(){
  IntPtr active=Native.GetForegroundWindow();
  if(foreground!=IntPtr.Zero&&(active==window||active==IntPtr.Zero))
   Native.SetForegroundWindow(foreground);
 }
}

// Hide the native host pixels without changing the renderer's screen bounds.
sealed class WindowOpacity:IDisposable {
 const int ExtendedStyle=-20;
 const long Layered=0x80000;
 const uint UseAlpha=2;
 readonly IntPtr window;
 readonly bool originallyLayered;
 readonly uint originalColor,originalFlags;
 readonly byte originalAlpha;
 bool disposed;
 public WindowOpacity(IntPtr handle){
  window=handle;
  long style=Native.GetWindowLongPtr(window,ExtendedStyle).ToInt64();
  originallyLayered=(style&Layered)!=0;
  if(originallyLayered&&!Native.GetLayeredWindowAttributes(window,out originalColor,out originalAlpha,out originalFlags))
   throw new InvalidOperationException("Could not save the Codex window's opacity.");
  if(!originallyLayered)Native.SetWindowLongPtr(window,ExtendedStyle,new IntPtr(style|Layered));
  if(!Native.SetLayeredWindowAttributes(window,0,0,UseAlpha)){
   Dispose();
   throw new InvalidOperationException("Could not hide the Codex window. No dictation control was clicked.");
  }
  Native.DwmFlush();
 }

 public void Dispose(){
  if(disposed)return;disposed=true;
  Native.DwmFlush();
  if(originallyLayered)Native.SetLayeredWindowAttributes(window,originalColor,originalAlpha,originalFlags);
  else{
   long current=Native.GetWindowLongPtr(window,ExtendedStyle).ToInt64();
   Native.SetWindowLongPtr(window,ExtendedStyle,new IntPtr(current&~Layered));
  }
 }
}
static class Native {
 [DllImport("dwmapi.dll")]public static extern int DwmFlush();
 [DllImport("user32.dll")]public static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll")]public static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
 [DllImport("user32.dll")]public static extern bool SetLayeredWindowAttributes(IntPtr h,uint color,byte alpha,uint flags);
 [DllImport("user32.dll")]public static extern bool GetLayeredWindowAttributes(IntPtr h,out uint color,out byte alpha,out uint flags);
 public static bool HasZeroOpacity(IntPtr h){uint color,flags;byte alpha;return GetLayeredWindowAttributes(h,out color,out alpha,out flags)&&(flags&2)!=0&&alpha==0;}
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int show);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 [DllImport("user32.dll")]public static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
 [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder name,int count);
 public static void PlaceBehindApps(IntPtr target){IntPtr bottom=IntPtr.Zero;for(IntPtr h=GetWindow(GetForegroundWindow(),0);h!=IntPtr.Zero;h=GetWindow(h,2)){if(h==target||!IsWindowVisible(h))continue;var cls=new StringBuilder(128);GetClassName(h,cls,cls.Capacity);string n=cls.ToString();if(n=="Progman"||n=="WorkerW"||n=="Shell_TrayWnd"||n=="Shell_SecondaryTrayWnd")continue;bottom=h;}SetWindowPos(target,bottom==IntPtr.Zero?new IntPtr(-2):bottom,0,0,0,0,0x0001|0x0002|0x0010);}
}
}
