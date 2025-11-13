using System;
using System.Collections.Generic;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Simulates a liquidity provider (bank) streaming quotes
    /// </summary>
    public class SimulatedLiquidityProvider
    {
        public string Name { get; }
        private double _baseVol;
        private double _spread;
        private Dictionary<string, StreamState> _activeStreams;
        private Random _random;

        private class StreamState
        {
            public string QuoteReqID { get; set; }
            public string Underlying { get; set; }
            public int QuoteCounter { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        public SimulatedLiquidityProvider(string name, double baseVol = 8.0, double spread = 0.5)
        {
            Name = name;
            _baseVol = baseVol;
            _spread = spread;
            _activeStreams = new Dictionary<string, StreamState>();
            _random = new Random(name.GetHashCode()); // Deterministic per LP
        }

        /// <summary>
        /// Receive quote request and prepare to stream
        /// </summary>
        public void ReceiveQuoteRequest(FIXMessage quoteRequest)
        {
            var quoteReqID = quoteRequest.Get(TagStrings.QuoteReqID);
            var underlying = quoteRequest.Get(TagStrings.Symbol);

            _activeStreams[quoteReqID] = new StreamState
            {
                QuoteReqID = quoteReqID,
                Underlying = underlying,
                QuoteCounter = 0,
                LastUpdate = DateTime.UtcNow
            };

            Console.WriteLine($"[{Name}] Stream opened for {underlying} (ReqID: {quoteReqID})");
        }

        /// <summary>
        /// Generate a quote for bid or offer side with full leg details
        /// </summary>
        public FIXMessage GenerateQuote(string quoteReqID, string side)
        {
            if (!_activeStreams.TryGetValue(quoteReqID, out var stream))
                return null;

            stream.QuoteCounter++;
            stream.LastUpdate = DateTime.UtcNow;

            // Simulate market movement
            var marketMove = (_random.NextDouble() - 0.5) * 0.2; // ±0.1 vol

            double vol;
            if (side == Values.Side.Buy) // Bid
            {
                vol = _baseVol - _spread / 2 + marketMove;
            }
            else // Offer
            {
                vol = _baseVol + _spread / 2 + marketMove;
            }

            // Build Quote message (35=S)
            var quote = new FIXMessage(MsgTypes.Quote)
                .Set(TagStrings.SenderCompID, "GFI")
                .Set(TagStrings.TargetCompID, "<CLIENT>")
                .Set(TagStrings.OnBehalfOfCompID, Name)
                .Set(TagStrings.QuoteReqID, quoteReqID)
                .Set(TagStrings.QuoteID, $"{Name}.Q{stream.QuoteCounter}")
                .Set(TagStrings.Side, side)
                .Set(TagStrings.Symbol, stream.Underlying)
                .Set(TagStrings.Structure, Values.Structure.CallSpread)
                .Set(TagStrings.SendingTime, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.ffffff"))
                .Set("62", DateTime.UtcNow.AddSeconds(30).ToString("yyyyMMdd-HH:mm:ss")) // ValidUntilTime
                .Set("6120", "2") // NoMQEntries (2 legs for call spread)

                // Leg 1: Buy 1M Call at 1.21 strike, 6M tenor
                .Set("leg1_7940", "SL0") // LegStrategyID
                .Set("leg1_5678", vol.ToString("F6")) // Volatility
                .Set("leg1_5359", "1") // MQSize: 1M
                .Set("leg1_5235", "1.1851") // LegSpotRate
                .Set("leg1_5191", "51.5") // LegForwardPoints
                .Set("leg1_9115", "0.06") // DepoRate
                .Set("leg1_9073", "USD") // DepoRateCcy
                .Set("leg1_5844", "171.84") // LegPremPrice (premium in points)
                .Set("leg1_6035", "38") // LegDelta
                .Set("leg1_6354", "1.21") // MQStrikePrice
                .Set("leg1_6215", "6M") // Tenor

                // Leg 2: Sell 2M Call at 1.22 strike, 1Y tenor
                .Set("leg2_7940", "SL1") // LegStrategyID
                .Set("leg2_5678", (vol - 0.3).ToString("F6")) // Volatility (lower for OTM)
                .Set("leg2_5359", "2") // MQSize: 2M
                .Set("leg2_5235", "1.1851") // LegSpotRate
                .Set("leg2_5191", "101.0") // LegForwardPoints
                .Set("leg2_9115", "0.05") // DepoRate
                .Set("leg2_9073", "USD") // DepoRateCcy
                .Set("leg2_5844", "-248.55") // LegPremPrice (negative = you receive)
                .Set("leg2_6035", "40") // LegDelta
                .Set("leg2_6354", "1.22") // MQStrikePrice
                .Set("leg2_6215", "1Y"); // Tenor

            return quote;
        }

        /// <summary>
        /// Cancel a quote stream
        /// </summary>
        public void CancelStream(string quoteReqID)
        {
            if (_activeStreams.Remove(quoteReqID))
            {
                Console.WriteLine($"[{Name}] Stream canceled for ReqID: {quoteReqID}");
            }
        }

        /// <summary>
        /// Check if stream is active
        /// </summary>
        public bool HasActiveStream(string quoteReqID)
        {
            return _activeStreams.ContainsKey(quoteReqID);
        }
    }
}