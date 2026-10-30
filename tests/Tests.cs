using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Xml.Linq;
using System.Windows.Forms;
using ClearMemory;
class Tests {
    static int checks;
    static void Check(bool ok,string what){if(!ok)throw new Exception(what);checks++;Console.WriteLine("PASS "+what);}
    static MemorySnapshot Snapshot(double used){return new MemorySnapshot{Total=16UL*1073741824,Available=(ulong)((16-used)*1073741824)};}
    static void Reject(Settings cfg){bool failed=false;try{cfg.Validate(16);}catch(ArgumentException){failed=true;}Check(failed,"invalid configuration rejected");}
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string work=Path.Combine(Path.GetFullPath("build"),"test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);Storage.DirectoryPath=work;
        try{
            var cfg=Settings.Defaults(15.78);Check(cfg.IntervalMinutes==30&&cfg.ThresholdGB<15.78,"fresh share defaults: thirty minutes and adaptive threshold");
            Check(!Startup.Wanted,"autostart is opt-in");Check(Startup.TaskName.StartsWith("ClearMemory-S-"),"independent user-specific startup identity");
            foreach(double total in new[]{4.0,8,15.78,32,64}){var s=Settings.Defaults(total);s.Validate(total);Check(s.ThresholdGB<total,"threshold fits destination machine");}
            Reject(new Settings{IntervalMinutes=0});Reject(new Settings{IntervalMinutes=1441});Reject(new Settings{ThresholdGB=16});Reject(new Settings{ThresholdGB=double.NaN});Reject(new Settings{ThresholdGB=double.PositiveInfinity});
            cfg.Enabled=false;cfg.TimerEnabled=false;cfg.IntervalMinutes=71;cfg.ThresholdGB=12.5;cfg.Save(16);var loaded=Settings.Load(16);Check(!loaded.Enabled&&!loaded.TimerEnabled&&loaded.IntervalMinutes==71&&loaded.ThresholdGB==12.5,"configuration roundtrip and pause persist");
            File.WriteAllText(Path.Combine(work,"settings.json"),"{invalid");Check(Settings.Load(16).IntervalMinutes==30,"corrupt configuration safely defaults");
            cfg=new Settings{IntervalMinutes=30,ThresholdGB=14};var schedule=new Schedule();schedule.Reset(0,cfg);
            Check(schedule.Remaining(0)==1800,"timer starts at thirty minutes");Check(schedule.Evaluate(1799999,cfg,Snapshot(8),false)==null,"low memory usage does not advance timer");
            Check(schedule.Evaluate(1800000,cfg,Snapshot(8),false)=="定时清理","independent timer runs at low usage");schedule.Complete(1801000,cfg,true);Check(schedule.Remaining(1801000)==1800,"timed completion resets next timer");
            schedule.Reset(0,cfg);Check(schedule.Evaluate(1,cfg,Snapshot(14.5),false)==null,"threshold requires sustained pressure");Check(schedule.Evaluate(10000,cfg,Snapshot(14.5),false)==null,"threshold does not fire early");Check(schedule.Evaluate(10001,cfg,Snapshot(14.5),false)=="阈值触发","threshold fires after ten seconds");
            schedule.Complete(11000,cfg,false);Check(schedule.Remaining(11000)==1789,"manual or threshold cleanup does not postpone timer");
            Check(schedule.Evaluate(12000,cfg,Snapshot(15),false)==null,"fresh threshold observation after cleanup");Check(schedule.Evaluate(22000,cfg,Snapshot(15),false)!=null,"sustained pressure can trigger another round without cooldown");
            schedule.Reset(0,cfg);schedule.Evaluate(0,cfg,Snapshot(15),false);schedule.Evaluate(9000,cfg,Snapshot(8),false);Check(schedule.Evaluate(10001,cfg,Snapshot(15),false)==null,"pressure dropping resets observation");
            cfg.Enabled=false;Check(schedule.Evaluate(99999999,cfg,Snapshot(15),false)==null,"pause suppresses both triggers");cfg.Enabled=true;Check(schedule.Evaluate(99999999,cfg,Snapshot(15),true)==null,"busy cleanup cannot overlap");
            cfg.TimerEnabled=false;cfg.ThresholdEnabled=false;Check(schedule.Evaluate(99999999,cfg,Snapshot(15),false)==null,"both triggers can be disabled");
            cfg.TimerEnabled=true;cfg.ThresholdEnabled=true;schedule.Reset(0,cfg);schedule.Evaluate(1790000,cfg,Snapshot(15),false);Check(schedule.Evaluate(1800000,cfg,Snapshot(15),false)=="定时及阈值触发","simultaneous triggers merge into one round");
            string exe="D:\\共享 文件 & 测试\\ClearMemory.exe",sid="S-1-5-21-123";XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
            var xml=XDocument.Parse(Startup.TaskXml(exe,sid));Check(xml.Descendants(ns+"Command").Single().Value==exe,"startup XML preserves Unicode and escaped paths");Check(xml.Descendants(ns+"RunLevel").Single().Value=="HighestAvailable","startup requests required elevation");
            Check(Startup.Plan(new StartupStatus(),false,exe)=="keep","disabled startup does not create task");bool denied=false;try{Startup.Plan(new StartupStatus{Exists=true,Author="Other"},true,exe);}catch(InvalidOperationException){denied=true;}Check(denied,"foreign startup task is never overwritten");
            var engine=new Engine();using(var window=new MainWindow(engine,true)){
                window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();window.PerformLayout();
                using(var bitmap=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));bitmap.Save("build\\preview.png");}
                Check(window.Text.Contains("ClearMemory")&&window.Icon!=null,"Chinese UI and embedded application icon initialize");
            }
            Check(!File.Exists(Path.Combine(work,"startup.txt")),"UI preview never changes autostart");
            Check(MemorySnapshot.Read().Valid,"native memory monitoring works");
            Check(typeof(Engine).Assembly.GetReferencedAssemblies().All(a=>a.Name=="mscorlib"||a.Name.StartsWith("System")||a.Name=="Microsoft.CSharp"),"only built-in framework dependencies");
            Console.WriteLine(checks+" standalone checks passed; no actual cleanup or startup registration performed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Directory.Delete(work,true);}
    }
}
