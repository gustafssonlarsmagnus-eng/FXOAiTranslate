using QuickFix;
using QuickFix.Fields;
using QuickFix.Transport;
using QuickFix.FIX44;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QLNet;  // <-- QLNet for date policy/rules

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXSessionManager
    {
        private readonly SocketInitiator _initiator;
        private readonly SessionSettings _settings;
        private readonly GFIFIXApplication _application;
        private SessionID _sessionID;
        private RawFIXMessageBuilder _rawBuilder;
        private readonly Dictionary<string, int> _executionCounters = new Dictionary<string, int>();
        private readonly object _seqNumLock = new object(); // Lock for sequence number operations

        public GFIFIXApplication Application => _application;
        public bool IsLoggedOn => _application.IsLoggedOn;

        public GFIFIXSessionManager(string configFile = "quickfix.cfg", FenicsConfig fenicsConfig = null)
        {
            Console.WriteLine($"[FIX Manager] Initializing with config: {configFile}");
            Console.WriteLine($"[FIX Manager] Config file path: {System.IO.Path.GetFullPath(configFile)}");

            Console.WriteLine($"[FIX Manager] Config exists: {File.Exists(configFile)}");

            // Create FenicsConfig if not provided
            var config = fenicsConfig ?? new FenicsConfig();

            _application = new GFIFIXApplication(config);
            _rawBuilder = new RawFIXMessageBuilder("FIX.4.4", "WEBFENICS55", "GFI");

            try
            {
                Console.WriteLine("[FIX Manager] Loading SessionSettings...");
                _settings = new SessionSettings(configFile);
                Console.WriteLine("[FIX Manager] SessionSettings loaded successfully");

                Console.WriteLine("\n[FIX Manager] === Config Contents ===");
                var sessions = _settings.GetSessions();

                Console.WriteLine($"[FIX Manager] Number of sessions: {sessions.Count}");

                foreach (var sessionID in sessions)
                {
                    Console.WriteLine($"[FIX Manager] Session: {sessionID}");
                    var dict = _settings.Get(sessionID);

                    string[] commonKeys = {
                        "BeginString", "SenderCompID", "TargetCompID",
                        "SocketConnectHost", "SocketConnectPort", "HeartBtInt",
                        "ConnectionType", "OnBehalfOfCompID", "ResetOnLogon",
                        "EncryptMethod", "ReconnectInterval", "StartTime", "EndTime"
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
                Console.WriteLine("[FIX Manager] === End Config Contents ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR loading config: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }

            try
            {
                Console.WriteLine("[FIX Manager] Creating MemoryStoreFactory (no file storage)...");
                var storeFactory = new MemoryStoreFactory();
                Console.WriteLine("[FIX Manager] ✓ MemoryStoreFactory created");

                Console.WriteLine("[FIX Manager] Creating ScreenLogFactory (console logging)...");
                var logFactory = new ScreenLogFactory(_settings);
                Console.WriteLine("[FIX Manager] ✓ ScreenLogFactory created");

                Console.WriteLine("[FIX Manager] Creating SocketInitiator...");
                _initiator = new SocketInitiator(_application, storeFactory, _settings, logFactory);
                Console.WriteLine("[FIX Manager] ✓ SocketInitiator created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR creating initiator: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[FIX Manager] Inner exception: {ex.InnerException?.Message}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }

            Console.WriteLine("[FIX Manager] Initialized successfully");
        }

        #region Connection Management

        public void Start()
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("STARTING FIX SESSION");
            Console.WriteLine(new string('=', 60));

            try
            {
                var sessions = _settings.GetSessions();
                if (sessions.Count == 0)
                {
                    throw new Exception("No sessions found in config file!");
                }

                _sessionID = sessions.First();
                var dict = _settings.Get(_sessionID);

                Console.WriteLine("\n[FIX Config]:");
                Console.WriteLine($"  SenderCompID: {dict.GetString("SenderCompID")}");
                Console.WriteLine($"  TargetCompID: {dict.GetString("TargetCompID")}");
                Console.WriteLine($"  Host: {dict.GetString("SocketConnectHost")}");
                Console.WriteLine($"  Port: {dict.GetString("SocketConnectPort")}");

                Console.WriteLine($"\n[FIX Manager] About to call _initiator.Start()...");
                Console.WriteLine($"[FIX Manager] Initiator is null: {_initiator == null}");
                Console.WriteLine($"[FIX Manager] SessionID: {_sessionID}");

                try
                {
                    _initiator.Start();
                    Console.WriteLine("\n[FIX Manager] ✓ Initiator started successfully");
                }
                catch (Exception startEx)
                {
                    Console.WriteLine($"\n[FIX Manager] ✗ ERROR in _initiator.Start()");
                    Console.WriteLine($"[FIX Manager] Error message: {startEx.Message}");
                    Console.WriteLine($"[FIX Manager] Exception type: {startEx.GetType().Name}");

                    if (startEx.InnerException != null)
                    {
                        Console.WriteLine($"\n[FIX Manager] === INNER EXCEPTION ===");
                        Console.WriteLine($"[FIX Manager] Inner message: {startEx.InnerException.Message}");
                        Console.WriteLine($"[FIX Manager] Inner type: {startEx.InnerException.GetType().Name}");

                        if (startEx.InnerException.InnerException != null)
                        {
                            Console.WriteLine($"\n[FIX Manager] === INNER INNER EXCEPTION ===");
                            Console.WriteLine($"[FIX Manager] Inner inner message: {startEx.InnerException.InnerException.Message}");
                            Console.WriteLine($"[FIX Manager] Inner inner type: {startEx.InnerException.InnerException.GetType().Name}");
                        }
                    }

                    Console.WriteLine($"\n[FIX Manager] === FULL STACK TRACE ===");
                    Console.WriteLine(startEx.StackTrace);

                    throw;
                }

                Console.WriteLine("[FIX Manager] Waiting for logon...\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[FIX Manager] ✗ ERROR starting session: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            Console.WriteLine("\n[FIX Manager] Stopping session...");
            _initiator.Stop();
            Console.WriteLine("[FIX Manager] ✓ Stopped");
        }

        #endregion

        #region Send Quote Request

        public string SendQuoteRequest(TradeStructure trade, string lpName, string groupId)
        {
            if (!IsLoggedOn)
                throw new InvalidOperationException("Cannot send quote request - not logged on!");

            string quoteReqID = $"FENICS.31491.Q{DateTime.UtcNow.Ticks}";
            _application.RegisterQuoteRequest(quoteReqID, groupId);

            Console.WriteLine($"\n[FIX Manager] Building Quote Request for {lpName}");
            Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            Console.WriteLine($"  GroupID: {groupId}");
            Console.WriteLine($"  Underlying: {trade.Underlying}");
            Console.WriteLine($"  Legs: {trade.Legs.Count}");

            try
            {
                // ===== Canonical single-policy dates (identical for all LPs) =====
                var pair = trade.Underlying;            // e.g., "EURUSD"
                var premiumCcy = trade.PremiumCurrency; // e.g., "USD"
                var P = GlobalDatePolicy.Policy;

                var rules = new FxDateRules
                {
                    Ccy1 = pair.Substring(0, 3),
                    Ccy2 = pair.Substring(3, 3),
                    SpotLag = P.SpotLagForPair(pair),
                    ExpiryConvention = P.ExpiryConvention,
                    ExpiryEOM = P.ExpiryEOM,
                    PremiumSettleDays = P.PremiumSettleDays,
                    PremiumCalMode = P.PremiumCalendarMode,
                    PremiumConvention = P.PremiumConvention
                };

                var nowUtc = DateTime.UtcNow;

                // Tag 75 (TradeDate) must be TODAY's actual date (not business-day-adjusted)
                // GFI explicitly requires: "Trade date has to be current date"
                var canonical75 = nowUtc.ToString("yyyyMMdd");

                // Tag 5020 (PremiumDelivery) is T+1 business day from today
                var (_, _, _, _, premiumDt) =
                    FxDateService.ComputeDates(nowUtc, pair, "0D", premiumCcy, rules);
                var canonical5020 = FxDateService.Ymd(premiumDt);

                Console.WriteLine($"[Dates] Policy: premium={P.PremiumCalendarMode}, conv={P.PremiumConvention}, spotLag={rules.SpotLag}");
                Console.WriteLine($"[Dates] 75={canonical75} (TODAY) 5020={canonical5020} (T+{P.PremiumSettleDays})");

                // ===== Get current sequence number and increment for next message =====
                // Lock to prevent race condition where multiple threads could get same sequence number
                string rawMessage;
                int seqNum;
                bool sent;

                lock (_seqNumLock)
                {
                    var session = Session.LookupSession(_sessionID);
                    if (session == null)
                        throw new InvalidOperationException("FIX session not found. Ensure Start() has been called.");

                    seqNum = session.NextSenderMsgSeqNum;
                    _rawBuilder.SetMsgSeqNum(seqNum);

                    // ===== Build raw message with exact field order, passing canonical overrides =====
                    rawMessage = _rawBuilder.BuildQuoteRequest(
                        trade, lpName, quoteReqID, groupId,
                        tag75Override: canonical75,
                        tag5020Override: canonical5020);

                    Console.WriteLine($"\n[DEBUG] Raw Quote Request Message (SeqNum={seqNum}):");
                    Console.WriteLine($"{rawMessage.Replace("\x01", "|")}");
                    Console.WriteLine($"[DEBUG] End of message\n");

                    // ===== Send raw message =====
                    sent = session.Send(rawMessage);

                    if (sent)
                    {
                        // Manually increment the sequence number since we sent raw
                        session.NextSenderMsgSeqNum = seqNum + 1;
                        Console.WriteLine($"[FIX Manager] ✓ Quote Request sent to {lpName} (SeqNum={seqNum})");
                    }
                    else
                    {
                        Console.WriteLine($"[FIX Manager] ✗ Failed to send message");
                        throw new InvalidOperationException("Failed to send Quote Request message");
                    }
                }

                for (int i = 0; i < trade.Legs.Count; i++)
                {
                    var leg = trade.Legs[i];
                    Console.WriteLine($"    Leg {i + 1}: {leg.Direction} {leg.OptionType} @ {leg.Strike} ({leg.NotionalMM}M)");
                }

                return quoteReqID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR sending quote request: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        #endregion

        #region Send Execution (35=AB)

        public string SendExecution(FIXMessage quote, string side, TradeStructure trade = null)
        {
            if (!IsLoggedOn)
                throw new InvalidOperationException("Cannot execute - not logged on!");

            string quoteID = quote.Get(Tags.QuoteID.ToString());
            string quoteReqID = quote.Get(Tags.QuoteReqID.ToString());
            string lpName = quote.Get(Tags.OnBehalfOfCompID.ToString());

            // Use raw message builder for proper field ordering
            // QuickFix sorts fields by tag number, but GFI requires specific order
            if (trade == null || quote.LegPricing == null || quote.LegPricing.Count == 0)
            {
                Console.WriteLine($"  [WARNING] Trade is NULL or no leg pricing available!");
                throw new InvalidOperationException("Cannot execute - missing trade or leg pricing information");
            }

            try
            {
                string clOrdID;
                string rawMessage;
                int seqNum;
                bool sent;

                // Lock to prevent race condition in sequence number and execution counter
                lock (_seqNumLock)
                {
                    // Increment execution counter for this QuoteReqID and generate ClOrdID with suffix
                    if (!_executionCounters.ContainsKey(quoteReqID))
                        _executionCounters[quoteReqID] = 0;

                    _executionCounters[quoteReqID]++;
                    clOrdID = $"{quoteReqID}_{_executionCounters[quoteReqID]}";

                    Console.WriteLine($"\n[FIX Manager] Executing trade");
                    Console.WriteLine($"  ClOrdID: {clOrdID}");
                    Console.WriteLine($"  QuoteReqID: {quoteReqID}");
                    Console.WriteLine($"  QuoteID: {quoteID}");
                    Console.WriteLine($"  LP: {lpName}");
                    Console.WriteLine($"  Side: {side}");

                    Console.WriteLine($"  [DEBUG] Building execution with {quote.LegPricing.Count} legs");
                    int structureCode = TradeStructure.GetStructureCode(trade.StructureType);

                    // Get current sequence number and increment for next message
                    var session = Session.LookupSession(_sessionID);
                    if (session == null)
                        throw new InvalidOperationException("FIX session not found. Ensure Start() has been called.");

                    seqNum = session.NextSenderMsgSeqNum;
                    _rawBuilder.SetMsgSeqNum(seqNum);

                    Console.WriteLine($"  [DEBUG BEFORE BUILD] side parameter value: '{side}'");

                    // Build raw message with exact field order
                    rawMessage = _rawBuilder.BuildNewOrderMultileg(
                        clOrdID,
                        quoteID,
                        side,
                        trade.Underlying,
                        structureCode,
                        quote);

                    Console.WriteLine($"\n[DEBUG] Raw Execution Message (SeqNum={seqNum}):");
                    Console.WriteLine($"{rawMessage.Replace("\x01", "|")}");
                    Console.WriteLine($"[DEBUG] End of message\n");

                    // Send raw message
                    sent = session.Send(rawMessage);

                    if (sent)
                    {
                        // Manually increment the sequence number since we sent raw
                        session.NextSenderMsgSeqNum = seqNum + 1;
                        Console.WriteLine($"[FIX Manager] ✓ Execution sent (SeqNum={seqNum})");
                    }
                    else
                    {
                        Console.WriteLine($"[FIX Manager] ✗ Failed to send message");
                        throw new InvalidOperationException("Failed to send Execution message");
                    }
                }

                // Add to blotter
                if (trade != null)
                {
                    // Calculate net premium from quote's leg pricing
                    // GFI sends premiums already from client perspective:
                    // - BID quote (SELL): Premium is positive (client receives)
                    // - OFFER quote (BUY): Premium is negative (client pays)
                    double netPremium = 0;
                    double volatility = 0;
                    double delta = 0;
                    double spotRate = 0;
                    if (quote.LegPricing != null && quote.LegPricing.Count > 0)
                    {
                        foreach (var leg in quote.LegPricing)
                        {
                            if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double legPrem))
                            {
                                netPremium += legPrem;
                            }
                            // Extract volatility from first leg (for single leg trades)
                            if (volatility == 0 && !string.IsNullOrEmpty(leg.Volatility) && double.TryParse(leg.Volatility, out double vol))
                            {
                                volatility = vol;
                            }
                            // Extract delta from first leg (for single leg trades)
                            if (delta == 0 && !string.IsNullOrEmpty(leg.LegDelta) && double.TryParse(leg.LegDelta, out double dlt))
                            {
                                delta = dlt;
                            }
                            // Extract spot rate from first leg
                            if (spotRate == 0 && !string.IsNullOrEmpty(leg.LegSpotRate) && double.TryParse(leg.LegSpotRate, out double spot))
                            {
                                spotRate = spot;
                            }
                        }
                    }
                    // No negation needed - GFI premiums are already client perspective

                    var blotterEntry = new TradeBlotterEntry
                    {
                        TradeTime = DateTime.Now,
                        ClOrdID = clOrdID,
                        LP = lpName,
                        Side = side,
                        Underlying = trade.Underlying,
                        StructureType = trade.StructureType,
                        LegCount = trade.Legs.Count,
                        NetPremium = netPremium,
                        Volatility = volatility,
                        Delta = delta,
                        SpotRate = spotRate,
                        Status = "PENDING"
                    };
                    TradeBlotter.Instance.AddTrade(blotterEntry);
                }

                return clOrdID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR executing: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}
