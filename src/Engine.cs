using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClearMemory {
    public sealed class Settings {
        public bool Enabled=true, TimerEnabled=true, ThresholdEnabled=true;
        public int IntervalMinutes=30;
        public double ThresholdGB=14;
        public static Settings Defaults(double total){return new Settings{ThresholdGB=Math.Max(0.1, Math.Min(14, Math.Floor(total*0.875*10)/10))};}
        public void Validate(double total){
            if(IntervalMinutes<1||IntervalMinutes>1440)throw new ArgumentException("清理间隔应为 1–1440 分钟。");
            if(double.IsNaN(ThresholdGB)||double.IsInfinity(ThresholdGB)||ThresholdGB<0.1||ThresholdGB>=total)throw new ArgumentException("内存阈值应大于等于 0.1 GB，并小于物理内存总量。");
        }
        public void Save(double total){Validate(total);Storage.WriteSetting("settings.json",new JavaScriptSerializer().Serialize(this));}
        public static Settings Load(double total){try{var s=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Storage.DirectoryPath,"settings.json")));if(s==null)throw new InvalidDataException();s.Validate(total);return s;}catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException))throw;Storage.Log("使用默认设置："+ex.Message);return Defaults(total);}}
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
        public string Summary {get{return "系统请求成功 "+Steps.Count(s=>s.Success)+"/"+Steps.Count+" 步 · 已用内存 "+Before.UsedGB.ToString("0.00")+" → "+After.UsedGB.ToString("0.00")+" GB";}}
        public string Detail {get{return Summary+Environment.NewLine+"全机可用内存变化 "+((After.AvailableGB-Before.AvailableGB)*1024).ToString("+0.0;-0.0;0.0")+" MB（包含其他程序活动）"+Environment.NewLine+string.Join(Environment.NewLine,Steps.Select(s=>s.Name+"："+s.Message+" · "+s.Status+" · "+s.DurationMs+" ms"));}}
    }
    public sealed class Engine {
        public Settings Settings;
        public MemorySnapshot Memory;
        public readonly Schedule Schedule=new Schedule();
        public volatile bool Busy;
        public string Message="尚未执行清理",Details="";
        public Engine(){Memory=MemorySnapshot.Read();if(!Memory.Valid)throw new InvalidOperationException("无法读取 Windows 内存状态。");Settings=Settings.Load(Memory.TotalGB);Schedule.Reset(Clock.Now,Settings);}
        public void Tick(){Memory=MemorySnapshot.Read();string reason=Schedule.Evaluate(Clock.Now,Settings,Memory,Busy);if(reason!=null)Start(reason);}
        public void Apply(Settings settings){if(Busy)throw new InvalidOperationException("请等待当前清理结束后保存设置。");settings.Save(Memory.TotalGB);Settings=settings;Schedule.Reset(Clock.Now,Settings);}
        public Task Start(string reason){
            if(Busy)return Task.FromResult(0);
            Busy=true;Message="正在执行"+reason+"…";
            bool timed=reason.StartsWith("定时",StringComparison.Ordinal);
            return Task.Run(()=>{
                try{
                    var report=new CleanResult{Before=MemorySnapshot.Read(),Steps=SystemMemory.Run(CancellationToken.None),After=MemorySnapshot.Read()};
                    Details=report.Detail;Message=report.Summary;Storage.Log(reason+" · "+report.Detail.Replace(Environment.NewLine," | "));
                }catch(Exception ex){Message="清理失败："+ex.Message;Details=ex.ToString();Storage.Log(Message);}
                finally{Schedule.Complete(Clock.Now,Settings,timed);Busy=false;}
            });
        }
    }
}
