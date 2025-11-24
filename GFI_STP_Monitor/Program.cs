using System;
using System.Threading;

namespace GFI_STP_Monitor
{
    class Program
    {
        private static SSLTunnelProxy _sslProxy;
        private static STPFixSession _fixSession;

        static void Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        GFI STP Monitor - Standalone Connector             ║");
            Console.WriteLine("║        Account: GFI_BFXO_SWED_TC1                         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                // Start SSL proxy
                Console.WriteLine("[STP Monitor] Starting SSL proxy...");
                _sslProxy = new SSLTunnelProxy("quotes.stage2.gfifx.com", 443, 9444);
                _sslProxy.Start();
                Console.WriteLine("[STP Monitor] ✓ SSL proxy started (localhost:9444 -> quotes.stage2.gfifx.com:443)\n");

                // Wait for proxy to initialize
                Thread.Sleep(2000);

                // Start FIX session
                Console.WriteLine("[STP Monitor] Starting FIX session...");
                _fixSession = new STPFixSession("stp_quickfix.cfg");
                _fixSession.Start();

                Console.WriteLine("\n[STP Monitor] ✓ Application running");
                Console.WriteLine("[STP Monitor] Monitoring STP messages...");
                Console.WriteLine("[STP Monitor] Press Ctrl+C to exit\n");

                // Handle Ctrl+C gracefully
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Shutdown();
                };

                // Keep running until interrupted
                while (true)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[STP Monitor] ✗ FATAL ERROR: {ex.Message}");
                Console.WriteLine($"[STP Monitor] Stack: {ex.StackTrace}");
                Shutdown();
                Environment.Exit(1);
            }
        }

        static void Shutdown()
        {
            Console.WriteLine("\n[STP Monitor] Shutting down...");

            if (_fixSession != null)
            {
                _fixSession.Stop();
                _fixSession = null;
            }

            if (_sslProxy != null)
            {
                _sslProxy.Stop();
                _sslProxy = null;
            }

            Console.WriteLine("[STP Monitor] ✓ Shutdown complete");
        }
    }
}
