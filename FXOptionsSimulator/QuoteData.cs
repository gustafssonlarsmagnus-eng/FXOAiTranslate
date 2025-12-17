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
    }
}
