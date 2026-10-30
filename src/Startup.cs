using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace ClearMemory
{
    public sealed class StartupStatus
    {
        public bool Exists, Enabled;
        public string Path, Arguments, RunLevel, Author, Xml;
    }
    public static class Startup
    {
        static string Sid { get { return WindowsIdentity.GetCurrent().User.Value; } }
        public static string TaskName { get { return "ClearMemory-" + Sid; } }
        static string PreferencePath { get { return Path.Combine(Storage.DirectoryPath, "startup.txt"); } }
        public static bool Wanted { get { try { return File.ReadAllText(PreferencePath).Trim() != "disabled"; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } } }
        public static string TaskXml(string exe, string userSid, bool enabled = true) {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo", new XElement(ns + "Author", "ClearMemory"), new XElement(ns + "Description", "登录后在托盘运行 ClearMemory，按用户设置自动清理内存。")),
                new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", true), new XElement(ns + "UserId", userSid), new XElement(ns + "Delay", "PT10S"))),
                new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "User"), new XElement(ns + "UserId", userSid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"), new XElement(ns + "DisallowStartIfOnBatteries", false), new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "StartWhenAvailable", true), new XElement(ns + "Enabled", enabled), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions", new XAttribute("Context", "User"), new XElement(ns + "Exec", new XElement(ns + "Command", exe), new XElement(ns + "Arguments", "--background"), new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(exe)))))).ToString();
        }
        public static bool IsMissingTaskError(Exception ex) { return ex!=null&&(ex.HResult==unchecked((int)0x80070002)||ex.HResult==unchecked((int)0x8004130F)); }
        static void CheckTaskName(string name){if(string.IsNullOrEmpty(name)||!name.StartsWith("ClearMemory-",StringComparison.Ordinal)||name.IndexOfAny(new[]{'\\','/'})>=0)throw new ArgumentException("无效的 ClearMemory 任务名。");}
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        static bool SameUser(string user, string sid) {
            if (string.Equals(user, sid, StringComparison.OrdinalIgnoreCase)) return true;
            if (user.StartsWith("S-", StringComparison.OrdinalIgnoreCase)) return false;
            using (var current = WindowsIdentity.GetCurrent()) {
                if (current.User.Value == sid && string.Equals(user, current.Name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            try { return ((SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier))).Value == sid; }
            catch (IdentityNotMappedException) { return false; }
            catch (ArgumentException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }
        public static bool HasEnabledLogonTrigger(XElement root, string sid) {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            XElement triggers = root.Element(ns + "Triggers");
            if (triggers == null) return false;
            foreach (XElement trigger in triggers.Elements(ns + "LogonTrigger")) {
                string enabled = (string)trigger.Element(ns + "Enabled"), user = (string)trigger.Element(ns + "UserId");
                if ((enabled == null || enabled == "true" || enabled == "1") && (string.IsNullOrEmpty(user) || SameUser(user, sid))) return true;
            }
            return false;
        }
        public static string Plan(StartupStatus status, bool wanted, string exe) {
            if (!status.Exists) return wanted ? "create" : "keep";
            if (status.Author != "ClearMemory") throw new InvalidOperationException("同名计划任务不属于 ClearMemory，未修改。请检查计划任务。");
            return !string.Equals(status.Path, exe, StringComparison.OrdinalIgnoreCase) || status.Arguments != "--background" || status.RunLevel != "HighestAvailable" ? "refresh" : "keep";
        }
        public static StartupStatus Reconcile(string exe) {
            var status = ReadStatus();
            string plan = Plan(status, Wanted, exe);
            if (plan == "create") Register(exe, true);
            else if (plan == "refresh") RegisterXml(RefreshXml(status.Xml, exe));
            status = ReadStatus();
            Storage.WriteSetting("startup.txt", status.Enabled ? "enabled" : "disabled");
            return status;
        }
        public static bool IsEnabled() { return ReadStatus().Enabled; }
        public static string RefreshXml(string xml, string exe) {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var doc = XDocument.Parse(xml);
            var action = doc.Root.Element(ns + "Actions").Element(ns + "Exec");
            action.ReplaceNodes(new XElement(ns + "Command", exe), new XElement(ns + "Arguments", "--background"), new XElement(ns + "WorkingDirectory", System.IO.Path.GetDirectoryName(exe)));
            doc.Root.Element(ns + "Principals").Element(ns + "Principal").SetElementValue(ns + "RunLevel", "HighestAvailable");
            // Keep the existing task Enabled bit and complete trigger configuration.
            return doc.ToString();
        }
        public static StartupStatus ReadStatus(){return ReadStatus(TaskName);}
        public static StartupStatus ReadStatus(string taskName) {
            CheckTaskName(taskName);
            dynamic service = null, folder = null, task = null;
            try {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); service.Connect(); folder = service.GetFolder("\\");
                try {
                    // A missing COM task can be marshaled as FileNotFoundException.
                    // Only treat a not-found HRESULT from GetTask as normal absence.
                    try{task = folder.GetTask(taskName);}catch(Exception ex){if(IsMissingTaskError(ex))return new StartupStatus();throw;}
                    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                    string xml = (string)task.Xml;
                    var root = XDocument.Parse(xml).Root;
                    var actions = root.Element(ns + "Actions"); var principals = root.Element(ns + "Principals"); var registration = root.Element(ns + "RegistrationInfo");
                    var action = actions == null ? null : actions.Element(ns + "Exec");
                    var principal = principals == null ? null : principals.Element(ns + "Principal");
                    if (action == null || principal == null) throw new InvalidDataException("自启动任务结构不完整，请在软件中重新设置。");
                    return new StartupStatus { Exists = true, Enabled = (bool)task.Enabled && HasEnabledLogonTrigger(root, Sid),
                        Xml = xml, Author = registration == null ? null : (string)registration.Element(ns + "Author"),
                        Path = (string)action.Element(ns + "Command"), Arguments = (string)action.Element(ns + "Arguments"), RunLevel = (string)principal.Element(ns + "RunLevel") };
                } catch {throw;}
            } finally { Release(task); Release(folder); Release(service); }
        }
        public static void SetEnabled(bool enabled, string exe){SetEnabled(enabled,exe,TaskName);}
        public static void SetEnabled(bool enabled,string exe,string taskName){
            CheckTaskName(taskName);string stage="读取当前任务";
            try{
                Storage.Log("startup.begin enabled="+enabled+" exe="+exe+" exists="+File.Exists(exe));
                var previous=ReadStatus(taskName);
                if(previous.Exists&&previous.Author!="ClearMemory")throw new InvalidOperationException("同名计划任务不属于 ClearMemory，未修改。");
                stage=enabled?"注册登录任务":"删除登录任务";
                if(enabled){if(!Path.IsPathRooted(exe)||!File.Exists(exe))throw new FileNotFoundException("请先把程序完整解压到固定目录，再开启自启动。",exe);RegisterXml(TaskXml(exe,Sid,true),taskName);}else Delete(taskName);
                stage="验证任务状态";var current=ReadStatus(taskName);
                if(enabled?(!current.Exists||!current.Enabled||!string.Equals(current.Path,exe,StringComparison.OrdinalIgnoreCase)||current.Arguments!="--background"||current.RunLevel!="HighestAvailable"):current.Exists)throw new InvalidOperationException("Windows 返回的任务状态与设置不一致。");
                stage="保存自启动偏好";Storage.WriteSetting("startup.txt",enabled?"enabled":"disabled");Storage.Log("startup.success enabled="+current.Enabled);
            }catch(Exception ex){Storage.LogException("startup."+stage,ex);throw new InvalidOperationException("自启动设置失败："+stage+"（0x"+unchecked((uint)ex.HResult).ToString("X8")+"）。"+ex.Message+"\n请导出日志用于排查。",ex);}
        }
        static void Register(string exe, bool enabled) {
            RegisterXml(TaskXml(exe, Sid, enabled));
        }
        static void RegisterXml(string xml){RegisterXml(xml,TaskName);}
        static void RegisterXml(string xml,string taskName) {
            dynamic service = null, folder = null, task = null;
            try {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); service.Connect(); folder = service.GetFolder("\\");
                task = folder.RegisterTask(taskName, xml, 6, Sid, null, 3, null);
            } finally { Release(task); Release(folder); Release(service); }
        }
        static void Delete(string taskName) {
            dynamic service = null, folder = null;
            try {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); service.Connect(); folder = service.GetFolder("\\");
                try { folder.DeleteTask(taskName, 0); } catch (Exception ex) { if (!IsMissingTaskError(ex)) throw; }
            } finally { Release(folder); Release(service); }
        }
    }
}
