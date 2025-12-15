using System;
using FXOptionsSimulator.FIX;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Global singleton for STP FIX session
    /// Receives Trade Capture Reports (35=AE) after trade execution
    /// </summary>
    public class GlobalSTPSession
    {
        private static GFISTPSessionManager _instance;
        private static readonly object _lock = new object();

        public static GFISTPSessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            Console.WriteLine("[GlobalSTPSession] Creating new STP session instance");
                            try
                            {
                                _instance = new GFISTPSessionManager("quickfix_stp.cfg");
                                _instance.Start();
                                Console.WriteLine("[GlobalSTPSession] ✓ STP session started");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[GlobalSTPSession] ✗ Failed to create STP session: {ex.Message}");
                                throw;
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        public static void Stop()
        {
            if (_instance != null)
            {
                Console.WriteLine("[GlobalSTPSession] Stopping STP session");
                _instance.Stop();
                _instance = null;
            }
        }
    }
}
