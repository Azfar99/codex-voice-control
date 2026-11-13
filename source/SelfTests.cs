using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;
namespace CodexVoice {
static class SelfTests {
 static StringBuilder report;
 static void Check(bool value,string description){if(!value)throw new Exception("FAIL "+description);report.AppendLine("PASS "+description);}
 public static void Run(bool audio){
  report=new StringBuilder();bool removed;
  Check(Commands.TrimTrailingCommand("বাংলা English কথাগুলো। Stop.",VoiceAction.Stop,out removed)=="বাংলা English কথাগুলো।"&&removed,"strip only a final English control word");
  Check(Commands.TrimTrailingCommand("এটা লিখলাম থামো",VoiceAction.Stop,out removed)=="এটা লিখলাম"&&removed,"strip final Bengali Stop");
  Check(Commands.TrimTrailingCommand("Please run the tests",VoiceAction.Send,out removed)=="Please run the tests"&&!removed,"preserve embedded run");
  Check(Commands.TrimTrailingCommand("send this later",VoiceAction.Send,out removed)=="send this later"&&!removed,"preserve leading send");
  Check(Commands.TrimTrailingCommand("run",VoiceAction.Send,out removed)==""&&removed,"empty commands cannot become a message");
  Check(Commands.TrimTrailingCommand("লেখা okay send!",VoiceAction.Send,out removed)=="লেখা"&&removed,"prefer full completion phrase");
  var gate=new SilenceGate();for(int ms=0;ms<=8000;ms+=20)gate.Observe(ms,ms<3000||(ms>=5100&&ms<=5500));
  Check(!gate.Allows(4000,2000),"embedded Stop/Send with a one-second gap is rejected");
  Check(gate.Allows(5100,2000),"standalone command after two quiet seconds is accepted");
  Check(!gate.Allows(6500,2000),"ordinary speech resets the two-second command gap");
  Check(gate.Allows(7700,2000),"commands rearm after a fresh two-second pause");
  Check(!new SilenceGate().Allows(3000,2000),"missing microphone history never authorizes sending");
  Check(Commands.IsExact(VoiceAction.Send,"Send!")&&!Commands.IsExact(VoiceAction.Send,"send this later"),"only a complete command phrase matches");
  var migrated=new Settings{Version=3,Size=72,AutoStart=true,CommandPauseSeconds=2};Check(migrated.Migrate()&&migrated.Size==76&&migrated.AutoStart&&migrated.CommandPauseSeconds==2,"upgrade adds four pixels and preserves user preferences");Check(!migrated.Migrate()&&migrated.Size==76,"size migration runs only once");
  Check(CodexBridge.CacheFresh(1050,1000)&&!CodexBridge.CacheFresh(2100,1000)&&CodexBridge.CacheFresh(Int32.MinValue+50,Int32.MaxValue-50),"accessibility cache expires and handles clock rollover");
  var visual=new OrbVisual();for(int i=0;i<30;i++)visual.Tick(SessionState.Recording,0,.033);Check(visual.Voice<.001,"quiet listening keeps the calm idle wave amplitude");for(int i=0;i<30;i++)visual.Tick(SessionState.Recording,.24,.033);Check(visual.Voice>.8,"real microphone energy creates an exaggerated outward pulse");for(int i=0;i<50;i++)visual.Tick(SessionState.Idle,.6,.1);Check(visual.Voice<.001,"idle does not pulse to unrelated microphone activity");Check(OrbVisual.Tint(SessionState.Transcribing,0,0)!=OrbVisual.Tint(SessionState.Idle,0,0),"frame and waves use one shared context palette");
  var idle=OrbVisual.Tint(SessionState.Idle,0,0);var talking=OrbVisual.Tint(SessionState.Recording,0,1);Check(idle.G>idle.R&&idle.G>idle.B&&talking.R>230&&talking.G>190&&talking.B<100,"idle green and speaking yellow share the complete orb palette");
  using(var received=new System.Threading.ManualResetEvent(false))using(var registration=InstanceControl.Listen("SelfTest"+System.Diagnostics.Process.GetCurrentProcess().Id,()=>received.Set())){Check(InstanceControl.Signal("SelfTest"+System.Diagnostics.Process.GetCurrentProcess().Id)&&received.WaitOne(1500),"per-user stop/show control channel delivers commands");}
  Check(!CodexBridge.AllowedPath(@"C:\Other\ChatGPT.exe"),"reject unrelated ChatGPT-named executable");
  Check(!CodexBridge.AllowedPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_x\app\fake.exe"),"reject wrong executable inside a similar package");
  Check(CodexBridge.AllowedPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.1106.6628.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"),"accept observed installed Codex package");
  using(var recognizer=new SpeechRecognitionEngine(CultureInfo.GetCultureInfo("en-US"))){recognizer.LoadGrammar(Commands.MakeGrammar(false));recognizer.LoadGrammar(Commands.MakeGrammar(true));Check(recognizer.Grammars.Count==2,"English and Bengali command pronunciations compile without a dictation grammar");}
  if(audio)foreach(string phrase in new[]{"Hello Codex","Okay Codex","Hey Codex","Stop","Send","Run"}) {
   using(var stream=new MemoryStream()){
    using(var tts=new SpeechSynthesizer()){tts.SetOutputToWaveStream(stream);tts.Speak(phrase);tts.SetOutputToNull();}stream.Position=0;
    using(var engine=new SpeechRecognitionEngine(CultureInfo.GetCultureInfo("en-US"))){engine.LoadGrammar(Commands.MakeGrammar(true));engine.SetInputToWaveStream(stream);var result=engine.Recognize(TimeSpan.FromSeconds(6));string expected=phrase=="Stop"?"Stop":phrase=="Send"||phrase=="Run"?"Send":"Wake";Check(result!=null&&result.Semantics.ContainsKey("action")&&result.Semantics["action"].Value.ToString()==expected,"in-memory command audio: "+phrase);}
   }
  }
  if(audio){CheckLiveGate("Send",3,true,"isolated Send after three quiet seconds");CheckLiveGate("Stop",3,true,"isolated Stop after three quiet seconds");CheckLiveGate("Run",3,true,"isolated Run after three quiet seconds");CheckLiveGate("Send",.5,false,"Send without the required pause");CheckLiveGate("I said stop and send inside this sentence",3,false,"Stop and Send embedded inside a sentence");CheckLiveGate("stop recording the screen right now",3,false,"a sentence starting with stop is not the standalone command");}
  report.AppendLine("No microphone audio, transcript or chat content saved by these tests.");
  File.WriteAllText(Path.Combine(Program.Root,"build",audio?"voice-tests.txt":"tests.txt"),report.ToString());
 }
 static void CheckLiveGate(string phrase,double quietSeconds,bool expected,string description){
  var format=new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,System.Speech.AudioFormat.AudioChannel.Mono);
  using(var pcm=new MemoryStream()){
   var quiet=new byte[(int)(16000*2*quietSeconds)];pcm.Write(quiet,0,quiet.Length);
   using(var spoken=new MemoryStream()){
    using(var tts=new SpeechSynthesizer()){tts.SetOutputToAudioStream(spoken,format);tts.Speak(phrase);tts.SetOutputToNull();}
    spoken.Position=0;spoken.CopyTo(pcm);
   }
   var ending=new byte[16000*2*2];pcm.Write(ending,0,ending.Length);pcm.Position=0;
   using(var completed=new System.Threading.ManualResetEvent(false))using(var listener=new CommandListener()){
    int accepted=0;listener.Command+=delegate(VoiceAction action,string text,float confidence){if(action==VoiceAction.Send||action==VoiceAction.Stop)System.Threading.Interlocked.Increment(ref accepted);};listener.Completed+=delegate{completed.Set();};
    listener.Start(true,pcm);Check(completed.WaitOne(8000),"audio test completed: "+description);System.Threading.Thread.Sleep(600);Check((accepted>0)==expected,"command-event pipeline: "+description);
   }
  }
 }
}
}
