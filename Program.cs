using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslator
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static void Main(string[] args)
        {
            AllocConsole();
            Console.WriteLine("=== DEBUG CONSOLE ENABLED ===");

            // Check for STP credential test flag
            if (args.Length > 0 && args[0] == "--test-stp")
            {
                Console.WriteLine("\n*** Running STP Credential Test ***\n");
                var test = new STPCredentialTest();
                test.RunTest();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}