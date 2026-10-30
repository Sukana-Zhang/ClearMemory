using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using ClearMemory;
class StartupDiagnosticsTests {
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static int Main(){
        string root=Path.Combine(Path.GetFullPath("build"),"diagnostics-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Storage.DirectoryPath=root;
        try{
            Check(Startup.IsMissingTaskError(new FileNotFoundException()),"managed missing-file task result is normal absence");
            Check(Startup.IsMissingTaskError(new COMException("missing",unchecked((int)0x80070002))),"COM missing-file task result is normal absence");
            Check(Startup.IsMissingTaskError(new COMException("missing",unchecked((int)0x8004130F))),"Task Scheduler unknown object is normal absence");
            Check(!Startup.IsMissingTaskError(new UnauthorizedAccessException()),"access denied cannot be mistaken for a missing task");
            Check(!Startup.IsMissingTaskError(new COMException("RPC unavailable",unchecked((int)0x800706BA))),"unavailable scheduler is not ignored");
            Check(!Startup.IsMissingTaskError(new DirectoryNotFoundException()),"missing scheduler folder is not ignored");
            Check(!Startup.IsMissingTaskError(new InvalidOperationException()),"unrelated errors are preserved");
            Check(!Startup.IsMissingTaskError(null),"missing error object is not classified as task absence");
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Storage.Log("fixture profile="+profile+" SID=S-1-5-21-111-222-333-1001");
            Storage.LogException("startup.registration",new COMException("fixture missing object",unchecked((int)0x80070002)));
            string current=File.ReadAllText(Storage.LogPath);
            Check(current.Contains("startup.registration")&&current.Contains("System.Runtime.InteropServices.COMException")&&current.Contains("0x80070002"),"startup failure logs stage, exception type and HRESULT");
            Check(!current.Contains(profile)&&current.Contains("%USERPROFILE%"),"logs redact the current user profile path");
            Check(!current.Contains("S-1-5-21-111-222-333-1001")&&current.Contains("<user-sid>"),"logs redact user SIDs");
            File.WriteAllText(Path.Combine(root,"activity.previous.log"),"previous-run-fixture");
            File.WriteAllText(Path.Combine(root,"settings.json"),"private-settings-fixture-never-export");
            string output=Path.Combine(root,"共享 诊断.txt");Diagnostics.Export(output);string exported=File.ReadAllText(output);
            Check(exported.Contains("ClearMemory diagnostics")&&exported.Contains("version=")&&exported.Contains("os=")&&exported.Contains("elevated="),"export includes application and system diagnostic context");
            Check(exported.Contains("previous-run-fixture")&&exported.Contains("startup.registration"),"export includes current and previous logs");
            Check(!exported.Contains("private-settings-fixture-never-export"),"export never includes settings file contents");
            byte[] bytes=File.ReadAllBytes(output);Check(bytes[0]==239&&bytes[1]==187&&bytes[2]==191,"export uses UTF-8 BOM for Windows text viewers");
            bool rejected=false;try{Diagnostics.Export(Storage.LogPath);}catch(ArgumentException){rejected=true;}Check(rejected&&File.ReadAllText(Storage.LogPath)==current,"export cannot overwrite the original active log");
            string large=Path.Combine(root,"large.log");File.WriteAllText(large,"old-prefix-fixture"+new string('x',2*1024*1024)+"recent-tail-fixture",new UTF8Encoding(false));
            string tail=Diagnostics.ReadTail(large);Check(tail.Length<=1024*1024&&!tail.Contains("old-prefix-fixture")&&tail.EndsWith("recent-tail-fixture"),"large diagnostic logs are bounded to their recent tail");
            Check(Diagnostics.ReadTail(Path.Combine(root,"missing.log"))=="(not present)","missing rotated log does not prevent exporting");
            File.WriteAllText("build/startup-diagnostics-results.json",new JavaScriptSerializer().Serialize(new{checks=checks,actualCleanup=false,startupRegistered=false}));
            Console.WriteLine(checks+" startup and diagnostic checks passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Directory.Delete(root,true);}
    }
}
