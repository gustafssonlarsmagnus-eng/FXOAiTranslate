using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Main FIX simulator orchestrating RFS workflow
    /// </summary>
    public class FIXSimulator
    {
        private Dictionary<string, SimulatedLiquidityProvider> _liquidityProviders;
        private Dictionary<string, List<StreamInfo>> _activeRequests;
        private int _seqNum;
        private Dictionary<string, int> _executionCounters = new Dictionary<string, int>();

        public class StreamInfo
        {
            public string LP { get; set; }
            public string QuoteReqID { get; set; }
            public FIXMessage BidQuote { get; set; }
            public FIXMessage OfferQuote { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        public FIXSimulator()
        {
            _liquidityProviders = new Dictionary<string, SimulatedLiquidityProvider>
            {
                ["MS"] = new SimulatedLiquidityProvider("MS", baseVol: 7.8, spread: 0.3),
                ["UBS"] = new SimulatedLiquidityProvider("UBS", baseVol: 8.1, spread: 0.4),
                ["NATWEST"] = new SimulatedLiquidityProvider("NATWEST", baseVol: 7.9, spread: 0.35),
                ["GOLDMAN"] = new SimulatedLiquidityProvider("GOLDMAN", baseVol: 7.7, spread: 0.32),
                ["BARCLAYS"] = new SimulatedLiquidityProvider("BARCLAYS", baseVol: 8.0, spread: 0.38),
                ["HSBC"] = new SimulatedLiquidityProvider("HSBC", baseVol: 7.9, spread: 0.36),
                ["BNP"] = new SimulatedLiquidityProvider("BNP", baseVol: 8.2, spread: 0.42),
                ["CIBC"] = new SimulatedLiquidityProvider("CIBC", baseVol: 8.3, spread: 0.45),
                ["DEUT"] = new SimulatedLiquidityProvider("DEUT", baseVol: 7.8, spread: 0.33),
                ["DBS"] = new SimulatedLiquidityProvider("DBS", baseVol: 8.1, spread: 0.40)
            };
            _activeRequests = new Dictionary<string, List<StreamInfo>>();
            _seqNum = 1;
        }

        /// <summary>
        /// Send quote request to multiple LPs
        /// </summary>
        public (string groupId, List<(string lp, string quoteReqId)> requests) SendQuoteRequest(
            string underlying,
            List<string> lps,
            string groupId = null)
        {
            if (groupId == null)
            {
                groupId = $"{lps.Count}-REQ{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            }

            var requests = new List<(string lp, string quoteReqId)>();

            Console.WriteLine($"\n=== SENDING QUOTE REQUESTS ===");
            Console.WriteLine($"Group: {groupId}");
            Console.WriteLine($"Underlying: {underlying}");
            Console.WriteLine($"LPs: {string.Join(", ", lps)}\n");

            foreach (var lp in lps)
            {
                if (!_liquidityProviders.ContainsKey(lp))
                {
                    Console.WriteLine($"[WARNING] Unknown LP: {lp}");
                    continue;
                }

                // Build Quote Request (35=R)
                var quoteReq = new FIXMessage(MsgTypes.QuoteRequest)
                    .Set(TagStrings.SenderCompID, "<CLIENT>")
                    .Set(TagStrings.TargetCompID, "GFI")
                    .Set(TagStrings.OnBehalfOfCompID, "<CLIENT>")
                    .Set(TagStrings.DeliverToCompID, lp)
                    .Set(TagStrings.NoBanksReqFenics, groupId)
                    .Set(TagStrings.QuoteReqID, $"FENICS.5015500.Q{_seqNum}")
                    .Set("75", DateTime.UtcNow.ToString("yyyyMMdd")) // TradeDate
                    .Set(TagStrings.Symbol, underlying)
                    .Set(TagStrings.Structure, Values.Structure.CallSpread)
                    .Set("5475", "S") // PremDel = Spot
                    .Set(TagStrings.PremiumCcy, "USD")
                    .Set(TagStrings.HedgeTradeType, "1") // Spot hedge
                    .Set("9943", "2") // ProductQuoteType = Premium
                    .Set("146", "1") // NoRelatedSym
                    .Set(TagStrings.NoLegs, "2")

                    // Leg 1: Buy 1M EUR Call, strike 1.21, 6M
                    .Set("leg1_600", underlying) // LegSymbol
                    .Set("leg1_6714", "1") // LegStrategyInd = CALL
                    .Set("leg1_9125", "1") // Cutoff = NY 10am
                    .Set("leg1_6215", "6M") // Tenor
                    .Set("leg1_611", DateTime.UtcNow.AddMonths(6).ToString("yyyyMMdd")) // LegMaturityDate
                    .Set("leg1_743", DateTime.UtcNow.AddMonths(6).AddDays(2).ToString("yyyyMMdd")) // DeliveryDate
                    .Set("leg1_5020", DateTime.UtcNow.AddDays(2).ToString("yyyyMMdd")) // PremiumDelivery
                    .Set("leg1_612", "1.21") // LegStrikePrice
                    .Set("leg1_9019", "2") // FXOptionStyle = EUROPEAN
                    .Set("leg1_6351", "1") // Position = SAME
                    .Set("leg1_9904", "2") // PriceIndicator = PTS (points)
                    .Set("leg1_5235", "1.1851") // LegSpotRate (reference spot)
                    .Set("leg1_556", "USD") // LegCurrency (notional ccy)
                    .Set("leg1_687", "1") // LegQty = 1M USD
                    .Set("leg1_7940", "SL0") // LegStrategyID
                    .Set("leg1_9034", "EUR") // LegStrategyCcy (put/call ccy)

                    // Leg 2: Sell 2M EUR Call, strike 1.22, 1Y
                    .Set("leg2_600", underlying)
                    .Set("leg2_6714", "1") // CALL
                    .Set("leg2_9125", "1") // NY Cut
                    .Set("leg2_6215", "1Y") // Tenor
                    .Set("leg2_611", DateTime.UtcNow.AddYears(1).ToString("yyyyMMdd"))
                    .Set("leg2_743", DateTime.UtcNow.AddYears(1).AddDays(2).ToString("yyyyMMdd"))
                    .Set("leg2_5020", DateTime.UtcNow.AddDays(2).ToString("yyyyMMdd"))
                    .Set("leg2_612", "1.22") // LegStrikePrice
                    .Set("leg2_9019", "2") // EUROPEAN
                    .Set("leg2_6351", "2") // Position = INVERSE (opposite direction)
                    .Set("leg2_9904", "2") // PTS
                    .Set("leg2_556", "USD")
                    .Set("leg2_687", "2") // LegQty = 2M USD
                    .Set("leg2_7940", "SL1") // LegStrategyID
                    .Set("leg2_9034", "EUR");

                _seqNum++;

                LogMessage("SENT", quoteReq);

                // LP receives request
                var quoteReqID = quoteReq.Get(TagStrings.QuoteReqID);
                _liquidityProviders[lp].ReceiveQuoteRequest(quoteReq);

                // Send Quote Status Report (35=AI)
                var status = new FIXMessage(MsgTypes.QuoteStatusReport)
                    .Set(TagStrings.QuoteReqID, quoteReqID)
                    .Set(TagStrings.QuoteStatus, "0"); // Accepted

                LogMessage("RECV", status);

                requests.Add((lp, quoteReqID));

                // Track request
                if (!_activeRequests.ContainsKey(groupId))
                {
                    _activeRequests[groupId] = new List<StreamInfo>();
                }

                _activeRequests[groupId].Add(new StreamInfo
                {
                    LP = lp,
                    QuoteReqID = quoteReqID,
                    BidQuote = null,
                    OfferQuote = null,
                    LastUpdate = DateTime.UtcNow
                });
            }

            return (groupId, requests);
        }

        /// <summary>
        /// Simulate streaming quotes from LPs
        /// </summary>
        public void StreamQuotes(string groupId, int numUpdates = 3, int delayMs = 500)
        {
            if (!_activeRequests.ContainsKey(groupId))
            {
                Console.WriteLine($"[ERROR] No active requests for group {groupId}");
                return;
            }

            Console.WriteLine($"\n=== STREAMING QUOTES ===");
            Console.WriteLine($"Updates: {numUpdates}, Delay: {delayMs}ms\n");

            for (int i = 0; i < numUpdates; i++)
            {
                Console.WriteLine($"--- Update {i + 1} ---");

                foreach (var stream in _activeRequests[groupId])
                {
                    var lp = _liquidityProviders[stream.LP];

                    // Send bid quote
                    var bid = lp.GenerateQuote(stream.QuoteReqID, Values.Side.Buy);
                    if (bid != null)
                    {
                        LogMessage("RECV", bid);
                        stream.BidQuote = bid;
                        stream.LastUpdate = DateTime.UtcNow;
                    }

                    // Send offer quote
                    var offer = lp.GenerateQuote(stream.QuoteReqID, Values.Side.Sell);
                    if (offer != null)
                    {
                        LogMessage("RECV", offer);
                        stream.OfferQuote = offer;
                        stream.LastUpdate = DateTime.UtcNow;
                    }
                }

                if (i < numUpdates - 1)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }

        /// <summary>
        /// Get best bid/offer across all LPs
        /// </summary>
        public (FIXMessage bestBid, FIXMessage bestOffer) GetBestPrices(string groupId)
        {
            if (!_activeRequests.ContainsKey(groupId))
            {
                return (null, null);
            }

            FIXMessage bestBid = null;
            FIXMessage bestOffer = null;
            double bestBidVol = double.MinValue;
            double bestOfferVol = double.MaxValue;

            foreach (var stream in _activeRequests[groupId])
            {
                // Check bid - read from leg1_5678
                if (stream.BidQuote != null)
                {
                    var volStr = stream.BidQuote.Get("leg1_5678");
                    if (!string.IsNullOrEmpty(volStr) && double.TryParse(volStr, out var bidVol))
                    {
                        if (bidVol > bestBidVol)
                        {
                            bestBidVol = bidVol;
                            bestBid = stream.BidQuote;
                        }
                    }
                }

                // Check offer - read from leg1_5678
                if (stream.OfferQuote != null)
                {
                    var volStr = stream.OfferQuote.Get("leg1_5678");
                    if (!string.IsNullOrEmpty(volStr) && double.TryParse(volStr, out var offerVol))
                    {
                        if (offerVol < bestOfferVol)
                        {
                            bestOfferVol = offerVol;
                            bestOffer = stream.OfferQuote;
                        }
                    }
                }
            }

            return (bestBid, bestOffer);
        }

        /// <summary>
        /// Get all active quote streams for a group
        /// </summary>
        public List<StreamInfo> GetActiveStreams(string groupId)
        {
            if (_activeRequests.ContainsKey(groupId))
            {
                return _activeRequests[groupId];
            }
            return new List<StreamInfo>();
        }

        /// <summary>
        /// Execute against a quote
        /// </summary>
        /// 
        public bool ExecuteTrade(FIXMessage quote, string executionSide)
        {
            Console.WriteLine($"\n=== EXECUTING TRADE ===");
            Console.WriteLine($"Side: {executionSide}");
            Console.WriteLine($"Quote: {quote.Get(TagStrings.OnBehalfOfCompID)} @ {quote.Get(TagStrings.Volatility)} vol\n");

            // Get QuoteReqID and generate ClOrdID with suffix
            string quoteReqID = quote.Get(TagStrings.QuoteReqID);

            // Increment execution counter for this QuoteReqID
            if (!_executionCounters.ContainsKey(quoteReqID))
                _executionCounters[quoteReqID] = 0;

            _executionCounters[quoteReqID]++;
            string clOrdID = $"{quoteReqID}_{_executionCounters[quoteReqID]}";

            // Build New Order Multileg (35=AB)
            var order = new FIXMessage(MsgTypes.NewOrderMultileg)
                .Set(TagStrings.SenderCompID, "<CLIENT>")
                .Set(TagStrings.TargetCompID, "GFI")
                .Set(TagStrings.ClOrdID, clOrdID)
                .Set("40", "1") // OrdType = MARKET
                .Set("59", "3") // TimeInForce = IOC
                .Set(TagStrings.Side, executionSide == "SELL" ? Values.Side.Sell : Values.Side.Buy)
                .Set(TagStrings.QuoteID, quote.Get(TagStrings.QuoteID))
                .Set(TagStrings.QuoteReqID, quoteReqID)
                .Set(TagStrings.SendingTime, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.ffffff"));

            LogMessage("SENT", order);

            // Simulate execution (90% fill rate)
            var random = new Random();
            bool filled = random.NextDouble() > 0.1;

            // Send Execution Report (35=8)
            var execReport = new FIXMessage(MsgTypes.ExecutionReport)
                .Set(TagStrings.ClOrdID, order.Get(TagStrings.ClOrdID))
                .Set(TagStrings.OrderID, $"EXEC{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}")
                .Set("17", $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.1") // ExecID
                .Set(TagStrings.OrdStatus, filled ? Values.OrdStatus.Filled : Values.OrdStatus.Rejected)
                .Set(TagStrings.ExecType, filled ? "2" : "8")
                .Set(TagStrings.Side, order.Get(TagStrings.Side))
                .Set("38", quote.Get(TagStrings.MQSize)) // OrderQty
                .Set("14", filled ? quote.Get(TagStrings.MQSize) : "0") // CumQty
                .Set("151", "0"); // LeavesQty

            if (!filled)
            {
                execReport.Set("58", "Rejected by LP (last-look)");
            }

            LogMessage("RECV", execReport);

            return filled;
        }

        /// <summary>
        /// Cancel quote stream
        /// </summary>
        public void CancelStream(string quoteReqID)
        {
            var cancel = new FIXMessage(MsgTypes.QuoteCancel)
                .Set(TagStrings.QuoteReqID, quoteReqID)
                .Set("298", "2"); // QuoteCancelType = Cancel Quote Request

            LogMessage("SENT", cancel);

            // Find and cancel in LP
            foreach (var lp in _liquidityProviders.Values)
            {
                lp.CancelStream(quoteReqID);
            }
        }

        private void LogMessage(string direction, FIXMessage msg)
        {
            var arrow = direction == "SENT" ? ">>>" : "<<<";

            // Show ALL fields for debugging (including leg-specific fields)
            var allFields = string.Join("|", msg.Fields.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            Console.WriteLine($"[{direction}] {arrow} FIX({msg.MsgType}): {allFields}");
        }
    }
}