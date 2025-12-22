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
public string BidQuoteId { get; set; }
        public string OfferQuoteId { get; set; }
        public DateTime LastUpdate { get; set; }
        public DateTime ValidUntilTime { get; set; }
     public string SpotRate { get; set; }
        public double Delta { get; set; }
    }
}
