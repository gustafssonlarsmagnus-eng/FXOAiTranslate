using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.ViewModels
{
    /// <summary>
    /// ViewModel for the Multi-LP Quote window
    /// </summary>
    public class MultiLPQuoteViewModel : INotifyPropertyChanged
    {
        private TradeStructure _trade;
        private string _tradeTitle = string.Empty;
        private string _underlying = string.Empty;
        private double _spotReference;
        private double _midVol;
        private bool _showActivePanel = false;

        // BID side (renamed from BestBuy)
        private string _bestBidBank = string.Empty;
        private double _bestBidPremium;
        private double _bestBidVol;
        private double _bestBidPips;

        // OFFER side (renamed from BestSell)
        private string _bestOfferBank = string.Empty;
        private double _bestOfferPremium;
        private double _bestOfferVol;
        private double _bestOfferPips;

        // Trade details
        private string _bidCurrency = "EUR";
        private string _offerCurrency = "USD";
        private double _notional = 10000000;
        private double _strike = 1.1751;
        private DateTime _expiryDate = DateTime.Now.AddMonths(1);
        private string _optionTypeDisplay = "EUR Call / USD Put";

        public ObservableCollection<QuoteRowViewModel> Quotes { get; } = new();
        public ObservableCollection<QuoteRowViewModel> LadderQuotes { get; } = new();

        public string TradeTitle
        {
            get => _tradeTitle;
            set => SetProperty(ref _tradeTitle, value);
        }

        public string Underlying
        {
            get => _underlying;
            set => SetProperty(ref _underlying, value);
        }

        public double SpotReference
        {
            get => _spotReference;
            set => SetProperty(ref _spotReference, value);
        }

        public double MidVol
        {
            get => _midVol;
            set => SetProperty(ref _midVol, value);
        }

        public bool ShowActivePanel
        {
            get => _showActivePanel;
            set
            {
                if (SetProperty(ref _showActivePanel, value))
                {
                    OnPropertyChanged(nameof(ShowRFQPanel));
                }
            }
        }

        public bool ShowRFQPanel => !ShowActivePanel;

        // BID side properties
        public string BestBidBank
        {
            get => _bestBidBank;
            set => SetProperty(ref _bestBidBank, value);
        }

        public double BestBidPremium
        {
            get => _bestBidPremium;
            set => SetProperty(ref _bestBidPremium, value);
        }

        public double BestBidVol
        {
            get => _bestBidVol;
            set => SetProperty(ref _bestBidVol, value);
        }

        public double BestBidPips
        {
            get => _bestBidPips;
            set => SetProperty(ref _bestBidPips, value);
        }

        // OFFER side properties
        public string BestOfferBank
        {
            get => _bestOfferBank;
            set => SetProperty(ref _bestOfferBank, value);
        }

        public double BestOfferPremium
        {
            get => _bestOfferPremium;
            set => SetProperty(ref _bestOfferPremium, value);
        }

        public double BestOfferVol
        {
            get => _bestOfferVol;
            set => SetProperty(ref _bestOfferVol, value);
        }

        public double BestOfferPips
        {
            get => _bestOfferPips;
            set => SetProperty(ref _bestOfferPips, value);
        }

        // Trade details properties
        public string BidCurrency
        {
            get => _bidCurrency;
            set => SetProperty(ref _bidCurrency, value);
        }

        public string OfferCurrency
        {
            get => _offerCurrency;
            set => SetProperty(ref _offerCurrency, value);
        }

        public double Notional
        {
            get => _notional;
            set => SetProperty(ref _notional, value);
        }

        public double Strike
        {
            get => _strike;
            set => SetProperty(ref _strike, value);
        }

        public DateTime ExpiryDate
        {
            get => _expiryDate;
            set => SetProperty(ref _expiryDate, value);
        }

        public string OptionTypeDisplay
        {
            get => _optionTypeDisplay;
            set => SetProperty(ref _optionTypeDisplay, value);
        }

        // Calculated properties
        public double Spread => BestOfferVol - BestBidVol;

        // Commands
        public ICommand ExecuteBuyCommand { get; }
        public ICommand ExecuteSellCommand { get; }
        public ICommand RequestQuotesCommand { get; }
        public ICommand StartRFQCommand { get; }

        public MultiLPQuoteViewModel(TradeStructure trade)
        {
            _trade = trade;

            // Initialize trade data
            Underlying = trade.Underlying ?? "EURUSD";
            TradeTitle = $"{GetStructureTypeName(trade.StructureType)} {(trade.Legs.FirstOrDefault()?.OptionType ?? "CALL")}";
            SpotReference = trade.SpotReference;
            Strike = trade.Legs.FirstOrDefault()?.Strike ?? 1.0;
            Notional = (trade.Legs.FirstOrDefault()?.NotionalMM ?? 10) * 1000000;
            ExpiryDate = trade.Expiry;

            // Set currency based on underlying
            if (Underlying.Length >= 6)
            {
                BidCurrency = Underlying.Substring(0, 3);
                OfferCurrency = Underlying.Substring(3, 3);
            }

            // Initialize commands
            ExecuteBuyCommand = new RelayCommand(ExecuteBuy);
            ExecuteSellCommand = new RelayCommand(ExecuteSell);
            RequestQuotesCommand = new RelayCommand(RequestQuotes);
            StartRFQCommand = new RelayCommand(StartRFQ);

            // Initialize with sample data
            LoadSampleQuotes();
        }

        private void LoadSampleQuotes()
        {
            var random = new Random();

            var banks = new[] { "JPM", "CITI", "GS", "BAML", "MS", "HSBC" };

            foreach (var bank in banks)
            {
                var bidVol = 5.0 + random.NextDouble() * 0.5;
                var offerVol = bidVol + 0.2 + random.NextDouble() * 0.2;
                var midVol = (bidVol + offerVol) / 2;

                var bidPremium = 65000 + random.Next(10000);
                var offerPremium = bidPremium + 2000 + random.Next(3000);

                Quotes.Add(new QuoteRowViewModel
                {
                    BankName = bank,
                    BidVol = bidVol,
                    BidPremium = bidPremium,
                    MidVol = midVol,
                    OfferVol = offerVol,
                    OfferPremium = offerPremium,
                    Delta = 25.0 + random.NextDouble() * 10,
                    SpotReference = 1.0850 + random.NextDouble() * 0.01,
                    IsBestPrice = false
                });
            }

            // Update best prices
            UpdateBestPrices();
        }

        public void UpdateBestPrices()
        {
            if (!Quotes.Any()) return;

            // Find best bid (lowest absolute bid premium for client buying protection)
            var bestBid = Quotes
                .Where(q => q.BidPremium < 0) // Negative = client receives
                .OrderBy(q => Math.Abs(q.BidPremium))
                .FirstOrDefault() ?? Quotes.OrderBy(q => q.BidPremium).First();

            // Find best offer (lowest offer premium for client selling)
            var bestOffer = Quotes
                .OrderBy(q => q.OfferPremium)
                .First();

            // Update best price flags
            foreach (var quote in Quotes)
            {
                quote.IsBestPrice = (quote == bestBid);
            }

            // Update BID side
            BestBidBank = bestBid.BankName;
            BestBidPremium = bestBid.BidPremium;
            BestBidVol = bestBid.BidVol;
            BestBidPips = CalculatePips(bestBid.BidPremium, Notional);

            // Update OFFER side
            BestOfferBank = bestOffer.BankName;
            BestOfferPremium = bestOffer.OfferPremium;
            BestOfferVol = bestOffer.OfferVol;
            BestOfferPips = CalculatePips(bestOffer.OfferPremium, Notional);

            // Update calculated properties
            MidVol = (BestBidVol + BestOfferVol) / 2;
            OnPropertyChanged(nameof(Spread));

            // Copy to ladder
            LadderQuotes.Clear();
            foreach (var quote in Quotes)
            {
                LadderQuotes.Add(quote);
            }
        }

        private double CalculatePips(double premium, double notional)
        {
            // Simple pip calculation (this should be more sophisticated in production)
            return (premium / notional) * 10000;
        }

        private void StartRFQ()
        {
            // Transition from RFQ dormant state to Active prices state
            ShowActivePanel = true;

            // Load fresh quotes
            LoadSampleQuotes();
        }

        private void ExecuteBuy()
        {
            System.Windows.MessageBox.Show(
                $"Executing BUY with {BestBidBank}\nPremium: {BestBidPremium:N0} {BidCurrency}\nVol: {BestBidVol:F2}%\nPips: {BestBidPips:+0;-0}p",
                "Execute Trade",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ExecuteSell()
        {
            System.Windows.MessageBox.Show(
                $"Executing SELL with {BestOfferBank}\nPremium: {BestOfferPremium:N0} {OfferCurrency}\nVol: {BestOfferVol:F2}%\nPips: {BestOfferPips:+0;-0}p",
                "Execute Trade",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void RequestQuotes()
        {
            System.Windows.MessageBox.Show(
                "Requesting fresh quotes from all LPs...",
                "Quote Request",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            // Simulate quote update
            LoadSampleQuotes();
        }

        private string GetStructureTypeName(string? type)
        {
            return type switch
            {
                "1" => "Vanilla",
                "5" => "Risk Reversal",
                "8" => "Call Spread",
                "9" => "Put Spread",
                "10" => "Seagull",
                _ => "Custom"
            };
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

    /// <summary>
    /// Simple relay command implementation
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
