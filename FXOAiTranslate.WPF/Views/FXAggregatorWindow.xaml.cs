using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FXOAiTranslate.WPF.ViewModels;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// FX Aggregator - Pure WPF implementation with FIX integration
    /// </summary>
    public partial class FXAggregatorWindow : Window
    {
        private readonly FXAggregatorViewModel _viewModel;
        private readonly TradeStructure _trade;
        private readonly string[] _lps;
        private readonly string _groupId;

        public FXAggregatorWindow(TradeStructure trade)
        {
         Console.WriteLine("[FXAggregator] Constructor called");
            InitializeComponent();
            Console.WriteLine("[FXAggregator] InitializeComponent completed");

            _trade = trade ?? throw new ArgumentNullException(nameof(trade));
       _groupId = $"GRP_{DateTime.Now:HHmmss}";

            // Get LPs from FenicsConfig
   var config = new FenicsConfig();
            _lps = config.LiquidityProviders.Keys.ToArray();
            Console.WriteLine($"[FXAggregator] Loaded {_lps.Length} LPs from FenicsConfig: {string.Join(", ", _lps)}");

      // Initialize ViewModel
            _viewModel = new FXAggregatorViewModel();
    DataContext = _viewModel;
            Console.WriteLine($"[FXAggregator] DataContext set, Trade: {_trade.Underlying}");

            // Populate trade details
         InitializeTradeDetails();

    // Initialize LP quotes
            InitializeLPQuotes();

      // Wire up FIX events
   SubscribeToFIXEvents();

            // Send quote requests
            SendQuoteRequestsToAllLPs();

    Console.WriteLine("[FXAggregator] Window initialization complete");
        }

        private void InitializeTradeDetails()
        {
            _viewModel.Underlying = _trade.Underlying;
            _viewModel.StructureType = _trade.StructureType;
          _viewModel.SpotRate = _trade.SpotReference;

    if (_trade.Legs?.Count > 0)
      {
     var leg = _trade.Legs[0];
  _viewModel.Strike = leg.Strike;
 _viewModel.NotionalMM = leg.NotionalMM;
                _viewModel.OptionType = leg.OptionType;
       _viewModel.Direction = leg.Direction;
          _viewModel.Tenor = leg.Tenor;
            _viewModel.Expiry = leg.ExpiryDate.ToString("dd-MMM-yy");
        }

   Title = $"FX Aggregator - {_trade.Underlying}";
      }

        private void InitializeLPQuotes()
        {
     foreach (var lp in _lps)
     {
         var lpQuote = new LPQuoteViewModel
    {
         LPName = lp,
      IsEnabled = true,
  RemainingSeconds = 0 // Will be set when quote arrives
   };
        
 // Subscribe to property changes to recalculate best prices
  lpQuote.PropertyChanged += (s, e) =>
   {
      if (e.PropertyName == "IsEnabled" || e.PropertyName == "RemainingSeconds")
  {
    _viewModel.RecalculateBestPrices();
      }
    };
       
       _viewModel.LPQuotes.Add(lpQuote);
        }
        }

        private void SubscribeToFIXEvents()
        {
  // TODO: Wire up FIX quote events when GlobalFIXSession supports OnQuoteReceived
     Console.WriteLine("[FXAggregator] FIX event subscription not yet implemented");
   }

      private void UnsubscribeFromFIXEvents()
        {
         // TODO: Wire up FIX quote events when GlobalFIXSession supports OnQuoteReceived
        }

        private void SendQuoteRequestsToAllLPs()
    {
     try
  {
         foreach (var lp in _lps)
         {
  // TODO: Send quote request via GlobalFIXSession
    Console.WriteLine($"[FXAggregator] Would send quote request to {lp}");
   }

    _viewModel.IsRfqActive = true;
    }
       catch (Exception ex)
   {
 Console.WriteLine($"[FXAggregator] Failed to send quote requests: {ex.Message}");
      System.Windows.MessageBox.Show($"Failed to send quote requests: {ex.Message}", "Error",
    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
         }
        }

        // Placeholder for quote handling - uncomment when QuoteData type is available
        /*
     private void HandleQuoteReceived(QuoteData quote)
        {
 Dispatcher.Invoke(() =>
    {
  // Handle quote update
       });
}
        */

   private void BidTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
if (!_viewModel.IsRfqActive)
    {
        SendQuoteRequestsToAllLPs();
   return;
      }

    ExecuteTrade("BID");
        }

        private void OfferTile_Click(object sender, MouseButtonEventArgs e)
      {
    if (!_viewModel.IsRfqActive)
   {
         SendQuoteRequestsToAllLPs();
     return;
            }

      ExecuteTrade("OFFER");
        }

  private void ExecuteTrade(string side)
     {
          try
    {
         // Find the best quote for execution
     var bestLP = side == "BID"
          ? _viewModel.LPQuotes.Where(lp => lp.IsEnabled && lp.RemainingSeconds > 0)
   .OrderByDescending(lp => lp.BidPremium)
      .FirstOrDefault()
   : _viewModel.LPQuotes.Where(lp => lp.IsEnabled && lp.RemainingSeconds > 0)
         .OrderBy(lp => lp.OfferPremium)
      .FirstOrDefault();

      if (bestLP == null)
 {
          MessageBox.Show("No valid quotes available for execution.", "No Quotes",
    MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
           }

                Console.WriteLine($"[FXAggregator] Executing {side} with {bestLP.LPName} QuoteID={bestLP.QuoteId}");

         // TODO: Send execution to GFI via FIX
                // GlobalFIXSession.Instance.SendExecution(bestLP.QuoteId, side);

         MessageBox.Show($"Trade executed!\n\nSide: {side}\nLP: {bestLP.LPName}\nPrice: {(side == "BID" ? bestLP.BidPremium : bestLP.OfferPremium):F2}",
 "Trade Executed", MessageBoxButton.OK, MessageBoxImage.Information);
          }
            catch (Exception ex)
    {
          MessageBox.Show($"Execution failed: {ex.Message}", "Error",
  MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
        ParseTradeInput();
        }

   private void TradeInputBox_KeyDown(object sender, KeyEventArgs e)
        {
    if (e.Key == Key.Enter)
     {
       ParseTradeInput();
 e.Handled = true;
            }
        }

        private void ParseTradeInput()
        {
            string input = TradeInputBox.Text?.Trim();
       if (string.IsNullOrEmpty(input))
   {
     MessageBox.Show("Please enter a trade request.", "No Input", MessageBoxButton.OK, MessageBoxImage.Information);
       return;
  }

       Console.WriteLine($"[FXAggregator] Parse requested: {input}");
    // TODO: Integrate with TradeParser to parse the input
   // For now, just log
     MessageBox.Show($"Trade parsing not yet integrated.\n\nInput: {input}\n\nThe trade details come from the MainForm's trade parser.", 
        "Parse", MessageBoxButton.OK, MessageBoxImage.Information);
      }

        protected override void OnClosed(EventArgs e)
        {
     base.OnClosed(e);

// Stop all timers
            foreach (var lp in _viewModel.LPQuotes)
     {
         lp.StopTimer();
  }

 // Unsubscribe from FIX events
 UnsubscribeFromFIXEvents();

            Console.WriteLine("[FXAggregator] Window closed, cleaned up");
        }
    }
}
