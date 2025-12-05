using System;
using System.Threading;
using QuickFix;
using QuickFix.Transport;

namespace FXOptionsSimulator.FIX
{
    /// <summary>
    /// Minimal test to validate STP credentials
    /// </summary>
    public class STPCredentialTest : IApplication
    {
        private SocketInitiator _initiator;
        private SessionID _sessionID;
        private bool _logonReceived = false;
        private string _rejectReason = null;
        private SSLTunnelProxy _sslProxy;

        public void RunTest()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("STP CREDENTIAL TEST");
            Console.WriteLine("========================================\n");

            try
            {
                // Start SSL proxy first
                Console.WriteLine("[Test] Starting SSL proxy...");
                _sslProxy = new SSLTunnelProxy("quotes.stage2.gfifx.com", 443, 9443);
                _sslProxy.Start();
                Thread.Sleep(1000); // Give proxy time to start

                var settings = new SessionSettings("quickfix_stp.cfg");
                var storeFactory = new MemoryStoreFactory();
                var logFactory = new ScreenLogFactory(settings);

                _initiator = new SocketInitiator(this, storeFactory, settings, logFactory);
                _initiator.Start();

                // Get session ID
                var sessions = settings.GetSessions();
                if (sessions.Count > 0)
                {
                    _sessionID = sessions.First();
                    Console.WriteLine($"[Test] Session ID: {_sessionID}");
                }

                Console.WriteLine("[Test] Waiting 10 seconds for Logon response...\n");
                Thread.Sleep(10000);

                Console.WriteLine("\n========================================");
                Console.WriteLine("TEST RESULTS:");
                Console.WriteLine("========================================");

                if (_logonReceived)
                {
                    Console.WriteLine("✓ SUCCESS: Logon accepted by GFI");
                    Console.WriteLine("✓ STP credentials are valid");
                }
                else if (_rejectReason != null)
                {
                    Console.WriteLine($"✗ FAILED: Logon rejected");
                    Console.WriteLine($"  Reason: {_rejectReason}");
                }
                else
                {
                    Console.WriteLine("✗ FAILED: No response from GFI");
                    Console.WriteLine("  - Connection may have been rejected");
                    Console.WriteLine("  - Credentials may be invalid");
                    Console.WriteLine("  - Account may not be provisioned");
                }
                Console.WriteLine("========================================\n");

                _initiator.Stop();
                _sslProxy?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ TEST ERROR: {ex.Message}");
                Console.WriteLine($"  {ex.StackTrace}");
                _sslProxy?.Stop();
            }
        }

        // IApplication interface methods
        public void OnCreate(SessionID sessionID)
        {
            Console.WriteLine($"[Test] OnCreate: {sessionID}");
        }

        public void OnLogon(SessionID sessionID)
        {
            Console.WriteLine($"\n[Test] ✓✓✓ OnLogon: {sessionID}");
            Console.WriteLine($"[Test] ✓✓✓ LOGON SUCCESSFUL!");
            _logonReceived = true;
        }

        public void OnLogout(SessionID sessionID)
        {
            Console.WriteLine($"[Test] OnLogout: {sessionID}");
        }

        public void ToAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[Test] → ToAdmin: {msgType}");

            // Add STP credentials to Logon message
            if (msgType == QuickFix.Fields.MsgType.LOGON)
            {
                message.SetField(new QuickFix.Fields.Username("gfi_bfxo_swed_tc1"));
                message.SetField(new QuickFix.Fields.Password("ylhU6Q1eaxXf"));

                Console.WriteLine($"[Test] → Sending Logon with credentials:");
                Console.WriteLine($"         Username (553): gfi_bfxo_swed_tc1");
                Console.WriteLine($"         Password (554): ylhU6Q1eaxXf");
            }
        }

        public void FromAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[Test] ← FromAdmin: {msgType}");

            // Check for Reject message
            if (msgType == QuickFix.Fields.MsgType.REJECT)
            {
                string text = message.IsSetField(Tags.Text) ? message.GetString(Tags.Text) : "No reason provided";
                _rejectReason = text;
                Console.WriteLine($"[Test] ✗✗✗ REJECT received: {text}");
            }

            // Check for Logout with text (could indicate credential failure)
            if (msgType == QuickFix.Fields.MsgType.LOGOUT)
            {
                if (message.IsSetField(Tags.Text))
                {
                    string text = message.GetString(Tags.Text);
                    _rejectReason = text;
                    Console.WriteLine($"[Test] ✗✗✗ LOGOUT with reason: {text}");
                }
            }
        }

        public void ToApp(QuickFix.Message message, SessionID sessionID)
        {
            Console.WriteLine($"[Test] → ToApp: {message.Header.GetString(Tags.MsgType)}");
        }

        public void FromApp(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[Test] ← FromApp: {msgType}");

            // Any app-level message means we're logged in
            if (!_logonReceived && msgType != null)
            {
                Console.WriteLine($"[Test] ✓ Received app message - session is active");
            }
        }
    }
}
