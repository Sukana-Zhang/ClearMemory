using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClearMemory;
class SystemMemoryTests {
    static int checks;static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    sealed class Fake:ISystemMemoryApi {
        public readonly List<int> Classes=new List<int>();public readonly List<string> Privileges=new List<string>();public int FailClass=-1,ThrowClass=-1;public string DenyPrivilege;
        public string EnablePrivilege(string name){Privileges.Add(name);return name==DenyPrivilege?"fixture permission denied":null;}
        public int SetInformation(int c,byte[] b){Classes.Add(c);if(c==ThrowClass)throw new InvalidOperationException("fixture unsupported API");if(c==FailClass)return unchecked((int)0xC0000061);if(c==130)Array.Copy(BitConverter.GetBytes(42UL),0,b,IntPtr.Size,IntPtr.Size);return 0;}
    }
    static int Main(){try{
        foreach(int ptr in new[]{4,8}){
            var plan=SystemMemory.Plan(ptr);Check(plan.Length==4,"default has exactly four operations (ABI "+ptr+")");
            Check(plan.Select(p=>p.InformationClass).SequenceEqual(new[]{130,21,80,80}),"default class order matches local disassembly (ABI "+ptr+")");
            Check(plan[0].Buffer.Length==ptr*3&&plan[0].Buffer.All(b=>b==0),"combine structure has zero handle, page count and flags (ABI "+ptr+")");
            Check(plan[1].Buffer.Length==(ptr==8?64:36),"file-cache structure has correct native alignment (ABI "+ptr+")");
            int offset=ptr==8?24:12;Check(plan[1].Buffer.Skip(offset).Take(ptr*2).All(b=>b==255),"file-cache minimum/maximum are SIZE_T -1 (ABI "+ptr+")");
            Check(plan[1].Buffer.Take(offset).Concat(plan[1].Buffer.Skip(offset+ptr*2)).All(b=>b==0),"cache flags and other fields are zero (ABI "+ptr+")");
            Check(BitConverter.ToInt32(plan[2].Buffer,0)==2&&BitConverter.ToInt32(plan[3].Buffer,0)==5,"working-set command 2 then priority-zero standby command 5 (ABI "+ptr+")");
            Check(plan.Where(p=>p.InformationClass==80).All(p=>p.Buffer.Length==4),"memory-list command is 32-bit under either ABI (ABI "+ptr+")");
            Check(plan[1].Privilege=="SeIncreaseQuotaPrivilege"&&plan[0].Privilege=="SeProfileSingleProcessPrivilege","operation privileges match required Windows privileges (ABI "+ptr+")");
        }
        var api=new Fake();var result=SystemMemory.Run(api,CancellationToken.None);Check(result.Count==4&&result.All(s=>s.Success),"all successful native statuses reported");Check(result[0].PagesCombined==42,"combined page count parsed using native pointer width");Check(api.Classes.SequenceEqual(new[]{130,21,80,80}),"pipeline executes exact default order");
        api=new Fake{FailClass=21};result=SystemMemory.Run(api,CancellationToken.None);Check(!result[1].Success&&result[1].Status=="0xC0000061","NT privilege failure is surfaced verbatim");Check(result[2].Success&&result[3].Success,"one failed step does not suppress later default steps");
        api=new Fake{DenyPrivilege="SeProfileSingleProcessPrivilege"};result=SystemMemory.Run(api,CancellationToken.None);Check(result.Count(s=>s.Success)==1&&api.Classes.SequenceEqual(new[]{21}),"unavailable privileges cannot produce false successful calls");
        api=new Fake{ThrowClass=21};result=SystemMemory.Run(api,CancellationToken.None);Check(result[1].Status=="API_ERROR"&&result[3].Success,"unsupported API is reported without aborting unrelated steps");
        var report=new CleanResult{Steps=result,Before=new MemorySnapshot{Total=1073741824,Available=1073741824},After=new MemorySnapshot{Total=1073741824,Available=1060634624}};Check(report.Summary.Contains("3/4")&&report.Detail.Contains("-12.5"),"partial success and negative physical-memory delta remain visible");
        bool canceled=false;api=new Fake();using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{SystemMemory.Run(api,cancel.Token);}catch(OperationCanceledException){canceled=true;}}Check(canceled&&api.Classes.Count==0,"pre-cancellation performs no system operations");
        bool rejected=false;try{SystemMemory.Plan(16);}catch(ArgumentException){rejected=true;}Check(rejected,"unsupported ABI rejected");
        Console.WriteLine(checks+" system-memory checks passed");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
