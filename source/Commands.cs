using System;
using System.Linq;
using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Recognition.SrgsGrammar;
using System.Text.RegularExpressions;
using System.Collections.Generic;
namespace CodexVoice {
enum VoiceAction {Wake,Start,Stop,Send}
sealed class SilenceGate {
 struct Sample {public double Time;public bool Sound;}
 readonly List<Sample> samples=new List<Sample>();readonly object gate=new object();
 public void Observe(double milliseconds,bool sound){lock(gate){samples.Add(new Sample{Time=milliseconds,Sound=sound});if(samples.Count>3500)samples.RemoveAll(s=>s.Time<milliseconds-45000);}}
 public double QuietBefore(double start){lock(gate){if(samples.Count<2||samples[0].Time>start-2000||samples[samples.Count-1].Time<start-300)return 0;double lastSound=samples[0].Time;foreach(var s in samples){if(s.Time>start-120)break;if(s.Sound)lastSound=s.Time;}return Math.Max(0,start-lastSound);}}
 public bool Allows(double start,double required){return QuietBefore(start)>=required;}
 public bool HasSound(double from,double to){lock(gate){return samples.Any(s=>s.Sound&&s.Time>=from&&s.Time<=to);}}
}
sealed class Phrase {
 public string Text,Phones;public VoiceAction Action;
 public Phrase(string text,VoiceAction action,string phones){Text=text;Action=action;Phones=phones;}
}
static class Commands {
 public static readonly Phrase[] Phrases={
  new Phrase("hello codex",VoiceAction.Wake,null),new Phrase("okay codex",VoiceAction.Wake,null),new Phrase("hey codex",VoiceAction.Wake,null),new Phrase("hi codex",VoiceAction.Wake,null),
  new Phrase("কোডেক্স আসো",VoiceAction.Wake,"k ow d eh k s aa sh ow"),new Phrase("কোডেক্স আছো",VoiceAction.Wake,"k ow d eh k s aa ch ow"),
  new Phrase("কোডেক্স কি তুমি আছো",VoiceAction.Wake,"k ow d eh k s k ih t uw m ih aa ch ow"),new Phrase("আচ্ছা কোডেক্স শোনো",VoiceAction.Wake,"aa ch aa k ow d eh k s sh ow n ow"),
  new Phrase("হ্যালো কোডেক্স",VoiceAction.Wake,"h eh l ow k ow d eh k s"),new Phrase("ওকে কোডেক্স",VoiceAction.Wake,"ow k ey k ow d eh k s"),
  new Phrase("start",VoiceAction.Start,null),new Phrase("okay start",VoiceAction.Start,null),
  new Phrase("stop",VoiceAction.Stop,null),new Phrase("okay stop",VoiceAction.Stop,null),new Phrase("থামো",VoiceAction.Stop,"th aa m ow"),
  new Phrase("send",VoiceAction.Send,null),new Phrase("run",VoiceAction.Send,null),new Phrase("okay send",VoiceAction.Send,null),new Phrase("now send",VoiceAction.Send,null),
  new Phrase("সেন্ড",VoiceAction.Send,"s eh n d"),new Phrase("রান",VoiceAction.Send,"r aa n")
 };
 public static Grammar MakeGrammar(bool active) {
  var choices=new SrgsOneOf();foreach(var p in Phrases){if(!active&&p.Action!=VoiceAction.Wake)continue;var item=new SrgsItem();if(p.Phones==null)item.Add(new SrgsText(p.Text));else{var token=new SrgsToken(p.Text);token.Pronunciation=p.Phones;item.Add(token);}item.Add(new SrgsNameValueTag("action",p.Action.ToString()));choices.Add(item);}
  var rule=new SrgsRule("commands",choices);rule.Scope=SrgsRuleScope.Public;var doc=new SrgsDocument(rule);doc.Culture=CultureInfo.GetCultureInfo("en-US");doc.PhoneticAlphabet=SrgsPhoneticAlphabet.Sapi;return new Grammar(doc){Name=active?"control":"wake"};
 }
 static readonly Regex Normalizer=new Regex(@"[\s\p{P}]+",RegexOptions.Compiled|RegexOptions.CultureInvariant);
 static readonly Dictionary<VoiceAction,HashSet<string>> Accepted=Phrases.GroupBy(p=>p.Action).ToDictionary(g=>g.Key,g=>new HashSet<string>(g.Select(p=>Normalize(p.Text)),StringComparer.Ordinal));
 public static string Normalize(string s){return Normalizer.Replace((s??"").Trim().ToLowerInvariant()," ").Trim();}
 public static bool IsExact(VoiceAction action,string text){HashSet<string> aliases;return Accepted.TryGetValue(action,out aliases)&&aliases.Contains(Normalize(text));}
 public static string TrimTrailingCommand(string text,VoiceAction action,out bool removed){removed=false;string original=text??"";var aliases=Phrases.Where(p=>p.Action==action).Select(p=>p.Text).OrderByDescending(p=>p.Length).ToList();if(action==VoiceAction.Send){aliases.Add("সেন্ড করো");aliases.Add("রান করো");}foreach(string alias in aliases){var m=Regex.Match(original,@"(?:^|[\s,;।.!?])"+Regex.Escape(alias)+@"[\s।.!?,]*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);if(!m.Success)continue;removed=true;return original.Substring(0,m.Index).TrimEnd();}return original;}
}
sealed class CommandListener:IDisposable {
 SpeechRecognitionEngine engine;Grammar wake,controls;readonly object sync=new object();bool active;DateTime last=DateTime.MinValue;SilenceGate silence=new SilenceGate();double utteranceStart;
 public event Action<VoiceAction,string,float> Command;public event Action<int> Level;public event Action<string> Status;
 public event Action Completed;
 public bool Running{get{return engine!=null;}}
 public void Start(bool sessionActive,System.IO.Stream input=null){
  Stop();active=sessionActive;silence=new SilenceGate();silence.Observe(0,false);
  var recognizer=SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r=>r.Culture.Name=="en-US");
  if(recognizer==null)throw new InvalidOperationException("Windows English speech recognizer unavailable. Click the orb to use Codex dictation.");
  var e=new SpeechRecognitionEngine(recognizer);engine=e;
  try{
   wake=Commands.MakeGrammar(false);controls=Commands.MakeGrammar(true);wake.Enabled=!active;controls.Enabled=active;e.LoadGrammar(wake);e.LoadGrammar(controls);
   e.BabbleTimeout=TimeSpan.Zero;e.InitialSilenceTimeout=TimeSpan.Zero;e.EndSilenceTimeout=TimeSpan.FromMilliseconds(150);e.EndSilenceTimeoutAmbiguous=TimeSpan.FromMilliseconds(220);
   e.SpeechRecognized+=Recognized;e.RecognizerUpdateReached+=Update;
   e.SpeechDetected+=delegate(object sender,SpeechDetectedEventArgs a){utteranceStart=a.AudioPosition.TotalMilliseconds;};
   e.AudioLevelUpdated+=delegate(object sender,AudioLevelUpdatedEventArgs a){
    if(!Object.ReferenceEquals(sender,engine))return;
    if(input==null)try{silence.Observe(e.AudioPosition.TotalMilliseconds,a.AudioLevel>=Program.Config.SilenceLevel);}catch{}
    var h=Level;if(h!=null)h(a.AudioLevel);
   };
   e.RecognizeCompleted+=delegate(object sender,RecognizeCompletedEventArgs a){if(a.Error!=null){Program.Log(a.Error);var h=Status;if(h!=null)h("Command microphone stopped. Pause and resume to reconnect.");}var done=Completed;if(done!=null)done();};
   if(input==null)e.SetInputToDefaultAudioDevice();else e.SetInputToAudioStream(new LevelTrackedPcmStream(input,silence),new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,System.Speech.AudioFormat.AudioChannel.Mono));
   e.RecognizeAsync(RecognizeMode.Multiple);
  }catch{Stop();throw;}
 }
 async void Recognized(object sender,SpeechRecognizedEventArgs e){
  if(!Object.ReferenceEquals(sender,engine))return;VoiceAction action;if(!e.Result.Semantics.ContainsKey("action")||!Enum.TryParse(e.Result.Semantics["action"].Value.ToString(),out action))return;
  float threshold=action==VoiceAction.Wake?Program.Config.WakeConfidence:Program.Config.ControlConfidence;
  double quiet=0;bool pauseNeeded=action==VoiceAction.Stop||action==VoiceAction.Send;
  if(pauseNeeded&&e.Result.Audio!=null)quiet=silence.QuietBefore(e.Result.Audio.AudioPosition.TotalMilliseconds);
  double leading=e.Result.Audio==null?0:e.Result.Audio.AudioPosition.TotalMilliseconds-utteranceStart;
  double end=e.Result.Audio==null?0:(e.Result.Audio.AudioPosition+e.Result.Audio.Duration).TotalMilliseconds;
  if(pauseNeeded){await System.Threading.Tasks.Task.Delay(450);if(!Object.ReferenceEquals(sender,engine))return;}
  bool followingSpeech=pauseNeeded&&silence.HasSound(end+80,end+500);
  bool accepted=e.Result.Confidence>=threshold&&(!pauseNeeded||(quiet>=Program.Config.CommandPauseSeconds*1000&&leading<300&&!followingSpeech));
  try{string path=System.IO.Path.Combine(Program.Data,"command-events.log");if(System.IO.File.Exists(path)&&new System.IO.FileInfo(path).Length>32768)System.IO.File.WriteAllText(path,"");System.IO.File.AppendAllText(path,DateTime.Now.ToString("s")+" action="+action+" confidence="+e.Result.Confidence.ToString("0.00",CultureInfo.InvariantCulture)+" priorQuietSeconds="+(quiet/1000).ToString("0.00",CultureInfo.InvariantCulture)+" followingSpeech="+followingSpeech+" accepted="+accepted+"\n");}catch{}
  if(!accepted||!Commands.IsExact(action,e.Result.Text))return;lock(sync){if((DateTime.UtcNow-last).TotalMilliseconds<700)return;last=DateTime.UtcNow;}var h=Command;if(h!=null)h(action,e.Result.Text,e.Result.Confidence);
 }
 public void SetActive(bool value){active=value;var e=engine;if(e!=null)e.RequestRecognizerUpdate();}
 void Update(object sender,RecognizerUpdateReachedEventArgs e){if(!Object.ReferenceEquals(sender,engine))return;wake.Enabled=!active;controls.Enabled=active;}
 public void Stop(){var e=engine;engine=null;if(e==null)return;try{e.RecognizeAsyncCancel();e.SetInputToNull();e.Dispose();}catch(Exception ex){Program.Log(ex);}}
 public void Dispose(){Stop();}
}
// File-fixture input does not expose timestamped AudioLevelUpdated samples.
// Measure 16 kHz / 16-bit PCM windows directly for deterministic tests; the
// live microphone continues to use Windows' own audio-level notifications.
sealed class LevelTrackedPcmStream:System.IO.Stream {
 readonly System.IO.Stream input;readonly SilenceGate gate;int frames,peak;long position;
 public LevelTrackedPcmStream(System.IO.Stream stream,SilenceGate silence){input=stream;gate=silence;}
 public override int Read(byte[] buffer,int offset,int count){int n=input.Read(buffer,offset,count);for(int i=offset;i+1<offset+n;i+=2){int sample=(short)(buffer[i]|(buffer[i+1]<<8));peak=Math.Max(peak,Math.Abs(sample));position+=2;if(++frames==320){gate.Observe(position/32.0,peak>=32768*Program.Config.SilenceLevel/100);frames=peak=0;}}return n;}
 public override bool CanRead{get{return true;}}public override bool CanSeek{get{return input.CanSeek;}}public override bool CanWrite{get{return false;}}public override long Length{get{return input.Length;}}public override long Position{get{return input.Position;}set{Seek(value,System.IO.SeekOrigin.Begin);}}
 public override void Flush(){}public override long Seek(long offset,System.IO.SeekOrigin origin){long result=input.Seek(offset,origin);if(result!=position){position=result;frames=peak=0;}return result;}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
}
}
