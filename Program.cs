using System;
using System.Windows.Forms;

namespace FXOAiTranslator
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm()); // ? Make sure this says MainForm, not Form1
        }
    }
}