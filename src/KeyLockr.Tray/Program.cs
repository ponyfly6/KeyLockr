using System;
using System.Windows.Forms;
using KeyLockr.Core;

namespace KeyLockr.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}
