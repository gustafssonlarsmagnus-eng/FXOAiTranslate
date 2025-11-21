using QuickFix;
using QuickFix.Transport;
using System;
using System.IO;

namespace FXOptionsSimulator.FIX
{
    /// <summary>
    /// Manages the STP FIX session for receiving Trade Capture Reports (35=AE)
    /// This is separate from the pricing session
    /// </summary>
    public class GFISTPSessionManager
    {
        private readonly SocketInitiator _initiator;
        private readonly SessionSettings _settings;
        private readonly GFISTPApplication _application;
        private SessionID _sessionID;

        public GFISTPApplication Application => _application;
        public bool IsLoggedOn => _application.IsLoggedOn;

        public GFISTPSessionManager(string configFile = "quickfix_stp.cfg")
        {
            Console.WriteLine($"[STP Manager] Initializing with config: {configFile}");
            Console.WriteLine($"[STP Manager] Config file path: {Path.GetFullPath(configFile)}");
            Console.WriteLine($"[STP Manager] Config exists: {File.Exists(configFile)}");

            _application = new GFISTPApplication();

            try
            {
                Console.WriteLine("[STP Manager] Loading SessionSettings...");
                _settings = new SessionSettings(configFile);
                Console.WriteLine("[STP Manager] SessionSettings loaded successfully");

                Console.WriteLine("\n[STP Manager] === Config Contents ===");
                var sessions = _settings.GetSessions();
                Console.WriteLine($"[STP Manager] Number of sessions: {sessions.Count}");

                foreach (var sessionID in sessions)
                {
                    Console.WriteLine($"[STP Manager] Session: {sessionID}");
                    var dict = _settings.Get(sessionID);

                    string[] commonKeys = {
                        "BeginString", "SenderCompID", "TargetCompID",
                        "SocketConnectHost", "SocketConnectPort", "HeartBtInt",
                        "ConnectionType", "ResetOnLogon", "EncryptMethod"
                    };

                    foreach (var key in commonKeys)
                    {
                        try
                        {
                            if (dict.Has(key))
                            {
                                var value = dict.GetString(key);
                                Console.WriteLine($"  {key} = {value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  {key} = ERROR: {ex.Message}");
                        }
                    }
                }
                Console.WriteLine("[STP Manager] === End Config Contents ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP Manager] ✗ ERROR loading config: {ex.Message}");
                Console.WriteLine($"[STP Manager] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[STP Manager] Stack trace: {ex.StackTrace}");
                throw;
            }

            try
            {
                Console.WriteLine("[STP Manager] Creating MemoryStoreFactory...");
                var storeFactory = new MemoryStoreFactory();
                Console.WriteLine("[STP Manager] ✓ MemoryStoreFactory created");

                Console.WriteLine("[STP Manager] Creating ScreenLogFactory...");
                var logFactory = new ScreenLogFactory(_settings);
                Console.WriteLine("[STP Manager] ✓ ScreenLogFactory created");

                Console.WriteLine("[STP Manager] Creating SocketInitiator...");
                _initiator = new SocketInitiator(_application, storeFactory, _settings, logFactory);
                Console.WriteLine("[STP Manager] ✓ SocketInitiator created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP Manager] ✗ ERROR creating initiator: {ex.Message}");
                throw;
            }
        }

        public void Start()
        {
            try
            {
                Console.WriteLine("[STP Manager] Starting FIX initiator...");
                _initiator.Start();

                var sessions = _settings.GetSessions();
                if (sessions.Count > 0)
                {
                    _sessionID = sessions[0];
                    Console.WriteLine($"[STP Manager] ✓ Session started: {_sessionID}");
                }
                else
                {
                    Console.WriteLine("[STP Manager] ✗ No sessions configured");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP Manager] ✗ ERROR starting: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            try
            {
                Console.WriteLine("[STP Manager] Stopping FIX initiator...");
                _initiator.Stop();
                Console.WriteLine("[STP Manager] ✓ Stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP Manager] ✗ ERROR stopping: {ex.Message}");
            }
        }
    }
}
