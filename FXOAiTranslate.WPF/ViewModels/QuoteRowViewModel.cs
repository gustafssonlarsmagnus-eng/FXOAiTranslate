using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FXOAiTranslate.WPF.ViewModels
{
    /// <summary>
    /// Represents a single row in the quote grid (one LP's quote)
    /// </summary>
    public class QuoteRowViewModel : INotifyPropertyChanged
    {
        private bool _isBestPrice;
        private string _bankName = string.Empty;
        private double _bidVol;
        private double _bidPremium;
        private double _midVol;
        private double _offerVol;
        private double _offerPremium;
        private double _delta;
        private double _spotReference;

        public string BankName
        {
            get => _bankName;
            set => SetProperty(ref _bankName, value);
        }

        public double BidVol
        {
            get => _bidVol;
            set => SetProperty(ref _bidVol, value);
        }

        public double BidPremium
        {
            get => _bidPremium;
            set => SetProperty(ref _bidPremium, value);
        }

        public double MidVol
        {
            get => _midVol;
            set => SetProperty(ref _midVol, value);
        }

        public double OfferVol
        {
            get => _offerVol;
            set => SetProperty(ref _offerVol, value);
        }

        public double OfferPremium
        {
            get => _offerPremium;
            set => SetProperty(ref _offerPremium, value);
        }

        public double Delta
        {
            get => _delta;
            set => SetProperty(ref _delta, value);
        }

        public double SpotReference
        {
            get => _spotReference;
            set => SetProperty(ref _spotReference, value);
        }

        public bool IsBestPrice
        {
            get => _isBestPrice;
            set => SetProperty(ref _isBestPrice, value);
        }

        // Formatted display properties (original format)
        public string BidPremiumFormatted => $"{BidPremium:N0}";
        public string OfferPremiumFormatted => $"{OfferPremium:N0}";
        public string BidVolFormatted => $"{BidVol:F2}%";
        public string MidVolFormatted => $"{MidVol:F2}%";
        public string OfferVolFormatted => $"{OfferVol:F2}%";
        public string DeltaFormatted => $"{Delta:F1}Δ";
        public string SpotReferenceFormatted => $"{SpotReference:F4}";

        // Ladder display properties (compact format)
        public double BidPremiumK => BidPremium / 1000.0;
        public double OfferPremiumK => OfferPremium / 1000.0;
        public double BidPips => (BidPremium / 10000000.0) * 10000; // Simplified pip calculation
        public double OfferPips => (OfferPremium / 10000000.0) * 10000;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
