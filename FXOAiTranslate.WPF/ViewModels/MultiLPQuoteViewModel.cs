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
        private string _bestBuyBank = string.Empty;
        private double _bestBuyPremium;
        private double _bestBuyVolatility;
        private string _bestSellBank = string.Empty;
        private double _bestSellPremium;
        private double _bestSellVolatility;

        public ObservableCollection<QuoteRowViewModel> Quotes { get; } = new();

        public string TradeTitle
        {
            get => _tradeTitle;
            set => SetProperty(ref _tradeTitle, value);
        }

        public string BestBuyBank
        {
            get => _bestBuyBank;
            set => SetProperty(ref _bestBuyBank, value);
        }

        public double BestBuyPremium
        {
            get => _bestBuyPremium;
            set => SetProperty(ref _bestBuyPremium, value);
        }

        public double BestBuyVolatility
        {
            get => _bestBuyVolatility;
            set => SetProperty(ref _bestBuyVolatility, value);
        }

        public string BestSellBank
        {
            get => _bestSellBank;
            set => SetProperty(ref _bestSellBank, value);
        }

        public double BestSellPremium
        {
            get => _bestSellPremium;
            set => SetProperty(ref _bestSellPremium, value);
        }

        public double BestSellVolatility
        {
            get => _bestSellVolatility;
            set => SetProperty(ref _bestSellVolatility, value);
        }

        // Formatted display properties
        public string BestBuyPremiumFormatted => $"${BestBuyPremium:N0}";
        public string BestBuyVolatilityFormatted => $"{BestBuyVolatility:F2} vol";
        public string BestSellPremiumFormatted => $"${BestSellPremium:N0}";
        public string BestSellVolatilityFormatted => $"{BestSellVolatility:F2} vol";

        // Alias properties for BID/OFFER terminology (matches new UI design)
        public string BestBidBank => BestBuyBank;
        public double BestBidPremium => BestBuyPremium;
        public double BestBidVolatility => BestBuyVolatility;
        public string BestBidPremiumFormatted => BestBuyPremiumFormatted;
        public string BestBidVolatilityFormatted => BestBuyVolatilityFormatted;

        public string BestOfferBank => BestSellBank;
        public double BestOfferPremium => BestSellPremium;
        public double BestOfferVolatility => BestSellVolatility;
        public string BestOfferPremiumFormatted => BestSellPremiumFormatted;
        public string BestOfferVolatilityFormatted => BestSellVolatilityFormatted;

        // Commands
        public ICommand ExecuteBuyCommand { get; }
        public ICommand ExecuteSellCommand { get; }
        public ICommand ExecuteBidCommand => ExecuteBuyCommand; // Alias
        public ICommand ExecuteOfferCommand => ExecuteSellCommand; // Alias
        public ICommand RequestQuotesCommand { get; }

        public MultiLPQuoteViewModel(TradeStructure trade)
        {
            _trade = trade;
            TradeTitle = $"{trade.Underlying} {GetStructureTypeName(trade.StructureType)}";

            // Initialize commands
            ExecuteBuyCommand = new RelayCommand(ExecuteBuy);
            ExecuteSellCommand = new RelayCommand(ExecuteSell);
            RequestQuotesCommand = new RelayCommand(RequestQuotes);

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

            // Find best buy (lowest absolute bid premium for client buying protection)
            var bestBuy = Quotes
                .Where(q => q.BidPremium < 0) // Negative = client receives
                .OrderBy(q => Math.Abs(q.BidPremium))
                .FirstOrDefault() ?? Quotes.OrderBy(q => q.BidPremium).First();

            // Find best sell (lowest offer premium for client selling)
            var bestSell = Quotes
                .OrderBy(q => q.OfferPremium)
                .First();

            // Update best price flags
            foreach (var quote in Quotes)
            {
                quote.IsBestPrice = (quote == bestBuy);
            }

            // Update top panel
            BestBuyBank = bestBuy.BankName;
            BestBuyPremium = bestBuy.BidPremium;
            BestBuyVolatility = bestBuy.BidVol;

            BestSellBank = bestSell.BankName;
            BestSellPremium = bestSell.OfferPremium;
            BestSellVolatility = bestSell.OfferVol;

            // Notify formatted properties changed
            OnPropertyChanged(nameof(BestBuyPremiumFormatted));
            OnPropertyChanged(nameof(BestBuyVolatilityFormatted));
            OnPropertyChanged(nameof(BestSellPremiumFormatted));
            OnPropertyChanged(nameof(BestSellVolatilityFormatted));
        }

        private void ExecuteBuy()
        {
            System.Windows.MessageBox.Show(
                $"Executing BUY with {BestBuyBank}\nPremium: {BestBuyPremiumFormatted}\nVol: {BestBuyVolatility:F2}%",
                "Execute Trade",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ExecuteSell()
        {
            System.Windows.MessageBox.Show(
                $"Executing SELL with {BestSellBank}\nPremium: {BestSellPremiumFormatted}\nVol: {BestSellVolatility:F2}%",
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
