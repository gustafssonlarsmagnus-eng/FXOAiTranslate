using System;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// Data model for storing LP quote information
    /// </summary>
    public class LPQuoteData
    {
        public string LP { get; set; }
        public double BidVol { get; set; }
        public double OfferVol { get; set; }
        public double BidPremium { get; set; }
        public double OfferPremium { get; set; }
        public double BidLegPremPrice { get; set; }  // Tag 5844: Premium per MM ("pips")
        public double OfferLegPremPrice { get; set; }  // Tag 5844: Premium per MM ("pips")
        public string BidQuoteId { get; set; }
        public string OfferQuoteId { get; set; }
        public DateTime LastUpdate { get; set; }
        public DateTime ValidUntilTime { get; set; }
        public string SpotRate { get; set; }    // Tag 5235: LegSpotRate
        public double ForwardPoints { get; set; }    // Tag 5191: LegForwardPoints
        public double Delta { get; set; }            // Tag 6035: LegDelta
        public string PremiumCurrency { get; set; }  // Tag 5830: Premium currency (EUR, USD, etc.)
        public string LegPremiumCurrency { get; set; }  // Tag 6436 as currency if provided, or derived from leg
    }
}
