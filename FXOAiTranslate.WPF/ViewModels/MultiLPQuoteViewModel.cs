using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.ViewModels
{
    public class MultiLPQuoteViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<LadderRowViewModel> LPRows { get; set; } = new ObservableCollection<LadderRowViewModel>();

        // Mock Properties for Visuals
        public string RawInput { get; set; } = "buy 10m eurusd 1m call";
        public double BestBidPrice { get; set; } = 5.42;
        public double BestAskPrice { get; set; } = 5.82;
        public string BestBidLP { get; set; } = "DEUT";
        public string BestAskLP { get; set; } = "JPM";
        public bool IsRfqState { get; set; } = false;

        public string Strike { get; set; } = "1.0850";
        public string Tenor { get; set; } = "1M";
        public string ExpiryDateString { get; set; } = "2024-06-20";
        public string OptionType { get; set; } = "CALL";

        public string MarketSpot { get; set; } = "1.0845";
        public string MarketFwdPts { get; set; } = "5.2";
        public string RiskDelta { get; set; } = "48%";
        public string RiskVol { get; set; } = "5.65";

        public MultiLPQuoteViewModel(TradeStructure trade)
        {
            // Sample Data for Visual Verification
            LPRows.Add(new LadderRowViewModel { LPName = "DEUT", BidVol="5.42", BidPremium="69k", BidPips="39p", IsBestBid=true,  OfferVol="5.82", OfferPremium="71k", OfferPips="44p", IsBestOffer=true, ResponseTime="8ms" });
            LPRows.Add(new LadderRowViewModel { LPName = "JPM",  BidVol="5.47", BidPremium="68k", BidPips="43p", IsBestBid=false, OfferVol="5.90", OfferPremium="72k", OfferPips="51p", IsBestOffer=false, ResponseTime="12ms" });
            LPRows.Add(new LadderRowViewModel { LPName = "CITI", BidVol="5.44", BidPremium="68k", BidPips="40p", IsBestBid=false, OfferVol="5.88", OfferPremium="75k", OfferPips="49p", IsBestOffer=false, ResponseTime="15ms" });
            LPRows.Add(new LadderRowViewModel { LPName = "BARX", BidVol="5.41", BidPremium="67k", BidPips="38p", IsBestBid=false, OfferVol="5.91", OfferPremium="76k", OfferPips="52p", IsBestOffer=false, ResponseTime="9ms" });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? un = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(un));
    }
}
