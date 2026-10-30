using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClearMemory
{
    public static class Clock
    {
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        // Uptime includes sleep and is independent of wall-clock corrections.
        public static long Now { get { return (long)GetTickCount64(); } }
    }
    public static class Storage
    {
        public static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearMemory");
        static readonly object LogLock = new object();
        public static string LogPath { get { return Path.Combine(DirectoryPath, "activity.log"); } }
        public static bool LoadEnabled() {
            try { return File.ReadAllText(Path.Combine(DirectoryPath, "paused.txt")).Trim() != "paused"; } catch (IOException) { return true; } catch (UnauthorizedAccessException) { return true; }
        }
        public static void SaveEnabled(bool enabled) { WriteSetting("paused.txt", enabled ? "running" : "paused"); }
        public static void WriteSetting(string name, string value) {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, name), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Log(string text) {
            lock (LogLock) try {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024) {
                    string archive = Path.Combine(DirectoryPath, "activity.previous.log");
                    if (File.Exists(archive)) File.Delete(archive);
                    File.Move(LogPath, archive);
                }
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Diagnostics.Redact(text) + Environment.NewLine, Encoding.UTF8);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public static void LogException(string stage,Exception error){Log(stage+" · "+error.GetType().FullName+" · HRESULT=0x"+unchecked((uint)error.HResult).ToString("X8")+Environment.NewLine+error);}
    }
}
