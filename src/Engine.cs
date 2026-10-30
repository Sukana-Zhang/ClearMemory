using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Globalization;

namespace ClearMemory {
    public sealed class Settings {
        public bool Enabled=true, TimerEnabled=true, ThresholdEnabled=true;
        public int IntervalMinutes=30;
        public double ThresholdGB=14;
        public static Settings Defaults(double total){return new Settings{ThresholdGB=Math.Max(0.1,Math.Floor(total*0.875*10)/10)};}
        public void Validate(double total){
            if(IntervalMinutes<1||IntervalMinutes>1440)throw new ArgumentException("清理间隔应为 1–1440 分钟。");
            if(double.IsNaN(ThresholdGB)||double.IsInfinity(ThresholdGB)||ThresholdGB<0.1||ThresholdGB>=total)throw new ArgumentException("内存阈值应大于等于 0.1 GB，并小于物理内存总量。");
        }
        public void Save(double total){Validate(total);Storage.WriteSetting("settings.json",new JavaScriptSerializer().Serialize(this));}
        static bool ReadBool(Dictionary<string,object> data,string key,bool fallback){object value;if(!data.TryGetValue(key,out value))return fallback;if(!(value is bool))throw new InvalidDataException("设置项 "+key+" 必须是布尔值。");return (bool)value;}
        static double ReadNumber(Dictionary<string,object> data,string key,double fallback){object value;if(!data.TryGetValue(key,out value))return fallback;if(!(value is int||value is long||value is decimal||value is double))throw new InvalidDataException("设置项 "+key+" 必须是数字。");return Convert.ToDouble(value,CultureInfo.InvariantCulture);}
        public static Settings Load(double total){
            try{
                var data=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Storage.DirectoryPath,"settings.json"))) as Dictionary<string,object>;
                if(data==null)throw new InvalidDataException("设置文件不是 JSON 对象。");
                var s=Defaults(total);s.Enabled=ReadBool(data,"Enabled",s.Enabled);s.TimerEnabled=ReadBool(data,"TimerEnabled",s.TimerEnabled);s.ThresholdEnabled=ReadBool(data,"ThresholdEnabled",s.ThresholdEnabled);
                double interval=ReadNumber(data,"IntervalMinutes",s.IntervalMinutes);if(interval!=Math.Truncate(interval))throw new InvalidDataException("清理间隔必须是整数。");
                s.IntervalMinutes=checked((int)interval);s.ThresholdGB=ReadNumber(data,"ThresholdGB",s.ThresholdGB);s.Validate(total);return s;
            }catch(FileNotFoundException){return Defaults(total);}catch(DirectoryNotFoundException){return Defaults(total);}
            catch(Exception ex){if(!(ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException||ex is FormatException||ex is OverflowException))throw;Storage.Log("设置不可用，使用默认值并暂停自动清理："+ex.Message);var fallback=Defaults(total);fallback.Enabled=false;return fallback;}
        }
    }
    public sealed class Schedule {
        long nextDue;long? highSince;
        readonly object gate=new object();
        public void Reset(long now,Settings s){lock(gate){nextDue=now+s.IntervalMinutes*60000L;highSince=null;}}
        public long Remaining(long now){lock(gate)return Math.Max(0,(nextDue-now+999)/1000);}
        public string Evaluate(long now,Settings s,MemorySnapshot memory,bool busy){
            lock(gate){
            if(!s.Enabled){highSince=null;return null;}
            if(!memory.Valid||!s.ThresholdEnabled||memory.UsedGB<s.ThresholdGB)highSince=null;
            else if(!highSince.HasValue)highSince=now;
            if(busy)return null;
            bool timed=s.TimerEnabled&&now>=nextDue;
            bool high=s.ThresholdEnabled&&highSince.HasValue&&now-highSince.Value>=10000;
            if(!timed&&!high)return null;
            highSince=null;
            return timed?(high?"定时及阈值触发":"定时清理"):"阈值触发";
            }
        }
        public void Complete(long now,Settings s,bool timed){lock(gate){if(timed)nextDue=now+s.IntervalMinutes*60000L;highSince=null;}}
    }
    public sealed class CleanResult {
        public MemorySnapshot Before,After;
        public List<SystemMemoryStep> Steps;
        static string MemoryText(MemorySnapshot value){return value!=null&&value.Valid?value.UsedGB.ToString("0.00")+" GB":"不可用";}
        public string Summary {get{return "系统请求成功 "+Steps.Count(s=>s.Success)+"/"+Steps.Count+" 步 · 已用内存 "+MemoryText(Before)+" → "+MemoryText(After);}}
        public string Detail {get{string change=Before!=null&&Before.Valid&&After!=null&&After.Valid?"全机可用内存变化 "+((After.AvailableGB-Before.AvailableGB)*1024).ToString("+0.0;-0.0;0.0")+" MB（包含其他程序活动）":"内存采样不完整，无法计算变化。";return Summary+Environment.NewLine+change+Environment.NewLine+string.Join(Environment.NewLine,Steps.Select(s=>s.Name+"："+s.Message+" · "+s.Status+" · "+s.DurationMs+" ms"));}}
    }
    public sealed class Engine {
        public Settings Settings;
        public MemorySnapshot Memory;
        public readonly Schedule Schedule=new Schedule();
        int running;
        public bool Busy {get{return Thread.VolatileRead(ref running)!=0;}}
        readonly Func<MemorySnapshot> readMemory;
        readonly Func<CancellationToken,List<SystemMemoryStep>> cleanMemory;
        public string Message="尚未执行清理",Details="";
        public Engine():this(MemorySnapshot.Read,SystemMemory.Run){}
        public Engine(Func<MemorySnapshot> memoryReader,Func<CancellationToken,List<SystemMemoryStep>> cleaner){if(memoryReader==null||cleaner==null)throw new ArgumentNullException();readMemory=memoryReader;cleanMemory=cleaner;Memory=readMemory();if(!Memory.Valid)throw new InvalidOperationException("无法读取 Windows 内存状态。");Settings=Settings.Load(Memory.TotalGB);Schedule.Reset(Clock.Now,Settings);}
        public void Tick(){Memory=readMemory();string reason=Schedule.Evaluate(Clock.Now,Settings,Memory,Busy);if(reason!=null)Start(reason);}
        public void Apply(Settings settings){if(Busy)throw new InvalidOperationException("请等待当前清理结束后保存设置。");settings.Save(Memory.TotalGB);Settings=settings;Schedule.Reset(Clock.Now,Settings);}
        public void SetEnabled(bool enabled){var current=Settings;Apply(new Settings{Enabled=enabled,TimerEnabled=current.TimerEnabled,ThresholdEnabled=current.ThresholdEnabled,IntervalMinutes=current.IntervalMinutes,ThresholdGB=current.ThresholdGB});}
        public Task Start(string reason){
            if(Interlocked.CompareExchange(ref running,1,0)!=0)return Task.FromResult(0);
            Message="正在执行"+reason+"…";
            bool timed=reason.StartsWith("定时",StringComparison.Ordinal);
            return Task.Run(()=>{
                try{
                    var report=new CleanResult{Before=readMemory(),Steps=cleanMemory(CancellationToken.None),After=readMemory()};
                    Details=report.Detail;Message=report.Summary;Storage.Log(reason+" · "+report.Detail.Replace(Environment.NewLine," | "));
                }catch(Exception ex){Message="清理失败："+ex.Message;Details=ex.ToString();Storage.Log(Message);}
                finally{Schedule.Complete(Clock.Now,Settings,timed);Interlocked.Exchange(ref running,0);}
            });
        }
    }
}
