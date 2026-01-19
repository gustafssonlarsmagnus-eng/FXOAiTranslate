using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using QuickFix;
using QuickFix.Fields;

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXApplication : MessageCracker, IApplication
    {
        private readonly ConcurrentDictionary<string, StreamInfo> _quotes;
        private ConcurrentDictionary<string, string> _quoteReqToGroupID = new ConcurrentDictionary<string, string>();
        public event Action<string, string, string> OnQuoteCanceled;

        public class StreamInfo
        {
            public string LP { get; set; }
            public string QuoteReqID { get; set; }
            public string GroupID { get; set; }
            public FIXMessage BidQuote { get; set; }
            public FIXMessage OfferQuote { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        public event Action<string> OnLogonEvent;
        public event Action<string> OnLogoutEvent;
        public event Action<string, FIXMessage> OnQuoteReceived;
        public event Action<QuoteData> OnQuoteDataReceived; // New event for WebView2 integration
        public event Action<string, string, string> OnExecutionReport; // ClOrdID, Status, ExecID
        public event Action<string, string, string> OnTradeCaptureReceived; // ClOrdID, CounterpartyName, LEI
        public event Action<string, int, string> OnQuoteRequestRejected; // QuoteReqID, RejectReason, RejectText

        public bool IsLoggedOn { get; private set; }

        public GFIFIXApplication()
        {
            _quotes = new ConcurrentDictionary<string, StreamInfo>();
            Console.WriteLine("[GFI FIX App] Initialized");
        }

        #region IApplication Implementation

        public void OnCreate(SessionID sessionID)
        {
            Console.WriteLine($"[GFI FIX] Session created: {sessionID}");
        }

        public void OnLogon(SessionID sessionID)
        {
            IsLoggedOn = true;
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"[{session}] ✓✓✓ LOGGED ON ✓✓✓");
            OnLogonEvent?.Invoke(sessionID.ToString());
        }

        public void OnLogout(SessionID sessionID)
        {
            IsLoggedOn = false;
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"[{session}] ✗ LOGGED OUT");
            OnLogoutEvent?.Invoke(sessionID.ToString());
        }

        public void ToAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);

            if (msgType == QuickFix.Fields.MsgType.LOGON)
            {
                // Determine credentials based on SenderCompID
                string senderCompID = sessionID.SenderCompID;
                string username, password, sessionType;

                if (senderCompID == "WEBFENICS55")
                {
                    // Trading credentials
                    username = "swed.obo.stg.api";
                    password = "v4fIzhR3KJCH";
                    sessionType = "Trading";

                    // Trading session includes OnBehalfOfCompID in HEADER (not body)
                    message.Header.SetField(new OnBehalfOfCompID("SWES"));
                }
                else if (senderCompID == "GFI_BFXO_SWED_TC1")
                {
                    // STP credentials
                    username = "gfi_bfxo_swed_tc1";
                    password = "ylhU6Q1eaxXf";
                    sessionType = "STP";
                    // STP session does NOT include OnBehalfOfCompID
                }
                else
                {
                    Console.WriteLine($"[GFI FIX] WARNING: Unknown SenderCompID: {senderCompID}");
                    username = "swed.obo.stg.api";
                    password = "v4fIzhR3KJCH";
                    sessionType = "Unknown";
                }

                message.SetField(new Username(username));
                message.SetField(new Password(password));

                Console.WriteLine($"[{sessionType}] >>> Sending Logon: {sessionID}");
                Console.WriteLine($"[{sessionType}] Username: {username}");

                Console.WriteLine($"[DEBUG] Full Logon Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End Logon Message");
            }
        }

        public void FromAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"[{session}] <<< Admin: {msgType}");

            if (msgType == "3")
            {
                ParseRejectMessage(message);
            }
        }

        public void ToApp(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"[{session}] >>> Sending: {msgType}");

            if (msgType == "R")
            {
                Console.WriteLine($"\n[DEBUG] Full Quote Request Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End of message\n");
            }
        }

        public void FromApp(QuickFix.Message message, SessionID sessionID)
        {
            string msgType = message.Header.GetString(35);
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"[{session}] <<< App: {msgType}");

            try
            {
                Crack(message, sessionID);
            }
            catch (UnsupportedMessageType)
            {
                Console.WriteLine($"[GFI FIX] ⚠️  Unsupported message type: {msgType}");
                Console.WriteLine($"[GFI FIX] Message: {message.ToString().Replace("\x01", "|")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ⚠️  Error processing message {msgType}: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        #endregion

        #region Message Handlers

        public void OnMessage(QuickFix.FIX44.Quote quote, SessionID sessionID)
        {
            try
            {
                string lpName = quote.Header.GetString(Tags.OnBehalfOfCompID);
                string quoteReqID = quote.GetString(Tags.QuoteReqID);
                string quoteID = quote.GetString(Tags.QuoteID);
                string sideStr = quote.GetString(Tags.Side);
                string side = sideStr == "1" ? "BID" : "OFFER";

                // ====== ENHANCED DEBUG LOGGING ======
                string timestamp = quote.Header.GetString(Tags.SendingTime);
                
   // Extract volatility for debugging
    string vol = "N/A";
      try
{
if (quote.IsSetField(5678)) // Volatility tag
              {
      vol = quote.GetString(5678);
           }
      }
  catch { }

        // Color-coded console output
   Console.ForegroundColor = side == "BID" ? ConsoleColor.Green : ConsoleColor.Cyan;
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════");
     Console.WriteLine($"║ QUOTE RECEIVED from {lpName}");
                Console.WriteLine($"║ Side: {side,-6} | QuoteID: {quoteID}");
        Console.WriteLine($"║ Vol: {vol,-8} | Time: {timestamp}");
                Console.WriteLine($"╚══════════════════════════════════════════════════════════════");
       Console.ResetColor();

       var fixMsg = ConvertQuoteToFIXMessage(quote);

        string key = $"{quoteReqID}_{lpName}";
      string groupID = GetGroupIDForQuoteReqID(quoteReqID);

        _quotes.AddOrUpdate(key,
         new StreamInfo
          {
        LP = lpName,
    QuoteReqID = quoteReqID,
                GroupID = groupID,
    BidQuote = side == "BID" ? fixMsg : null,
 OfferQuote = side == "OFFER" ? fixMsg : null,
     LastUpdate = DateTime.UtcNow
     },
     (k, existing) =>
  {
   if (side == "BID")
         existing.BidQuote = fixMsg;
            else
            existing.OfferQuote = fixMsg;
     existing.LastUpdate = DateTime.UtcNow;
        return existing;
     });

     OnQuoteReceived?.Invoke(quoteReqID, fixMsg);

                // Fire new QuoteData event for WebView2 integration
                try
                {
                    // Helper to safely parse double from FIX field
                    double ParseFieldDouble(int tag)
                    {
                        if (quote.IsSetField(tag))
                        {
                            string value = quote.GetString(tag);
                            if (double.TryParse(value, out double result))
                                return result;
                        }
                        return 0;
                    }

                    string GetFieldString(int tag)
                    {
                        return quote.IsSetField(tag) ? quote.GetString(tag) : "";
                    }

                    var quoteData = new QuoteData
                    {
                        LP = lpName,
                        Side = side,
                        QuoteID = quoteID,
                        Timestamp = DateTime.Now,
                        Underlying = quote.IsSetField(Tags.Symbol) ? quote.GetString(Tags.Symbol) : "",
                        BidPremium = side == "BID" ? ParseFieldDouble(6436) : 0,
                        OfferPremium = side == "OFFER" ? ParseFieldDouble(6436) : 0,
                        BidVol = side == "BID" ? ParseFieldDouble(5678) : 0,
                        OfferVol = side == "OFFER" ? ParseFieldDouble(5678) : 0,
                        BidLegPremPrice = side == "BID" ? ParseFieldDouble(5844) : 0,  // Tag 5844: Premium per MM ("pips")
                        OfferLegPremPrice = side == "OFFER" ? ParseFieldDouble(5844) : 0,  // Tag 5844: Premium per MM ("pips")
                        Notional = ParseFieldDouble(5359),  // MQSize
                        Delta = ParseFieldDouble(6035),      // LegDelta
                        SpotRate = GetFieldString(5235),  // Tag 5235: LegSpotRate
                        ForwardPoints = ParseFieldDouble(5191),  // Tag 5191: LegForwardPoints
                        PremiumCurrency = GetFieldString(5830),  // PremiumCcy
                        ValidUntilTime = GetFieldString(62),     // ValidUntilTime
                        Spread = 0  // Will be calculated client-side
                    };

                    OnQuoteDataReceived?.Invoke(quoteData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GFI FIX] Failed to fire OnQuoteDataReceived: {ex.Message}");
                }

                // ====== ACTIVE LP SUMMARY ======
      PrintActiveLPSummary(groupID);
            }
     catch (Exception ex)
            {
            Console.ForegroundColor = ConsoleColor.Red;
   Console.WriteLine($"[GFI FIX] ERROR: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
     Console.ResetColor();
            }
        }

        /// <summary>
        /// Print a summary of which LPs are actively streaming
      /// </summary>
        private void PrintActiveLPSummary(string groupID)
        {
 var lpStreams = _quotes.Values
     .Where(s => s.GroupID == groupID)
      .GroupBy(s => s.LP)
   .Select(g => new
{
             LP = g.Key,
      HasBid = g.Any(s => s.BidQuote != null),
        HasOffer = g.Any(s => s.OfferQuote != null),
       LastUpdate = g.Max(s => s.LastUpdate)
    })
        .OrderBy(x => x.LP)
    .ToList();

    Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"\n┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine($"│ ACTIVE LP SUMMARY (Group: {groupID})");
    Console.WriteLine($"├─────────────────────────────────────────────────────────┤");
            
 if (lpStreams.Count == 0)
            {
   Console.ForegroundColor = ConsoleColor.Red;
   Console.WriteLine($"│ ⚠️  NO ACTIVE LPs STREAMING            │");
   }
     else
  {
        foreach (var lp in lpStreams)
      {
   string bidStatus = lp.HasBid ? "✓" : "✗";
     string offerStatus = lp.HasOffer ? "✓" : "✗";
         string ageSeconds = $"{(DateTime.UtcNow - lp.LastUpdate).TotalSeconds:F1}s ago";
    
  Console.ForegroundColor = (lp.HasBid && lp.HasOffer) ? ConsoleColor.Green : ConsoleColor.Yellow;
               Console.WriteLine($"│ {lp.LP,-20} | Bid:{bidStatus} Offer:{offerStatus} | {ageSeconds,-12} │");
   }
     
           Console.ForegroundColor = ConsoleColor.Yellow;
 Console.WriteLine($"│ Total LPs: {lpStreams.Count}         │");
  }
   
      Console.WriteLine($"└─────────────────────────────────────────────────────────┘\n");
    Console.ResetColor();
        }
        
        public void OnMessage(QuickFix.FIX44.QuoteCancel message, SessionID sessionID)
        {
            try
            {
                // FIXED: Get LP from HEADER, not body!
                string lpName = "UNKNOWN";
                try
                {
                    lpName = message.Header.GetString(Tags.OnBehalfOfCompID);
                }
                catch
                {
                    // Fallback if not in header
                    if (message.IsSetField(Tags.OnBehalfOfCompID))
                    {
                        lpName = message.GetString(Tags.OnBehalfOfCompID);
                    }
                }

                // Extract QuoteReqID
                string quoteReqID = message.IsSetQuoteReqID()
                    ? message.QuoteReqID.getValue()
                    : "N/A";

                // Extract QuoteID (tag 117)
                string quoteID = "N/A";
                if (message.IsSetField(Tags.QuoteID))
                {
                    quoteID = message.GetString(Tags.QuoteID);
                }

                // Extract cancel type
                int cancelType = message.IsSetQuoteCancelType()
                    ? message.QuoteCancelType.getValue()
                    : 0;

                // Extract the actual QuoteID being cancelled from tag 9262
                string cancelledQuoteID = "N/A";
                if (message.IsSetField(9262))
                {
                    cancelledQuoteID = message.GetString(9262);
                }

                // Compact trace for quote cancellations
                string timestamp = message.Header.GetString(Tags.SendingTime);
                Console.WriteLine($"[TRACE] {timestamp} | QUOTE_CXL | {cancelledQuoteID}");

                // Update your quotes dictionary to mark as canceled
                string key = $"{quoteReqID}_{lpName}";

                if (_quotes.TryGetValue(key, out var existingStream))
                {
                    // Determine which side is being canceled based on QuoteID pattern
                    if (quoteID.Contains("_b") || quoteID.StartsWith("B_"))
                    {
                        Console.WriteLine($"  → Canceling BID quote (replacement expected)");
                        existingStream.BidQuote = null; // Clear stale bid
                    }
                    else if (quoteID.Contains("_s") || quoteID.Contains("_o") || quoteID.Contains("-O") || quoteID.Contains("-T") || quoteID.StartsWith("O_"))
                    {
                        Console.WriteLine($"  → Canceling OFFER quote (replacement expected)");
                        existingStream.OfferQuote = null; // Clear stale offer
                    }
                    else
                    {
                        Console.WriteLine($"  → Quote canceled (awaiting replacement)");
                    }

                    existingStream.LastUpdate = DateTime.UtcNow;
                }
                else
                {
                    Console.WriteLine($"  ⚠ Warning: No existing quote found for key '{key}'");
                }

                // Notify UI that quote was canceled
                OnQuoteCanceled?.Invoke(quoteReqID, lpName, quoteID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR processing Quote Cancel: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        public void OnMessage(QuickFix.FIX44.QuoteStatusReport report, SessionID sessionID)
        {
            string quoteReqID = report.GetString(Tags.QuoteReqID);
            int quoteStatus = report.IsSetField(Tags.QuoteStatus) ? report.GetInt(Tags.QuoteStatus) : -1;

            Console.WriteLine($"\n[GFI FIX] <<< Quote Status Report (35=AI)");
            Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            Console.WriteLine($"  QuoteStatus: {quoteStatus} ({GetQuoteStatusText(quoteStatus)})");

            if (report.IsSetField(58))
            {
                string text = report.GetString(58);
                Console.WriteLine($"  Text: {text}");
            }
        }

        public void OnMessage(QuickFix.FIX44.QuoteRequestReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< QUOTE REQUEST REJECT (35=AG)");
            Console.WriteLine(new string('=', 60));

            string quoteReqID = "";
            int rejectReason = 99; // Default: Other
            string rejectText = "";

            if (reject.IsSetField(131))
            {
                quoteReqID = reject.GetString(131);
                Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            }

            if (reject.IsSetField(658))
            {
                rejectReason = reject.GetInt(658);
                Console.WriteLine($"  RejectReason: {rejectReason} ({GetQuoteRequestRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58))
            {
                rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));

            // Fire event to notify UI
            OnQuoteRequestRejected?.Invoke(quoteReqID, rejectReason, rejectText);
        }

        public void OnMessage(QuickFix.FIX44.Reject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< SESSION REJECT (35=3)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetRefSeqNum())
            {
                int refSeqNum = reject.RefSeqNum.getValue();
                Console.WriteLine($"  RefSeqNum: {refSeqNum}");
            }

            if (reject.IsSetRefTagID())
            {
                int refTagID = reject.RefTagID.getValue();
                Console.WriteLine($"  RefTagID: {refTagID} (Field that caused rejection)");
            }

            if (reject.IsSetRefMsgType())
            {
                string refMsgType = reject.RefMsgType.getValue();
                Console.WriteLine($"  RefMsgType: {refMsgType}");
            }

            if (reject.IsSetSessionRejectReason())
            {
                int rejectReason = reject.SessionRejectReason.getValue();
                Console.WriteLine($"  SessionRejectReason: {rejectReason} ({GetSessionRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetText())
            {
                string rejectText = reject.Text.getValue();
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        public void OnMessage(QuickFix.FIX44.ExecutionReport execReport, SessionID sessionID)
        {
            string clOrdID = execReport.GetString(Tags.ClOrdID);
            string ordStatus = execReport.GetString(Tags.OrdStatus);
            string execID = execReport.IsSetField(Tags.ExecID) ? execReport.GetString(Tags.ExecID) : "N/A";

            // Extract premium delivery date (tag 5020)
            string premiumDate = null;
            if (execReport.IsSetField(5020))
            {
                premiumDate = execReport.GetString(5020);
                Console.WriteLine($"[DEBUG] ✓ Found tag 5020 (Premium Date): {premiumDate}");
            }
            else
            {
                Console.WriteLine($"[DEBUG] ✗ Tag 5020 (Premium Date) NOT present in execution report");
            }

            string statusText = "PENDING";
            string rejectReason = null;

            if (ordStatus == "2")
            {
                statusText = "FILLED";
            }
            else if (ordStatus == "8")
            {
                statusText = "REJECTED";

                if (execReport.IsSetField(58))
                {
                    rejectReason = execReport.GetString(58);
                }
            }

            // Compact trace for execution results
            string timestamp = execReport.Header.GetString(Tags.SendingTime);
            string reason = rejectReason != null ? $" | {rejectReason}" : "";
            Console.WriteLine($"[TRACE] {timestamp} | EXEC_RPT  | {statusText} {clOrdID}{reason}");

            // Update blotter (including premium date)
            TradeBlotter.Instance.UpdateTradeStatus(clOrdID, statusText, execID, null, rejectReason, premiumDate);

            // Notify listeners
            OnExecutionReport?.Invoke(clOrdID, statusText, execID);
        }

        public void OnMessage(QuickFix.FIX44.TradeCaptureReport report, SessionID sessionID)
        {
            string session = GetSessionLabel(sessionID);
            Console.WriteLine($"\n[{session}] <<< TRADE CAPTURE REPORT (35=AE)");
            Console.WriteLine(new string('=', 60));

            try
            {
                // Extract ClOrdID (tag 11)
                string clOrdID = report.IsSetField(Tags.ClOrdID) ? report.GetString(Tags.ClOrdID) : "N/A";
                Console.WriteLine($"  ClOrdID: {clOrdID}");

                // Extract ExecID (tag 17)
                string execID = report.IsSetField(Tags.ExecID) ? report.GetString(Tags.ExecID) : "N/A";
                Console.WriteLine($"  ExecID: {execID}");

                // Extract TradeReportID (tag 571)
                string tradeReportID = report.IsSetField(571) ? report.GetString(571) : "N/A";
                Console.WriteLine($"  TradeReportID: {tradeReportID}");

                // Extract LEI from tag 1 (Account field - GFI puts LEI here)
                string lei = "UNKNOWN";
                string counterpartyName = "Unknown Counterparty";

                if (report.IsSetField(1))
                {
                    lei = report.GetString(1);
                    Console.WriteLine($"  LEI (tag 1): {lei}");

                    // Map LEI to bank name
                    counterpartyName = MapLEIToBankName(lei);
                    Console.WriteLine($"  Counterparty: {counterpartyName}");
                }

                // Extract Symbol (tag 55)
                if (report.IsSetField(Tags.Symbol))
                {
                    string symbol = report.GetString(Tags.Symbol);
                    Console.WriteLine($"  Symbol: {symbol}");
                }

                // Extract Premium Date (tag 5020)
                string premiumDate = null;
                if (report.IsSetField(5020))
                {
                    premiumDate = report.GetString(5020);
                    Console.WriteLine($"  Premium Date (5020): {premiumDate}");
                }
                else
                {
                    Console.WriteLine($"  Premium Date (5020): NOT PRESENT");
                }

                // Compact trace for STP confirmation
                string timestamp = report.Header.GetString(Tags.SendingTime);
                Console.WriteLine($"[TRACE] {timestamp} | STP_CONF  | {clOrdID} | {counterpartyName}");

                Console.WriteLine(new string('=', 60));

                // Update blotter with STP confirmation and premium date
                TradeBlotter.Instance.UpdateTradeWithSTPConfirmation(clOrdID, counterpartyName, premiumDate);

                // Fire event to notify UI
                OnTradeCaptureReceived?.Invoke(clOrdID, counterpartyName, lei);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR processing Trade Capture Report: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        public void OnMessage(QuickFix.FIX44.BusinessMessageReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< BUSINESS MESSAGE REJECT (35=j)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetField(372))
            {
                string refMsgType = reject.GetString(372);
                Console.WriteLine($"  RefMsgType: {refMsgType}");
            }

            if (reject.IsSetField(380))
            {
                int rejectReason = reject.GetInt(380);
                Console.WriteLine($"  BusinessRejectReason: {rejectReason} ({GetBusinessRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58))
            {
                string rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        public void OnMessage(QuickFix.Message message, SessionID sessionID)
        {
            try
            {
                var msgType = message.Header.GetField(Tags.MsgType);

                if (msgType == "SD")
                {
                    Console.WriteLine($"\n[GFI FIX] <<< StaticData (35=SD) - LP Information");
                    Console.WriteLine(new string('-', 60));

                    if (message.IsSetField(1663))
                    {
                        int numElements = message.GetInt(1663);
                        Console.WriteLine($"  Available Liquidity Providers: {numElements}\n");

                        for (int i = 1; i <= numElements; i++)
                        {
                            try
                            {
                                var group = message.GetGroup(i, 1663);

                                string lpCompID = group.IsSetField(Tags.OnBehalfOfCompID)
                                    ? group.GetString(Tags.OnBehalfOfCompID)
                                    : "N/A";

                                string displayName = group.IsSetField(1402)
                                    ? group.GetString(1402)
                                    : "N/A";

                                string priceRequest = group.IsSetField(9996)
                                    ? group.GetString(9996)
                                    : "N/A";

                                Console.WriteLine($"  [{i,2}] {lpCompID,-15} | {displayName}");
                                Console.WriteLine($"      PriceRequest: {priceRequest}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  [{i,2}] Error parsing LP data: {ex.Message}");
                            }
                        }

                        Console.WriteLine(new string('-', 60));
                        Console.WriteLine("  ✓ StaticData received successfully");
                    }
                }
                else
                {
                    Console.WriteLine($"[GFI FIX] <<< Custom message type: {msgType}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR handling custom message: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        public void RegisterQuoteRequest(string quoteReqID, string groupID)
        {
            _quoteReqToGroupID[quoteReqID] = groupID;
        }

        private string GetGroupIDForQuoteReqID(string quoteReqID)
        {
            return _quoteReqToGroupID.TryGetValue(quoteReqID, out string groupID) ? groupID : "unknown";
        }

        private void ParseRejectMessage(QuickFix.Message message)
        {
            try
            {
                Console.WriteLine($"\n[GFI FIX] === PARSING REJECT MESSAGE ===");

                if (message.IsSetField(45))
                {
                    int refSeqNum = message.GetInt(45);
                    Console.WriteLine($"  RefSeqNum: {refSeqNum}");
                }

                if (message.IsSetField(371))
                {
                    int refTagID = message.GetInt(371);
                    Console.WriteLine($"  RefTagID: {refTagID}");
                }

                if (message.IsSetField(372))
                {
                    string refMsgType = message.GetString(372);
                    Console.WriteLine($"  RefMsgType: {refMsgType}");
                }

                if (message.IsSetField(373))
                {
                    int reason = message.GetInt(373);
                    Console.WriteLine($"  SessionRejectReason: {reason} ({GetSessionRejectReasonText(reason)})");
                }

                if (message.IsSetField(58))
                {
                    string text = message.GetString(58);
                    Console.WriteLine($"  ⚠️  Text: {text}");
                }

                Console.WriteLine($"=================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error parsing reject: {ex.Message}");
            }
        }

        private FIXMessage ConvertQuoteToFIXMessage(QuickFix.FIX44.Quote quote)
        {
            var msg = new FIXMessage(MsgTypes.Quote);
            msg.Set(Tags.OnBehalfOfCompID.ToString(), quote.Header.GetString(Tags.OnBehalfOfCompID));
            msg.Set(Tags.QuoteReqID.ToString(), quote.GetString(Tags.QuoteReqID));
            msg.Set(Tags.QuoteID.ToString(), quote.GetString(Tags.QuoteID));
            msg.Set(Tags.Side.ToString(), quote.GetString(Tags.Side));
            msg.Set(Tags.Symbol.ToString(), quote.GetString(Tags.Symbol));

            // Extract ValidUntilTime (tag 62) for countdown timer
            if (quote.IsSetField(62))
            {
                string validUntilTime = quote.GetString(62);
                msg.Set("62", validUntilTime);
                Console.WriteLine($"  [DEBUG] ValidUntilTime (tag 62): {validUntilTime}");
            }

            // Extract Premium (tag 6436) - GFI sends this as integer
            if (quote.IsSetField(6436))
            {
                string premium = quote.GetString(6436);
                msg.Set("6436", premium);
                Console.WriteLine($"  [DEBUG] Premium (tag 6436): {premium}");
            }

            // Extract PremiumCcy (tag 5830)
            if (quote.IsSetField(5830))
            {
                string premiumCcy = quote.GetString(5830);
                msg.Set("5830", premiumCcy);
                Console.WriteLine($"  [DEBUG] PremiumCcy (tag 5830): {premiumCcy}");
            }

            // Extract PremiumCcy from tag 9073 (DepoRateCcy, often used for premium currency in responses)
            if (quote.IsSetField(9073))
            {
                string premiumCcy9073 = quote.GetString(9073);
                msg.Set("9073", premiumCcy9073);
                Console.WriteLine($"  [DEBUG] DepoRateCcy/PremiumCcy (tag 9073): {premiumCcy9073}");
            }

            // For debugging - print the full raw message
            Console.WriteLine($"  [DEBUG] Raw quote message:");
            Console.WriteLine($"  {quote.ToString().Replace("\x01", "|")}");

            // Parse leg pricing - GFI may put these fields directly on the message body
            // or in NoMQEntries (6120) repeating groups
            Console.WriteLine($"  [DEBUG] Checking for leg pricing fields...");

            // Check if we have NoMQEntries count
            int noMQEntries = 1; // Default to 1 leg
            if (quote.IsSetField(6120))
            {
                noMQEntries = quote.GetInt(6120);
                Console.WriteLine($"  [DEBUG] NoMQEntries (6120) = {noMQEntries}");
            }

            // For now, try to extract fields from the message body directly
            // This works for vanilla options and we can expand for multi-leg later
            var legPricing = new LegPricingInfo();

            // Try to get leg pricing fields from message body
            if (quote.IsSetField(7940)) // LegStrategyID
            {
                legPricing.LegStrategyID = quote.GetString(7940);
                Console.WriteLine($"  [DEBUG] LegStrategyID (7940): {legPricing.LegStrategyID}");
            }
            else
            {
                legPricing.LegStrategyID = "SL0"; // Default
            }

            if (quote.IsSetField(5678)) // Volatility
            {
                legPricing.Volatility = quote.GetString(5678);
                Console.WriteLine($"  [DEBUG] Volatility (5678): {legPricing.Volatility}");
            }

            if (quote.IsSetField(5359)) // MQSize
            {
                legPricing.MQSize = quote.GetString(5359);
                Console.WriteLine($"  [DEBUG] MQSize (5359): {legPricing.MQSize}");
            }
            else
            {
                legPricing.MQSize = "1"; // Default
            }

            if (quote.IsSetField(5844)) // LegPremPrice
            {
                legPricing.LegPremPrice = quote.GetString(5844);
                Console.WriteLine($"  [DEBUG] LegPremPrice (5844): {legPricing.LegPremPrice}");
            }

            if (quote.IsSetField(5235)) // LegSpotRate
            {
                legPricing.LegSpotRate = quote.GetString(5235);
                Console.WriteLine($"  [DEBUG] LegSpotRate (5235): {legPricing.LegSpotRate}");
            }

            if (quote.IsSetField(6035)) // LegDelta
            {
                legPricing.LegDelta = quote.GetString(6035);
                Console.WriteLine($"  [DEBUG] LegDelta (6035): {legPricing.LegDelta}");
            }

            // LegSymbol (600)
            if (quote.IsSetField(600))
            {
                legPricing.LegSymbol = quote.GetString(600);
            }
            else
            {
                legPricing.LegSymbol = quote.GetString(Tags.Symbol); // Default to main symbol
            }

            // Add the leg pricing if we found any data
            if (!string.IsNullOrEmpty(legPricing.Volatility) || !string.IsNullOrEmpty(legPricing.LegPremPrice))
            {
                msg.LegPricing.Add(legPricing);
                Console.WriteLine($"  [DEBUG] Added leg pricing: StrategyID={legPricing.LegStrategyID}, Vol={legPricing.Volatility}, Size={legPricing.MQSize}, Premium={legPricing.LegPremPrice}");
            }
            else
            {
                Console.WriteLine($"  [WARNING] No leg pricing fields found in quote!");
            }

            return msg;
        }

        public List<StreamInfo> GetActiveStreams(string groupId)
        {
            var result = new List<StreamInfo>();

            foreach (var stream in _quotes.Values)
            {
                if (stream.GroupID == groupId)
                {
                    result.Add(stream);
                }
            }

            return result;
        }

        private string GetQuoteStatusText(int status)
        {
            return status switch
            {
                0 => "Accepted",
                1 => "Canceled for Symbol",
                2 => "Canceled for Security Type",
                3 => "Canceled for Underlying",
                4 => "Canceled All",
                5 => "Rejected",
                6 => "Removed from Market",
                7 => "Expired",
                8 => "Query",
                9 => "Quote Not Found",
                10 => "Pending",
                11 => "Pass",
                _ => $"Unknown ({status})"
            };
        }

        private string GetQuoteRequestRejectReasonText(int reason)
        {
            return reason switch
            {
                1 => "Unknown Symbol",
                2 => "Exchange Closed",
                3 => "Quote Request Exceeds Limit",
                4 => "Too Late to Enter",
                5 => "Unknown Quote",
                6 => "Duplicate Quote",
                7 => "Invalid Bid/Ask Spread",
                8 => "Invalid Price",
                9 => "Not Authorized to Quote",
                99 => "Other",
                _ => $"Unknown ({reason})"
            };
        }

        private string GetSessionRejectReasonText(int reason)
        {
            return reason switch
            {
                0 => "Invalid tag number",
                1 => "Required tag missing",
                2 => "Tag not defined for this message type",
                3 => "Undefined Tag",
                4 => "Tag specified without a value",
                5 => "Value is incorrect (out of range) for this tag",
                6 => "Incorrect data format for value",
                7 => "Decryption problem",
                8 => "Signature problem",
                9 => "CompID problem",
                10 => "SendingTime accuracy problem",
                11 => "Invalid MsgType",
                12 => "XML Validation error",
                13 => "Tag appears more than once",
                14 => "Tag specified out of required order",
                15 => "Repeating group fields out of order",
                16 => "Incorrect NumInGroup count for repeating group",
                17 => "Non 'data' value includes field delimiter",
                99 => "Other",
                _ => $"Unknown ({reason})"
            };
        }

        private string GetBusinessRejectReasonText(int reason)
        {
            return reason switch
            {
                0 => "Other",
                1 => "Unknown ID",
                2 => "Unknown Security",
                3 => "Unsupported Message Type",
                4 => "Application not available",
                5 => "Conditionally Required Field Missing",
                6 => "Not Authorized",
                7 => "DeliverTo firm not available at this time",
                _ => $"Unknown ({reason})"
            };
        }

        private string MapLEIToBankName(string lei)
        {
            // LEI mappings from GFI STP 35=AE tag 1 (4th pipe-separated value)
            var leiMapping = new Dictionary<string, string>
            {
                { "G5GSEF7VJP5I7OUK5573", "Barclays Bank PLC London" },
                { "R0MUWSFPU8MPRO8K5P83", "BNP Paribas" },
                { "2IGI19DL77OX0HC3ZE78", "Canadian Imperial Bank of Commerce" },
                { "ATUEL7OJR5057F2PV266", "DBS" },
                { "7LTWFZYICNSX8D621K86", "Deutsche Bank AG" },
                { "W22LROWP2IHZNBB6K528", "Goldman Sachs International" },
                { "MP6I5ZYZBEU3UXPYFY54", "HSBC Bank PLC London" },
                { "GGDZP1UYGU9STUHRDP48", "Merrill Lynch International" },
                { "4PQUHN3JPFGFNF3BB653", "Morgan Stanley International London" },
                { "RR3QWICWWIPCS8A4S074", "Natwest Markets London" },
                { "DGQCSV2PHVF7I2743539", "Nomura International PLC London" },
                { "894500A9DTYKF33B7Z76", "Optiver FX Limited" },
                { "O2RNE8IBXP4R0TD8PU41", "Societe Generale" },
                { "RILFO74KP1CM8P6PCT96", "Standard Chartered Bank London" },
                { "BFM8T61CT2L1QCEMIK50", "UBS AG Zurich" },
                { "KB1H1DSPRFMYMCUFXT09", "Wells Fargo Bank N.A." },
                { "165GRDQ39W63PHVONY02", "Zurcher Kantonalbank" }
            };

            // Extract first LEI if multiple are present (some systems send pipe-separated list)
            string primaryLEI = lei.Contains("|") ? lei.Split('|')[0].Trim() : lei.Trim();

            if (leiMapping.TryGetValue(primaryLEI, out string bankName))
            {
                return bankName;
            }

            // Return LEI if not found in mapping
            return $"Unknown Bank ({primaryLEI})";
        }

        private string GetSessionLabel(SessionID sessionID)
        {
            string senderCompID = sessionID.SenderCompID;
            return senderCompID switch
            {
                "WEBFENICS55" => "Trading",
                "GFI_BFXO_SWED_TC1" => "STP",
                _ => senderCompID
            };
        }

        #endregion
    }
}