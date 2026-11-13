using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CodexVoice {
enum SessionState {Idle,Starting,Recording,Transcribing,Ready,Sending,Paused,Error}
sealed class Companion:Form {
 readonly CodexBridge bridge=new CodexBridge();readonly CommandListener listener=new CommandListener();
 readonly OrbVisual visual=new OrbVisual();
 readonly NotifyIcon tray=new NotifyIcon();readonly ContextMenuStrip menu=new ContextMenuStrip();
 readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();readonly System.Windows.Forms.Timer previewTimer=new System.Windows.Forms.Timer();
 readonly Panel panel=new Panel();readonly Label status=new Label();readonly Label title=new Label();readonly TextBox preview=new TextBox();
 readonly Button start=new Button(),stop=new Button(),send=new Button(),pause=new Button();
 readonly ToolTip tip=new ToolTip();readonly bool designMode;SessionState phase=SessionState.Idle;bool busy,reading,expanded,dragged,closing,closedCleanly;Point dragAt;int orbSize,epoch,lastStatusTick,matchedCommands,lastAudioTick;float lastConfidence;double micLevel;DateTime lastAction=DateTime.MinValue;
 string message="Ready · Hello / Okay / Hey Codex";Icon ownedIcon;
 public Companion(bool previewOnly=false){designMode=previewOnly;
  Text="CODEX Dictation Control";ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
  BackColor=Color.FromArgb(1,2,3);TransparencyKey=BackColor;DoubleBuffered=true;ForeColor=Color.FromArgb(106,245,173);Font=new Font("Segoe UI",9);
  orbSize=Math.Max(56,Math.Min(104,Program.Config.Size));ClientSize=new Size(orbSize+20,orbSize+38);
  Rectangle wa=Screen.PrimaryScreen.WorkingArea;Location=Program.Config.X>=0?new Point(Program.Config.X,Program.Config.Y):new Point(wa.Right-130,wa.Bottom-170);Clamp();
  panel.BackColor=Color.FromArgb(14,23,22);panel.Padding=new Padding(14);panel.Visible=false;
  title.Text="CODEX  /  DICTATION";title.ForeColor=Color.FromArgb(128,245,187);title.Font=new Font("Segoe UI",10,FontStyle.Bold);title.SetBounds(14,12,290,24);
  status.ForeColor=Color.FromArgb(187,205,197);status.SetBounds(14,40,320,48);
  preview.Multiline=true;preview.ReadOnly=true;preview.ScrollBars=ScrollBars.Vertical;preview.BackColor=Color.FromArgb(20,32,29);preview.ForeColor=Color.FromArgb(230,240,234);preview.BorderStyle=BorderStyle.FixedSingle;preview.Font=new Font("Segoe UI",10);preview.SetBounds(14,92,322,150);
  SetupButton(start,"Record",14,252);SetupButton(stop,"Stop",96,252);SetupButton(send,"Send",178,252);SetupButton(pause,"Pause mic",260,252);
  start.Click+=delegate{Dispatch(VoiceAction.Start,false);};stop.Click+=delegate{Dispatch(VoiceAction.Stop,false);};send.Click+=delegate{Dispatch(VoiceAction.Send,false);};pause.Click+=delegate{TogglePause();};
  var note=new Label{Text="Preview of the actual Codex draft · never a separate recording",ForeColor=Color.FromArgb(116,143,130),Font=new Font("Segoe UI",8),Bounds=new Rectangle(14,292,322,34)};
  panel.Controls.AddRange(new Control[]{title,status,preview,start,stop,send,pause,note});Controls.Add(panel);
  BuildMenu();ContextMenuStrip=menu;
  ownedIcon=CreateIcon();tray.Icon=ownedIcon;tray.Text="CODEX · native dictation control";tray.ContextMenuStrip=menu;tray.Visible=!designMode;tray.DoubleClick+=delegate{RestoreOrb();};
  MouseDown+=Down;MouseMove+=MoveOrb;MouseUp+=Up;
  listener.Command+=delegate(VoiceAction action,string phrase,float confidence){OnUi(delegate{matchedCommands++;lastConfidence=confidence;Dispatch(action,true);});};
  listener.Level+=delegate(int level){micLevel=level/100.0;lastAudioTick=Environment.TickCount;};listener.Status+=delegate(string text){OnUi(delegate{SetState(SessionState.Error,text);});};
  animation.Interval=100;animation.Tick+=delegate{int now=Environment.TickCount;double energy=unchecked((uint)(now-lastAudioTick))>300?0:micLevel;visual.Tick(phase,energy,animation.Interval/1000.0);if(Visible&&!Native.IsIconic(Handle)&&Native.WindowFromPoint(PointToScreen(new Point(orbSize/2+10,orbSize/2+10)))==Handle)Invalidate(new Rectangle(0,0,orbSize+20,orbSize+38));if(unchecked((uint)(now-lastStatusTick))>=5000){lastStatusTick=now;WriteRuntimeStatus();}};VisibleChanged+=delegate{animation.Interval=Visible?(phase==SessionState.Recording?33:100):500;};
  previewTimer.Interval=250;previewTimer.Tick+=async delegate{if(busy||reading||!expanded||!bridge.Bound)return;reading=true;try{string text=await Task.Run(()=>bridge.Preview());if(!busy)SetPreview(text);}catch(Exception e){SetState(SessionState.Error,e.Message);}finally{reading=false;}};
  Shown+=async delegate{if(designMode)return;Native.RegisterHotKey(Handle,1,0x0001|0x0002|0x4000,(uint)Keys.Escape);Native.RegisterHotKey(Handle,2,0x0001|0x0002|0x4000,(uint)Keys.D);Startup.Set(Program.Config.AutoStart);animation.Start();previewTimer.Start();if(Program.Config.Hidden)Hide();if(Program.Config.ListenerEnabled)await EnableListener();else SetState(SessionState.Paused,"Command microphone is off · click Record to use Codex");};
  FormClosing+=async delegate(object sender,FormClosingEventArgs e){if(!closedCleanly){e.Cancel=true;if(closing)return;closing=true;epoch++;listener.Stop();while(busy||reading)await Task.Delay(50);try{await Task.Run(()=>bridge.CancelOwned());}catch(Exception ex){Program.Log(ex);}closedCleanly=true;Close();return;}animation.Stop();previewTimer.Stop();listener.Dispose();Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);tray.Visible=false;tray.Dispose();ownedIcon.Dispose();menu.Dispose();tip.Dispose();Program.Config.X=Left;Program.Config.Y=Top;Program.Config.Save();};
  SetState(SessionState.Idle,message);
 }
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x00000080;return p;}}
 void OnUi(Action a){if(closing||IsDisposed||!IsHandleCreated)return;try{BeginInvoke(a);}catch(InvalidOperationException){}}
 void SetupButton(Button b,string text,int x,int y){b.Text=text;b.SetBounds(x,y,76,30);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(41,78,59);b.BackColor=Color.FromArgb(22,43,33);b.ForeColor=ForeColor;b.TabStop=true;}
 void BuildMenu(){
  menu.RenderMode=ToolStripRenderMode.System;
  menu.Items.Add("Show preview",null,delegate{RestoreOrb();Expand(!expanded);});
  menu.Items.Add("Record in Codex",null,delegate{Dispatch(VoiceAction.Start,false);});menu.Items.Add("Stop dictation",null,delegate{Dispatch(VoiceAction.Stop,false);});menu.Items.Add("Send current draft",null,delegate{Dispatch(VoiceAction.Send,false);});
  menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Pause / resume command microphone",null,delegate{TogglePause();});
  var sizes=new ToolStripMenuItem("Orb size");foreach(int n in new[]{56,68,76,92}){int value=n;sizes.DropDownItems.Add(value+" px",null,delegate{orbSize=value;Program.Config.Size=value;Program.Config.Save();LayoutUi();});}menu.Items.Add(sizes);
  var startup=new ToolStripMenuItem("Start when I sign in"){Checked=Program.Config.AutoStart,CheckOnClick=true};startup.Click+=delegate{try{Startup.Set(startup.Checked);}catch(Exception e){SetState(SessionState.Error,e.Message);}};menu.Items.Add(startup);
  menu.Items.Add("Hide orb (keep tray)",null,delegate{Program.Config.Hidden=true;Program.Config.Save();Hide();});
  menu.Items.Add("Show Codex",null,delegate{ShowCodex();});
  menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Stop completely (until I open it again)",null,delegate{StopCompletely();});
 }
 void RestoreOrb(){Program.Config.Hidden=false;Program.Config.Save();Show();Clamp();}
 async Task EnableListener(){SetState(SessionState.Starting,"Opening the small command microphone…");try{await Task.Run(()=>listener.Start(bridge.Bound));SetState(bridge.Bound?SessionState.Ready:SessionState.Idle,bridge.Bound?"Ready · Start, Send or Run":"Ready · Hello / Okay / Hey Codex");}catch(Exception e){SetState(SessionState.Error,e.Message);}}
 async void TogglePause(){if(listener.Running){Emergency();}else{if(busy)return;Program.Config.ListenerEnabled=true;Program.Config.Save();await EnableListener();}}
 async void Dispatch(VoiceAction action,bool fromVoice){
  if(busy||closing)return;if((DateTime.UtcNow-lastAction).TotalMilliseconds<700)return;
  if(action==VoiceAction.Send&&!bridge.Bound)return;
  if(action==VoiceAction.Stop&&!bridge.Bound)return;
  if(action==VoiceAction.Wake&&phase==SessionState.Recording)return;
  busy=true;int actionEpoch=epoch;lastAction=DateTime.UtcNow;while(reading)await Task.Delay(25);
  try {
   if(action==VoiceAction.Wake||action==VoiceAction.Start){
    if(phase==SessionState.Error)bridge.RebindIfSafe();RestoreOrb();SetState(SessionState.Starting,"Opening Codex's own dictation…");
    string msg=await Task.Run(()=>bridge.Start());if(actionEpoch!=epoch){await Task.Run(()=>bridge.CancelOwned());return;}listener.SetActive(true);SetState(SessionState.Recording,msg);
   }else if(action==VoiceAction.Stop){
    SetState(SessionState.Transcribing,"Codex is finishing your transcript…");string text=await Task.Run(()=>bridge.Stop());
    SetPreview(text);listener.SetActive(true);SetState(SessionState.Ready,"Draft stays in Codex · Start to add more · Send or Run to submit");
   }else if(action==VoiceAction.Send){
    bool wasRecording=await Task.Run(()=>bridge.IsRecording());
    if(wasRecording){SetState(SessionState.Transcribing,"Finishing Codex dictation before sending…");string text=await Task.Run(()=>bridge.Stop());SetPreview(text);
     if(fromVoice){bool confirmed;string content=Commands.TrimTrailingCommand(text,VoiceAction.Send,out confirmed);if(!confirmed||String.IsNullOrWhiteSpace(content))throw new InvalidOperationException("Codex did not confirm text followed by Send/Run. Review the draft, then say Send again.");}
    }
    if(actionEpoch!=epoch)return;
    SetState(SessionState.Sending,"Sending the prepared Codex draft once…");string result=await Task.Run(()=>bridge.Send(()=>actionEpoch!=epoch));SetPreview("");listener.SetActive(false);SetState(SessionState.Idle,result);
   }
  }catch(OperationCanceledException){SetState(SessionState.Paused,"Pending send cancelled · draft stays in Codex");}catch(Exception e){
   Program.Log(e);
   SetState(SessionState.Error,e.Message);
   if(bridge.Bound)listener.SetActive(true);
   else tray.ShowBalloonTip(4500,Product.Name,e.Message,ToolTipIcon.Info);
  }
  finally{busy=false;UpdateButtons();}
 }
 void SetPreview(string text){if(preview.Text!=text)preview.Text=text??"";}
 void SetState(SessionState state,string text){phase=state;message=text;status.Text=text;ApplyLayer();tray.Text=("CODEX · "+state.ToString()).Substring(0,Math.Min(63,("CODEX · "+state.ToString()).Length));tip.SetToolTip(this,text+"\nClick = Record/Stop · arrow = preview · right-click = Stop completely");animation.Interval=Visible?(state==SessionState.Recording?33:100):500;UpdateButtons();Invalidate();}
 void UpdateButtons(){start.Enabled=!busy;stop.Enabled=!busy&&bridge.Bound;send.Enabled=!busy&&bridge.Bound;pause.Enabled=!busy;pause.Text=listener.Running?"Pause":"Resume";}
 void Expand(bool value){expanded=value;ApplyLayer();LayoutUi();if(value&&bridge.Bound&&!busy&&!reading){previewTimer.Stop();previewTimer.Start();}}
 void LayoutUi(){panel.SetBounds(6,orbSize+42,350,330);panel.Visible=expanded;ClientSize=expanded?new Size(362,orbSize+380):new Size(orbSize+20,orbSize+38);using(var path=new GraphicsPath()){path.AddEllipse(7,7,orbSize+6,orbSize+6);path.AddRectangle(new Rectangle(orbSize/2-8,orbSize+8,34,30));if(expanded){path.AddRectangle(new Rectangle(6,orbSize+42,350,330));}var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}Clamp();Invalidate();}
 void Clamp(){if(designMode)return;var wa=Screen.FromPoint(Location).WorkingArea;Location=new Point(Math.Max(wa.Left,Math.Min(Left,wa.Right-Width)),Math.Max(wa.Top,Math.Min(Top,wa.Bottom-Height)));}
 void Down(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;Capture=true;dragAt=e.Location;dragged=false;}
 void MoveOrb(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;if(Math.Abs(e.X-dragAt.X)+Math.Abs(e.Y-dragAt.Y)>4)dragged=true;if(dragged)Location=new Point(Left+e.X-dragAt.X,Top+e.Y-dragAt.Y);}
 void Up(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;Capture=false;if(dragged){Clamp();Program.Config.X=Left;Program.Config.Y=Top;Program.Config.Save();return;}if(e.Y>orbSize+7){Expand(!expanded);return;}Dispatch(phase==SessionState.Recording?VoiceAction.Stop:VoiceAction.Start,false);}
 void ShowCodex(){foreach(var p in Process.GetProcessesByName("ChatGPT")){try{if(p.MainWindowHandle!=IntPtr.Zero&&CodexBridge.AllowedPath(p.MainModule.FileName)){Native.ShowWindow(p.MainWindowHandle,9);Native.SetForegroundWindow(p.MainWindowHandle);return;}}catch{}finally{p.Dispose();}}Expand(true);SetState(SessionState.Error,"Open Codex from the Windows Start menu, choose a chat, then leave it minimized.");}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float cx=(orbSize+20)/2f,cy=orbSize/2f+10;visual.Draw(e.Graphics,cx,cy,orbSize);using(var p=new Pen(Color.FromArgb(150,visual.Accent),1.2f)){float yy=orbSize+20;e.Graphics.DrawLines(p,expanded?new[]{new PointF(cx-4,yy+3),new PointF(cx,yy),new PointF(cx+4,yy+3)}:new[]{new PointF(cx-4,yy),new PointF(cx,yy+3),new PointF(cx+4,yy)});}}
 static Icon CreateIcon(){using(var bmp=new Bitmap(32,32)){using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(Color.FromArgb(51,226,139),2.5f)){g.DrawEllipse(p,4,4,24,24);g.DrawArc(p,8,6,16,20,20,270);}using(var b=new SolidBrush(Color.FromArgb(170,255,201)))g.FillEllipse(b,13,13,6,6);}IntPtr h=bmp.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}}}
 protected override void OnLoad(EventArgs e){base.OnLoad(e);LayoutUi();}
 internal void CaptureDesign(){Location=new Point(-12000,-12000);Show();Expand(true);SetState(SessionState.Ready,"Your words stay in Codex. Review here, then say Send.");preview.Text="আমি বাংলায় কথা বলছি, and I can switch to English.\r\n\r\nThis preview mirrors the Codex draft.";Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(System.IO.Path.Combine(Program.Root,"build","design-preview.png"),System.Drawing.Imaging.ImageFormat.Png);}OrbVisual.SaveStates(System.IO.Path.Combine(Program.Root,"build","orb-states.png"));Hide();}
 void ApplyLayer(){bool front=(busy&&phase==SessionState.Starting)||phase==SessionState.Recording||phase==SessionState.Transcribing||phase==SessionState.Sending||(bridge.Bound&&(phase==SessionState.Ready||phase==SessionState.Error))||expanded;TopMost=front;if(!front&&IsHandleCreated&&!designMode)Native.PlaceBehindApps(Handle);}
 internal void StopCompletely(){Startup.Set(false);Program.Config.StoppedByUser=true;Program.Config.ListenerEnabled=false;Program.Config.Save();Close();}
 internal void Reveal(){RestoreOrb();Native.SetForegroundWindow(Handle);}
 internal void RequestAction(string action){OnUi(delegate{if(action=="stop")StopCompletely();else if(action=="exit")Close();else Reveal();});}
 void WriteRuntimeStatus(){try{System.IO.File.WriteAllText(System.IO.Path.Combine(Program.Data,"runtime-status.txt"),"Pid="+Process.GetCurrentProcess().Id+"\nState="+phase+"\nLayer="+(TopMost?"Foreground":"Desktop")+"\nCommandMicrophone="+listener.Running+"\nSessionSelected="+bridge.Bound+"\nMicLevel="+micLevel.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"\nMatchedCommands="+matchedCommands+"\nLastConfidence="+lastConfidence.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"\nBusy="+busy+"\n");}catch{}}
 async void Emergency(){epoch++;listener.Stop();Program.Config.ListenerEnabled=false;Program.Config.Save();SetState(SessionState.Paused,"Microphone paused · pending send cancelled");while(busy||reading)await Task.Delay(30);try{await Task.Run(()=>bridge.CancelOwned());}catch(Exception ex){Program.Log(ex);}SetState(SessionState.Paused,"Command microphone off · owned Codex recording cancelled");}
 protected override void WndProc(ref Message m){if(m.Msg==0x312){if(m.WParam.ToInt32()==1)Emergency();else Dispatch(phase==SessionState.Recording?VoiceAction.Stop:VoiceAction.Start,false);return;}base.WndProc(ref m);}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr h);
}
}
