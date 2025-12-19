using Bloomberglp.Blpapi;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FXOptionsSimulator
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
            _bloombergWindow = FindBloombergWindow();
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
                // Re-verify Bloomberg window is still valid
                string currentTitle = GetWindowTitle(_bloombergWindow);
                if (currentTitle.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                    currentTitle.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
                    currentTitle.Contains("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[Bloomberg API] ERROR: Browser window detected, reconnecting...");
                    TryConnect();
                    if (!IsConnected) return;
                }

                Console.WriteLine($"DEBUG: Attempting to send to Bloomberg: '{ovmlCommand}'");
                Console.WriteLine($"DEBUG: Target window: '{currentTitle}'");

                // Copy command to clipboard
                System.Windows.Forms.Clipboard.SetText(ovmlCommand);

                // Activate Bloomberg window with retry
                for (int i = 0; i < 3; i++)
                {
                    SetForegroundWindow(_bloombergWindow);
                    Task.Delay(200).Wait();

                    // Verify window is in foreground
                    if (GetForegroundWindow() == _bloombergWindow)
                    {
                        break;
                    }
                }

                // Simulate CTRL+T (open new tab in Bloomberg)
                SendKeys.SendWait("^t");
                Task.Delay(150).Wait();

                // Paste command
                SendKeys.SendWait("^v");
                Task.Delay(100).Wait();

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

        // Fetch a spot rate from Bloomberg if possible
        public async Task<double?> GetSpotRate(string underlying)
        {
            try
            {
                Console.WriteLine($"[Bloomberg API] Requesting spot rate for {underlying}");

                using var session = new Session();
                if (!session.Start())
                {
                    Console.WriteLine("[Bloomberg API] Failed to start session");
                    return null;
                }

                if (!session.OpenService("//blp/refdata"))
                {
                    Console.WriteLine("[Bloomberg API] Failed to open reference data service");
                    return null;
                }

                var service = session.GetService("//blp/refdata");
                var request = service.CreateRequest("ReferenceDataRequest");
                request.Append("securities", $"{underlying} Curncy");
                request.Append("fields", "PX_LAST");

                session.SendRequest(request, null);

                while (true)
                {
                    var evt = session.NextEvent(5000);
                    if (evt == null)
                    {
                        Console.WriteLine("[Bloomberg API] Timeout waiting for response");
                        break;
                    }

                    foreach (var msg in evt)
                    {
                        if (msg.HasElement("securityData"))
                        {
                            var securityDataArray = msg.GetElement("securityData");
                            for (int i = 0; i < securityDataArray.NumValues; i++)
                            {
                                var securityData = securityDataArray.GetValueAsElement(i);

                                if (securityData.HasElement("fieldData"))
                                {
                                    var fieldData = securityData.GetElement("fieldData");
                                    if (fieldData.HasElement("PX_LAST"))
                                    {
                                        double spot = fieldData.GetElementAsFloat64("PX_LAST");
                                        Console.WriteLine($"[Bloomberg API] Got spot rate for {underlying}: {spot:F4}");
                                        return spot;
                                    }
                                }

                                if (securityData.HasElement("securityError"))
                                {
                                    var error = securityData.GetElement("securityError");
                                    Console.WriteLine($"[Bloomberg API] Security error: {error.GetElementAsString("message")}");
                                }
                            }
                        }
                    }

                    if (evt.Type == Event.EventType.RESPONSE) break;
                }

                Console.WriteLine($"[Bloomberg API] No data returned for {underlying}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] Error fetching spot for {underlying}: {ex.Message}");
                return null;
            }
        }

        public string DetermineCallOrPut(double strikePrice, string currencyPair)
        {
            if (strikePrice > 1.0)
                return "CALL";
            else
                return "PUT";
        }

        public string DetermineCallOrPut(string tradeRequest)
        {
            var lowerRequest = tradeRequest.ToLower();

            if (lowerRequest.Contains("call") || lowerRequest.Contains("buy option"))
                return "C";
            else if (lowerRequest.Contains("put") || lowerRequest.Contains("sell option"))
                return "P";
            else
                return "C";
        }

        // --- Bloomberg Window Detection ---

        private IntPtr FindBloombergWindow()
        {
            // Try to find Bloomberg by process name first (most reliable)
            IntPtr bloombergWnd = FindWindowByProcessName("bplus");
            if (bloombergWnd != IntPtr.Zero) return bloombergWnd;

            // Fallback: Look for OVML windows (any currency pair), excluding browsers
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd, out string windowTitle))
                {
                    // Check if it contains OVML and is NOT a browser
                    if (windowTitle.Contains("OVML", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Firefox", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Edge", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Safari", StringComparison.OrdinalIgnoreCase))
                    {
                        found = hWnd;
                        return false; // Stop searching
                    }
                }
                return true; // Continue searching
            }, IntPtr.Zero);

            return found;
        }

        private IntPtr FindWindowByProcessName(string processName)
        {
            IntPtr found = IntPtr.Zero;

            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    found = processes[0].MainWindowHandle;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg] Error finding by process: {ex.Message}");
            }

            return found;
        }

        private IntPtr FindWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd, out string windowTitle) &&
                    windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}