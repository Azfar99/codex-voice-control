using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CodexVoice {
// The frame, waves, particles and core share one smoothly changing palette.
// Silence keeps the old gentle motion; microphone energy adds outward motion.
sealed class OrbVisual {
 double time,voice;
 readonly PointF[][] wavePoints={new PointF[97],new PointF[97],new PointF[97]};
 static readonly double[] Cos=MakeCircle(true),Sin=MakeCircle(false);
 static double[] MakeCircle(bool cosine){var values=new double[97];for(int i=0;i<values.Length;i++){double a=i*Math.PI*2/(values.Length-1);values[i]=cosine?Math.Cos(a):Math.Sin(a);}return values;}
 float red=58,green=224,blue=151;
 public double Voice {get{return voice;}}
 public Color Accent {get{return Color.FromArgb((int)red,(int)green,(int)blue);}}
 public void Tick(SessionState state,double rawLevel,double seconds){
  bool recording=state==SessionState.Recording;
  double target=recording?Math.Pow(Math.Max(0,Math.Min(1,(rawLevel-.02)*4.8)),.65):0;
  voice+=(target-voice)*(target>voice?.65:.18);
  time+=seconds*(.58+voice*1.8);
  Color tint=Tint(state,time,voice);float blend=(float)Math.Min(1,seconds*12);
  red+=(tint.R-red)*blend;green+=(tint.G-green)*blend;blue+=(tint.B-blue)*blend;
 }
 static Color Mix(Color a,Color b,double x){return Color.FromArgb((int)(a.R+(b.R-a.R)*x),(int)(a.G+(b.G-a.G)*x),(int)(a.B+(b.B-a.B)*x));}
 public static Color Tint(SessionState state,double t,double energy){
  Color emerald=Color.FromArgb(58,224,151),cyan=Color.FromArgb(88,207,238),gold=Color.FromArgb(231,188,105);
  if(state==SessionState.Paused)return Color.FromArgb(112,143,128);
  if(state==SessionState.Error)return Color.FromArgb(236,171,95);
  if(state==SessionState.Transcribing)return gold;
  if(state==SessionState.Starting||state==SessionState.Sending)return cyan;
  if(state==SessionState.Recording&&energy>.035)return Color.FromArgb(248,211,82);
  return emerald;
 }
 public void Draw(Graphics g,float cx,float cy,int size){
  g.SmoothingMode=SmoothingMode.AntiAlias;
  Color accent=Accent;float frame=size*.445f,r=size*.342f;
  using(var path=new GraphicsPath()){
   path.AddEllipse(cx-frame,cy-frame,frame*2,frame*2);
   using(var glass=new PathGradientBrush(path)){glass.CenterColor=Color.FromArgb(Math.Max(14,accent.R/8),Math.Max(19,accent.G/7),Math.Max(9,accent.B/7));glass.SurroundColors=new[]{Color.FromArgb(9,17,13)};g.FillPath(glass,path);}
  }
  // One hairline frame replaces the old stack of thick glow strokes.
  using(var framePen=new Pen(Color.FromArgb(180,accent),1.2f))g.DrawEllipse(framePen,cx-frame,cy-frame,frame*2,frame*2);
  for(int n=0;n<3;n++){
   var points=wavePoints[n];
   for(int j=0;j<points.Length;j++){
    double a=j*Math.PI*2/(points.Length-1);
    double wave=Math.Sin(a*(3+n)+time*(n%2==0?1:-1)+n*1.8);
    double vibration=voice*Math.Sin(a*9+time*16+n)*2.3;
    double radius=r+wave*(2.4+voice*10.2)+vibration;
    points[j]=new PointF(cx+(float)(Cos[j]*radius),cy+(float)(Sin[j]*radius*.95));
   }
   using(var pen=new Pen(Color.FromArgb(94+n*49,accent),n==2?1.05f:.75f))g.DrawLines(pen,points);
  }
  for(int i=0;i<9;i++){
   double a=i*2.39996+time*(.38+i*.018);
   double radius=r*(.23+(i%4)*.14)+voice*(4+(i%3)*2.2);
   double jitter=voice*Math.Sin(time*29+i*2.7)*2.9;
   float x=cx+(float)(Math.Cos(a)*radius+jitter),y=cy+(float)(Math.Sin(a)*radius-jitter*.6);
   float dot=(float)(.9+voice*(i%3)*.4);
   using(var b=new SolidBrush(Color.FromArgb((int)(40+voice*120),accent)))g.FillEllipse(b,x-dot/2,y-dot/2,dot,dot);
  }
  float glowRadius=(float)(8+voice*9),nucleus=(float)(3.2+voice*3.7);
  using(var path=new GraphicsPath()){
   path.AddEllipse(cx-glowRadius,cy-glowRadius,glowRadius*2,glowRadius*2);
   using(var glow=new PathGradientBrush(path)){glow.CenterColor=Color.FromArgb((int)(55+voice*115),accent);glow.SurroundColors=new[]{Color.FromArgb(0,accent)};g.FillPath(glow,path);}
  }
  using(var b=new SolidBrush(Mix(accent,Color.FromArgb(255,247,194),voice*.8)))g.FillEllipse(b,cx-nucleus/2,cy-nucleus/2,nucleus,nucleus);
 }
 public static void SaveStates(string path){
  using(var image=new Bitmap(540,170))using(var g=Graphics.FromImage(image)){
   g.Clear(Color.FromArgb(9,15,13));
   using(var font=new Font("Segoe UI",9))using(var label=new SolidBrush(Color.FromArgb(166,197,181))){
    for(int i=0;i<3;i++){var visual=new OrbVisual();var state=i==0?SessionState.Idle:SessionState.Recording;for(int k=0;k<40;k++)visual.Tick(state,i==2?.22:0,.033);visual.Draw(g,90+i*180,75,100);g.DrawString(i==0?"Idle":i==1?"Listening, quiet":"Speaking",font,label,51+i*180,137);}
   }
   image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
  }
 }
}
}
