using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClearMemory {
    public sealed class MemoryCommand {public string Name,Privilege;public int InformationClass;public byte[] Buffer;}
    public sealed class SystemMemoryStep {public string Name,Status,Message;public bool Success;public long DurationMs;public ulong PagesCombined;}
    public interface ISystemMemoryApi {string EnablePrivilege(string name);int SetInformation(int informationClass,byte[] buffer);}
    public static class SystemMemory {
        // Independent use of Windows native system-information operations.
        // No vendor DLL, service or proprietary implementation is loaded.
        // Native structure widths follow the current process ABI, not the x86 DLL.
        public static MemoryCommand[] Plan(int pointerSize){
            if(pointerSize!=4&&pointerSize!=8)throw new ArgumentException("Unsupported pointer size");
            var cache=new byte[pointerSize==8?64:36];int minimumOffset=pointerSize==8?24:12;
            for(int i=minimumOffset;i<minimumOffset+pointerSize*2;i++)cache[i]=255;
            return new[]{
                new MemoryCommand{Name="合并相同内存页",Privilege="SeProfileSingleProcessPrivilege",InformationClass=130,Buffer=new byte[pointerSize*3]},
                new MemoryCommand{Name="回收系统文件缓存",Privilege="SeIncreaseQuotaPrivilege",InformationClass=21,Buffer=cache},
                new MemoryCommand{Name="清理系统工作集",Privilege="SeProfileSingleProcessPrivilege",InformationClass=80,Buffer=BitConverter.GetBytes(2)},
                new MemoryCommand{Name="回收低优先级备用页",Privilege="SeProfileSingleProcessPrivilege",InformationClass=80,Buffer=BitConverter.GetBytes(5)}
            };
        }
        public static List<SystemMemoryStep> Run(CancellationToken token){using(var api=new WindowsSystemMemoryApi())return Run(api,token);}
        public static List<SystemMemoryStep> Run(ISystemMemoryApi api,CancellationToken token){
            var results=new List<SystemMemoryStep>();
            foreach(var command in Plan(IntPtr.Size)){
                token.ThrowIfCancellationRequested();var timer=Stopwatch.StartNew();var step=new SystemMemoryStep{Name=command.Name};
                try{
                    string error=api.EnablePrivilege(command.Privilege);
                    if(error!=null){step.Status="PRIVILEGE_ERROR";step.Message=error;}
                    else{
                        int status=api.SetInformation(command.InformationClass,command.Buffer);step.Status="0x"+unchecked((uint)status).ToString("X8");step.Success=status>=0;
                        step.Message=step.Success?"系统请求执行成功":"系统拒绝或不支持本次请求";
                        if(step.Success&&command.InformationClass==130)step.PagesCombined=IntPtr.Size==8?BitConverter.ToUInt64(command.Buffer,8):BitConverter.ToUInt32(command.Buffer,4);
                    }
                }catch(Exception ex){if(ex is OperationCanceledException)throw;step.Status="API_ERROR";step.Message=ex.Message;}
                step.DurationMs=timer.ElapsedMilliseconds;results.Add(step);
            }
            return results;
        }
    }
    public sealed class WindowsSystemMemoryApi:ISystemMemoryApi,IDisposable {
        [StructLayout(LayoutKind.Sequential)]struct Luid {public uint Low;public int High;}
        [StructLayout(LayoutKind.Sequential)]struct Privilege {public uint Count;public Luid Luid;public uint Attributes;}
        [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool LookupPrivilegeValue(string system,string name,out Luid luid);
        [DllImport("advapi32.dll",SetLastError=true)]static extern bool AdjustTokenPrivileges(IntPtr token,bool disableAll,ref Privilege next,uint length,out Privilege previous,out uint returned);
        [DllImport("ntdll.dll")]static extern int NtSetSystemInformation(int informationClass,[In,Out]byte[] information,uint length);
        readonly List<Privilege> previous=new List<Privilege>();readonly Dictionary<string,string> enabled=new Dictionary<string,string>();IntPtr token;
        public string EnablePrivilege(string name){
            string saved;if(enabled.TryGetValue(name,out saved))return saved;
            if(token==IntPtr.Zero&&!OpenProcessToken(GetCurrentProcess(),0x28,out token))return enabled[name]="无法打开管理员进程令牌："+new Win32Exception(Marshal.GetLastWin32Error()).Message;
            Luid luid;if(!LookupPrivilegeValue(null,name,out luid))return enabled[name]="无法查询所需权限："+new Win32Exception(Marshal.GetLastWin32Error()).Message;
            var next=new Privilege{Count=1,Luid=luid,Attributes=2};Privilege old;uint returned;
            bool success=AdjustTokenPrivileges(token,false,ref next,(uint)Marshal.SizeOf(typeof(Privilege)),out old,out returned);int error=Marshal.GetLastWin32Error();
            if(!success||error!=0)return enabled[name]="所需权限不可用（"+name+"）："+new Win32Exception(error).Message;
            previous.Add(old);enabled[name]=null;return null;
        }
        public int SetInformation(int informationClass,byte[] information){return NtSetSystemInformation(informationClass,information,(uint)information.Length);}
        public void Dispose(){if(token==IntPtr.Zero)return;try{for(int i=previous.Count-1;i>=0;i--){var old=previous[i];Privilege ignored;uint returned;if(!AdjustTokenPrivileges(token,false,ref old,(uint)Marshal.SizeOf(typeof(Privilege)),out ignored,out returned))Storage.Log("清理后恢复进程权限失败："+Marshal.GetLastWin32Error());}}finally{CloseHandle(token);token=IntPtr.Zero;}}
    }
}
