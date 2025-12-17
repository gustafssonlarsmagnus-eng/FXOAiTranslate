using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FXOAiTranslate.WPF.ViewModels
{
    public class LadderRowViewModel : INotifyPropertyChanged
    {
        public string LPName { get; set; } = "LP";
        public string ResponseTime { get; set; } = "15ms";
        public bool IsEnabled { get; set; } = true;
        
        // Bid Side
        public string BidVol { get; set; } = "5M";
        public string BidPremium { get; set; } = "65k";
        public string BidPips { get; set; } = "39p";
        public bool IsBestBid { get; set; }

        // Offer Side
        public string OfferVol { get; set; } = "5M";
        public string OfferPremium { get; set; } = "68k";
        public string OfferPips { get; set; } = "44p";
        public bool IsBestOffer { get; set; }

        public double StalenessOpacity { get; set; } = 1.0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? un = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(un));
    }
}
