using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly:AssemblyTitle("ClearMemory · 内存清理")]
[assembly:AssemblyVersion("1.0.1.0")]
[assembly:AssemblyFileVersion("1.0.1.0")]
namespace ClearMemory {
    public sealed class MainWindow:Form {
        readonly Engine engine;
        readonly Label memory=new Label(),status=new Label(),next=new Label();
        readonly TextBox detail=new TextBox();
        readonly CheckBox timerEnabled=new CheckBox(),thresholdEnabled=new CheckBox(),startup=new CheckBox();
        readonly NumericUpDown minutes=new NumericUpDown(),threshold=new NumericUpDown();
        readonly Button clean=new Button(),pause=new Button(),save=new Button();
        bool syncing;public bool AllowExit;public readonly bool Preview;
        readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer{Interval=1000};
        public MainWindow(Engine engine,bool preview=false){
            this.engine=engine;Preview=preview;Text="ClearMemory · 内存清理";Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(245,247,251);
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(680,610);MinimumSize=new Size(480,360);StartPosition=FormStartPosition.CenterScreen;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearMemory.ico")){if(stream!=null)Icon=new Icon(stream);}
            var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};Controls.Add(viewport);
            var root=new TableLayoutPanel{Location=Point.Empty,Size=ClientSize,MinimumSize=new Size(680,610),Padding=new Padding(24),ColumnCount=1,RowCount=7};viewport.Controls.Add(root);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            viewport.Resize+=(s,e)=>{root.Size=new Size(Math.Max(viewport.ClientSize.Width,root.MinimumSize.Width),Math.Max(viewport.ClientSize.Height,root.MinimumSize.Height));};
            foreach(int height in new[]{64,86,140,45,42})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            var title=new Label{Text="ClearMemory",Font=new Font(Font.FontFamily,25,FontStyle.Bold),AutoSize=true,Dock=DockStyle.Fill};root.Controls.Add(title,0,0);
            var stats=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=Color.White,Padding=new Padding(14,5,14,5)};
            stats.RowStyles.Add(new RowStyle(SizeType.Absolute,44));stats.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            memory.Dock=DockStyle.Fill;memory.Font=new Font(Font.FontFamily,18,FontStyle.Bold);next.Dock=DockStyle.Fill;next.ForeColor=Color.FromArgb(90,107,130);stats.Controls.Add(memory);stats.Controls.Add(next);root.Controls.Add(stats,0,1);
            var settings=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,Padding=new Padding(0,12,0,0)};
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            foreach(int height in new[]{38,38,40})settings.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            timerEnabled.Text="按时间自动清理";timerEnabled.AutoSize=true;timerEnabled.Anchor=AnchorStyles.Left;
            thresholdEnabled.Text="达到已用内存阈值";thresholdEnabled.AutoSize=true;thresholdEnabled.Anchor=AnchorStyles.Left;
            minutes.Minimum=1;minutes.Maximum=1440;minutes.Dock=DockStyle.Fill;
            threshold.Minimum=0.1M;threshold.Maximum=Math.Max(0.1M,(decimal)(Math.Ceiling(engine.Memory.TotalGB*10)-1)/10);threshold.DecimalPlaces=1;threshold.Increment=0.5M;threshold.Dock=DockStyle.Fill;
            settings.Controls.Add(timerEnabled,0,0);settings.Controls.Add(minutes,1,0);settings.Controls.Add(new Label{Text="分钟",AutoSize=true,Anchor=AnchorStyles.Left},2,0);
            settings.Controls.Add(thresholdEnabled,0,1);settings.Controls.Add(threshold,1,1);settings.Controls.Add(new Label{Text="GB · 持续 10 秒触发",AutoSize=true,Anchor=AnchorStyles.Left},2,1);
            startup.Text="登录 Windows 后启动";startup.AutoSize=true;startup.Anchor=AnchorStyles.Left;settings.Controls.Add(startup,0,2);settings.SetColumnSpan(startup,2);
            root.Controls.Add(settings,0,2);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            foreach(var b in new[]{clean,pause,save}){b.Size=new Size(145,36);b.FlatStyle=FlatStyle.Flat;b.Margin=new Padding(0,0,12,0);buttons.Controls.Add(b);}
            clean.Text="立即清理";clean.BackColor=Color.FromArgb(38,139,123);clean.ForeColor=Color.White;pause.Text="暂停自动清理";save.Text="保存设置";root.Controls.Add(buttons,0,3);
            status.Dock=DockStyle.Fill;status.AutoEllipsis=true;status.Padding=new Padding(0,8,0,0);root.Controls.Add(status,0,4);
            detail.Dock=DockStyle.Fill;detail.Multiline=true;detail.ReadOnly=true;detail.ScrollBars=ScrollBars.Vertical;detail.BackColor=Color.White;detail.BorderStyle=BorderStyle.FixedSingle;root.Controls.Add(detail,0,5);
            root.Controls.Add(new Label{Text="关闭窗口后在托盘运行 · 全系统清理可能造成切换程序时短暂卡顿",AutoSize=false,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(90,107,130),TextAlign=ContentAlignment.MiddleLeft},0,6);
            LoadSettings();
            clean.Click+=async(s,e)=>{var task=engine.Start("手动清理");RefreshState();await task;RefreshState();};
            save.Click+=(s,e)=>{try{engine.Apply(new Settings{Enabled=engine.Settings.Enabled,TimerEnabled=timerEnabled.Checked,ThresholdEnabled=thresholdEnabled.Checked,IntervalMinutes=(int)minutes.Value,ThresholdGB=(double)threshold.Value});}catch(Exception ex){MessageBox.Show(this,ex.Message,"保存设置");}RefreshState();};
            pause.Click+=(s,e)=>{try{engine.SetEnabled(!engine.Settings.Enabled);}catch(Exception ex){MessageBox.Show(this,ex.Message,"自动清理");}RefreshState();};
            startup.CheckedChanged+=(s,e)=>{if(syncing||Preview)return;try{Startup.SetEnabled(startup.Checked,Application.ExecutablePath);}catch(Exception ex){syncing=true;startup.Checked=!startup.Checked;syncing=false;MessageBox.Show(this,ex.Message,"自启动设置");}};
            FormClosing+=(s,e)=>{if(!AllowExit&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
            poll.Tick+=(s,e)=>{engine.Tick();RefreshState();};if(!preview)poll.Start();RefreshState();
        }
        void LoadSettings(){syncing=true;var cfg=engine.Settings;timerEnabled.Checked=cfg.TimerEnabled;thresholdEnabled.Checked=cfg.ThresholdEnabled;minutes.Value=cfg.IntervalMinutes;threshold.Value=(decimal)cfg.ThresholdGB;
            if(!Preview)try{startup.Checked=Startup.Reconcile(Application.ExecutablePath).Enabled;}catch(Exception ex){engine.Message="自启动状态读取失败："+ex.Message;}
            syncing=false;}
        public static string Countdown(long seconds){seconds=Math.Max(0,seconds);return (seconds/3600).ToString("00")+":"+((seconds/60)%60).ToString("00")+":"+(seconds%60).ToString("00");}
        public void RefreshState(){var m=engine.Memory;memory.Text=m.Valid?"已用 "+m.UsedGB.ToString("0.00")+" / "+m.TotalGB.ToString("0.00")+" GB":"内存状态读取失败";
            var cfg=engine.Settings;long seconds=engine.Schedule.Remaining(Clock.Now);
            next.Text=!cfg.Enabled?"自动清理已暂停":engine.Busy?"清理中，完成后显示结果":(cfg.TimerEnabled?"下次定时清理 "+Countdown(seconds):"定时清理未启用")+(cfg.ThresholdEnabled?" · 阈值 "+cfg.ThresholdGB.ToString("0.0")+" GB":"");
            clean.Enabled=save.Enabled=pause.Enabled=!engine.Busy;status.Text=engine.Message;pause.Text=cfg.Enabled?"暂停自动清理":"恢复自动清理";if(detail.Text!=engine.Details)detail.Text=engine.Details;
        }
        public void Reveal(){Show();WindowState=FormWindowState.Normal;Activate();}
        public void FitToArea(Rectangle area){if(area.Width<1||area.Height<1)return;MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));}
        protected override void OnShown(EventArgs e){base.OnShown(e);if(!Preview)FitToArea(Screen.FromControl(this).WorkingArea);}
        protected override void Dispose(bool disposing){if(disposing)poll.Dispose();base.Dispose(disposing);}
    }
    public sealed class TrayContext:ApplicationContext {
        readonly MainWindow window;readonly NotifyIcon tray;readonly System.Windows.Forms.Timer signalTimer;
        public TrayContext(Engine engine,EventWaitHandle signal,bool background){
            window=new MainWindow(engine);var menu=new ContextMenuStrip();menu.Items.Add("打开内存清理",null,(s,e)=>window.Reveal());menu.Items.Add("打开日志目录",null,(s,e)=>{Directory.CreateDirectory(Storage.DirectoryPath);Process.Start(new ProcessStartInfo(Storage.DirectoryPath){UseShellExecute=true});});menu.Items.Add("退出",null,(s,e)=>{if(engine.Busy){MessageBox.Show(window,"请等待当前清理结束后退出。","内存清理");return;}ExitThread();});
            tray=new NotifyIcon{Icon=window.Icon,Text="ClearMemory · 内存清理",ContextMenuStrip=menu,Visible=true};tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)window.Reveal();};
            signalTimer=new System.Windows.Forms.Timer{Interval=150};signalTimer.Tick+=(s,e)=>{if(signal.WaitOne(0))window.Reveal();};signalTimer.Start();if(!background)window.Show();
        }
        protected override void ExitThreadCore(){signalTimer.Dispose();tray.Visible=false;tray.Dispose();window.AllowExit=true;window.Dispose();base.ExitThreadCore();}
    }
    static class Program {
        [STAThread] static void Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string sid=WindowsIdentity.GetCurrent().User.Value;bool created;
            using(var mutex=new Mutex(true,"Local\\ClearMemory-"+sid,out created))
            using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ClearMemory-Show-"+sid)){
                if(!created){signal.Set();return;}
                try{Application.Run(new TrayContext(new Engine(),signal,Array.IndexOf(args,"--background")>=0));}catch(Exception ex){MessageBox.Show(ex.Message,"ClearMemory 无法启动",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                finally{mutex.ReleaseMutex();}
            }
        }
    }
}
