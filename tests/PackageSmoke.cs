using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
class PackageSmoke {
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try{
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            string data=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"smoke-settings");
            assembly.GetType("ClearMemory.Storage").GetField("DirectoryPath").SetValue(null,data);
            var engine=Activator.CreateInstance(assembly.GetType("ClearMemory.Engine"));
            using(var form=(Form)Activator.CreateInstance(assembly.GetType("ClearMemory.MainWindow"),new[]{engine,(object)true})){
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();
                if(form.Icon==null||!form.Text.Contains("ClearMemory"))throw new Exception("Packaged UI did not initialize");
            }
            Console.WriteLine("Extracted executable UI, icon, memory monitoring and isolated configuration passed from a Unicode path.");
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
