using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using ClearMemory;
class CompatibilityTests {
    static int checks;
    static void Check(bool ok,string what){if(!ok)throw new Exception(what);checks++;Console.WriteLine("PASS "+what);}
    static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(Control child in All(c))yield return child;}}
    [STAThread] static int Main(){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string work=Path.Combine(Path.GetFullPath("build"),"compat-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);Storage.DirectoryPath=work;
        var oldCulture=Thread.CurrentThread.CurrentCulture;
        try{
            foreach(string json in new[]{"null","[]","{invalid","{\"IntervalMinutes\":\"abc\"}","{\"IntervalMinutes\":999999999999999999999}","{\"IntervalMinutes\":1.5}","{\"ThresholdGB\":{}}","{\"Enabled\":\"not-a-bool\"}","{\"TimerEnabled\":null}","{\"ThresholdGB\":null}"}){
                File.WriteAllText(Path.Combine(work,"settings.json"),json);var cfg=Settings.Load(16);cfg.Validate(16);Check(!cfg.Enabled&&cfg.IntervalMinutes==30,"invalid settings start safely with automatic cleanup paused");
            }
            File.Delete(Path.Combine(work,"settings.json"));Check(Settings.Load(16).Enabled,"first launch with no settings uses normal defaults");
            foreach(double total in new[]{4,8,16,32,64,128}){var cfg=Settings.Defaults(total);Check(cfg.ThresholdGB==Math.Floor(total*.875*10)/10,"default threshold adapts to RAM capacity: "+total+" GB");}
            foreach(string culture in new[]{"en-US","zh-CN","de-DE"}){
                Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo(culture);var cfg=new Settings{Enabled=false,IntervalMinutes=1440,ThresholdGB=12.5};cfg.Save(16);var loaded=Settings.Load(16);Check(!loaded.Enabled&&loaded.IntervalMinutes==1440&&loaded.ThresholdGB==12.5,"settings preserve decimal values and pause state: "+culture);
            }
            Thread.CurrentThread.CurrentCulture=oldCulture;
            Check(MainWindow.Countdown(86400)=="24:00:00","one-day interval does not wrap to zero");Check(MainWindow.Countdown(3661)=="01:01:01","normal countdown formatting");Check(MainWindow.Countdown(-1)=="00:00:00","expired countdown never goes negative");
            var valid=new MemorySnapshot{Total=16UL*1073741824,Available=8UL*1073741824};
            foreach(bool missingBefore in new[]{true,false}){
                var result=new CleanResult{Before=missingBefore?new MemorySnapshot():valid,After=missingBefore?valid:new MemorySnapshot(),Steps=new List<SystemMemoryStep>()};
                Check(result.Summary.Contains("不可用")&&result.Detail.Contains("无法计算变化")&&!result.Detail.Contains("8192"),"failed sampling cannot report invented memory savings");
            }
            File.Delete(Path.Combine(work,"settings.json"));var engine=new Engine();bool before=engine.Settings.Enabled;
            string blocker=Path.Combine(work,"not-a-directory");File.WriteAllText(blocker,"fixture");Storage.DirectoryPath=blocker;bool rejected=false;
            try{engine.SetEnabled(!before);}catch(IOException){rejected=true;}finally{Storage.DirectoryPath=work;}
            Check(rejected&&engine.Settings.Enabled==before,"failed setting write does not change the active pause state");
            foreach(double total in new[]{4.0,16.0,32.0,64.0}){
                engine.Memory=new MemorySnapshot{Total=(ulong)(total*1073741824),Available=(ulong)(total*.5*1073741824)};engine.Settings=Settings.Defaults(total);
                using(var window=new MainWindow(engine,true)){
                    var limit=All(window).OfType<NumericUpDown>().Single(c=>c.DecimalPlaces==1);Check(limit.Maximum<(decimal)total&&limit.Value<=limit.Maximum,"threshold editor fits exact physical RAM totals: "+total+" GB");
                }
            }
            foreach(float scale in new[]{1f,1.5f,2f}){
                using(var window=new MainWindow(engine,true)){
                    window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();
                    if(scale!=1)window.Scale(new SizeF(scale,scale));
                    var area=new Rectangle(-30000,-30000,960,680);window.FitToArea(area);window.PerformLayout();Application.DoEvents();
                    Check(area.Contains(window.Bounds),"window fits a small work area at simulated scale "+scale);
                    var viewport=window.Controls.OfType<Panel>().Single();Check(viewport.AutoScroll,"small display has a scrollable viewport");
                    foreach(Control control in All(window).Where(c=>c is Button||c is NumericUpDown||c is CheckBox)){
                        viewport.ScrollControlIntoView(control);Application.DoEvents();var p=viewport.PointToClient(control.Parent.PointToScreen(control.Location));Check(viewport.ClientRectangle.Contains(new Rectangle(p,control.Size)),"control remains reachable at simulated scale "+scale+": "+control.GetType().Name+" "+p+" "+control.Size+" viewport "+viewport.ClientSize);
                    }
                }
            }
            int calls=0;using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var concurrent=new Engine(()=>valid,t=>{Interlocked.Increment(ref calls);entered.Set();if(!release.WaitOne(5000))throw new TimeoutException("fixture release");return new List<SystemMemoryStep>{new SystemMemoryStep{Success=true,Name="fixture",Status="0x00000000",Message="ok"}};});
                Task task=concurrent.Start("手动清理");Check(entered.WaitOne(3000)&&concurrent.Busy,"cleanup exposes busy state while running");
                Parallel.For(0,32,i=>concurrent.Start("手动清理"));Check(calls==1,"concurrent requests cannot overlap native cleanup");
                bool refused=false;try{concurrent.SetEnabled(false);}catch(InvalidOperationException){refused=true;}Check(refused,"settings cannot change during cleanup");
                release.Set();Check(task.Wait(3000)&&!concurrent.Busy&&concurrent.Message.Contains("1/1"),"cleanup completes and releases the operation gate");
            }
            int tries=0;var recovered=new Engine(()=>valid,t=>{if(Interlocked.Increment(ref tries)==1)throw new InvalidOperationException("fixture failure");return new List<SystemMemoryStep>{new SystemMemoryStep{Success=true}};});
            Check(recovered.Start("手动清理").Wait(3000)&&!recovered.Busy&&recovered.Message.Contains("清理失败"),"cleanup failure is reported and busy state resets");
            Check(recovered.Start("手动清理").Wait(3000)&&!recovered.Busy&&recovered.Message.Contains("1/1"),"a new cleanup can run after failure");
            File.WriteAllText("build/compatibility-results.json",new JavaScriptSerializer().Serialize(new{checks=checks,actualCleanup=false,startupRegistered=false,simulatedScales=new[]{100,150,200},cultures=new[]{"en-US","zh-CN","de-DE"}}));
            Console.WriteLine(checks+" compatibility checks passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Thread.CurrentThread.CurrentCulture=oldCulture;Directory.Delete(work,true);}
    }
}
