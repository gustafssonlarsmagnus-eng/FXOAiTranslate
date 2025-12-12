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
        private string _selectedMetric = "Premium"; // Premium, Vol, or Pips

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

        public string SelectedMetric
        {
            get => _selectedMetric;
            set
            {
                if (SetProperty(ref _selectedMetric, value))
                {
                    // Notify all display properties to refresh
                    OnPropertyChanged(nameof(BidDisplay));
                    OnPropertyChanged(nameof(OfferDisplay));
                }
            }
        }

        // Calculated properties
        public double BidPips => BidPremium / 100; // Simplified pips calculation
        public double OfferPips => OfferPremium / 100;

        // Formatted display properties
        public string BidPremiumFormatted => $"{BidPremium:N0}";
        public string OfferPremiumFormatted => $"{OfferPremium:N0}";
        public string BidVolFormatted => $"{BidVol:F2}%";
        public string MidVolFormatted => $"{MidVol:F2}%";
        public string OfferVolFormatted => $"{OfferVol:F2}%";
        public string DeltaFormatted => $"{Delta:F1}Δ";
        public string SpotReferenceFormatted => $"{SpotReference:F4}";

        // Combined column formats (PREM / VOL) - Legacy
        public string BidCombined => $"{BidPremiumFormatted}\n{BidVol:F2}";
        public string AskCombined => $"{OfferPremium:N0}\n{OfferVol:F2}";
        public string DeltaSpotCombined => $"{Delta:F0}%\n{SpotReference:F4}";

        // Option B Format: 2-line display with selected metric emphasized
        public string BidDisplay
        {
            get
            {
                return SelectedMetric switch
                {
                    "Premium" => $"R {BidPremium:N0}\n{BidVol:F2}v | {BidPips:F0}p",
                    "Vol" => $"{BidVol:F2}v\n R {BidPremium:N0} | {BidPips:F0}p",
                    "Pips" => $"{BidPips:F0}p\nR {BidPremium:N0} | {BidVol:F2}v",
                    _ => $"R {BidPremium:N0}\n{BidVol:F2}v | {BidPips:F0}p"
                };
            }
        }

        public string OfferDisplay
        {
            get
            {
                return SelectedMetric switch
                {
                    "Premium" => $"P {OfferPremium:N0}\n{OfferVol:F2}v | {OfferPips:F0}p",
                    "Vol" => $"{OfferVol:F2}v\nP {OfferPremium:N0} | {OfferPips:F0}p",
                    "Pips" => $"{OfferPips:F0}p\nP {OfferPremium:N0} | {OfferVol:F2}v",
                    _ => $"P {OfferPremium:N0}\n{OfferVol:F2}v | {OfferPips:F0}p"
                };
            }
        }

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
