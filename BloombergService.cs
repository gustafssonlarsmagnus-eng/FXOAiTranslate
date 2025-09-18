using Bloomberglp.Blpapi;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FXOAiTranslator
{
    public class BloombergService
    {
        private IntPtr _bloombergWindow = IntPtr.Zero;
        public bool IsConnected { get; private set; } = false;

        public BloombergService()
        {
            TryConnect();
        }

        // Try to locate Bloomberg OVML window
        public void TryConnect()
        {
            _bloombergWindow = FindWindowByTitle("OVML");
            if (_bloombergWindow != IntPtr.Zero)
            {
                IsConnected = true;
                Console.WriteLine($"Found Bloomberg window: {GetWindowTitle(_bloombergWindow)}");
                Console.WriteLine("Bloomberg Terminal detected - Connected");
            }
            else
            {
                Console.WriteLine("Bloomberg Terminal not detected.");
            }
        }

        // Send OVML command to Bloomberg
        public void SendOVML(string ovmlCommand)
        {
            if (!IsConnected || _bloombergWindow == IntPtr.Zero)
            {
                Console.WriteLine("[Bloomberg API] Not connected to Bloomberg.");
                return;
            }

            try
            {
                Console.WriteLine($"DEBUG: Attempting to send to Bloomberg: '{ovmlCommand}'");

                // Copy command to clipboard
                System.Windows.Forms.Clipboard.SetText(ovmlCommand);

                // Activate Bloomberg window
                SetForegroundWindow(_bloombergWindow);

                // Simulate CTRL+T (open new tab in Bloomberg)
                SendKeys.SendWait("^t");
                Task.Delay(100).Wait();

                // Paste command
                SendKeys.SendWait("^v");
                Task.Delay(50).Wait();

                // Press Enter
                SendKeys.SendWait("{ENTER}");

                Console.WriteLine("DEBUG: Commands sent to Bloomberg successfully");
                Console.WriteLine($"Sent to Bloomberg (new tab): {ovmlCommand}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] Failed to send command: {ex.Message}");
            }
        }

        // Simulate fetching a spot rate (stub for Bloomberg market data API)
        public async Task<double?> GetSpotRate(string underlying)
        {
            try
            {
                // Simulate async delay
                await Task.Delay(200);

                // This would be a real Bloomberg API call in production
                var random = new Random();
                double fakeSpot = 9.8000 + random.NextDouble() * 0.0500;

                Console.WriteLine($"[Bloomberg API] Got spot rate for {underlying}: {fakeSpot:F4}");
                return fakeSpot;
            }
            catch
            {
                return null;
            }
        }

        // ADD THIS METHOD HERE (after GetSpotRate method, before the comment "--- Helpers to find Bloomberg window ---"):
        public string DetermineCallOrPut(double strikePrice, string currencyPair)
        {
            // Logic to determine if it's a call or put based on strike vs market data
            // For now, simple placeholder logic:
            if (strikePrice > 1.0)
                return "CALL";
            else
                return "PUT";
        }
        // --- Helpers to find Bloomberg window ---
        private IntPtr FindWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd, out string windowTitle) && windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false; // stop searching
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            if (GetWindowText(hWnd, out string title))
                return title;
            return "";
        }
        public string DetermineCallOrPut(string tradeRequest)
        {
            // Add logic to determine if it's a Call or Put option
            var lowerRequest = tradeRequest.ToLower();

            if (lowerRequest.Contains("call") || lowerRequest.Contains("buy option"))
                return "C";
            else if (lowerRequest.Contains("put") || lowerRequest.Contains("sell option"))
                return "P";
            else
                return "C"; // Default to Call
        }
        // --- Win32 API Imports ---
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static bool GetWindowText(IntPtr hWnd, out string text)
        {
            var sb = new StringBuilder(256);
            int length = GetWindowText(hWnd, sb, sb.Capacity);
            text = sb.ToString();
            return length > 0;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
