using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FXOAiTranslator
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static void Main()
        {
            AllocConsole();
            Console.WriteLine("=== DEBUG CONSOLE ENABLED ===");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}