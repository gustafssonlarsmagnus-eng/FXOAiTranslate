using FXOptionsSimulator.FIX;
using System;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Singleton to hold one FIX session for the entire application
    /// </summary>
    public static class GlobalFIXSession
    {
        private static GFIFIXSessionManager _instance;
        private static SSLTunnelProxy _sslProxy;  // ADD THIS
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
                            // START SSL PROXY FIRST
                            if (_sslProxy == null)
                            {
                                Console.WriteLine("\n============================================================");
                                Console.WriteLine("STARTING SSL PROXY");
                                Console.WriteLine("============================================================\n");

                                // Read SSL configuration from quickfix.cfg
                                string configFile = "quickfix.cfg";
                                string sslHost = "quotes.stage2.gfifx.com"; // default
                                int sslPort = 443; // default
                                int localPort = 9443; // default

                                try
                                {
                                    var settings = new QuickFix.SessionSettings(configFile);
                                    var sessions = settings.GetSessions();
                                    if (sessions.Count > 0)
                                    {
                                        var sessionDict = settings.Get(sessions[0]);

                                        if (sessionDict.Has("SSLTargetHost"))
                                            sslHost = sessionDict.GetString("SSLTargetHost");

                                        if (sessionDict.Has("SSLTargetPort"))
                                            sslPort = int.Parse(sessionDict.GetString("SSLTargetPort"));

                                        if (sessionDict.Has("SocketConnectPort"))
                                            localPort = int.Parse(sessionDict.GetString("SocketConnectPort"));

                                        Console.WriteLine($"[Global] SSL Config: {sslHost}:{sslPort} -> localhost:{localPort}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Global] Warning: Could not read SSL config from {configFile}: {ex.Message}");
                                    Console.WriteLine($"[Global] Using defaults: {sslHost}:{sslPort}");
                                }

                                _sslProxy = new SSLTunnelProxy(sslHost, sslPort, localPort);
                                _sslProxy.Start();

                                // Wait for proxy to be ready
                                Console.WriteLine("[Global] Waiting for SSL proxy to initialize...");
                                System.Threading.Thread.Sleep(2000);
                            }

                            // THEN START FIX SESSION
                            Console.WriteLine("[Global] Creating FIX session...");
                            _instance = new GFIFIXSessionManager("quickfix.cfg");
                            _instance.Start();

                            // Wait a moment for logon
                            System.Threading.Thread.Sleep(2000);

                            if (_instance.IsLoggedOn)
                            {
                                Console.WriteLine("[Global] ✓ FIX session ready");
                            }
                            else
                            {
                                Console.WriteLine("[Global] ⚠️ FIX session starting...");
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
                Console.WriteLine("[Global] Shutting down FIX session...");
                _instance.Stop();
                _instance = null;
            }

            if (_sslProxy != null)
            {
                Console.WriteLine("[Global] Shutting down SSL proxy...");
                _sslProxy.Stop();
                _sslProxy = null;
            }
        }
    }
}