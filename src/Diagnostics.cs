using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClearMemory {
    public static class Diagnostics {
        public static string Redact(string text){
            string result=text??"",profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(!string.IsNullOrEmpty(profile))result=Regex.Replace(result,Regex.Escape(profile),"%USERPROFILE%",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            return Regex.Replace(result,@"S-1-5-21-\d+-\d+-\d+-\d+","<user-sid>");
        }
        public static string Header(){
            string elevated="unknown";try{using(var who=WindowsIdentity.GetCurrent())elevated=new WindowsPrincipal(who).IsInRole(WindowsBuiltInRole.Administrator).ToString();}catch(SystemException){}
            return "ClearMemory diagnostics"+Environment.NewLine+"version="+Assembly.GetExecutingAssembly().GetName().Version+Environment.NewLine+"os="+Environment.OSVersion.VersionString+Environment.NewLine+"process64="+Environment.Is64BitProcess+" os64="+Environment.Is64BitOperatingSystem+" elevated="+elevated+Environment.NewLine+"clr="+Environment.Version+Environment.NewLine+"exe="+Application.ExecutablePath;
        }
        public static string ReadTail(string path){
            if(!File.Exists(path))return "(not present)";
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                int length=(int)Math.Min(stream.Length,1024*1024);var bytes=new byte[length];stream.Position=Math.Max(0,stream.Length-length);int read=0,n;while(read<length&&(n=stream.Read(bytes,read,length-read))>0)read+=n;return Encoding.UTF8.GetString(bytes,0,read);
            }
        }
        public static void Export(string destination){
            if(string.IsNullOrWhiteSpace(destination))throw new ArgumentException("请选择导出文件位置。");
            string target=Path.GetFullPath(destination);
            if(string.Equals(target,Path.GetFullPath(Storage.LogPath),StringComparison.OrdinalIgnoreCase)||string.Equals(target,Path.GetFullPath(Path.Combine(Storage.DirectoryPath,"activity.previous.log")),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("导出文件不能覆盖原始日志。");
            var content=new StringBuilder(Header()).AppendLine().AppendLine("exported="+DateTimeOffset.Now.ToString("o"));
            foreach(string name in new[]{"activity.previous.log","activity.log"}){content.AppendLine().AppendLine("--- "+name+" ---");try{content.AppendLine(ReadTail(Path.Combine(Storage.DirectoryPath,name)));}catch(IOException ex){content.AppendLine("read-error="+ex.GetType().Name);}catch(UnauthorizedAccessException){content.AppendLine("read-error=access-denied");}}
            File.WriteAllText(target,Redact(content.ToString()),new UTF8Encoding(true));
        }
    }
}
