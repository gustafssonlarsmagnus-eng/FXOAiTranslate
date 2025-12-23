using System;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Represents a quote received from a Liquidity Provider via FIX
    /// </summary>
    public class QuoteData
    {
        /// <summary>
        /// Liquidity Provider name (DEUT, JPM, CITI, BARX, etc.)
        /// </summary>
        public string LP { get; set; }

        /// <summary>
        /// BID premium (you receive - more positive is better for you)
        /// FIX Tag 6436
        /// </summary>
        public double BidPremium { get; set; }

        /// <summary>
        /// OFFER premium (you pay - less negative is better for you)
        /// FIX Tag 6436
        /// </summary>
        public double OfferPremium { get; set; }

        /// <summary>
        /// BID volatility (optional)
        /// FIX Tag 5678
        /// </summary>
        public double BidVol { get; set; }

        /// <summary>
        /// OFFER volatility (optional)
        /// FIX Tag 5678
        /// </summary>
        public double OfferVol { get; set; }

        /// <summary>
        /// BID leg premium price (premium per million)
        /// FIX Tag 5844 - This is the "pips" value streamed by GFI
        /// </summary>
        public double BidLegPremPrice { get; set; }

        /// <summary>
        /// OFFER leg premium price (premium per million)
        /// FIX Tag 5844 - This is the "pips" value streamed by GFI
        /// </summary>
        public double OfferLegPremPrice { get; set; }

        /// <summary>
        /// Quote ID for execution (critical - must match exactly)
        /// FIX Tag 117
        /// </summary>
        public string QuoteID { get; set; }

        /// <summary>
        /// Timestamp when quote was received
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Underlying currency pair (EURUSD, EURNOK, etc.)
        /// </summary>
        public string Underlying { get; set; }

        /// <summary>
        /// Quote side: "BID" or "OFFER"
        /// </summary>
        public string Side { get; set; }

        /// <summary>
        /// Notional amount (size)
        /// FIX Tag 5359 (MQSize)
        /// </summary>
        public double Notional { get; set; }

        /// <summary>
        /// Delta (percentage)
        /// FIX Tag 6035 (LegDelta)
        /// </summary>
        public double Delta { get; set; }

        /// <summary>
        /// Premium currency (EUR, USD, etc.)
        /// FIX Tag 5830
        /// </summary>
        public string PremiumCurrency { get; set; }

        /// <summary>
        /// Quote expiry time
        /// FIX Tag 62 (ValidUntilTime)
        /// </summary>
        public string ValidUntilTime { get; set; }

        /// <summary>
        /// Spread (difference between BID and OFFER premiums)
        /// Calculated client-side
        /// </summary>
        public double Spread { get; set; }
    }
}
