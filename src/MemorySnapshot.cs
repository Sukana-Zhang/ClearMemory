using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace ClearMemory {
    public sealed class MemorySnapshot {
        public const long GiB=1073741824L;
        public ulong Total,Available;
        public bool Valid { get { return Total>0 && Available<=Total; } }
        public double TotalGB { get { return Total/(double)GiB; } }
        public double UsedGB { get { return Valid ? (Total-Available)/(double)GiB : 0; } }
        public double AvailableGB { get { return Available/(double)GiB; } }
        [StructLayout(LayoutKind.Sequential)] sealed class Status {
            public uint Length=(uint)Marshal.SizeOf(typeof(Status)),Load;
            public ulong Total,Available,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In,Out] Status s);
        public static MemorySnapshot Read() { var s=new Status(); return GlobalMemoryStatusEx(s) ? new MemorySnapshot {Total=s.Total,Available=s.Available} : new MemorySnapshot(); }
    }
}
