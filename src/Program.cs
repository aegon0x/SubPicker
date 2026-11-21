using System;
using System.Drawing;
using System.Windows.Forms;

namespace SubPicker
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Th.Init(g.DpiX);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "SubPicker - error");
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
