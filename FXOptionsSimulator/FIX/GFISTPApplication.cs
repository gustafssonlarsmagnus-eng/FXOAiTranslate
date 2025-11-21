using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using System;

namespace FXOptionsSimulator.FIX
{
    /// <summary>
    /// FIX Application for STP session - receives Trade Capture Reports (35=AE)
    /// This is a separate session from the pricing session
    /// </summary>
    public class GFISTPApplication : MessageCracker, IApplication
    {
        public bool IsLoggedOn { get; private set; }

        // Event fired when a Trade Capture Report is received
        public event Action<CapturedTrade> OnTradeCaptureReceived;

        public void OnCreate(SessionID sessionID)
        {
            Console.WriteLine($"[STP] Session created: {sessionID}");
        }

        public void OnLogon(SessionID sessionID)
        {
            Console.WriteLine($"[STP] ✓ Logged on: {sessionID}");
            IsLoggedOn = true;
        }

        public void OnLogout(SessionID sessionID)
        {
            Console.WriteLine($"[STP] Logged out: {sessionID}");
            IsLoggedOn = false;
        }

        public void ToAdmin(Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[STP] → Admin message: {msgType}");
        }

        public void ToApp(Message message, SessionID sessionID)
        {
            // STP session is receive-only, we don't send app messages
            Console.WriteLine($"[STP] → App message (unexpected): {message.Header.GetString(Tags.MsgType)}");
        }

        public void FromAdmin(Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[STP] ← Admin message: {msgType}");
        }

        public void FromApp(Message message, SessionID sessionID)
        {
            try
            {
                Crack(message, sessionID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP] ✗ Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Trade Capture Report (35=AE)
        /// </summary>
        public void OnMessage(QuickFix.FIX44.TradeCaptureReport message, SessionID sessionID)
        {
            try
            {
                Console.WriteLine($"\n[STP] ← Trade Capture Report received");

                // Convert QuickFix message to pipe-delimited string for our parser
                string fixString = ConvertToFixString(message);

                // Parse using our TradeCaptureParser
                var trade = TradeCaptureParser.ParseAE(fixString);

                Console.WriteLine($"[STP] Trade parsed: {trade.Symbol} {trade.Side} with {trade.CounterpartyName}");
                Console.WriteLine($"[STP]   ExecID: {trade.ExecID}");
                Console.WriteLine($"[STP]   QuoteID: {trade.QuoteID}");
                Console.WriteLine($"[STP]   ClOrdID: {trade.ClOrdID}");

                // Fire event to notify subscribers
                OnTradeCaptureReceived?.Invoke(trade);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STP] ✗ Error parsing Trade Capture Report: {ex.Message}");
                Console.WriteLine($"[STP] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Convert QuickFix message to pipe-delimited FIX string
        /// </summary>
        private string ConvertToFixString(Message message)
        {
            // Get all fields from the message
            var fields = new System.Collections.Generic.List<string>();

            // Add header fields
            var header = message.Header;
            for (int i = 1; i < 1000; i++)
            {
                try
                {
                    if (header.IsSetField(i))
                    {
                        var value = header.GetString(i);
                        fields.Add($"{i}={value}");
                    }
                }
                catch { }
            }

            // Add body fields
            for (int i = 1; i < 10000; i++)
            {
                try
                {
                    if (message.IsSetField(i))
                    {
                        var value = message.GetString(i);
                        fields.Add($"{i}={value}");
                    }
                }
                catch { }
            }

            // Add trailer fields
            var trailer = message.Trailer;
            for (int i = 1; i < 100; i++)
            {
                try
                {
                    if (trailer.IsSetField(i))
                    {
                        var value = trailer.GetString(i);
                        fields.Add($"{i}={value}");
                    }
                }
                catch { }
            }

            return string.Join("|", fields);
        }
    }
}
