using FXOptionsSimulator.FIX;
using System;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Singleton to hold FIX sessions (Trading + STP) for the entire application
    /// </summary>
    public static class GlobalFIXSession
    {
        private static GFIFIXSessionManager _instance;
        private static SSLTunnelProxy _tradingProxy;  // Trading SSL proxy (port 9443)
        private static SSLTunnelProxy _stpProxy;      // STP SSL proxy (port 9444)
        private static readonly object _lock = new object();

        public static GFIFIXSessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            Console.WriteLine("\n============================================================");
                            Console.WriteLine("STARTING SSL PROXIES (Trading + STP)");
                            Console.WriteLine("============================================================\n");

                            // START TRADING SSL PROXY (port 9443)
                            if (_tradingProxy == null)
                            {
                                Console.WriteLine("[Global] Starting Trading SSL proxy...");
                                _tradingProxy = new SSLTunnelProxy("quotes.stage2.gfifx.com", 443, 9443);
                                _tradingProxy.Start();
                                Console.WriteLine("[Global] ✓ Trading SSL proxy started (localhost:9443 -> quotes.stage2.gfifx.com:443)");
                            }

                            // START STP SSL PROXY (port 9444)
                            if (_stpProxy == null)
                            {
                                Console.WriteLine("[Global] Starting STP SSL proxy...");
                                _stpProxy = new SSLTunnelProxy("quotes.stage2.gfifx.com", 443, 9444);
                                _stpProxy.Start();
                                Console.WriteLine("[Global] ✓ STP SSL proxy started (localhost:9444 -> quotes.stage2.gfifx.com:443)");
                            }

                            // Wait for proxies to be ready
                            Console.WriteLine("[Global] Waiting for SSL proxies to initialize...");
                            System.Threading.Thread.Sleep(2000);

                            // THEN START FIX SESSIONS (both Trading and STP)
                            Console.WriteLine("[Global] Creating FIX sessions (Trading + STP)...");
                            _instance = new GFIFIXSessionManager("quickfix.cfg");
                            _instance.Start();

                            // Wait a moment for logons
                            System.Threading.Thread.Sleep(3000);

                            if (_instance.IsLoggedOn)
                            {
                                Console.WriteLine("[Global] ✓ FIX sessions ready");
                            }
                            else
                            {
                                Console.WriteLine("[Global] ⚠️ FIX sessions starting...");
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        public static bool IsConnected => _instance?.IsLoggedOn ?? false;

        public static void Shutdown()
        {
            if (_instance != null)
            {
                Console.WriteLine("[Global] Shutting down FIX sessions...");
                _instance.Stop();
                _instance = null;
            }

            if (_tradingProxy != null)
            {
                Console.WriteLine("[Global] Shutting down Trading SSL proxy...");
                _tradingProxy.Stop();
                _tradingProxy = null;
            }

            if (_stpProxy != null)
            {
                Console.WriteLine("[Global] Shutting down STP SSL proxy...");
                _stpProxy.Stop();
                _stpProxy = null;
            }
        }
    }
}