using System;
using QuickFix;
using QuickFix.Fields;
using QuickFix.Transport;

namespace GFI_STP_Monitor
{
    public class STPFixSession : IApplication
    {
        private SocketInitiator _initiator;
        private SessionSettings _settings;
        private IMessageStoreFactory _storeFactory;
        private ILogFactory _logFactory;

        public STPFixSession(string configFile)
        {
            _settings = new SessionSettings(configFile);
            _storeFactory = new FileStoreFactory(_settings);
            _logFactory = new FileLogFactory(_settings);
        }

        public void Start()
        {
            _initiator = new SocketInitiator(this, _storeFactory, _settings, _logFactory);
            _initiator.Start();
            Console.WriteLine("[STP Monitor] FIX initiator started");
        }

        public void Stop()
        {
            if (_initiator != null)
            {
                _initiator.Stop();
                Console.WriteLine("[STP Monitor] FIX initiator stopped");
            }
        }

        // Called when application starts up
        public void OnCreate(SessionID sessionID)
        {
            Console.WriteLine($"[STP Monitor] Session created: {sessionID}");
        }

        // Called when FIX session is logged on
        public void OnLogon(SessionID sessionID)
        {
            Console.WriteLine($"[STP Monitor] ✓ LOGGED ON: {sessionID}");
            Console.WriteLine($"[STP Monitor] Monitoring STP messages...");
        }

        // Called when FIX session is logged out
        public void OnLogout(SessionID sessionID)
        {
            Console.WriteLine($"[STP Monitor] ✗ LOGGED OUT: {sessionID}");
        }

        // Called for application messages (non-admin)
        public void FromApp(QuickFix.Message message, SessionID sessionID)
        {
            Console.WriteLine($"\n[STP Monitor] ═══ INCOMING MESSAGE ═══");
            Console.WriteLine($"Session: {sessionID}");

            try
            {
                // Get message type
                var msgType = message.Header.GetField(Tags.MsgType);
                Console.WriteLine($"MsgType: {msgType}");

                // Check if this is a Trade Capture Report (AE)
                if (msgType == "AE")
                {
                    Console.WriteLine("📊 TRADE CAPTURE REPORT:");

                    // Extract common STP fields
                    if (message.IsSetField(Tags.TradeReportID))
                        Console.WriteLine($"  TradeReportID: {message.GetField(Tags.TradeReportID)}");

                    if (message.IsSetField(Tags.ExecID))
                        Console.WriteLine($"  ExecID: {message.GetField(Tags.ExecID)}");

                    if (message.IsSetField(Tags.TradeReportTransType))
                        Console.WriteLine($"  TradeReportTransType: {message.GetField(Tags.TradeReportTransType)}");

                    if (message.IsSetField(Tags.Symbol))
                        Console.WriteLine($"  Symbol: {message.GetField(Tags.Symbol)}");

                    if (message.IsSetField(Tags.Side))
                        Console.WriteLine($"  Side: {message.GetField(Tags.Side)}");

                    if (message.IsSetField(Tags.OrderQty))
                        Console.WriteLine($"  OrderQty: {message.GetField(Tags.OrderQty)}");

                    if (message.IsSetField(Tags.LastPx))
                        Console.WriteLine($"  LastPx: {message.GetField(Tags.LastPx)}");
                }

                // Print full message
                Console.WriteLine($"\nFull message:\n{message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP Monitor] Error processing message: {ex.Message}");
            }

            Console.WriteLine($"════════════════════════════════\n");
        }

        // Called for admin messages (heartbeats, logon, etc.)
        public void FromAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);

            // Only log non-heartbeat admin messages
            if (msgType != QuickFix.Fields.MsgType.HEARTBEAT)
            {
                Console.WriteLine($"[STP Monitor] Admin message: {msgType} from {sessionID}");
            }
        }

        // Called before sending application messages
        public void ToApp(QuickFix.Message message, SessionID sessionID)
        {
            Console.WriteLine($"[STP Monitor] Sending app message: {message.Header.GetField(Tags.MsgType)}");
        }

        // Called before sending admin messages (this is where we set credentials)
        public void ToAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);

            if (msgType == QuickFix.Fields.MsgType.LOGON)
            {
                // TESTING WITH TRADING CREDENTIALS (known working)
                string username = "swed.obo.stg.api";
                string password = "ZQcZokEOLjb9";

                message.SetField(new Username(username));
                message.SetField(new Password(password));

                // Trading session includes OnBehalfOfCompID in HEADER
                message.Header.SetField(new OnBehalfOfCompID("SWES"));

                Console.WriteLine($"[STP Monitor] Sending LOGON with username: {username}");
                Console.WriteLine($"[STP Monitor] Testing with TRADING credentials to isolate issue");
            }
        }
    }
}
